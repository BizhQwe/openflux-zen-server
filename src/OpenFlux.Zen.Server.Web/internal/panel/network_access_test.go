package panel

import (
	"crypto/tls"
	"net/http"
	"net/http/httptest"
	"testing"
)

func TestNetworkAccessUsesSocketPeerAndConfiguredSubnets(t *testing.T) {
	s, settings, _ := testServer(t)
	s.settings.Listen = "0.0.0.0:5000"
	s.settings.LANAccessMode = "on"
	s.settings.LANSubnets = "192.168.31.0/24, fd42::/48"
	s.settings.Domain = "panel.test"
	s.settings.DomainEnabled = true
	s.settings.TLSCert, s.settings.TLSKey = "configured-cert", "configured-key"
	prefix := "/" + settings.SecretPath + "/"
	for _, test := range []struct {
		name, peer, host, sni string
		tls, lan, domain      bool
		want                  int
	}{
		{"localhost HTTP", "127.0.0.1:9999", "127.0.0.1:5000", "", false, false, true, 200},
		{"IPv6 localhost HTTP", "[::1]:9999", "localhost:5000", "", false, false, true, 200},
		{"selected LAN HTTP", "192.168.31.42:9999", "192.168.31.11:5000", "", false, true, true, 200},
		{"mapped LAN HTTP", "[::ffff:192.168.31.42]:9999", "192.168.31.11:5000", "", false, true, true, 200},
		{"selected IPv6 LAN", "[fd42::123]:9999", "[fd42::1]:5000", "", false, true, true, 200},
		{"unselected private subnet", "192.168.120.42:9999", "192.168.31.11:5000", "", false, true, true, 403},
		{"LAN toggle off", "192.168.31.42:9999", "192.168.31.11:5000", "", false, false, true, 403},
		{"public HTTP IP", "198.51.100.7:9999", "203.0.113.20:5000", "", false, true, true, 403},
		{"public HTTP domain", "198.51.100.7:9999", "panel.test:5000", "", false, true, true, 403},
		{"public fake localhost Host", "198.51.100.7:9999", "localhost:5000", "", false, true, true, 403},
		{"public IPv6 HTTP", "[2001:db8::42]:9999", "panel.test:5000", "", false, true, true, 403},
		{"public HTTPS domain", "198.51.100.7:9999", "panel.test:5000", "panel.test", true, false, true, 200},
		{"public HTTPS IP", "198.51.100.7:9999", "203.0.113.20:5000", "panel.test", true, true, true, 403},
		{"public wrong TLS name", "198.51.100.7:9999", "panel.test:5000", "other.test", true, true, true, 403},
		{"public missing TLS name", "198.51.100.7:9999", "panel.test:5000", "", true, true, true, 403},
		{"domain disabled", "198.51.100.7:9999", "panel.test:5000", "panel.test", true, true, false, 403},
		{"invalid peer", "invalid", "localhost:5000", "", false, true, true, 403},
	} {
		t.Run(test.name, func(t *testing.T) {
			s.settings.LANAccessMode = "off"
			if test.lan {
				s.settings.LANAccessMode = "on"
			}
			s.settings.DomainEnabled = test.domain
			r := httptest.NewRequest("GET", "http://"+test.host+prefix, nil)
			r.RemoteAddr = test.peer
			// None of these client-controlled headers may grant local access.
			r.Header.Set("X-Forwarded-For", "127.0.0.1, 192.168.31.42")
			r.Header.Set("X-Real-IP", "192.168.31.42")
			r.Header.Set("X-Forwarded-Proto", "https")
			r.Header.Set("Forwarded", "for=127.0.0.1;proto=https")
			if test.tls {
				r.TLS = &tls.ConnectionState{Version: tls.VersionTLS13, ServerName: test.sni}
			}
			w := httptest.NewRecorder()
			s.ServeHTTP(w, r)
			if w.Code != test.want {
				t.Fatalf("status=%d, want=%d", w.Code, test.want)
			}
			if test.want == 403 && w.Header().Get("Location") != "" {
				t.Fatal("denied request redirected")
			}
		})
	}
	// An existing authenticated session does not bypass the network policy.
	r := httptest.NewRequest("GET", "http://panel.test:5000"+prefix+"api/settings", nil)
	r.RemoteAddr = "198.51.100.7:9999"
	r.AddCookie(&http.Cookie{Name: "zen_session", Value: s.auth.issue()})
	w := httptest.NewRecorder()
	s.ServeHTTP(w, r)
	if w.Code != 403 {
		t.Fatal("session bypassed network policy")
	}
}
