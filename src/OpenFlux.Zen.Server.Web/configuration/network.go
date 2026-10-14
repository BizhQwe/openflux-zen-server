package configuration

import (
	"crypto/tls"
	"errors"
	"fmt"
	"net"
	"net/url"
	"os"
	"path/filepath"
	"strconv"
	"strings"
)

func ReadSettings(dir string) (Settings, error) {
	var s Settings
	store := &Store{Dir: dir}
	if err := store.Read("settings.json", &s); err != nil {
		return s, err
	}
	return s, ValidateSettings(s)
}

func ValidateNetwork(s Settings) error {
	if s.LANAccessMode != "" && s.LANAccessMode != "on" && s.LANAccessMode != "off" {
		return errors.New("неверный режим доступа LAN")
	}
	if _, err := ParseLANSubnets(s.LANSubnets); err != nil {
		return err
	}
	if (s.TLSCert == "") != (s.TLSKey == "") {
		return errors.New("задайте сертификат и ключ TLS вместе")
	}
	for _, address := range []string{s.LocaltunnelHost} {
		if address == "" {
			continue
		}
		u, err := url.Parse(address)
		if err != nil || u.Host == "" || u.User != nil || (u.Scheme != "http" && u.Scheme != "https") {
			return errors.New("адрес должен начинаться с http:// или https:// и не содержать пароль")
		}
	}
	if s.DomainEnabled && s.Domain == "" {
		return errors.New("не задан домен")
	}
	if s.Domain != "" {
		if _, err := DomainAddress(s.Domain); err != nil {
			return err
		}
	}
	return nil
}

func DomainAddress(value string) (string, error) {
	value = strings.TrimSpace(value)
	if !strings.Contains(value, "://") {
		value = "https://" + value
	}
	u, err := url.Parse(value)
	if err != nil || u.Hostname() == "" || u.User != nil || u.RawQuery != "" || u.Fragment != "" || (u.Path != "" && u.Path != "/") || (u.Scheme != "http" && u.Scheme != "https") {
		return "", errors.New("укажите домен/IP с необязательным портом, без пути и учётных данных")
	}
	if strings.ContainsAny(u.Hostname(), " /\\\t\r\n") {
		return "", errors.New("некорректный домен")
	}
	if p := u.Port(); p != "" {
		n, e := strconv.Atoi(p)
		if e != nil || n < 1 || n > 65535 {
			return "", errors.New("некорректный порт домена")
		}
	}
	return strings.TrimSuffix(u.String(), "/"), nil
}

// A hostname uses this panel's protocol and port. An explicit URL describes an
// existing reverse proxy and keeps its external protocol and port.
func DomainURL(s Settings) (string, error) {
	base, err := DomainAddress(s.Domain)
	if err != nil {
		return "", err
	}
	if !strings.Contains(s.Domain, "://") {
		u, _ := url.Parse(base)
		_, port, _ := net.SplitHostPort(s.Listen)
		u.Scheme = "http"
		if s.TLSCert != "" && net.ParseIP(u.Hostname()) == nil && !strings.EqualFold(u.Hostname(), "localhost") {
			u.Scheme = "https"
		}
		u.Host = net.JoinHostPort(u.Hostname(), port)
		if (u.Scheme == "http" && port == "80") || (u.Scheme == "https" && port == "443") {
			u.Host = u.Hostname()
		}
		base = u.String()
	}
	return base + "/" + s.SecretPath + "/", nil
}

func LocalAddress(s Settings) string {
	host, port, _ := net.SplitHostPort(s.Listen)
	if ip := net.ParseIP(host); host == "" || (ip != nil && ip.IsUnspecified()) {
		host = "127.0.0.1"
	}
	return net.JoinHostPort(host, port)
}
func LANEnabled(s Settings) bool {
	if s.LANAccessMode == "off" {
		return false
	}
	host, _, _ := net.SplitHostPort(s.Listen)
	ip := net.ParseIP(host)
	return host != "localhost" && (ip == nil || !ip.IsLoopback()) && LANAvailable(s)
}
func SetLAN(s *Settings, enabled bool) {
	s.LANAccessMode = "off"
	if enabled {
		s.LANAccessMode = "on"
	}
	SetPublicListen(s, enabled)
}
func SetPublicListen(s *Settings, enabled bool) {
	_, port, _ := net.SplitHostPort(s.Listen)
	host := "127.0.0.1"
	if enabled {
		host = "0.0.0.0"
	}
	s.Listen = net.JoinHostPort(host, port)
}
func PanelURLs(s Settings) map[string]string {
	scheme := "http"
	urls := map[string]string{"localhost": scheme + "://" + LocalAddress(s) + "/" + s.SecretPath + "/"}
	if address := LANAddress(s); address != "" {
		urls["lan"] = scheme + "://" + address + "/" + s.SecretPath + "/"
	}
	if s.DomainEnabled {
		if address, e := DomainURL(s); e == nil {
			urls["domain"] = address
		}
	}
	return urls
}
func CheckTLS(cert, key string) error {
	if _, err := tls.LoadX509KeyPair(cert, key); err != nil {
		return fmt.Errorf("сертификат TLS: %w", err)
	}
	return nil
}
func DetectTLS(domain string) (string, string, error) {
	base, err := DomainAddress(domain)
	if err != nil {
		return "", "", err
	}
	u, _ := url.Parse(base)
	host := u.Hostname()
	if strings.ContainsAny(host, "/\\") || host == "." || host == ".." {
		return "", "", errors.New("некорректный домен")
	}
	var pairs [][2]string
	for _, home := range []string{os.Getenv("HOME"), "/root"} {
		if home == "" {
			continue
		}
		for _, suffix := range []string{"", "_ecc"} {
			d := filepath.Join(home, ".acme.sh", host+suffix)
			pairs = append(pairs, [2]string{filepath.Join(d, "fullchain.cer"), filepath.Join(d, host+".key")})
		}
	}
	d := filepath.Join("/etc/letsencrypt/live", host)
	pairs = append(pairs, [2]string{filepath.Join(d, "fullchain.pem"), filepath.Join(d, "privkey.pem")})
	for _, pair := range pairs {
		if CheckTLS(pair[0], pair[1]) == nil {
			return pair[0], pair[1], nil
		}
	}
	return "", "", errors.New("готовый сертификат домена не найден")
}
