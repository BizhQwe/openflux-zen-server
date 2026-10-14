package configuration

import (
	"os"
	"path/filepath"
	"testing"
)

func TestCanonicalDirRejectsRoot(t *testing.T) {
	root := filepath.VolumeName(t.TempDir()) + string(filepath.Separator)
	if _, err := CanonicalDir(root); err == nil {
		t.Fatal("filesystem root accepted as an installation directory")
	}
}

func TestCanonicalDirRejectsSymlinkComponents(t *testing.T) {
	root := t.TempDir()
	target := filepath.Join(root, "target")
	if err := os.MkdirAll(filepath.Join(target, "data"), 0700); err != nil {
		t.Fatal(err)
	}
	if _, err := CanonicalDir(target); err != nil {
		t.Fatalf("ordinary directory rejected: %v", err)
	}
	link := filepath.Join(root, "link")
	if err := os.Symlink(target, link); err != nil {
		t.Skipf("symlink creation unavailable: %v", err)
	}
	for _, path := range []string{link, filepath.Join(link, "data")} {
		if _, err := CanonicalDir(path); err == nil {
			t.Fatalf("symbolic link component accepted: %s", path)
		}
	}
}
