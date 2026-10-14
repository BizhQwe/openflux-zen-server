package configuration

import (
	"regexp"
	"testing"
)

func TestGeneratedAccessPathAndPreservedLegacyPath(t *testing.T) {
	pattern := regexp.MustCompile(`^[a-zA-Z0-9]{18}$`)
	seen := make(map[string]bool)
	for range 100 {
		path := RandomAccessPath()
		if !pattern.MatchString(path) || seen[path] {
			t.Fatalf("invalid or repeated generated path: %q", path)
		}
		seen[path] = true
	}
	store, err := NewStore(t.TempDir())
	if err != nil {
		t.Fatal(err)
	}
	settings, _, err := store.LoadSettings()
	if err != nil || !pattern.MatchString(settings.SecretPath) {
		t.Fatalf("fresh installation path: %q, %v", settings.SecretPath, err)
	}
	settings.SecretPath = "zen-old_Path-12345678"
	if err := store.Write("settings.json", settings); err != nil {
		t.Fatal(err)
	}
	reloaded, _, err := store.LoadSettings()
	if err != nil || reloaded.SecretPath != settings.SecretPath {
		t.Fatalf("existing access path must survive updates: %q, %v", reloaded.SecretPath, err)
	}
}
