// Package l3guard manages a private, temporary nftables table for Linux L3.
// It never replaces the host ruleset or filters connections by address alone.
package l3guard

import (
	"crypto/sha256"
	"encoding/binary"
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"

	"openflux.local/zen/panel/configuration"
)

const MarkEnv = "OPENFLUX_ZEN_L3_MARK"
const stateFile = "l3-firewall.json"

type backend interface {
	owned(*Guard) (bool, error)
	create(*Guard) error
	remove(*Guard) error
}
type Guard struct {
	dir, table, owner string
	mark              uint32
	backend           backend
	ready             bool
}

func New(dir string) *Guard {
	abs, _ := filepath.Abs(dir)
	if real, e := filepath.EvalSymlinks(abs); e == nil {
		abs = real
	}
	hash := sha256.Sum256([]byte(filepath.Clean(abs)))
	id := fmt.Sprintf("%x", hash[:8])
	return &Guard{dir: abs, table: "openflux_zen_l3_" + id, owner: "OpenFlux Zen Server L3 " + id, mark: 0x4f000000 | binary.BigEndian.Uint32(hash[8:12])&0x00ffffff, backend: systemBackend{}}
}
func (g *Guard) Ensure() error {
	if g.ready {
		return nil
	}
	owned, e := g.backend.owned(g)
	if e != nil {
		return fmt.Errorf("L3: требуется поддержка nftables в ядре Linux и CAP_NET_ADMIN (служба установщика работает от root): %w", e)
	}
	if owned {
		return errors.New("L3: обнаружена оставшаяся таблица; перезапустите службу панели для очистки")
	}
	// Write before committing the atomic netlink batch, so an interrupted
	// start can always recover its exact table without scanning other rules.
	if e = configuration.AtomicJSON(filepath.Join(g.dir, stateFile), map[string]string{"owner": g.owner}); e != nil {
		return e
	}
	if e = g.backend.create(g); e != nil {
		return errors.Join(fmt.Errorf("L3: не удалось включить защиту TCP RST: %w", e), g.Clear())
	}
	g.ready = true
	return nil
}
func (g *Guard) Clear() error {
	path := filepath.Join(g.dir, stateFile)
	data, e := os.ReadFile(path)
	if os.IsNotExist(e) {
		g.ready = false
		return nil
	}
	if e != nil {
		return e
	}
	var state struct{ Owner string }
	if e = json.Unmarshal(data, &state); e != nil {
		return e
	}
	if state.Owner != g.owner {
		return errors.New("L3: метка владельца таблицы не соответствует каталогу установки")
	}
	owned, e := g.backend.owned(g)
	if e != nil {
		return e
	}
	if owned {
		if e = g.backend.remove(g); e != nil {
			return e
		}
	}
	if e = os.Remove(path); e != nil && !os.IsNotExist(e) {
		return e
	}
	g.ready = false
	return nil
}
