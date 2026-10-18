package commands

import (
	"bytes"
	"encoding/json"
	"errors"
	"os"
	"path/filepath"
	"reflect"
	"strings"
	"testing"

	"openflux.local/zen/cli/internal/platform"
	"openflux.local/zen/panel/configuration"
)

type fakeService struct {
	status  platform.Status
	calls   []string
	stopErr error
}

func (f *fakeService) Status(string) (platform.Status, error) { return f.status, nil }
func (f *fakeService) Start(string) error {
	f.calls = append(f.calls, "start")
	f.status.Running = true
	return nil
}
func (f *fakeService) Stop(string) error {
	f.calls = append(f.calls, "stop")
	if f.stopErr != nil {
		return f.stopErr
	}
	f.status.Running = false
	return nil
}
func (f *fakeService) Enable(_ string, on bool) error {
	f.calls = append(f.calls, "enable")
	f.status.Enabled = on
	return nil
}
func (f *fakeService) Remove(string) error {
	f.calls = append(f.calls, "remove")
	f.status.Registered = false
	return nil
}
func fixture(t *testing.T) (*App, string, configuration.Settings, string) {
	t.Helper()
	dir := t.TempDir()
	store, e := configuration.NewStore(filepath.Join(dir, "data"))
	if e != nil {
		t.Fatal(e)
	}
	s, password, e := store.LoadSettings()
	if e != nil {
		t.Fatal(e)
	}
	if e = configuration.AtomicJSON(filepath.Join(dir, configuration.MarkerName), configuration.Installation{Product: configuration.Product, Version: Version, Dir: dir}); e != nil {
		t.Fatal(e)
	}
	app := New()
	app.Out = &bytes.Buffer{}
	app.In = bytes.NewBufferString("no\n")
	app.IsAdmin = func() bool { return true }
	app.Service = &fakeService{}
	return app, dir, s, password
}
func TestCredentialsReadOnly(t *testing.T) {
	a, dir, initial, password := fixture(t)
	path := filepath.Join(dir, "data/settings.json")
	before, _ := os.ReadFile(path)
	if e := a.Run([]string{"credentials", "--dir", dir, "--no-service", "--json"}); e != nil {
		t.Fatal(e)
	}
	after, _ := os.ReadFile(path)
	output := a.Out.(*bytes.Buffer).Bytes()
	var displayed struct {
		Password          string
		PasswordAvailable bool
	}
	if e := json.Unmarshal(output, &displayed); e != nil {
		t.Fatal(e)
	}
	if !bytes.Equal(before, after) || bytes.Contains(output, []byte(initial.PasswordHash)) || displayed.Password != password || !displayed.PasswordAvailable {
		t.Fatal("credentials changed settings, exposed the hash or lost the display password")
	}
}

