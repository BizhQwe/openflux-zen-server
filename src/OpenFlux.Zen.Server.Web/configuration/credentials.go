package configuration

import (
	"encoding/json"
	"errors"
	"os"
	"path/filepath"
)

const CredentialsFile = ".credentials"

var ErrPasswordUnavailable = errors.New("сохранённый пароль отсутствует или больше не соответствует настройкам")

type localCredentials struct {
	Password string `json:"password"`
}

// The installer and local CLI may display this file. It is never part of
// Settings, HTTP responses or tunnel exports. AtomicJSON writes it with 0600;
// Windows installations inherit the protected data-directory ACL.
func ReadDisplayPassword(dir string, settings Settings) (string, error) {
	var credentials localCredentials
	if e := (&Store{Dir: dir}).Read(CredentialsFile, &credentials); e != nil {
		return "", e
	}
	if credentials.Password == "" || !CheckPassword(credentials.Password, settings) {
		return "", ErrPasswordUnavailable
	}
	return credentials.Password, nil
}

// SavePassword commits the recoverable local password and its authentication
// hash together. A failed settings write restores the previous credential file.
func (s *Store) SavePassword(settings Settings, password string) error {
	if !CheckPassword(password, settings) {
		return ErrPasswordUnavailable
	}
	if e := ValidateSettings(settings); e != nil {
		return e
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	file := filepath.Join(s.Dir, CredentialsFile)
	previous, e := os.ReadFile(file)
	if e != nil && !os.IsNotExist(e) {
		return e
	}
	existed := e == nil
	if e = AtomicJSON(file, localCredentials{Password: password}); e != nil {
		return e
	}
	if e = AtomicJSON(filepath.Join(s.Dir, "settings.json"), settings); e != nil {
		var rollback error
		if existed {
			rollback = AtomicJSON(file, json.RawMessage(previous))
		} else {
			rollback = os.Remove(file)
		}
		return errors.Join(e, rollback)
	}
	return nil
}

// RememberPassword adds display support to an installation that only had a
// hash, without changing its existing settings or login password.
func RememberPassword(dir string, settings Settings, password string) error {
	if !CheckPassword(password, settings) {
		return ErrPasswordUnavailable
	}
	return AtomicJSON(filepath.Join(dir, CredentialsFile), localCredentials{Password: password})
}
