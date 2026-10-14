package panel

import (
	"bytes"
	"encoding/binary"
	"encoding/json"
	"io"
	"net"
	"net/http"
	"net/http/httptest"
	"os"
	"os/exec"
	"path/filepath"
	"strconv"
	"strings"
	"testing"
	"time"

	"github.com/p1neappleXpress/OpenFlux/share"
)

func testServer(t *testing.T) (*Server, Settings, string) {
	t.Helper()
	store, e := NewStore(t.TempDir())
	if e != nil {
		t.Fatal(e)
	}
	s, password, e := store.LoadSettings()
	if e != nil {
		t.Fatal(e)
	}
	m, e := NewManager(store, filepath.Join(store.Dir, "missing-core"))
	if e != nil {
		t.Fatal(e)
	}
	t.Cleanup(func() { _ = m.Close() })
	return NewServer(store, s, m), s, password
}
func request(t *testing.T, s *Server, method, path string, v any, cookie *http.Cookie, origin string) *httptest.ResponseRecorder {
	t.Helper()
	var body io.Reader
	if v != nil {
		b, e := json.Marshal(v)
		if e != nil {
			t.Fatal(e)
		}
		body = bytes.NewReader(b)
	}
	r := httptest.NewRequest(method, "http://panel.test"+path, body)
	r.RemoteAddr = "127.0.0.1:9999"
	if cookie != nil {
		r.AddCookie(cookie)
	}
	if origin != "" {
		r.Header.Set("Origin", origin)
	}
	w := httptest.NewRecorder()
	s.ServeHTTP(w, r)
	return w
}
func TestAuthIsolationAndLegacyImport(t *testing.T) {
	s, settings, password := testServer(t)
	prefix := "/" + settings.SecretPath + "/"
	if w := request(t, s, "GET", prefix, nil, nil, ""); w.Code != 200 || !strings.Contains(w.Body.String(), "OpenFlux Zen Server") {
		t.Fatalf("panel entry page: %d", w.Code)
	}
	if w := request(t, s, "GET", "/api/tunnels", nil, nil, ""); w.Code != 404 {
		t.Fatalf("unprotected root: %d", w.Code)
	}
	if w := request(t, s, "GET", prefix+"api/tunnels", nil, nil, ""); w.Code != 401 {
		t.Fatalf("unauthenticated API: %d", w.Code)
	}
	credentials := map[string]string{"username": settings.Username, "password": password}
	if w := request(t, s, "POST", prefix+"api/auth/login", credentials, nil, "http://evil.test"); w.Code != 403 {
		t.Fatalf("cross-origin login: %d", w.Code)
	}
	w := request(t, s, "POST", prefix+"api/auth/login", credentials, nil, "")
	if w.Code != 200 {
		t.Fatal(w.Body.String())
	}
	cookies := w.Result().Cookies()
	if len(cookies) != 1 || !cookies[0].HttpOnly || cookies[0].SameSite != http.SameSiteStrictMode {
		t.Fatal("invalid session cookie")
	}
	cookie := cookies[0]
	tunnel := DefaultTunnel()
	tunnel.Transport = "direct"
	tunnel.EncryptionKey = "integration-secret-123456"
	raw, e := json.Marshal(tunnel)
	if e != nil {
		t.Fatal(e)
	}
	var fields map[string]json.RawMessage
	_ = json.Unmarshal(raw, &fields)
	legacy := map[string]json.RawMessage{}
	for k, v := range fields {
		legacy[strings.ToUpper(k[:1])+k[1:]] = v
	}
	legacy["Id"] = json.RawMessage(`"` + newID() + `"`)
	w = request(t, s, "POST", prefix+"api/config/import", map[string]any{"Version": "1.0", "Tunnels": []any{legacy}}, cookie, "")
	if w.Code != 200 {
		t.Fatal(w.Body.String())
	}
	list := s.manager.List()
	if len(list) != 1 || list[0].Transport != "direct" || list[0].IsEnabled {
		t.Fatalf("bad import: %+v", list)
	}
	w = request(t, s, "GET", prefix+"api/config/export", nil, cookie, "")
	if w.Code != 200 || !strings.Contains(w.Body.String(), tunnel.EncryptionKey) {
		t.Fatal("export did not preserve core configuration")
	}
	w = request(t, s, "GET", prefix+"api/settings", nil, cookie, "")
	if strings.Contains(w.Body.String(), settings.PasswordHash) || strings.Contains(w.Body.String(), password) {
		t.Fatal("settings disclose credentials")
	}
	w = request(t, s, "POST", prefix+"api/auth/logout", nil, cookie, "")
	if w.Code != 200 {
		t.Fatal(w.Code)
	}
	w = request(t, s, "GET", prefix+"api/tunnels", nil, cookie, "")
	if w.Code != 401 {
		t.Fatal("logout did not revoke the session")
	}
}
func TestImportIsAtomic(t *testing.T) {
	s, _, _ := testServer(t)
	a := DefaultTunnel()
	a.EncryptionKey = "integration-secret-123456"
	b := a
	b.Transport = "unsupported"
	if _, e := s.manager.Import([]Tunnel{a, b}); e == nil {
		t.Fatal("invalid batch accepted")
	}
	if len(s.manager.List()) != 0 {
		t.Fatal("partially imported invalid batch")
	}
}

