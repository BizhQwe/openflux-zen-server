package main

import (
	"os"
	"path/filepath"
	"testing"
)

func TestInstallerKeepsPrivateDataButMakesCLITraversable(t *testing.T) {
	dir := filepath.Join(t.TempDir(), "installation")
	data := filepath.Join(dir, "data")
	if e := protectData(data); e != nil { t.Fatal(e) }
	bundle, e := readBundle(testBundle(t, false)); if e != nil { t.Fatal(e) }
	if e = installFiles(dir, bundle); e != nil { t.Fatal(e) }
	for path, mode := range map[string]os.FileMode{dir: 0755, data: 0700, filepath.Join(dir, cliName()): 0755} {
		info, e := os.Stat(path)
		if e != nil || info.Mode().Perm() != mode { t.Fatalf("incorrect permissions on %s: %v", path, e) }
	}
}
