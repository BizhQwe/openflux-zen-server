package l3guard

import (
	"fmt"
	"os"
	"os/exec"
)

func (g *Guard) Prepare(cmd *exec.Cmd, mode string) error {
	if mode != "l3" {
		return nil
	}
	if e := g.Ensure(); e != nil {
		return e
	}
	cmd.Env = append(os.Environ(), fmt.Sprintf("%s=0x%x", MarkEnv, g.mark))
	return nil
}
func Cleanup(dir string) error  { return New(dir).Clear() }
func (g *Guard) Release() error { return g.Clear() }
