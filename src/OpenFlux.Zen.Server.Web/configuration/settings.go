package configuration

import (
	"crypto/rand"
	"encoding/base64"
	"encoding/json"
	"errors"
	"math/big"
	"net"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"sync"
)

type Settings struct {
	Username           string `json:"username"`
	PasswordHash       string `json:"passwordHash,omitempty"`
	PasswordSalt       string `json:"passwordSalt,omitempty"`
	SecretPath         string `json:"secretPath"`
	Listen             string `json:"listen"`
	LANAccessMode      string `json:"lanAccessMode,omitempty"`
	LANSubnets         string `json:"lanSubnets,omitempty"`
	Language           string `json:"language"`
	AutoStartEnabled   bool   `json:"autoStartEnabled"`
	TLSCert            string `json:"tlsCert"`
	TLSKey             string `json:"tlsKey"`
	Domain             string `json:"domain"`
	DomainEnabled      bool   `json:"domainEnabled"`
	LocaltunnelEnabled bool   `json:"localtunnelEnabled"`
	LocaltunnelHost    string `json:"localtunnelHost,omitempty"`
}

type Store struct {
	Dir string
	mu  sync.Mutex
}

const PasswordCharset = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"

const SecretPathLength = 18

func RandomAccessPath() string {
	var path [SecretPathLength]byte
	limit := big.NewInt(int64(len(PasswordCharset)))
	for i := range path {
		n, err := rand.Int(rand.Reader, limit)
		if err != nil {
			panic(err)
		}
		path[i] = PasswordCharset[n.Int64()]
	}
	return string(path[:])
}

func RandomPassword(n int) string {
	if n <= 0 || n > 12 {
		n = 12
	}
	b := make([]byte, n)
	randBytes := make([]byte, n)
	if _, err := rand.Read(randBytes); err != nil {
		panic(err)
	}
	for i := 0; i < n; i++ {
		b[i] = PasswordCharset[int(randBytes[i])%len(PasswordCharset)]
	}
	return string(b)
}

func RandomSecret(n int) string {
	b := make([]byte, n)
	if _, err := rand.Read(b); err != nil {
		panic(err)
	}
	return base64.RawURLEncoding.EncodeToString(b)
}

func NewStore(dir string) (*Store, error) {
	dir, err := filepath.Abs(dir)
	if err != nil {
		return nil, err
	}
	if err = os.MkdirAll(dir, 0700); err != nil {
		return nil, err
	}
	return &Store{Dir: dir}, nil
}
func (s *Store) Read(name string, v any) error {
	s.mu.Lock()
	defer s.mu.Unlock()
	b, e := os.ReadFile(filepath.Join(s.Dir, name))
	if e != nil {
		return e
	}
	return json.Unmarshal(b, v)
}
func (s *Store) Write(name string, v any) error {
	s.mu.Lock()
	defer s.mu.Unlock()
	return AtomicJSON(filepath.Join(s.Dir, name), v)
}
func AtomicJSON(path string, v any) error {
	b, err := json.MarshalIndent(v, "", "  ")
	if err != nil {
		return err
	}
	f, err := os.CreateTemp(filepath.Dir(path), ".pending-*")
	if err != nil {
		return err
	}
	tmp := f.Name()
	defer os.Remove(tmp)
	if err = f.Chmod(0600); err == nil {
		_, err = f.Write(b)
	}
	if err == nil {
		err = f.Sync()
	}
	if e := f.Close(); err == nil {
		err = e
	}
	if err != nil {
		return err
	}
	return os.Rename(tmp, path)
}
func (s *Store) LoadSettings() (Settings, string, error) {
	var settings Settings
	err := s.Read("settings.json", &settings)
	if err == nil {
		return settings, "", ValidateSettings(settings)
	}
	if !errors.Is(err, os.ErrNotExist) {
		return settings, "", err
	}
	password := RandomPassword(12)
	settings = Settings{Username: RandomPassword(12), SecretPath: RandomAccessPath(), Listen: "127.0.0.1:5000", Language: "ru", AutoStartEnabled: true}
	settings.PasswordSalt = RandomSecret(18)
	settings.PasswordHash = HashPassword(password, settings.PasswordSalt)
	return settings, password, s.SavePassword(settings, password)
}
func ValidateSettings(s Settings) error {
	if s.Username == "" || len(s.Username) > 100 {
		return errors.New("некорректный логин")
	}
	if len(s.SecretPath) < 8 || strings.ContainsAny(s.SecretPath, "/\\?#. \t\r\n") {
		return errors.New("секретный путь должен содержать минимум 8 символов без пробелов и разделителей")
	}
	if s.Listen == "" {
		return errors.New("не задан адрес панели")
	}
	_, port, err := net.SplitHostPort(s.Listen)
	if err != nil || strings.ContainsAny(s.Listen, " \t\r\n") {
		return errors.New("адрес панели должен быть host:port")
	}
	number, err := strconv.Atoi(port)
	if err != nil || number < 1 || number > 65535 {
		return errors.New("порт панели должен быть от 1 до 65535")
	}
	return ValidateNetwork(s)
}
