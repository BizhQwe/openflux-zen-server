package main

import (
	"errors"
	"path"
	"strconv"
	"strings"
)

// WorkingDirectory is a scalar path, not a shell argument. Quotes are literal
// here and cause systemd to reject the unit as a non-absolute path.
func systemdUnit(dir string) (string, error) {
	if !path.IsAbs(dir) || strings.TrimSpace(dir) != dir || strings.ContainsAny(dir, "\x00\r\n\t") {
		return "", errors.New("systemd installation directory must be an absolute path without control characters or trailing whitespace")
	}
	dir = path.Clean(dir)
	working := strings.ReplaceAll(dir, "%", "%%")
	arg := func(value string) string {
		value = strings.ReplaceAll(value, "%", "%%")
		return strconv.Quote(value)
	}
	return "[Unit]\nDescription=OpenFlux Zen Server\nWants=network-online.target\nAfter=network-online.target\n\n" +
		"[Service]\nType=simple\nWorkingDirectory=" + working + "\nExecStart=:" + arg(path.Join(dir, "openflux-zen-server-web")) + " --data-dir " + arg(path.Join(dir, "data")) + "\n" +
		"Restart=on-failure\nRestartSec=5\nKillMode=mixed\nTimeoutStopSec=20\nUMask=0077\n\n[Install]\nWantedBy=multi-user.target\n", nil
}
