package configuration

import (
	"golang.org/x/sys/windows"
	"os"
	"path/filepath"
)

func Lock(dir string) (func() error, error) {
	f, err := os.OpenFile(filepath.Join(dir, ".panel.lock"), os.O_CREATE|os.O_RDWR, 0600)
	if err != nil {
		return nil, err
	}
	overlapped := windows.Overlapped{}
	if err = windows.LockFileEx(windows.Handle(f.Fd()), windows.LOCKFILE_EXCLUSIVE_LOCK|windows.LOCKFILE_FAIL_IMMEDIATELY, 0, 1, 0, &overlapped); err != nil {
		f.Close()
		return nil, err
	}
	return f.Close, nil
}
