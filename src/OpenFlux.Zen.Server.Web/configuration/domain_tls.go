package configuration

import (
	"crypto/tls"
	"crypto/x509"
	"errors"
	"time"
)

func ValidateDomainTLSName(domain string) error {
	_, err := certificateHost(domain)
	return err
}

func CheckDomainTLS(cert, key, domain string) error {
	host, err := certificateHost(domain)
	if err != nil {
		return err
	}
	pair, err := tls.LoadX509KeyPair(cert, key)
	if err != nil {
		return err
	}
	leaf, err := x509.ParseCertificate(pair.Certificate[0])
	if err != nil {
		return err
	}
	if err := leaf.VerifyHostname(host); err != nil {
		return err
	}
	if now := time.Now(); now.Before(leaf.NotBefore) || !now.Before(leaf.NotAfter) {
		return errors.New("сертификат домена ещё не действует или просрочен")
	}
	return nil
}

// FindDomainTLS also reloads this installation's certificate after a restart;
// certificates for a previously configured domain must not be reused blindly.
func FindDomainTLS(dir, domain, currentCert, currentKey string) (string, string, error) {
	host, err := certificateHost(domain)
	if err != nil {
		return "", "", err
	}
	cert, key := tlsPaths(dir, host)
	if CheckDomainTLS(cert, key, domain) == nil {
		return cert, key, nil
	}
	if currentCert != "" && CheckDomainTLS(currentCert, currentKey, domain) == nil {
		return currentCert, currentKey, nil
	}
	if cert, key, err := DetectTLS(domain); err == nil && CheckDomainTLS(cert, key, domain) == nil {
		return cert, key, nil
	}
	return "", "", errors.New("подходящий сертификат домена не найден")
}
