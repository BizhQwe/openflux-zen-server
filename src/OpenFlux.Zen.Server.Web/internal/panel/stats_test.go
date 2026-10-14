package panel

import (
	"net"
	"os"
	"path/filepath"
	"testing"
	"time"

	"github.com/p1neappleXpress/OpenFlux/transport/ipc"
)

func TestLiveStatsDoNotRetainPeerOrSpeed(t *testing.T) {
	tunnel := &Tunnel{ID: "test", Status: Running}
	p := &process{}
	m := &Manager{tunnels: map[string]*Tunnel{"test": tunnel}, processes: map[string]*process{"test": p}}
	now := time.Now()
	m.applyStatusLocked(tunnel, p, ipc.StatusPayload{Running: true, Connected: true, PeerStatus: "connected", BytesIn: 1000, BytesOut: 2000}, now)
	m.applyStatusLocked(tunnel, p, ipc.StatusPayload{Running: true, Connected: true, PeerStatus: "connected", BytesIn: 3000, BytesOut: 5000}, now.Add(time.Second))
	if tunnel.UploadRate != 2000 || tunnel.DownloadRate != 3000 || tunnel.ConnectedClients != 1 {
		t.Fatal("live IPC rates/presence incorrect", *tunnel)
	}
	m.applyStatusLocked(tunnel, p, ipc.StatusPayload{Running: true, Connected: true, PeerStatus: "disconnected", BytesIn: 3200, BytesOut: 5200}, now.Add(2*time.Second))
	if tunnel.ConnectedClients != 0 || tunnel.UploadRate != 0 || tunnel.DownloadRate != 0 {
		t.Fatal("carrier connection overrode missing peer", *tunnel)
	}
	m.applyStatusLocked(tunnel, p, ipc.StatusPayload{Running: true, Connected: true, PeerStatus: "connected", BytesIn: 4000, BytesOut: 6000}, now.Add(3*time.Second))
	m.expireLiveStatsLocked(now.Add(3*time.Second + ipcStatusTimeout + time.Millisecond))
	if tunnel.ConnectedClients != 0 || tunnel.UploadRate != 0 || tunnel.DownloadRate != 0 || tunnel.UploadBytes != 4000 || tunnel.DownloadBytes != 6000 {
		t.Fatal("stale status retained or totals lost", *tunnel)
	}
}

func TestIPCDisconnectResetsAndReconnects(t *testing.T) {
	dir, err := os.MkdirTemp("", "zenipc-")
	if err != nil {
		t.Fatal(err)
	}
	defer os.RemoveAll(dir)
	socket := filepath.Join(dir, "s.sock")
	listener, err := net.Listen("unix", socket)
	if err != nil {
		t.Fatal(err)
	}
	defer listener.Close()
	p := &process{done: make(chan struct{})}
	tunnel := &Tunnel{ID: "test", Status: Running}
	m := &Manager{tunnels: map[string]*Tunnel{"test": tunnel}, processes: map[string]*process{"test": p}}
	done := make(chan struct{})
	go func() { m.readIPC("test", p, socket); close(done) }()
	var conn net.Conn
	defer func() {
		close(p.done)
		if conn != nil {
			conn.Close()
		}
		select {
		case <-done:
		case <-time.After(2 * time.Second):
			t.Error("IPC reader did not exit")
		}
	}()
	accept := func() net.Conn {
		t.Helper()
		_ = listener.(*net.UnixListener).SetDeadline(time.Now().Add(3 * time.Second))
		conn, err := listener.Accept()
		if err != nil {
			t.Fatal(err)
		}
		return conn
	}
	conn = accept()
	if err = ipc.WriteFrame(conn, ipc.MsgStatus, &ipc.StatusPayload{Running: true, Connected: true, PeerStatus: "connected", BytesIn: 1000}); err != nil {
		t.Fatal(err)
	}
	waitUntil(t, time.Second, func() bool { v, _ := m.Get("test"); return v.ConnectedClients == 1 })
	conn.Close()
	waitUntil(t, time.Second, func() bool { v, _ := m.Get("test"); return v.ConnectedClients == 0 })
	conn = accept()
	if err = ipc.WriteFrame(conn, ipc.MsgStatus, &ipc.StatusPayload{Running: true, Connected: true, PeerStatus: "connected", BytesIn: 2000}); err != nil {
		t.Fatal(err)
	}
	waitUntil(t, time.Second, func() bool { v, _ := m.Get("test"); return v.ConnectedClients == 1 && v.UploadBytes == 2000 })
}

func trafficValue(n uint64) *uint64 { return &n }

func TestLogicalTrafficAndCoreSampleTime(t *testing.T) {
	tunnel := &Tunnel{ID: "test", Status: Running}
	p := &process{}
	m := &Manager{}
	now := time.Now()
	sample := ipc.StatusPayload{Running: true, Connected: true, PeerStatus: "connected", BytesIn: 1 << 20, BytesOut: 2 << 20, TrafficBytesIn: trafficValue(0), TrafficBytesOut: trafficValue(0), UptimeMs: 1000}
	m.applyStatusLocked(tunnel, p, sample, now)
	if tunnel.UploadBytes != 0 || tunnel.DownloadBytes != 0 {
		t.Fatal("carrier/control bytes counted despite a zero logical sample")
	}
	sample.BytesOut = 3 << 20
	sample.TrafficBytesIn, sample.TrafficBytesOut = trafficValue(1000), trafficValue(25<<20)
	sample.UptimeMs = 2000
	m.applyStatusLocked(tunnel, p, sample, now.Add(10*time.Millisecond))
	if tunnel.DownloadBytes != 25<<20 || tunnel.UploadBytes != 1000 || tunnel.DownloadRate != 25<<20 || tunnel.UploadRate != 1000 {
		t.Fatal("compressed size or IPC arrival jitter used for traffic/rates", *tunnel)
	}
	// An earlier snapshot cannot rewind the baseline and double-count later.
	sample.UptimeMs = 1500
	sample.TrafficBytesOut = trafficValue(1 << 20)
	m.applyStatusLocked(tunnel, p, sample, now.Add(20*time.Millisecond))
	if tunnel.DownloadBytes != 25<<20 || p.lastSampleMs != 2000 {
		t.Fatal("stale sample corrupted counters")
	}
	sample.UptimeMs, sample.TrafficBytesOut = 3000, trafficValue(25<<20)
	m.applyStatusLocked(tunnel, p, sample, now.Add(time.Second))
	if tunnel.DownloadRate != 0 || tunnel.DownloadBytes != 25<<20 {
		t.Fatal("idle retained speed or lost totals")
	}
	// Resetting displayed totals must not re-add the whole core lifetime.
	tunnel.UploadBytes, tunnel.DownloadBytes = 0, 0
	sample.UptimeMs, sample.TrafficBytesOut = 4000, trafficValue(26<<20)
	m.applyStatusLocked(tunnel, p, sample, now.Add(2*time.Second))
	if tunnel.DownloadBytes != 1<<20 {
		t.Fatal("reset re-counted previous traffic")
	}
}