func TestCredentialsShowsDomainBeforeLocalhost(t *testing.T) {
	a, dir, settings, _ := fixture(t)
	settings.Domain, settings.DomainEnabled = "panel.example.test", true
	settings.TLSCert, settings.TLSKey = "saved-cert", "saved-key"
	if err := configuration.AtomicJSON(filepath.Join(dir, "data/settings.json"), settings); err != nil {
		t.Fatal(err)
	}
	if err := a.Run([]string{"credentials", "--dir", dir, "--no-service", "--lang", "en"}); err != nil {
		t.Fatal(err)
	}
	output := a.Out.(*bytes.Buffer).String()
	domain, localhost := strings.Index(output, "Domain:"), strings.Index(output, "This PC (localhost):")
	if domain < 0 || localhost < 0 || domain >= localhost {
		t.Fatal("CLI did not put the configured domain before the local fallback")
	}
}
func TestServiceCommandsAndPrivileges(t *testing.T) {
	a, dir, _, _ := fixture(t)
	f := &fakeService{status: platform.Status{Registered: true, Executable: platform.PanelPath(dir)}}
	a.Service = f
	for _, args := range [][]string{{"start"}, {"stop"}, {"restart"}, {"autostart", "enable"}, {"autostart", "disable"}, {"autostart", "status"}, {"status"}} {
		if e := a.Run(append(args, "--dir", dir)); e != nil {
			t.Fatal(e)
		}
	}
	if !reflect.DeepEqual(f.calls, []string{"start", "stop", "stop", "start", "enable", "enable"}) || !f.status.Running || f.status.Enabled {
		t.Fatal(f.calls, f.status)
	}
	a.IsAdmin = func() bool { return false }
	for _, args := range [][]string{{"start"}, {"stop"}, {"restart"}, {"autostart", "enable"}} {
		if e := a.Run(append(args, "--dir", dir)); e != ErrAdmin {
			t.Fatal("mutation allowed without admin", e)
		}
	}
	if len(f.calls) != 6 {
		t.Fatal("denied commands still controlled the service")
	}
}
func TestLockedManualPanelCannotBeUninstalled(t *testing.T) {
	a, dir, _, _ := fixture(t)
	unlock, e := configuration.Lock(filepath.Join(dir, "data"))
	if e != nil {
		t.Fatal(e)
	}
	defer unlock()
	if e = a.Run([]string{"uninstall", "--yes", "--dir", dir, "--no-service"}); e == nil {
		t.Fatal("locked installation removed")
	}
	if _, e = os.Stat(filepath.Join(dir, configuration.MarkerName)); e != nil {
		t.Fatal("locked installation removed")
	}
}
func TestHelpAndRemovedSettingsCommands(t *testing.T) {
	a, dir, _, _ := fixture(t)
	if e := a.Run([]string{"справка", "--lang", "en"}); e != nil {
		t.Fatal(e)
	}
	help := a.Out.(*bytes.Buffer).String()
	if !bytes.Contains([]byte(help), []byte("<command>")) || !bytes.Contains([]byte(help), []byte("==============================================================")) {
		t.Fatal("legacy help layout lost")
	}
	for _, removed := range []string{"network", "reset", "decoy", "--data-dir", "--purge"} {
		if bytes.Contains([]byte(help), []byte(removed)) {
			t.Fatal("extra setting in help:", removed)
		}
	}
	for _, args := range [][]string{{"network", "lan", "on"}, {"reset"}, {"credentials", "--reset"}, {"decoy", "status"}} {
		if e := a.Run(append(args, "--dir", dir, "--no-service")); e == nil {
			t.Fatal("removed command accepted", args)
		}
	}
}
func TestUninstallRemovesAllDataAndRejectsUnmanagedDirectory(t *testing.T) {
	a, dir, _, _ := fixture(t)
	if e := os.WriteFile(platform.PanelPath(dir), []byte("fixture"), 0700); e != nil {
		t.Fatal(e)
	}
	if e := a.Run([]string{"uninstall", "--yes", "--no-service", "--dir", dir}); e != nil {
		t.Fatal(e)
	}
	if _, e := os.Stat(dir); !os.IsNotExist(e) {
		t.Fatal("default uninstall retained files")
	}
	output := a.Out.(*bytes.Buffer).String()
	for _, expected := range []string{"OpenFlux Zen Server — Полное удаление", "  [INFO] Остановка служб", "  [INFO] Удаление файлов", "  [OK] OpenFlux Zen Server полностью удалён"} {
		if !strings.Contains(output, expected) {
			t.Fatalf("legacy uninstall layout missing %q: %s", expected, output)
		}
	}
	unmanaged := t.TempDir()
	sentinel := filepath.Join(unmanaged, "important.txt")
	_ = os.WriteFile(sentinel, []byte("preserve"), 0600)
	if e := a.Run([]string{"uninstall", "--yes", "--no-service", "--dir", unmanaged}); e == nil {
		t.Fatal("unmanaged directory accepted")
	}
	if _, e := os.Stat(sentinel); e != nil {
		t.Fatal("unmanaged data lost")
	}
}

