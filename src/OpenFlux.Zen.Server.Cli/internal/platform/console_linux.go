package platform

import (
	"golang.org/x/sys/unix"
	"os"
)

func ConsoleColor() bool {
	_, e := unix.IoctlGetTermios(int(os.Stdout.Fd()), unix.TCGETS)
	return e == nil && os.Getenv("TERM") != "dumb"
}
