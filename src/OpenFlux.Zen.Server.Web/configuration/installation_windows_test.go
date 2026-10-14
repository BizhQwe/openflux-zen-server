package configuration

import (
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"testing"

	"golang.org/x/sys/windows"
)

func TestCanonicalDirAcceptsWindowsShortNames(t *testing.T) {
	dir := filepath.Join(t.TempDir(), "installation with a long directory name")
	if err := os.Mkdir(dir, 0700); err != nil {
		t.Fatal(err)
	}
	name, err := windows.UTF16PtrFromString(dir)
	if err != nil {
		t.Fatal(err)
	}
	buf := make([]uint16, 32768)
	n, err := windows.GetShortPathName(name, &buf[0], uint32(len(buf)))
	if err != nil || n == 0 || n >= uint32(len(buf)) {
		t.Fatalf("GetShortPathName: length=%d error=%v", n, err)
	}
	short := windows.UTF16ToString(buf[:n])
	if strings.EqualFold(short, dir) {
		t.Skip("filesystem has no DOS 8.3 names for this directory")
	}
	canonical, err := CanonicalDir(short)
	if err != nil {
		t.Fatalf("valid short path rejected: %v", err)
	}
	longInfo, err := os.Stat(dir)
	if err != nil {
		t.Fatal(err)
	}
	canonicalInfo, err := os.Stat(canonical)
	if err != nil || !os.SameFile(longInfo, canonicalInfo) {
		t.Fatalf("canonical path refers to a different directory: %v", err)
	}
	if err := AtomicJSON(filepath.Join(short, MarkerName), Installation{Product: Product, Version: "test", Dir: short}); err != nil {
		t.Fatal(err)
	}
	if _, err := ReadInstallation(short); err != nil {
		t.Fatalf("installation under a short path rejected: %v", err)
	}
}

func TestCanonicalDirRejectsWindowsJunctions(t *testing.T) {
	root := t.TempDir()
	target := filepath.Join(root, "target")
	if err := os.MkdirAll(filepath.Join(target, "data"), 0700); err != nil {
		t.Fatal(err)
	}
	if _, err := CanonicalDir(target); err != nil {
		t.Fatalf("ordinary directory rejected: %v", err)
	}
	junction := filepath.Join(root, "junction")
	if output, err := exec.Command("cmd", "/c", "mklink", "/J", junction, target).CombinedOutput(); err != nil {
		t.Fatalf("create test junction: %v: %s", err, output)
	}
	for _, path := range []string{junction, filepath.Join(junction, "data")} {
		if _, err := CanonicalDir(path); err == nil {
			t.Fatalf("junction component accepted: %s", path)
		}
	}
}
