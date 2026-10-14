package configuration

import (
	"context"
	"crypto/ecdsa"
	"crypto/elliptic"
	"crypto/rand"
	"crypto/x509"
	"encoding/pem"
	"errors"
	"fmt"
	"net"
	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"time"

	"golang.org/x/crypto/acme"
	"golang.org/x/net/idna"
)

func certificateHost(domain string) (string, error) {
	base, err := DomainAddress(domain)
	if err != nil {
		return "", err
	}
	u, _ := url.Parse(base)
	host, err := idna.Lookup.ToASCII(strings.ToLower(strings.TrimSuffix(u.Hostname(), ".")))
	if err != nil || net.ParseIP(host) != nil || !strings.Contains(host, ".") || len(host) > 253 {
		return "", errors.New("для сертификата нужен публичный домен")
	}
	for _, label := range strings.Split(host, ".") {
		if len(label) == 0 || len(label) > 63 || label[0] == '-' || label[len(label)-1] == '-' {
			return "", errors.New("некорректный домен сертификата")
		}
		for _, r := range label {
			if !(r >= 'a' && r <= 'z' || r >= '0' && r <= '9' || r == '-') {
				return "", errors.New("некорректный домен сертификата")
			}
		}
	}
	return host, nil
}

func tlsPaths(dir, host string) (string, string) {
	base := filepath.Join(dir, "tls", host)
	return filepath.Join(base, "fullchain.pem"), filepath.Join(base, "privkey.pem")
}

func saveTLSFile(path string, value []byte) error {
	temp, err := os.CreateTemp(filepath.Dir(path), ".tls-*")
	if err != nil {
		return err
	}
	name := temp.Name()
	defer os.Remove(name)
	if err = temp.Chmod(0600); err == nil {
		_, err = temp.Write(value)
	}
	if err == nil {
		err = temp.Sync()
	}
	closeErr := temp.Close()
	if err != nil {
		return err
	}
	if closeErr != nil {
		return closeErr
	}
	return os.Rename(name, path)
}

func tlsPrivateKey(path string) (*ecdsa.PrivateKey, error) {
	data, err := os.ReadFile(path)
	if err == nil {
		block, _ := pem.Decode(data)
		if block == nil {
			return nil, errors.New("некорректный ключ сертификата")
		}
		key, err := x509.ParseECPrivateKey(block.Bytes)
		if err != nil {
			return nil, err
		}
		return key, nil
	}
	if !errors.Is(err, os.ErrNotExist) {
		return nil, err
	}
	key, err := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
	if err != nil {
		return nil, err
	}
	raw, err := x509.MarshalECPrivateKey(key)
	if err != nil {
		return nil, err
	}
	if err = saveTLSFile(path, pem.EncodeToMemory(&pem.Block{Type: "EC PRIVATE KEY", Bytes: raw})); err != nil {
		return nil, err
	}
	return key, nil
}

