package panel

import (
	"encoding/json"
	"encoding/xml"
	"fmt"
	"io"
	"net/http"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strconv"
	"strings"
	"sync"
	"time"
)

type PanelVersionInfo struct {
	CurrentVersion    string `json:"currentVersion"`
	LatestVersion     string `json:"latestVersion"`
	IsUpdateAvailable bool   `json:"isUpdateAvailable"`
	PublishedAt       string `json:"publishedAt,omitempty"`
	LastCheckedAt     string `json:"lastCheckedAt,omitempty"`
	ReleaseNotes      string `json:"releaseNotes,omitempty"`
	ReleaseURL        string `json:"releaseUrl,omitempty"`
}

type ReleaseItemDto struct {
	TagName     string `json:"tagName"`
	Name        string `json:"name"`
	PublishedAt string `json:"publishedAt"`
	Prerelease  bool   `json:"prerelease"`
	HtmlURL     string `json:"htmlUrl"`
}

type ghRelease struct {
	TagName     string `json:"tag_name"`
	Name        string `json:"name"`
	Body        string `json:"body"`
	PublishedAt string `json:"published_at"`
	HtmlURL     string `json:"html_url"`
	Prerelease  bool   `json:"prerelease"`
}

type updateCache struct {
	mu           sync.Mutex
	info         PanelVersionInfo
	releases     []ReleaseItemDto
	lastCheck    time.Time
	releasesTime time.Time
	isUpdating   bool
}

const (
	updateRepo          = "BizhQwe/openflux-zen-server"
	githubLatestRelease = "https://api.github.com/repos/" + updateRepo + "/releases/latest"
	githubReleasesList  = "https://api.github.com/repos/" + updateRepo + "/releases?per_page=30"
	githubAtomReleases  = "https://github.com/" + updateRepo + "/releases.atom"
)

func cleanVersion(v string) string {
	v = strings.TrimSpace(v)
	v = strings.TrimPrefix(v, "v")
	v = strings.TrimPrefix(v, "V")
	return v
}

func compareVersions(v1, v2 string) int {
	c1 := cleanVersion(v1)
	c2 := cleanVersion(v2)
	if c1 == c2 {
		return 0
	}
	parts1 := strings.Split(c1, ".")
	parts2 := strings.Split(c2, ".")
	maxLen := max(len(parts1), len(parts2))
	for i := 0; i < maxLen; i++ {
		var n1, n2 int
		if i < len(parts1) {
			n1, _ = strconv.Atoi(parts1[i])
		}
		if i < len(parts2) {
			n2, _ = strconv.Atoi(parts2[i])
		}
		if n1 < n2 {
			return -1
		}
		if n1 > n2 {
			return 1
		}
	}
	return 0
}

func (s *Server) CheckPanelUpdates(force bool) PanelVersionInfo {
	s.updateCache.mu.Lock()
	defer s.updateCache.mu.Unlock()

	curVer := Version
	if !force && !s.updateCache.lastCheck.IsZero() && time.Since(s.updateCache.lastCheck) < 5*time.Minute {
		return s.updateCache.info
	}

	client := &http.Client{Timeout: 10 * time.Second}
	req, err := http.NewRequest("GET", githubLatestRelease, nil)
	if err == nil {
		req.Header.Set("User-Agent", "OpenFluxZenServer-PanelUpdater/1.0")
		req.Header.Set("Accept", "application/vnd.github.v3+json")
		resp, err := client.Do(req)
		if err == nil {
			defer resp.Body.Close()
			if resp.StatusCode == http.StatusOK {
				var rel ghRelease
				if json.NewDecoder(resp.Body).Decode(&rel) == nil && rel.TagName != "" {
					isNewer := compareVersions(rel.TagName, curVer) > 0
					s.updateCache.info = PanelVersionInfo{
						CurrentVersion:    curVer,
						LatestVersion:     rel.TagName,
						IsUpdateAvailable: isNewer,
						PublishedAt:       rel.PublishedAt,
						LastCheckedAt:     time.Now().UTC().Format(time.RFC3339),
						ReleaseNotes:      rel.Body,
						ReleaseURL:        rel.HtmlURL,
					}
					s.updateCache.lastCheck = time.Now()
					return s.updateCache.info
				}
			}
		}
	}

	atomReleases := fetchAtomReleases(client)
	if len(atomReleases) > 0 {
		latest := atomReleases[0]
		isNewer := compareVersions(latest.TagName, curVer) > 0
		s.updateCache.info = PanelVersionInfo{
			CurrentVersion:    curVer,
			LatestVersion:     latest.TagName,
			IsUpdateAvailable: isNewer,
			PublishedAt:       latest.PublishedAt,
			LastCheckedAt:     time.Now().UTC().Format(time.RFC3339),
			ReleaseURL:        latest.HtmlURL,
		}
		s.updateCache.lastCheck = time.Now()
		return s.updateCache.info
	}

	if s.updateCache.info.CurrentVersion == "" {
		s.updateCache.info = PanelVersionInfo{
			CurrentVersion:    curVer,
			LatestVersion:     curVer,
			IsUpdateAvailable: false,
			LastCheckedAt:     time.Now().UTC().Format(time.RFC3339),
		}
	}
	s.updateCache.lastCheck = time.Now()
	return s.updateCache.info
}

