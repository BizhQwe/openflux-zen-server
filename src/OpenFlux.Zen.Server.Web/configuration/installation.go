package configuration

import (
	"encoding/json"
	"errors"
	"os"
	"path/filepath"
	"runtime"
	"strings"
)

const Product = "openflux-zen-server"
const MarkerName = ".openflux-zen-install.json"

type Installation struct {
	Product   string `json:"product"`
	Version   string `json:"version"`
	Dir       string `json:"dir"`
	PathAdded bool   `json:"pathAdded"`
}

func CanonicalDir(dir string) (string, error) {
	abs, err := filepath.Abs(dir)
	if err != nil {
		return "", err
	}
	if strings.TrimSpace(dir) == "" || filepath.Dir(abs) == abs {
		return "", errors.New("отказ работать с корнем файловой системы")
	}
	// Check actual link attributes, including parent directories. On Windows,
	// EvalSymlinks also expands ordinary DOS 8.3 names, so comparing its result
	// with the input incorrectly rejects valid paths such as RUNNER~1.
	for path := abs; ; {
		info, err := os.Lstat(path)
		if err != nil {
			return "", err
		}
		// Modern Go reports Windows junctions as irregular name-surrogate
		// entries rather than directories or POSIX symlinks.
		if info.Mode()&os.ModeSymlink != 0 || (info.Mode()&os.ModeIrregular != 0 && !info.IsDir()) {
			return "", errors.New("каталог установки не должен проходить через символическую ссылку")
		}
		if !info.IsDir() {
			return "", errors.New("путь установки должен указывать на каталог")
		}
		parent := filepath.Dir(path)
		if parent == path {
			break
		}
		path = parent
	}
	return abs, nil
}
func SamePath(a, b string) bool {
	a = filepath.Clean(a)
	b = filepath.Clean(b)
	if runtime.GOOS == "windows" {
		return strings.EqualFold(a, b)
	}
	return a == b
}
func ReadInstallation(dir string) (Installation, error) {
	var marker Installation
	canonical, err := CanonicalDir(dir)
	if err != nil {
		return marker, err
	}
	b, err := os.ReadFile(filepath.Join(canonical, MarkerName))
	if err != nil {
		return marker, err
	}
	if err = json.Unmarshal(b, &marker); err != nil {
		return marker, err
	}
	if marker.Product != Product || !SamePath(marker.Dir, canonical) {
		return marker, errors.New("каталог не является установкой OpenFlux Zen Server")
	}
	return marker, nil
}
