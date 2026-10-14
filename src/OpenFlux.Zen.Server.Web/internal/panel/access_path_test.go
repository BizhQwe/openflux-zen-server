package panel

import (
	"encoding/json"
	"regexp"
	"testing"
)

func TestRegenerateAccessPathUsesAlphanumeric18(t *testing.T) {
	s, settings, password := testServer(t)
	prefix := "/" + settings.SecretPath + "/"
	login := request(t, s, "POST", prefix+"api/auth/login", map[string]string{"username": settings.Username, "password": password}, nil, "")
	if login.Code != 200 {
		t.Fatal(login.Body.String())
	}
	cookie := login.Result().Cookies()[0]
	response := request(t, s, "POST", prefix+"api/settings/regenerate-secret", nil, cookie, "")
	var result map[string]string
	if response.Code != 200 || json.Unmarshal(response.Body.Bytes(), &result) != nil {
		t.Fatal(response.Code, response.Body.String())
	}
	path := result["secretPath"]
	if !regexp.MustCompile(`^[a-zA-Z0-9]{18}$`).MatchString(path) || path == settings.SecretPath {
		t.Fatalf("regenerated path: %q", path)
	}
	stored, _, err := s.store.LoadSettings()
	if err != nil || stored.SecretPath != path {
		t.Fatal("regenerated path not persisted", err)
	}
	if old := request(t, s, "GET", prefix, nil, nil, ""); old.Code != 404 {
		t.Fatal("previous path still serves the panel", old.Code)
	}
	if current := request(t, s, "GET", "/"+path+"/", nil, nil, ""); current.Code != 200 {
		t.Fatal("new path is not accessible", current.Code)
	}
	if oldSession := request(t, s, "GET", "/"+path+"/api/tunnels", nil, cookie, ""); oldSession.Code != 401 {
		t.Fatal("old session remained valid", oldSession.Code)
	}
}
