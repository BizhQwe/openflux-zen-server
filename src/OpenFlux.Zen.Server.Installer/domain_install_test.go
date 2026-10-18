package main

import (
	"context"
	"errors"
	"path/filepath"
	"testing"

	"openflux.local/zen/panel/configuration"
)

func TestInstallerDirectDomainIssuesTLS(t *testing.T) {
	previousIssueTLS := issueTLS
	defer func() { issueTLS = previousIssueTLS }()

	issued := false
	issueTLS = func(ctx context.Context, dir, domain string) (string, string, error) {
		issued = true
		if domain != "panel.example.test" {
			t.Fatalf("unexpected domain: %s", domain)
		}
		return filepath.Join(dir, "cert.pem"), filepath.Join(dir, "key.pem"), nil
	}

	opts := installOptions{
		ModeSet:   true,
		Mode:      "domain",
		DomainSet: true,
		Domain:    "panel.example.test",
	}

	settings := wizardSettings()
	if err := opts.apply(&settings); err != nil {
		t.Fatal(err)
	}

	if !settings.DomainEnabled || !configuration.DirectDomainTLS(settings) {
		t.Fatal("direct domain TLS expected")
	}

	// Simulate Step 2 TLS handling
	dir := t.TempDir()
	cert, key, err := configuration.FindDomainTLS(dir, settings.Domain, settings.TLSCert, settings.TLSKey)
	if err != nil {
		ctx := context.Background()
		cert, key, err = issueTLS(ctx, dir, settings.Domain)
		if err != nil {
			t.Fatal(err)
		}
	}
	settings.TLSCert = cert
	settings.TLSKey = key

	if !issued {
		t.Fatal("issueTLS was not called")
	}
	if settings.TLSCert == "" || settings.TLSKey == "" {
		t.Fatal("certificates not set")
	}

	urls := configuration.PanelURLs(settings)
	if urls["domain"] != "https://panel.example.test:5000/zen-test-access/" {
		t.Fatalf("expected HTTPS domain URL, got %s", urls["domain"])
	}
}

func TestInstallerDirectDomainFailsWhenIssueTLSFails(t *testing.T) {
	previousIssueTLS := issueTLS
	defer func() { issueTLS = previousIssueTLS }()

	issueTLS = func(ctx context.Context, dir, domain string) (string, string, error) {
		return "", "", errors.New("port 80 in use")
	}

	opts := installOptions{
		ModeSet:   true,
		Mode:      "domain",
		DomainSet: true,
		Domain:    "panel.example.test",
	}

	settings := wizardSettings()
	if err := opts.apply(&settings); err != nil {
		t.Fatal(err)
	}

	dir := t.TempDir()
	_, _, err := configuration.FindDomainTLS(dir, settings.Domain, settings.TLSCert, settings.TLSKey)
	if err != nil {
		ctx := context.Background()
		_, _, err = issueTLS(ctx, dir, settings.Domain)
		if err == nil {
			t.Fatal("expected issueTLS failure")
		}
	}
}
