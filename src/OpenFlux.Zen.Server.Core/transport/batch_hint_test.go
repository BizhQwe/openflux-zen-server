package transport

import (
	"bytes"
	"testing"
	"time"

	"github.com/p1neappleXpress/OpenFlux/transport/control"
)

type hintedCarrier struct{ *fakeTransport }

func (*hintedCarrier) PreferredBatchBytes() int { return 500_000 }

func TestCarrierBatchHintFlowsThroughSessionAndEnvironment(t *testing.T) {
	for _, test := range []struct {
		name, override string
		carrier        Transport
		want           int
	}{
		{"ordinary carrier", "", &fakeTransport{}, defaultMaxBatchBytes},
		{"hinted carrier", "", &hintedCarrier{&fakeTransport{}}, 500_000},
		{"explicit override", "16384", &hintedCarrier{&fakeTransport{}}, 16384},
	} {
		t.Run(test.name, func(t *testing.T) {
			t.Setenv("OPENFLUX_BATCH_BYTES", test.override)
			session, err := NewSession(PeerParameters{Capabilities: control.CapabilityIPv4 | control.CapabilityTCP, MaxPacketSize: 1500}, true)
			if err != nil {
				t.Fatal(err)
			}
			defer session.Stop()
			if err := session.AddTransport("carrier", test.carrier, testSessionSecret, testSessionCtx, 50); err != nil {
				t.Fatal(err)
			}
			if got := session.links["carrier"].batched.maxBatchBytes; got != test.want {
				t.Fatalf("batch limit: got %d, want %d", got, test.want)
			}
		})
	}
}

func TestLargeCarrierBatchPreservesExistingV2Wire(t *testing.T) {
	t.Setenv("OPENFLUX_BATCH_BYTES", "")
	t.Setenv("OPENFLUX_BATCH_COUNT", "")
	t.Setenv("OPENFLUX_BATCH_LINGER_MS", "100")
	carrier := &fakeTransport{loopback: true}
	batched := NewBatchedTransport(carrier, 500_000)
	const count = 8
	received := make(chan []byte, count)
	batched.Receive(func(packet []byte) { received <- append([]byte(nil), packet...) })
	if err := batched.Start(); err != nil {
		t.Fatal(err)
	}
	defer batched.Stop()
	for i := range count {
		if err := batched.Send(bytes.Repeat([]byte{byte(i)}, 16_000)); err != nil {
			t.Fatal(err)
		}
	}
	for i := range count {
		select {
		case packet := <-received:
			if !bytes.Equal(packet, bytes.Repeat([]byte{byte(i)}, 16_000)) {
				t.Fatal("packet contents or ordering changed", i)
			}
		case <-time.After(3 * time.Second):
			t.Fatal("large batch did not round-trip")
		}
	}
	if carrier.sendCount() != 1 {
		t.Fatal("large burst split into multiple carrier messages", carrier.sendCount())
	}
	if wire := carrier.firstSent(); len(wire) == 0 || wire[0] != batchFormatVersion {
		t.Fatal("existing v2 wire format changed")
	}
}
