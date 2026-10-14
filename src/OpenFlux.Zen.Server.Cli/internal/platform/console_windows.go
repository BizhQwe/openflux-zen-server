package platform

import (
	"golang.org/x/sys/windows"
	"os"
)

func ConsoleColor() bool {
	var mode uint32
	handle := windows.Handle(os.Stdout.Fd())
	return windows.GetConsoleMode(handle, &mode) == nil && windows.SetConsoleMode(handle, mode|windows.ENABLE_VIRTUAL_TERMINAL_PROCESSING) == nil
}
