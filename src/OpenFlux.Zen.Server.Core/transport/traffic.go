package transport

import (
	"encoding/binary"
	"sync/atomic"
)

// TrafficMeter counts IPv4 traffic at the tunnel boundary, before compression,
// encryption and carrier framing. Carrier Stats retain their original meaning.
// Session control, relay keepalives and duplicate Session records stay outside
// this boundary. Counters survive carrier replacement within one core process.
type TrafficMeter struct {
	Transport
	sent, received atomic.Uint64
}

func NewTrafficMeter(inner Transport) *TrafficMeter { return &TrafficMeter{Transport: inner} }

func ipv4TrafficSize(p []byte) int {
	if len(p) < 20 || p[0]>>4 != 4 {
		return 0
	}
	header := int(p[0]&15) * 4
	total := int(binary.BigEndian.Uint16(p[2:4]))
	if header < 20 || total < header || total > len(p) {
		return 0
	}
	return total
}

func (m *TrafficMeter) Send(p []byte) error {
	if err := m.Transport.Send(p); err != nil {
		return err
	}
	m.sent.Add(uint64(ipv4TrafficSize(p)))
	return nil
}

func (m *TrafficMeter) Receive(cb func([]byte)) {
	if cb == nil {
		m.Transport.Receive(nil)
		return
	}
	m.Transport.Receive(func(p []byte) {
		m.received.Add(uint64(ipv4TrafficSize(p)))
		cb(p)
	})
}

func (m *TrafficMeter) TrafficStats() (received, sent uint64) {
	return m.received.Load(), m.sent.Load()
}

func (m *TrafficMeter) PeerStatus() string {
	if peer, ok := m.Transport.(interface{ PeerStatus() string }); ok {
		return peer.PeerStatus()
	}
	if m.IsConnected() {
		return PeerConnected
	}
	return PeerDisconnected
}