func (s *Server) GetAvailablePanelReleases() []ReleaseItemDto {
	s.updateCache.mu.Lock()
	defer s.updateCache.mu.Unlock()

	if len(s.updateCache.releases) > 0 && time.Since(s.updateCache.releasesTime) < 5*time.Minute {
		return s.updateCache.releases
	}

	client := &http.Client{Timeout: 10 * time.Second}
	req, err := http.NewRequest("GET", githubReleasesList, nil)
	if err == nil {
		req.Header.Set("User-Agent", "OpenFluxZenServer-PanelUpdater/1.0")
		req.Header.Set("Accept", "application/vnd.github.v3+json")
		resp, err := client.Do(req)
		if err == nil {
			defer resp.Body.Close()
			if resp.StatusCode == http.StatusOK {
				var list []ghRelease
				if json.NewDecoder(resp.Body).Decode(&list) == nil && len(list) > 0 {
					var result []ReleaseItemDto
					for _, r := range list {
						if r.TagName != "" {
							name := r.Name
							if name == "" {
								name = r.TagName
							}
							result = append(result, ReleaseItemDto{
								TagName:     r.TagName,
								Name:        name,
								PublishedAt: r.PublishedAt,
								Prerelease:  r.Prerelease,
								HtmlURL:     r.HtmlURL,
							})
						}
					}
					s.updateCache.releases = result
					s.updateCache.releasesTime = time.Now()
					return result
				}
			}
		}
	}

	atomReleases := fetchAtomReleases(client)
	if len(atomReleases) > 0 {
		s.updateCache.releases = atomReleases
		s.updateCache.releasesTime = time.Now()
		return atomReleases
	}

	if s.updateCache.releases != nil {
		return s.updateCache.releases
	}
	return []ReleaseItemDto{}
}

type atomFeed struct {
	Entry []struct {
		Title   string `xml:"title"`
		Updated string `xml:"updated"`
		Link    struct {
			Href string `xml:"href,attr"`
		} `xml:"link"`
	} `xml:"entry"`
}

func fetchAtomReleases(client *http.Client) []ReleaseItemDto {
	req, err := http.NewRequest("GET", githubAtomReleases, nil)
	if err != nil {
		return nil
	}
	req.Header.Set("User-Agent", "Mozilla/5.0")
	resp, err := client.Do(req)
	if err != nil || resp.StatusCode != http.StatusOK {
		return nil
	}
	defer resp.Body.Close()
	var feed atomFeed
	if xml.NewDecoder(resp.Body).Decode(&feed) != nil {
		return nil
	}
	var res []ReleaseItemDto
	for _, e := range feed.Entry {
		tag := strings.TrimSpace(e.Title)
		if idx := strings.LastIndex(e.Link.Href, "/tag/"); idx != -1 {
			tag = e.Link.Href[idx+5:]
		}
		if tag != "" {
			res = append(res, ReleaseItemDto{
				TagName:     tag,
				Name:        e.Title,
				PublishedAt: e.Updated,
				HtmlURL:     e.Link.Href,
			})
		}
	}
	return res
}

