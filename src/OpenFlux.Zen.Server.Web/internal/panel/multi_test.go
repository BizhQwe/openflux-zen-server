package panel

import (
	"encoding/json"
	"net"
	"strings"
	"testing"
	"time"

	"github.com/p1neappleXpress/OpenFlux/transport/ipc"
)

func TestMultiCookiesTargetSelectedTransport(t *testing.T) {
	s, settings, password := testServer(t)
	profile := DefaultTunnel()
	profile.Transport = "multi"
	profile.Transports = "yandex:70,avito:50,telemost:30"
	profile.TelemostURL = "https://telemost.yandex.ru/j/test-conference"
	profile, err := s.manager.Upsert(profile, true)
	if err != nil {
		t.Fatal(err)
	}
	left, right := net.Pipe()
	defer left.Close()
	defer right.Close()
	s.manager.mu.Lock()
	s.manager.processes[profile.ID] = &process{conn: left}
	s.manager.tunnels[profile.ID].Challenge = &ipc.CookiesRequestPayload{Transport: "avito"}
	s.manager.mu.Unlock()
	defer func() { s.manager.mu.Lock(); delete(s.manager.processes, profile.ID); s.manager.mu.Unlock() }()
	prefix := "/" + settings.SecretPath + "/"
	login := request(t, s, "POST", prefix+"api/auth/login", map[string]string{"username": settings.Username, "password": password}, nil, "")
	cookie := login.Result().Cookies()[0]
	for _, target := range []string{"yandex", "avito"} {
		got := make(chan ipc.CookiesOfferPayload, 1)
		go func() {
			kind, raw, e := ipc.ReadFrame(right)
			var offer ipc.CookiesOfferPayload
			if e == nil && kind == ipc.MsgCookiesOffer {
				_ = json.Unmarshal(raw, &offer)
			}
			got <- offer
		}()
		response := request(t, s, "POST", prefix+"api/tunnels/"+profile.ID+"/cookies?transport="+target, map[string]string{"cookies": "session=" + target + "-auth"}, cookie, "")
		if response.Code != 200 {
			t.Fatal(response.Body.String())
		}
		select {
		case offer := <-got:
			if offer.Transport != target || offer.Jar["session"] != target+"-auth" {
				t.Fatal("cookies routed to the wrong transport", offer)
			}
		case <-time.After(time.Second):
			t.Fatal("no IPC offer")
		}
		current, _ := s.manager.Get(profile.ID)
		if target == "yandex" && (current.Challenge == nil || current.Challenge.Transport != "avito") {
			t.Fatal("another transport's challenge cleared")
		}
	}
	for _, target := range []string{"", "mailru", "telemost"} {
		response := request(t, s, "POST", prefix+"api/tunnels/"+profile.ID+"/cookies?transport="+target, map[string]string{"cookies": "session=not-sent"}, cookie, "")
		if response.Code == 200 {
			t.Fatal("ambiguous, unrelated or unsupported transport accepted", target)
		}
	}
}

func TestMultiTelemostURLIsPassedToCore(t *testing.T) {
	profile := DefaultTunnel()
	profile.ID = newID()
	profile.Transport = "multi"
	profile.Transports = "yandex:50,telemost:75"
	profile.EncryptionKey = "multi-test-secret-123456"
	profile.YandexURL = "https://disk.yandex.ru/i/example"
	profile.TelemostURL = "https://telemost.yandex.ru/j/conference"
	args, err := BuildArgs(profile, t.TempDir(), "ipc.sock")
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(strings.Join(args, "\n"), "--telemost-url="+profile.TelemostURL) {
		t.Fatal("Telemost URL not passed", args)
	}
}
