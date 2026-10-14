package panel

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"net"
	"os"
	"os/exec"
	"path/filepath"
	"regexp"
	"strings"
	"sync"
	"syscall"
	"time"

	"github.com/p1neappleXpress/OpenFlux/transport/ipc"
	"openflux.local/zen/panel/l3guard"
)

type process struct {
	cmd             *exec.Cmd
	done            chan struct{}
	intentional     bool
	conn            net.Conn
	writeMu         sync.Mutex
	lastIn, lastOut uint64
	lastStat        time.Time
	lastSampleMs    int64
	trafficMode     string
	wireIn, wireOut uint64
	statusUpdated   chan struct{}
	l3              bool
	cleanupErr      error
}
type Manager struct {
	mu                sync.Mutex
	op                sync.Mutex
	store             *Store
	core              string
	socketDir         string
	tunnels           map[string]*Tunnel
	processes         map[string]*process
	retry             map[string]time.Time
	logs              *Logs
	closed            bool
	cancel            context.CancelFunc
	bg                sync.WaitGroup
	dirty             bool
	attachProcess     func(*exec.Cmd) error
	closeProcessGroup func() error
	unlockStore       func() error
	l3Guard           *l3guard.Guard
}

var sharePattern = regexp.MustCompile(`openflux://v1/[A-Za-z0-9_-]+`)

