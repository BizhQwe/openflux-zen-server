package panel

import (
	"bytes"
	"crypto/rand"
	"encoding/base64"
	"image/png"
	"strconv"
	"testing"

	"github.com/p1neappleXpress/OpenFlux/share"
)

func TestDenseQRHasIntegralPixelsAndQuietZone(t *testing.T) {
	s, settings, password := testServer(t)
	config := share.Config{Negotiate: true, Secret: "dense-qr-test-secret-12345", Context: "multi-context"}
	for _, kind := range []string{"yandex", "vyandex", "boards", "mailru", "cupsonline", "avito", "telemost"} {
		entropy := make([]byte, 160)
		if _, e := rand.Read(entropy); e != nil {
			t.Fatal(e)
		}
		config.Transports = append(config.Transports, share.Transport{Type: kind, URL: "https://example.test/" + base64.RawURLEncoding.EncodeToString(entropy), Priority: 50})
	}
	link, err := share.MakeLink(config)
	if err != nil {
		t.Fatal(err)
	}
	prefix := "/" + settings.SecretPath + "/"
	login := request(t, s, "POST", prefix+"api/auth/login", map[string]string{"username": settings.Username, "password": password}, nil, "")
	response := request(t, s, "POST", prefix+"api/qr", map[string]string{"link": link}, login.Result().Cookies()[0], "")
	if response.Code != 200 {
		t.Fatal(response.Body.String())
	}
	img, err := png.Decode(bytes.NewReader(response.Body.Bytes()))
	if err != nil {
		t.Fatal(err)
	}
	modules, err := strconv.Atoi(response.Header().Get("X-QR-Modules"))
	if err != nil || modules < 29 {
		t.Fatal("invalid module count", modules, err)
	}
	side := img.Bounds().Dx()
	if side < 1024 || side%modules != 0 {
		t.Fatal("small/fractional module rendering", side, modules)
	}
	scale := side / modules
	for y := 0; y < side; y++ {
		for x := 0; x < side; x++ {
			r, g, b, _ := img.At(x, y).RGBA()
			if r != g || r != b || (r != 0 && r != 65535) {
				t.Fatal("QR has blurred pixels")
			}
			if x < 4*scale || y < 4*scale || x >= side-4*scale || y >= side-4*scale {
				if r != 65535 {
					t.Fatal("quiet zone too narrow")
				}
			}
		}
	}
	result := share.Read(link)
	if result.Error != "" || len(result.Config.Transports) != 7 {
		t.Fatal("link format changed", result.Error)
	}
}
