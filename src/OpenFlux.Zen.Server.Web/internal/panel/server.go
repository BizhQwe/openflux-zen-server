package panel

import (
	"bytes"
	"context"
	"embed"
	"encoding/json"
	"errors"
	"fmt"
	"github.com/p1neappleXpress/OpenFlux/share"
	"io"
	"io/fs"
	"net"
	"net/http"
	"net/url"
	"os"
	"path"
	"runtime"
	"sort"
	"strconv"
	"strings"
	"sync"
	"time"

	"openflux.local/zen/panel/configuration"
	"openflux.local/zen/panel/publication"
)

//go:embed web/*
var assets embed.FS

type Server struct {
	mu           sync.RWMutex
	settings     Settings
	store        *Store
	manager      *Manager
	auth         *Auth
	files        http.Handler
	started      time.Time
	host         hostSampler
	publication  *publication.Client
	applyNetwork func(Settings) (func(), error)
}

func NewServer(store *Store, settings Settings, manager *Manager) *Server {
	web, _ := fs.Sub(assets, "web")
	s := &Server{settings: settings, store: store, manager: manager, auth: newAuth(), files: http.FileServer(http.FS(web)), started: time.Now()}
	s.publication = publication.New(s, func(status publication.Status) { _ = store.Write("publication.json", status) })
	manager.SystemLog("info", "Подготовлен HTTP-сервер панели: listen=%s, TLS=%t, os=%s/%s, Go=%s", settings.Listen, settings.TLSCert != "", runtime.GOOS, runtime.GOARCH, runtime.Version())
	return s
}
func (s *Server) RunPublication(ctx context.Context) { s.publication.Run(ctx, s.Settings) }
func (s *Server) Settings() Settings                 { s.mu.RLock(); defer s.mu.RUnlock(); return s.settings }

