package hosting

import (
	"context"
	"crypto/ecdsa"
	"crypto/elliptic"
	"crypto/rand"
	"crypto/tls"
	"crypto/x509"
	"crypto/x509/pkix"
	"encoding/pem"
	"io"
	"math/big"
	"net"
	"net/http"
	"os"
	"path/filepath"
	"testing"
	"time"

	"openflux.local/zen/panel/configuration"
)

func availableAddress(t *testing.T) string {
	t.Helper()
	l, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	address := l.Addr().String()
	l.Close()
	return address
}
func get(t *testing.T, client *http.Client, address string) string {
	t.Helper()
	r, err := client.Get(address)
	if err != nil {
		t.Fatal(err)
	}
	defer r.Body.Close()
	if r.StatusCode != http.StatusOK {
		t.Fatalf("HTTP status: %d", r.StatusCode)
	}
	data, err := io.ReadAll(r.Body)
	if err != nil {
		t.Fatal(err)
	}
	return string(data)
}
func TestLiveListenerChangePreservesInFlightAndForeignService(t *testing.T) {
	next := configuration.Settings{Listen: availableAddress(t)}
	host := New(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { io.WriteString(w, "panel") }), nil)
	if _, err := host.Apply(next); err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { host.Close(context.Background()) })
	client := &http.Client{Timeout: time.Second}
	if get(t, client, "http://"+next.Listen) != "panel" {
		t.Fatal("panel unavailable")
	}
	foreignListener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	foreign := &http.Server{Handler: http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { io.WriteString(w, "foreign") })}
	go foreign.Serve(foreignListener)
	defer foreign.Close()
	blocked := next
	blocked.Listen = foreignListener.Addr().String()
	if _, err = host.Apply(blocked); err == nil {
		t.Fatal("occupied port accepted")
	}
	if get(t, client, "http://"+next.Listen) != "panel" || get(t, client, "http://"+blocked.Listen) != "foreign" {
		t.Fatal("failed apply disrupted a listener")
	}
	updated := next
	updated.Listen = availableAddress(t)
	undo, err := host.Apply(updated)
	if err != nil {
		t.Fatal(err)
	}
	if get(t, client, "http://"+updated.Listen) != "panel" {
		t.Fatal("new port unavailable")
	}
	undo()
	if get(t, client, "http://"+next.Listen) != "panel" {
		t.Fatal("rollback lost panel")
	}
}

func TestSwitchHTTPToTLSOnSamePort(t *testing.T) {
	key, err := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
	if err != nil {
		t.Fatal(err)
	}
	template := &x509.Certificate{SerialNumber: big.NewInt(1), Subject: pkix.Name{CommonName: "localhost"}, DNSNames: []string{"localhost"}, IPAddresses: []net.IP{net.ParseIP("127.0.0.1")}, NotBefore: time.Now().Add(-time.Hour), NotAfter: time.Now().Add(time.Hour), KeyUsage: x509.KeyUsageDigitalSignature, ExtKeyUsage: []x509.ExtKeyUsage{x509.ExtKeyUsageServerAuth}}
	der, err := x509.CreateCertificate(rand.Reader, template, template, &key.PublicKey, key)
	if err != nil {
		t.Fatal(err)
	}
	raw, err := x509.MarshalECPrivateKey(key)
	if err != nil {
		t.Fatal(err)
	}
	dir := t.TempDir()
	certPath, keyPath := filepath.Join(dir, "cert.pem"), filepath.Join(dir, "key.pem")
	if err = os.WriteFile(certPath, pem.EncodeToMemory(&pem.Block{Type: "CERTIFICATE", Bytes: der}), 0600); err != nil {
		t.Fatal(err)
	}
	if err = os.WriteFile(keyPath, pem.EncodeToMemory(&pem.Block{Type: "EC PRIVATE KEY", Bytes: raw}), 0600); err != nil {
		t.Fatal(err)
	}
	next := configuration.Settings{Listen: availableAddress(t)}
	var host *Server
	undoChan := make(chan func(), 1)
	host = New(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if (r.URL.Path == "/tls-state" && r.TLS == nil) || (r.URL.Path == "/http-state" && r.TLS != nil) {
			http.Error(w, "wrong TLS state", 500)
			return
		}
		if r.URL.Path == "/apply" {
			next.TLSCert, next.TLSKey = certPath, keyPath
			undo, err := host.Apply(next)
			if err != nil {
				t.Error(err)
				http.Error(w, "failed", 500)
				return
			}
			undoChan <- undo
		}
		io.WriteString(w, "tls panel")
	}), nil)
	if _, err = host.Apply(next); err != nil {
		t.Fatal(err)
	}
	defer host.Close(context.Background())
	// The applying HTTP request must finish even though its listener is retired.
	if get(t, &http.Client{Timeout: time.Second}, "http://"+next.Listen+"/apply") != "tls panel" {
		t.Fatal("apply response lost")
	}
	pool := x509.NewCertPool()
	leaf, _ := x509.ParseCertificate(der)
	pool.AddCert(leaf)
	client := &http.Client{Timeout: time.Second, Transport: &http.Transport{TLSClientConfig: &tls.Config{RootCAs: pool}}}
	if get(t, client, "https://"+next.Listen+"/tls-state") != "tls panel" {
		t.Fatal("TLS unavailable")
	}
	if get(t, &http.Client{Timeout: time.Second}, "http://"+next.Listen+"/http-state") != "tls panel" {
		t.Fatal("HTTP unavailable alongside TLS")
	}
	idle, err := net.Dial("tcp", next.Listen)
	if err != nil {
		t.Fatal(err)
	}
	defer idle.Close()
	client.CloseIdleConnections()
	if get(t, client, "https://"+next.Listen+"/tls-state") != "tls panel" {
		t.Fatal("idle client blocked other connections")
	}
	undo := <-undoChan
	undo()
	_ = idle.SetReadDeadline(time.Now().Add(time.Second))
	if _, err = idle.Read(make([]byte, 1)); err == nil {
		t.Fatal("pending connection survived listener shutdown")
	} else if e, ok := err.(net.Error); ok && e.Timeout() {
		t.Fatal("shutdown did not close pending connection")
	}
	if get(t, &http.Client{Timeout: time.Second}, "http://"+next.Listen) != "tls panel" {
		t.Fatal("HTTP rollback unavailable")
	}
}