// IssueTLS uses HTTP-01 only while issuing a certificate. A busy port 80
// causes a clean refusal; no other listener, firewall rule or service is changed.
func IssueTLS(ctx context.Context, dir, domain string) (string, string, error) {
	host, err := certificateHost(domain)
	if err != nil {
		return "", "", err
	}
	listener, err := net.Listen("tcp", "0.0.0.0:80")
	if err != nil {
		return "", "", fmt.Errorf("порт 80 занят или недоступен; используйте сертификат/HTTPS существующего reverse proxy, другие службы не изменялись: %w", err)
	}
	defer listener.Close()
	certPath, keyPath := tlsPaths(dir, host)
	if err = os.MkdirAll(filepath.Dir(certPath), 0700); err != nil {
		return "", "", err
	}
	account, err := tlsPrivateKey(filepath.Join(filepath.Dir(certPath), "account.pem"))
	if err != nil {
		return "", "", err
	}
	key, err := tlsPrivateKey(keyPath)
	if err != nil {
		return "", "", err
	}
	client := &acme.Client{Key: account, DirectoryURL: acme.LetsEncryptURL, HTTPClient: &http.Client{Timeout: 30 * time.Second}}
	var challengeMu sync.RWMutex
	challenges := map[string]string{}
	server := &http.Server{ReadHeaderTimeout: 5 * time.Second, ReadTimeout: 10 * time.Second, WriteTimeout: 10 * time.Second, IdleTimeout: 10 * time.Second, Handler: http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		challengeMu.RLock()
		value, ok := challenges[r.URL.Path]
		challengeMu.RUnlock()
		if !ok || r.Method != http.MethodGet {
			http.NotFound(w, r)
			return
		}
		w.Header().Set("Content-Type", "text/plain")
		_, _ = w.Write([]byte(value))
	})}
	go func() { _ = server.Serve(listener) }()
	defer server.Close()
	if _, err = client.Register(ctx, &acme.Account{}, acme.AcceptTOS); err != nil {
		return "", "", fmt.Errorf("ACME account: %w", err)
	}
	order, err := client.AuthorizeOrder(ctx, acme.DomainIDs(host))
	if err != nil {
		return "", "", err
	}
	for _, address := range order.AuthzURLs {
		authorization, err := client.GetAuthorization(ctx, address)
		if err != nil {
			return "", "", err
		}
		if authorization.Status == acme.StatusValid {
			continue
		}
		var challenge *acme.Challenge
		for _, candidate := range authorization.Challenges {
			if candidate.Type == "http-01" {
				challenge = candidate
				break
			}
		}
		if challenge == nil {
			return "", "", errors.New("ACME не предложил HTTP-01")
		}
		value, err := client.HTTP01ChallengeResponse(challenge.Token)
		if err != nil {
			return "", "", err
		}
		challengeMu.Lock()
		challenges[client.HTTP01ChallengePath(challenge.Token)] = value
		challengeMu.Unlock()
		if _, err = client.Accept(ctx, challenge); err != nil {
			return "", "", err
		}
		if _, err = client.WaitAuthorization(ctx, address); err != nil {
			return "", "", err
		}
	}
	order, err = client.WaitOrder(ctx, order.URI)
	if err != nil {
		return "", "", err
	}
	csr, err := x509.CreateCertificateRequest(rand.Reader, &x509.CertificateRequest{DNSNames: []string{host}}, key)
	if err != nil {
		return "", "", err
	}
	chain, _, err := client.CreateOrderCert(ctx, order.FinalizeURL, csr, true)
	if err != nil {
		return "", "", err
	}
	if len(chain) == 0 {
		return "", "", errors.New("ACME вернул пустой сертификат")
	}
	leaf, err := x509.ParseCertificate(chain[0])
	if err != nil {
		return "", "", err
	}
	if err = leaf.VerifyHostname(host); err != nil {
		return "", "", err
	}
	var result []byte
	for _, der := range chain {
		result = append(result, pem.EncodeToMemory(&pem.Block{Type: "CERTIFICATE", Bytes: der})...)
	}
	if err = saveTLSFile(certPath, result); err != nil {
		return "", "", err
	}
	return certPath, keyPath, nil
}

// Only certificates issued into this installation's data directory are
// renewed here. Certificates belonging to another panel/proxy are untouched.
func RenewTLS(ctx context.Context, dir string, settings func() Settings, log func(string, string, ...any)) {
	for {
		s := settings()
		host, err := certificateHost(s.Domain)
		if err == nil && s.TLSCert != "" {
			cert, key := tlsPaths(dir, host)
			if filepath.Clean(s.TLSCert) == filepath.Clean(cert) && filepath.Clean(s.TLSKey) == filepath.Clean(key) {
				raw, err := os.ReadFile(cert)
				if err == nil {
					block, _ := pem.Decode(raw)
					if block != nil {
						leaf, err := x509.ParseCertificate(block.Bytes)
						if err == nil && time.Until(leaf.NotAfter) < 30*24*time.Hour {
							child, cancel := context.WithTimeout(ctx, 3*time.Minute)
							_, _, err = IssueTLS(child, dir, s.Domain)
							cancel()
							if err != nil {
								log("warn", "Обновление собственного TLS-сертификата: %v", err)
							} else {
								log("info", "Собственный TLS-сертификат обновлён")
							}
						}
					}
				}
			}
		}
		timer := time.NewTimer(12 * time.Hour)
		select {
		case <-ctx.Done():
			timer.Stop()
			return
		case <-timer.C:
		}
	}
}
