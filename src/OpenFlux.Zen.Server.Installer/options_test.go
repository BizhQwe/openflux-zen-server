package main

import (
	"bufio"
	"bytes"
	"strings"
	"testing"

	"openflux.local/zen/installer/internal/terminal"
	"openflux.local/zen/panel/configuration"
)

func wizardSettings() configuration.Settings {
	return configuration.Settings{Username: "admin", SecretPath: "zen-test-access", Listen: "0.0.0.0:5000", Language: "en", AutoStartEnabled: true}
}

func TestLegacyWizardChangesActualSettings(t *testing.T) {
	var output bytes.Buffer
	u := &terminal.UI{In: bufio.NewReader(strings.NewReader("2\n1\nn\n")), Out: &output, Interactive: true}
	opts := installOptions{Autostart: true}
	settings := wizardSettings()
	if e := opts.wizard(u, settings, true); e != nil {
		t.Fatal(e)
	}
	if e := opts.apply(&settings); e != nil {
		t.Fatal(e)
	}
	if settings.Language != "ru" || settings.Listen != "127.0.0.1:5000" || opts.Autostart {
		t.Fatalf("wizard selections were ignored: %#v %#v", settings, opts)
	}
	for _, prompt := range []string{"Language / Язык", "Сетевое размещение", "автозапуск службы"} {
		if !strings.Contains(output.String(), prompt) {
			t.Fatalf("missing legacy prompt: %s", prompt)
		}
	}
}
func TestNoninteractiveUpdatePreservesSettings(t *testing.T) {
	settings := wizardSettings()
	settings.LocaltunnelEnabled = true
	settings.Domain = "https://panel.example.test"
	settings.Language = "ru"
	before := settings
	var output bytes.Buffer
	u := &terminal.UI{In: bufio.NewReader(strings.NewReader("")), Out: &output}
	opts := installOptions{Autostart: true}
	if e := opts.wizard(u, settings, false); e != nil {
		t.Fatal(e)
	}
	if e := opts.apply(&settings); e != nil {
		t.Fatal(e)
	}
	if settings != before {
		t.Fatal("unattended update replaced existing settings")
	}
	if output.Len() != 0 {
		t.Fatal("redirected input received interactive questions")
	}
}
func TestInvalidWizardConfigurationAndPasswordOutput(t *testing.T) {
	for _, opts := range []installOptions{{ModeSet: true, Mode: "invalid"}, {Port: -1}, {DomainSet: true, Domain: "https://panel.test/wrong/path"}} {
		s := wizardSettings()
		if e := opts.apply(&s); e == nil {
			t.Fatal("invalid installation settings accepted")
		}
	}
	if bootstrapPassword("Password: stored as a hash; use --reset-password\n") != "" {
		t.Fatal("a hash notice became a password")
	}
	if bootstrapPassword("Username: admin\nPassword: fresh-password\n") != "fresh-password" {
		t.Fatal("new password lost")
	}
}
