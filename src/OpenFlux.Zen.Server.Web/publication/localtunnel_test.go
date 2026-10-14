package publication

import (
	"bufio"
	"context"
	"encoding/json"
	"io"
	"net"
	"net/http"
	"net/http/httptest"
	"openflux.local/zen/panel/configuration"
	"strings"
	"testing"
	"time"
)

func TestNativeRelayUsesOriginalHostAndShutsDown(t *testing.T) {
	relay, e := net.Listen("tcp", "127.0.0.1:0")
	if e != nil {
		t.Fatal(e)
	}
	defer relay.Close()
	control := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.RawQuery != "new" {
			t.Error("wrong allocation endpoint")
		}
		_ = json.NewEncoder(w).Encode(map[string]any{"id": "test", "ip": "127.0.0.1", "port": relay.Addr().(*net.TCPAddr).Port, "url": "https://test.provider", "max_conn_count": 1})
	}))
	defer control.Close()
	c := New(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Host != "test.provider" || r.URL.Path != "/zen-fixture/" || r.TLS == nil || RelayOrigin(r) != "https://test.provider" {
			t.Error("relay rewrote the request or lost HTTPS termination")
		}
		http.SetCookie(w, &http.Cookie{Name: "test", Value: "value", Secure: r.TLS != nil})
		_, _ = io.WriteString(w, "original-handler")
	}), nil)
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	done := make(chan struct{})
	go func() {
		defer close(done)
		c.Run(ctx, func() configuration.Settings {
			return configuration.Settings{LocaltunnelEnabled: true, LocaltunnelHost: control.URL}
		})
	}()
	_ = relay.(*net.TCPListener).SetDeadline(time.Now().Add(5 * time.Second))
	conn, e := relay.Accept()
	if e != nil {
		t.Fatal(e)
	}
	defer conn.Close()
	_ = conn.SetDeadline(time.Now().Add(5 * time.Second))
	_, _ = io.WriteString(conn, "GET /zen-fixture/ HTTP/1.1\r\nHost: test.provider\r\nConnection: close\r\n\r\n")
	response, e := http.ReadResponse(bufio.NewReader(conn), nil)
	if e != nil {
		t.Fatal(e)
	}
	body, _ := io.ReadAll(response.Body)
	_ = response.Body.Close()
	if string(body) != "original-handler" || !strings.Contains(response.Header.Get("Set-Cookie"), "Secure") {
		t.Fatal("relay response failed")
	}
	cancel()
	select {
	case <-done:
	case <-time.After(5 * time.Second):
		t.Fatal("publication did not stop")
	}
	if c.Status().State != "disabled" {
		t.Fatal("publication status did not reset")
	}
}
func TestInvalidAllocationDoesNotConnect(t *testing.T) {
	control := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		_ = json.NewEncoder(w).Encode(map[string]any{"port": 0, "url": "file:///invalid"})
	}))
	defer control.Close()
	c := New(http.NotFoundHandler(), nil)
	if e := c.session(context.Background(), control.URL); e == nil {
		t.Fatal("invalid relay allocation accepted")
	}
}
