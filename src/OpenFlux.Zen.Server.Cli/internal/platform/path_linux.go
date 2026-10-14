package platform

import (
	"os"
	"path/filepath"
)

const AliasPath = "/usr/local/bin/openflux"
const FallbackAliasPath = "/usr/bin/openflux"

func RegisterPath(dir string) (bool, error) {
	// Repair installations whose parent was created by MkdirAll(data, 0700).
	if e := os.Chmod(dir, 0755); e != nil {
		return false, e
	}
	e := registerAliases(filepath.Join(dir, "openflux-zen-server-cli"), filepath.Join(dir, "openflux"), []string{AliasPath, FallbackAliasPath})
	return e == nil, e
}
func RemovePath(dir string, owned bool) error {
	if !owned {
		return nil
	}
	return removeAliases(filepath.Join(dir, "openflux-zen-server-cli"), filepath.Join(dir, "openflux"), []string{AliasPath, FallbackAliasPath})
}
