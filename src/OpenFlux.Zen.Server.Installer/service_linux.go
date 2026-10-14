package main

import (
	"errors"
	"fmt"
	"os"
	"os/exec"
)

func defaultInstallDir() string { return "/opt/openflux-zen-server" }
func hideProcess(cmd *exec.Cmd) {}
func protectData(dir string) error {
	if e := os.MkdirAll(dir, 0700); e != nil {
		return e
	}
	return os.Chmod(dir, 0700)
}
func preflightService() error {
	if os.Geteuid() != 0 {
		return errors.New("run this installer with sudo, or use --no-service --dir <directory>")
	}
	if _, e := exec.LookPath("systemctl"); e != nil {
		return errors.New("systemd is unavailable; use --no-service and run the panel with your process supervisor")
	}
	return nil
}
func stopService() error {
	cmd := exec.Command("systemctl", "is-active", "--quiet", "openflux-zen-server.service")
	if cmd.Run() == nil {
		if b, e := exec.Command("systemctl", "stop", "openflux-zen-server.service").CombinedOutput(); e != nil {
			return fmt.Errorf("stop service: %s: %w", b, e)
		}
	}
	return nil
}
func registerService(dir string, autostart bool) error {
	unit, e := systemdUnit(dir)
	if e != nil {
		return e
	}
	if e := os.WriteFile("/etc/systemd/system/openflux-zen-server.service", []byte(unit), 0644); e != nil {
		return e
	}
	boot := "disable"
	if autostart {
		boot = "enable"
	}
	for _, args := range [][]string{{"daemon-reload"}, {boot, "openflux-zen-server.service"}} {
		if b, e := exec.Command("systemctl", args...).CombinedOutput(); e != nil {
			return fmt.Errorf("systemctl: %s: %w", b, e)
		}
	}
	return nil
}
