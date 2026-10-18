package panel

import (
	"context"
	"crypto/ecdsa"
	"crypto/elliptic"
	"crypto/rand"
	"crypto/tls"
	"crypto/x509"
	"encoding/json"
	"encoding/pem"
	"errors"
	"math/big"
	"net"
	"net/http"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"testing"
	"time"

	"openflux.local/zen/panel/internal/hosting"
)

func testDomainCertificate(t *testing.T, host string) (string, string) {
	t.Helper()
	key, err := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
	if err != nil {
		t.Fatal(err)
	}
	template := &x509.Certificate{SerialNumber: big.NewInt(1), DNSNames: []string{host}, NotBefore: time.Now().Add(-time.Hour), NotAfter: time.Now().Add(24 * time.Hour), KeyUsage: x509.KeyUsageDigitalSignature, ExtKeyUsage: []x509.ExtKeyUsage{x509.ExtKeyUsageServerAuth}}
	der, err := x509.CreateCertificate(rand.Reader, template, template, &key.PublicKey, key)
	if err != nil {
		t.Fatal(err)
	}
	keyBytes, err := x509.MarshalECPrivateKey(key)
	if err != nil {
		t.Fatal(err)
	}
	dir := t.TempDir()
	certPath, keyPath := filepath.Join(dir, "cert.pem"), filepath.Join(dir, "key.pem")
	if err := os.WriteFile(certPath, pem.EncodeToMemory(&pem.Block{Type: "CERTIFICATE", Bytes: der}), 0600); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(keyPath, pem.EncodeToMemory(&pem.Block{Type: "EC PRIVATE KEY", Bytes: keyBytes}), 0600); err != nil {
		t.Fatal(err)
	}
	return certPath, keyPath
}

func runTestDomainWorker(t *testing.T, s *Server) {
	t.Helper()
	ctx, cancel := context.WithCancel(context.Background())
	done := make(chan struct{})
	go func() { defer close(done); s.runDomainSetup(ctx) }()
	t.Cleanup(func() { cancel(); <-done })
}

func TestDomainSetupPreparesHTTPSBeforeChangingListener(t *testing.T) {
	s, settings, _ := testServer(t)
	cert, key := testDomainCertificate(t, "panel.example.test")
	s.domainSetup.issue = func(context.Context, string, string) (string, string, error) { return cert, key, nil }
	applied := 0
	s.SetNetworkApply(func(next Settings) (func(), error) { applied++; return func() { applied-- }, nil })
	runTestDomainWorker(t, s)
	before := s.Settings()
	prefix := "/" + settings.SecretPath + "/"
	cookie := &http.Cookie{Name: "zen_session", Value: s.auth.issue()}
	response := request(t, s, "POST", prefix+"api/settings/network-placement", map[string]any{"domainEnabled": true, "domain": "panel.example.test", "panelPort": 5443}, cookie, "")
	if response.Code != http.StatusAccepted {
		t.Fatal(response.Code, response.Body.String())
	}
	waitUntil(t, time.Second, func() bool { return s.domainSetupStatus().State == "ready" })
	if s.Settings() != before || applied != 0 {
		t.Fatal("background issuance changed the running panel")
	}
	var stored Settings
	if err := s.store.Read("settings.json", &stored); err != nil || stored != before {
		t.Fatal("issuance changed persisted settings", err)
	}
	response = request(t, s, "POST", prefix+"api/settings/network-placement", map[string]string{"domainSetupID": s.domainSetupStatus().ID}, cookie, "")
	if response.Code != 200 || !strings.Contains(response.Body.String(), "https://panel.example.test:5443/") {
		t.Fatal(response.Body.String())
	}
	if current := s.Settings(); current.TLSCert != cert || current.TLSKey != key || current.Listen != "0.0.0.0:5443" || applied != 1 {
		t.Fatal("HTTPS result was not applied")
	}
	if s.domainSetupStatus().State != "applied" {
		t.Fatal("HTTPS setup was not completed")
	}
}

func TestDomainSetupFailuresKeepPreviousSettings(t *testing.T) {
	for _, reason := range []string{"issuance", "wrong certificate", "busy port", "changed placement"} {
		t.Run(reason, func(t *testing.T) {
			s, _, _ := testServer(t)
			cert, key := testDomainCertificate(t, "panel.example.test")
			if reason == "wrong certificate" {
				cert, key = testDomainCertificate(t, "other.example.test")
			}
			s.domainSetup.issue = func(context.Context, string, string) (string, string, error) {
				if reason == "issuance" {
					return "", "", errors.New("ACME refused")
				}
				return cert, key, nil
			}
			s.SetNetworkApply(func(Settings) (func(), error) {
				if reason == "busy port" {
					return nil, errors.New("port is busy")
				}
				return func() {}, nil
			})
			runTestDomainWorker(t, s)
			before := s.Settings()
			next := before
			next.Domain, next.DomainEnabled, next.Listen = "panel.example.test", true, "0.0.0.0:5443"
			if err := s.startDomainSetup(before, next); err != nil {
				t.Fatal(err)
			}
			waitUntil(t, time.Second, func() bool { return s.domainSetupStatus().State != "issuing" })
			if reason == "changed placement" {
				changed := s.Settings()
				changed.LANSubnets = "192.168.1.0/24"
				if err := s.saveSettings(changed); err != nil {
					t.Fatal(err)
				}
				before = changed
			}
			if s.domainSetupStatus().State == "ready" {
				if err := s.completeDomainSetup(s.domainSetupStatus().ID); err == nil {
					t.Fatal("unsafe HTTPS result was applied")
				}
			}
			if s.domainSetupStatus().State != "failed" || s.Settings() != before {
				t.Fatal("failed HTTPS setup replaced settings")
			}
			var stored Settings
			if err := s.store.Read("settings.json", &stored); err != nil || stored != before {
				t.Fatal("failed HTTPS setup replaced disk settings", err)
			}
		})
	}
}