func NewManager(store *Store, core string) (*Manager, error) {
	unlock, err := lockStore(store.Dir)
	if err != nil {
		return nil, fmt.Errorf("каталог данных уже занят другой панелью: %w", err)
	}
	success := false
	defer func() {
		if !success {
			_ = unlock()
		}
	}()
	if !filepath.IsAbs(core) {
		var e error
		core, e = filepath.Abs(core)
		if e != nil {
			return nil, e
		}
	}
	list, e := store.LoadTunnels()
	if e != nil {
		return nil, e
	}
	logs, e := newLogs(filepath.Join(store.Dir, "logs"))
	if e != nil {
		return nil, e
	}
	socketDir, e := os.MkdirTemp("", "zen-")
	if e != nil {
		return nil, e
	}
	m := &Manager{store: store, core: core, socketDir: socketDir, tunnels: map[string]*Tunnel{}, processes: map[string]*process{}, retry: map[string]time.Time{}, logs: logs}
	m.SystemLog("info", "Инициализация OpenFlux Zen Server %s: data=%q, core=%q, профилей=%d", Version, store.Dir, core, len(list))
	m.l3Guard = l3guard.New(store.Dir)
	if e := l3guard.Cleanup(store.Dir); e != nil {
		m.SystemLog("error", "L3: очистка правил предыдущего запуска: %v", e)
		_ = os.RemoveAll(socketDir)
		return nil, fmt.Errorf("L3: не удалось очистить оставшееся правило: %w", e)
	}
	attach, closeGroup, e := newProcessGroup()
	if e != nil {
		_ = os.RemoveAll(socketDir)
		return nil, e
	}
	m.attachProcess = attach
	m.closeProcessGroup = closeGroup
	m.unlockStore = unlock
	for i := range list {
		t := list[i]
		m.tunnels[t.ID] = &t
	}
	ctx, cancel := context.WithCancel(context.Background())
	m.cancel = cancel
	m.bg.Add(1)
	go m.supervise(ctx)
	success = true
	m.SystemLog("info", "Панель готова к работе; счётчики сохраняются каждые 5 секунд, диагностика — каждую минуту")
	return m, nil
}
func (m *Manager) List() []Tunnel { m.mu.Lock(); defer m.mu.Unlock(); return m.listLocked() }
func (m *Manager) listLocked() []Tunnel {
	list := make([]Tunnel, 0, len(m.tunnels))
	for _, t := range m.tunnels {
		copy := *t
		if t.Challenge != nil {
			c := *t.Challenge
			copy.Challenge = &c
		}
		list = append(list, copy)
	}
	return list
}
func (m *Manager) Get(id string) (Tunnel, bool) {
	m.mu.Lock()
	defer m.mu.Unlock()
	t, ok := m.tunnels[id]
	if !ok {
		return Tunnel{}, false
	}
	copy := *t
	if t.Challenge != nil {
		c := *t.Challenge
		copy.Challenge = &c
	}
	return copy, true
}
func (m *Manager) saveLocked() error {
	err := m.store.Write("tunnels.json", m.listLocked())
	if err == nil {
		m.dirty = false
	}
	return err
}
func (m *Manager) Upsert(t Tunnel, create bool) (Tunnel, error) {
	m.op.Lock()
	defer m.op.Unlock()
	if t.EncryptionKey == "" && create && (t.Negotiate || t.Transport == "multi" || t.Transport == "direct" || t.Transport == "script") {
		t.EncryptionKey = RandomSecret(32)
	}
	if e := t.Validate(); e != nil {
		return t, e
	}
	m.mu.Lock()
	old, exists := m.tunnels[t.ID]
	if create {
		t.ID = newID()
		exists = false
	} else if !exists {
		m.mu.Unlock()
		return t, os.ErrNotExist
	}
	restart := exists && old.IsEnabled
	m.mu.Unlock()
	if restart {
		if e := m.stopLocked(t.ID, false); e != nil {
			return t, e
		}
	}
	m.mu.Lock()
	defer m.mu.Unlock()
	if m.closed {
		return t, errors.New("панель завершает работу")
	}
	if exists {
		old = m.tunnels[t.ID]
		t.CreatedAt = old.CreatedAt
		t.UploadBytes = old.UploadBytes
		t.DownloadBytes = old.DownloadBytes
		t.IsEnabled = old.IsEnabled
	} else {
		t.CreatedAt = time.Now().UTC()
		t.IsEnabled = false
	}
	t.Status = Stopped
	t.ShareLink = ""
	t.Challenge = nil
	clearLiveStats(&t)
	t.RestartAttempts = 0
	t.ErrorMessage = ""
	t.LastStoppedAt = nil
	t.LastStartedAt = nil
	t.UpdatedAt = time.Now().UTC()
	m.tunnels[t.ID] = &t
	if e := m.saveLocked(); e != nil {
		if exists {
			m.tunnels[t.ID] = old
		} else {
			delete(m.tunnels, t.ID)
		}
		return t, e
	}
	if restart {
		m.retry[t.ID] = time.Now()
	}
	action := "обновлён"
	if create {
		action = "создан"
	}
	m.SystemLog("info", "Туннель %q (%s) %s: transport=%s, mode=%s, restart=%t", t.Name, t.ID, action, t.Transport, t.Mode, restart)
	return t, nil
}
func (m *Manager) Delete(id string) error {
	m.op.Lock()
	defer m.op.Unlock()
	if e := m.stopLocked(id, true); e != nil {
		return e
	}
	m.mu.Lock()
	defer m.mu.Unlock()
	old, ok := m.tunnels[id]
	if !ok {
		return os.ErrNotExist
	}
	delete(m.tunnels, id)
	delete(m.retry, id)
	if e := m.saveLocked(); e != nil {
		m.tunnels[id] = old
		return e
	}
	for _, path := range []string{filepath.Join(m.store.Dir, "keys", id+".key"), filepath.Join(m.store.Dir, "cookies", id+".json")} {
		_ = os.Remove(path)
	}
	err := m.logs.Clear(id)
	m.SystemLog("info", "Туннель %q (%s) удалён; настройки, cookies и ключ очищены", old.Name, id)
	return err
}
func (m *Manager) Start(id string) error {
	m.op.Lock()
	defer m.op.Unlock()
	return m.startLocked(id, true)
}
func (m *Manager) startLocked(id string, manual bool) (result error) {
	defer func() {
		if result != nil {
			m.SystemLog("error", "Запуск туннеля %s: %v", id, result)
		}
	}()
	m.mu.Lock()
	defer m.mu.Unlock()
	if m.closed {
		return errors.New("панель завершает работу")
	}
	t, ok := m.tunnels[id]
	if !ok {
		return os.ErrNotExist
	}
	if err := t.Validate(); err != nil {
		return err
	}
	if _, ok := m.processes[id]; ok {
		return nil
	}
	m.SystemLog("info", "Запуск туннеля %q (%s): mode=%s, transport=%s, carriers=%s, manual=%t", t.Name, id, t.Mode, t.Transport, t.Transports, manual)
	if t.TrafficLimitBytes > 0 && t.UploadBytes+t.DownloadBytes >= t.TrafficLimitBytes {
		return errors.New("лимит трафика исчерпан: сбросьте счётчики или измените лимит")
	}
	if _, e := os.Stat(m.core); e != nil {
		return coreMissing(m.core)
	}
	if manual {
		t.RestartAttempts = 0
	}
	t.IsEnabled = true
	t.Status = Starting
	clearLiveStats(t)
	t.ErrorMessage = ""
	t.Challenge = nil
	t.ShareLink = ""
	t.UpdatedAt = time.Now().UTC()
	if e := m.saveLocked(); e != nil {
		t.IsEnabled = false
		t.Status = Failed
		return e
	}
	socket := filepath.Join(m.socketDir, id[:8]+".sock")
	args, e := BuildArgs(*t, m.store.Dir, socket)
	if e != nil {
		t.Status = Failed
		t.ErrorMessage = e.Error()
		_ = m.saveLocked()
		return e
	}
	cmd := exec.Command(m.core, args...)
	cmd.Dir = m.store.Dir
	if e = m.l3Guard.Prepare(cmd, t.Mode); e != nil {
		t.Status = Failed
		t.IsEnabled = false
		t.ErrorMessage = e.Error()
		_ = m.saveLocked()
		m.logs.Append(id, e.Error())
		return e
	}
	prepareProcess(cmd)
	writer := &lineWriter{consume: func(line string) { m.consumeLine(id, line) }}
	cmd.Stdout = writer
	cmd.Stderr = writer
	if e = cmd.Start(); e != nil {
		e = errors.Join(e, m.clearL3Locked(id))
		t.Status = Failed
		t.ErrorMessage = e.Error()
		t.RestartAttempts++
		m.retry[id] = time.Now().Add(5 * time.Second)
		_ = m.saveLocked()
		return e
	}
	if e = m.attachProcess(cmd); e != nil {
		_ = cmd.Process.Kill()
		go cmd.Wait()
		e = errors.Join(e, m.clearL3Locked(id))
		t.Status = Failed
		t.ErrorMessage = e.Error()
		t.RestartAttempts++
		m.retry[id] = time.Now().Add(5 * time.Second)
		_ = m.saveLocked()
		return e
	}
	p := &process{cmd: cmd, done: make(chan struct{}), l3: t.Mode == "l3", statusUpdated: make(chan struct{}, 1)}
	m.processes[id] = p
	t.Status = Running
	t.LastStartedAt = nowPointer()
	delete(m.retry, id)
	_ = m.saveLocked()
	m.logs.Append(id, "Процесс OpenFlux запущен")
	m.SystemLog("info", "Ядро запущено для %q (%s): pid=%d, L3=%t", t.Name, id, cmd.Process.Pid, p.l3)
	m.bg.Add(2)
	go func() {
		defer m.bg.Done()
		err := cmd.Wait()
		writer.Flush()
		m.mu.Lock()
		if p.conn != nil {
			_ = p.conn.Close()
		}
		delete(m.processes, id)
		p.cleanupErr = m.clearL3Locked(id)
		if t := m.tunnels[id]; t != nil {
			clearLiveStats(t)
			t.LastStoppedAt = nowPointer()
			t.Status = Stopped
			if !p.intentional && !m.closed {
				t.Status = Failed
				t.RestartAttempts++
				if err != nil {
					t.ErrorMessage = err.Error()
				} else {
					t.ErrorMessage = "Процесс завершился"
				}
				delay := min(60, 1<<min(t.RestartAttempts, 6))
				m.retry[id] = time.Now().Add(time.Duration(delay) * time.Second)
				m.SystemLog("error", "Ядро туннеля %q (%s) неожиданно завершилось: %s; повтор №%d через %d s", t.Name, id, t.ErrorMessage, t.RestartAttempts, delay)
			}
			if e := m.saveLocked(); e != nil {
				m.logs.Append(id, "Ошибка сохранения: "+e.Error())
			}
		}
		close(p.done)
		m.mu.Unlock()
		m.logs.Append(id, "Процесс OpenFlux завершён")
		m.SystemLog("info", "Ядро туннеля %s завершено: intentional=%t, result=%v, cleanup=%v", id, p.intentional, err, p.cleanupErr)
	}()
	go func() { defer m.bg.Done(); m.readIPC(id, p, socket) }()
	return nil
}
func (m *Manager) Stop(id string) error {
	m.op.Lock()
	defer m.op.Unlock()
	return m.stopLocked(id, true)
}
func (m *Manager) stopLocked(id string, disable bool) error {
	m.mu.Lock()
	t, ok := m.tunnels[id]
	if !ok {
		m.mu.Unlock()
		return os.ErrNotExist
	}
	if disable {
		previous := t.IsEnabled
		t.IsEnabled = false
		if e := m.saveLocked(); e != nil {
			t.IsEnabled = previous
			m.mu.Unlock()
			return e
		}
	}
	delete(m.retry, id)
	p := m.processes[id]
	if p == nil {
		t.Status = Stopped
		clearLiveStats(t)
		m.dirty = true
		m.mu.Unlock()
		return nil
	}
	p.intentional = true
	m.SystemLog("info", "Остановка туннеля %q (%s): disable=%t, pid=%d", t.Name, id, disable, p.cmd.Process.Pid)
	t.Status = Stopping
	clearLiveStats(t)
	m.dirty = true
	m.mu.Unlock()
	m.captureFinalStatus(id, p)
	_ = p.cmd.Process.Signal(syscall.SIGTERM)
	select {
	case <-p.done:
		return p.cleanupErr
	case <-time.After(3 * time.Second):
	}
	if e := p.cmd.Process.Kill(); e != nil && !errors.Is(e, os.ErrProcessDone) {
		return e
	}
	select {
	case <-p.done:
		return p.cleanupErr
	case <-time.After(3 * time.Second):
		return errors.New("не удалось дождаться завершения ядра")
	}
}
func (m *Manager) Restart(id string) error {
	m.op.Lock()
	defer m.op.Unlock()
	if e := m.stopLocked(id, false); e != nil {
		return e
	}
	return m.startLocked(id, true)
}
func (m *Manager) ResetStats(id string) error {
	m.mu.Lock()
	defer m.mu.Unlock()
	t, ok := m.tunnels[id]
	if !ok {
		return os.ErrNotExist
	}
	t.UploadBytes = 0
	t.DownloadBytes = 0
	t.UploadRate = 0
	t.DownloadRate = 0
	m.SystemLog("info", "Сброшены счётчики трафика туннеля %q (%s)", t.Name, id)
	return m.saveLocked()
}
func (m *Manager) consumeLine(id, line string) {
	qrLine := line != ""
	for _, r := range line {
		if !strings.ContainsRune(" █▄▀\t", r) {
			qrLine = false
			break
		}
	}
	if qrLine {
		return
	}
	if link := sharePattern.FindString(line); link != "" {
		m.mu.Lock()
		if t := m.tunnels[id]; t != nil {
			t.ShareLink = link
			m.dirty = true
		}
		m.mu.Unlock()
		line = strings.ReplaceAll(line, link, "[ссылка подключения доступна в карточке туннеля]")
	}
	m.logs.Append(id, line)
}
func (m *Manager) readIPC(id string, p *process, socket string) {
	lastDialLog := time.Now()
	for {
		select {
		case <-p.done:
			return
		case <-time.After(200 * time.Millisecond):
		}
		conn, err := net.DialTimeout("unix", socket, time.Second)
		if err != nil {
			if time.Since(lastDialLog) >= 10*time.Second {
				m.SystemLog("warn", "Туннель %s: ожидание IPC ядра: %v", id, err)
				lastDialLog = time.Now()
			}
			continue
		}
		m.mu.Lock()
		if m.processes[id] != p {
			m.mu.Unlock()
			_ = conn.Close()
			return
		}
		p.conn = conn
		m.mu.Unlock()
		m.SystemLog("info", "Туннель %s: IPC подключён, получение статистики и запросов cookies", id)
		err = m.readIPCFrames(id, p, conn)
		_ = conn.Close()
		m.mu.Lock()
		if m.processes[id] == p {
			p.conn = nil
			if !p.intentional && !m.closed {
				m.SystemLog("warn", "Туннель %s: IPC отключён: %v; переподключение и сброс текущей скорости", id, err)
			}
			if t := m.tunnels[id]; t != nil {
				clearLiveStats(t)
				m.dirty = true
			}
		}
		m.mu.Unlock()
	}
}
func (m *Manager) readIPCFrames(id string, p *process, conn net.Conn) error {
	for {
		_ = conn.SetReadDeadline(time.Now().Add(ipcStatusTimeout))
		kind, data, err := ipc.ReadFrame(conn)
		if err != nil {
			return err
		}
		m.mu.Lock()
		t := m.tunnels[id]
		if t == nil || m.processes[id] != p {
			m.mu.Unlock()
			return nil
		}
		switch kind {
		case ipc.MsgCookiesRequest:
			var c ipc.CookiesRequestPayload
			if json.Unmarshal(data, &c) == nil {
				t.Challenge = &c
				m.SystemLog("warn", "Туннель %q (%s): требуется проверка/cookies для транспорта %s, remote=%t", t.Name, id, c.Transport, c.Remote)
			}
		case ipc.MsgStatus:
			var status ipc.StatusPayload
			if err := json.Unmarshal(data, &status); err == nil {
				m.applyStatusLocked(t, p, status, time.Now())
			} else {
				m.SystemLog("warn", "Туннель %s: некорректный IPC status: %v", id, err)
			}
		}
		m.mu.Unlock()
	}
}
func (m *Manager) Cookies(id string, jar map[string]string, selected ...string) error {
	_, err := m.saveOrApplyCookies(id, jar, selected...)
	return err
}
func (m *Manager) saveOrApplyCookies(id string, jar map[string]string, selected ...string) (string, error) {
	m.op.Lock()
	defer m.op.Unlock()
	m.mu.Lock()
	t, ok := m.tunnels[id]
	if !ok {
		m.mu.Unlock()
		return "", os.ErrNotExist
	}
	if m.closed {
		m.mu.Unlock()
		return "", errors.New("панель завершает работу")
	}
	target := ""
	if len(selected) > 0 {
		target = strings.TrimSpace(selected[0])
	}
	if target == "" && t.Challenge != nil {
		target = t.Challenge.Transport
	}
	if target == "" && t.Transport != "multi" {
		target = t.Transport
	}
	types, err := t.types()
	if err != nil {
		m.mu.Unlock()
		return "", err
	}
	found := false
	for _, kind := range types {
		if kind == target {
			found = true
		}
	}
	if !found {
		m.mu.Unlock()
		return "", errors.New("выберите транспорт из состава этого туннеля")
	}
	switch target {
	case "yandex", "vyandex", "boards", "mailru", "cupsonline", "avito", "script":
	default:
		m.mu.Unlock()
		return "", errors.New("этот транспорт не поддерживает ввод cookies")
	}
	p := m.processes[id]
	if p == nil {
		profile := *t
		m.mu.Unlock()
		if err := m.saveStoppedCookies(profile, target, jar); err != nil {
			m.SystemLog("error", "Сохранение cookies туннеля %s, транспорт=%s: %v", id, target, err)
			return "", err
		}
		m.SystemLog("info", "Cookies сохранены для туннеля %s, транспорт=%s, записей=%d; применятся при запуске", id, target, len(jar))
		return "stored", nil
	}
	if p.conn == nil {
		m.mu.Unlock()
		return "", errors.New("туннель запускается: дождитесь подключения IPC или остановите его для сохранения cookies")
	}
	challenge := t.Challenge
	offer := ipc.CookiesOfferPayload{Transport: target, Jar: jar}
	if challenge != nil && challenge.Transport == target {
		offer.Remote = t.Challenge.Remote
	}
	conn := p.conn
	m.mu.Unlock()
	p.writeMu.Lock()
	_ = conn.SetWriteDeadline(time.Now().Add(5 * time.Second))
	e := ipc.WriteFrame(conn, ipc.MsgCookiesOffer, &offer)
	p.writeMu.Unlock()
	if e == nil {
		m.mu.Lock()
		if t = m.tunnels[id]; t != nil && t.Challenge == challenge && challenge != nil && challenge.Transport == target {
			t.Challenge = nil
		}
		m.mu.Unlock()
	}
	if e == nil {
		m.SystemLog("info", "Cookies переданы ядру туннеля %s: transport=%s, count=%d, remote=%t", id, target, len(jar), offer.Remote)
	} else {
		m.SystemLog("error", "Передача cookies туннеля %s: %v", id, e)
	}
	return "sent", e
}
func (m *Manager) ClearCookies(id string) error {
	m.op.Lock()
	defer m.op.Unlock()
	t, ok := m.Get(id)
	if !ok {
		return os.ErrNotExist
	}
	if e := m.stopLocked(id, false); e != nil {
		return e
	}
	if e := os.Remove(filepath.Join(m.store.Dir, "cookies", id+".json")); e != nil && !os.IsNotExist(e) {
		return e
	}
	if t.IsEnabled {
		return m.startLocked(id, true)
	}
	return nil
}
func (m *Manager) Restore() error {
	m.SystemLog("info", "Проверка автозапуска сохранённых туннелей")
	for _, t := range m.List() {
		if t.IsEnabled {
			if e := m.Start(t.ID); e != nil {
				m.logs.Append(t.ID, e.Error())
			}
		}
	}
	return nil
}
func (m *Manager) supervise(ctx context.Context) {
	defer m.bg.Done()
	ticker := time.NewTicker(time.Second)
	defer ticker.Stop()
	ticks := 0
	for {
		select {
		case <-ctx.Done():
			return
		case <-ticker.C:
		}
		ticks++
		m.op.Lock()
		m.mu.Lock()
		var restart, limited []string
		m.expireLiveStatsLocked(time.Now())
		for id, t := range m.tunnels {
			if t.IsEnabled && t.TrafficLimitBytes > 0 && t.UploadBytes+t.DownloadBytes >= t.TrafficLimitBytes {
				limited = append(limited, id)
			} else if t.IsEnabled && m.processes[id] == nil && t.RestartAttempts < 10 && !m.retry[id].After(time.Now()) {
				restart = append(restart, id)
			}
		}
		if ticks%5 == 0 && m.dirty {
			if e := m.saveLocked(); e != nil {
				m.logs.Append("system", e.Error())
			}
		}
		if ticks%60 == 0 {
			m.diagnosticsLocked()
		}
		m.mu.Unlock()
		for _, id := range limited {
			_ = m.stopLocked(id, true)
			m.mu.Lock()
			if t := m.tunnels[id]; t != nil {
				t.ErrorMessage = "Лимит трафика исчерпан"
				_ = m.saveLocked()
			}
			m.mu.Unlock()
		}
		for _, id := range restart {
			m.mu.Lock()
			attempts := m.tunnels[id].RestartAttempts
			m.mu.Unlock()
			if e := m.startLocked(id, false); e != nil {
				m.mu.Lock()
				if t := m.tunnels[id]; t != nil {
					if t.RestartAttempts == attempts {
						t.RestartAttempts++
					}
					t.Status = Failed
					t.ErrorMessage = e.Error()
					m.retry[id] = time.Now().Add(30 * time.Second)
					_ = m.saveLocked()
				}
				m.mu.Unlock()
			}
		}
		m.op.Unlock()
	}
}
func (m *Manager) Close() error {
	m.SystemLog("info", "Панель завершает работу: остановка ядер и сохранение счётчиков")
	m.cancel()
	m.op.Lock()
	m.mu.Lock()
	m.closed = true
	ids := make([]string, 0, len(m.processes))
	for id := range m.processes {
		ids = append(ids, id)
	}
	m.mu.Unlock()
	var errs []error
	for _, id := range ids {
		if e := m.stopLocked(id, false); e != nil {
			errs = append(errs, e)
		}
	}
	m.op.Unlock()
	m.bg.Wait()
	m.mu.Lock()
	err := m.saveLocked()
	m.mu.Unlock()
	errs = append(errs, err, m.closeProcessGroup(), os.RemoveAll(m.socketDir), m.unlockStore())
	result := errors.Join(errs...)
	if result != nil {
		m.SystemLog("error", "Ошибки завершения панели: %v", result)
	} else {
		m.SystemLog("info", "Панель остановлена; настройки и счётчики сохранены")
	}
	return result
}
func (m *Manager) CorePath() string { return m.core }

