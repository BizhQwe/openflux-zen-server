package panel

import (
	"crypto/tls"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
)

type beforeBodyRead struct {
	io.Reader
	before func()
}

func (r *beforeBodyRead) Read(p []byte) (int, error) {
	if r.before != nil {
		before := r.before
		r.before = nil
		before()
	}
	return r.Reader.Read(p)
}

func TestLoginRejectsCredentialsRotatedDuringBodyRead(t *testing.T) {
	for _, changePath := range []bool{false, true} {
		t.Run(map[bool]string{false: "password", true: "access path"}[changePath], func(t *testing.T) {
			s, previous, password := testServer(t)
			oldToken := s.auth.issue()
			rotated := previous
			if changePath {
				rotated.SecretPath = RandomAccessPath()
			} else {
				rotated.PasswordSalt = RandomSecret(18)
				rotated.PasswordHash = HashPassword("new-dummy-password", rotated.PasswordSalt)
			}
			body := &beforeBodyRead{Reader: strings.NewReader(`{"username":"` + previous.Username + `","password":"` + password + `"}`), before: func() {
				if err := s.saveSettings(rotated); err != nil {
					t.Fatal(err)
				}
			}}
			r := httptest.NewRequest("POST", "http://panel.test/"+previous.SecretPath+"/api/auth/login", body)
			r.RemoteAddr = "127.0.0.1:9999"
			w := httptest.NewRecorder()
			s.ServeHTTP(w, r)
			if w.Code != http.StatusUnauthorized || len(w.Result().Cookies()) != 0 || s.auth.valid(oldToken) {
				t.Fatalf("old credentials survived rotation: status=%d", w.Code)
			}
			if !changePath {
				password = "new-dummy-password"
			}
			fresh := request(t, s, "POST", "/"+rotated.SecretPath+"/api/auth/login", map[string]string{"username": rotated.Username, "password": password}, nil, "")
			if fresh.Code != http.StatusOK {
				t.Fatal("current credentials rejected", fresh.Body.String())
			}
		})
	}
}

func TestStaleSettingsRequestsCannotRestoreRevokedCredentials(t *testing.T) {
	for _, route := range []string{"api/settings", "api/settings/language", "api/auth/change-profile"} {
		t.Run(route, func(t *testing.T) {
			s, previous, password := testServer(t)
			session := s.auth.issue()
			rotated := previous
			rotated.SecretPath = RandomAccessPath()
			rotated.PasswordSalt = RandomSecret(18)
			rotated.PasswordHash = HashPassword("new-dummy-password", rotated.PasswordSalt)
			body := &beforeBodyRead{Reader: strings.NewReader(`{"language":"ru","currentPassword":"` + password + `","newUsername":"stale-admin"}`), before: func() {
				if err := s.saveSettings(rotated); err != nil {
					t.Fatal(err)
				}
			}}
			r := httptest.NewRequest("POST", "http://panel.test/"+previous.SecretPath+"/"+route, body)
			r.RemoteAddr = "127.0.0.1:9999"
			r.AddCookie(&http.Cookie{Name: "zen_session", Value: session})
			w := httptest.NewRecorder()
			s.ServeHTTP(w, r)
			if w.Code != http.StatusBadRequest || s.Settings() != rotated || s.auth.valid(session) {
				t.Fatalf("stale request restored settings: status=%d", w.Code)
			}
		})
	}
}

func TestMutationOriginBindsSchemeAndHost(t *testing.T) {
	for _, tc := range []struct {
		name, origin, fetchSite string
		tls, proxy, want        bool
	}{
		{"native API", "", "", false, false, true},
		{"HTTP", "http://panel.test", "same-origin", false, false, true},
		{"HTTPS", "https://panel.test", "same-origin", true, false, true},
		{"HTTPS proxy", "https://panel.test", "same-origin", false, true, true},
		{"wrong scheme", "http://panel.test", "same-site", true, false, false},
		{"wrong host", "https://evil.test", "", true, false, false},
		{"wrong port", "https://panel.test:444", "", true, false, false},
		{"opaque", "null", "", false, false, false},
		{"userinfo", "http://user@panel.test", "", false, false, false},
		{"path", "http://panel.test/path", "", false, false, false},
		{"cross site without origin", "", "cross-site", false, false, false},
	} {
		t.Run(tc.name, func(t *testing.T) {
			r := httptest.NewRequest("POST", "http://panel.test/", nil)
			r.Header.Set("Origin", tc.origin)
			r.Header.Set("Sec-Fetch-Site", tc.fetchSite)
			if tc.tls {
				r.TLS = &tls.ConnectionState{}
			}
			settings := Settings{DomainEnabled: tc.proxy, Domain: "https://panel.test"}
			if got := sameOriginRequest(r, settings); got != tc.want {
				t.Fatalf("allowed=%v, want %v", got, tc.want)
			}
		})
	}
}
