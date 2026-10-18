package panel

import (
	"encoding/hex"
	"errors"
	"fmt"
	"strconv"
	"strings"
	"time"

	"github.com/p1neappleXpress/OpenFlux/transport/ipc"
	"github.com/p1neappleXpress/OpenFlux/utils"
)

const Version = "1.1.2"
const CoreVersion = "0.13.0"
const ServerRole = "exit"

var ErrServerOnly = errors.New("панель поддерживает только серверные туннели (role=exit)")

const (
	Stopped = iota
	Starting
	Running
	Stopping
	Failed
)

// JSON names deliberately match Zen Server exports. OpenFlux wire formats live in the original core.
type Tunnel struct {
	ID                  string                            `json:"id"`
	Name                string                            `json:"name"`
	Role                string                            `json:"role"`
	Transport           string                            `json:"transport"`
	Inbound             string                            `json:"inbound"`
	Socks5Address       string                            `json:"socks5Address"`
	Mode                string                            `json:"mode"`
	URL                 string                            `json:"url"`
	MaxToken            string                            `json:"maxToken"`
	MaxUID              string                            `json:"maxUid"`
	LocalIP             string                            `json:"localIp"`
	Codec               string                            `json:"codec"`
	EncryptionKey       string                            `json:"encryptionKey"`
	BenchBytes          int                               `json:"benchBytes"`
	BenchCompressible   bool                              `json:"benchCompressible"`
	ExtraArgs           string                            `json:"extraArgs"`
	ScriptPath          string                            `json:"scriptPath"`
	ScriptPublicKey     string                            `json:"scriptPublicKey"`
	ScriptName          string                            `json:"scriptName"`
	ScriptAutoUpdate    bool                              `json:"scriptAutoUpdate"`
	ScriptUpdateChannel string                            `json:"scriptUpdateChannel"`
	EnableShare         bool                              `json:"enableShare"`
	ShareHost           string                            `json:"shareHost"`
	ShareLink           string                            `json:"shareLink"`
	DirectListen        string                            `json:"directListen"`
	DirectDial          string                            `json:"directDial"`
	SessionContext      string                            `json:"sessionContext"`
	Negotiate           bool                              `json:"negotiate"`
	Transports          string                            `json:"transports"`
	YandexURL           string                            `json:"yandexUrl"`
	VyandexURL          string                            `json:"vyandexUrl"`
	BoardsURL           string                            `json:"boardsUrl"`
	MailruURL           string                            `json:"mailruUrl"`
	CupsonlineURL       string                            `json:"cupsonlineUrl"`
	TelemostURL         string                            `json:"telemostUrl"`
	AvitoURL            string                            `json:"avitoUrl"`
	AvitoCookies        string                            `json:"avitoCookies"`
	Bitrix24URL         string                            `json:"bitrix24Url"`
	Bitrix24Cookies     string                            `json:"bitrix24Cookies"`
	Bitrix24Sessid      string                            `json:"bitrix24Sessid"`
	Bitrix24ChatID      string                            `json:"bitrix24ChatId"`
	MixedURL            string                            `json:"mixedUrl"`
	Lanes               int                               `json:"lanes"`
	UpstreamProxy       string                            `json:"upstreamProxy"`
	TransportOptions    map[string]map[string]interface{} `json:"transportOptions,omitempty"`
	OnemeToken          string                            `json:"onemeToken"`
	OnemeUID            string                            `json:"onemeUid"`
	MaxPacketSize       int                               `json:"maxPacketSize"`
	YandexCookiesFile   string                            `json:"yandexCookiesFile"`
	ClientLimit         int                               `json:"clientLimit"`
	TrafficLimitBytes   int64                             `json:"trafficLimitBytes"`
	UploadBytes         int64                             `json:"uploadBytes"`
	DownloadBytes       int64                             `json:"downloadBytes"`
	UploadRate          float64                           `json:"uploadRateBytesPerSec"`
	DownloadRate        float64                           `json:"downloadRateBytesPerSec"`
	ConnectedClients    int                               `json:"connectedClients"`
	ClientStatus        string                            `json:"clientStatus,omitempty"`
	IsEnabled           bool                              `json:"isEnabled"`
	Status              int                               `json:"status"`
	LastStartedAt       *time.Time                        `json:"lastStartedAt"`
	LastStoppedAt       *time.Time                        `json:"lastStoppedAt"`
	CreatedAt           time.Time                         `json:"createdAt"`
	UpdatedAt           time.Time                         `json:"updatedAt"`
	ErrorMessage        string                            `json:"errorMessage"`
	ActiveTransport     string                            `json:"activeTransport"`
	Challenge           *ipc.CookiesRequestPayload        `json:"challenge,omitempty"`
	RestartAttempts     int                               `json:"restartAttempts"`
}

