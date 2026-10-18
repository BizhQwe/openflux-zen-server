package panel

import (
	"os"
	"os/exec"
	"path/filepath"
	"strconv"
)

const applyUpdatePowerShell = `param([string]$InstallerPath, [string]$InstallDir, [int]$PanelPid)
$ErrorActionPreference = 'Stop'
try {
    Start-Sleep -Seconds 2
    Stop-Process -Id $PanelPid -Force -ErrorAction SilentlyContinue
    $process = Start-Process -FilePath $InstallerPath -ArgumentList @('--silent', '--dir', ('"' + $InstallDir + '"')) -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) { throw 'Installer failed' }
} finally {
    Remove-Item -LiteralPath $InstallerPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
    [IO.Directory]::Delete($PSScriptRoot)
}
`

func startPanelUpdate(installer, installDir, updateDir string) error {
	script := filepath.Join(updateDir, "apply.ps1")
	f, err := os.OpenFile(script, os.O_CREATE|os.O_EXCL|os.O_WRONLY, 0600)
	if err != nil {
		return err
	}
	_, err = f.WriteString(applyUpdatePowerShell)
	closeErr := f.Close()
	if err != nil {
		return err
	}
	if closeErr != nil {
		return closeErr
	}
	// Arguments are separate from the constant script. In particular, %, &,
	// spaces and parentheses in paths never become cmd.exe batch source.
	cmd := exec.Command("powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
		"-InstallerPath", installer, "-InstallDir", installDir, "-PanelPid", strconv.Itoa(os.Getpid()))
	prepareProcess(cmd)
	if err := cmd.Start(); err != nil {
		return err
	}
	go func() { _ = cmd.Wait() }()
	return nil
}
