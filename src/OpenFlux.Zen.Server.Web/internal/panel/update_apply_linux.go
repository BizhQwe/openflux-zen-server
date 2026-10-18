package panel

import (
	"os"
	"os/exec"
	"strconv"
	"syscall"
)

// Paths are positional parameters, never interpolated into shell source.
const applyUpdateShell = `set -eu
installer=$1
install_dir=$2
update_dir=$3
panel_pid=$4
trap 'rm -f -- "$installer"; rmdir -- "$update_dir"' EXIT
sleep 2
kill -TERM "$panel_pid" 2>/dev/null || :
attempts=0
while kill -0 "$panel_pid" 2>/dev/null; do
    attempts=$((attempts + 1))
    [ "$attempts" -lt 30 ] || exit 1
    sleep 1
done
"$installer" --silent --dir "$install_dir"
`

func startPanelUpdate(installer, installDir, updateDir string) error {
	args := []string{"/bin/sh", "-c", applyUpdateShell, "openflux-update", installer, installDir, updateDir, strconv.Itoa(os.Getpid())}
	// A helper inside the panel's systemd cgroup would be killed together
	// with the old service. Submit it to its own transient unit instead.
	if os.Getenv("INVOCATION_ID") != "" {
		unit := "openflux-update-" + RandomAccessPath()
		args = append([]string{"--quiet", "--collect", "--unit=" + unit}, args...)
		return exec.Command("systemd-run", args...).Run()
	}
	cmd := exec.Command(args[0], args[1:]...)
	cmd.SysProcAttr = &syscall.SysProcAttr{Setsid: true}
	if err := cmd.Start(); err != nil {
		return err
	}
	go func() { _ = cmd.Wait() }()
	return nil
}
