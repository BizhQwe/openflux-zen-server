package main

import (
	"encoding/binary"
	"encoding/json"
	"os"
	"path/filepath"
	"testing"
	"time"

	"github.com/p1neappleXpress/OpenFlux/transport"
	"github.com/p1neappleXpress/OpenFlux/transport/ipc"
)

type fixedStats struct{ in, out uint64 }

func (f fixedStats) IsConnected() bool { return true }
func (f fixedStats) Stats() transport.TransportStats {
	return transport.TransportStats{BytesReceived: f.in, BytesSent: f.out}
}

// A classic carrier without a Session reports its traffic too: the app's
// speed counters read it.
func TestIPCStatusWithoutSession(t *testing.T) {
	dir, err := os.MkdirTemp("", "ofipc")
	if err != nil {
		t.Fatal(err)
	}
	defer os.RemoveAll(dir)
	path := filepath.Join(dir, "s.sock")
	srv := ipc.NewServer(path, &coreIPCHandler{})
	if err := srv.Listen(); err != nil {
		t.Fatal(err)
	}
	defer srv.Close()
	go ipcStatusLoop(srv, fixedStats{in: 1500, out: 700}, nil, nil, nil, nil)

	c, err := ipc.Dial(path)
	if err != nil {
		t.Fatal(err)
	}
	defer c.Close()
	got := make(chan ipc.StatusPayload, 1)
	c.SetHandler(func(typ ipc.MsgType, body []byte) {
		if typ != ipc.MsgStatus {
			return
		}
		var p ipc.StatusPayload
		if json.Unmarshal(body, &p) == nil {
			select {
			case got <- p:
			default:
			}
		}
	})
	select {
	case p := <-got:
		if !p.Connected || p.BytesIn != 1500 || p.BytesOut != 700 || p.Active != "" {
			t.Fatalf("status = %+v", p)
		}
	case <-time.After(3 * time.Second):
		t.Fatal("no status")
	}
}

func TestIPCRequestedLogicalStatusAndReporterShutdown(t *testing.T) {
	dir, err := os.MkdirTemp("", "ofmeter")
	if err != nil {
		t.Fatal(err)
	}
	defer os.RemoveAll(dir)
	path := filepath.Join(dir, "s.sock")
	handler := &coreIPCHandler{}
	srv := ipc.NewServer(path, handler)
	if err := srv.Listen(); err != nil {
		t.Fatal(err)
	}
	defer srv.Close()
	meter := transport.NewTrafficMeter(quietCarrier{})
	done := make(chan struct{})
	go func() { ipcStatusLoop(srv, fixedStats{in: 2, out: 3}, nil, nil, meter, handler); close(done) }()
	c, err := ipc.Dial(path)
	if err != nil {
		t.Fatal(err)
	}
	defer c.Close()
	got := make(chan ipc.StatusPayload, 8)
	c.SetHandler(func(kind ipc.MsgType, body []byte) {
		if kind == ipc.MsgStatus {
			var p ipc.StatusPayload
			if json.Unmarshal(body, &p) == nil {
				select {
				case got <- p:
				default:
				}
			}
		}
	})
	select {
	case <-got:
	case <-time.After(3 * time.Second):
		t.Fatal("no initial status")
	}
	p := make([]byte, 1000)
	p[0] = 0x45
	binary.BigEndian.PutUint16(p[2:4], 1000)
	if err := meter.Send(p); err != nil {
		t.Fatal(err)
	}
	if err := c.SendCommand(&ipc.CommandPayload{Action: "status"}); err != nil {
		t.Fatal(err)
	}
	deadline := time.After(500 * time.Millisecond)
	for {
		select {
		case sample := <-got:
			if sample.TrafficBytesOut != nil && *sample.TrafficBytesOut == 1000 {
				if sample.TrafficBytesIn == nil || *sample.TrafficBytesIn != 0 || sample.BytesIn != 2 || sample.BytesOut != 3 {
					t.Fatal("legacy counters changed or zero logical field omitted")
				}
				_ = srv.Close()
				select {
				case <-done:
					return
				case <-time.After(time.Second):
					t.Fatal("status reporter leaked after server close")
				}
			}
		case <-deadline:
			t.Fatal("on-demand status was not returned before the next one-second tick")
		}
	}
}
