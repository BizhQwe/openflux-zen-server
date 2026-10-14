package panel

import (
	"openflux.local/zen/panel/configuration"
	"os/exec"
	"syscall"
)

func prepareProcess(cmd *exec.Cmd) {
	cmd.SysProcAttr = &syscall.SysProcAttr{Pdeathsig: syscall.SIGKILL}
}
func newProcessGroup() (func(*exec.Cmd) error, func() error, error) {
	return func(*exec.Cmd) error { return nil }, func() error { return nil }, nil
}

var lockStore = configuration.Lock
