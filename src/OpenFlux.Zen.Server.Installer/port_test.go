package main

import (
	"net"
	"testing"

	"openflux.local/zen/panel/configuration"
)

func TestFreshInstallerAvoidsForeignListener(t *testing.T) {
	foreign, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	defer foreign.Close()
	s := configuration.Settings{Listen: foreign.Addr().String()}
	before := s
	if err = freshPanelPort(&s, true); err == nil || s != before {
		t.Fatal("explicit occupied port changed")
	}
	if err = freshPanelPort(&s, false); err != nil {
		t.Fatal(err)
	}
	if s.Listen == before.Listen {
		t.Fatal("foreign port selected")
	}
	connection, err := net.Dial("tcp", before.Listen)
	if err != nil {
		t.Fatal("foreign service was disrupted")
	}
	connection.Close()
}
