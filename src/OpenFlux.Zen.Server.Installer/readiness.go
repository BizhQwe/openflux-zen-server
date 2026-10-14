package main

import (
	"context"
	"fmt"
	"io"
	"net/http"
	"strings"
	"time"

	"openflux.local/zen/panel/configuration"
)

func waitForPanel(ctx context.Context, settings configuration.Settings) error {
	address := configuration.PanelURLs(settings)["localhost"]
	// Localhost always serves HTTP, even when the domain has a TLS certificate.
	transport := &http.Transport{}
	defer transport.CloseIdleConnections()
	client := &http.Client{Transport: transport, Timeout: 2 * time.Second, CheckRedirect: func(*http.Request, []*http.Request) error { return http.ErrUseLastResponse }}
	for {
		request, e := http.NewRequestWithContext(ctx, "GET", address, nil)
		if e != nil {
			return e
		}
		response, e := client.Do(request)
		if e == nil {
			body, readErr := io.ReadAll(io.LimitReader(response.Body, 128<<10))
			_ = response.Body.Close()
			if readErr == nil && response.StatusCode == 200 && strings.Contains(string(body), "<title>OpenFlux Zen Server</title>") {
				return nil
			}
		}
		select {
		case <-ctx.Done():
			return fmt.Errorf("panel did not become ready: %w", ctx.Err())
		case <-time.After(250 * time.Millisecond):
		}
	}
}