// SetNetworkApply installs the listener controller before serving requests.
func (s *Server) SetNetworkApply(apply func(Settings) (func(), error)) { s.applyNetwork = apply }
func (s *Server) saveSettings(next Settings, displayPassword ...string) error {
	if e := ValidateSettings(next); e != nil {
		return e
	}
	if _, _, e := net.SplitHostPort(next.Listen); e != nil {
		return errors.New("адрес панели должен быть host:port")
	}
	if (next.TLSCert == "") != (next.TLSKey == "") {
		return errors.New("задайте сертификат и ключ TLS вместе")
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	previous := s.settings
	undo := func() {}
	if next.Listen != previous.Listen || next.TLSCert != previous.TLSCert || next.TLSKey != previous.TLSKey {
		if next.TLSCert != "" {
			if e := configuration.CheckTLS(next.TLSCert, next.TLSKey); e != nil {
				return e
			}
		}
		if s.applyNetwork == nil {
			return errors.New("изменение сетевого адреса недоступно: контроллер HTTP не запущен")
		}
		var e error
		undo, e = s.applyNetwork(next)
		if e != nil {
			return e
		}
	}
	var e error
	if len(displayPassword) > 0 {
		e = s.store.SavePassword(next, displayPassword[0])
	} else {
		e = s.store.Write("settings.json", next)
	}
	if e != nil {
		undo()
		return e
	}
	s.settings = next
	s.manager.SystemLog("info", "Настройки панели сохранены: listen=%s, language=%s, TLS=%t, localtunnel=%t, domain=%t", next.Listen, next.Language, next.TLSCert != "", next.LocaltunnelEnabled, next.DomainEnabled)
	return nil
}
func jsonResponse(w http.ResponseWriter, status int, v any) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(v)
}
func fail(w http.ResponseWriter, e error) {
	status := http.StatusBadRequest
	if errors.Is(e, os.ErrNotExist) {
		status = http.StatusNotFound
	}
	jsonResponse(w, status, map[string]any{"error": e.Error(), "message": e.Error(), "success": false, "errors": 1})
}
func decode(r *http.Request, v any) error {
	decoder := json.NewDecoder(r.Body)
	if e := decoder.Decode(v); e != nil {
		return errors.New("некорректный JSON: " + e.Error())
	}
	var extra any
	if decoder.Decode(&extra) != io.EOF {
		return errors.New("в запросе должен быть один JSON-объект")
	}
	return nil
}
func token(r *http.Request) string {
	if value := r.Header.Get("Authorization"); strings.HasPrefix(value, "Bearer ") {
		return strings.TrimPrefix(value, "Bearer ")
	}
	if c, e := r.Cookie("zen_session"); e == nil {
		return c.Value
	}
	return ""
}
func (s *Server) serveHTTP(w http.ResponseWriter, r *http.Request) {
	w.Header().Set("X-Content-Type-Options", "nosniff")
	w.Header().Set("Referrer-Policy", "no-referrer")
	w.Header().Set("Cache-Control", "no-store")
	w.Header().Set("X-Frame-Options", "DENY")
	// The original Zen interface uses inline event handlers and styles.
	w.Header().Set("Content-Security-Policy", "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; frame-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'")
	settings := s.Settings()
	if !s.networkAccessAllowed(r, settings) {
		http.Error(w, "Forbidden", http.StatusForbidden)
		return
	}
	prefix := "/" + settings.SecretPath + "/"
	if r.URL.Path == strings.TrimSuffix(prefix, "/") {
		http.Redirect(w, r, prefix, http.StatusTemporaryRedirect)
		return
	}
	if !strings.HasPrefix(r.URL.Path, prefix) {
		http.NotFound(w, r)
		return
	}
	// Direct domain access upgrades to HTTPS. IP/localhost remain HTTP, and
	// an existing reverse proxy keeps its own external TLS termination.
	if r.TLS == nil && settings.TLSCert != "" && settings.DomainEnabled && !strings.Contains(settings.Domain, "://") {
		if address, e := configuration.DomainURL(settings); e == nil {
			secure, _ := url.Parse(address)
			requested, e := url.Parse("//" + r.Host)
			if e == nil && net.ParseIP(requested.Hostname()) == nil && !strings.EqualFold(requested.Hostname(), "localhost") && strings.EqualFold(strings.TrimSuffix(requested.Hostname(), "."), strings.TrimSuffix(secure.Hostname(), ".")) {
				secure.Path, secure.RawPath, secure.RawQuery = r.URL.Path, r.URL.RawPath, r.URL.RawQuery
				http.Redirect(w, r, secure.String(), http.StatusTemporaryRedirect)
				return
			}
		}
	}
	route := strings.TrimPrefix(r.URL.Path, prefix)
	if !strings.HasPrefix(route, "api/") {
		if r.Method != "GET" && r.Method != "HEAD" {
			http.NotFound(w, r)
			return
		}
		switch route {
		case "", "index.html":
			w.Header().Set("Content-Type", "text/html; charset=utf-8")
			_, _ = w.Write(indexPage)
			return
		default:
			if !(strings.HasPrefix(route, "images/") || strings.HasPrefix(route, "css/") || strings.HasPrefix(route, "js/")) || path.Clean(route) != route {
				http.NotFound(w, r)
				return
			}
			info, err := fs.Stat(assets, "web/"+route)
			if err != nil || info.IsDir() {
				http.NotFound(w, r)
				return
			}
		}
		copy := r.Clone(r.Context())
		u := *r.URL
		u.Path = "/" + route
		copy.URL = &u
		s.files.ServeHTTP(w, copy)
		return
	}
	if r.Method != "GET" && r.Method != "HEAD" {
		if origin := r.Header.Get("Origin"); origin != "" {
			u, e := url.Parse(origin)
			if e != nil || !strings.EqualFold(u.Host, r.Host) {
				jsonResponse(w, 403, map[string]string{"error": "Origin mismatch"})
				return
			}
		}
	}
	r.Body = http.MaxBytesReader(w, r.Body, 2<<20)
	if route == "api/auth/login" && r.Method == "POST" {
		ip, _, _ := net.SplitHostPort(r.RemoteAddr)
		if !s.auth.allow(ip) {
			jsonResponse(w, 429, map[string]string{"error": "слишком много попыток входа; повторите через минуту"})
			return
		}
		var credentials struct{ Username, Password string }
		if e := decode(r, &credentials); e != nil {
			fail(w, e)
			return
		}
		if credentials.Username != settings.Username || !CheckPassword(credentials.Password, settings) {
			jsonResponse(w, 401, map[string]string{"error": "неверный логин или пароль"})
			return
		}
		value := s.auth.issue()
		secure := r.TLS != nil
		if settings.DomainEnabled {
			if address, e := configuration.DomainURL(settings); e == nil {
				if external, e := url.Parse(address); e == nil && external.Scheme == "https" && strings.EqualFold(external.Host, r.Host) {
					secure = true
				}
			}
		}
		http.SetCookie(w, &http.Cookie{Name: "zen_session", Value: value, Path: prefix, HttpOnly: true, Secure: secure, SameSite: http.SameSiteStrictMode, MaxAge: 86400})
		jsonResponse(w, 200, map[string]any{"success": true, "username": settings.Username, "token": value})
		return
	}
	if !s.auth.valid(token(r)) {
		jsonResponse(w, 401, map[string]string{"error": "требуется вход"})
		return
	}
	if s.legacyRoute(w, r, route) {
		return
	}
	switch {
	case route == "api/qr" && r.Method == "POST":
		var req struct {
			Link string `json:"link"`
		}
		if err := decode(r, &req); err != nil {
			fail(w, err)
			return
		}
		if result := share.Read(req.Link); result.Error != "" {
			fail(w, errors.New("некорректная ссылка подключения OpenFlux"))
			return
		}
		png, modules, err := qrPNG(req.Link, 1024)
		if err != nil {
			fail(w, err)
			return
		}
		w.Header().Set("Content-Type", "image/png")
		w.Header().Set("X-QR-Modules", strconv.Itoa(modules))
		_, _ = w.Write(png)
	case route == "api/auth/logout" && r.Method == "POST":
		s.auth.revoke(token(r))
		http.SetCookie(w, &http.Cookie{Name: "zen_session", Path: prefix, Value: "", MaxAge: -1, HttpOnly: true, SameSite: http.SameSiteStrictMode})
		jsonResponse(w, 200, map[string]bool{"success": true})
	case route == "api/auth/me" && r.Method == "GET":
		jsonResponse(w, 200, map[string]string{"username": settings.Username, "secretPath": settings.SecretPath})
	case route == "api/auth/change-profile" && r.Method == "POST":
		var req struct{ CurrentPassword, NewUsername, NewPassword string }
		if e := decode(r, &req); e != nil {
			fail(w, e)
			return
		}
		if !CheckPassword(req.CurrentPassword, settings) {
			fail(w, errors.New("неверный текущий пароль"))
			return
		}
		if req.NewUsername != "" {
			settings.Username = req.NewUsername
		}
		if req.NewPassword != "" {
			if len(req.NewPassword) < 8 {
				fail(w, errors.New("пароль должен содержать минимум 8 символов"))
				return
			}
			settings.PasswordSalt = RandomSecret(18)
			settings.PasswordHash = HashPassword(req.NewPassword, settings.PasswordSalt)
		}
		displayPassword := req.NewPassword
		if displayPassword == "" {
			displayPassword = req.CurrentPassword
		}
		if e := s.saveSettings(settings, displayPassword); e != nil {
			fail(w, e)
			return
		}
		s.auth.clear()
		jsonResponse(w, 200, map[string]any{"success": true, "username": settings.Username})
	case route == "api/settings" && r.Method == "GET":
		settings.PasswordSalt = ""
		settings.PasswordHash = ""
		jsonResponse(w, 200, settings)
	case route == "api/settings" && r.Method == "POST":
		next := settings
		if e := decode(r, &next); e != nil {
			fail(w, e)
			return
		}
		next.PasswordHash = settings.PasswordHash
		next.PasswordSalt = settings.PasswordSalt
		next.Username = settings.Username
		next.SecretPath = settings.SecretPath
		if e := s.saveSettings(next); e != nil {
			fail(w, e)
			return
		}
		next.PasswordHash = ""
		next.PasswordSalt = ""
		jsonResponse(w, 200, next)
	case route == "api/settings/regenerate-secret" && r.Method == "POST":
		settings.SecretPath = RandomAccessPath()
		if e := s.saveSettings(settings); e != nil {
			fail(w, e)
			return
		}
		s.auth.clear()
		jsonResponse(w, 200, map[string]string{"secretPath": settings.SecretPath})
	case route == "api/system/stats" && r.Method == "GET":
		var mem runtime.MemStats
		runtime.ReadMemStats(&mem)
		list := s.manager.List()
		running := 0
		var up, down int64
		var uploadRate, downloadRate float64
		for _, t := range list {
			if t.Status == Running {
				running++
			}
			up += t.UploadBytes
			down += t.DownloadBytes
			uploadRate += t.UploadRate
			downloadRate += t.DownloadRate
		}
		jsonResponse(w, 200, map[string]any{"panelVersion": Version, "os": runtime.GOOS, "arch": runtime.GOARCH, "memoryBytes": mem.Sys, "uptimeSeconds": int(time.Since(s.started).Seconds()), "runningTunnels": running, "totalTunnels": len(list), "uploadBytes": up, "downloadBytes": down, "uploadRate": uploadRate, "downloadRate": downloadRate, "corePath": s.manager.CorePath()})
	case route == "api/tunnels/defaults" && r.Method == "GET":
		t := DefaultTunnel()
		t.EncryptionKey = RandomSecret(32)
		jsonResponse(w, 200, t)
	case route == "api/tunnels" && r.Method == "GET":
		list := s.manager.List()
		sort.Slice(list, func(i, j int) bool { return list[i].CreatedAt.Before(list[j].CreatedAt) })
		jsonResponse(w, 200, list)
	case route == "api/tunnels" && r.Method == "POST":
		t := DefaultTunnel()
		if e := decode(r, &t); e != nil {
			fail(w, e)
			return
		}
		created, e := s.manager.Upsert(t, true)
		if e != nil {
			fail(w, e)
			return
		}
		jsonResponse(w, 201, created)
	case strings.HasPrefix(route, "api/tunnels/"):
		s.tunnelRoute(w, r, strings.TrimPrefix(route, "api/tunnels/"))
	case route == "api/config/export" && r.Method == "GET":
		list := s.manager.List()
		for i := range list {
			list[i].Challenge = nil
			list[i].Status = Stopped
			list[i].ShareLink = ""
		}
		w.Header().Set("Content-Disposition", "attachment; filename=openflux-zen-backup.json")
		jsonResponse(w, 200, map[string]any{"version": "1.0", "exportedAt": time.Now().UTC(), "tunnels": list})
	case route == "api/config/import" && r.Method == "POST":
		raw, e := io.ReadAll(r.Body)
		if e != nil {
			fail(w, e)
			return
		}
		raw = bytes.TrimSpace(bytes.TrimPrefix(raw, []byte{0xef, 0xbb, 0xbf}))
		var list []Tunnel
		var envelope struct {
			Tunnels []json.RawMessage `json:"tunnels"`
		}
		var entries []json.RawMessage
		if len(raw) > 0 && raw[0] == '[' {
			e = json.Unmarshal(raw, &entries)
		} else {
			e = json.Unmarshal(raw, &envelope)
			entries = envelope.Tunnels
		}
		if e != nil || len(entries) == 0 || len(entries) > 1000 {
			fail(w, errors.New("ожидается JSON с массивом tunnels (1–1000 записей)"))
			return
		}
		for _, entry := range entries {
			t := DefaultTunnel()
			if e = json.Unmarshal(entry, &t); e != nil {
				fail(w, e)
				return
			}
			if e = t.Validate(); e != nil {
				fail(w, fmt.Errorf("%s: %w", t.Name, e))
				return
			}
			list = append(list, t)
		}
		count, e := s.manager.Import(list)
		if e != nil {
			fail(w, e)
			return
		}
		jsonResponse(w, 200, map[string]any{"imported": count, "errors": 0, "message": fmt.Sprintf("Импортировано туннелей: %d", count)})
	case route == "api/logs/system" && r.Method == "GET":
		tail, _ := strconv.Atoi(r.URL.Query().Get("tail"))
		if tail == 0 {
			tail = 200
		}
		lines := []string{}
		for _, line := range s.manager.Logs("system", tail) {
			prefix := ""
			if !line.Timestamp.IsZero() {
				prefix = line.Timestamp.Format(time.RFC3339) + " "
			}
			lines = append(lines, prefix+"["+strings.ToUpper(line.Level)+"] "+line.Message)
		}
		jsonResponse(w, 200, lines)
	case route == "api/logs/system" && r.Method == "DELETE":
		if e := s.manager.ClearLogs("system"); e != nil {
			fail(w, e)
			return
		}
		jsonResponse(w, 200, map[string]bool{"success": true})
	default:
		http.NotFound(w, r)
	}
}
func (s *Server) tunnelRoute(w http.ResponseWriter, r *http.Request, route string) {
	parts := strings.Split(route, "/")
	id := parts[0]
	if !validID(id) {
		http.NotFound(w, r)
		return
	}
	t, ok := s.manager.Get(id)
	if !ok {
		http.NotFound(w, r)
		return
	}
	action := ""
	if len(parts) > 1 {
		action = parts[1]
	}
	if len(parts) > 2 {
		http.NotFound(w, r)
		return
	}
	var e error
	handled := true
	switch {
	case action == "" && r.Method == "GET":
		jsonResponse(w, 200, t)
		return
	case action == "" && r.Method == "PUT":
		next := DefaultTunnel()
		if e = decode(r, &next); e == nil {
			next.ID = id
			next, e = s.manager.Upsert(next, false)
		}
		if e == nil {
			jsonResponse(w, 200, next)
			return
		}
	case action == "" && r.Method == "DELETE":
		e = s.manager.Delete(id)
	case action == "start" && r.Method == "POST":
		e = s.manager.Start(id)
	case action == "stop" && r.Method == "POST":
		e = s.manager.Stop(id)
	case action == "restart" && r.Method == "POST":
		e = s.manager.Restart(id)
	case action == "toggle" && r.Method == "POST":
		if r.URL.Query().Get("enable") == "false" {
			e = s.manager.Stop(id)
		} else {
			e = s.manager.Start(id)
		}
	case action == "reset-stats" && r.Method == "POST":
		e = s.manager.ResetStats(id)
	case action == "clear-cookies" && r.Method == "POST":
		e = s.manager.ClearCookies(id)
	case action == "quick-update-url" && r.Method == "POST":
		var req struct {
			URL string `json:"url"`
		}
		e = decode(r, &req)
		if e == nil {
			t.URL = req.URL
			_, e = s.manager.Upsert(t, false)
		}
	case action == "logs" && r.Method == "GET":
		tail, _ := strconv.Atoi(r.URL.Query().Get("tail"))
		if tail == 0 {
			tail = 200
		}
		jsonResponse(w, 200, legacyLogLines(s.manager.Logs(id, tail)))
		return
	case action == "logs" && r.Method == "DELETE":
		e = s.manager.ClearLogs(id)
	case action == "captcha" && r.Method == "GET":
		jsonResponse(w, 200, t.Challenge)
		return
	case action == "cookies" && r.Method == "POST":
		raw, err := io.ReadAll(r.Body)
		if err != nil {
			e = err
			break
		}
		jar, err := ParseCookies(string(raw))
		if err != nil {
			e = err
			break
		}
		mode, err := s.manager.saveOrApplyCookies(id, jar, r.URL.Query().Get("transport"))
		e = err
		if e == nil {
			applied := 0
			stored := 0
			if mode == "sent" {
				applied = len(jar)
			} else {
				stored = len(jar)
			}
			jsonResponse(w, 200, map[string]any{"success": true, "mode": mode, "appliedCount": applied, "storedCount": stored})
			return
		}
	case action == "qr" && r.Method == "GET":
		if t.ShareLink == "" {
			fail(w, errors.New("ядро ещё не выдало ссылку: запустите exit-туннель с включённым share"))
			return
		}
		size, _ := strconv.Atoi(r.URL.Query().Get("size"))
		png, modules, err := qrPNG(t.ShareLink, size)
		if err != nil {
			fail(w, err)
			return
		}
		w.Header().Set("Content-Type", "image/png")
		w.Header().Set("X-QR-Modules", strconv.Itoa(modules))
		_, _ = w.Write(png)
		return
	default:
		handled = false
	}
	if !handled {
		http.NotFound(w, r)
		return
	}
	if e != nil {
		fail(w, e)
		return
	}
	jsonResponse(w, 200, map[string]bool{"success": true})
}
