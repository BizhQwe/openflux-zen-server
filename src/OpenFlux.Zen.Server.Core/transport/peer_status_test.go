package transport

import (
	"testing"
	"testing/synctest"
	"time"
)

func testPeerStatusWithClock(t *testing.T, test func(*testing.T)) {
	t.Helper()
	// Shared zstd codecs lazily create channels on first use. Initialize
	// them outside the clock bubble so other tests can use them as well.
	if _, err := decodeBatch(encodeBatch([][]byte{make([]byte, 256)})); err != nil {
		t.Fatal(err)
	}
	synctest.Test(t, test)
}

func TestPeerStatusExpiresWhileRoomStaysConnected(t *testing.T) {
	testPeerStatusWithClock(t, func(t *testing.T) {
		client, exit, _, rooms := linkedSessions(t, "avito")
		fastKeepalive(client, exit)
		startPair(t, client, exit)
		if exit.PeerStatus() != PeerConnected {
			t.Fatal("authenticated peer missing")
		}
		if err := client.Stop(); err != nil {
			t.Fatal(err)
		}
		if !rooms["avito"].IsConnected() {
			t.Fatal("fixture must keep the exit attached to its room")
		}
		eventually(t, "peer disconnect without carrier disconnect", func() bool { return exit.PeerStatus() == PeerDisconnected })
	})
}

func TestPeerStatusKeepsIdleHeartbeatClient(t *testing.T) {
	// Test the real heartbeat exchange against a virtual clock: a loaded
	// Windows runner may otherwise pause past the 30 ms presence window.
	testPeerStatusWithClock(t, func(t *testing.T) {
		client, exit, _, _ := linkedSessions(t, "avito")
		fastKeepalive(client, exit)
		startPair(t, client, exit)
		time.Sleep(250 * time.Millisecond)
		synctest.Wait()
		if got := exit.PeerStatus(); got != PeerConnected {
			t.Fatalf("idle client with heartbeat: %s", got)
		}
	})
}

func TestClassicPeerStatusRequiresRecentPeerNotRoom(t *testing.T) {
	_, exit, _, rooms := linkedSessions(t, "avito")
	if err := rooms["avito"].Start(); err != nil {
		t.Fatal(err)
	}
	exit.mu.Lock()
	exit.classic = ClassicAccept
	l := exit.links["avito"]
	l.started = true
	exit.classicLink = l
	l.classicHeard = time.Now().Add(-time.Minute)
	exit.mu.Unlock()
	if got := exit.PeerStatus(); got != PeerIdle {
		t.Fatal("room counted as client", got)
	}
	exit.mu.Lock()
	l.classicHeard = time.Now()
	exit.mu.Unlock()
	if got := exit.PeerStatus(); got != PeerActivity {
		t.Fatal("classic activity lost", got)
	}
	exit.mu.Lock()
	l.dead = true
	exit.mu.Unlock()
	if got := exit.PeerStatus(); got != PeerIdle {
		t.Fatal("closed carrier counted as client", got)
	}
}
