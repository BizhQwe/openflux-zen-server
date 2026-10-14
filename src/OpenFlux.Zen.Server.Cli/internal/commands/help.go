package commands

import (
	"fmt"
	"os"
	"runtime"
	"strings"

	"openflux.local/zen/cli/internal/platform"
)

func (a *App) colored(color, text string) string {
	if a.Out == os.Stdout && os.Getenv("NO_COLOR") == "" && platform.ConsoleColor() {
		return "\x1b[" + color + "m" + text + "\x1b[0m"
	}
	return text
}
func (a *App) divider() { fmt.Fprintln(a.Out, a.colored("36", strings.Repeat("=", 62))) }
func (a *App) header(title string) {
	fmt.Fprintln(a.Out)
	a.divider()
	fmt.Fprintln(a.Out, a.colored("36", "  "+title))
	a.divider()
	fmt.Fprintln(a.Out)
}
func (a *App) notice(kind, color, message string) {
	if !a.Options.JSON {
		fmt.Fprintf(a.Out, "  %s %s\n", a.colored(color, "["+kind+"]"), message)
	}
}
func (a *App) field(label, value string) {
	fmt.Fprintf(a.Out, "  %s%s\n", a.colored("36", label+strings.Repeat(" ", max(1, 26-len([]rune(label))))), value)
}
func (a *App) help() error {
	a.header(a.text("OpenFlux Zen Server — Управление сервером (CLI)", "OpenFlux Zen Server — CLI Management Tool"))
	fmt.Fprintln(a.Out, a.colored("33", a.text("  Использование: openflux <команда>", "  Usage: openflux <command>")))
	fmt.Fprintln(a.Out)
	fmt.Fprintln(a.Out, a.colored("36", a.text("  Команды управления службой:", "  Service Management Commands:")))
	item := func(command, ru, en string) {
		fmt.Fprintf(a.Out, "    %s%s\n", a.colored("32", fmt.Sprintf("%-14s", command)), a.text(ru, en))
	}
	item("start", "Запустить службу сервера", "Start the server service")
	item("stop", "Остановить службу сервера", "Stop the server service")
	item("restart", "Перезапустить службу сервера", "Restart the server service")
	item("status", "Показать статус сервера и службы", "Show server and service status")
	item("autostart", "Автозапуск при загрузке системы (enable | disable | status)", "Autostart on system boot (enable | disable | status)")
	fmt.Fprintln(a.Out)
	fmt.Fprintln(a.Out, a.colored("36", a.text("  Основные команды:", "  General Commands:")))
	item("credentials", "Показать данные входа (логин, пароль, ссылки)", "Display login details (username, password, URLs)")
	item("uninstall", "Полностью удалить сервер, настройки, туннели и логи", "Completely remove the server, settings, tunnels and logs")
	item("help", "Показать эту справку", "Show this help")
	fmt.Fprintln(a.Out)
	fmt.Fprintln(a.Out, a.colored("36", a.text("  Примеры:", "  Examples:")))
	for _, command := range []string{"openflux status", "sudo openflux credentials", "sudo openflux restart", "sudo openflux autostart enable", "sudo openflux autostart disable", "sudo openflux uninstall -y"} {
		if runtime.GOOS == "windows" {
			command = strings.TrimPrefix(command, "sudo ")
		}
		fmt.Fprintf(a.Out, "    $ %s\n", a.colored("33", command))
	}
	fmt.Fprintln(a.Out)
	a.divider()
	fmt.Fprintln(a.Out)
	return nil
}
