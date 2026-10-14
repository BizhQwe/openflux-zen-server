package platform

import (
	"encoding/base64"
	"encoding/binary"
	"fmt"
	"openflux.local/zen/panel/configuration"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"unicode/utf16"
)

func RemoveInstallation(dir string) error {
	if _, e := configuration.ReadInstallation(dir); e != nil {
		return e
	}
	exe, _ := os.Executable()
	if !configuration.SamePath(filepath.Dir(exe), dir) {
		return os.RemoveAll(dir)
	}
	// Windows cannot remove the running CLI binary. A hidden PowerShell helper
	// waits for this process, rechecks the exact installation marker and paths,
	// and performs all deletion in the same shell using LiteralPath.
	quoted := strings.ReplaceAll(dir, "'", "''")
	code := fmt.Sprintf(`$ErrorActionPreference='Stop';$installRoot='%s';$ownerPid=%d;Wait-Process -Id $ownerPid -ErrorAction SilentlyContinue;$resolved=[IO.Path]::GetFullPath($installRoot);$root=[IO.Path]::GetPathRoot($resolved);if($resolved.TrimEnd('\') -eq $root.TrimEnd('\')){exit 2};$item=Get-Item -LiteralPath $resolved;if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){exit 3};$marker=Get-Content -LiteralPath (Join-Path $resolved '.openflux-zen-install.json') -Raw|ConvertFrom-Json;if($marker.product -ne 'openflux-zen-server' -or -not [StringComparer]::OrdinalIgnoreCase.Equals($marker.dir,$resolved)){exit 4};`, quoted, os.Getpid())
	code += `$stack=New-Object 'System.Collections.Generic.Stack[string]';$stack.Push($resolved);while($stack.Count -gt 0){$current=$stack.Pop();foreach($entry in [IO.Directory]::EnumerateFileSystemEntries($current)){$attrs=[IO.File]::GetAttributes($entry);if($attrs -band [IO.FileAttributes]::ReparsePoint){exit 6};if($attrs -band [IO.FileAttributes]::Directory){$stack.Push($entry)}}};Remove-Item -LiteralPath $resolved -Recurse -Force`
	units := utf16.Encode([]rune(code))
	raw := make([]byte, len(units)*2)
	for i, v := range units {
		binary.LittleEndian.PutUint16(raw[i*2:], v)
	}
	cmd := exec.Command("powershell.exe", "-NoProfile", "-NonInteractive", "-EncodedCommand", base64.StdEncoding.EncodeToString(raw))
	Hide(cmd)
	if e := cmd.Start(); e != nil {
		return e
	}
	return cmd.Process.Release()
}
