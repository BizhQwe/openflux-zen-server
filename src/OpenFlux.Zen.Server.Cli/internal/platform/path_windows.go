package platform

import (
	"golang.org/x/sys/windows"
	"golang.org/x/sys/windows/registry"
	"openflux.local/zen/panel/configuration"
	"strings"
	"unsafe"
)

const environmentKey = `SYSTEM\CurrentControlSet\Control\Session Manager\Environment`

func RegisterPath(dir string) (bool, error) {
	key, e := registry.OpenKey(registry.LOCAL_MACHINE, environmentKey, registry.QUERY_VALUE|registry.SET_VALUE)
	if e != nil {
		return false, e
	}
	defer key.Close()
	value, kind, e := key.GetStringValue("Path")
	if e != nil {
		return false, e
	}
	for _, part := range strings.Split(value, ";") {
		if configuration.SamePath(strings.TrimSpace(part), dir) {
			return false, nil
		}
	}
	value = strings.TrimRight(value, ";") + ";" + dir
	if kind == registry.EXPAND_SZ {
		e = key.SetExpandStringValue("Path", value)
	} else {
		e = key.SetStringValue("Path", value)
	}
	if e == nil {
		broadcastEnvironment()
	}
	return true, e
}
func RemovePath(dir string, owned bool) error {
	if !owned {
		return nil
	}
	key, e := registry.OpenKey(registry.LOCAL_MACHINE, environmentKey, registry.QUERY_VALUE|registry.SET_VALUE)
	if e != nil {
		return e
	}
	defer key.Close()
	value, kind, e := key.GetStringValue("Path")
	if e != nil {
		return e
	}
	parts := []string{}
	for _, part := range strings.Split(value, ";") {
		if !configuration.SamePath(strings.TrimSpace(part), dir) {
			parts = append(parts, part)
		}
	}
	value = strings.Join(parts, ";")
	if kind == registry.EXPAND_SZ {
		e = key.SetExpandStringValue("Path", value)
	} else {
		e = key.SetStringValue("Path", value)
	}
	if e == nil {
		broadcastEnvironment()
	}
	return e
}
func broadcastEnvironment() {
	message, _ := windows.UTF16PtrFromString("Environment")
	var result uintptr
	proc := windows.NewLazySystemDLL("user32.dll").NewProc("SendMessageTimeoutW")
	_, _, _ = proc.Call(0xffff, 0x1a, 0, uintptr(unsafe.Pointer(message)), 2, 1000, uintptr(unsafe.Pointer(&result)))
}
