package panel

import (
	"encoding/json"
	"errors"
	"net"
	"net/http"
	"openflux.local/zen/panel/configuration"
	"strconv"
	"strings"
	"time"
)

// Match the response shapes used by the original, unchanged Zen JavaScript.
// These adapters never change the core CLI, IPC or share-link formats.
func (s *Server) legacyRoute(w http.ResponseWriter, r *http.Request, route string) bool {
	settings := s.Settings()
	switch {
	case route == "api/stats" && r.Method == "GET":
		stats := s.host.read()
		list := s.manager.List()
		running := 0
		var up, down int64
		var upRate, downRate float64
		for _, tunnel := range list {
			if tunnel.Status == Running {
				running++
			}
			up += tunnel.UploadBytes
			down += tunnel.DownloadBytes
			upRate += tunnel.UploadRate
			downRate += tunnel.DownloadRate
		}
		jsonResponse(w, 200, map[string]any{
			"activeTunnels": running, "totalTunnels": len(list),
			"cpuUsagePercent": stats.CPUPercent,
			"memoryUsedBytes": stats.MemoryUsed, "memoryTotalBytes": stats.MemoryTotal,
			"memoryUsagePercent": stats.MemoryPercent,
			"totalUploadBytes":   up, "totalDownloadBytes": down,
			"uploadRateBytesPerSec": upRate, "downloadRateBytesPerSec": downRate,
		})
	case route == "api/settings/language" && r.Method == "POST":
		var req struct{ Language string }
		if err := decode(r, &req); err != nil {
			fail(w, err)
		} else if req.Language != "ru" && req.Language != "en" {
			fail(w, errors.New("язык должен быть ru или en"))
		} else {
			settings.Language = req.Language
			if err := s.saveSettings(settings); err != nil {
				fail(w, err)
			} else {
				jsonResponse(w, 200, map[string]any{"success": true, "language": settings.Language})
			}
		}
	case route == "api/settings/network-placement" && r.Method == "GET":
		jsonResponse(w, 200, s.networkPlacement(r))
	case route == "api/settings/network-placement" && r.Method == "POST":
		var req struct {
			LANEnabled, LocaltunnelEnabled, DomainEnabled bool
			Domain                                        string
			PanelPort                                     int
			LANSubnets                                    *string
		}
		if err := decode(r, &req); err != nil {
			fail(w, err)
			return true
		}
		{
			configuration.SetLAN(&settings, req.LANEnabled)
			if req.DomainEnabled && !strings.Contains(req.Domain, "://") {
				configuration.SetPublicListen(&settings, true)
			}
			if req.LANSubnets != nil {
				settings.LANSubnets = strings.TrimSpace(*req.LANSubnets)
			}
			if req.PanelPort != 0 {
				if req.PanelPort < 1 || req.PanelPort > 65535 {
					fail(w, errors.New("порт панели должен быть от 1 до 65535"))
					return true
				}
				host, _, _ := net.SplitHostPort(settings.Listen)
				settings.Listen = net.JoinHostPort(host, strconv.Itoa(req.PanelPort))
			}
			settings.LocaltunnelEnabled = req.LocaltunnelEnabled
			settings.DomainEnabled = req.DomainEnabled
			settings.Domain = strings.TrimSpace(req.Domain)
			if e := s.saveSettings(settings); e != nil {
				fail(w, e)
			} else {
				next := s.networkPlacement(r)
				next["success"] = true
				jsonResponse(w, 200, next)
			}
		}
	case route == "api/panel/version" && r.Method == "GET":
		jsonResponse(w, 200, s.CheckPanelUpdates(false))
	case route == "api/core/version" && r.Method == "GET":
		jsonResponse(w, 200, map[string]any{"currentVersion": CoreVersion, "latestVersion": CoreVersion, "isUpdateAvailable": false})
	case route == "api/panel/check-update" && r.Method == "POST":
		jsonResponse(w, 200, s.CheckPanelUpdates(true))
	case route == "api/core/check-update" && r.Method == "POST":
		jsonResponse(w, 200, map[string]any{"currentVersion": CoreVersion, "latestVersion": CoreVersion, "isUpdateAvailable": false})
	case route == "api/panel/releases" && r.Method == "GET":
		jsonResponse(w, 200, s.GetAvailablePanelReleases())
	case route == "api/core/releases" && r.Method == "GET":
		jsonResponse(w, 200, []any{})
	case route == "api/panel/update" && r.Method == "POST":
		var req struct {
			TargetVersion string `json:"targetVersion"`
		}
		_ = json.NewDecoder(r.Body).Decode(&req)
		ok, msg := s.UpdatePanel(req.TargetVersion)
		if ok {
			jsonResponse(w, 200, map[string]any{"success": true, "message": msg})
		} else {
			jsonResponse(w, 400, map[string]any{"success": false, "message": msg})
		}
	case route == "api/core/update" && r.Method == "POST":
		jsonResponse(w, 400, map[string]any{"success": false, "message": "Ядро OpenFlux встроено в дистрибутив OpenFlux Zen Server. Чтобы обновить ядро, обновите панель до последней версии."})
	default:
		return false
	}
	return true
}

