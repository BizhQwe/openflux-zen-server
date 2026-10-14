package panel

import (
	"errors"
	"strings"
	"testing"
)

func TestServerOnlyAPI(t *testing.T) {
	s, settings, password := testServer(t)
	prefix := "/" + settings.SecretPath + "/"
	login := request(t, s, "POST", prefix+"api/auth/login", map[string]string{"username": settings.Username, "password": password}, nil, "")
	cookie := login.Result().Cookies()[0]
	server := DefaultTunnel()
	server.Name, server.Transport, server.EncryptionKey = "Original server", "direct", "integration-secret-123456"
	server, err := s.manager.Upsert(server, true)
	if err != nil {
		t.Fatal(err)
	}
	for _, role := range []string{"client", "bench-send", "bench-sink", "", "EXIT"} {
		t.Run(role, func(t *testing.T) {
			invalid := server
			invalid.Role, invalid.Name = role, "Rejected role"
			for _, action := range []struct {
				method, route string
				body          any
			}{
				{"POST", "api/tunnels", invalid},
				{"PUT", "api/tunnels/" + server.ID, invalid},
				{"POST", "api/config/import", map[string]any{"tunnels": []Tunnel{invalid}}},
			} {
				response := request(t, s, action.method, prefix+action.route, action.body, cookie, "")
				if response.Code != 400 || !strings.Contains(response.Body.String(), "role=exit") {
					t.Fatalf("non-server role accepted by %s: %d %s", action.route, response.Code, response.Body.String())
				}
			}
			list := s.manager.List()
			if len(list) != 1 || list[0].Name != server.Name || list[0].Role != ServerRole {
				t.Fatal("rejected request changed existing tunnels")
			}
		})
	}
	for _, extra := range []string{"--role=client", "--role client", "-role=client", "-role client", "-r=client", "-r client", `"--role=client"`, "--client", "-client", "--bench-send=1", "-bench-send 1", "--bench-sink", "-bench-sink", "--exit-node"} {
		invalid := server
		invalid.ExtraArgs = extra
		if _, err := BuildArgs(invalid, t.TempDir(), "unused.sock"); !errors.Is(err, ErrServerOnly) {
			t.Fatalf("role override was not rejected: %s (%v)", extra, err)
		}
		for _, action := range []struct {
			method, route string
			body          any
		}{
			{"POST", "api/tunnels", invalid},
			{"PUT", "api/tunnels/" + server.ID, invalid},
			{"POST", "api/config/import", []Tunnel{invalid}},
		} {
			if response := request(t, s, action.method, prefix+action.route, action.body, cookie, ""); response.Code != 400 {
				t.Fatalf("API accepted role override %s: %d", extra, response.Code)
			}
		}
	}
	args, err := BuildArgs(server, t.TempDir(), "unused.sock")
	if err != nil {
		t.Fatal(err)
	}
	if len(args) == 0 || args[0] != "--role=exit" {
		t.Fatal("panel launch is not fixed to the server role")
	}
}

func TestOldClientProfilesStayDisabled(t *testing.T) {
	store, err := NewStore(t.TempDir())
	if err != nil {
		t.Fatal(err)
	}
	client := DefaultTunnel()
	client.ID, client.Role, client.IsEnabled = newID(), "client", true
	client.EncryptionKey = "preserved-client-key-123456"
	override := client
	override.ID, override.Role, override.ExtraArgs = newID(), ServerRole, "-r client"
	server := DefaultTunnel()
	server.ID, server.Role = newID(), "" // Old server exports can omit the role.
	server.EncryptionKey = "integration-secret-123456"
	if err = store.Write("tunnels.json", []Tunnel{client, override, server}); err != nil {
		t.Fatal(err)
	}
	m, err := NewManager(store, "missing-core")
	if err != nil {
		t.Fatal(err)
	}
	defer m.Close()
	if err = m.Restore(); err != nil {
		t.Fatal(err)
	}
	for _, id := range []string{client.ID, override.ID} {
		profile, _ := m.Get(id)
		if profile.IsEnabled || profile.Status != Failed || profile.ErrorMessage == "" || profile.EncryptionKey != client.EncryptionKey {
			t.Fatal("old client profile was activated or its configuration was lost")
		}
		if err = m.Start(id); !errors.Is(err, ErrServerOnly) {
			t.Fatalf("old client can be launched: %v", err)
		}
	}
	if len(m.processes) != 0 {
		t.Fatal("the panel started a non-server process")
	}
	profile, _ := m.Get(server.ID)
	if profile.Role != ServerRole {
		t.Fatal("missing legacy role should default to exit")
	}
}
