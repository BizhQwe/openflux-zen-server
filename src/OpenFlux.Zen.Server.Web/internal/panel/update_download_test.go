package panel

import (
	"crypto/sha256"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

type updateTransport func(*http.Request) (*http.Response, error)

func (f updateTransport) RoundTrip(r *http.Request) (*http.Response, error) { return f(r) }

func TestInstallerRequiresMatchingChecksumFromSelectedRelease(t *testing.T) {
	const asset = "openflux-zen-server-installer-linux-amd64"
	const base = "https://github.com/owner/repo/releases/download/v1.1.2"
	payload := "dummy installer bytes; never executed"
	hash := fmt.Sprintf("%x", sha256.Sum256([]byte(payload)))
	for _, tc := range []struct {
		name, checksum, payload string
		status                  int
		wantOK                  bool
	}{
		{"matching", hash + "  " + asset + "\n", payload, 200, true},
		{"binary marker and CRLF", hash + " *" + asset + "\r\n", payload, 200, true},
		{"tampered", hash + "  " + asset, payload + "changed", 200, false},
		{"missing checksum", "", payload, 404, false},
		{"wrong asset", hash + "  other-file", payload, 200, false},
		{"not hex", strings.Repeat("z", 64) + "  " + asset, payload, 200, false},
		{"multiple checksums", hash + "  " + asset + "\n" + hash + "  other", payload, 200, false},
		{"empty installer", hash + "  " + asset, "", 200, false},
	} {
		t.Run(tc.name, func(t *testing.T) {
			client := &http.Client{Transport: updateTransport(func(r *http.Request) (*http.Response, error) {
				if r.URL.Scheme != "https" || !strings.HasPrefix(r.URL.String(), base+"/") {
					t.Fatalf("escaped selected release: %s", r.URL)
				}
				body, status := tc.payload, 200
				if r.URL.String() == base+"/"+asset+".sha256" {
					body, status = tc.checksum, tc.status
				} else if r.URL.String() != base+"/"+asset {
					t.Fatalf("unexpected download: %s", r.URL)
				}
				return &http.Response{StatusCode: status, Body: io.NopCloser(strings.NewReader(body)), ContentLength: -1, Header: make(http.Header)}, nil
			})}
			dir := t.TempDir()
			path, err := downloadPanelInstaller(client, base, asset, dir)
			if (err == nil) != tc.wantOK {
				t.Fatalf("download result: path=%q err=%v", path, err)
			}
			data, readErr := os.ReadFile(filepath.Join(dir, asset))
			if tc.wantOK {
				if readErr != nil || string(data) != payload || path == "" {
					t.Fatal("verified installer content changed")
				}
			} else if !os.IsNotExist(readErr) || path != "" {
				t.Fatal("unverified executable retained")
			}
		})
	}
}

func TestInstallerDoesNotOverwriteExistingFile(t *testing.T) {
	const asset = "installer"
	const sentinel = "existing file must survive"
	dir := t.TempDir()
	path := filepath.Join(dir, asset)
	if err := os.WriteFile(path, []byte(sentinel), 0600); err != nil {
		t.Fatal(err)
	}
	client := &http.Client{Transport: updateTransport(func(r *http.Request) (*http.Response, error) {
		if !strings.HasSuffix(r.URL.Path, ".sha256") {
			t.Fatal("installer downloaded before exclusive creation")
		}
		body := fmt.Sprintf("%x  %s", sha256.Sum256([]byte("dummy")), asset)
		return &http.Response{StatusCode: 200, Body: io.NopCloser(strings.NewReader(body)), ContentLength: -1}, nil
	})}
	if _, err := downloadPanelInstaller(client, "https://github.com/release", asset, dir); err == nil {
		t.Fatal("existing file overwritten")
	}
	data, err := os.ReadFile(path)
	if err != nil || string(data) != sentinel {
		t.Fatal("existing file changed")
	}
}

func TestUpdateDownloadsAreBoundedWithoutContentLength(t *testing.T) {
	client := &http.Client{Transport: updateTransport(func(*http.Request) (*http.Response, error) {
		return &http.Response{StatusCode: 200, Body: io.NopCloser(strings.NewReader("123456789")), ContentLength: -1}, nil
	})}
	var out strings.Builder
	if err := fetchUpdateFile(client, "https://github.com/release", &out, 4); err == nil || out.Len() > 5 {
		t.Fatal("streamed body limit not enforced")
	}
}

func TestUpdateRedirectsRejectDowngradeAndCredentials(t *testing.T) {
	client := updateHTTPClient(time.Second)
	for _, address := range []string{"http://github.com/installer", "https://user:password@github.com/installer"} {
		u, err := url.Parse(address)
		if err != nil {
			t.Fatal(err)
		}
		if err := client.CheckRedirect(&http.Request{URL: u}, nil); err == nil {
			t.Fatalf("unsafe redirect accepted: %s", address)
		}
	}
	u, _ := url.Parse("https://release-assets.githubusercontent.com/file")
	if err := client.CheckRedirect(&http.Request{URL: u}, nil); err != nil {
		t.Fatal("HTTPS asset CDN rejected", err)
	}
}

func TestUpdateVersionCannotEscapeReleasePath(t *testing.T) {
	for _, value := range []string{"1.1.2", "v1.1.2", "V1.1.2", "v1.1.2-rc.1"} {
		if _, err := releaseTag(value); err != nil {
			t.Fatalf("valid version rejected: %s", value)
		}
	}
	for _, value := range []string{"../latest", "1.1.2/other", "1.1.2?x=y", "1.1.2#x", "1.1.2\nother", "latest"} {
		if _, err := releaseTag(value); err == nil {
			t.Fatalf("unsafe version accepted: %q", value)
		}
	}
}
