package panel

import (
	"crypto/tls"
	"encoding/json"
	"errors"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
)

func TestPlacementAppliesListenerAndDoesNotFalselySaveBusyPort(t *testing.T) {
	s, settings, password := testServer(t)
	prefix := "/" + settings.SecretPath + "/"
	login := request(t, s, "POST", prefix+"api/auth/login", map[string]string{"username": settings.Username, "password": password}, nil, "")
	cookie := login.Result().Cookies()[0]
	applied := 0
	s.SetNetworkApply(func(next Settings) (func(), error) {
		if strings.HasSuffix(next.Listen, ":5555") {
			return nil, errors.New("occupied port")
		}
		applied++
		return func() { applied-- }, nil
	})
	value := map[string]any{"lanEnabled": false, "domainEnabled": true, "domain": "panel.example.test", "panelPort": 5443}
	w := request(t, s, "POST", prefix+"api/settings/network-placement", value, cookie, "")
	if w.Code != 200 {
		t.Fatal(w.Body.String())
	}
	var data map[string]any
	if err := json.Unmarshal(w.Body.Bytes(), &data); err != nil {
		t.Fatal(err)
	}
	if data["domainUrl"] != "http://panel.example.test:5443/"+settings.SecretPath+"/" || data["lanEnabled"] != false || applied != 1 {
		t.Fatalf("placement: %s", w.Body.String())
	}
	before := s.Settings()
	value["panelPort"] = 5555
	w = request(t, s, "POST", prefix+"api/settings/network-placement", value, cookie, "")
	if w.Code != 400 || s.Settings() != before {
		t.Fatal("busy port replaced working settings")
	}
	var stored Settings
	if err := s.store.Read("settings.json", &stored); err != nil {
		t.Fatal(err)
	}
	if stored != before {
		t.Fatal("busy port was saved to disk")
	}
	// A full proxy URL can use only loopback for the backend. It does not
	// switch the backend to port 443 or attach TLS to its HTTP listener.
	value["domain"] = "https://proxy.example.test"
	value["panelPort"] = 5443
	w = request(t, s, "POST", prefix+"api/settings/network-placement", value, cookie, "")
	if w.Code != 200 {
		t.Fatal(w.Body.String())
	}
	if next := s.Settings(); next.Listen != "127.0.0.1:5443" || next.TLSCert != "" {
		t.Fatalf("proxy changed listener unexpectedly: %s", next.Listen)
	}
	if !strings.Contains(w.Body.String(), "https://proxy.example.test/") {
		t.Fatal(w.Body.String())
	}
}

func TestHTTPIPAccessHTTPSDomainAndPlacementLinks(t *testing.T) {
	s, settings, _ := testServer(t)
	// Router metadata only; TLS handshakes are tested by hosting separately.
	s.settings.TLSCert, s.settings.TLSKey = "configured-cert", "configured-key"
	s.settings.Domain, s.settings.DomainEnabled = "panel.test", true
	prefix := "/" + settings.SecretPath + "/"
	w := request(t, s, "GET", prefix+"?test=1", nil, nil, "")
	if w.Code != 307 || w.Header().Get("Location") != "https://panel.test:5000"+prefix+"?test=1" {
		t.Fatal(w.Code, w.Header().Get("Location"))
	}
	for _, host := range []string{"127.0.0.1:5000", "localhost:5000", "192.168.1.20:5000", "203.0.113.20:5000"} {
		r := httptest.NewRequest("GET", "http://"+host+prefix, nil)
		r.RemoteAddr = "127.0.0.1:9999"
		w = httptest.NewRecorder()
		s.ServeHTTP(w, r)
		if w.Code != 200 || w.Header().Get("Location") != "" {
			t.Fatalf("IP/localhost upgraded to TLS: %s: %d", host, w.Code)
		}
	}
	r := httptest.NewRequest("GET", "https://panel.test:5000"+prefix+"api/settings/network-placement", nil)
	r.TLS = &tls.ConnectionState{Version: tls.VersionTLS13, ServerName: "panel.test"}
	r.AddCookie(&http.Cookie{Name: "zen_session", Value: s.auth.issue()})
	w = httptest.NewRecorder()
	s.ServeHTTP(w, r)
	if w.Code != 200 {
		t.Fatal(w.Body.String())
	}
	var data map[string]any
	if err := json.Unmarshal(w.Body.Bytes(), &data); err != nil {
		t.Fatal(err)
	}
	lan, _ := data["lanUrl"].(string)
	if !strings.HasPrefix(data["localhostUrl"].(string), "http://") || (lan != "" && !strings.HasPrefix(lan, "http://")) || !strings.HasPrefix(data["domainUrl"].(string), "https://") {
		t.Fatal(data)
	}
	// A proxy's plaintext backend must not redirect the external HTTPS URL
	// back to itself, which would create a redirect loop.
	s.settings.Domain = "https://panel.test"
	w = request(t, s, "GET", prefix, nil, nil, "")
	if w.Code != 200 {
		t.Fatal("reverse proxy backend redirected", w.Code)
	}
}

func TestConfiguredHTTPSProxyLoginSetsSecureCookie(t *testing.T) {
	s, settings, password := testServer(t)
	settings.Domain = "https://panel.test"
	settings.DomainEnabled = true
	if err := s.saveSettings(settings); err != nil {
		t.Fatal(err)
	}
	w := request(t, s, "POST", "/"+settings.SecretPath+"/api/auth/login", map[string]string{"username": settings.Username, "password": password}, nil, "https://panel.test")
	if w.Code != 200 {
		t.Fatal(w.Body.String())
	}
	if cookies := w.Result().Cookies(); len(cookies) != 1 || !cookies[0].Secure {
		t.Fatal("HTTPS proxy login cookie is not secure")
	}
}
