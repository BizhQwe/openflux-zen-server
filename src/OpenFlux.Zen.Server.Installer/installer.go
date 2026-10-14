package main

import (
	"context"
	"errors"
	"flag"
	"fmt"
	"net"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strconv"
	"strings"
	"time"

	"openflux.local/zen/installer/internal/terminal"
	"openflux.local/zen/panel/configuration"
	"openflux.local/zen/panel/publication"
)

func bootstrapPassword(output string) string {
	for _, line := range strings.Split(output, "\n") {
		if value, ok := strings.CutPrefix(strings.TrimSpace(line), "Password: "); ok && !strings.HasPrefix(value, "stored as a hash") {
			return strings.TrimSpace(value)
		}
	}
	return ""
}
func runInstalled(dir string, args ...string) error {
	command := exec.Command(filepath.Join(dir, cliName()), args...)
	hideProcess(command)
	if output, e := command.CombinedOutput(); e != nil {
		return fmt.Errorf("%s: %s: %w", args[0], strings.TrimSpace(string(output)), e)
	}
	return nil
}
func accessSummary(settings configuration.Settings, data, password string, installed, running bool) terminal.Summary {
	urls := configuration.PanelURLs(settings)
	public := urls["localhost"]
	if urls["lan"] != "" {
		public = urls["lan"]
	}
	if urls["domain"] != "" {
		public = urls["domain"]
	}
	if settings.LocaltunnelEnabled {
		var status publication.Status
		if (&configuration.Store{Dir: data}).Read("publication.json", &status) == nil && status.State == "connected" && status.URL != "" {
			public = strings.TrimSuffix(status.URL, "/") + "/" + settings.SecretPath + "/"
		}
	}
	return terminal.Summary{PublicURL: public, LocalURL: urls["localhost"], Username: settings.Username, Password: password, Mode: publishMode(settings), Installed: installed, Running: running}
}
func run() (result error) {
	u := terminal.New("en")
	var initialized *configuration.Settings
	var password, data string
	defer func() {
		if result == nil {
			return
		}
		if !u.FailedStep {
			u.Error(result.Error())
		}
		// If a later step fails, still show a newly generated password once. It
		// cannot be recovered from settings.json on the next installation attempt.
		if initialized != nil && password != "" {
			u.Info(u.Text("Сохраните данные доступа. После исправления повторите установку.", "Save these access details. Retry installation after fixing the error."))
			u.Summary(accessSummary(*initialized, data, password, false, false))
		}
	}()
	dirFlag := flag.String("dir", defaultInstallDir(), "Installation directory")
	listen := flag.String("listen", "", "Panel host:port; first installation defaults to 0.0.0.0:5000")
	noService := flag.Bool("no-service", false, "Install files and credentials without registering or starting a service")
	verify := flag.Bool("verify", false, "Verify the embedded payload and exit")
	reset := flag.Bool("reset-password", false, "Generate a new password during installation")
	lang := flag.String("lang", os.Getenv("OPENFLUX_LANGUAGE"), "Installer language: ru or en")
	silent := flag.Bool("silent", false, "Skip interactive questions")
	mode := flag.String("mode", "", "Network placement: domain, localtunnel, lan, localhost")
	domain := flag.String("domain", "", "Public domain / reverse proxy URL")
	port := flag.Int("port", 0, "Panel port (1-65535)")
	autostart := flag.String("autostart", "", "Boot autostart: on or off")
	flag.Parse()
	if *lang == "ru" {
		u.Language = "ru"
	}
	if flag.NArg() > 1 || (flag.NArg() == 1 && flag.Arg(0) != "install") {
		return errors.New("usage: installer [options] [install]")
	}
	raw, e := payload.ReadFile("payload/bundle.zip")
	if e != nil {
		return errors.New("payload is not bundled; build the installer with scripts/build")
	}
	bundle, e := readBundle(raw)
	if e != nil {
		return e
	}
	if *verify {
		fmt.Printf("Verified OpenFlux Zen Server %s %s/%s\n", bundle.Manifest.Version, bundle.Manifest.OS, bundle.Manifest.Arch)
		return nil
	}
	dir, e := filepath.Abs(*dirFlag)
	if e != nil {
		return e
	}
	if dir == filepath.VolumeName(dir)+string(os.PathSeparator) {
		return errors.New("refusing to install in a filesystem root")
	}
	if !*noService {
		if e = preflightService(); e != nil {
			return e
		}
		if runtime.GOOS == "linux" {
			if _, e = systemdUnit(dir); e != nil {
				return e
			}
		}
	}
	data = filepath.Join(dir, "data")
	previous, e := configuration.ReadSettings(data)
	fresh := os.IsNotExist(e)
	if e != nil && !fresh {
		return e
	}
	if fresh {
		previous = configuration.Settings{Username: "admin", SecretPath: "preview-settings", Listen: "0.0.0.0:5000", Language: "en", AutoStartEnabled: true}
	}
	opts := installOptions{Listen: *listen, NoService: *noService, Silent: *silent, Mode: *mode, ModeSet: *mode != "", Domain: *domain, Language: *lang, LanguageSet: *lang != "", Port: *port, Autostart: true, AutostartSet: *autostart != ""}
	var portSet bool
	flag.Visit(func(f *flag.Flag) {
		if f.Name == "domain" {
			opts.DomainSet = true
		}
		if f.Name == "port" {
			portSet = true
		}
	})
	if portSet && (*port < 1 || *port > 65535) {
		return errors.New("--port must be between 1 and 65535")
	}
	if opts.DomainSet && !opts.ModeSet {
		opts.Mode, opts.ModeSet = "domain", true
	}
	if opts.AutostartSet {
		switch strings.ToLower(*autostart) {
		case "on", "true", "1", "enable":
			opts.Autostart = true
		case "off", "false", "0", "disable":
			opts.Autostart = false
		default:
			return errors.New("--autostart must be on or off")
		}
	}
	fmt.Fprintln(u.Out)
	if e = opts.wizard(u, previous, fresh); e != nil {
		return e
	}
	if fresh {
		opts.LanguageSet = true
	}
	var rememberedPassword string
	if !fresh && !*reset {
		rememberedPassword, _ = configuration.ReadDisplayPassword(data, previous)
		if rememberedPassword == "" {
			// No display password saved in previous installation (e.g. from an older version that only stored a hash,
			// or missing/corrupted .credentials). Automatically generate a fresh password without prompting for an old one.
			*reset = true
		}
	}
	preview := previous
	if e = opts.apply(&preview); e != nil {
		return e
	}
	if fresh {
		previousListen := preview.Listen
		if e = freshPanelPort(&preview, opts.Listen != "" || opts.Port != 0); e != nil {
			return e
		}
		if preview.Listen != previousListen {
			_, port, _ := net.SplitHostPort(preview.Listen)
			opts.Port, _ = strconv.Atoi(port)
			u.Info(u.Text("Стандартный порт занят; выбран свободный адрес: ", "Default port is busy; selected a free address: ") + preview.Listen)
		}
	}
	fmt.Fprintln(u.Out)
	var unlock func() error
	defer func() {
		if unlock != nil {
			_ = unlock()
		}
	}()
	if e = u.Step(1, u.Text("Распаковка компонентов сервера...", "Unpacking server components..."), func() error {
		if !opts.NoService {
			if err := stopService(); err != nil {
				return err
			}
		}
		if err := protectData(data); err != nil {
			return err
		}
		var err error
		unlock, err = configuration.Lock(data)
		if err != nil {
			return fmt.Errorf("stop the running panel before updating: %w", err)
		}
		return installFiles(dir, bundle)
	}); e != nil {
		return e
	}
	if e = u.Step(2, u.Text("Настройка службы и окружения...", "Configuring system service..."), func() error {
		_, web := executableNames()
		args := []string{"--init", "--data-dir", data}
		if *reset {
			args = append(args, "--reset-password")
		}
		if fresh {
			args = append(args, "--listen", preview.Listen)
		}
		cmd := exec.Command(filepath.Join(dir, web), args...)
		hideProcess(cmd)
		output, err := cmd.CombinedOutput()
		if err != nil {
			return fmt.Errorf("initialize panel: %s: %w", strings.TrimSpace(string(output)), err)
		}
		password = bootstrapPassword(string(output))
		settings, err := configuration.ReadSettings(data)
		if err != nil {
			return err
		}
		initialized = &settings
		if cached, cacheErr := configuration.ReadDisplayPassword(data, settings); cacheErr == nil {
			password = cached
		}
		next := settings
		if err = opts.apply(&next); err != nil {
			return err
		}
		if next != settings {
			if err = configuration.AtomicJSON(filepath.Join(data, "settings.json"), next); err != nil {
				return err
			}
		}
		initialized = &next
		if password == "" && rememberedPassword != "" {
			if err = configuration.RememberPassword(data, next, rememberedPassword); err != nil {
				return err
			}
			password = rememberedPassword
		}
		if err = unlock(); err != nil {
			return err
		}
		unlock = nil
		if !opts.NoService {
			if err = registerService(dir, opts.Autostart); err != nil {
				return fmt.Errorf("files installed in %s, but service registration failed: %w", dir, err)
			}
			if err = runInstalled(dir, "__path", "install", "--dir", dir); err != nil {
				return err
			}
			if runtime.GOOS == "linux" {
				alias, lookupErr := exec.LookPath("openflux")
				if lookupErr != nil {
					return fmt.Errorf("openflux is unavailable in PATH after registration: %w", lookupErr)
				}
				actual, statErr := os.Stat(alias)
				expected, targetErr := os.Stat(filepath.Join(dir, cliName()))
				if statErr != nil || targetErr != nil || !os.SameFile(actual, expected) {
					return fmt.Errorf("openflux in PATH points to another program: %s", alias)
				}
			}
		}
		return nil
	}); e != nil {
		return e
	}
	step := u.Text("Запуск сервера и проверка подключения...", "Starting server & verifying readiness...")
	if opts.NoService {
		step = u.Text("Проверка установленной конфигурации...", "Verifying installed configuration...")
	}
	if e = u.Step(3, step, func() error {
		if opts.NoService {
			_, err := configuration.ReadSettings(data)
			return err
		}
		if err := runInstalled(dir, "start", "--dir", dir, "--json"); err != nil {
			return err
		}
		ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
		defer cancel()
		return waitForPanel(ctx, *initialized)
	}); e != nil {
		return e
	}
	if opts.NoService {
		_, web := executableNames()
		quote := func(s string) string { return "'" + strings.ReplaceAll(s, "'", "'\\''") + "'" }
		prefix := ""
		if runtime.GOOS == "windows" {
			prefix = "& "
			quote = func(s string) string { return "'" + strings.ReplaceAll(s, "'", "''") + "'" }
		}
		u.Info(u.Text("Запуск: ", "Start: ") + prefix + quote(filepath.Join(dir, web)) + " --data-dir " + quote(data))
	} else {
		if runtime.GOOS == "windows" {
			u.Info(u.Text("Откройте новый терминал, чтобы использовать обновлённый PATH.", "Reopen your terminal to use the updated PATH."))
		}
	}
	if initialized.LocaltunnelEnabled && !opts.NoService {
		deadline := time.Now().Add(8 * time.Second)
		for time.Now().Before(deadline) {
			var status publication.Status
			if (&configuration.Store{Dir: data}).Read("publication.json", &status) == nil && status.State == "connected" {
				break
			}
			time.Sleep(250 * time.Millisecond)
		}
		var status publication.Status
		_ = (&configuration.Store{Dir: data}).Read("publication.json", &status)
		if status.State != "connected" {
			u.Info(u.Text("Localtunnel ещё подключается. Статус: openflux network status", "Localtunnel is still connecting. Check: openflux network status"))
		}
	}
	u.Summary(accessSummary(*initialized, data, password, true, !opts.NoService))
	return nil
}
