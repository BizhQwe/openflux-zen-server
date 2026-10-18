package panel

import (
	"fmt"
	"os"
	"path/filepath"
	"strconv"
	"strings"

	"openflux.local/zen/panel/configuration"
)

func BuildArgs(t Tunnel, dir, socket string) ([]string, error) {
	if err := t.Validate(); err != nil {
		return nil, err
	}
	extra, err := SplitArgs(t.ExtraArgs)
	if err != nil {
		return nil, err
	}
	args := []string{"--role=" + ServerRole, "--mode=" + t.Mode, "--codec=" + t.Codec}
	add := func(name, value string) {
		if strings.TrimSpace(value) != "" {
			args = append(args, "--"+name+"="+value)
		}
	}
	flag := func(name string, enabled bool) {
		if enabled {
			args = append(args, "--"+name)
		}
	}
	types, _ := t.types()
	has := func(kind string) bool {
		for _, v := range types {
			if v == kind {
				return true
			}
		}
		return false
	}
	if t.Transport == "multi" {
		add("transports", t.Transports)
		add("yandex-url", first(t.YandexURL, t.URL))
		add("vyandex-url", t.VyandexURL)
		add("telemost-url", t.TelemostURL)
		add("boards-url", t.BoardsURL)
		add("mailru-url", t.MailruURL)
		add("cupsonline-url", t.CupsonlineURL)
		add("avito-url", t.AvitoURL)
		add("bitrix24-url", t.Bitrix24URL)
		add("mixed-url", t.MixedURL)
		add("oneme-token", first(t.OnemeToken, t.MaxToken))
		add("oneme-uid", first(t.OnemeUID, t.MaxUID))
	} else {
		add("transport", t.Transport)
		switch t.Transport {
		case "telemost":
			add("url", first(t.TelemostURL, t.URL))
		case "mailru":
			add("url", first(t.MailruURL, t.URL))
		case "cupsonline":
			add("url", first(t.CupsonlineURL, t.URL))
		case "yandex":
			add("url", first(t.YandexURL, t.URL))
		case "vyandex":
			add("url", first(t.VyandexURL, t.URL))
		case "boards":
			add("url", first(t.BoardsURL, t.URL))
		case "avito":
			add("url", first(t.AvitoURL, t.URL))
		case "bitrix24":
			add("url", first(t.Bitrix24URL, t.URL))
		case "mixed":
			add("url", first(t.MixedURL, t.URL))
		case "oneme":
			add("maxToken", first(t.OnemeToken, t.MaxToken))
			add("maxUid", first(t.OnemeUID, t.MaxUID))
		default:
			add("url", t.URL)
		}
	}
	add("avito-cookies", t.AvitoCookies)
	add("local-ip", t.LocalIP)
	add("upstream-proxy", t.UpstreamProxy)
	if t.Lanes > 0 && t.Transport != "multi" {
		add("lanes", strconv.Itoa(t.Lanes))
	}
	options := make(map[string]map[string]interface{}, len(t.TransportOptions))
	for kind, params := range t.TransportOptions {
		options[kind] = make(map[string]interface{}, len(params))
		for key, value := range params {
			options[kind][key] = value
		}
	}
	if has("bitrix24") {
		if options["bitrix24"] == nil {
			options["bitrix24"] = make(map[string]interface{})
		}
		for key, value := range map[string]string{"cookies": t.Bitrix24Cookies, "sessid": t.Bitrix24Sessid, "chat_id": t.Bitrix24ChatID} {
			if value != "" {
				options["bitrix24"][key] = value
			}
		}
	}
	if len(options) > 0 {
		path := filepath.Join(dir, "carrier-options", t.ID+".json")
		if err := os.MkdirAll(filepath.Dir(path), 0700); err != nil {
			return nil, err
		}
		if err := configuration.AtomicJSON(path, options); err != nil {
			return nil, err
		}
		add("transport-options-file", path)
	}
	add("session-context", t.SessionContext)
	add("max-packet-size", strconv.Itoa(t.MaxPacketSize))
	add("yandex-cookies-file", t.YandexCookiesFile)
	// Multi starts a Session on its own. Keep the upstream classic fallback
	// unless strict negotiation was requested or a Session-only carrier needs it.
	flag("negotiate", t.Negotiate || has("direct") || has("script"))
	if has("direct") {
		add("direct-listen", first(t.DirectListen, "0.0.0.0:8445"))
	}
	if has("script") {
		add("script-path", t.ScriptPath)
		add("script-pubkey", t.ScriptPublicKey)
		add("script-name", t.ScriptName)
		flag("script-auto-update", t.ScriptAutoUpdate)
		if t.ScriptAutoUpdate {
			add("script-update-channel", t.ScriptUpdateChannel)
		}
	}
	if t.EncryptionKey != "" {
		path := filepath.Join(dir, "keys", t.ID+".key")
		if err := os.MkdirAll(filepath.Dir(path), 0700); err != nil {
			return nil, err
		}
		if err := os.WriteFile(path, []byte(t.EncryptionKey), 0600); err != nil {
			return nil, err
		}
		add("encryption-key-file", path)
	}
	cookie := filepath.Join(dir, "cookies", t.ID+".json")
	if err := os.MkdirAll(filepath.Dir(cookie), 0700); err != nil {
		return nil, err
	}
	add("cookie-store", cookie)
	if t.EnableShare {
		flag("share", true)
		add("share-host", t.ShareHost)
	}
	add("ipc-socket", socket)
	debug := false
	for _, a := range extra {
		if strings.HasPrefix(a, "--debug") || strings.HasPrefix(a, "-d") {
			debug = true
		}
	}
	if !debug {
		add("debug", "0")
	}
	args = append(args, extra...)
	return args, nil
}
func first(values ...string) string {
	for _, v := range values {
		if v != "" {
			return v
		}
	}
	return ""
}
func coreMissing(path string) error {
	return fmt.Errorf("ядро OpenFlux не найдено: %s; выполните scripts/build или задайте --core", path)
}
