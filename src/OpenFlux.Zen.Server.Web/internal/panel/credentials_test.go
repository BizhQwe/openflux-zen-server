package panel

import (
	"strings"
	"testing"

	"openflux.local/zen/panel/configuration"
)

func TestProfileChangeUpdatesLocalPasswordWithoutExposingItInAPI(t *testing.T) {
	s, settings, password := testServer(t)
	prefix := "/" + settings.SecretPath + "/"
	login := request(t, s, "POST", prefix+"api/auth/login", map[string]string{"username": settings.Username, "password": password}, nil, "")
	cookie := login.Result().Cookies()[0]
	nextPassword := "updated-test-password-123"
	changed := request(t, s, "POST", prefix+"api/auth/change-profile", map[string]string{"currentPassword": password, "newPassword": nextPassword}, cookie, "")
	if changed.Code != 200 {
		t.Fatal(changed.Body.String())
	}
	if display, e := configuration.ReadDisplayPassword(s.store.Dir, s.Settings()); e != nil || display != nextPassword {
		t.Fatal("web password change did not update local credentials")
	}
	login = request(t, s, "POST", prefix+"api/auth/login", map[string]string{"username": settings.Username, "password": nextPassword}, nil, "")
	cookie = login.Result().Cookies()[0]
	for _, endpoint := range []string{"api/settings", "api/config/export"} {
		response := request(t, s, "GET", prefix+endpoint, nil, cookie, "")
		if strings.Contains(response.Body.String(), nextPassword) {
			t.Fatalf("password exposed by %s", endpoint)
		}
	}
	if response := request(t, s, "GET", prefix+".credentials", nil, cookie, ""); response.Code != 404 {
		t.Fatal("local credentials accessible over HTTP")
	}
}
