package terminal

import (
	"bufio"
	"fmt"
	"io"
	"os"
	"strconv"
	"strings"
	"unicode/utf8"
)

type UI struct {
	In                             *bufio.Reader
	Out                            io.Writer
	Language                       string
	Interactive, Color, FailedStep bool
}

func New(language string) *UI {
	return &UI{In: bufio.NewReader(os.Stdin), Out: os.Stdout, Language: language, Interactive: inputTerminal(), Color: os.Getenv("NO_COLOR") == "" && outputColor()}
}
func (u *UI) Text(ru, en string) string {
	if u.Language == "ru" {
		return ru
	}
	return en
}
func (u *UI) paint(code, value string) string {
	if u.Color {
		return "\x1b[" + code + "m" + value + "\x1b[0m"
	}
	return value
}
func (u *UI) read() string { line, _ := u.In.ReadString('\n'); return strings.TrimSpace(line) }
func (u *UI) Choice(title string, options []string, defaultIndex int) int {
	fmt.Fprintln(u.Out, u.paint("36", "  "+title+":"))
	for i, option := range options {
		fmt.Fprintf(u.Out, "    %d) %s\n", i+1, u.paint("97", option))
	}
	fmt.Fprintf(u.Out, "  %s [1-%d, default: %d]: ", u.Text("Выберите вариант", "Select option"), len(options), defaultIndex+1)
	value, e := strconv.Atoi(u.read())
	fmt.Fprintln(u.Out)
	if e == nil && value >= 1 && value <= len(options) {
		return value - 1
	}
	return defaultIndex
}
func (u *UI) AskText(prompt, defaultValue string) string {
	fmt.Fprint(u.Out, "  "+prompt)
	if defaultValue != "" {
		fmt.Fprintf(u.Out, " [default: %s]", defaultValue)
	}
	fmt.Fprint(u.Out, ": ")
	if value := u.read(); value != "" {
		return value
	}
	return defaultValue
}
func (u *UI) YesNo(question string, defaultYes bool) bool {
	hint := "[y/N, default: N]"
	if defaultYes {
		hint = "[Y/n, default: Y]"
	}
	fmt.Fprintf(u.Out, "  %s %s: ", question, hint)
	value := strings.ToLower(u.read())
	if value == "y" || value == "yes" || value == "да" {
		return true
	}
	if value == "n" || value == "no" || value == "нет" {
		return false
	}
	return defaultYes
}
func (u *UI) Step(index int, description string, action func() error) error {
	padding := max(1, 46-utf8.RuneCountInString(description))
	fmt.Fprintf(u.Out, "%s%s", u.paint("36", fmt.Sprintf("  [%d/3] ", index)), u.paint("97", description+strings.Repeat(" ", padding)))
	if e := action(); e != nil {
		u.FailedStep = true
		fmt.Fprintln(u.Out, u.paint("31", "[ERROR]"))
		fmt.Fprintln(u.Out, u.paint("33", "      -> "+e.Error()))
		return e
	}
	fmt.Fprintln(u.Out, u.paint("32", "[OK]"))
	return nil
}
func (u *UI) Info(value string) {
	fmt.Fprintln(u.Out)
	fmt.Fprintln(u.Out, u.paint("90", "      "+value))
}
func (u *UI) Error(value string) { fmt.Fprintln(u.Out, u.paint("31", "  [ERROR] "+value)) }

type Summary struct {
	PublicURL, LocalURL, Username, Password, Mode string
	Installed, Running                            bool
}

func (u *UI) Summary(s Summary) {
	title := u.Text("OpenFlux Zen Server — УСПЕШНО УСТАНОВЛЕН И ЗАПУЩЕН!", "OpenFlux Zen Server — SUCCESSFULLY INSTALLED AND STARTED!")
	color := "32"
	if !s.Running {
		title = u.Text("OpenFlux Zen Server — УСПЕШНО УСТАНОВЛЕН!", "OpenFlux Zen Server — SUCCESSFULLY INSTALLED!")
	}
	if !s.Installed {
		title = u.Text("OpenFlux Zen Server — УСТАНОВКА НЕ ЗАВЕРШЕНА", "OpenFlux Zen Server — INSTALLATION INCOMPLETE")
		color = "33"
	}
	fmt.Fprintln(u.Out)
	fmt.Fprintln(u.Out, u.paint(color, strings.Repeat("=", 62)))
	fmt.Fprintln(u.Out, u.paint(color, "  "+title))
	fmt.Fprintln(u.Out, u.paint(color, strings.Repeat("=", 62)))
	fmt.Fprintln(u.Out)
	field := func(label, value, valueColor string) {
		fmt.Fprintln(u.Out, u.paint("36", "  "+label)+u.paint(valueColor, value))
	}
	mainLabel := u.Text("Панель управления:\t", "Web Dashboard:\t")
	if s.Mode == "lan" || s.Mode == "local" {
		mainLabel = u.Text("Вход по LAN / Wi-Fi:\t", "LAN / Wi-Fi Access:\t")
	}
	field(mainLabel, s.PublicURL, "33")
	if s.PublicURL != s.LocalURL {
		field(u.Text("Этот ПК (localhost):\t", "This PC (localhost):\t"), s.LocalURL, "97")
	}
	field(u.Text("Логин:\t\t", "Username:\t"), s.Username, "97")
	password := s.Password
	if password == "" {
		password = u.Text("не сохранён для показа; повторите установку и введите прежний пароль", "not available for display; rerun the installer and enter the existing password")
	}
	field(u.Text("Пароль:\t\t", "Password:\t"), password, "97")
	fmt.Fprintln(u.Out)
	fmt.Fprintln(u.Out, u.paint(color, strings.Repeat("=", 62)))
	fmt.Fprintln(u.Out)
}
