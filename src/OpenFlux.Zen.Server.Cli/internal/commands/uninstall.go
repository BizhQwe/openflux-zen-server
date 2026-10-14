package commands

import (
	"bufio"
	"errors"
	"fmt"
	"openflux.local/zen/cli/internal/platform"
	"openflux.local/zen/panel/configuration"
	"openflux.local/zen/panel/l3guard"
	"path/filepath"
	"strings"
)

func (a *App) uninstall() error {
	marker, e := configuration.ReadInstallation(a.Options.Dir)
	if e != nil {
		return fmt.Errorf("отказ удалять неподтверждённый каталог: %w", e)
	}
	if !configuration.SamePath(a.Options.DataDir, filepath.Join(a.Options.Dir, "data")) {
		return errors.New("для uninstall используйте стандартный data внутри каталога установки")
	}
	s, e := a.serviceStatus()
	if e != nil {
		return e
	}
	if s.Registered {
		if !configuration.SamePath(s.Executable, platform.PanelPath(a.Options.Dir)) {
			return errors.New("служба принадлежит другой установке")
		}
	}
	if (s.Registered || marker.PathAdded) && !a.IsAdmin() {
		return ErrAdmin
	}
	if !a.Options.Yes {
		if a.Options.JSON {
			return errors.New(a.text("для удаления с --json требуется --yes", "uninstall with --json requires --yes"))
		}
		question := a.text("Вы уверены, что хотите полностью удалить OpenFlux Zen Server из системы (все службы, настройки, туннели и файлы)?", "Are you sure you want to completely uninstall OpenFlux Zen Server from system (all services, settings, tunnels and files)?")
		fmt.Fprint(a.Out, a.colored("33", "  "+question+" [y/N, default: N]: "))
		line, _ := bufio.NewReader(a.In).ReadString('\n')
		answer := strings.ToLower(strings.TrimSpace(line))
		if answer != "y" && answer != "yes" && answer != "да" {
			a.notice("WARN", "33", a.text("Удаление отменено.", "Uninstallation cancelled."))
			return nil
		}
	}
	if !a.Options.JSON {
		a.header(a.text("OpenFlux Zen Server — Полное удаление", "OpenFlux Zen Server — Full Uninstallation"))
	}
	a.notice("INFO", "36", a.text("Остановка служб и завершение фоновых процессов...", "Stopping OpenFlux Zen Server services and processes..."))
	if s.Registered {
		if s.Running {
			if e = a.Service.Stop(a.Options.Dir); e != nil {
				return e
			}
		}
	}
	unlock, e := configuration.Lock(a.Options.DataDir)
	if e != nil {
		return fmt.Errorf("остановите панель перед удалением: %w", e)
	}
	if s.Registered {
		if e = a.Service.Remove(a.Options.Dir); e != nil {
			_ = unlock()
			return e
		}
	}
	if e = l3guard.Cleanup(a.Options.DataDir); e != nil {
		_ = unlock()
		return e
	}
	if e = unlock(); e != nil {
		return e
	}
	a.notice("INFO", "36", a.text("Удаление файлов, конфигураций и баз данных...", "Completely removing application directories and data..."))
	if e = platform.RemovePath(a.Options.Dir, marker.PathAdded); e != nil {
		return e
	}
	if e = platform.RemoveInstallation(a.Options.Dir); e != nil {
		return e
	}
	if a.Options.JSON {
		return a.emit(map[string]any{"success": true, "dataPreserved": false})
	}
	fmt.Fprintln(a.Out)
	a.notice("OK", "32", a.text("OpenFlux Zen Server полностью удалён из системы без каких-либо остатков.", "OpenFlux Zen Server has been completely uninstalled from the system without any traces left."))
	fmt.Fprintln(a.Out)
	a.divider()
	fmt.Fprintln(a.Out)
	return nil
}