func DefaultTunnel() Tunnel {
	return Tunnel{Name: "Новый туннель", Role: ServerRole, Transport: "yandex", Inbound: "socks5", Socks5Address: "127.0.0.1:1080", Mode: "l4", Codec: "batched", Negotiate: false, EnableShare: true, MaxPacketSize: 65000, ScriptUpdateChannel: "stable", DirectListen: "0.0.0.0:8445"}
}

var known = map[string]bool{"yandex": true, "vyandex": true, "boards": true, "mailru": true, "cupsonline": true, "oneme": true, "telemost": true, "direct": true, "script": true, "avito": true, "bitrix24": true, "mixed": true}

func (t *Tunnel) types() ([]string, error) {
	if t.Transport != "multi" {
		if !known[t.Transport] {
			return nil, fmt.Errorf("неизвестный транспорт %q", t.Transport)
		}
		return []string{t.Transport}, nil
	}
	if strings.TrimSpace(t.Transports) == "" {
		return nil, errors.New("добавьте транспорты для multi-сессии")
	}
	var result []string
	seen := map[string]bool{}
	for _, part := range strings.Split(t.Transports, ",") {
		fields := strings.Split(strings.TrimSpace(part), ":")
		kind := fields[0]
		if !known[kind] || seen[kind] || len(fields) > 2 {
			return nil, fmt.Errorf("некорректный транспорт %q", part)
		}
		if len(fields) == 2 {
			n, e := strconv.Atoi(fields[1])
			if e != nil || n < 0 || n > 1000 {
				return nil, errors.New("приоритет должен быть от 0 до 1000")
			}
		}
		seen[kind] = true
		result = append(result, kind)
	}
	return result, nil
}

