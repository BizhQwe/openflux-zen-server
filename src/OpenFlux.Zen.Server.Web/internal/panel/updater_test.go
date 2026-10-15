package panel

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"testing"
)

func TestCompareVersions(t *testing.T) {
	tests := []struct {
		v1, v2 string
		want   int
	}{
		{"1.0.0", "1.0.0", 0},
		{"v1.0.0", "1.0.0", 0},
		{"v1.0.1", "v1.0.0", 1},
		{"1.0.0", "1.0.1", -1},
		{"v1.1.0", "v1.0.9", 1},
		{"2.0.0", "1.99.99", 1},
		{"v1.0.0-rc1", "1.0.0", 0},
	}

	for _, tt := range tests {
		got := compareVersions(tt.v1, tt.v2)
		if (tt.want > 0 && got <= 0) || (tt.want < 0 && got >= 0) || (tt.want == 0 && got != 0) {
			t.Errorf("compareVersions(%q, %q) = %d, want sign %d", tt.v1, tt.v2, got, tt.want)
		}
	}
}

func TestPanelVersionAPI(t *testing.T) {
	s, settings, _ := testServer(t)

	// Test GET api/panel/version
	req := httptest.NewRequest("GET", "/"+settings.SecretPath+"/api/panel/version", nil)
	w := httptest.NewRecorder()
	handled := s.legacyRoute(w, req, "api/panel/version")
	if !handled {
		t.Fatal("api/panel/version not handled")
	}
	if w.Code != http.StatusOK {
		t.Fatalf("expected 200, got %d", w.Code)
	}
	var info PanelVersionInfo
	if err := json.Unmarshal(w.Body.Bytes(), &info); err != nil {
		t.Fatalf("failed to decode version info: %v", err)
	}
	if info.CurrentVersion != Version {
		t.Fatalf("expected CurrentVersion %q, got %q", Version, info.CurrentVersion)
	}

	// Test GET api/panel/releases
	req = httptest.NewRequest("GET", "/"+settings.SecretPath+"/api/panel/releases", nil)
	w = httptest.NewRecorder()
	handled = s.legacyRoute(w, req, "api/panel/releases")
	if !handled {
		t.Fatal("api/panel/releases not handled")
	}
	if w.Code != http.StatusOK {
		t.Fatalf("expected 200, got %d", w.Code)
	}
}
