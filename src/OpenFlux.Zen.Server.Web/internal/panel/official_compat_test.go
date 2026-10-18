package panel

import (
	"encoding/json"
	"os"
	"os/exec"
	"reflect"
	"strings"
	"testing"

	"github.com/p1neappleXpress/OpenFlux/share"
	"github.com/p1neappleXpress/OpenFlux/transport"
)

func TestMultiStrictNegotiationMatchesSetting(t *testing.T) {
	for _, tc := range []struct {
		name, carriers string
		strict, want   bool
	}{
		{"compatible multi", "avito:100,cupsonline:50", false, false},
		{"strict multi", "avito:100,cupsonline:50", true, true},
		{"session-only direct", "direct:100,avito:50", false, true},
	} {
		t.Run(tc.name, func(t *testing.T) {
			profile := DefaultTunnel()
			profile.ID = newID()
			profile.Transport, profile.Transports = "multi", tc.carriers
			profile.EncryptionKey = "official-client-compatibility-secret"
			profile.Negotiate = tc.strict
			args, err := BuildArgs(profile, t.TempDir(), "test.sock")
			if err != nil {
				t.Fatal(err)
			}
			strict := false
			for _, arg := range args {
				strict = strict || arg == "--negotiate"
			}
			if strict != tc.want {
				t.Fatalf("strict negotiation = %v, want %v", strict, tc.want)
			}
		})
	}
}

// The optional binary must be built from pristine upstream sources. Using a
// second process catches format extensions a round trip through our own reader
// would accept even though the published applications cannot read them.
func TestLinksForOfficialCore(t *testing.T) {
	original := os.Getenv("OPENFLUX_TEST_CLIENT_CORE")
	if original == "" {
		t.Skip("set OPENFLUX_TEST_CLIENT_CORE to a pristine upstream core")
	}
	urls := map[string]string{
		"yandex": "https://disk.yandex.ru/i/test", "vyandex": "https://disk.yandex.ru/i/test",
		"boards": "https://boards.yandex.ru/p/test", "mailru": "https://cloud.mail.ru/public/test",
		"telemost":   "https://telemost.yandex.ru/j/test",
		"cupsonline": transport.PackRooms([]string{"room-one", "room-two"}, false),
		"avito":      transport.PackRooms([]string{"room-one", "room-two"}, true),
		"direct":     "",
	}
	for kind, url := range urls {
		t.Run(kind, func(t *testing.T) {
			cfg := share.Config{Secret: "official-client-compatibility-secret", Context: "shared-test-context", Transports: []share.Transport{{Type: kind, URL: url, Priority: 100}}}
			if kind == "direct" {
				cfg.Negotiate, cfg.Transports[0].Dial = true, "127.0.0.1:8445"
			}
			link, err := share.Encode(cfg)
			if err != nil {
				t.Fatal(err)
			}
			assertOfficialLink(t, original, link)
			input, err := json.Marshal(cfg)
			if err != nil {
				t.Fatal(err)
			}
			cmd := exec.Command(original, "--make-link", "-")
			cmd.Stdin = strings.NewReader(string(input))
			output, err := cmd.Output()
			if err != nil {
				t.Fatalf("official link writer: %v", err)
			}
			var upstream share.Result
			if err := json.Unmarshal(output, &upstream); err != nil || upstream.Error != "" {
				t.Fatalf("official link writer failed: %s, %v", output, err)
			}
			ours := share.Read(upstream.Link)
			if ours.Error != "" || !reflect.DeepEqual(ours.Config, upstream.Config) {
				t.Fatalf("official link changed on import: %s", ours.Error)
			}
		})
	}
	t.Run("multi", func(t *testing.T) {
		cfg := share.Config{Negotiate: true, Secret: "official-client-compatibility-secret", Context: "shared-test-context", Transports: []share.Transport{
			{Type: "avito", URL: urls["avito"], Priority: 100},
			{Type: "cupsonline", URL: urls["cupsonline"], Priority: 50},
		}}
		link, err := share.Encode(cfg)
		if err != nil {
			t.Fatal(err)
		}
		assertOfficialLink(t, original, link)
	})
}

func assertOfficialLink(t *testing.T, original, link string) {
	t.Helper()
	cmd := exec.Command(original, "--parse-link", "-")
	cmd.Stdin = strings.NewReader(link)
	output, err := cmd.Output()
	if err != nil {
		t.Fatalf("official client rejected our link: %s, %v", output, err)
	}
	var upstream share.Result
	if err := json.Unmarshal(output, &upstream); err != nil || upstream.Error != "" {
		t.Fatalf("official link reader failed: %s, %v", output, err)
	}
	ours := share.Read(link)
	if ours.Error != "" || !reflect.DeepEqual(ours.Config, upstream.Config) {
		t.Fatal("official reader changed transport, encryption context or secret")
	}
}
