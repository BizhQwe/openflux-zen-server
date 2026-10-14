package configuration

import (
	"crypto/sha256"
	"crypto/subtle"
	"encoding/base64"
	"golang.org/x/crypto/pbkdf2"
)

func HashPassword(password, salt string) string {
	return base64.StdEncoding.EncodeToString(pbkdf2.Key([]byte(password), []byte(salt), 100000, 32, sha256.New))
}
func CheckPassword(password string, s Settings) bool {
	return subtle.ConstantTimeCompare([]byte(HashPassword(password, s.PasswordSalt)), []byte(s.PasswordHash)) == 1
}
