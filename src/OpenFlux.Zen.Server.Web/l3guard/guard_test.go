package l3guard

import (
	"errors"
	"os"
	"path/filepath"
	"testing"
)

type fakeBackend struct {
	exists                 bool
	owner                  string
	creates, removes       int
	failCreate, failLookup bool
}

func (b *fakeBackend) owned(g *Guard) (bool, error) {
	if b.failLookup {
		return false, errors.New("permission denied")
	}
	if b.exists && b.owner != g.owner {
		return false, errors.New("foreign table")
	}
	return b.exists, nil
}
func (b *fakeBackend) create(g *Guard) error {
	b.creates++
	if b.failCreate {
		return errors.New("create failed")
	}
	b.exists = true
	b.owner = g.owner
	return nil
}
func (b *fakeBackend) remove(g *Guard) error { b.removes++; b.exists = false; return nil }
func TestGuardLifecycleAndCrashRecovery(t *testing.T) {
	dir := t.TempDir()
	g := New(dir)
	b := &fakeBackend{}
	g.backend = b
	if e := g.Ensure(); e != nil {
		t.Fatal(e)
	}
	if e := g.Ensure(); e != nil || b.creates != 1 {
		t.Fatal("guard recreated while active", e)
	}
	recovered := New(dir)
	recovered.backend = b
	if e := recovered.Clear(); e != nil || b.exists || b.removes != 1 {
		t.Fatal("crash cleanup failed", e)
	}
	if _, e := os.Stat(filepath.Join(dir, stateFile)); !os.IsNotExist(e) {
		t.Fatal("journal retained")
	}
}
func TestGuardRefusesForeignTableAndRollsBackFailure(t *testing.T) {
	g := New(t.TempDir())
	b := &fakeBackend{exists: true, owner: "another application"}
	g.backend = b
	if e := g.Ensure(); e == nil || !b.exists || b.creates+b.removes != 0 {
		t.Fatal("foreign table changed")
	}
	b.exists = false
	b.failCreate = true
	if e := g.Ensure(); e == nil {
		t.Fatal("create failure ignored")
	}
	if _, e := os.Stat(filepath.Join(g.dir, stateFile)); !os.IsNotExist(e) {
		t.Fatal("failed creation left a journal")
	}
}
func TestGuardKeepsJournalWhenCleanupFails(t *testing.T) {
	g := New(t.TempDir())
	b := &fakeBackend{}
	g.backend = b
	if e := g.Ensure(); e != nil {
		t.Fatal(e)
	}
	b.failLookup = true
	if e := g.Clear(); e == nil {
		t.Fatal("cleanup failure ignored")
	}
	if _, e := os.Stat(filepath.Join(g.dir, stateFile)); e != nil {
		t.Fatal("recovery journal lost")
	}
}
