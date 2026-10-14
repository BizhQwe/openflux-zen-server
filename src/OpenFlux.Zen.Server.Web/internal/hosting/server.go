// Package hosting owns only the panel's listener. It never stops other
// services or changes shared web-server, firewall or reverse-proxy settings.
package hosting

import (
	"context"
	"crypto/tls"
	"errors"
	"fmt"
	"net"
	"net/http"
	"sync"
	"time"

	"openflux.local/zen/panel/configuration"
)

type Server struct {
	mu       sync.Mutex
	handler  http.Handler
	settings configuration.Settings
	listener net.Listener
	current  *http.Server
	servers  []*http.Server
	closed   bool
	log      func(string, string, ...any)
}

func New(handler http.Handler, log func(string, string, ...any)) *Server {
	return &Server{handler: handler, log: log}
}

func (s *Server) start(next configuration.Settings) error {
	var certificate tls.Certificate
	var err error
	if next.TLSCert != "" {
		certificate, err = tls.LoadX509KeyPair(next.TLSCert, next.TLSKey)
		if err != nil {
			return fmt.Errorf("сертификат TLS: %w", err)
		}
	}
	listener, err := net.Listen("tcp", next.Listen)
	if err != nil {
		return fmt.Errorf("порт панели %s недоступен; выберите свободный порт, другие службы не изменялись: %w", next.Listen, err)
	}
	server := &http.Server{Handler: s.handler, ReadHeaderTimeout: 10 * time.Second, ReadTimeout: 20 * time.Second, WriteTimeout: 20 * time.Second, IdleTimeout: 60 * time.Second, MaxHeaderBytes: 32 << 10}
	if next.TLSCert != "" {
		server.TLSConfig = &tls.Config{MinVersion: tls.VersionTLS12, NextProtos: []string{"http/1.1"}, GetCertificate: func(*tls.ClientHelloInfo) (*tls.Certificate, error) {
			// Renewed files are picked up without restarting tunnels. A failed
			// renewal leaves the previously loaded certificate available.
			if fresh, e := tls.LoadX509KeyPair(next.TLSCert, next.TLSKey); e == nil {
				return &fresh, nil
			}
			return &certificate, nil
		}}
		listener = newProtocolListener(listener, server.TLSConfig)
	}
	s.listener, s.current, s.settings = listener, server, next
	s.servers = append(s.servers, server)
	go func() {
		err := server.Serve(listener)
		if err != nil && !errors.Is(err, http.ErrServerClosed) && !errors.Is(err, net.ErrClosed) && s.log != nil {
			s.log("error", "HTTP-сервер панели: %v", err)
		}
	}()
	if s.log != nil {
		s.log("info", "HTTP-сервер слушает %s, HTTPS на том же порту=%t", next.Listen, next.TLSCert != "")
	}
	return nil
}

func (s *Server) Apply(next configuration.Settings) (func(), error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.closed {
		return nil, errors.New("HTTP-сервер остановлен")
	}
	previous, oldServer, oldListener := s.settings, s.current, s.listener
	if oldListener != nil {
		if next.Listen != previous.Listen {
			// Try a different port before releasing the current one. Changing
			// loopback/public binding on the same port requires releasing it.
			_, oldPort, _ := net.SplitHostPort(previous.Listen)
			_, newPort, _ := net.SplitHostPort(next.Listen)
			if oldPort != newPort {
				if err := s.start(next); err != nil {
					return nil, err
				}
				_ = oldListener.Close()
			} else {
				_ = oldListener.Close()
				if err := s.start(next); err != nil {
					s.restore(previous)
					s.retire(oldServer)
					return nil, err
				}
			}
		} else {
			_ = oldListener.Close()
			if err := s.start(next); err != nil {
				s.restore(previous)
				s.retire(oldServer)
				return nil, err
			}
		}
		s.retire(oldServer)
	} else if err := s.start(next); err != nil {
		return nil, err
	}
	return func() {
		s.mu.Lock()
		defer s.mu.Unlock()
		if s.closed {
			return
		}
		_ = s.listener.Close()
		s.retire(s.current)
		s.restore(previous)
	}, nil
}

func (s *Server) restore(previous configuration.Settings) {
	if previous.Listen != "" {
		if err := s.start(previous); err != nil && s.log != nil {
			s.log("error", "Не удалось восстановить HTTP-адрес: %v", err)
		}
	}
}
func (s *Server) retire(server *http.Server) {
	go func() {
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		_ = server.Shutdown(ctx)
		s.mu.Lock()
		for i, candidate := range s.servers {
			if candidate == server {
				s.servers = append(s.servers[:i], s.servers[i+1:]...)
				break
			}
		}
		s.mu.Unlock()
	}()
}
func (s *Server) Close(ctx context.Context) error {
	s.mu.Lock()
	s.closed = true
	servers := append([]*http.Server(nil), s.servers...)
	s.mu.Unlock()
	var result error
	for _, server := range servers {
		result = errors.Join(result, server.Shutdown(ctx))
	}
	return result
}
