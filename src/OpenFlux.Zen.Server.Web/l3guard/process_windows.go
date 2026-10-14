package l3guard

import "os/exec"

func (g *Guard) Prepare(cmd *exec.Cmd, mode string) error { return nil }
func Cleanup(dir string) error                            { return nil }
func (g *Guard) Release() error                           { return nil }
