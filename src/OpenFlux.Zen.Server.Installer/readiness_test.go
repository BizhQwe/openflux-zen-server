package main

import (
	"context"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"
)

func TestReadinessChecksPanelBeforeSuccess(t *testing.T) {
	for _, hasCertificate := range []bool{false, true} {
		s := httptest.NewUnstartedServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			if r.URL.Path != "/zen-test-access/" {
				t.Error("wrong readiness path")
			}
			_, _ = w.Write([]byte("<title>OpenFlux Zen Server</title>"))
		}))
		s.Start()
		settings := wizardSettings()
		settings.Listen = strings.TrimPrefix(strings.TrimPrefix(s.URL, "https://"), "http://")
		if hasCertificate {
			settings.TLSCert = "configured-domain-cert"
		}
		ctx, cancel := context.WithTimeout(context.Background(), time.Second)
		e := waitForPanel(ctx, settings)
		cancel()
		s.Close()
		if e != nil {
			t.Fatal(e)
		}
	}
	s := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { _, _ = w.Write([]byte("some other server")) }))
	defer s.Close()
	settings := wizardSettings()
	settings.Listen = strings.TrimPrefix(s.URL, "http://")
	ctx, cancel := context.WithTimeout(context.Background(), 50*time.Millisecond)
	defer cancel()
	if e := waitForPanel(ctx, settings); e == nil {
		t.Fatal("reported readiness for an unrelated HTTP server")
	}
}
