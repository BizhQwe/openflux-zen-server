package commands

import (
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"openflux.local/zen/cli/internal/platform"
	"openflux.local/zen/panel/configuration"
	"os"
	"path/filepath"
)

const Version = "1.0.0"

var ErrAdmin = errors.New("запустите openflux через sudo (Linux) или PowerShell от имени администратора (Windows)")

type App struct {
	In      io.Reader
	Out     io.Writer
	Service platform.Service
	IsAdmin func() bool
	Options Options
}

func New() *App {
	return &App{In: os.Stdin, Out: os.Stdout, Service: platform.New(), IsAdmin: platform.IsAdmin}
}
func (a *App) text(ru, en string) string {
	if a.Options.Language == "en" {
		return en
	}
	return ru
}
func (a *App) emit(value any) error {
	encoder := json.NewEncoder(a.Out)
	encoder.SetIndent("", "  ")
	return encoder.Encode(value)
}
func (a *App) Run(args []string) error {
	o, e := Parse(args, ExecutableDir())
	if e != nil {
		return e
	}
	a.Options = o
	if o.Language == "" {
		o.Language = "ru"
		if s, e := configuration.ReadSettings(o.DataDir); e == nil && s.Language == "en" {
			o.Language = "en"
		}
		if language := os.Getenv("OPENFLUX_LANGUAGE"); language == "ru" || language == "en" {
			o.Language = language
		}
		a.Options = o
	}
	command := "help"
	rest := []string{}
	if len(o.Args) > 0 {
		command = commandName(o.Args[0])
		rest = o.Args[1:]
	}
	switch command {
	case "help":
		return a.help()
	case "version":
		if o.JSON {
			return a.emit(map[string]string{"version": Version})
		}
		fmt.Fprintln(a.Out, "OpenFlux Zen Server "+Version)
		return nil
	case "status":
		if len(rest) > 0 {
			return errors.New("usage: openflux status")
		}
		return a.status()
	case "start", "stop", "restart":
		if len(rest) > 0 {
			return fmt.Errorf("usage: openflux %s", command)
		}
		if o.NoService {
			return errors.New("--no-service недоступен для управления службой")
		}
		if !a.IsAdmin() {
			return ErrAdmin
		}
		if command == "stop" || command == "restart" {
			if e = a.Service.Stop(o.Dir); e != nil {
				return e
			}
		}
		if command == "start" || command == "restart" {
			if e = a.Service.Start(o.Dir); e != nil {
				return e
			}
		}
		if o.JSON {
			return a.emit(map[string]any{"success": true, "command": command})
		}
		fmt.Fprintln(a.Out, a.text("Команда выполнена: ", "Completed: ")+command)
		return nil
	case "autostart":
		return a.autostart(rest)
	case "credentials":
		if len(rest) > 0 {
			return errors.New("usage: openflux credentials")
		}
		return a.credentials()
	case "uninstall":
		if len(rest) > 0 {
			return errors.New("usage: openflux uninstall [-y]")
		}
		return a.uninstall()
	case "__path":
		if len(rest) != 1 || rest[0] != "install" {
			return errors.New("invalid internal command")
		}
		if !a.IsAdmin() {
			return ErrAdmin
		}
		marker, e := configuration.ReadInstallation(o.Dir)
		if e != nil {
			return e
		}
		added, e := platform.RegisterPath(o.Dir)
		if e != nil {
			return e
		}
		marker.PathAdded = marker.PathAdded || added
		return configuration.AtomicJSON(filepath.Join(o.Dir, configuration.MarkerName), marker)
	default:
		return fmt.Errorf("%s: %s", a.text("Неизвестная команда", "Unknown command"), command)
	}
}
func (a *App) serviceStatus() (platform.Status, error) {
	if a.Options.NoService {
		return platform.Status{}, nil
	}
	return a.Service.Status(a.Options.Dir)
}
func (a *App) status() error {
	s, e := a.serviceStatus()
	if e != nil {
		return e
	}
	settings, configErr := configuration.ReadSettings(a.Options.DataDir)
	if configErr != nil && !os.IsNotExist(configErr) && !os.IsPermission(configErr) {
		return configErr
	}
	result := map[string]any{"version": Version, "dir": a.Options.Dir, "service": s, "installed": configErr == nil || os.IsPermission(configErr)}
	if configErr == nil {
		result["urls"] = configuration.PanelURLs(settings)
	}
	if a.Options.JSON {
		return a.emit(result)
	}
	fmt.Fprintln(a.Out, "OpenFlux Zen Server "+Version)
	fmt.Fprintf(a.Out, "%s: %s\n", a.text("Каталог", "Directory"), a.Options.Dir)
	fmt.Fprintf(a.Out, "%s: %t; %s: %t; %s: %t\n", a.text("Служба зарегистрирована", "Registered"), s.Registered, a.text("Работает", "Running"), s.Running, a.text("Автозапуск", "Autostart"), s.Enabled)
	if configErr == nil {
		a.printURLs(settings)
	}
	return nil
}
func (a *App) autostart(args []string) error {
	if len(args) == 0 || (len(args) == 1 && args[0] == "status") {
		return a.status()
	}
	if len(args) != 1 {
		return errors.New("usage: openflux autostart enable|disable|status")
	}
	on, e := boolean(args[0])
	if e != nil {
		return e
	}
	if a.Options.NoService {
		return errors.New("--no-service недоступен для автозапуска")
	}
	if !a.IsAdmin() {
		return ErrAdmin
	}
	if e = a.Service.Enable(a.Options.Dir, on); e != nil {
		return e
	}
	if a.Options.JSON {
		return a.emit(map[string]any{"success": true, "enabled": on})
	}
	fmt.Fprintf(a.Out, "%s: %t\n", a.text("Автозапуск", "Autostart"), on)
	return nil
}
