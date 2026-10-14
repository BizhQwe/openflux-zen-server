package platform

import (
	"encoding/json"
	"errors"
	"golang.org/x/sys/windows"
	"os/exec"
	"path/filepath"
	"syscall"
)

func Hide(cmd *exec.Cmd)          { cmd.SysProcAttr = &syscall.SysProcAttr{CreationFlags: 0x08000000} }
func PanelPath(dir string) string { return filepath.Join(dir, "openflux-zen-server-web.exe") }
func IsAdmin() bool {
	sid, e := windows.CreateWellKnownSid(windows.WinBuiltinAdministratorsSid)
	if e != nil {
		return false
	}
	member, e := windows.Token(0).IsMember(sid)
	return e == nil && member
}
func (c *Controller) Status(dir string) (Status, error) {
	var s Status
	code := `$t=Get-ScheduledTask -TaskName 'OpenFluxZenServer' -ErrorAction SilentlyContinue;if(-not $t){'{"registered":false}';exit 0};$a=@($t.Actions)[0];[pscustomobject]@{registered=$true;running=($t.State.ToString() -eq 'Running');enabled=[bool]$t.Settings.Enabled;executable=$a.Execute}|ConvertTo-Json -Compress`
	out, e := c.Run("powershell.exe", "-NoProfile", "-NonInteractive", "-Command", code)
	if e != nil {
		return s, e
	}
	e = json.Unmarshal(out, &s)
	return s, e
}
func (c *Controller) Start(dir string) (err error) {
	s, err := c.owned(dir)
	if err != nil {
		return err
	}
	if !s.Enabled {
		if _, err = c.Run("schtasks.exe", "/Change", "/TN", TaskName, "/Enable"); err != nil {
			return err
		}
		defer func() {
			_, e := c.Run("schtasks.exe", "/Change", "/TN", TaskName, "/Disable")
			err = errors.Join(err, e)
		}()
	}
	if _, e := c.Run("schtasks.exe", "/Run", "/TN", TaskName); e != nil {
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
	if _, e = c.Run("schtasks.exe", "/End", "/TN", TaskName); e != nil {
		return e
	}
	return c.wait(dir, false)
}
func (c *Controller) Enable(dir string, on bool) error {
	if _, e := c.owned(dir); e != nil {
		return e
	}
	flag := "/Disable"
	if on {
		flag = "/Enable"
	}
	_, e := c.Run("schtasks.exe", "/Change", "/TN", TaskName, flag)
	return e
}
func (c *Controller) Remove(dir string) error {
	if _, e := c.owned(dir); e != nil {
		return e
	}
	if e := c.Stop(dir); e != nil {
		return e
	}
	_, e := c.Run("schtasks.exe", "/Delete", "/TN", TaskName, "/F")
	return e
}
