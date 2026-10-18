package panel

import (
	"errors"
	"net/http"
	"net/url"
	"strings"

	"openflux.local/zen/panel/configuration"
)

// A request may spend time reading its body while another request changes the
// account. Never let that older request restore credentials or a revoked path.
func (s *Server) saveAuthenticatedSettings(r *http.Request, previous, next Settings, displayPassword ...string) error {
	s.mu.Lock()
	defer s.mu.Unlock()
	if previous != s.settings || !s.auth.valid(token(r)) {
		return errors.New("настройки или сессия изменились; обновите страницу и повторите вход")
	}
	return s.saveSettingsLocked(next, displayPassword...)
}

// Login uses the current credentials, not the snapshot taken before reading
// the HTTP body. Saving credentials and issuing sessions share this lock.
func (s *Server) login(username, password, accessPath string) (Settings, string, bool) {
	s.mu.RLock()
	defer s.mu.RUnlock()
	settings := s.settings
	if accessPath != settings.SecretPath || username != settings.Username || !CheckPassword(password, settings) {
		return settings, "", false
	}
	return settings, s.auth.issue(), true
}

func sameOriginRequest(r *http.Request, settings Settings) bool {
	if strings.EqualFold(r.Header.Get("Sec-Fetch-Site"), "cross-site") {
		return false
	}
	origin := r.Header.Get("Origin")
	if origin == "" {
		// Existing non-browser API clients need no new header or token format.
		return true
	}
	u, err := url.Parse(origin)
	if err != nil || u.User != nil || u.Host == "" || u.Path != "" || u.RawQuery != "" || u.Fragment != "" || !strings.EqualFold(u.Host, r.Host) {
		return false
	}
	scheme := "http"
	if r.TLS != nil {
		scheme = "https"
	} else if settings.DomainEnabled {
		// A configured HTTPS reverse proxy may terminate TLS on loopback.
		// Forwarded headers from arbitrary callers are never trusted.
		if address, err := configuration.DomainURL(settings); err == nil {
			if external, err := url.Parse(address); err == nil && external.Scheme == "https" && strings.EqualFold(external.Host, r.Host) {
				scheme = "https"
			}
		}
	}
	return u.Scheme == scheme
}
