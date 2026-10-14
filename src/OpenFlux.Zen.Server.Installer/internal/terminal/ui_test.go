package terminal

import (
	"bytes"
	"errors"
	"strings"
	"testing"
)

func TestFailureNeverDisplaysSuccessfulStartup(t *testing.T) {
	var output bytes.Buffer
	u := &UI{Out: &output, Language: "ru"}
	if e := u.Step(3, "Запуск сервера...", func() error { return errors.New("service failed") }); e == nil {
		t.Fatal("lost failure")
	}
	u.Summary(Summary{PublicURL: "http://127.0.0.1:5000/secret/", LocalURL: "http://127.0.0.1:5000/secret/", Username: "admin", Password: "new-test-password"})
	text := output.String()
	if strings.Contains(text, "[OK]") || strings.Contains(text, "УСПЕШНО") || strings.Contains(text, "\x1b[") {
		t.Fatal("misleading success or ANSI on redirected output")
	}
	if !strings.Contains(text, "[3/3]") || !strings.Contains(text, "[ERROR]") || !strings.Contains(text, "new-test-password") {
		t.Fatal("failure or access details missing")
	}
}
