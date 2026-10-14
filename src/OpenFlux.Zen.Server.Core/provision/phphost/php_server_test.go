package phphost

import (
	"os"
	"os/exec"
	"path/filepath"
	"regexp"
	"strings"
	"testing"
	"time"
)

func startPHPTestServer(t *testing.T, php, root string) (serverURL, logPath string) {
	t.Helper()
	temp := t.TempDir()
	logPath = filepath.Join(temp, "php-server.log")
	serverLog, err := os.Create(logPath)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = serverLog.Close() })

	// Let PHP bind an ephemeral port itself. Closing a separate Go listener
	// first leaves a race in which another test can take the chosen port.
	cmd := exec.Command(php, "-S", "127.0.0.1:0", "-t", root)
	cmd.Dir = root
	cmd.Stdout, cmd.Stderr = serverLog, serverLog
	for _, entry := range os.Environ() {
		key, _, _ := strings.Cut(entry, "=")
		if strings.EqualFold(key, "TMPDIR") || strings.EqualFold(key, "TMP") || strings.EqualFold(key, "TEMP") || strings.EqualFold(key, "PHP_CLI_SERVER_WORKERS") {
			continue
		}
		cmd.Env = append(cmd.Env, entry)
	}
	// Ping, authentication and status requests are sequential. A single
	// worker also works on Windows, where PHP cannot fork server workers.
	cmd.Env = append(cmd.Env, "TMPDIR="+temp, "TMP="+temp, "TEMP="+temp)
	if err := cmd.Start(); err != nil {
		t.Fatalf("start PHP: %v", err)
	}
	done := make(chan struct{})
	var processErr error
	go func() {
		processErr = cmd.Wait()
		close(done)
	}()
	t.Cleanup(func() {
		_ = cmd.Process.Kill()
		<-done
	})

	startupURL := regexp.MustCompile(`Development Server \((http://127\.0\.0\.1:[1-9][0-9]{0,4})\) started`)
	deadline := time.NewTimer(15 * time.Second)
	defer deadline.Stop()
	tick := time.NewTicker(10 * time.Millisecond)
	defer tick.Stop()
	for {
		select {
		case <-done:
			output, _ := os.ReadFile(logPath)
			t.Fatalf("PHP exited before listening: %v\nPHP output:\n%s", processErr, output)
		case <-deadline.C:
			output, _ := os.ReadFile(logPath)
			t.Fatalf("PHP did not listen within 15s\nPHP output:\n%s", output)
		case <-tick.C:
			output, err := os.ReadFile(logPath)
			if err != nil {
				continue
			}
			if match := startupURL.FindStringSubmatch(string(output)); match != nil {
				return match[1], logPath
			}
		}
	}
}
