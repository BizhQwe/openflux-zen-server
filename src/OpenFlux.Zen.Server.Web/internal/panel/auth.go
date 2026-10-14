package panel

import (
	"openflux.local/zen/panel/configuration"
	"sync"
	"time"
)

var HashPassword = configuration.HashPassword
var CheckPassword = configuration.CheckPassword

type Auth struct {
	mu       sync.Mutex
	tokens   map[string]time.Time
	attempts map[string][]time.Time
}

func newAuth() *Auth {
	return &Auth{tokens: map[string]time.Time{}, attempts: map[string][]time.Time{}}
}
func (a *Auth) issue() string {
	a.mu.Lock()
	defer a.mu.Unlock()
	for k, t := range a.tokens {
		if time.Now().After(t) {
			delete(a.tokens, k)
		}
	}
	token := RandomSecret(32)
	a.tokens[token] = time.Now().Add(24 * time.Hour)
	return token
}
func (a *Auth) valid(token string) bool {
	a.mu.Lock()
	defer a.mu.Unlock()
	t, ok := a.tokens[token]
	return ok && time.Now().Before(t)
}
func (a *Auth) revoke(token string) { a.mu.Lock(); defer a.mu.Unlock(); delete(a.tokens, token) }
func (a *Auth) clear()              { a.mu.Lock(); defer a.mu.Unlock(); a.tokens = map[string]time.Time{} }
func (a *Auth) allow(ip string) bool {
	a.mu.Lock()
	defer a.mu.Unlock()
	now := time.Now()
	for k, list := range a.attempts {
		fresh := list[:0]
		for _, t := range list {
			if now.Sub(t) < time.Minute {
				fresh = append(fresh, t)
			}
		}
		if len(fresh) == 0 {
			delete(a.attempts, k)
		} else {
			a.attempts[k] = fresh
		}
	}
	list := a.attempts[ip]
	if len(list) >= 10 || len(a.attempts) > 10000 {
		return false
	}
	a.attempts[ip] = append(list, now)
	return true
}