func (s *Server) UpdatePanel(targetVersion string) (bool, string) {
	s.updateCache.mu.Lock()
	if s.updateCache.isUpdating {
		s.updateCache.mu.Unlock()
		return false, "Обновление уже выполняется..."
	}
	s.updateCache.isUpdating = true
	s.updateCache.mu.Unlock()

	defer func() {
		time.AfterFunc(10*time.Minute, func() {
			s.updateCache.mu.Lock()
			s.updateCache.isUpdating = false
			s.updateCache.mu.Unlock()
		})
	}()

	targetTag := strings.TrimSpace(targetVersion)
	if targetTag == "" || targetTag == "latest" {
		info := s.CheckPanelUpdates(true)
		targetTag = info.LatestVersion
		if targetTag == "" {
			targetTag = "v" + Version
		}
	}
	if !strings.HasPrefix(targetTag, "v") && !strings.HasPrefix(targetTag, "V") {
		targetTag = "v" + targetTag
	}

	var targetArch string
	switch runtime.GOOS {
	case "windows":
		switch runtime.GOARCH {
		case "amd64":
			targetArch = "windows-amd64"
		case "arm64":
			targetArch = "windows-arm64"
		case "386":
			targetArch = "windows-386"
		default:
			return false, fmt.Sprintf("Неподдерживаемая архитектура Windows: %s", runtime.GOARCH)
		}
	case "linux":
		switch runtime.GOARCH {
		case "amd64":
			targetArch = "linux-amd64"
		case "arm64":
			targetArch = "linux-arm64"
		case "386":
			targetArch = "linux-386"
		case "arm":
			targetArch = "linux-armv7"
		default:
			return false, fmt.Sprintf("Неподдерживаемая архитектура Linux: %s", runtime.GOARCH)
		}
	default:
		return false, fmt.Sprintf("Неподдерживаемая операционная система: %s", runtime.GOOS)
	}

	suffix := ""
	if runtime.GOOS == "windows" {
		suffix = ".exe"
	}
	assetName := fmt.Sprintf("openflux-zen-server-installer-%s%s", targetArch, suffix)

	candidateURLs := []string{
		fmt.Sprintf("https://github.com/%s/releases/download/%s/%s", updateRepo, targetTag, assetName),
		fmt.Sprintf("https://github.com/%s/releases/latest/download/%s", updateRepo, assetName),
	}

	tempInstaller := filepath.Join(os.TempDir(), fmt.Sprintf("openflux_install_%d_%s", time.Now().Unix(), assetName))

	var downloaded bool
	client := &http.Client{Timeout: 5 * time.Minute}
	for _, downloadURL := range candidateURLs {
		req, err := http.NewRequest("GET", downloadURL, nil)
		if err != nil {
			continue
		}
		req.Header.Set("User-Agent", "OpenFluxZenServer-PanelUpdater/1.0")
		resp, err := client.Do(req)
		if err != nil || resp.StatusCode != http.StatusOK {
			if resp != nil {
				resp.Body.Close()
			}
			continue
		}
		f, err := os.OpenFile(tempInstaller, os.O_CREATE|os.O_WRONLY|os.O_TRUNC, 0755)
		if err != nil {
			resp.Body.Close()
			continue
		}
		_, copyErr := io.Copy(f, resp.Body)
		f.Close()
		resp.Body.Close()
		if copyErr != nil {
			_ = os.Remove(tempInstaller)
			continue
		}

		fi, err := os.Stat(tempInstaller)
		if err == nil && fi.Size() > 1000000 {
			downloaded = true
			break
		}
		_ = os.Remove(tempInstaller)
	}

	if !downloaded {
		return false, fmt.Sprintf("Не удалось загрузить файл обновления (%s) из релиза %s.", assetName, targetTag)
	}

	exe, err := os.Executable()
	if err != nil {
		_ = os.Remove(tempInstaller)
		return false, fmt.Sprintf("Не удалось определить каталог установки: %v", err)
	}
	appDir := filepath.Dir(exe)

	if runtime.GOOS == "windows" {
		batPath := filepath.Join(os.TempDir(), fmt.Sprintf("oflux_apply_%d.bat", time.Now().Unix()))
		batContent := fmt.Sprintf(`@echo off
timeout /t 2 /nobreak >nul
net stop openflux-zen-server >nul 2>&1
taskkill /f /im openflux-zen-server-web.exe >nul 2>&1
timeout /t 1 /nobreak >nul
"%s" --silent --dir "%s"
net start openflux-zen-server >nul 2>&1
if errorlevel 1 (
    start "" "%s\openflux-zen-server-web.exe"
)
del "%s" >nul 2>&1
del "%%~f0" >nul 2>&1
`, tempInstaller, appDir, appDir, tempInstaller)
		if err := os.WriteFile(batPath, []byte(batContent), 0700); err != nil {
			_ = os.Remove(tempInstaller)
			return false, fmt.Sprintf("Не удалось создать скрипт применения обновления: %v", err)
		}
		cmd := exec.Command("cmd.exe", "/c", batPath)
		prepareProcess(cmd)
		if err := cmd.Start(); err != nil {
			_ = os.Remove(batPath)
			_ = os.Remove(tempInstaller)
			return false, fmt.Sprintf("Не удалось запустить скрипт обновления: %v", err)
		}
	} else {
		_ = os.Chmod(tempInstaller, 0755)
		shScript := fmt.Sprintf(`sleep 2 && "%s" --silent --dir "%s" && (systemctl restart openflux-zen-server 2>/dev/null || (cd "%s" && nohup ./openflux-zen-server-web >/dev/null 2>&1 &)) && rm -f "%s"`, tempInstaller, appDir, appDir, tempInstaller)
		cmd := exec.Command("sh", "-c", shScript)
		prepareProcess(cmd)
		if err := cmd.Start(); err != nil {
			_ = os.Remove(tempInstaller)
			return false, fmt.Sprintf("Не удалось запустить обновление: %v", err)
		}
	}

	return true, fmt.Sprintf("Панель успешно обновляется до %s и перезагружается. Страница обновится автоматически.", targetTag)
}
