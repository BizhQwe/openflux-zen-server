package commands

import (
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"strings"
)

type Options struct {
	Dir, DataDir, Language string
	JSON, NoService, Yes   bool
	Args                   []string
}

func Parse(args []string, defaultDir string) (Options, error) {
	o := Options{Dir: defaultDir}
	for i := 0; i < len(args); i++ {
		a := args[i]
		name, value, inline := strings.Cut(a, "=")
		switch name {
		case "--dir", "--data-dir", "--lang", "-l":
			if !inline {
				i++
				if i >= len(args) {
					return o, fmt.Errorf("для %s нужно значение", name)
				}
				value = args[i]
			}
			if value == "" {
				return o, fmt.Errorf("пустое значение %s", name)
			}
			switch name {
			case "--dir":
				o.Dir = value
			case "--data-dir":
				o.DataDir = value
			default:
				o.Language = value
			}
		case "--json":
			o.JSON = true
		case "--no-service":
			o.NoService = true
		case "--yes", "-y", "--force":
			o.Yes = true
		case "--purge":
			// Compatibility with the previous Go CLI: uninstall is always full.
		default:
			o.Args = append(o.Args, a)
		}
	}
	if o.Language != "" && o.Language != "ru" && o.Language != "en" {
		return o, errors.New("язык должен быть ru или en")
	}
	var e error
	o.Dir, e = filepath.Abs(o.Dir)
	if e != nil {
		return o, e
	}
	if o.DataDir == "" {
		o.DataDir = filepath.Join(o.Dir, "data")
	}
	o.DataDir, e = filepath.Abs(o.DataDir)
	if e != nil {
		return o, e
	}
	return o, nil
}
func ExecutableDir() string {
	exe, e := os.Executable()
	if e != nil {
		return "."
	}
	if resolved, e := filepath.EvalSymlinks(exe); e == nil {
		exe = resolved
	}
	return filepath.Dir(exe)
}
func commandName(value string) string {
	v := strings.ToLower(strings.TrimLeft(value, "-/"))
	aliases := map[string]string{"h": "help", "?": "help", "помощь": "help", "справка": "help", "up": "start", "run": "start", "старт": "start", "запуск": "start", "down": "stop", "стоп": "stop", "остановить": "stop", "reboot": "restart", "рестарт": "restart", "перезапуск": "restart", "info": "status", "статус": "status", "инфо": "status", "autorun": "autostart", "автозапуск": "autostart", "net": "network", "placement": "network", "сеть": "network", "размещение": "network", "cred": "credentials", "creds": "credentials", "пароль": "credentials", "пароли": "credentials", "доступ": "credentials", "учетка": "credentials", "reset-password": "reset", "сброс": "reset", "сбросить": "reset", "remove": "uninstall", "delete": "uninstall", "удалить": "uninstall", "деинсталляция": "uninstall"}
	if name, ok := aliases[v]; ok {
		return name
	}
	return v
}
func boolean(value string) (bool, error) {
	switch strings.ToLower(value) {
	case "on", "enable", "1", "true", "вкл", "включить", "yes", "да":
		return true, nil
	case "off", "disable", "0", "false", "выкл", "выключить", "no", "нет":
		return false, nil
	}
	return false, errors.New("используйте on|off (enable|disable)")
}
