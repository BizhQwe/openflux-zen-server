package main

import (
	"encoding/binary"
	"encoding/xml"
	"errors"
	"fmt"
	"os"
	"os/exec"
	"os/user"
	"path/filepath"
	"strings"
	"syscall"
	"time"
	"unicode/utf16"
)

func defaultInstallDir() string {
	base := os.Getenv("ProgramFiles")
	if base == "" {
		base = `C:\Program Files`
	}
	return filepath.Join(base, "OpenFlux Zen Server")
}
func hideProcess(cmd *exec.Cmd) { cmd.SysProcAttr = &syscall.SysProcAttr{CreationFlags: 0x08000000} }
func protectData(dir string) error {
	if e := os.MkdirAll(dir, 0700); e != nil {
		return e
	}
	current, e := user.Current()
	if e != nil {
		return e
	}
	output, e := command("icacls.exe", dir, "/inheritance:r", "/grant:r", "*"+current.Uid+":(OI)(CI)F", "*S-1-5-18:(OI)(CI)F", "*S-1-5-32-544:(OI)(CI)F")
	if e != nil {
		return fmt.Errorf("icacls: %s: %w", output, e)
	}
	return nil
}
func command(name string, args ...string) ([]byte, error) {
	cmd := exec.Command(name, args...)
	hideProcess(cmd)
	return cmd.CombinedOutput()
}
func preflightService() error {
	code := `$identity=[Security.Principal.WindowsIdentity]::GetCurrent();$principal=New-Object Security.Principal.WindowsPrincipal($identity);if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){exit 1}`
	if _, e := command("powershell.exe", "-NoProfile", "-NonInteractive", "-Command", code); e != nil {
		return errors.New("run this installer as Administrator, or use --no-service --dir <directory>")
	}
	return nil
}
func stopService() error {
	if _, e := command("schtasks.exe", "/Query", "/TN", "OpenFluxZenServer"); e != nil {
		return nil
	}
	b, e := command("schtasks.exe", "/End", "/TN", "OpenFluxZenServer")
	if e != nil {
		state, queryErr := command("powershell.exe", "-NoProfile", "-NonInteractive", "-Command", `(Get-ScheduledTask -TaskName 'OpenFluxZenServer' -ErrorAction Stop).State.ToString()`)
		if queryErr != nil || strings.TrimSpace(string(state)) == "Running" {
			return fmt.Errorf("stop existing task: %s: %w", b, e)
		}
	}
	time.Sleep(time.Second)
	return nil
}
func taskXML(dir string) string {
	escape := func(value string) string {
		var b strings.Builder
		_ = xml.EscapeText(&b, []byte(value))
		return b.String()
	}
	return `<?xml version="1.0" encoding="UTF-16"?><Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task"><RegistrationInfo><Description>OpenFlux Zen Server</Description></RegistrationInfo><Triggers><BootTrigger><Enabled>true</Enabled><Delay>PT10S</Delay></BootTrigger></Triggers><Principals><Principal id="System"><UserId>S-1-5-18</UserId><RunLevel>HighestAvailable</RunLevel></Principal></Principals><Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><StartWhenAvailable>true</StartWhenAvailable><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><RestartOnFailure><Interval>PT1M</Interval><Count>3</Count></RestartOnFailure></Settings><Actions Context="System"><Exec><Command>` + escape(filepath.Join(dir, "openflux-zen-server-web.exe")) + `</Command><Arguments>` + escape(`--data-dir "`+filepath.Join(dir, "data")+`"`) + `</Arguments><WorkingDirectory>` + escape(dir) + `</WorkingDirectory></Exec></Actions></Task>`
}
func registerService(dir string, autostart bool) error {
	temp, e := os.CreateTemp("", "openflux-task-*.xml")
	if e != nil {
		return e
	}
	path := temp.Name()
	defer os.Remove(path)
	units := utf16.Encode([]rune(taskXML(dir)))
	data := make([]byte, 2+len(units)*2)
	data[0] = 0xff
	data[1] = 0xfe
	for i, v := range units {
		binary.LittleEndian.PutUint16(data[2+i*2:], v)
	}
	_, e = temp.Write(data)
	closeErr := temp.Close()
	if e != nil {
		return e
	}
	if closeErr != nil {
		return closeErr
	}
	boot := "/Disable"
	if autostart {
		boot = "/Enable"
	}
	for _, args := range [][]string{{"/Create", "/TN", "OpenFluxZenServer", "/XML", path, "/F"}, {"/Change", "/TN", "OpenFluxZenServer", boot}} {
		b, e := command("schtasks.exe", args...)
		if e != nil {
			return fmt.Errorf("schtasks: %s: %w", b, e)
		}
	}
	return nil
}
