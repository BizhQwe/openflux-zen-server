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
	if strings.TrimSpace(dir) == "" || filepath.Clean(abs) == filepath.VolumeName(abs)+string(filepath.Separator) {
		return "", errors.New("отказ работать с корнем файловой системы")
	}
	resolved, err := filepath.EvalSymlinks(abs)
	if err != nil {
		return "", err
	}
	if !SamePath(abs, resolved) {
		return "", errors.New("каталог установки не должен проходить через символическую ссылку")
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
