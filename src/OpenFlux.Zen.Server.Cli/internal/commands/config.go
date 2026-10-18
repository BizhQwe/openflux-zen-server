package commands

import (
	"errors"
	"fmt"
	"os"
	"strings"

	"openflux.local/zen/panel/configuration"
	"openflux.local/zen/panel/publication"
)

func (a *App) panelURLs(s configuration.Settings) map[string]string {
	urls := configuration.PanelURLs(s)
	if s.LocaltunnelEnabled {
		var status publication.Status
		if e := (&configuration.Store{Dir: a.Options.DataDir}).Read("publication.json", &status); e == nil && status.URL != "" {
			urls["localtunnel"] = strings.TrimSuffix(status.URL, "/") + "/" + s.SecretPath + "/"
		}
	}
	return urls
}

func (a *App) printURLs(s configuration.Settings) {
	urls := a.panelURLs(s)
	for _, kind := range []string{"localtunnel", "domain", "lan", "localhost"} {
		if value := urls[kind]; value != "" {
			label := map[string]string{"lan": a.text("Вход по LAN / Wi-Fi:", "LAN / Wi-Fi:"), "localhost": a.text("Этот ПК (localhost):", "This PC (localhost):"), "domain": a.text("Домен:", "Domain:"), "localtunnel": "Localtunnel:"}[kind]
			a.field(label, value)
		}
	}
}

func (a *App) credentials() error {
	s, e := configuration.ReadSettings(a.Options.DataDir)
	if e != nil {
		if os.IsPermission(e) {
			return ErrAdmin
		}
		return e
	}
	password, readErr := configuration.ReadDisplayPassword(a.Options.DataDir, s)
	if readErr != nil && !errors.Is(readErr, os.ErrNotExist) && !errors.Is(readErr, configuration.ErrPasswordUnavailable) {
		return readErr
	}
	if a.Options.JSON {
		return a.emit(map[string]any{"username": s.Username, "password": password, "passwordAvailable": password != "", "secretPath": s.SecretPath, "urls": a.panelURLs(s), "passwordStoredAsHash": true})
	}
	a.header(a.text("OpenFlux Zen Server — Данные для входа", "OpenFlux Zen Server — Login details"))
	a.printURLs(s)
	a.field(a.text("Логин:", "Username:"), s.Username)
	if password != "" {
		a.field(a.text("Пароль:", "Password:"), password)
	} else {
		fmt.Fprintln(a.Out, a.text("  В старой установке сохранён только хеш пароля. Запустите установщик повторно и введите прежний пароль, чтобы CLI мог его показывать.", "  The old installation only stored a password hash. Run the installer again and enter the existing password to make it available in the CLI."))
	}
	fmt.Fprintln(a.Out)
	a.divider()
	return nil
}
