package l3guard

import "errors"

type systemBackend struct{}

func (systemBackend) owned(*Guard) (bool, error) { return false, errors.New("nftables is Linux-only") }
func (systemBackend) create(*Guard) error        { return errors.New("nftables is Linux-only") }
func (systemBackend) remove(*Guard) error        { return errors.New("nftables is Linux-only") }