func TestEmbeddedBrandAssetsStayWithinSecretRoute(t *testing.T) {
	s, settings, _ := testServer(t)
	prefix := "/" + settings.SecretPath + "/"
	for _, asset := range []string{"css/app.css", "js/pages/app.js", "js/pages/tunnels.js", "js/common/api.js", "js/vendor/qrcode.min.js", "images/logo-32x32.png", "images/logo-192x192.png", "images/transport-yandex.png", "images/transport-avito.png", "images/favicon.ico"} {
		w := request(t, s, "GET", prefix+asset, nil, nil, "")
		if w.Code != 200 || w.Body.Len() == 0 {
			t.Fatalf("missing embedded asset %s: %d", asset, w.Code)
		}
	}
	for _, route := range []string{"/images/logo-32x32.png", prefix + "images/", prefix + "images/../index.html", prefix + "images/../../settings.json", prefix + "html/index.html", prefix + "html/pages/login.html", prefix + "html/dialogs/tunnel.html", prefix + "images/transport-yandex-telemost.png", prefix + "README.md", prefix + "js/", prefix + "js/pages/"} {
		if w := request(t, s, "GET", route, nil, nil, ""); w.Code != 404 {
			t.Fatalf("asset route exposed: %s (%d)", route, w.Code)
		}
	}
}

