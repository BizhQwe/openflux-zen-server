package panel

import (
	"net/http"
	"net/netip"
	"net/url"
	"strings"

	"golang.org/x/net/idna"
	"openflux.local/zen/panel/configuration"
	"openflux.local/zen/panel/publication"
)

func networkHost(value string) string {
	u, err := url.Parse("//" + value)
	if err != nil || u.User != nil {
		return ""
	}
	host, err := idna.Lookup.ToASCII(strings.TrimSuffix(strings.ToLower(u.Hostname()), "."))
	if err != nil {
		return ""
	}
	return host
}

func (s *Server) networkAccessAllowed(r *http.Request, settings Settings) bool {
	peer, err := netip.ParseAddrPort(r.RemoteAddr)
	if err == nil {
		ip := peer.Addr().Unmap()
		if ip.IsLoopback() || configuration.LANClientAllowed(settings, ip) {
			return true
		}
	}
	// This marker can only be created by the built-in relay connection,
	// never by Host, Forwarded or X-Forwarded-For headers from a client.
	if settings.LocaltunnelEnabled {
		if origin := publication.RelayOrigin(r); origin != "" {
			u, e := url.Parse(origin)
			if e == nil && u.Scheme == "https" && r.TLS != nil && networkHost(r.Host) == networkHost(u.Host) {
				return true
			}
		}
	}
	if r.TLS == nil || !settings.DomainEnabled {
		return false
	}
	base, err := configuration.DomainAddress(settings.Domain)
	if err != nil {
		return false
	}
	domain, _ := url.Parse(base)
	host := networkHost(domain.Host)
	if domain.Scheme != "https" || host == "" || host == "localhost" {
		return false
	}
	if _, err = netip.ParseAddr(host); err == nil {
		return false
	}
	return networkHost(r.Host) == host && networkHost(r.TLS.ServerName) == host
}
