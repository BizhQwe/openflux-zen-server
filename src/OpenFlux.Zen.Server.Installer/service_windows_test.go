package main

import (
	"encoding/xml"
	"io"
	"strings"
	"testing"
)

func TestWindowsTaskXML(t *testing.T) {
	task := taskXML(`C:\Program Files\OpenFlux & Test`)
	decoder := xml.NewDecoder(strings.NewReader(strings.Replace(task, "UTF-16", "UTF-8", 1)))
	for {
		_, e := decoder.Token()
		if e == io.EOF {
			break
		}
		if e != nil {
			t.Fatal(e)
		}
	}
	for _, required := range []string{"<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>", "<RestartOnFailure>", "<UserId>S-1-5-18</UserId>", "OpenFlux &amp; Test"} {
		if !strings.Contains(task, required) {
			t.Fatalf("task missing %s", required)
		}
	}
}
