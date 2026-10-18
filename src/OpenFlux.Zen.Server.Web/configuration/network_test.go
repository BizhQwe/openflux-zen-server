package configuration

import (
	"path/filepath"
	"strings"
	"testing"
)

func TestDomainURLUsesActualPanelOrExplicitProxy(t *testing.T) {
	for _, test := range []struct{ domain, listen, cert, want string }{
		{"panel.example.test", "0.0.0.0:5443", "cert", "https://panel.example.test:5443/test-path/"},
		{"http://panel.example.test", "127.0.0.1:5000", "", "http://panel.example.test/test-path/"},
		{"https://panel.example.test", "127.0.0.1:5000", "", "https://panel.example.test/test-path/"},
		{"https://panel.example.test:8443/", "127.0.0.1:5000", "", "https://panel.example.test:8443/test-path/"},
		{"panel.example.test", "0.0.0.0:443", "cert", "https://panel.example.test/test-path/"},
		{"192.168.1.20", "0.0.0.0:5000", "cert", "http://192.168.1.20:5000/test-path/"},
		{"localhost", "127.0.0.1:5000", "cert", "http://localhost:5000/test-path/"},
	} {
		s := Settings{Domain: test.domain, DomainEnabled: true, Listen: test.listen, TLSCert: test.cert, TLSKey: test.cert, SecretPath: "test-path"}
		got, err := DomainURL(s)
		if err != nil || got != test.want {
			t.Errorf("%s: %s, %v; want %s", test.domain, got, err, test.want)
		}
		if PanelURLs(s)["domain"] != test.want {
			t.Fatal("CLI and panel disagree about domain URL")
		}
	}
}

func TestDomainWithoutCertificateHasNoUsableURL(t *testing.T) {
	s := Settings{Domain: "panel.example.test", DomainEnabled: true, Listen: "0.0.0.0:5000", SecretPath: "test-path"}
	if address, err := DomainURL(s); err == nil || address != "" {
		t.Fatalf("domain without HTTPS was advertised: %s, %v", address, err)
	}
	if PanelURLs(s)["domain"] != "" {
		t.Fatal("panel must not advertise a public HTTP domain")
	}
}

func TestCertificateDomainCannotEscapeStorage(t *testing.T) {
	for _, domain := range []string{"..", ".", "localhost", "127.0.0.1", "panel..test", "-panel.test", "https://panel.test/../../other", "panel_test.test"} {
		if _, err := certificateHost(domain); err == nil {
			t.Fatalf("invalid certificate domain accepted: %s", domain)
		}
	}
	host, err := certificateHost("https://Panel.Example.test:5000")
	if err != nil || host != "panel.example.test" {
		t.Fatalf("host: %s %v", host, err)
	}
	dir := t.TempDir()
	cert, key := tlsPaths(dir, host)
	if filepath.Dir(cert) != filepath.Dir(key) || filepath.Dir(filepath.Dir(filepath.Dir(cert))) != dir {
		t.Fatal("certificate outside data directory")
	}
}

func TestLocalAndLANLinksStayHTTPWithDomainCertificate(t *testing.T) {
	s := Settings{Domain: "panel.example.test", DomainEnabled: true, Listen: "0.0.0.0:5000", TLSCert: "configured-cert", TLSKey: "configured-key", SecretPath: "test-path"}
	urls := PanelURLs(s)
	if urls["localhost"] != "http://127.0.0.1:5000/test-path/" {
		t.Fatal(urls["localhost"])
	}
	if lan := urls["lan"]; lan != "" && !strings.HasPrefix(lan, "http://") {
		t.Fatal(lan)
	}
	if urls["domain"] != "https://panel.example.test:5000/test-path/" {
		t.Fatal(urls["domain"])
	}
}
