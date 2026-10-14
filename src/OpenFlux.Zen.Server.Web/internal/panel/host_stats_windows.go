package panel

import (
	"syscall"
	"unsafe"
)

var (
	hostKernel       = syscall.NewLazyDLL("kernel32.dll")
	hostSystemTimes  = hostKernel.NewProc("GetSystemTimes")
	hostMemoryStatus = hostKernel.NewProc("GlobalMemoryStatusEx")
)

func readHostCounters() (total, idle, used, memory uint64) {
	var idleTime, kernelTime, userTime syscall.Filetime
	if ok, _, _ := hostSystemTimes.Call(uintptr(unsafe.Pointer(&idleTime)), uintptr(unsafe.Pointer(&kernelTime)), uintptr(unsafe.Pointer(&userTime))); ok != 0 {
		value := func(t syscall.Filetime) uint64 { return uint64(t.HighDateTime)<<32 | uint64(t.LowDateTime) }
		idle, total = value(idleTime), value(kernelTime)+value(userTime)
	}
	// MEMORYSTATUSEX has the same fixed layout on Windows x86, x64 and ARM64.
	var status struct {
		Length, Load                                                                                         uint32
		TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual uint64
	}
	status.Length = uint32(unsafe.Sizeof(status))
	if ok, _, _ := hostMemoryStatus.Call(uintptr(unsafe.Pointer(&status))); ok != 0 {
		memory, used = status.TotalPhys, status.TotalPhys-status.AvailPhys
	}
	return
}
