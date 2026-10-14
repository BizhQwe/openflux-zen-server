package hosting

import (
	"bufio"
	"crypto/tls"
	"net"
	"sync"
	"time"
)

// protocolListener accepts HTTP and TLS on the panel's one TCP port. Detection
// happens per connection so a client that sends nothing cannot block Accept.
type protocolListener struct {
	net.Listener
	config    *tls.Config
	ready     chan net.Conn
	done      chan struct{}
	slots     chan struct{}
	mu        sync.Mutex
	pending   map[net.Conn]struct{}
	acceptErr error
	closeOnce sync.Once
	closeErr  error
}

type bufferedConn struct {
	net.Conn
	reader *bufio.Reader
}

func (c *bufferedConn) Read(p []byte) (int, error) { return c.reader.Read(p) }

func newProtocolListener(listener net.Listener, config *tls.Config) *protocolListener {
	l := &protocolListener{Listener: listener, config: config, ready: make(chan net.Conn), done: make(chan struct{}), slots: make(chan struct{}, 128), pending: make(map[net.Conn]struct{})}
	go l.acceptLoop()
	return l
}

func (l *protocolListener) acceptLoop() {
	for {
		conn, err := l.Listener.Accept()
		if err != nil {
			l.mu.Lock()
			l.acceptErr = err
			l.mu.Unlock()
			_ = l.Close()
			return
		}
		select {
		case l.slots <- struct{}{}:
		case <-l.done:
			conn.Close()
			return
		}
		l.mu.Lock()
		select {
		case <-l.done:
			l.mu.Unlock()
			conn.Close()
			<-l.slots
			return
		default:
			l.pending[conn] = struct{}{}
		}
		l.mu.Unlock()
		go l.detect(conn)
	}
}

func (l *protocolListener) detect(raw net.Conn) {
	defer func() { l.mu.Lock(); delete(l.pending, raw); l.mu.Unlock(); <-l.slots }()
	_ = raw.SetReadDeadline(time.Now().Add(10 * time.Second))
	reader := bufio.NewReader(raw)
	first, err := reader.Peek(1)
	if err != nil {
		raw.Close()
		return
	}
	_ = raw.SetReadDeadline(time.Time{})
	var conn net.Conn = &bufferedConn{Conn: raw, reader: reader}
	if first[0] == 0x16 {
		// Returning *tls.Conn lets net/http perform its usual handshake,
		// populate Request.TLS and set timeouts and secure cookies normally.
		conn = tls.Server(conn, l.config)
	}
	// Ownership leaves pending before delivery: Close must not close a
	// connection already being handled by net/http during a live rebind.
	l.mu.Lock()
	delete(l.pending, raw)
	l.mu.Unlock()
	select {
	case l.ready <- conn:
	case <-l.done:
		conn.Close()
	}
}

func (l *protocolListener) Accept() (net.Conn, error) {
	select {
	case conn := <-l.ready:
		return conn, nil
	case <-l.done:
		l.mu.Lock()
		err := l.acceptErr
		l.mu.Unlock()
		if err == nil {
			err = net.ErrClosed
		}
		return nil, err
	}
}

func (l *protocolListener) Close() error {
	l.closeOnce.Do(func() {
		close(l.done)
		l.closeErr = l.Listener.Close()
		l.mu.Lock()
		for conn := range l.pending {
			_ = conn.Close()
		}
		l.mu.Unlock()
	})
	return l.closeErr
}
