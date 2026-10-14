package panel

import (
	"bufio"
	"io"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"time"
)

type LogLine struct {
	Timestamp time.Time `json:"timestamp"`
	Level     string    `json:"level"`
	Message   string    `json:"message"`
}
type Logs struct {
	mu    sync.Mutex
	dir   string
	lines map[string][]LogLine
}

func newLogs(dir string) (*Logs, error) {
	if e := os.MkdirAll(dir, 0700); e != nil {
		return nil, e
	}
	return &Logs{dir: dir, lines: map[string][]LogLine{}}, nil
}
func (l *Logs) Append(id, line string) {
	l.AppendLevel(id, "info", line)
}
func (l *Logs) AppendLevel(id, level, line string) {
	l.mu.Lock()
	defer l.mu.Unlock()
	l.loadLocked(id)
	if len(line) > 16000 {
		line = line[:16000] + "…"
	}
	entry := LogLine{Timestamp: time.Now().UTC(), Level: level, Message: line}
	list := append(l.lines[id], entry)
	if len(list) > 500 {
		list = append([]LogLine(nil), list[len(list)-500:]...)
	}
	l.lines[id] = list
	path := filepath.Join(l.dir, id+".log")
	if stat, e := os.Stat(path); e == nil && stat.Size() > 10<<20 {
		_ = os.Remove(path + ".1")
		_ = os.Rename(path, path+".1")
	}
	f, e := os.OpenFile(path, os.O_CREATE|os.O_WRONLY|os.O_APPEND, 0600)
	if e == nil {
		_, _ = io.WriteString(f, entry.Timestamp.Format(time.RFC3339Nano)+" ["+strings.ToUpper(level)+"] "+line+"\n")
		_ = f.Close()
	}
}
func (l *Logs) Tail(id string, n int) []LogLine {
	l.mu.Lock()
	defer l.mu.Unlock()
	n = max(1, min(n, 500))
	l.loadLocked(id)
	list := l.lines[id]
	if len(list) > n {
		list = list[len(list)-n:]
	}
	return append([]LogLine{}, list...)
}

func (l *Logs) loadLocked(id string) {
	if _, loaded := l.lines[id]; loaded {
		return
	}
	var list []LogLine
	f, e := os.Open(filepath.Join(l.dir, id+".log"))
	if e == nil {
		defer f.Close()
		if stat, e := f.Stat(); e == nil && stat.Size() > 256<<10 {
			_, _ = f.Seek(-(256 << 10), io.SeekEnd)
			r := bufio.NewReader(f)
			_, _ = r.ReadString('\n')
			scan := bufio.NewScanner(r)
			for scan.Scan() {
				list = append(list, parseLogLine(scan.Text()))
			}
		} else {
			scan := bufio.NewScanner(f)
			for scan.Scan() {
				list = append(list, parseLogLine(scan.Text()))
			}
		}
	}
	if len(list) > 500 {
		list = list[len(list)-500:]
	}
	l.lines[id] = list
}

func parseLogLine(line string) LogLine {
	entry := LogLine{Level: "info", Message: line}
	stamp, message, ok := strings.Cut(line, " ")
	if !ok {
		return entry
	}
	if timestamp, err := time.Parse(time.RFC3339Nano, stamp); err == nil {
		entry.Timestamp = timestamp
		entry.Message = message
		for _, level := range []string{"info", "warn", "error", "debug"} {
			if body, found := strings.CutPrefix(message, "["+strings.ToUpper(level)+"] "); found {
				entry.Level, entry.Message = level, body
				break
			}
		}
	}
	return entry
}
func (l *Logs) Clear(id string) error {
	l.mu.Lock()
	defer l.mu.Unlock()
	delete(l.lines, id)
	for _, suffix := range []string{".log", ".log.1"} {
		if e := os.Remove(filepath.Join(l.dir, id+suffix)); e != nil && !os.IsNotExist(e) {
			return e
		}
	}
	return nil
}

type lineWriter struct {
	mu      sync.Mutex
	pending string
	consume func(string)
}

func (w *lineWriter) Write(p []byte) (int, error) {
	w.mu.Lock()
	defer w.mu.Unlock()
	w.pending += string(p)
	for {
		idx := strings.IndexByte(w.pending, '\n')
		if idx < 0 {
			break
		}
		w.consume(strings.TrimRight(w.pending[:idx], "\r"))
		w.pending = w.pending[idx+1:]
	}
	if len(w.pending) > 16000 {
		w.consume(w.pending[:16000])
		w.pending = ""
	}
	return len(p), nil
}
func (w *lineWriter) Flush() {
	w.mu.Lock()
	defer w.mu.Unlock()
	if w.pending != "" {
		w.consume(w.pending)
		w.pending = ""
	}
}
