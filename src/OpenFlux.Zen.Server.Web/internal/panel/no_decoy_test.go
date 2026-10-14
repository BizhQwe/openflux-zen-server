package panel

import (
	"strings"
	"testing"
)

func TestNoDecoyOrRedirectOutsideSecretPath(t *testing.T) {
	s, settings, password := testServer(t)
	prefix := "/" + settings.SecretPath + "/"
	for _, route := range []string{"/", "/index.html", "/anything", "/api/tunnels"} {
		response := request(t, s, "GET", route, nil, nil, "")
		if response.Code != 404 || response.Header().Get("Location") != "" || strings.Contains(response.Body.String(), settings.SecretPath) {
			t.Fatalf("unexpected anonymous response: %s %d", route, response.Code)
		}
	}
	login := request(t, s, "POST", prefix+"api/auth/login", map[string]string{"username": settings.Username, "password": password}, nil, "")
	cookie := login.Result().Cookies()[0]
	if response := request(t, s, "POST", prefix+"api/settings/decoy", map[string]string{"decoyRedirectUrl": "https://example.test"}, cookie, ""); response.Code != 404 {
		t.Fatal("removed decoy endpoint still exists")
	}
	if response := request(t, s, "GET", prefix+"api/settings", nil, cookie, ""); strings.Contains(response.Body.String(), "decoy") {
		t.Fatal("decoy settings exposed")
	}
}
