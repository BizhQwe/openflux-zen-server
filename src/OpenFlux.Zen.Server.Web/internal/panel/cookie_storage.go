package panel

import (
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"strings"

	"github.com/p1neappleXpress/OpenFlux/transport"
	"openflux.local/zen/panel/configuration"
)

// Use the effective launch flags so URL overrides have the same cookie key
// as the core. BuildArgs prepares private files, but never starts a process.
func cookieSessionKey(t Tunnel, dir, target string) (string, error) {
	args, err := BuildArgs(t, dir, "")
	if err != nil {
		return "", err
	}
	flags := map[string]string{"url": transport.ContextPlaceholder}
	for i := 0; i < len(args); i++ {
		arg := args[i]
		if !strings.HasPrefix(arg, "-") {
			continue
		}
		name, value, inline := strings.Cut(strings.TrimLeft(arg, "-"), "=")
		if name == "u" {
			name = "url"
		}
		if name != "url" && name != "script-name" && !strings.HasSuffix(name, "-url") {
			continue
		}
		if !inline && i+1 < len(args) {
			i++
			value = args[i]
		}
		flags[name] = value
	}
	doc := flags["url"]
	if t.Transport == "multi" {
		doc = flags[target+"-url"]
		if target == "yandex" && doc == "" && flags["url"] != transport.ContextPlaceholder {
			doc = flags["url"]
		}
	}
	if target == "script" {
		return "script:" + flags["script-name"] + " " + doc, nil
	}
	return doc, nil
}

// Called with m.op held and no running core, so the core cannot concurrently
// rewrite its in-memory store over a newly saved jar.
func (m *Manager) saveStoppedCookies(t Tunnel, target string, jar map[string]string) error {
	if len(jar) == 0 {
		return errors.New("cookies не заданы")
	}
	legacyKey, err := cookieSessionKey(t, m.store.Dir, target)
	if err != nil {
		return err
	}
	path := filepath.Join(m.store.Dir, "cookies", t.ID+".json")
	all := make(map[string]map[string]string)
	data, err := os.ReadFile(path)
	if err != nil && !os.IsNotExist(err) {
		return fmt.Errorf("не удалось прочитать сохранённые cookies: %w", err)
	}
	if len(data) > 0 {
		if err := json.Unmarshal(data, &all); err != nil {
			return errors.New("файл cookies повреждён: сбросьте cookies перед сохранением новых")
		}
	}
	if all == nil {
		all = make(map[string]map[string]string)
	}
	key := transport.TransportCookieKey(target, legacyKey)
	kept := all[key]
	if kept == nil && legacyKey != "" {
		kept = all[legacyKey]
	}
	merged := make(map[string]string, len(kept)+len(jar))
	for name, value := range kept {
		merged[name] = value
	}
	for name, value := range jar {
		merged[name] = value
	}
	all[key] = merged
	return configuration.AtomicJSON(path, all)
}
