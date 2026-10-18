package panel

import (
	"crypto/sha256"
	"encoding/hex"
	"errors"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"regexp"
	"strings"
	"time"
)

const maxInstallerBytes int64 = 256 << 20

var releaseTagPattern = regexp.MustCompile(`^v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z]+([.-][0-9A-Za-z]+)*)?$`)

func releaseTag(value string) (string, error) {
	tag := "v" + cleanVersion(value)
	if len(tag) > 100 || !releaseTagPattern.MatchString(tag) {
		return "", errors.New("некорректная версия релиза")
	}
	return tag, nil
}

func updateHTTPClient(timeout time.Duration) *http.Client {
	return &http.Client{Timeout: timeout, CheckRedirect: func(req *http.Request, via []*http.Request) error {
		if req.URL.Scheme != "https" || req.URL.User != nil || len(via) >= 10 {
			return errors.New("небезопасное перенаправление обновления")
		}
		return nil
	}}
}

func fetchUpdateFile(client *http.Client, address string, dst io.Writer, limit int64) error {
	u, err := url.Parse(address)
	if err != nil || u.Scheme != "https" || u.Host == "" || u.User != nil {
		return errors.New("обновления требуют HTTPS")
	}
	req, err := http.NewRequest(http.MethodGet, address, nil)
	if err != nil {
		return err
	}
	req.Header.Set("User-Agent", "OpenFluxZenServer-PanelUpdater/"+Version)
	resp, err := client.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return fmt.Errorf("сервер обновлений: HTTP %d", resp.StatusCode)
	}
	if resp.ContentLength > limit {
		return errors.New("файл обновления превышает допустимый размер")
	}
	n, err := io.Copy(dst, io.LimitReader(resp.Body, limit+1))
	if err != nil {
		return err
	}
	if n == 0 || n > limit {
		return errors.New("недопустимый размер файла обновления")
	}
	return nil
}

// Both files come from the same tag URL. Missing/invalid checksums fail closed;
// a selected release never silently falls back to latest.
func downloadPanelInstaller(client *http.Client, base, assetName, dir string) (string, error) {
	if assetName == "" || filepath.Base(assetName) != assetName || strings.ContainsAny(assetName, `/\`) {
		return "", errors.New("некорректное имя установщика")
	}
	var checksum strings.Builder
	if err := fetchUpdateFile(client, base+"/"+assetName+".sha256", &checksum, 4096); err != nil {
		return "", fmt.Errorf("загрузка SHA-256: %w", err)
	}
	fields := strings.Fields(checksum.String())
	if len(fields) != 2 || len(fields[0]) != 64 || strings.TrimPrefix(fields[1], "*") != assetName {
		return "", errors.New("некорректный файл SHA-256")
	}
	if _, err := hex.DecodeString(fields[0]); err != nil {
		return "", errors.New("некорректная сумма SHA-256")
	}
	installer := filepath.Join(dir, assetName)
	f, err := os.OpenFile(installer, os.O_CREATE|os.O_EXCL|os.O_WRONLY, 0600)
	if err != nil {
		return "", err
	}
	verified := false
	defer func() {
		_ = f.Close()
		if !verified {
			_ = os.Remove(installer)
		}
	}()
	hash := sha256.New()
	if err = fetchUpdateFile(client, base+"/"+assetName, io.MultiWriter(f, hash), maxInstallerBytes); err != nil {
		return "", err
	}
	if err = f.Close(); err != nil {
		return "", err
	}
	if !strings.EqualFold(hex.EncodeToString(hash.Sum(nil)), fields[0]) {
		return "", errors.New("SHA-256 установщика не совпадает; обновление отменено")
	}
	if err = os.Chmod(installer, 0700); err != nil {
		return "", err
	}
	verified = true
	return installer, nil
}
