// OpenFlux Zen Server's installer embeds the core, panel and CLI executables.
package main

import (
	"archive/zip"
	"bytes"
	"crypto/sha256"
	"embed"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"runtime"
	"strings"
)

//go:embed payload
var payload embed.FS

type Manifest struct {
	Version string            `json:"version"`
	OS      string            `json:"os"`
	Arch    string            `json:"arch"`
	Files   map[string]string `json:"files"`
}
type Bundle struct {
	Manifest Manifest
	Files    map[string][]byte
}

func readBundle(raw []byte) (Bundle, error) {
	result := Bundle{Files: map[string][]byte{}}
	reader, e := zip.NewReader(bytes.NewReader(raw), int64(len(raw)))
	if e != nil {
		return result, e
	}
	if len(reader.File) != 4 {
		return result, errors.New("bundle must contain exactly a manifest and three executables")
	}
	for _, entry := range reader.File {
		core, web := executableNames()
		if entry.Name != "manifest.json" && entry.Name != core && entry.Name != web && entry.Name != cliName() {
			return result, errors.New("unexpected bundle entry")
		}
		if _, ok := result.Files[entry.Name]; ok {
			return result, errors.New("duplicate bundle entry")
		}
		if entry.UncompressedSize64 > 150<<20 {
			return result, errors.New("bundle entry too large")
		}
		r, e := entry.Open()
		if e != nil {
			return result, e
		}
		b, e := io.ReadAll(io.LimitReader(r, 150<<20+1))
		_ = r.Close()
		if e != nil || len(b) > 150<<20 {
			return result, errors.New("cannot read bundle entry")
		}
		result.Files[entry.Name] = b
	}
	if e = json.Unmarshal(result.Files["manifest.json"], &result.Manifest); e != nil {
		return result, e
	}
	if result.Manifest.OS != runtime.GOOS || result.Manifest.Arch != runtime.GOARCH {
		return result, errors.New("installer architecture does not match payload")
	}
	if len(result.Manifest.Files) != 3 {
		return result, errors.New("invalid manifest")
	}
	for name, digest := range result.Manifest.Files {
		b, ok := result.Files[name]
		if !ok || name == "manifest.json" {
			return result, errors.New("missing executable")
		}
		hash := sha256.Sum256(b)
		if hex.EncodeToString(hash[:]) != digest {
			return result, errors.New("payload checksum mismatch")
		}
	}
	return result, nil
}
func cliName() string {
	if runtime.GOOS == "windows" {
		return "openflux-zen-server-cli.exe"
	}
	return "openflux-zen-server-cli"
}
func executableNames() (string, string) {
	suffix := ""
	if runtime.GOOS == "windows" {
		suffix = ".exe"
	}
	return "openflux-zen-server-core" + suffix, "openflux-zen-server-web" + suffix
}
func legacyNames() []string {
	if runtime.GOOS == "windows" {
		return []string{"openflux.exe", "openflux-core.exe", "openflux-zen-server.exe"}
	}
	return []string{"openflux", "openflux-core", "openflux-zen-server"}
}
func aliasFiles() map[string][]byte {
	if runtime.GOOS == "windows" {
		return map[string][]byte{"openflux.cmd": []byte("@echo off\r\n\"%~dp0openflux-zen-server-cli.exe\" %*\r\nexit /b %errorlevel%\r\n")}
	}
	return nil
}
func writeAtomic(path string, b []byte) error {
	f, e := os.CreateTemp(filepath.Dir(path), ".install-*")
	if e != nil {
		return e
	}
	tmp := f.Name()
	defer os.Remove(tmp)
	if e = f.Chmod(0755); e == nil {
		_, e = f.Write(b)
	}
	if e == nil {
		e = f.Sync()
	}
	if err := f.Close(); e == nil {
		e = err
	}
	if e != nil {
		return e
	}
	return os.Rename(tmp, path)
}
func installFiles(dir string, bundle Bundle) error {
	core, web := executableNames()
	if _, ok := bundle.Files[core]; !ok {
		return errors.New("missing core")
	}
	if _, ok := bundle.Files[web]; !ok {
		return errors.New("missing panel")
	}
	if _, ok := bundle.Files[cliName()]; !ok {
		return errors.New("missing CLI")
	}
	if e := os.MkdirAll(dir, 0755); e != nil {
		return e
	}
	resolved, e := filepath.EvalSymlinks(dir)
	if e != nil {
		return e
	}
	same := filepath.Clean(dir) == filepath.Clean(resolved)
	if runtime.GOOS == "windows" {
		same = strings.EqualFold(filepath.Clean(dir), filepath.Clean(resolved))
	}
	if !same {
		return errors.New("installation directory must not pass through a symbolic link")
	}
	entries, e := os.ReadDir(dir)
	if e != nil {
		return e
	}
	allowed := map[string]bool{core: true, web: true, cliName(): true, "data": true, ".openflux-zen-install.json": true}
	for _, name := range legacyNames() {
		allowed[name] = true
	}
	for name := range aliasFiles() {
		allowed[name] = true
	}
	for _, entry := range entries {
		if !allowed[entry.Name()] {
			return fmt.Errorf("refusing to install into a directory with unrelated files: %s", entry.Name())
		}
	}
	var marker struct {
		Product   string `json:"product"`
		Version   string `json:"version"`
		Dir       string `json:"dir"`
		PathAdded bool   `json:"pathAdded"`
	}
	markerPath := filepath.Join(dir, ".openflux-zen-install.json")
	if b, e := os.ReadFile(markerPath); e == nil {
		if e = json.Unmarshal(b, &marker); e != nil {
			return e
		}
		if marker.Product != "openflux-zen-server" || !strings.EqualFold(filepath.Clean(marker.Dir), filepath.Clean(dir)) {
			return errors.New("invalid installation marker")
		}
	} else if !os.IsNotExist(e) {
		return e
	}
	// protectData may have created this parent directory with 0700. Only repair
	// the verified installation path; data retains its private permissions.
	if runtime.GOOS == "linux" {
		if e := os.Chmod(dir, 0755); e != nil {
			return e
		}
	}
	names := []string{core, web, cliName()}
	install := map[string][]byte{core: bundle.Files[core], web: bundle.Files[web], cliName(): bundle.Files[cliName()]}
	for name, content := range aliasFiles() {
		names = append(names, name)
		install[name] = content
	}
	backup := map[string][]byte{}
	for _, name := range names {
		b, e := os.ReadFile(filepath.Join(dir, name))
		if e == nil {
			backup[name] = b
		} else if !os.IsNotExist(e) {
			return e
		}
	}
	var installed []string
	for _, name := range names {
		if e := writeAtomic(filepath.Join(dir, name), install[name]); e != nil {
			for _, previous := range installed {
				if old, ok := backup[previous]; ok {
					_ = writeAtomic(filepath.Join(dir, previous), old)
				} else {
					_ = os.Remove(filepath.Join(dir, previous))
				}
			}
			return e
		}
		installed = append(installed, name)
	}
	marker.Product = "openflux-zen-server"
	marker.Version = bundle.Manifest.Version
	marker.Dir = dir
	metadata, e := json.MarshalIndent(marker, "", "  ")
	if e != nil {
		return e
	}
	if e = writeAtomic(markerPath, metadata); e != nil {
		return e
	}
	if e = os.Chmod(markerPath, 0644); e != nil {
		return e
	}
	for _, old := range legacyNames() {
		if e = os.Remove(filepath.Join(dir, old)); e != nil && !os.IsNotExist(e) {
			return e
		}
	}
	return nil
}
func main() {
	if e := run(); e != nil {
		os.Exit(1)
	}
}
