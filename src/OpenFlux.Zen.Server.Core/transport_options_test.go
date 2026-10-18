package main

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"github.com/p1neappleXpress/OpenFlux/share"
	"github.com/p1neappleXpress/OpenFlux/transport"
)

func TestPrivateOptionsReachBitrix24Carrier(t *testing.T) {
	sent := make(chan string, 1)
	portal := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		_ = r.ParseForm()
		if r.Header.Get("Cookie") != "PHPSESSID=test" || r.Form.Get("sessid") != "csrf-test" || r.Form.Get("DIALOG_ID") != "chat123" {
			t.Errorf("Bitrix24 authentication or chat ID did not reach REST API")
		}
		w.Header().Set("Content-Type", "application/json")
		if strings.Contains(r.URL.Path, "im.message.add") {
			select {
			case sent <- r.Form.Get("MESSAGE"):
			default:
			}
			_, _ = w.Write([]byte(`{"result":1}`))
		} else {
			_, _ = w.Write([]byte(`{"result":{"messages":[]}}`))
		}
	}))
	defer portal.Close()
	path := filepath.Join(t.TempDir(), "options.json")
	if err := os.WriteFile(path, []byte(`{"bitrix24":{"cookies":"PHPSESSID=test","sessid":"csrf-test","chat_id":"123"}}`), 0600); err != nil {
		t.Fatal(err)
	}
	specs := []transportSpec{{Name: "bitrix24", Type: "bitrix24", URL: portal.URL}}
	if err := applyTransportOptions(specs, path); err != nil {
		t.Fatal(err)
	}
	raw, err := makeRawTransport(specs[0], transport.DefaultConfig(), true)
	if err != nil {
		t.Fatal(err)
	}
	if err := raw.Start(); err != nil {
		t.Fatal(err)
	}
	defer raw.Stop()
	if err := raw.Send([]byte("test packet")); err != nil {
		t.Fatal(err)
	}
	select {
	case frame := <-sent:
		if frame != "OFX1:dGVzdCBwYWNrZXQ=" {
			t.Fatalf("unexpected carrier frame %q", frame)
		}
	case <-time.After(3 * time.Second):
		t.Fatal("Bitrix24 did not deliver the frame")
	}
}

func TestOptionsPreserveExistingRoleAndFactoryParameters(t *testing.T) {
	specs := []transportSpec{{Type: "cupsonline", Params: map[string]interface{}{"exit": true}}}
	path := filepath.Join(t.TempDir(), "options.json")
	if err := os.WriteFile(path, []byte(`{"cupsonline":{"lanes":2,"network":"ethernet"}}`), 0600); err != nil {
		t.Fatal(err)
	}
	if err := applyTransportOptions(specs, path); err != nil {
		t.Fatal(err)
	}
	if specs[0].Params["exit"] != true || specs[0].Params["lanes"] != float64(2) {
		t.Fatal(specs[0].Params)
	}
	if _, err := makeRawTransport(specs[0], transport.DefaultConfig(), true); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(path, []byte(`{"cupsonline":false}`), 0600); err != nil {
		t.Fatal(err)
	}
	if err := applyTransportOptions(specs, path); err == nil {
		t.Fatal("invalid parameters accepted")
	}
}

func TestShareSkipsTypesUnsupportedByOfficialClients(t *testing.T) {
	const links = "cupsonline:packed-cups,avito:packed-avito"
	specs := []transportSpec{{Name: "mixed", Type: "mixed", URL: "cupsonline: avito:"}, {Name: "bitrix24", Type: "bitrix24", Params: map[string]interface{}{"cookies": "private"}}, {Name: "avito", Type: "avito"}}
	rooms := map[string]roomLister{"mixed": &fakeRooms{}, "avito": &fakeRooms{list: "packed-avito"}}
	cfg, skipped := shareConfig(specs, true, codecBatched, "a secret of at least 16 characters", "context", "", rooms)
	if len(cfg.Transports) != 1 || cfg.Transports[0].Type != "avito" || len(skipped) != 2 {
		t.Fatal("unsupported carriers must not break the remaining shareable transport")
	}
	rooms["mixed"].(*fakeRooms).list = links
	cfg, skipped = shareConfig(specs, true, codecBatched, "a secret of at least 16 characters", "context", "", rooms)
	if len(skipped) != 2 || len(cfg.Transports) != 1 || cfg.Transports[0].URL != "packed-avito" {
		t.Fatalf("config=%+v skipped=%v", cfg, skipped)
	}
	link, err := share.Encode(cfg)
	if err != nil {
		t.Fatal(err)
	}
	decoded, err := share.Decode(link)
	if err != nil || decoded.Transports[0].Type != "avito" || decoded.Transports[0].URL != "packed-avito" {
		t.Fatalf("round trip: %+v %v", decoded, err)
	}
	data, _ := json.Marshal(decoded)
	if strings.Contains(string(data), "private") {
		t.Fatal("exit credentials included in share link")
	}
	if _, err := share.Encode(share.Config{Transports: []share.Transport{{Type: "mixed", URL: links}}}); err == nil {
		t.Fatal("the share format must not accept a type the official reader rejects")
	}
}
