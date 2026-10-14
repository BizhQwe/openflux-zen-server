package panel

import (
	"fmt"
	"net"
	"net/http"
	"runtime"
	"strings"
	"time"
)

func (m *Manager) SystemLog(level, format string, args ...any) {
	if m.logs == nil {
		return
	}
	message := strings.NewReplacer("\r", " ", "\n", " ").Replace(fmt.Sprintf(format, args...))
	m.logs.AppendLevel("system", level, message)
}

// Called with the manager lock. A minute summary provides useful context
// without writing every IPC heartbeat or HTTP polling request to disk.
func (m *Manager) diagnosticsLocked() {
	var up, down int64
	var upRate, downRate float64
	for _, t := range m.tunnels {
		up += t.UploadBytes
		down += t.DownloadBytes
		upRate += t.UploadRate
		downRate += t.DownloadRate
	}
	var mem runtime.MemStats
	runtime.ReadMemStats(&mem)
	m.SystemLog("debug", "Состояние панели: туннелей=%d, процессов=%d, upload=%d B, download=%d B, скорость=%.0f/%.0f B/s, Go heap=%d B, goroutines=%d", len(m.tunnels), len(m.processes), up, down, upRate, downRate, mem.HeapAlloc, runtime.NumGoroutine())
	for id, p := range m.processes {
		if t := m.tunnels[id]; t != nil {
			age := "нет замеров"
			if !p.lastStat.IsZero() {
				age = time.Since(p.lastStat).Round(time.Millisecond).String()
			}
			m.SystemLog("debug", "Туннель %q (%s): IPC=%t, замер=%s, счётчики=%s, peer=%s, transport=%s, IP=%d/%d B, carrier=%d/%d B", t.Name, id, p.conn != nil, age, p.trafficMode, t.ClientStatus, t.ActiveTransport, p.lastIn, p.lastOut, p.wireIn, p.wireOut)
		}
	}
}

type auditResponse struct {
	http.ResponseWriter
	status int
}

func (w *auditResponse) WriteHeader(status int) {
	if w.status != 0 {
		return
	}
	w.status = status
	w.ResponseWriter.WriteHeader(status)
}
func (w *auditResponse) Write(p []byte) (int, error) {
	if w.status == 0 {
		w.WriteHeader(http.StatusOK)
	}
	return w.ResponseWriter.Write(p)
}
func (w *auditResponse) Unwrap() http.ResponseWriter { return w.ResponseWriter }

func (s *Server) ServeHTTP(w http.ResponseWriter, r *http.Request) {
	started := time.Now()
	prefix := "/" + s.Settings().SecretPath + "/"
	route := strings.TrimPrefix(r.URL.Path, prefix)
	audit := &auditResponse{ResponseWriter: w}
	s.serveHTTP(audit, r)
	// Only known API paths are logged. Secret access paths, request bodies,
	// passwords, cookies, authorization tokens and share links are excluded.
	if !strings.HasPrefix(r.URL.Path, prefix) || !strings.HasPrefix(route, "api/") {
		return
	}
	status := audit.status
	if status == 0 {
		status = http.StatusOK
	}
	if r.Method == "GET" || r.Method == "HEAD" {
		if status < 400 {
			return
		}
	}
	level := "info"
	if status >= 500 {
		level = "error"
	} else if status >= 400 {
		level = "warn"
	}
	ip, _, _ := net.SplitHostPort(r.RemoteAddr)
	s.manager.SystemLog(level, "HTTP %s %s: status=%d, client=%s, duration=%s", r.Method, route, status, ip, time.Since(started).Round(time.Millisecond))
}
