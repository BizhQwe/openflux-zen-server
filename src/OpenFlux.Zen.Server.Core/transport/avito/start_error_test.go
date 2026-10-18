package avito

import (
	"bytes"
	"errors"
	"log"
	"net/http"
	"net/http/httptest"
	"net/url"
	"strings"
	"testing"
	"time"

	"github.com/p1neappleXpress/OpenFlux/transport"
	"github.com/p1neappleXpress/OpenFlux/utils"
)

func TestRoomCreationFailureVisibleWithoutDebug(t *testing.T) {
	for _, test := range []struct {
		name, body, want string
		status           int
	}{
		{"forbidden", "test access denied", "POST room status 403: test access denied", http.StatusForbidden},
		{"invalid response", "not JSON", "parse room:", http.StatusOK},
	} {
		t.Run(test.name, func(t *testing.T) {
			portal := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				w.WriteHeader(test.status)
				_, _ = w.Write([]byte(test.body))
			}))
			defer portal.Close()
			oldURL := baseURL
			baseURL = portal.URL
			defer func() { baseURL = oldURL }()

			var output bytes.Buffer
			oldOutput, oldLevel := log.Writer(), utils.Level()
			log.SetOutput(&output)
			utils.SetLevel(utils.LevelOff)
			defer func() { log.SetOutput(oldOutput); utils.SetLevel(oldLevel) }()

			carrier := NewAvitoTransport("", transport.DefaultConfig(), "", false)
			carrier.config.NumRooms = 1
			carrier.config.RoomCreatePause = time.Millisecond
			defer carrier.Stop()
			err := carrier.Start()
			if err == nil || !strings.Contains(err.Error(), test.want) {
				t.Fatalf("startup lost the API failure: %v", err)
			}
			if !strings.Contains(output.String(), test.want) {
				t.Fatalf("failure invisible at debug=0: %s", output.String())
			}
			if count := strings.Count(output.String(), "room creation failed:"); count != 1 {
				t.Fatalf("expected one normal-log error across retries, got %d", count)
			}
			if carrier.RoomList() != "" {
				t.Fatal("failed startup advertised rooms")
			}
		})
	}
}

func TestRoomCreationStartupPreservesNetworkError(t *testing.T) {
	portal := httptest.NewServer(http.HandlerFunc(func(http.ResponseWriter, *http.Request) {}))
	portal.Close()
	oldURL := baseURL
	baseURL = portal.URL
	defer func() { baseURL = oldURL }()
	carrier := NewAvitoTransport("", transport.DefaultConfig(), "", false)
	carrier.config.NumRooms = 1
	carrier.config.RoomCreatePause = time.Millisecond
	defer carrier.Stop()
	var requestError *url.Error
	if err := carrier.Start(); !errors.As(err, &requestError) {
		t.Fatalf("network cause lost: %v", err)
	}
}
