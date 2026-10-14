package terminal

import (
	"fmt"
	"os"
	"os/signal"
	"strings"
	"sync"
	"syscall"
)

func (u *UI) AskPassword(prompt string) (string, error) {
	restore, e := disableInputEcho()
	if e != nil {
		return "", e
	}
	var once sync.Once
	restoreOnce := func() { once.Do(func() { _ = restore() }) }
	defer restoreOnce()
	abort := make(chan os.Signal, 1)
	done := make(chan struct{})
	signal.Notify(abort, os.Interrupt, syscall.SIGTERM)
	defer signal.Stop(abort)
	defer close(done)
	go func() {
		select {
		case <-abort:
			restoreOnce()
			os.Exit(130)
		case <-done:
		}
	}()
	fmt.Fprint(u.Out, "  "+prompt+": ")
	line, e := u.In.ReadString('\n')
	fmt.Fprintln(u.Out)
	if e != nil && line == "" {
		return "", e
	}
	return strings.TrimSuffix(strings.TrimSuffix(line, "\n"), "\r"), nil
}
