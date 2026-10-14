package l3guard

import (
	"encoding/binary"
	"errors"
	"net"
	"os"
	"syscall"
	"testing"
	"time"

	"github.com/google/nftables"
	"github.com/google/nftables/expr"
)

func checksum(data []byte) uint16 {
	var sum uint32
	for len(data) >= 2 {
		sum += uint32(binary.BigEndian.Uint16(data))
		data = data[2:]
	}
	if len(data) > 0 {
		sum += uint32(data[0]) << 8
	}
	for sum>>16 != 0 {
		sum = (sum & 0xffff) + (sum >> 16)
	}
	return ^uint16(sum)
}
func tcpPacket(source, destination uint16, flags byte) []byte {
	p := make([]byte, 40)
	p[0] = 0x45
	binary.BigEndian.PutUint16(p[2:4], 40)
	p[8] = 64
	p[9] = 6
	copy(p[12:16], []byte{127, 0, 0, 1})
	copy(p[16:20], p[12:16])
	binary.BigEndian.PutUint16(p[10:12], checksum(p[:20]))
	binary.BigEndian.PutUint16(p[20:22], source)
	binary.BigEndian.PutUint16(p[22:24], destination)
	binary.BigEndian.PutUint32(p[24:28], 1234)
	p[32] = 0x50
	p[33] = flags
	binary.BigEndian.PutUint16(p[34:36], 65535)
	pseudo := append([]byte{}, p[12:20]...)
	pseudo = append(pseudo, 0, 6, 0, 20)
	pseudo = append(pseudo, p[20:]...)
	binary.BigEndian.PutUint16(p[36:38], checksum(pseudo))
	return p
}
func TestKernelRSTGuard(t *testing.T) {
	if os.Getenv("OPENFLUX_TEST_NETFILTER") != "1" {
		t.Skip("set OPENFLUX_TEST_NETFILTER=1 in an isolated Linux VM/net namespace")
	}
	g := New(t.TempDir())
	if e := g.Ensure(); e != nil {
		t.Fatal(e)
	}
	defer func() {
		if e := g.Clear(); e != nil {
			t.Error(e)
		}
	}()
	listener, e := net.Listen("tcp4", "127.0.0.1:0")
	if e != nil {
		t.Fatal(e)
	}
	defer listener.Close()
	destination := uint16(listener.Addr().(*net.TCPAddr).Port)
	reservation, e := net.Listen("tcp4", "127.0.0.1:0")
	if e != nil {
		t.Fatal(e)
	}
	source := uint16(reservation.Addr().(*net.TCPAddr).Port)
	reservation.Close()
	fd, e := syscall.Socket(syscall.AF_INET, syscall.SOCK_RAW, syscall.IPPROTO_RAW)
	if e != nil {
		t.Fatal(e)
	}
	defer syscall.Close(fd)
	if e = syscall.SetsockoptInt(fd, syscall.IPPROTO_IP, syscall.IP_HDRINCL, 1); e != nil {
		t.Fatal(e)
	}
	send := func(src, dst uint16, flags byte, mark int) {
		t.Helper()
		if e := syscall.SetsockoptInt(fd, syscall.SOL_SOCKET, syscall.SO_MARK, mark); e != nil {
			t.Fatal(e)
		}
		if e := syscall.Sendto(fd, tcpPacket(src, dst, flags), 0, &syscall.SockaddrInet4{Addr: [4]byte{127, 0, 0, 1}}); e != nil {
			t.Fatal(e)
		}
	}
	counter := func() uint64 {
		t.Helper()
		table := &nftables.Table{Name: g.table, Family: nftables.TableFamilyIPv4}
		rules, e := (&nftables.Conn{}).GetRules(table, &nftables.Chain{Name: "output", Table: table})
		if e != nil {
			t.Fatal(e)
		}
		for _, r := range rules {
			for _, x := range r.Exprs {
				if counter, ok := x.(*expr.Counter); ok {
					return counter.Packets
				}
			}
		}
		t.Fatal("RST counter missing")
		return 0
	}
	// The real Linux TCP stack replies to the SYN-ACK with RST because
	// the marked raw sender never opened a kernel TCP connection.
	send(source, destination, 2, int(g.mark))
	deadline := time.Now().Add(3 * time.Second)
	for counter() == 0 && time.Now().Before(deadline) {
		time.Sleep(20 * time.Millisecond)
	}
	if counter() == 0 {
		t.Fatal("kernel RST was not dropped")
	}
	before := counter()
	// A forwarded client RST must leave, and RSTs with either a different
	// source port or destination port must not match the guarded tuple.
	send(source, destination, 4, int(g.mark))
	send(source+1, destination, 4, 0)
	send(source, destination+1, 4, 0)
	time.Sleep(50 * time.Millisecond)
	if got := counter(); got != before {
		t.Fatalf("client/foreign RSTs dropped: %d -> %d", before, got)
	}
	// Host TCP to a closed port still gets ECONNREFUSED, rather than timing out.
	closed, e := net.Listen("tcp4", "127.0.0.1:0")
	if e != nil {
		t.Fatal(e)
	}
	address := closed.Addr().String()
	closed.Close()
	if conn, e := net.DialTimeout("tcp4", address, time.Second); e == nil {
		conn.Close()
		t.Fatal("closed port connected")
	} else if !errors.Is(e, syscall.ECONNREFUSED) {
		t.Fatal("host RST suppressed", e)
	}
	// Simulate a panel crash: the next instance removes the owned table
	// using the journal, with no nft command installed or executed.
	if e = New(g.dir).Clear(); e != nil {
		t.Fatal(e)
	}
	if owned, e := g.backend.owned(g); e != nil || owned {
		t.Fatal("firewall table retained", e)
	}
}
