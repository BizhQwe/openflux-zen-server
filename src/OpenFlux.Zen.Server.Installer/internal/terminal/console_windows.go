package terminal

import (
	"golang.org/x/sys/windows"
	"os"
)

func inputTerminal() bool {
	var mode uint32
	return windows.GetConsoleMode(windows.Handle(os.Stdin.Fd()), &mode) == nil
}
func outputColor() bool {
	var mode uint32
	handle := windows.Handle(os.Stdout.Fd())
	if windows.GetConsoleMode(handle, &mode) != nil {
		return false
	}
	return windows.SetConsoleMode(handle, mode|windows.ENABLE_VIRTUAL_TERMINAL_PROCESSING) == nil
}

func disableInputEcho() (func() error, error) {
	handle := windows.Handle(os.Stdin.Fd())
	var previous uint32
	if e := windows.GetConsoleMode(handle, &previous); e != nil {
		return nil, e
	}
	if e := windows.SetConsoleMode(handle, previous&^windows.ENABLE_ECHO_INPUT); e != nil {
		return nil, e
	}
	return func() error { return windows.SetConsoleMode(handle, previous) }, nil
}