func (t *Tunnel) Validate() error {
	if strings.TrimSpace(t.Name) == "" || len(t.Name) > 200 {
		return errors.New("укажите название до 200 символов")
	}
	if t.Role != ServerRole {
		return ErrServerOnly
	}
	if t.Mode != "l3" && t.Mode != "l4" {
		return errors.New("режим должен быть l3 или l4")
	}
	if t.Codec != "batched" && t.Codec != "legacy" {
		return errors.New("неизвестный кодек")
	}
	types, err := t.types()
	if err != nil {
		return err
	}
	session := t.Negotiate || t.Transport == "multi"
	for _, kind := range types {
		if kind == "direct" || kind == "script" {
			session = true
		}
		if kind == "script" {
			b, e := hex.DecodeString(t.ScriptPublicKey)
			if e != nil || len(b) != 32 || t.ScriptPath == "" {
				return errors.New("для скрипта нужны путь .flux/.js и публичный ключ из 64 hex-символов")
			}
		}
	}
	if session && t.Codec == "legacy" {
		return errors.New("сессия требует batched; legacy доступен для одного classic-транспорта")
	}
	if (session || t.EncryptionKey != "") && utils.SecretChars(t.EncryptionKey) < utils.MinSecretChars {
		return errors.New("ключ шифрования должен содержать минимум 16 символов")
	}
	if t.MaxPacketSize < 1280 || t.MaxPacketSize > 65000 {
		return errors.New("размер пакета должен быть 1280–65000")
	}
	if t.Inbound != "socks5" && t.Inbound != "tun" {
		return errors.New("неизвестный входящий интерфейс")
	}
	if t.TrafficLimitBytes < 0 || t.ClientLimit < 0 || t.BenchBytes < 0 {
		return errors.New("лимиты не могут быть отрицательными")
	}
	if t.Lanes < 0 || t.Lanes > 64 {
		return errors.New("число каналов должно быть от 0 (авто) до 64")
	}
	if t.UpstreamProxy != "" && t.Mode != "l4" {
		return errors.New("исходящий SOCKS5-прокси доступен только в режиме l4")
	}
	for kind, options := range t.TransportOptions {
		if !known[kind] {
			return fmt.Errorf("параметры неизвестного транспорта %q", kind)
		}
		for key := range options {
			switch key {
			case "network", "stripes", "stripes_max", "lanes", "lane_rooms", "settings", "cookies", "sessid", "chat_id":
			default:
				return fmt.Errorf("параметр транспорта %s.%s не поддерживается панелью", kind, key)
			}
		}
		if network, ok := options["network"]; ok && network != "" && network != "wifi" && network != "cellular" && network != "ethernet" {
			return fmt.Errorf("неизвестная сеть транспорта %s", kind)
		}
		for _, key := range []string{"lanes", "lane_rooms", "stripes", "stripes_max"} {
			value, present := options[key]
			if !present || (key == "stripes" && value == "auto") {
				continue
			}
			number, err := strconv.Atoi(fmt.Sprint(value))
			if err != nil || number < 0 || number > 64 {
				return fmt.Errorf("%s.%s должен быть целым числом от 0 до 64", kind, key)
			}
		}
		if value, present := options["settings"]; present {
			if _, ok := value.(map[string]interface{}); !ok {
				return fmt.Errorf("%s.settings должен быть JSON-объектом", kind)
			}
		}
	}
	if t.ClientLimit > 0 {
		return errors.New("ядро не предоставляет точный счётчик клиентов: ограничение числа клиентов не поддерживается, используйте 0")
	}
	if t.ScriptUpdateChannel != "stable" && t.ScriptUpdateChannel != "nightly" {
		return errors.New("канал скриптов: stable или nightly")
	}
	_, err = SplitArgs(t.ExtraArgs)
	return err
}

// SplitArgs parses quoting without ever invoking a shell; Windows backslashes stay literal.
func SplitArgs(value string) ([]string, error) {
	var args []string
	var b strings.Builder
	var quote rune
	started := false
	chars := []rune(value)
	for i := 0; i < len(chars); i++ {
		c := chars[i]
		if c == '\\' && i+1 < len(chars) && (chars[i+1] == '\'' || chars[i+1] == '"') {
			i++
			b.WriteRune(chars[i])
			started = true
			continue
		}
		if quote != 0 {
			if c == quote {
				quote = 0
			} else {
				b.WriteRune(c)
			}
			continue
		}
		if c == '\'' || c == '"' {
			quote = c
			started = true
		} else if c == ' ' || c == '\t' || c == '\n' || c == '\r' {
			if started {
				args = append(args, b.String())
				b.Reset()
				started = false
			}
		} else {
			b.WriteRune(c)
			started = true
		}
	}
	if quote != 0 {
		return nil, errors.New("незакрытая кавычка в дополнительных аргументах")
	}
	if started {
		args = append(args, b.String())
	}
	for i, a := range args {
		name := strings.TrimLeft(strings.SplitN(a, "=", 2)[0], "-")
		switch name {
		case "role", "r", "client", "exit-node", "bench-send", "bench-sink":
			return nil, fmt.Errorf("%w; параметр --%s недоступен в дополнительных аргументах", ErrServerOnly, name)
		case "ipc-socket", "encryption-key-file", "cookie-store", "config", "node-wizard", "mode", "local-ip", "transport-options-file":
			return nil, fmt.Errorf("параметр --%s управляется панелью", name)
		}
		if a == "--debug" {
			args[i] = "--debug=2"
		}
	}
	return args, nil
}