func (m *Manager) clearL3Locked(id string) error {
	for _, p := range m.processes {
		if p.l3 {
			return nil
		}
	}
	if e := m.l3Guard.Release(); e != nil {
		m.logs.Append(id, "L3: ошибка очистки nftables: "+e.Error())
		return e
	}
	return nil
}

// Import validates the whole batch before writing and never auto-starts newly imported tunnels.
func (m *Manager) Import(list []Tunnel) (int, error) {
	seen := map[string]bool{}
	for i := range list {
		t := &list[i]
		t.ID = strings.ToLower(t.ID)
		if t.ID == "" {
			t.ID = newID()
		}
		if !validID(t.ID) || seen[t.ID] {
			return 0, errors.New("некорректный или повторяющийся id")
		}
		seen[t.ID] = true
		if e := t.Validate(); e != nil {
			return 0, e
		}
	}
	m.op.Lock()
	defer m.op.Unlock()
	for _, t := range list {
		if old, ok := m.Get(t.ID); ok && old.IsEnabled {
			if e := m.stopLocked(t.ID, false); e != nil {
				return 0, e
			}
		}
	}
	m.mu.Lock()
	defer m.mu.Unlock()
	oldMap := m.tunnels
	next := make(map[string]*Tunnel, len(oldMap)+len(list))
	for id, t := range oldMap {
		next[id] = t
	}
	for _, t := range list {
		old := oldMap[t.ID]
		t.Status = Stopped
		t.ShareLink = ""
		t.Challenge = nil
		t.RestartAttempts = 0
		t.ErrorMessage = ""
		t.IsEnabled = false
		clearLiveStats(&t)
		t.CreatedAt = time.Now().UTC()
		t.UpdatedAt = time.Now().UTC()
		if old != nil {
			t.IsEnabled = old.IsEnabled
			t.CreatedAt = old.CreatedAt
		}
		copy := t
		next[t.ID] = &copy
	}
	m.tunnels = next
	if e := m.saveLocked(); e != nil {
		m.tunnels = oldMap
		return 0, e
	}
	for _, t := range list {
		if next[t.ID].IsEnabled {
			m.retry[t.ID] = time.Now()
		}
	}
	m.SystemLog("info", "Импорт конфигурации: %d туннелей; новые профили остановлены", len(list))
	return len(list), nil
}
func (m *Manager) Logs(id string, n int) []LogLine { return m.logs.Tail(id, n) }
func (m *Manager) ClearLogs(id string) error       { return m.logs.Clear(id) }
func (m *Manager) String() string                  { return fmt.Sprintf("OpenFlux manager (%d tunnels)", len(m.List())) }
