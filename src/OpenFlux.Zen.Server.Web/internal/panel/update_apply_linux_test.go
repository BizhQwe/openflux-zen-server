package panel

import (
	"context"
	"os"
	"os/exec"
	"path/filepath"
	"strconv"
	"strings"
	"testing"
	"time"
)

func TestUpdateHelperPreservesPathsAndCleansAfterInstaller(t *testing.T) {
	for _, exitCode := range []int{0, 7} {
		t.Run(strconv.Itoa(exitCode), func(t *testing.T) {
			panel := exec.Command("sleep", "30")
			if err := panel.Start(); err != nil {
				t.Fatal(err)
			}
			done := make(chan struct{})
			go func() { _ = panel.Wait(); close(done) }()
			t.Cleanup(func() { _ = panel.Process.Kill(); <-done })

			installDir := filepath.Join(t.TempDir(), "install with spaces;$literal")
			updateDir := filepath.Join(t.TempDir(), "update with spaces;$literal")
			for _, dir := range []string{installDir, updateDir} {
				if err := os.Mkdir(dir, 0700); err != nil {
					t.Fatal(err)
				}
			}
			installer := filepath.Join(updateDir, "installer;$literal")
			fixture := "#!/bin/sh\nprintf '%s\\n' \"$@\" > \"$3/received-args.txt\"\nexit " + strconv.Itoa(exitCode) + "\n"
			if err := os.WriteFile(installer, []byte(fixture), 0700); err != nil {
				t.Fatal(err)
			}
			ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
			defer cancel()
			cmd := exec.CommandContext(ctx, "/bin/sh", "-c", applyUpdateShell, "openflux-update", installer, installDir, updateDir, strconv.Itoa(panel.Process.Pid))
			output, err := cmd.CombinedOutput()
			if (exitCode == 0 && err != nil) || (exitCode != 0 && err == nil) || ctx.Err() != nil {
				t.Fatalf("helper exit: %v; %s", err, output)
			}
			args, err := os.ReadFile(filepath.Join(installDir, "received-args.txt"))
			if err != nil || string(args) != strings.Join([]string{"--silent", "--dir", installDir, ""}, "\n") {
				t.Fatalf("installer arguments changed: %q, %v", args, err)
			}
			if _, err := os.Stat(updateDir); !os.IsNotExist(err) {
				t.Fatalf("update directory retained: %v", err)
			}
			select {
			case <-done:
			default:
				t.Fatal("old dummy panel is still running")
			}
		})
	}
}
