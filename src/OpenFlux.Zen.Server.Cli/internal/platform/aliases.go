package platform

import (
	"fmt"
	"os"
	"path/filepath"

	"openflux.local/zen/panel/configuration"
)

func aliasTarget(alias string) (string, error) {
	value, e := os.Readlink(alias)
	if e != nil {
		return "", e
	}
	if !filepath.IsAbs(value) {
		value = filepath.Join(filepath.Dir(alias), value)
	}
	return filepath.Clean(value), nil
}
func registerAliases(target, legacyTarget string, aliases []string) error {
	if info, e := os.Stat(target); e != nil {
		return e
	} else if !info.Mode().IsRegular() {
		return fmt.Errorf("CLI executable is not a file: %s", target)
	}
	// Validate every destination before changing any command owned by another app.
	for _, alias := range aliases {
		old, e := aliasTarget(alias)
		if os.IsNotExist(e) {
			continue
		}
		if e != nil || (!configuration.SamePath(old, target) && !configuration.SamePath(old, legacyTarget)) {
			return fmt.Errorf("команда %s принадлежит другой программе", alias)
		}
	}
	for _, alias := range aliases {
		old, e := aliasTarget(alias)
		if e == nil && configuration.SamePath(old, target) {
			continue
		}
		if e = os.MkdirAll(filepath.Dir(alias), 0755); e != nil {
			return e
		}
		file, e := os.CreateTemp(filepath.Dir(alias), ".openflux-alias-*")
		if e != nil {
			return e
		}
		temporary := file.Name()
		_ = file.Close()
		_ = os.Remove(temporary)
		if e = os.Symlink(target, temporary); e == nil {
			e = os.Rename(temporary, alias)
		}
		_ = os.Remove(temporary)
		if e != nil {
			return e
		}
	}
	return nil
}
func removeAliases(target, legacyTarget string, aliases []string) error {
	for _, alias := range aliases {
		old, e := aliasTarget(alias)
		if e != nil {
			continue
		}
		if configuration.SamePath(old, target) || configuration.SamePath(old, legacyTarget) {
			if e = os.Remove(alias); e != nil {
				return e
			}
		}
	}
	return nil
}
