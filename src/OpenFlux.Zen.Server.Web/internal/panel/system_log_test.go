package panel

import (
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestSystemLogsHaveEventsWithoutSecretsAndCanBeCleared(t *testing.T) {
	s, settings, password := testServer(t)
	prefix := "/" + settings.SecretPath + "/"
	login := request(t, s, "POST", prefix+"api/auth/login", map[string]string{"username": settings.Username, "password": password}, nil, "")
	if login.Code != 200 {
		t.Fatal(login.Body.String())
	}
	cookie := login.Result().Cookies()[0]
	profile := DefaultTunnel()
	profile.Name = "SystemLog profile"
	profile.Transport = "direct"
	profile.EncryptionKey = "do-not-log-this-encryption-secret"
	w := request(t, s, "POST", prefix+"api/tunnels", profile, cookie, "")
	if w.Code != 201 {
		t.Fatal(w.Body.String())
	}
	request(t, s, "POST", prefix+"api/auth/login", map[string]string{"username": settings.Username, "password": "do-not-log-this-password"}, nil, "")
	s.manager.mu.Lock()
	s.manager.diagnosticsLocked()
	s.manager.mu.Unlock()
	w = request(t, s, "GET", prefix+"api/logs/system", nil, cookie, "")
	var logs []string
	if err := json.Unmarshal(w.Body.Bytes(), &logs); err != nil {
		t.Fatal(err)
	}
	joined := strings.Join(logs, "\n")
	for _, expected := range []string{"Инициализация", "HTTP-сервер", "SystemLog profile", "HTTP POST api/tunnels", "[WARN]", "[DEBUG]"} {
		if !strings.Contains(joined, expected) {
			t.Fatalf("missing system event %q: %s", expected, joined)
		}
	}
	for _, secret := range []string{settings.SecretPath, password, profile.EncryptionKey, "do-not-log-this-password", cookie.Value} {
		if strings.Contains(joined, secret) {
			t.Fatalf("system logs exposed a secret")
		}
	}
	w = request(t, s, "DELETE", prefix+"api/logs/system", nil, cookie, "")
	if w.Code != 200 {
		t.Fatal(w.Body.String())
	}
	w = request(t, s, "GET", prefix+"api/logs/system", nil, cookie, "")
	if strings.Contains(w.Body.String(), "SystemLog profile") || !strings.Contains(w.Body.String(), "HTTP DELETE api/logs/system") {
		t.Fatal("system clear did not remove old history or audit the clear")
	}
}

func TestLogHistoryAndLevelsSurviveRestartIncludingLegacyLines(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "system.log")
	if err := os.WriteFile(path, []byte("2026-10-14T10:00:00Z old-format-message\n"), 0600); err != nil {
		t.Fatal(err)
	}
	logs, err := newLogs(dir)
	if err != nil {
		t.Fatal(err)
	}
	logs.AppendLevel("system", "warn", "new-warning")
	before := logs.Tail("system", 10)
	if len(before) != 2 || before[0].Timestamp.IsZero() || before[0].Message != "old-format-message" {
		t.Fatal("new startup entries hid old history", before)
	}
	reloaded, err := newLogs(dir)
	if err != nil {
		t.Fatal(err)
	}
	after := reloaded.Tail("system", 10)
	if len(after) != 2 || after[1].Level != "warn" || !after[1].Timestamp.Equal(before[1].Timestamp) || after[1].Message != "new-warning" {
		t.Fatal("persisted time, level or history lost", after)
	}
}
