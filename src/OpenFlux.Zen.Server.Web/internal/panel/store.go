package panel

import (
	"crypto/rand"
	"errors"
	"fmt"
	"openflux.local/zen/panel/configuration"
	"os"
	"strings"
	"time"
)

type Settings = configuration.Settings
type Store struct{ *configuration.Store }

var RandomSecret = configuration.RandomSecret
var RandomAccessPath = configuration.RandomAccessPath
var RandomPassword = configuration.RandomPassword
var ValidateSettings = configuration.ValidateSettings

func NewStore(dir string) (*Store, error) {
	store, err := configuration.NewStore(dir)
	if err != nil {
		return nil, err
	}
	return &Store{Store: store}, nil
}

func (s *Store) LoadTunnels() ([]Tunnel, error) {
	tunnels := []Tunnel{}
	if err := s.Read("tunnels.json", &tunnels); err != nil && !errors.Is(err, os.ErrNotExist) {
		return nil, fmt.Errorf("tunnels.json: %w", err)
	}
	seen := map[string]bool{}
	for i := range tunnels {
		t := &tunnels[i]
		t.ID = strings.ToLower(t.ID)
		if !validID(t.ID) || seen[t.ID] {
			return nil, errors.New("некорректный или повторяющийся id в tunnels.json")
		}
		seen[t.ID] = true
		t.Status = Stopped
		t.Challenge = nil
		t.ConnectedClients = 0
		t.ClientStatus = ""
		t.UploadRate = 0
		t.DownloadRate = 0
		t.RestartAttempts = 0
		// Older data may contain client profiles. Preserve their configuration,
		// but keep them disabled so restore/supervision can never launch them.
		if t.Role == "" {
			t.Role = ServerRole
		}
		var policyError error
		if t.Role != ServerRole {
			policyError = ErrServerOnly
		} else if _, e := SplitArgs(t.ExtraArgs); e != nil {
			policyError = e
		}
		if policyError != nil {
			t.IsEnabled = false
			t.Status = Failed
			t.ErrorMessage = policyError.Error()
		}
	}
	return tunnels, nil
}
func newID() string {
	b := make([]byte, 16)
	if _, e := rand.Read(b); e != nil {
		panic(e)
	}
	b[6] = (b[6] & 15) | 64
	b[8] = (b[8] & 63) | 128
	return fmt.Sprintf("%x-%x-%x-%x-%x", b[:4], b[4:6], b[6:8], b[8:10], b[10:])
}
func validID(s string) bool {
	if len(s) != 36 {
		return false
	}
	for i, c := range s {
		if i == 8 || i == 13 || i == 18 || i == 23 {
			if c != '-' {
				return false
			}
		} else if !strings.ContainsRune("0123456789abcdefABCDEF", c) {
			return false
		}
	}
	return true
}
func nowPointer() *time.Time { t := time.Now().UTC(); return &t }