func (s *Server) networkPlacement(r *http.Request) map[string]any {
	settings := s.Settings()
	_, port, _ := net.SplitHostPort(settings.Listen)
	lanEnabled := configuration.LANEnabled(settings)
	scheme := "http"
	domainScheme, requestScheme := "http", "http"
	if settings.TLSCert != "" {
		domainScheme = "https"
	}
	if r.TLS != nil {
		requestScheme = "https"
	}
	url := func(address string) string {
		return scheme + "://" + address + "/" + settings.SecretPath + "/"
	}
	publication := s.publication.Status()
	urls := configuration.PanelURLs(settings)
	allowedSubnets := []string{}
	for _, prefix := range configuration.LANPrefixes(settings) {
		allowedSubnets = append(allowedSubnets, prefix.String())
	}
	publicURL := requestScheme + "://" + r.Host + "/" + settings.SecretPath + "/"
	if domain := urls["domain"]; domain != "" {
		publicURL = domain
	}
	localtunnelURL := ""
	if settings.LocaltunnelEnabled && publication.URL != "" {
		localtunnelURL = strings.TrimSuffix(publication.URL, "/") + "/" + settings.SecretPath + "/"
		publicURL = localtunnelURL
	}
	return map[string]any{
		"listen": settings.Listen, "panelPort": port, "panelScheme": scheme, "secretPath": settings.SecretPath,
		"domainScheme": domainScheme, "requestScheme": requestScheme,
		"localhostEnabled": true, "lanEnabled": lanEnabled,
		"lanAvailable": configuration.LANAvailable(settings), "lanSubnets": settings.LANSubnets, "lanAllowedSubnets": allowedSubnets,
		"localtunnelEnabled": settings.LocaltunnelEnabled, "domainEnabled": settings.DomainEnabled,
		"localhostUrl": url(net.JoinHostPort("127.0.0.1", port)),
		"lanUrl":       urls["lan"], "publicUrl": publicURL,
		"localtunnelUrl": localtunnelURL, "localtunnelStatus": publication.State, "localtunnelError": publication.Error, "domainUrl": urls["domain"], "domain": settings.Domain,
	}
}

func legacyLogLines(lines []LogLine) []map[string]any {
	result := make([]map[string]any, 0, len(lines))
	for _, line := range lines {
		stream := "stdout"
		if line.Level == "error" {
			stream = "stderr"
		}
		timestamp := line.Timestamp
		if timestamp.IsZero() {
			if stamp, message, ok := strings.Cut(line.Message, " "); ok {
				if parsed, err := time.Parse(time.RFC3339, stamp); err == nil {
					timestamp, line.Message = parsed, message
				}
			}
		}
		result = append(result, map[string]any{"timestamp": timestamp, "stream": stream, "level": line.Level, "message": line.Message})
	}
	return result
}
