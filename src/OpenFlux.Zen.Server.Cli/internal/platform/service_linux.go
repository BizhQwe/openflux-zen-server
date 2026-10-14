package platform

import (
	"os"
	"os/exec"
	"path/filepath"
	"regexp"
	"strings"
)

func Hide(cmd *exec.Cmd)          {}
func PanelPath(dir string) string { return filepath.Join(dir, "openflux-zen-server-web") }
func IsAdmin() bool               { return os.Geteuid() == 0 }
func (c *Controller) Status(dir string) (Status, error) {
	var s Status
	output, e := c.Run("systemctl", "show", ServiceName, "--no-pager", "--property=LoadState,ActiveState,UnitFileState,ExecStart")
	if e != nil {
		return s, e
	}
	values := map[string]string{}
	for _, line := range strings.Split(string(output), "\n") {
		if k, v, ok := strings.Cut(line, "="); ok {
			values[k] = strings.TrimSpace(v)
		}
	}
	s.Registered = values["LoadState"] != "not-found" && values["LoadState"] != ""
	s.Running = values["ActiveState"] == "active" || values["ActiveState"] == "activating"
	s.Enabled = values["UnitFileState"] == "enabled" || values["UnitFileState"] == "enabled-runtime"
	if m := regexp.MustCompile(`path=([^;]+)`).FindStringSubmatch(values["ExecStart"]); len(m) > 1 {
		s.Executable = strings.TrimSpace(m[1])
	}
	return s, nil
}
func (c *Controller) Start(dir string) error {
	if _, e := c.owned(dir); e != nil {
		return e
	}
	if _, e := c.Run("systemctl", "start", ServiceName); e != nil {
		return e
	}
	return c.wait(dir, true)
}
func (c *Controller) Stop(dir string) error {
	s, e := c.owned(dir)
	if e != nil {
		return e
	}
	if !s.Running {
		return nil
	}
	if _, e = c.Run("systemctl", "stop", ServiceName); e != nil {
		return e
	}
	return c.wait(dir, false)
}
func (c *Controller) Enable(dir string, on bool) error {
	if _, e := c.owned(dir); e != nil {
		return e
	}
	verb := "disable"
	if on {
		verb = "enable"
	}
	_, e := c.Run("systemctl", verb, ServiceName)
	return e
}
func (c *Controller) Remove(dir string) error {
	if _, e := c.owned(dir); e != nil {
		return e
	}
	if e := c.Stop(dir); e != nil {
		return e
	}
	if _, e := c.Run("systemctl", "reset-failed", ServiceName); e != nil {
		return e
	}
	if _, e := c.Run("systemctl", "disable", ServiceName); e != nil {
		return e
	}
	unit := "/etc/systemd/system/" + ServiceName
	if e := os.Remove(unit); e != nil && !os.IsNotExist(e) {
		return e
	}
	_, e := c.Run("systemctl", "daemon-reload")
	return e
}
