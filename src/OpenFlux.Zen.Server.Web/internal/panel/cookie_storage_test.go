package panel

import (
	"crypto/ed25519"
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"testing"
	"time"

	"github.com/p1neappleXpress/OpenFlux/transport"
)

func TestStoppedMultiCookiesPersistSeparately(t *testing.T) {
	s, settings, password := testServer(t)
	profile := DefaultTunnel()
	profile.Transport = "multi"
	profile.Transports = "yandex:70,avito:50,cupsonline:30"
	profile.YandexURL = "https://disk.yandex.ru/i/offline"
	profile, err := s.manager.Upsert(profile, true)
	if err != nil {
		t.Fatal(err)
	}
	path := filepath.Join(s.store.Dir, "cookies", profile.ID+".json")
	old, err := transport.NewCookieStore(path)
	if err != nil {
		t.Fatal(err)
	}
	if err := old.Save(profile.YandexURL, map[string]string{"Session_id": "kept-login"}); err != nil {
		t.Fatal(err)
	}
	if err := old.Save("unrelated-document", map[string]string{"kept": "yes"}); err != nil {
		t.Fatal(err)
	}
	prefix := "/" + settings.SecretPath + "/"
	login := request(t, s, "POST", prefix+"api/auth/login", map[string]string{"username": settings.Username, "password": password}, nil, "")
	cookie := login.Result().Cookies()[0]
	for _, target := range []string{"yandex", "avito", "cupsonline"} {
		response := request(t, s, "POST", prefix+"api/tunnels/"+profile.ID+"/cookies?transport="+target, map[string]string{"cookies": "session=" + target + "-saved"}, cookie, "")
		var result struct {
			Mode                      string
			AppliedCount, StoredCount int
		}
		if response.Code != 200 || json.Unmarshal(response.Body.Bytes(), &result) != nil || result.Mode != "stored" || result.AppliedCount != 0 || result.StoredCount != 1 {
			t.Fatal("cookies were not saved offline", response.Code, response.Body.String())
		}
	}
	if err := s.manager.Cookies(profile.ID, map[string]string{"spravka": "new-check"}, "yandex"); err != nil {
		t.Fatal(err)
	}
	loaded, err := transport.NewCookieStore(path)
	if err != nil {
		t.Fatal(err)
	}
	for _, target := range []string{"yandex", "avito", "cupsonline"} {
		legacy := ""
		if target == "yandex" {
			legacy = profile.YandexURL
		}
		jar := loaded.Load(loaded.KeyForTransport(target, legacy))
		if jar["session"] != target+"-saved" {
			t.Fatal("transport jars mixed or not readable by core", target)
		}
		if target == "yandex" && (jar["Session_id"] != "kept-login" || jar["spravka"] != "new-check") {
			t.Fatal("existing account or earlier saved cookies lost")
		}
	}
	if loaded.Load("unrelated-document")["kept"] != "yes" {
		t.Fatal("unrelated jar lost")
	}
	current, _ := s.manager.Get(profile.ID)
	if current.IsEnabled || current.Status != Stopped || len(s.manager.processes) != 0 {
		t.Fatal("saving cookies started or enabled the tunnel")
	}
	if runtime.GOOS != "windows" {
		info, err := os.Stat(path)
		if err != nil || info.Mode().Perm() != 0600 {
			t.Fatal("cookies file is not private", err)
		}
	}
	if response := request(t, s, "GET", prefix+"cookies/"+profile.ID+".json", nil, cookie, ""); response.Code != 404 {
		t.Fatal("cookie file exposed over HTTP")
	}
}

func TestStoppedCookiesRefuseToOverwriteCorruptStore(t *testing.T) {
	s, _, _ := testServer(t)
	profile, err := s.manager.Upsert(DefaultTunnel(), true)
	if err != nil {
		t.Fatal(err)
	}
	path := filepath.Join(s.store.Dir, "cookies", profile.ID+".json")
	if err := os.MkdirAll(filepath.Dir(path), 0700); err != nil {
		t.Fatal(err)
	}
	const broken = "{ existing invalid cookie data"
	if err := os.WriteFile(path, []byte(broken), 0600); err != nil {
		t.Fatal(err)
	}
	if err := s.manager.Cookies(profile.ID, map[string]string{"session": "new"}); err == nil {
		t.Fatal("corrupt store was overwritten")
	}
	if data, _ := os.ReadFile(path); string(data) != broken {
		t.Fatal("original cookie data changed")
	}
}

// A signed script checks the jar in its very first open(), before it can
// receive a live IPC offer. This exercises the actual core startup path.
func TestStoppedCookiesLoadedByCoreBeforeStart(t *testing.T) {
	core := os.Getenv("OPENFLUX_TEST_CORE")
	if core == "" {
		t.Skip("set OPENFLUX_TEST_CORE to test actual core startup")
	}
	const source = `var Transport = {
  info: function() { return {name:"offline-cookie-test", version:"1.0.0", cookieDomain:"https://cookies.example.test/"}; },
  open: function() {
    if (cookieJar.get().token !== "saved-before-start") throw new Error("OFFLINE_COOKIE_MISSING");
    console.log("OFFLINE_COOKIE_READY"); setState("connected");
  }, write: function() {}, close: function() {}
};`
	pub, priv, err := ed25519.GenerateKey(rand.Reader)
	if err != nil {
		t.Fatal(err)
	}
	script := filepath.Join(t.TempDir(), "offline.js")
	if err := os.WriteFile(script, []byte(source), 0600); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(script+".sig", ed25519.Sign(priv, []byte(source)), 0600); err != nil {
		t.Fatal(err)
	}
	for _, multi := range []bool{false, true} {
		name := "single"
		if multi {
			name = "multi"
		}
		t.Run(name, func(t *testing.T) {
			store, err := NewStore(t.TempDir())
			if err != nil {
				t.Fatal(err)
			}
			m, err := NewManager(store, core)
			if err != nil {
				t.Fatal(err)
			}
			defer m.Close()
			profile := DefaultTunnel()
			profile.Transport, profile.ScriptPath, profile.ScriptPublicKey, profile.ScriptName = "script", script, hex.EncodeToString(pub), "offline-cookie-test"
			profile.URL = "https://cookies.example.test/doc"
			if multi {
				profile.Transport, profile.Transports, profile.DirectListen = "multi", "script:100,direct:50", freeAddress(t)
			}
			profile, err = m.Upsert(profile, true)
			if err != nil {
				t.Fatal(err)
			}
			if err := m.Cookies(profile.ID, map[string]string{"token": "saved-before-start"}, "script"); err != nil {
				t.Fatal(err)
			}
			for range 2 {
				if err := m.ClearLogs(profile.ID); err != nil {
					t.Fatal(err)
				}
				if err := m.Start(profile.ID); err != nil {
					t.Fatal(err)
				}
				waitUntil(t, 8*time.Second, func() bool {
					for _, line := range m.Logs(profile.ID, 100) {
						if strings.Contains(line.Message, "OFFLINE_COOKIE_READY") {
							return true
						}
					}
					return false
				})
				if err := m.Stop(profile.ID); err != nil {
					t.Fatal(err)
				}
			}
		})
	}
}
