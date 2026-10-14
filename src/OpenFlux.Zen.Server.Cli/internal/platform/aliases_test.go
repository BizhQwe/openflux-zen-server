package platform

import (
	"os"
	"path/filepath"
	"testing"
)

func TestCommandAliasesUpgradeAndRefuseForeignCommands(t *testing.T) {
	dir := t.TempDir()
	target := filepath.Join(dir, "openflux-zen-server-cli")
	legacy := filepath.Join(dir, "openflux")
	if e := os.WriteFile(target, []byte("test binary"), 0755); e != nil {
		t.Fatal(e)
	}
	primary := filepath.Join(dir, "local-bin", "openflux")
	fallback := filepath.Join(dir, "bin", "openflux")
	if e := os.MkdirAll(filepath.Dir(primary), 0755); e != nil {
		t.Fatal(e)
	}
	if e := os.Symlink(legacy, primary); e != nil {
		t.Skipf("local test symlinks unavailable: %v", e)
	}
	if e := registerAliases(target, legacy, []string{primary, fallback}); e != nil {
		t.Fatal(e)
	}
	for _, alias := range []string{primary, fallback} {
		info, e := os.Stat(alias)
		real, _ := os.Stat(target)
		if e != nil || !os.SameFile(info, real) {
			t.Fatalf("broken alias: %s", alias)
		}
	}
	if e := registerAliases(target, legacy, []string{primary, fallback}); e != nil {
		t.Fatal("reinstallation broke the command")
	}
	if e := os.Remove(fallback); e != nil {
		t.Fatal(e)
	}
	foreign := []byte("another application's command")
	if e := os.WriteFile(fallback, foreign, 0755); e != nil {
		t.Fatal(e)
	}
	if e := registerAliases(target, legacy, []string{primary, fallback}); e == nil {
		t.Fatal("replaced a foreign command")
	}
	if e := removeAliases(target, legacy, []string{primary, fallback}); e != nil {
		t.Fatal(e)
	}
	if _, e := os.Lstat(primary); !os.IsNotExist(e) {
		t.Fatal("own alias remains")
	}
	if contents, _ := os.ReadFile(fallback); string(contents) != string(foreign) {
		t.Fatal("foreign command changed")
	}
}
