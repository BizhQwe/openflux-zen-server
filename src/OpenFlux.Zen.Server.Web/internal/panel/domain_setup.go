package panel

import (
	"context"
	"errors"
	"fmt"
	"net"
	"sync"
	"time"

	"openflux.local/zen/panel/configuration"
)

type domainSetupStatus struct {
	ID     string `json:"id,omitempty"`
	State  string `json:"state,omitempty"`
	Domain string `json:"domain,omitempty"`
	Port   string `json:"port,omitempty"`
	Error  string `json:"error,omitempty"`
}

type domainSetupRequest struct {
	status         domainSetupStatus
	previous, next Settings
}

type domainSetup struct {
	mu       sync.Mutex
	current  domainSetupRequest
	requests chan domainSetupRequest
	issue    func(context.Context, string, string) (string, string, error)
}

func (s *Server) domainSetupStatus() domainSetupStatus {
	s.domainSetup.mu.Lock()
	defer s.domainSetup.mu.Unlock()
	return s.domainSetup.current.status
}

func (s *Server) clearFinishedDomainSetup() {
	s.domainSetup.mu.Lock()
	defer s.domainSetup.mu.Unlock()
	if s.domainSetup.current.status.State != "issuing" {
		s.domainSetup.current = domainSetupRequest{}
	}
}

func (s *Server) startDomainSetup(previous, next Settings) error {
	s.domainSetup.mu.Lock()
	defer s.domainSetup.mu.Unlock()
	if state := s.domainSetup.current.status.State; state == "issuing" || state == "ready" {
		return errors.New("настройка HTTPS уже выполняется; дождитесь результата")
	}
	_, port, _ := net.SplitHostPort(next.Listen)
	job := domainSetupRequest{previous: previous, next: next, status: domainSetupStatus{ID: RandomSecret(18), State: "issuing", Domain: next.Domain, Port: port}}
	select {
	case s.domainSetup.requests <- job:
		s.domainSetup.current = job
		return nil
	default:
		return errors.New("настройка HTTPS уже выполняется")
	}
}

// Issuance does not change the listener. The browser commits the prepared
// result in a short POST, so even a port change receives its redirect response
// on the old connection instead of losing access during background polling.
func (s *Server) runDomainSetup(ctx context.Context) {
	for {
		select {
		case <-ctx.Done():
			return
		case job := <-s.domainSetup.requests:
			s.manager.SystemLog("info", "Настройка HTTPS: запрос сертификата домена %s", job.next.Domain)
			child, cancel := context.WithTimeout(ctx, 3*time.Minute)
			cert, key, err := s.domainSetup.issue(child, s.store.Dir, job.next.Domain)
			cancel()
			if err == nil {
				err = configuration.CheckDomainTLS(cert, key, job.next.Domain)
			}
			s.domainSetup.mu.Lock()
			if err != nil {
				s.domainSetup.current.status.State = "failed"
				s.domainSetup.current.status.Error = fmt.Sprintf("Не удалось включить HTTPS: %v. Проверьте DNS домена и доступность входящего TCP-порта 80. Прежние настройки сохранены.", err)
			} else {
				s.domainSetup.current.next.TLSCert, s.domainSetup.current.next.TLSKey = cert, key
				s.domainSetup.current.status.State = "ready"
			}
			s.domainSetup.mu.Unlock()
			if err != nil {
				s.manager.SystemLog("error", "Настройка HTTPS домена %s: %v", job.next.Domain, err)
			}
		}
	}
}

func (s *Server) completeDomainSetup(id string) error {
	s.domainSetup.mu.Lock()
	defer s.domainSetup.mu.Unlock()
	job := s.domainSetup.current
	if job.status.ID != id || job.status.State != "ready" {
		return errors.New("подготовленный сертификат не найден или ещё не готов")
	}
	if err := s.saveNetworkSettings(job.previous, job.next); err != nil {
		s.domainSetup.current.status.State = "failed"
		s.domainSetup.current.status.Error = err.Error()
		return err
	}
	s.domainSetup.current.status.State = "applied"
	return nil
}

func sameNetworkSettings(a, b Settings) bool {
	return a.Listen == b.Listen && a.LANAccessMode == b.LANAccessMode && a.LANSubnets == b.LANSubnets &&
		a.LocaltunnelEnabled == b.LocaltunnelEnabled && a.LocaltunnelHost == b.LocaltunnelHost &&
		a.DomainEnabled == b.DomainEnabled && a.Domain == b.Domain && a.TLSCert == b.TLSCert && a.TLSKey == b.TLSKey
}

func (s *Server) saveNetworkSettings(previous, next Settings) error {
	s.mu.Lock()
	defer s.mu.Unlock()
	if !sameNetworkSettings(previous, s.settings) {
		return errors.New("сетевые настройки изменились во время выпуска сертификата; примените размещение повторно")
	}
	merged := s.settings // preserve newer account, language and secret-path changes
	merged.Listen, merged.LANAccessMode, merged.LANSubnets = next.Listen, next.LANAccessMode, next.LANSubnets
	merged.LocaltunnelEnabled, merged.LocaltunnelHost = next.LocaltunnelEnabled, next.LocaltunnelHost
	merged.DomainEnabled, merged.Domain, merged.TLSCert, merged.TLSKey = next.DomainEnabled, next.Domain, next.TLSCert, next.TLSKey
	return s.saveSettingsLocked(merged)
}
