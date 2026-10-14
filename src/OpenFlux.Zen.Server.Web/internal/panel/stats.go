package panel

import (
	"time"

	"github.com/p1neappleXpress/OpenFlux/transport/ipc"
)

const ipcStatusTimeout = 4 * time.Second

func clearLiveStats(t *Tunnel) {
	t.ConnectedClients = 0
	t.ClientStatus = "disconnected"
	t.UploadRate, t.DownloadRate = 0, 0
	t.ActiveTransport = ""
}

// Counters remain available for old exports/limits; rates and presence are
// live values and must not survive a stopped process or lost IPC feed.
func (m *Manager) applyStatusLocked(t *Tunnel, p *process, s ipc.StatusPayload, now time.Time) {
	up, down := s.BytesIn, s.BytesOut
	mode := "carrier"
	if s.TrafficBytesIn != nil && s.TrafficBytesOut != nil {
		up, down = *s.TrafficBytesIn, *s.TrafficBytesOut
		mode = "ip"
	}
	// A delayed older snapshot must not rewind the baseline and count the
	// same bytes again. IPC reconnects retain this per-process baseline.
	if s.UptimeMs > 0 && p.lastSampleMs > s.UptimeMs {
		return
	}
	first := p.trafficMode == ""
	if p.trafficMode != mode {
		level := "info"
		if mode == "carrier" {
			level = "warn"
		}
		m.SystemLog(level, "Туннель %q (%s): источник трафика=%s, время замера ядра=%t; ip означает IPv4 до сжатия, carrier — старые счётчики транспорта", t.Name, t.ID, mode, s.UptimeMs > 0)
	}
	previousPeer, previousActive := t.ClientStatus, t.ActiveTransport
	du, dd := up, down
	if up >= p.lastIn {
		du = up - p.lastIn
	}
	if down >= p.lastOut {
		dd = down - p.lastOut
	}
	t.UploadBytes += int64(du)
	t.DownloadBytes += int64(dd)
	t.UploadRate, t.DownloadRate = 0, 0
	seconds := float64(s.UptimeMs) / 1000
	if !p.lastStat.IsZero() {
		seconds = now.Sub(p.lastStat).Seconds()
		if s.UptimeMs > p.lastSampleMs {
			seconds = float64(s.UptimeMs-p.lastSampleMs) / 1000
		}
	}
	if seconds > 0 {
		t.UploadRate = float64(du) / seconds
		t.DownloadRate = float64(dd) / seconds
	}
	p.lastIn, p.lastOut, p.lastStat = up, down, now
	p.lastSampleMs, p.trafficMode = s.UptimeMs, mode
	p.wireIn, p.wireOut = s.BytesIn, s.BytesOut
	t.ConnectedClients = 0
	t.ClientStatus = s.PeerStatus
	if t.ClientStatus == "" {
		t.ClientStatus = "disconnected"
		if s.Connected {
			t.ClientStatus = "connected"
		}
	}
	if s.Running && t.Status == Running && (t.ClientStatus == "connected" || t.ClientStatus == "activity") {
		t.ConnectedClients = 1
	} else {
		t.UploadRate, t.DownloadRate = 0, 0
	}
	t.ActiveTransport = s.Active
	if t.ConnectedClients == 0 {
		t.ActiveTransport = ""
	}
	if !first && (previousPeer != t.ClientStatus || previousActive != t.ActiveTransport) {
		m.SystemLog("info", "Туннель %q (%s): peer=%s → %s, transport=%s → %s", t.Name, t.ID, previousPeer, t.ClientStatus, previousActive, t.ActiveTransport)
	}
	if p.statusUpdated != nil {
		select {
		case p.statusUpdated <- struct{}{}:
		default:
		}
	}
	m.dirty = true
}

// Request one last cumulative sample before signalling the core. This avoids
// losing a short download or the bytes since the most recent one-second tick.
// Older cores ignore the optional command; waiting is bounded to 500 ms.
func (m *Manager) captureFinalStatus(id string, p *process) {
	m.mu.Lock()
	conn := p.conn
	m.mu.Unlock()
	if conn == nil || p.statusUpdated == nil {
		return
	}
	requested := time.Now()
	p.writeMu.Lock()
	_ = conn.SetWriteDeadline(requested.Add(500 * time.Millisecond))
	err := ipc.WriteFrame(conn, ipc.MsgCommand, &ipc.CommandPayload{Action: "status"})
	p.writeMu.Unlock()
	if err != nil {
		m.SystemLog("warn", "Последний замер туннеля %s не запрошен: %v", id, err)
		return
	}
	timer := time.NewTimer(500 * time.Millisecond)
	defer timer.Stop()
	for {
		m.mu.Lock()
		fresh := p.lastStat.After(requested)
		m.mu.Unlock()
		if fresh {
			return
		}
		select {
		case <-p.statusUpdated:
		case <-p.done:
			return
		case <-timer.C:
			m.SystemLog("debug", "Последний замер туннеля %s: нет ответа за 500 ms; сохранён предыдущий замер", id)
			return
		}
	}
}

func (m *Manager) expireLiveStatsLocked(now time.Time) {
	for id, p := range m.processes {
		if !p.lastStat.IsZero() && now.Sub(p.lastStat) > ipcStatusTimeout {
			if t := m.tunnels[id]; t != nil && (t.ConnectedClients != 0 || t.UploadRate != 0 || t.DownloadRate != 0 || t.ActiveTransport != "") {
				clearLiveStats(t)
				m.SystemLog("warn", "Туннель %q (%s): статус IPC устарел (возраст=%s); текущая скорость и присутствие клиента сброшены", t.Name, id, now.Sub(p.lastStat).Round(time.Millisecond))
				m.dirty = true
			}
		}
	}
}
