package main

import (
	"errors"
	"fmt"
	"openflux.local/zen/cli/internal/commands"
	"os"
	"os/exec"
	"runtime"
)

func main() {
	app := commands.New()
	err := app.Run(os.Args[1:])
	if err == nil {
		return
	}
	if runtime.GOOS == "linux" && (errors.Is(err, commands.ErrAdmin) || errors.Is(err, os.ErrPermission)) {
		exe, _ := os.Executable()
		cmd := exec.Command("sudo", append([]string{exe}, os.Args[1:]...)...)
		cmd.Stdin, cmd.Stdout, cmd.Stderr = os.Stdin, os.Stdout, os.Stderr
		if e := cmd.Run(); e == nil {
			return
		} else if exit, ok := e.(*exec.ExitError); ok {
			os.Exit(exit.ExitCode())
		}
	}
	fmt.Fprintln(os.Stderr, "OpenFlux:", err)
	os.Exit(1)
}
