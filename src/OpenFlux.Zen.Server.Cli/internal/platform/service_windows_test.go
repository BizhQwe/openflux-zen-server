package platform

import (
	"encoding/json"
	"errors"
	"path/filepath"
	"strings"
	"testing"
)

func TestWindowsManualStartPreservesDisabledAutostart(t *testing.T) {
	dir := t.TempDir()
	state := Status{Registered: true, Enabled: false, Executable: PanelPath(dir)}
	var changes []string
	c := &Controller{Run: func(name string, args ...string) ([]byte, error) {
		if name == "powershell.exe" {
			b, _ := json.Marshal(state)
			return b, nil
		}
		command := strings.Join(args, " ")
		changes = append(changes, command)
		if strings.Contains(command, "/Enable") {
			state.Enabled = true
		}
		if strings.Contains(command, "/Disable") {
			state.Enabled = false
		}
		if strings.Contains(command, "/Run") {
			if !state.Enabled {
				return nil, errors.New("task disabled")
			}
			state.Running = true
		}
		return nil, nil
	}}
	if e := c.Start(dir); e != nil {
		t.Fatal(e)
	}
	if !state.Running || state.Enabled || len(changes) != 3 {
		t.Fatal("manual start changed boot preference", changes)
	}
}
func TestWindowsDoesNotControlAnotherInstallation(t *testing.T) {
	mutated := false
	c := &Controller{Run: func(name string, args ...string) ([]byte, error) {
		if name != "powershell.exe" {
			mutated = true
		}
		b, _ := json.Marshal(Status{Registered: true, Enabled: true, Executable: filepath.Join(t.TempDir(), "other.exe")})
		return b, nil
	}}
	if e := c.Start(t.TempDir()); e == nil || mutated {
		t.Fatal("another installation was controlled")
	}
}
