package panel

import (
	"encoding/json"
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"testing"
)

func TestNewCarrierOptionsPersistAndStayOutOfArguments(t *testing.T) {
	store, err := NewStore(t.TempDir())
	if err != nil {
		t.Fatal(err)
	}
	profile := DefaultTunnel()
	profile.ID = newID()
	profile.Transport = "multi"
	profile.Transports = "bitrix24:100,mixed:50,telemost:20"
	profile.Bitrix24URL = "https://portal.bitrix24.ru"
	profile.MixedURL = "cupsonline: avito:"
	profile.Bitrix24Cookies = "PHPSESSID=private-cookie"
	profile.Bitrix24Sessid = "private-csrf"
	profile.Bitrix24ChatID = "chat123"
	profile.EncryptionKey = "test-session-secret-1234"
	profile.UpstreamProxy = "127.0.0.1:10808"
	profile.TransportOptions = map[string]map[string]interface{}{"telemost": {"stripes": "auto", "stripes_max": float64(6)}}
	if err := store.Write("tunnels.json", []Tunnel{profile}); err != nil {
		t.Fatal(err)
	}
	loaded, err := store.LoadTunnels()
	if err != nil || len(loaded) != 1 {
		t.Fatalf("load: %v", err)
	}
	args, err := BuildArgs(loaded[0], store.Dir, "test.sock")
	if err != nil {
		t.Fatal(err)
	}
	joined := strings.Join(args, " ")
	for _, want := range []string{"--bitrix24-url=" + profile.Bitrix24URL, "--mixed-url=" + profile.MixedURL, "--upstream-proxy=127.0.0.1:10808"} {
		if !strings.Contains(joined, want) {
			t.Fatalf("missing %s in %v", want, args)
		}
	}
	if strings.Contains(joined, "private-cookie") || strings.Contains(joined, "private-csrf") {
		t.Fatal("Bitrix24 credentials exposed in process arguments")
	}
	path := filepath.Join(store.Dir, "carrier-options", profile.ID+".json")
	data, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	var options map[string]map[string]interface{}
	if err := json.Unmarshal(data, &options); err != nil {
		t.Fatal(err)
	}
	if options["bitrix24"]["cookies"] != profile.Bitrix24Cookies || options["bitrix24"]["sessid"] != profile.Bitrix24Sessid || options["bitrix24"]["chat_id"] != "chat123" || options["telemost"]["stripes_max"] != float64(6) {
		t.Fatal("carrier options lost")
	}
	if _, exists := profile.TransportOptions["bitrix24"]; exists {
		t.Fatal("launch mutated stored parameters")
	}
	if runtime.GOOS != "windows" {
		info, _ := os.Stat(path)
		if info.Mode().Perm() != 0600 {
			t.Fatal("carrier credentials must be private")
		}
	}
}

func TestNewOptionsCannotOverrideServerPolicy(t *testing.T) {
	profile := DefaultTunnel()
	profile.TransportOptions = map[string]map[string]interface{}{"direct": {"is_exit": false}}
	if profile.Validate() == nil {
		t.Fatal("carrier role override accepted")
	}
	if _, err := SplitArgs("--transport-options-file=foreign.json"); err == nil {
		t.Fatal("external parameter file accepted")
	}
	profile.TransportOptions = nil
	profile.Lanes = 65
	if profile.Validate() == nil {
		t.Fatal("too many lanes accepted")
	}
	profile.Lanes = 0
	profile.Mode = "l3"
	profile.UpstreamProxy = "127.0.0.1:10808"
	if profile.Validate() == nil {
		t.Fatal("upstream proxy silently ignored in l3")
	}
}

func TestMixedAndBitrix24AssetsAreEmbedded(t *testing.T) {
	for _, kind := range []string{"mixed", "bitrix24"} {
		if !strings.Contains(string(indexPage), `data-transport="`+kind+`"`) || !strings.Contains(string(indexPage), `<option value="`+kind+`"`) {
			t.Fatalf("missing %s controls", kind)
		}
	}
	if _, err := assets.ReadFile("web/images/transport-bitrix24.png"); err != nil {
		t.Fatal(err)
	}
}
