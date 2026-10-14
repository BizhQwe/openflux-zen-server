package configuration

import (
	"net/netip"
	"testing"
)

func TestLANSubnetsRejectPublicAndOverbroadRanges(t *testing.T) {
	for _, value := range []string{"0.0.0.0/0", "10.0.0.0/7", "172.16.0.0/11", "192.168.0.0/15", "203.0.113.20/32", "::/0", "2001:db8::/32", "127.0.0.0/8", "invalid", "192.168.1.0/24, 0.0.0.0/0"} {
		if _, err := ParseLANSubnets(value); err == nil {
			t.Fatalf("unsafe LAN subnet accepted: %s", value)
		}
	}
	s := Settings{Listen: "0.0.0.0:5000", LANAccessMode: "on", LANSubnets: "192.168.31.0/24, fd42::/48"}
	for _, test := range []struct {
		ip   string
		want bool
	}{{"192.168.31.11", true}, {"::ffff:192.168.31.11", true}, {"192.168.32.11", false}, {"10.8.0.1", false}, {"fd42::123", true}, {"fd43::123", false}, {"203.0.113.20", false}} {
		if got := LANClientAllowed(s, netip.MustParseAddr(test.ip)); got != test.want {
			t.Fatalf("%s: %t", test.ip, got)
		}
	}
	SetLAN(&s, false)
	if LANClientAllowed(s, netip.MustParseAddr("192.168.31.11")) {
		t.Fatal("disabled LAN allowed access")
	}
	// A wildcard listener retained for the domain must not enable LAN again.
	SetPublicListen(&s, true)
	if LANEnabled(s) {
		t.Fatal("domain bind enabled LAN")
	}
	for _, iface := range LANInterfaces() {
		if !iface.Address.IsPrivate() {
			t.Fatal("public interface advertised as LAN")
		}
	}
}
