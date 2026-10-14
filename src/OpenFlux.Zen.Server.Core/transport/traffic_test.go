package transport

import (
	"encoding/binary"
	"errors"
	"sync/atomic"
	"testing"
	"time"
)

func trafficPacket(payload int) []byte {
	p := make([]byte, 20+payload)
	p[0], p[8], p[9] = 0x45, 64, 6
	binary.BigEndian.PutUint16(p[2:4], uint16(len(p)))
	for i := 20; i < len(p); i++ {
		p[i] = 'x'
	}
	return p
}

func TestTrafficMeterCounts25MiBBeforeBothCodecs(t *testing.T) {
	for _, preferred := range []string{CodecBatched, CodecLegacy} {
		t.Run(preferred, func(t *testing.T) {
			wire := &fakeTransport{loopback: true}
			codec := NewCodecTransport(wire, preferred, false)
			meter := NewTrafficMeter(codec)
			var delivered atomic.Uint64
			meter.Receive(func(p []byte) { delivered.Add(uint64(len(p))) })
			if err := meter.Start(); err != nil {
				t.Fatal(err)
			}
			defer meter.Stop()
			var want uint64
			sendDeadline := time.Now().Add(10 * time.Second)
			for remaining := 25 << 20; remaining > 0; {
				n := min(remaining, 65000)
				p := trafficPacket(n)
				for {
					err := meter.Send(p)
					if err == nil {
						break
					}
					if err.Error() != "codec queue full" || time.Now().After(sendDeadline) {
						t.Fatal(err)
					}
					time.Sleep(time.Millisecond)
				}
				want += uint64(len(p))
				remaining -= n
			}
			deadline := time.Now().Add(10 * time.Second)
			for delivered.Load() != want && time.Now().Before(deadline) {
				time.Sleep(10 * time.Millisecond)
			}
			in, out := meter.TrafficStats()
			if in != want || out != want || delivered.Load() != want {
				t.Fatalf("logical counters: in=%d out=%d delivered=%d want=%d", in, out, delivered.Load(), want)
			}
			wire.mu.Lock()
			var compressed uint64
			for _, frame := range wire.sent {
				compressed += uint64(len(frame))
			}
			wire.mu.Unlock()
			if compressed >= want/10 {
				t.Fatalf("regression requires compression to expose carrier undercount: wire=%d logical=%d", compressed, want)
			}
		})
	}
}

type rejectedTraffic struct{ fakeTransport }

func (*rejectedTraffic) Send([]byte) error { return errors.New("queue full") }

func TestTrafficMeterExcludesControlAndFailedWrites(t *testing.T) {
	m := NewTrafficMeter(&rejectedTraffic{})
	if m.Send(trafficPacket(128)) == nil {
		t.Fatal("write should fail")
	}
	if in, out := m.TrafficStats(); in != 0 || out != 0 {
		t.Fatal("failed write counted")
	}
	wire := &fakeTransport{loopback: true}
	m = NewTrafficMeter(wire)
	var passed atomic.Int32
	m.Receive(func([]byte) { passed.Add(1) })
	for _, frame := range [][]byte{{0}, {'O', 'F', 'X', 1}, {0x45, 0, 0, 20}, []byte("session-control")} {
		if err := m.Send(frame); err != nil {
			t.Fatal(err)
		}
	}
	if in, out := m.TrafficStats(); in != 0 || out != 0 || passed.Load() != 4 {
		t.Fatal("non-IP traffic counted or modified")
	}
}
