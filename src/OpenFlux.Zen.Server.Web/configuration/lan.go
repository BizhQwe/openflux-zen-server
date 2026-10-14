package configuration

import (
	"fmt"
	"net"
	"net/netip"
	"strings"
)

var privateLANBlocks = []netip.Prefix{netip.MustParsePrefix("10.0.0.0/8"), netip.MustParsePrefix("172.16.0.0/12"), netip.MustParsePrefix("192.168.0.0/16"), netip.MustParsePrefix("fc00::/7")}

func ParseLANSubnets(value string) ([]netip.Prefix, error) {
	var result []netip.Prefix
	for _, entry := range strings.FieldsFunc(value, func(r rune) bool { return r == ',' || r == ';' || r == ' ' || r == '\t' || r == '\n' || r == '\r' }) {
		prefix, err := netip.ParsePrefix(entry)
		if err != nil {
			return nil, fmt.Errorf("некорректная подсеть LAN: %s", entry)
		}
		prefix = prefix.Masked()
		private := false
		for _, block := range privateLANBlocks {
			if prefix.Bits() >= block.Bits() && block.Contains(prefix.Addr()) {
				private = true
				break
			}
		}
		if !private {
			return nil, fmt.Errorf("LAN допускает только частные подсети, публичные адреса запрещены: %s", entry)
		}
		result = append(result, prefix)
	}
	return result, nil
}

type LANInterface struct {
	Address netip.Addr
	Subnet  netip.Prefix
}

func LANInterfaces() []LANInterface {
	var result []LANInterface
	interfaces, _ := net.Interfaces()
	for _, iface := range interfaces {
		if iface.Flags&net.FlagUp == 0 || iface.Flags&net.FlagLoopback != 0 {
			continue
		}
		addresses, _ := iface.Addrs()
		for _, address := range addresses {
			prefix, err := netip.ParsePrefix(address.String())
			if err != nil || !prefix.Addr().IsPrivate() {
				continue
			}
			if _, err = ParseLANSubnets(prefix.String()); err != nil {
				continue
			}
			result = append(result, LANInterface{Address: prefix.Addr(), Subnet: prefix.Masked()})
		}
	}
	return result
}

func LANPrefixes(s Settings) []netip.Prefix {
	if strings.TrimSpace(s.LANSubnets) != "" {
		prefixes, _ := ParseLANSubnets(s.LANSubnets)
		return prefixes
	}
	var prefixes []netip.Prefix
	seen := map[netip.Prefix]bool{}
	for _, iface := range LANInterfaces() {
		if !seen[iface.Subnet] {
			prefixes = append(prefixes, iface.Subnet)
			seen[iface.Subnet] = true
		}
	}
	return prefixes
}

func LANAvailable(s Settings) bool { return len(LANPrefixes(s)) > 0 }

func LANClientAllowed(s Settings, address netip.Addr) bool {
	address = address.Unmap().WithZone("")
	if !address.IsPrivate() || !LANEnabled(s) {
		return false
	}
	for _, prefix := range LANPrefixes(s) {
		if prefix.Contains(address) {
			return true
		}
	}
	return false
}

func LANAddress(s Settings) string {
	if !LANEnabled(s) {
		return ""
	}
	_, port, _ := net.SplitHostPort(s.Listen)
	for _, iface := range LANInterfaces() {
		if LANClientAllowed(s, iface.Address) {
			return net.JoinHostPort(iface.Address.String(), port)
		}
	}
	return ""
}