func TestDomainSetupPreservesConcurrentAccountChanges(t *testing.T) {
	s, _, _ := testServer(t)
	cert, key := testDomainCertificate(t, "panel.example.test")
	s.domainSetup.issue = func(context.Context, string, string) (string, string, error) { return cert, key, nil }
	s.SetNetworkApply(func(Settings) (func(), error) { return func() {}, nil })
	runTestDomainWorker(t, s)
	before := s.Settings()
	next := before
	next.Domain, next.DomainEnabled = "panel.example.test", true
	if err := s.startDomainSetup(before, next); err != nil {
		t.Fatal(err)
	}
	waitUntil(t, time.Second, func() bool { return s.domainSetupStatus().State == "ready" })
	changed := s.Settings()
	changed.Language, changed.Username, changed.SecretPath = "en", "new-admin", RandomSecret(18)
	if err := s.saveSettings(changed); err != nil {
		t.Fatal(err)
	}
	if err := s.completeDomainSetup(s.domainSetupStatus().ID); err != nil {
		t.Fatal(err)
	}
	current := s.Settings()
	if current.Language != changed.Language || current.Username != changed.Username || current.SecretPath != changed.SecretPath {
		t.Fatal("HTTPS completion rolled back account settings")
	}
}

func TestDomainSetupRejectsOverlappingIssuance(t *testing.T) {
	s, _, _ := testServer(t)
	next := s.Settings()
	next.Domain, next.DomainEnabled = "panel.example.test", true
	if err := s.startDomainSetup(s.Settings(), next); err != nil {
		t.Fatal(err)
	}
	if err := s.startDomainSetup(s.Settings(), next); err == nil {
		t.Fatal("overlapping certificate request was accepted")
	}
}

func TestDomainSetupRealHTTPToHTTPSPortChange(t *testing.T) {
	s, settings, _ := testServer(t)
	certPath, keyPath := testDomainCertificate(t, "panel.example.test")
	s.domainSetup.issue = func(context.Context, string, string) (string, string, error) { return certPath, keyPath, nil }
	listener := hosting.New(s, s.manager.SystemLog)
	s.SetNetworkApply(listener.Apply)
	before := s.Settings()
	before.Listen = freeAddress(t)
	if err := s.saveSettings(before); err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), time.Second)
		defer cancel()
		_ = listener.Close(ctx)
	})
	runTestDomainWorker(t, s)
	_, nextPort, _ := net.SplitHostPort(freeAddress(t))
	endpoint := "http://" + before.Listen + "/" + settings.SecretPath + "/api/settings/network-placement"
	client := &http.Client{Timeout: 5 * time.Second}
	post := func(value any) map[string]any {
		t.Helper()
		body, _ := json.Marshal(value)
		req, err := http.NewRequest("POST", endpoint, strings.NewReader(string(body)))
		if err != nil {
			t.Fatal(err)
		}
		req.Header.Set("Content-Type", "application/json")
		req.AddCookie(&http.Cookie{Name: "zen_session", Value: s.auth.issue()})
		response, err := client.Do(req)
		if err != nil {
			t.Fatal("old listener lost the placement response", err)
		}
		defer response.Body.Close()
		var result map[string]any
		if err := json.NewDecoder(response.Body).Decode(&result); err != nil || response.StatusCode >= 400 {
			t.Fatalf("placement response: %v %+v", err, result)
		}
		return result
	}
	port, err := strconv.Atoi(nextPort)
	if err != nil {
		t.Fatal(err)
	}
	post(map[string]any{"domainEnabled": true, "domain": "panel.example.test", "panelPort": port})
	waitUntil(t, time.Second, func() bool { return s.domainSetupStatus().State == "ready" })
	result := post(map[string]string{"domainSetupID": s.domainSetupStatus().ID})
	certBytes, err := os.ReadFile(certPath)
	if err != nil {
		t.Fatal(err)
	}
	roots := x509.NewCertPool()
	if !roots.AppendCertsFromPEM(certBytes) {
		t.Fatal("test certificate unavailable")
	}
	transport := &http.Transport{TLSClientConfig: &tls.Config{RootCAs: roots}, DialContext: func(ctx context.Context, network, address string) (net.Conn, error) {
		return (&net.Dialer{}).DialContext(ctx, network, "127.0.0.1:"+nextPort)
	}}
	defer transport.CloseIdleConnections()
	tlsClient := &http.Client{Transport: transport, Timeout: 5 * time.Second}
	response, err := tlsClient.Get(result["domainUrl"].(string))
	if err != nil {
		t.Fatal("HTTPS handshake on the new panel port failed", err)
	}
	defer response.Body.Close()
	if response.StatusCode != 200 || response.TLS == nil || len(response.TLS.VerifiedChains) == 0 {
		t.Fatal("domain is not served over verified TLS")
	}
}
