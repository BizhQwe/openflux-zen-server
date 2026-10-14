package platform

import (
	"openflux.local/zen/panel/configuration"
	"os"
)

func RemoveInstallation(dir string) error {
	if _, e := configuration.ReadInstallation(dir); e != nil {
		return e
	}
	return os.RemoveAll(dir)
}