func TestUninstallCancellationKeepsAllFiles(t *testing.T) {
	for _, answer := range []string{"", "\n", "no\n", "нет\n"} {
		a, dir, _, _ := fixture(t)
		a.In = strings.NewReader(answer)
		service := &fakeService{status: platform.Status{Registered: true, Running: true, Executable: platform.PanelPath(dir)}}
		a.Service = service
		if err := a.Run([]string{"uninstall", "--dir", dir}); err != nil {
			t.Fatal(err)
		}
		output := a.Out.(*bytes.Buffer).String()
		if !strings.Contains(output, "[y/N, default: N]: ") || !strings.Contains(output, "[WARN] Удаление отменено.") || strings.Contains(output, "[OK]") {
			t.Fatal("cancellation did not retain the legacy prompt/warning")
		}
		if len(service.calls) != 0 {
			t.Fatal("cancelled uninstall mutated the service")
		}
		if _, err := configuration.ReadSettings(filepath.Join(dir, "data")); err != nil {
			t.Fatal("cancelled uninstall removed settings", err)
		}
	}
}

func TestUninstallStopsServiceAndReportsOnlyRealSuccess(t *testing.T) {
	for _, failure := range []bool{false, true} {
		a, dir, _, _ := fixture(t)
		service := &fakeService{status: platform.Status{Registered: true, Running: true, Executable: platform.PanelPath(dir)}}
		if failure {
			service.stopErr = errors.New("service stop failed")
		}
		a.Service = service
		a.In = strings.NewReader("yes\n")
		err := a.Run([]string{"uninstall", "--dir", dir, "--lang", "en"})
		output := a.Out.(*bytes.Buffer).String()
		if failure {
			if !errors.Is(err, service.stopErr) || strings.Contains(output, "[OK]") || !reflect.DeepEqual(service.calls, []string{"stop"}) {
				t.Fatal("failed uninstall reported success or continued cleanup")
			}
			if _, err := configuration.ReadSettings(filepath.Join(dir, "data")); err != nil {
				t.Fatal("failed service stop removed settings", err)
			}
		} else if err != nil || !reflect.DeepEqual(service.calls, []string{"stop", "remove"}) || !strings.Contains(output, "Full Uninstallation") || !strings.Contains(output, "[OK]") {
			t.Fatal("confirmed uninstall failed", err, service.calls, output)
		}
	}
}

func TestUninstallJSONRequiresExplicitConfirmationAndStaysClean(t *testing.T) {
	a, dir, _, _ := fixture(t)
	args := []string{"uninstall", "--dir", dir, "--no-service", "--json"}
	if err := a.Run(args); err == nil || a.Out.(*bytes.Buffer).Len() != 0 {
		t.Fatal("JSON uninstall prompted or accepted implicit confirmation")
	}
	if _, err := configuration.ReadSettings(filepath.Join(dir, "data")); err != nil {
		t.Fatal("unconfirmed JSON uninstall removed data", err)
	}
	if err := a.Run(append(args, "--yes")); err != nil {
		t.Fatal(err)
	}
	var result struct {
		Success, DataPreserved bool
	}
	if err := json.Unmarshal(a.Out.(*bytes.Buffer).Bytes(), &result); err != nil || !result.Success || result.DataPreserved {
		t.Fatal("JSON output contains console decorations or incorrect result", err)
	}
}

func TestUninstallChecksPrivilegesAndOwnershipBeforeAnyMutation(t *testing.T) {
	for _, foreign := range []bool{false, true} {
		a, dir, _, _ := fixture(t)
		executable := platform.PanelPath(dir)
		if foreign {
			executable = platform.PanelPath(t.TempDir())
		} else {
			a.IsAdmin = func() bool { return false }
		}
		service := &fakeService{status: platform.Status{Registered: true, Running: true, Executable: executable}}
		a.Service = service
		if err := a.Run([]string{"uninstall", "--yes", "--dir", dir}); err == nil || (!foreign && !errors.Is(err, ErrAdmin)) {
			t.Fatal("unchecked uninstall allowed", err)
		}
		if len(service.calls) != 0 || a.Out.(*bytes.Buffer).Len() != 0 {
			t.Fatal("uninstall started before its preflight passed")
		}
		if _, err := configuration.ReadInstallation(dir); err != nil {
			t.Fatal("unchecked uninstall removed installation", err)
		}
	}
}
