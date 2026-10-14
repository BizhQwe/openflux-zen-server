package main

import (
	"context"
	"flag"
	"fmt"
	"log"
	"os"
	"os/signal"
	"path/filepath"
	"strings"
	"syscall"
	"time"

	"openflux.local/zen/panel/configuration"
	"openflux.local/zen/panel/internal/hosting"
	"openflux.local/zen/panel/internal/panel"
)

func main() {
	if e := run(); e != nil {
		log.Fatal(e)
	}
}
func run() error {
	executable, e := os.Executable()
	if e != nil {
		return e
	}
	base := filepath.Dir(executable)
	coreName := "openflux-zen-server-core"
	if strings.HasSuffix(executable, ".exe") {
		coreName += ".exe"
	}
	data := flag.String("data-dir", filepath.Join(base, "data"), "Settings, tunnels and logs directory")
	core := flag.String("core", filepath.Join(base, coreName), "Path to the original OpenFlux executable")
	listen := flag.String("listen", "", "Override and persist host:port (default: 127.0.0.1:5000)")
	initOnly := flag.Bool("init", false, "Initialize data and print access details without running a server")
	credentials := flag.Bool("credentials", false, "Print access details")
	reset := flag.Bool("reset-password", false, "Reset the admin password; stop the panel first")
	version := flag.Bool("version", false, "Print version")
	issueTLS := flag.Bool("issue-tls", false, "Issue/renew the configured domain certificate using a temporarily free port 80; stop the panel first")
	flag.Parse()
	if *version {
		fmt.Println("OpenFlux Zen Server " + panel.Version)
		return nil
	}
	if flag.NArg() != 0 {
		return fmt.Errorf("unexpected arguments; use --help")
	}
	store, e := panel.NewStore(*data)
	if e != nil {
		return e
	}
	settings, password, e := store.LoadSettings()
	if e != nil {
		return e
	}
	if *issueTLS {
		if !settings.DomainEnabled || strings.Contains(settings.Domain, "://") {
			return fmt.Errorf("set a bare domain (without http:// or https://) for direct HTTPS; an explicit URL refers to an existing reverse proxy")
		}
		unlock, err := configuration.Lock(store.Dir)
		if err != nil {
			return fmt.Errorf("stop the panel before issuing a certificate: %w", err)
		}
		defer unlock()
		ctx, cancel := context.WithTimeout(context.Background(), 3*time.Minute)
		defer cancel()
		cert, key, err := configuration.IssueTLS(ctx, store.Dir, settings.Domain)
		if err != nil {
			return err
		}
		settings.TLSCert, settings.TLSKey = cert, key
		if err = store.Write("settings.json", settings); err != nil {
			return err
		}
		fmt.Println("HTTPS certificate saved. Panel URL:", configuration.PanelURLs(settings)["domain"])
		return nil
	}
	if *listen != "" {
		settings.Listen = *listen
		if e = panel.ValidateSettings(settings); e != nil {
			return e
		}
		if e = store.Write("settings.json", settings); e != nil {
			return e
		}
	}
	if password == "" && (*credentials || *initOnly || *reset) {
		password, _ = configuration.ReadDisplayPassword(store.Dir, settings)
	}
	if *reset {
		password = panel.RandomPassword(12)
		settings.PasswordSalt = panel.RandomSecret(18)
		settings.PasswordHash = panel.HashPassword(password, settings.PasswordSalt)
		if e = store.SavePassword(settings, password); e != nil {
			return e
		}
	}
	if password != "" || *credentials || *initOnly || *reset {
		scheme := "http"
		host := settings.Listen
		if strings.HasPrefix(host, "0.0.0.0:") {
			host = "127.0.0.1:" + strings.TrimPrefix(host, "0.0.0.0:")
		}
		fmt.Printf("OpenFlux Zen Server %s\nPanel: %s://%s/%s/\nUsername: %s\n", panel.Version, scheme, host, settings.SecretPath, settings.Username)
		if password != "" {
			fmt.Printf("Password: %s\n", password)
		} else {
			fmt.Println("Password: stored as a hash; use --reset-password to generate a new one")
		}
	}
	if *initOnly || *credentials || *reset {
		return nil
	}
	manager, e := panel.NewManager(store, *core)
	if e != nil {
		return e
	}
	defer manager.Close()
	server := panel.NewServer(store, settings, manager)
	httpServer := hosting.New(server, manager.SystemLog)
	server.SetNetworkApply(httpServer.Apply)
	if _, e = httpServer.Apply(settings); e != nil {
		manager.SystemLog("error", "HTTP-сервер панели: %v", e)
		return e
	}
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	publicationDone := make(chan struct{})
	go func() { defer close(publicationDone); server.RunPublication(ctx) }()
	defer func() { stop(); <-publicationDone }()
	defer func() {
		shutdown, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		_ = httpServer.Close(shutdown)
	}()
	renewDone := make(chan struct{})
	go func() {
		defer close(renewDone)
		configuration.RenewTLS(ctx, store.Dir, server.Settings, manager.SystemLog)
	}()
	defer func() { stop(); <-renewDone }()
	_ = manager.Restore()
	log.Printf("OpenFlux Zen Server listening on %s", settings.Listen)
	<-ctx.Done()
	return nil
}
