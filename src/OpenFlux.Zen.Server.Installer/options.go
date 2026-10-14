package main

import (
	"errors"
	"net"
	"strconv"
	"strings"

	"openflux.local/zen/installer/internal/terminal"
	"openflux.local/zen/panel/configuration"
)

type installOptions struct {
	Listen, Mode, Domain, Language                     string
	Port                                               int
	NoService, Silent, LanguageSet, ModeSet, DomainSet bool
	Autostart                                          bool
	AutostartSet                                       bool
}

func publishMode(s configuration.Settings) string {
	if s.LocaltunnelEnabled {
		return "localtunnel"
	}
	if s.DomainEnabled {
		return "domain"
	}
	if configuration.LANEnabled(s) {
		return "lan"
	}
	return "localhost"
}
func (o *installOptions) wizard(u *terminal.UI, previous configuration.Settings, fresh bool) error {
	if o.Language == "" {
		o.Language = previous.Language
	}
	if o.Language == "" {
		o.Language = "en"
	}
	if o.Language != "en" && o.Language != "ru" {
		return errors.New("--lang must be ru or en")
	}
	u.Language = o.Language
	interactive := u.Interactive && !o.Silent
	if interactive && !o.LanguageSet {
		fmtDefault := 0
		if o.Language == "ru" {
			fmtDefault = 1
		}
		choice := u.Choice("Language / Язык", []string{"English", "Русский"}, fmtDefault)
		o.Language = []string{"en", "ru"}[choice]
		u.Language = o.Language
		o.LanguageSet = true
	}
	if interactive && !o.ModeSet {
		modes := []string{"localhost", "lan", "localtunnel", "domain"}
		defaultIndex := 3
		if !fresh {
			for i, mode := range modes {
				if mode == publishMode(previous) {
					defaultIndex = i
				}
			}
		}
		options := []string{
			u.Text("Только на этом ПК (localhost)", "Only on this PC (localhost)"),
			u.Text("Через локальную сеть (LAN / Wi-Fi)", "Local network (LAN / Wi-Fi)"),
			u.Text("Через Localtunnel (без публичного IP)", "Via Localtunnel (no public IP required)"),
			u.Text("Прямое подключение (домен / публичный IP)", "Direct connection (domain / public IP)"),
		}
		o.Mode = modes[u.Choice(u.Text("Сетевое размещение", "Network accessibility"), options, defaultIndex)]
		o.ModeSet = true
	}
	if interactive && o.Mode == "domain" && !o.DomainSet {
		o.Domain = u.AskText(u.Text("Домен без схемы — прямой доступ; полный URL — готовый reverse proxy (Enter — без домена)", "Hostname for direct access, full URL for an existing reverse proxy (Enter for no domain)"), previous.Domain)
		o.DomainSet = true
	}
	if interactive && !o.NoService && !o.AutostartSet {
		o.Autostart = u.YesNo(u.Text("Включить автозапуск службы при загрузке системы?", "Enable server autostart on system boot?"), true)
	}
	return nil
}
func (o installOptions) apply(s *configuration.Settings) error {
	if o.ModeSet {
		s.LocaltunnelEnabled = false
		s.DomainEnabled = false
		switch strings.ToLower(o.Mode) {
		case "domain", "direct", "lan", "local":
			configuration.SetLAN(s, true)
		case "localtunnel":
			s.LocaltunnelEnabled = true
			configuration.SetLAN(s, true)
		case "localhost":
			configuration.SetLAN(s, false)
		default:
			return errors.New("--mode must be domain, localtunnel, lan or localhost")
		}
	}
	// An explicit listen address takes priority over the wizard's host choice.
	if o.Listen != "" {
		s.Listen = o.Listen
	}
	if o.Port != 0 {
		if o.Port < 1 || o.Port > 65535 {
			return errors.New("--port must be between 1 and 65535")
		}
		host, _, e := net.SplitHostPort(s.Listen)
		if e != nil {
			return e
		}
		s.Listen = net.JoinHostPort(host, strconv.Itoa(o.Port))
	}
	if o.DomainSet {
		s.Domain = strings.TrimSpace(o.Domain)
		s.DomainEnabled = s.Domain != ""
		if s.DomainEnabled {
			_, e := configuration.DomainAddress(s.Domain)
			if e != nil {
				return e
			}
		}
	}
	if o.LanguageSet {
		s.Language = o.Language
	}
	return configuration.ValidateSettings(*s)
}
