package transport

import "time"

const (
	PeerConnected         = "connected"
	PeerDisconnected      = "disconnected"
	PeerActivity          = "activity"
	PeerIdle              = "idle"
	classicPresenceWindow = 15 * time.Second
)

// PeerStatus is an IPC/UI observation, independent of the routing state.
// An attached room/WebSocket does not by itself prove a client is there.
// Existing ping/pong frames are sufficient; no wire messages are added.
func (s *Session) PeerStatus() string {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.stopped {
		return PeerDisconnected
	}
	window := min(s.linkTimeout, s.keepaliveInterval*3/2)
	if s.ready {
		for _, l := range s.links {
			if s.connectedLocked(l) && !l.lastHeard.IsZero() && time.Since(l.lastHeard) < window {
				return PeerConnected
			}
		}
	}
	if s.classic == ClassicAccept {
		l := s.classicLink
		if s.connectedLocked(l) && !l.classicHeard.IsZero() && time.Since(l.classicHeard) < window {
			return PeerActivity
		}
		if !s.ready {
			return PeerIdle
		}
	}
	return PeerDisconnected
}

// Classic has no presence heartbeat. Report recent decoded peer packets
// instead of claiming that a carrier connection is a connected client.
func (c *CodecTransport) PeerStatus() string {
	if !c.running.Load() || !c.Transport.IsConnected() {
		return PeerIdle
	}
	last := c.lastPeerPacket.Load()
	if last != 0 && time.Since(time.Unix(0, last)) < classicPresenceWindow {
		return PeerActivity
	}
	return PeerIdle
}
