package panel

import (
	"os"
	"strconv"
	"strings"
)

func readHostCounters() (total, idle, used, memory uint64) {
	if data, err := os.ReadFile("/proc/stat"); err == nil {
		line, _, _ := strings.Cut(string(data), "\n")
		fields := strings.Fields(line)
		if len(fields) >= 5 && fields[0] == "cpu" {
			// Guest counters are already included in user/nice, so exclude them.
			for i := 1; i < len(fields) && i <= 8; i++ {
				value, _ := strconv.ParseUint(fields[i], 10, 64)
				total += value
				if i == 4 || i == 5 {
					idle += value
				}
			}
		}
	}
	if data, err := os.ReadFile("/proc/meminfo"); err == nil {
		values := map[string]uint64{}
		for _, line := range strings.Split(string(data), "\n") {
			fields := strings.Fields(line)
			if len(fields) >= 2 {
				value, _ := strconv.ParseUint(fields[1], 10, 64)
				values[strings.TrimSuffix(fields[0], ":")] = value * 1024
			}
		}
		memory = values["MemTotal"]
		available, exists := values["MemAvailable"]
		if !exists {
			available = values["MemFree"] + values["Buffers"] + values["Cached"] + values["SReclaimable"]
		}
		if available <= memory {
			used = memory - available
		}
	}
	return
}