func TestLegacyPageAndAPIContract(t *testing.T) {
	s, settings, password := testServer(t)
	prefix := "/" + settings.SecretPath + "/"
	w := request(t, s, "GET", prefix, nil, nil, "")
	if w.Code != 200 || strings.Contains(w.Body.String(), "{{template") {
		t.Fatal("page fragments were not assembled")
	}
	for _, id := range []string{"login-view", "app-header", "tab-tunnels", "tab-logs", "tab-config", "tab-settings", "tunnel-modal", "qr-modal", "cookies-modal", "rollback-modal"} {
		if !strings.Contains(w.Body.String(), `id="`+id+`"`) {
			t.Fatalf("missing legacy view: %s", id)
		}
	}
	w = request(t, s, "POST", prefix+"api/auth/login", map[string]string{"username": settings.Username, "password": password}, nil, "")
	var login struct{ Token string }
	if e := json.Unmarshal(w.Body.Bytes(), &login); e != nil || !s.auth.valid(login.Token) {
		t.Fatal("legacy login did not receive a usable bearer token")
	}
	cookie := w.Result().Cookies()[0]
	w = request(t, s, "GET", prefix+"api/stats", nil, cookie, "")
	var stats map[string]float64
	if e := json.Unmarshal(w.Body.Bytes(), &stats); e != nil || w.Code != 200 {
		t.Fatal("legacy stats response is invalid")
	}
	for _, field := range []string{"activeTunnels", "totalTunnels", "cpuUsagePercent", "memoryUsedBytes", "memoryTotalBytes", "memoryUsagePercent", "uploadRateBytesPerSec", "downloadRateBytesPerSec", "totalUploadBytes", "totalDownloadBytes"} {
		if _, ok := stats[field]; !ok {
			t.Fatalf("missing legacy stats field: %s", field)
		}
	}
	if stats["memoryTotalBytes"] <= 0 || stats["memoryUsedBytes"] > stats["memoryTotalBytes"] {
		t.Fatal("host memory metrics are invalid")
	}
	s.manager.logs.Append("system", "legacy-system-message")
	w = request(t, s, "GET", prefix+"api/logs/system", nil, cookie, "")
	var system []string
	if e := json.Unmarshal(w.Body.Bytes(), &system); e != nil || len(system) == 0 || !strings.Contains(system[len(system)-1], "legacy-system-message") {
		t.Fatal("system logs are not legacy string records")
	}
	tunnel := DefaultTunnel()
	tunnel.Transport, tunnel.EncryptionKey = "direct", "integration-secret-123456"
	created, e := s.manager.Upsert(tunnel, true)
	if e != nil {
		t.Fatal(e)
	}
	s.manager.logs.Append(created.ID, "legacy-tunnel-message")
	w = request(t, s, "GET", prefix+"api/tunnels/"+created.ID+"/logs", nil, cookie, "")
	var lines []struct{ Stream, Message string }
	if e = json.Unmarshal(w.Body.Bytes(), &lines); e != nil || len(lines) == 0 || lines[len(lines)-1].Stream != "stdout" {
		t.Fatal("tunnel logs are missing legacy stream fields")
	}
	for _, route := range []string{"api/panel/version", "api/core/version", "api/settings/network-placement"} {
		if result := request(t, s, "GET", prefix+route, nil, cookie, ""); result.Code != 200 || !json.Valid(result.Body.Bytes()) {
			t.Fatalf("legacy settings API unavailable: %s", route)
		}
	}
	w = request(t, s, "POST", prefix+"api/settings/language", map[string]string{"language": "en"}, cookie, "")
	if w.Code != 200 || s.Settings().Language != "en" {
		t.Fatal("legacy language setting did not persist")
	}
}
func TestCookiesFormats(t *testing.T) {
	for _, input := range []string{`{"a":"b"}`, `[{"name":"a","value":"b"}]`, `a=b; token=123`, `# Netscape HTTP Cookie File
#HttpOnly_example.test	TRUE	/	TRUE	0	a	b`, `{"cookies":"a=b"}`} {
		jar, e := ParseCookies(input)
		if e != nil || jar["a"] != "b" {
			t.Fatalf("%s: %v %+v", input, e, jar)
		}
	}
}
func TestArgsDoNotInvokeShellOrExposeKey(t *testing.T) {
	tunnel := DefaultTunnel()
	tunnel.ID = newID()
	tunnel.Transport = "direct"
	tunnel.EncryptionKey = "integration-secret-123456"
	tunnel.ExtraArgs = `--http-proxy="127.0.0.1:8080" --session-context='context with spaces'`
	tunnel.LocalIP = "127.0.0.1"
	dir := t.TempDir()
	args, e := BuildArgs(tunnel, dir, "/private/ipc.sock")
	if e != nil {
		t.Fatal(e)
	}
	for _, a := range args {
		if strings.Contains(a, tunnel.EncryptionKey) {
			t.Fatal("key exposed as process argument")
		}
	}
	key, e := os.ReadFile(filepath.Join(dir, "keys", tunnel.ID+".key"))
	if e != nil || string(key) != tunnel.EncryptionKey {
		t.Fatal("key not preserved")
	}
	if _, e := SplitArgs(`--ipc-socket=/tmp/foreign`); e == nil {
		t.Fatal("IPC override accepted")
	}
	if _, e := SplitArgs(`--url="unterminated`); e == nil {
		t.Fatal("invalid quoting accepted")
	}
}

func freeAddress(t *testing.T) string {
	t.Helper()
	ln, e := net.Listen("tcp4", "127.0.0.1:0")
	if e != nil {
		t.Fatal(e)
	}
	address := ln.Addr().String()
	_ = ln.Close()
	return address
}
func waitUntil(t *testing.T, timeout time.Duration, condition func() bool) {
	t.Helper()
	deadline := time.Now().Add(timeout)
	for time.Now().Before(deadline) {
		if condition() {
			return
		}
		time.Sleep(100 * time.Millisecond)
	}
	t.Fatal("condition did not become true")
}

