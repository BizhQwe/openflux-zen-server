package configuration

import (
	"bytes"
	"errors"
	"os"
	"path/filepath"
	"regexp"
	"runtime"
	"strings"
	"testing"
)

func TestDisplayPasswordAndLegacyMigration(t *testing.T) {
	store, e := NewStore(t.TempDir())
	if e != nil {
		t.Fatal(e)
	}
	settings, password, e := store.LoadSettings()
	if e != nil {
		t.Fatal(e)
	}
	file := filepath.Join(store.Dir, CredentialsFile)
	if actual, err := ReadDisplayPassword(store.Dir, settings); err != nil || actual != password {
		t.Fatal("initial password not saved")
	}
	if runtime.GOOS == "linux" {
		info, _ := os.Stat(file)
		if info.Mode().Perm() != 0600 {
			t.Fatal("display password is accessible to other users")
		}
	}
	before, _ := os.ReadFile(filepath.Join(store.Dir, "settings.json"))
	if e = os.Remove(file); e != nil {
		t.Fatal(e)
	}
	if _, e = ReadDisplayPassword(store.Dir, settings); !os.IsNotExist(e) {
		t.Fatal("legacy password should be unavailable")
	}
	if e = RememberPassword(store.Dir, settings, "wrong-password"); !errors.Is(e, ErrPasswordUnavailable) {
		t.Fatal("accepted a wrong migration password")
	}
	if e = RememberPassword(store.Dir, settings, password); e != nil {
		t.Fatal(e)
	}
	after, _ := os.ReadFile(filepath.Join(store.Dir, "settings.json"))
	if !bytes.Equal(before, after) {
		t.Fatal("password migration changed existing settings")
	}
	if actual, err := ReadDisplayPassword(store.Dir, settings); err != nil || actual != password {
		t.Fatal("migration lost the existing password")
	}
}
func TestChangedPasswordRejectsStaleCopyAndRollsBackFailedCommit(t *testing.T) {
	store, _ := NewStore(t.TempDir())
	old, oldPassword, e := store.LoadSettings()
	if e != nil {
		t.Fatal(e)
	}
	next := old
	newPassword := " new password with spaces "
	next.PasswordSalt = RandomSecret(18)
	next.PasswordHash = HashPassword(newPassword, next.PasswordSalt)
	if _, e = ReadDisplayPassword(store.Dir, next); !errors.Is(e, ErrPasswordUnavailable) {
		t.Fatal("displayed a stale password")
	}
	settingsPath := filepath.Join(store.Dir, "settings.json")
	backup := filepath.Join(store.Dir, "settings.backup.json")
	if e = os.Rename(settingsPath, backup); e != nil {
		t.Fatal(e)
	}
	if e = os.Mkdir(settingsPath, 0700); e != nil {
		t.Fatal(e)
	}
	if e = store.SavePassword(next, newPassword); e == nil {
		t.Fatal("accepted failed settings commit")
	}
	if actual, err := ReadDisplayPassword(store.Dir, old); err != nil || actual != oldPassword {
		t.Fatal("failed commit replaced the existing password")
	}
	if e = os.Remove(settingsPath); e != nil {
		t.Fatal(e)
	}
	if e = os.Rename(backup, settingsPath); e != nil {
		t.Fatal(e)
	}
	if e = store.SavePassword(next, newPassword); e != nil {
		t.Fatal(e)
	}
	if actual, err := ReadDisplayPassword(store.Dir, next); err != nil || actual != newPassword {
		t.Fatal("password whitespace or value changed")
	}
}

func TestRandomPassword(t *testing.T) {
	for i := 0; i < 50; i++ {
		p := RandomPassword(12)
		if len(p) != 12 {
			t.Fatalf("expected 12 characters, got %d: %q", len(p), p)
		}
		for _, c := range p {
			if !strings.ContainsRune(PasswordCharset, c) {
				t.Fatalf("unexpected character %c in password %q", c, p)
			}
		}
	}
}

func TestFreshLoginIsRandomAndExistingCredentialsPreserved(t *testing.T) {
	pattern := regexp.MustCompile(`^[a-zA-Z0-9]{12}$`)
	seen := make(map[string]bool)
	for range 3 {
		store, err := NewStore(t.TempDir())
		if err != nil {
			t.Fatal(err)
		}
		settings, password, err := store.LoadSettings()
		if err != nil || !pattern.MatchString(settings.Username) || !pattern.MatchString(password) || seen[settings.Username] || settings.Username == password {
			t.Fatalf("fresh login/password must be independent random alphanumeric strings: %v", err)
		}
		seen[settings.Username] = true
		// Loading settings during an update must retain both generated and old logins.
		for _, username := range []string{settings.Username, "admin", "my-existing-login"} {
			settings.Username = username
			if err := store.SavePassword(settings, password); err != nil {
				t.Fatal(err)
			}
			path := filepath.Join(store.Dir, "settings.json")
			before, err := os.ReadFile(path)
			if err != nil {
				t.Fatal(err)
			}
			reloaded, generated, err := store.LoadSettings()
			if err != nil || reloaded != settings || generated != "" {
				t.Fatalf("loading an existing installation changed its credentials: %v", err)
			}
			after, err := os.ReadFile(path)
			if err != nil || !bytes.Equal(before, after) {
				t.Fatal("loading existing credentials rewrote settings")
			}
			if actual, err := ReadDisplayPassword(store.Dir, reloaded); err != nil || actual != password {
				t.Fatalf("existing display password lost: %v", err)
			}
		}
	}
}
