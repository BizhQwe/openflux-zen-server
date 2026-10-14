package main

import (
	"fmt"
	"net"
	"strconv"

	"openflux.local/zen/panel/configuration"
)

// Only a fresh installation may choose another default port. Explicit ports
// and existing installations retain their address and refuse conflicts.
func freshPanelPort(s *configuration.Settings, explicit bool) error {
	host, port, err := net.SplitHostPort(s.Listen)
	if err != nil {
		return err
	}
	start, err := strconv.Atoi(port)
	if err != nil {
		return err
	}
	for i := 0; i < 100; i++ {
		candidate := start + i
		if candidate > 65535 {
			candidate = 5000 + i
		}
		address := net.JoinHostPort(host, strconv.Itoa(candidate))
		listener, err := net.Listen("tcp", address)
		if err == nil {
			listener.Close()
			s.Listen = address
			return nil
		}
		if explicit {
			return fmt.Errorf("порт панели %s занят или недоступен; укажите свободный --port, другие службы не изменялись: %w", address, err)
		}
	}
	return fmt.Errorf("не найден свободный порт панели; укажите --port")
}