// This test uses actual OpenFlux binaries, two peers and a SOCKS5 TCP round trip.
func TestOriginalCoreInterop(t *testing.T) {
	core := os.Getenv("OPENFLUX_TEST_CORE")
	if core == "" {
		t.Skip("set OPENFLUX_TEST_CORE to run the real core compatibility test")
	}
	store, e := NewStore(t.TempDir())
	if e != nil {
		t.Fatal(e)
	}
	m, e := NewManager(store, core)
	if e != nil {
		t.Fatal(e)
	}
	defer m.Close()
	var destination net.IP
	addresses, e := net.InterfaceAddrs()
	if e != nil {
		t.Fatal(e)
	}
	for _, a := range addresses {
		if ipnet, ok := a.(*net.IPNet); ok && ipnet.IP.To4() != nil && !ipnet.IP.IsLoopback() && ipnet.IP.IsGlobalUnicast() {
			destination = ipnet.IP.To4()
			break
		}
	}
	if destination == nil {
		t.Skip("the original gVisor exit does not forward loopback destinations; a local IPv4 interface is required")
	}
	echo, e := net.Listen("tcp4", "0.0.0.0:0")
	if e != nil {
		t.Fatal(e)
	}
	defer echo.Close()
	go func() {
		for {
			conn, e := echo.Accept()
			if e != nil {
				return
			}
			go func() { defer conn.Close(); _, _ = io.Copy(conn, conn) }()
		}
	}()
	exit := DefaultTunnel()
	exit.Name = "Interop exit"
	exit.Transport = "direct"
	if mode := os.Getenv("OPENFLUX_TEST_EXIT_MODE"); mode != "" {
		exit.Mode = mode
	}
	exit.DirectListen = freeAddress(t)
	exit.ShareHost = "127.0.0.1"
	exit.EncryptionKey = "integration-secret-123456"
	exit, e = m.Upsert(exit, true)
	if e != nil {
		t.Fatal(e)
	}
	if e = m.Start(exit.ID); e != nil {
		t.Fatal(e)
	}
	waitUntil(t, 10*time.Second, func() bool { v, _ := m.Get(exit.ID); return v.ShareLink != "" })
	v, _ := m.Get(exit.ID)
	result := share.Read(v.ShareLink)
	if result.Error != "" || result.Config.Secret != exit.EncryptionKey {
		t.Fatalf("incompatible share link: %+v", result)
	}
	png, e := share.PNG(v.ShareLink, 320)
	if e != nil || !bytes.HasPrefix(png, []byte{137, 80, 78, 71}) {
		t.Fatal("invalid QR")
	}
	// The panel manages only the server. The compatible client runs separately,
	// just as a user's original OpenFlux client would.
	clientAddress := freeAddress(t)
	if originalClient := os.Getenv("OPENFLUX_TEST_CLIENT_CORE"); originalClient != "" {
		core = originalClient
	}
	client := exec.Command(core, "--role=client", "--transport=direct", "--mode=l4", "--codec=batched", "--negotiate", "--inbound=socks5", "--socks5="+clientAddress, "--direct-dial="+exit.DirectListen, "--encryption-key-file="+filepath.Join(store.Dir, "keys", exit.ID+".key"), "--debug=0")
	prepareProcess(client)
	clientOutput := &lineWriter{consume: func(line string) { m.logs.Append("interop-client", line) }}
	client.Stdout, client.Stderr = clientOutput, clientOutput
	if e = client.Start(); e != nil {
		t.Fatal(e)
	}
	clientDone := make(chan error, 1)
	clientStopped := false
	go func() { err := client.Wait(); clientOutput.Flush(); clientDone <- err }()
	defer func() {
		if !clientStopped {
			_ = client.Process.Kill()
			<-clientDone
		}
	}()
	var proxy net.Conn
	waitUntil(t, 10*time.Second, func() bool {
		c, e := net.DialTimeout("tcp", clientAddress, 200*time.Millisecond)
		if e != nil {
			return false
		}
		proxy = c
		return true
	})
	defer proxy.Close()
	_ = proxy.SetDeadline(time.Now().Add(15 * time.Second))
	_, _ = proxy.Write([]byte{5, 1, 0})
	var greeting [2]byte
	if _, e = io.ReadFull(proxy, greeting[:]); e != nil || greeting != [2]byte{5, 0} {
		t.Fatalf("SOCKS greeting: %v %v", greeting, e)
	}
	address := echo.Addr().(*net.TCPAddr)
	if external := os.Getenv("OPENFLUX_TEST_DESTINATION"); external != "" {
		address, e = net.ResolveTCPAddr("tcp4", external)
		if e != nil {
			t.Fatal(e)
		}
		destination = address.IP.To4()
	}
	packet := []byte{5, 1, 0, 1, 127, 0, 0, 1, 0, 0}
	copy(packet[4:8], destination)
	binary.BigEndian.PutUint16(packet[8:], uint16(address.Port))
	_, _ = proxy.Write(packet)
	reply := make([]byte, 10)
	if _, e = io.ReadFull(proxy, reply); e != nil || reply[1] != 0 {
		t.Fatalf("SOCKS connect: %v %v; exit logs: %+v; client logs: %+v", reply, e, m.Logs(exit.ID, 20), m.Logs("interop-client", 20))
	}
	message := []byte("OpenFlux Zen Server compatibility round trip")
	if size, err := strconv.Atoi(os.Getenv("OPENFLUX_TEST_BYTES")); err == nil && size > 0 && size <= 32<<20 {
		message = bytes.Repeat([]byte("x"), size)
		_ = proxy.SetDeadline(time.Now().Add(90 * time.Second))
	}
	writeDone := make(chan error, 1)
	go func() {
		n, err := proxy.Write(message)
		if err == nil && n != len(message) {
			err = io.ErrShortWrite
		}
		writeDone <- err
	}()
	received := make([]byte, len(message))
	if _, e = io.ReadFull(proxy, received); e != nil || !bytes.Equal(message, received) {
		t.Fatalf("TCP round trip (%d bytes): %v", len(message), e)
	}
	if err := <-writeDone; err != nil {
		t.Fatal(err)
	}
	if len(message) >= 25<<20 {
		m.mu.Lock()
		process := m.processes[exit.ID]
		m.mu.Unlock()
		m.captureFinalStatus(exit.ID, process)
		v, _ := m.Get(exit.ID)
		m.mu.Lock()
		trafficMode := process.trafficMode
		m.mu.Unlock()
		if v.UploadBytes < int64(len(message)) || v.DownloadBytes < int64(len(message)) || trafficMode != "ip" {
			t.Fatalf("25 MiB transfer undercounted: up=%d down=%d mode=%s", v.UploadBytes, v.DownloadBytes, trafficMode)
		}
		t.Logf("25 MiB transfer: logical upload=%d download=%d; rates=%.0f/%.0f B/s", v.UploadBytes, v.DownloadBytes, v.UploadRate, v.DownloadRate)
	}
	waitUntil(t, 5*time.Second, func() bool { v, _ := m.Get(exit.ID); return v.UploadBytes > 0 && v.DownloadBytes > 0 })
	waitUntil(t, 3*time.Second, func() bool { v, _ := m.Get(exit.ID); return v.ConnectedClients == 1 })
	_ = proxy.Close()
	_ = client.Process.Kill()
	<-clientDone
	clientStopped = true
	waitUntil(t, 3*time.Second, func() bool {
		v, _ := m.Get(exit.ID)
		return v.Status == Running && v.ConnectedClients == 0 && v.UploadRate == 0 && v.DownloadRate == 0
	})
	if e = m.Stop(exit.ID); e != nil {
		t.Fatal(e)
	}
	if exit.Mode == "l3" {
		if _, e := os.Stat(filepath.Join(store.Dir, "l3-firewall.json")); !os.IsNotExist(e) {
			t.Fatal("L3 firewall cleanup failed", e)
		}
	}
	persisted, e := store.LoadTunnels()
	if e != nil {
		t.Fatal(e)
	}
	if len(persisted) != 1 || persisted[0].Role != ServerRole {
		t.Fatal("the external client must not become a panel tunnel")
	}
	for _, v := range persisted {
		if v.IsEnabled {
			t.Fatal("manual stop must persist disabled state")
		}
	}
}
