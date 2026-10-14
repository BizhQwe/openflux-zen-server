package panel

import (
	"sync"
	"time"
)

type hostStats struct {
	CPUPercent    float64
	MemoryUsed    uint64
	MemoryTotal   uint64
	MemoryPercent float64
}

type hostSampler struct {
	mu          sync.Mutex
	total, idle uint64
	last        time.Time
	cpu         float64
}

func (s *hostSampler) read() hostStats {
	s.mu.Lock()
	defer s.mu.Unlock()
	total, idle, used, memory := readHostCounters()
	now := time.Now()
	if s.last.IsZero() || now.Sub(s.last) >= 250*time.Millisecond {
		if !s.last.IsZero() && total > s.total && idle >= s.idle {
			delta, idleDelta := total-s.total, idle-s.idle
			if idleDelta <= delta {
				s.cpu = float64(delta-idleDelta) / float64(delta) * 100
			}
		}
		s.total, s.idle, s.last = total, idle, now
	}
	percent := float64(0)
	if memory > 0 {
		percent = float64(used) / float64(memory) * 100
	}
	return hostStats{CPUPercent: s.cpu, MemoryUsed: used, MemoryTotal: memory, MemoryPercent: percent}
}
