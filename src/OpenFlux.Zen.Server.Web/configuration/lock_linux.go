package configuration

import (
	"os"
	"path/filepath"
	"syscall"
)

func Lock(dir string) (func() error, error) {
	f, err := os.OpenFile(filepath.Join(dir, ".panel.lock"), os.O_CREATE|os.O_RDWR, 0600)
	if err != nil {
		return nil, err
	}
	if err = syscall.Flock(int(f.Fd()), syscall.LOCK_EX|syscall.LOCK_NB); err != nil {
		f.Close()
		return nil, err
	}
	return f.Close, nil
}
