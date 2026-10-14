package main

import (
	"archive/zip"
	"bytes"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"os"
	"path/filepath"
	"runtime"
	"testing"
)

func testBundle(t *testing.T, corrupt bool) []byte {
	t.Helper()
	var b bytes.Buffer
	w := zip.NewWriter(&b)
	core, web := executableNames()
	files := map[string][]byte{core: []byte("core fixture"), web: []byte("panel fixture"), cliName(): []byte("cli fixture")}
	hashes := map[string]string{}
	for n, v := range files {
		h := sha256.Sum256(v)
		hashes[n] = hex.EncodeToString(h[:])
	}
	if corrupt {
		files[core] = []byte("tampered")
	}
	manifest, _ := json.Marshal(Manifest{Version: "test", OS: runtime.GOOS, Arch: runtime.GOARCH, Files: hashes})
	files["manifest.json"] = manifest
	for n, v := range files {
		f, e := w.Create(n)
		if e != nil {
			t.Fatal(e)
		}
		_, _ = f.Write(v)
	}
	if e := w.Close(); e != nil {
		t.Fatal(e)
	}
	return b.Bytes()
}
func TestBundleChecksums(t *testing.T) {
	if _, e := readBundle(testBundle(t, false)); e != nil {
		t.Fatal(e)
	}
	if _, e := readBundle(testBundle(t, true)); e == nil {
		t.Fatal("tampered payload accepted")
	}
}
func TestInstallPreservesData(t *testing.T) {
	bundle, e := readBundle(testBundle(t, false))
	if e != nil {
		t.Fatal(e)
	}
	dir := t.TempDir()
	data := filepath.Join(dir, "data")
	if e = os.Mkdir(data, 0700); e != nil {
		t.Fatal(e)
	}
	path := filepath.Join(data, "tunnels.json")
	original := []byte(`[{"name":"preserve"}]`)
	if e = os.WriteFile(path, original, 0600); e != nil {
		t.Fatal(e)
	}
	for _, name := range legacyNames() {
		if e = os.WriteFile(filepath.Join(dir, name), []byte("previous binary"), 0755); e != nil {
			t.Fatal(e)
		}
	}
	if e = installFiles(dir, bundle); e != nil {
		t.Fatal(e)
	}
	for _, name := range legacyNames() {
		if _, e = os.Stat(filepath.Join(dir, name)); !os.IsNotExist(e) {
			t.Fatalf("old binary remains: %s", name)
		}
	}
	for name, expected := range aliasFiles() {
		actual, err := os.ReadFile(filepath.Join(dir, name))
		if err != nil || !bytes.Equal(expected, actual) {
			t.Fatalf("missing CLI alias: %s", name)
		}
	}
	if e = installFiles(dir, bundle); e != nil {
		t.Fatal(e)
	}
	stored, e := os.ReadFile(path)
	if e != nil || !bytes.Equal(stored, original) {
		t.Fatal("update modified tunnel data")
	}
}
func TestInstallRejectsDirectoryWithUnrelatedFiles(t *testing.T) {
	bundle, e := readBundle(testBundle(t, false))
	if e != nil {
		t.Fatal(e)
	}
	dir := t.TempDir()
	sentinel := filepath.Join(dir, "project.go")
	_ = os.WriteFile(sentinel, []byte("preserve"), 0600)
	if e = installFiles(dir, bundle); e == nil {
		t.Fatal("installer accepted unrelated directory")
	}
	stored, _ := os.ReadFile(sentinel)
	if string(stored) != "preserve" {
		t.Fatal("unrelated file changed")
	}
}
