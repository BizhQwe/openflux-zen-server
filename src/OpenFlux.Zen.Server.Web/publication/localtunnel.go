// Package publication implements the Localtunnel wire protocol with Go's HTTP
// and TCP servers. No Node runtime or downloaded client executable is needed.
package publication

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net"
	"net/http"
	"net/url"
	"strconv"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"openflux.local/zen/panel/configuration"
)

type Status struct {
	State string `json:"state"`
	URL   string `json:"url"`
	Error string `json:"error,omitempty"`
}
type Client struct {
	mu       sync.RWMutex
	status   Status
	handler  http.Handler
	onChange func(Status)
}

func New(handler http.Handler, onChange func(Status)) *Client {
	return &Client{handler: handler, onChange: onChange, status: Status{State: "disabled"}}
}
func (c *Client) Status() Status { c.mu.RLock(); defer c.mu.RUnlock(); return c.status }
func (c *Client) set(value Status) {
	c.mu.Lock()
	changed := c.status != value
	c.status = value
	c.mu.Unlock()
	if changed && c.onChange != nil {
		c.onChange(value)
	}
}
func wait(ctx context.Context, delay time.Duration) bool {
	t := time.NewTimer(delay)
	defer t.Stop()
	select {
	case <-ctx.Done():
		return false
	case <-t.C:
		return true
	}
}

// Run follows configuration changes and cancels all relay connections on stop.
func (c *Client) Run(ctx context.Context, settings func() configuration.Settings) {
	var cancel context.CancelFunc
	var done chan struct{}
	var current string
	defer func() {
		if cancel != nil {
			cancel()
			<-done
		}
		c.set(Status{State: "disabled"})
	}()
	for {
		s := settings()
		wanted := ""
		if s.LocaltunnelEnabled {
			wanted = s.LocaltunnelHost
			if wanted == "" {
				wanted = "https://localtunnel.me"
			}
		}
		if wanted != current {
			if cancel != nil {
				cancel()
				<-done
				cancel = nil
			}
			current = wanted
			if wanted == "" {
				c.set(Status{State: "disabled"})
			} else {
				child, stop := context.WithCancel(ctx)
				cancel = stop
				done = make(chan struct{})
				go func(host string, done chan struct{}) { defer close(done); c.serve(child, host) }(wanted, done)
			}
		}
		if !wait(ctx, time.Second) {
			return
		}
	}
}
func (c *Client) serve(ctx context.Context, host string) {
	for ctx.Err() == nil {
		c.set(Status{State: "connecting"})
		if err := c.session(ctx, host); err != nil && ctx.Err() == nil {
			c.set(Status{State: "connecting", Error: err.Error()})
		}
		if !wait(ctx, 3*time.Second) {
			return
		}
	}
}
func (c *Client) session(ctx context.Context, host string) error {
	base, err := url.Parse(host)
	if err != nil || base.Hostname() == "" {
		return errors.New("invalid Localtunnel server")
	}
	base.Path = strings.TrimSuffix(base.Path, "/") + "/"
	base.RawQuery = "new"
	req, err := http.NewRequestWithContext(ctx, "GET", base.String(), nil)
	if err != nil {
		return err
	}
	client := &http.Client{Timeout: 20 * time.Second}
	res, err := client.Do(req)
	if err != nil {
		return err
	}
	defer res.Body.Close()
	if res.StatusCode != 200 {
		_, _ = io.Copy(io.Discard, io.LimitReader(res.Body, 4096))
		return fmt.Errorf("Localtunnel HTTP %d", res.StatusCode)
	}
	var info struct {
		ID    string `json:"id"`
		IP    string `json:"ip"`
		Port  int    `json:"port"`
		URL   string `json:"url"`
		Count int    `json:"max_conn_count"`
	}
	if err = json.NewDecoder(io.LimitReader(res.Body, 64<<10)).Decode(&info); err != nil {
		return err
	}
	public, err := url.Parse(info.URL)
	if err != nil || public.Hostname() == "" || public.User != nil || (public.Scheme != "http" && public.Scheme != "https") || info.Port < 1 || info.Port > 65535 {
		return errors.New("invalid Localtunnel allocation")
	}
	remote := base.Hostname()
	if ip := net.ParseIP(info.IP); ip != nil {
		remote = ip.String()
	}
	address := net.JoinHostPort(remote, strconv.Itoa(info.Port))
	count := max(1, min(info.Count, 32))
	var workers sync.WaitGroup
	var active atomic.Int32
	for i := 0; i < count; i++ {
		workers.Add(1)
		go func() {
			defer workers.Done()
			for ctx.Err() == nil {
				conn, e := (&net.Dialer{Timeout: 10 * time.Second, KeepAlive: 30 * time.Second}).DialContext(ctx, "tcp", address)
				if e != nil {
					if active.Load() == 0 {
						c.set(Status{State: "connecting", Error: e.Error()})
					}
					if !wait(ctx, time.Second) {
						return
					}
					continue
				}
				active.Add(1)
				c.set(Status{State: "connected", URL: info.URL})
				listener := newOneConnection(conn)
				handler := relayHandler(c.handler, public)
				server := &http.Server{Handler: handler, ReadHeaderTimeout: 10 * time.Second, ReadTimeout: 30 * time.Second, WriteTimeout: 30 * time.Second, IdleTimeout: 60 * time.Second, MaxHeaderBytes: 32 << 10}
				stop := context.AfterFunc(ctx, func() { _ = server.Close(); _ = listener.Close() })
				_ = server.Serve(listener)
				stop()
				_ = server.Close()
				_ = listener.Close()
				if active.Add(-1) == 0 && ctx.Err() == nil {
					c.set(Status{State: "connecting"})
				}
			}
		}()
	}
	workers.Wait()
	return ctx.Err()
}

type trackedConn struct {
	net.Conn
	once   sync.Once
	closed chan struct{}
}

func (c *trackedConn) Close() error {
	err := c.Conn.Close()
	c.once.Do(func() { close(c.closed) })
	return err
}

type oneConnection struct {
	conn  *trackedConn
	taken bool
}

func newOneConnection(c net.Conn) *oneConnection {
	return &oneConnection{conn: &trackedConn{Conn: c, closed: make(chan struct{})}}
}
func (l *oneConnection) Accept() (net.Conn, error) {
	if !l.taken {
		l.taken = true
		return l.conn, nil
	}
	<-l.conn.closed
	return nil, net.ErrClosed
}
func (l *oneConnection) Close() error   { return l.conn.Close() }
func (l *oneConnection) Addr() net.Addr { return l.conn.LocalAddr() }
