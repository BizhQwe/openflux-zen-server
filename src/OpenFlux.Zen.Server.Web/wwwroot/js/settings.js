// OpenFlux Zen Server - Settings & Backup

function handleConfigFileSelect(event) {
  const file = event.target.files && event.target.files[0];
  if (!file) return;
  const fileNameEl = document.getElementById('import-file-name');
  if (fileNameEl) fileNameEl.textContent = file.name;
  const reader = new FileReader();
  reader.onload = (e) => {
    document.getElementById('import-json').value = e.target.result;
    toast(`Файл ${file.name} загружен в форму`, 'info');
  };
  reader.readAsText(file);
}

async function downloadConfig() {
  const res = await api('api/config/export');
  const blob = await res.blob();
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = `openflux-config-${new Date().toISOString().slice(0,10)}.json`;
  a.click();
  toast(t('toast_saved'), 'success');
}

async function copyConfigToClipboard() {
  const res = await api('api/config/export');
  const text = await res.text();
  await navigator.clipboard.writeText(text);
  toast(t('toast_copied'), 'success');
}

async function importConfig() {
  const text = document.getElementById('import-json').value.trim();
  if (!text) { toast(t('toast_error'), 'danger'); return; }
  try {
    const res = await api('api/config/import', { method: 'POST', body: text });
    const d = await res.json();
    toast(d.message || t('toast_imported'), d.errors > 0 ? 'warning' : 'success');
    loadTunnels();
  } catch {
    toast(t('toast_error'), 'danger');
  }
}

async function loadSettings() {
  const res = await api('api/settings');
  if (res.ok) {
    const s = await res.json();
    const userInput = document.getElementById('setting-username');
    if (userInput) userInput.value = s.username || '';
    const secretInput = document.getElementById('setting-secret-path');
    if (secretInput && s.secretPath) {
      secretInput.value = '/' + s.secretPath.replace(/^\/+|\/+$/g, '') + '/';
    }
    const langSelect = document.getElementById('setting-language');
    if (langSelect && s.language) {
      langSelect.value = s.language;
      if (s.language !== currentLanguage && !localStorage.getItem('zen_lang')) {
        setLanguage(s.language, false);
      }
    }
    const modeBadge = document.getElementById('setting-network-mode');
    const modeUrl = document.getElementById('setting-network-url');
    const modeUrlInput = document.getElementById('setting-network-url-input');
    if (modeBadge) {
      const mode = s.publishMode || 'local';
      let modeText = t('mode_local');
      if (mode === 'domain') modeText = t('mode_domain');
      else if (mode === 'localtunnel') modeText = t('mode_localtunnel');
      else if (mode === 'localhost') modeText = t('mode_localhost');
      modeBadge.textContent = modeText;
    }
    if (s.publicUrl) {
      if (modeUrl) modeUrl.textContent = 'URL: ' + s.publicUrl;
      if (modeUrlInput) modeUrlInput.value = s.publicUrl;
    }
    const decoyInput = document.getElementById('setting-decoy-redirect');
    if (decoyInput) {
      decoyInput.value = s.decoyRedirectUrl || '';
    }
  }

  await loadPanelVersionInfo(false);
  await loadCoreVersionInfo(false);
}

async function copyNetworkUrl() {
  const inp = document.getElementById('setting-network-url-input');
  if (inp && inp.value) {
    try {
      await navigator.clipboard.writeText(inp.value);
      toast(t('toast_copied'), 'success');
    } catch (_) {
      inp.select();
      document.execCommand('copy');
      toast(t('toast_copied'), 'success');
    }
  }
}

async function updateAccountProfile() {
  const cur = document.getElementById('cur-pass').value;
  const u = document.getElementById('setting-username').value.trim();
  const nw = document.getElementById('new-pass').value;

  if (!cur) {
    toast(currentLanguage === 'en' ? 'Enter current password to confirm' : 'Введите текущий пароль для подтверждения', 'danger');
    return;
  }

  try {
    const res = await api('api/auth/change-profile', {
      method: 'POST',
      body: JSON.stringify({ currentPassword: cur, newUsername: u, newPassword: nw })
    });
    const data = await res.json();
    if (res.ok && data.success) {
      toast(t('toast_saved'), 'success');
      document.getElementById('cur-pass').value = '';
      document.getElementById('new-pass').value = '';
      if (data.username) {
        document.getElementById('setting-username').value = data.username;
      }
    } else {
      toast(data.error || data.message || t('toast_error'), 'danger');
    }
  } catch {
    toast(t('toast_error'), 'danger');
  }
}

async function regenerateSecretPath() {
  if (!confirm(t('confirm_regen_secret'))) return;
  const res = await api('api/settings/regenerate-secret', { method: 'POST' });
  if (res.ok) {
    const d = await res.json();
    toast(currentLanguage === 'en' ? 'Secret path updated! Redirecting...' : 'Секретный путь обновлён! Перенаправление...', 'success');
    setTimeout(() => {
      window.location.pathname = '/' + d.secretPath.replace(/^\/+|\/+$/g, '') + '/';
    }, 1500);
  }
}

async function saveDecoySettings() {
  const val = (document.getElementById('setting-decoy-redirect').value || '').trim();
  try {
    const res = await api('api/settings/decoy', {
      method: 'POST',
      body: JSON.stringify({ decoyRedirectUrl: val })
    });
    if (res.ok) {
      toast(t('toast_saved'), 'success');
    } else {
      toast(t('toast_error'), 'danger');
    }
  } catch {
    toast(t('toast_error'), 'danger');
  }
}

// OpenFlux Core Updates
let coreInfoCache = null;

async function loadCoreVersionInfo(forceCheck = false) {
  try {
    const url = forceCheck ? 'api/core/check-update' : 'api/core/version';
    const method = forceCheck ? 'POST' : 'GET';
    const res = await api(url, { method });
    if (!res.ok) return;
    const info = await res.json();
    coreInfoCache = info;
    renderCoreVersionInfo(info);
  } catch (e) {
    console.warn('Failed to load core version info:', e);
  }
}

function renderCoreVersionInfo(info) {
  if (!info) return;

  const curBadge = document.getElementById('core-current-version-badge');
  if (curBadge) {
    curBadge.textContent = info.currentVersion || 'v0.2.0';
  }

  const binName = document.getElementById('core-binary-name');
  if (binName) binName.textContent = info.binaryName || '—';

  const latVer = document.getElementById('core-latest-version');
  if (latVer) latVer.textContent = info.latestVersion || info.currentVersion || '—';

  const binSize = document.getElementById('core-binary-size');
  if (binSize) {
    binSize.textContent = info.binarySizeBytes > 0 ? fmtBytes(info.binarySizeBytes) : '—';
  }

  const lastChecked = document.getElementById('core-last-checked');
  if (lastChecked) {
    if (info.lastCheckedAt) {
      const dt = new Date(info.lastCheckedAt);
      lastChecked.textContent = dt.toLocaleString();
    } else {
      lastChecked.textContent = t('core_not_checked') || '—';
    }
  }

  const statusBadge = document.getElementById('core-status-badge');
  const btnUpdate = document.getElementById('btn-update-core');
  const releaseBox = document.getElementById('core-release-notes-box');
  const releaseText = document.getElementById('core-release-notes-text');
  const releaseLink = document.getElementById('core-release-link');

  if (info.isUpdateAvailable) {
    if (statusBadge) {
      statusBadge.style.display = 'inline-block';
      statusBadge.style.background = 'rgba(245, 158, 11, 0.15)';
      statusBadge.style.border = '1px solid rgba(245, 158, 11, 0.35)';
      statusBadge.style.color = '#fbbf24';
      statusBadge.textContent = t('core_update_available');
    }
    if (btnUpdate) btnUpdate.style.display = 'inline-flex';
    if (releaseBox) {
      releaseBox.style.display = 'block';
      if (releaseText) releaseText.textContent = info.releaseNotes || '—';
      if (releaseLink && info.releaseUrl) releaseLink.href = info.releaseUrl;
    }
  } else {
    if (statusBadge) {
      statusBadge.style.display = 'inline-block';
      statusBadge.style.background = 'rgba(16, 185, 129, 0.15)';
      statusBadge.style.border = '1px solid rgba(16, 185, 129, 0.35)';
      statusBadge.style.color = '#34d399';
      statusBadge.textContent = t('core_up_to_date');
    }
    if (btnUpdate) btnUpdate.style.display = 'none';
    if (releaseBox) releaseBox.style.display = 'none';
  }
}

async function checkForCoreUpdates() {
  const btn = document.getElementById('btn-check-core');
  const spinner = document.getElementById('btn-check-core-spinner');
  if (btn) btn.disabled = true;
  if (spinner) spinner.style.display = 'inline-block';

  try {
    const res = await api('api/core/check-update', { method: 'POST' });
    if (res.ok) {
      const info = await res.json();
      coreInfoCache = info;
      renderCoreVersionInfo(info);
      if (info.isUpdateAvailable) {
        toast(t('toast_core_update_available'), 'info');
      } else {
        toast(t('toast_core_up_to_date'), 'success');
      }
    } else {
      toast(t('toast_core_check_failed'), 'danger');
    }
  } catch (e) {
    toast(t('toast_core_check_failed'), 'danger');
  } finally {
    if (btn) btn.disabled = false;
    if (spinner) spinner.style.display = 'none';
  }
}

async function confirmUpdateCore() {
  const targetVer = (coreInfoCache && coreInfoCache.latestVersion) ? coreInfoCache.latestVersion : '';
  const msg = t('confirm_update_core', { version: targetVer });
  if (!confirm(msg)) return;

  const btn = document.getElementById('btn-update-core');
  const spinner = document.getElementById('btn-update-core-spinner');
  if (btn) btn.disabled = true;
  if (spinner) spinner.style.display = 'inline-block';

  toast(t('toast_core_updating'), 'info');

  try {
    const res = await api('api/core/update', { method: 'POST' });
    const result = await res.json();
    if (res.ok && result.success) {
      toast(result.message || t('toast_core_update_success'), 'success');
      await loadCoreVersionInfo(false);
      if (typeof loadTunnels === 'function') {
        loadTunnels();
      }
    } else {
      toast(result.message || t('toast_core_update_failed'), 'danger');
    }
  } catch (e) {
    toast(t('toast_core_update_failed'), 'danger');
  } finally {
    if (btn) btn.disabled = false;
    if (spinner) spinner.style.display = 'none';
  }
}

// OpenFlux Zen Server Panel Updates
let panelInfoCache = null;

async function loadPanelVersionInfo(forceCheck = false) {
  try {
    const url = forceCheck ? 'api/panel/check-update' : 'api/panel/version';
    const method = forceCheck ? 'POST' : 'GET';
    const res = await api(url, { method });
    if (!res.ok) return;
    const info = await res.json();
    panelInfoCache = info;
    renderPanelVersionInfo(info);
  } catch (e) {
    console.warn('Failed to load panel version info:', e);
  }
}

function renderPanelVersionInfo(info) {
  if (!info) return;

  const curBadge = document.getElementById('panel-current-version-badge');
  if (curBadge) {
    curBadge.textContent = info.currentVersion || 'v1.0.31';
  }

  const curVer = document.getElementById('panel-current-version');
  if (curVer) curVer.textContent = info.currentVersion || 'v1.0.31';

  const latVer = document.getElementById('panel-latest-version');
  if (latVer) latVer.textContent = info.latestVersion || info.currentVersion || '—';

  const hdrVer = document.getElementById('header-version-text');
  const hdrDot = document.getElementById('header-update-dot');
  if (hdrVer && info.currentVersion) {
    hdrVer.textContent = info.currentVersion;
  }
  if (hdrDot) {
    if (info.isUpdateAvailable) {
      hdrDot.style.background = '#fbbf24';
      hdrDot.title = t('panel_update_available');
    } else {
      hdrDot.style.background = '#10b981';
      hdrDot.title = t('panel_up_to_date');
    }
  }

  const relDate = document.getElementById('panel-release-date');
  if (relDate) {
    if (info.publishedAt) {
      const dt = new Date(info.publishedAt);
      relDate.textContent = dt.toLocaleDateString();
    } else {
      relDate.textContent = '—';
    }
  }

  const lastChecked = document.getElementById('panel-last-checked');
  if (lastChecked) {
    if (info.lastCheckedAt) {
      const dt = new Date(info.lastCheckedAt);
      lastChecked.textContent = dt.toLocaleString();
    } else {
      lastChecked.textContent = t('panel_not_checked') || '—';
    }
  }

  const statusBadge = document.getElementById('panel-status-badge');
  const btnUpdate = document.getElementById('btn-update-panel');
  const releaseBox = document.getElementById('panel-release-notes-box');
  const releaseText = document.getElementById('panel-release-notes-text');
  const releaseLink = document.getElementById('panel-release-link');

  if (info.isUpdateAvailable) {
    if (statusBadge) {
      statusBadge.style.display = 'inline-block';
      statusBadge.style.background = 'rgba(245, 158, 11, 0.15)';
      statusBadge.style.border = '1px solid rgba(245, 158, 11, 0.35)';
      statusBadge.style.color = '#fbbf24';
      statusBadge.textContent = t('panel_update_available');
    }
    if (btnUpdate) btnUpdate.style.display = 'inline-flex';
    if (releaseBox) {
      releaseBox.style.display = 'block';
      if (releaseText) releaseText.textContent = info.releaseNotes || '—';
      if (releaseLink && info.releaseUrl) releaseLink.href = info.releaseUrl;
    }
  } else {
    if (statusBadge) {
      statusBadge.style.display = 'inline-block';
      statusBadge.style.background = 'rgba(16, 185, 129, 0.15)';
      statusBadge.style.border = '1px solid rgba(16, 185, 129, 0.35)';
      statusBadge.style.color = '#34d399';
      statusBadge.textContent = t('panel_up_to_date');
    }
    if (btnUpdate) btnUpdate.style.display = 'none';
    if (releaseBox) releaseBox.style.display = 'none';
  }
}

async function checkForPanelUpdates() {
  const btn = document.getElementById('btn-check-panel');
  const spinner = document.getElementById('btn-check-panel-spinner');
  if (btn) btn.disabled = true;
  if (spinner) spinner.style.display = 'inline-block';

  try {
    const res = await api('api/panel/check-update', { method: 'POST' });
    if (res.ok) {
      const info = await res.json();
      panelInfoCache = info;
      renderPanelVersionInfo(info);
      if (info.isUpdateAvailable) {
        toast(t('toast_panel_update_available'), 'info');
      } else {
        toast(t('toast_panel_up_to_date'), 'success');
      }
    } else {
      toast(t('toast_panel_check_failed'), 'danger');
    }
  } catch (e) {
    toast(t('toast_panel_check_failed'), 'danger');
  } finally {
    if (btn) btn.disabled = false;
    if (spinner) spinner.style.display = 'none';
  }
}

async function confirmUpdatePanel() {
  const targetVer = (panelInfoCache && panelInfoCache.latestVersion) ? panelInfoCache.latestVersion : '';
  const msg = t('confirm_update_panel', { version: targetVer });
  if (!confirm(msg)) return;

  const btn = document.getElementById('btn-update-panel');
  const spinner = document.getElementById('btn-update-panel-spinner');
  if (btn) btn.disabled = true;
  if (spinner) spinner.style.display = 'inline-block';

  toast(t('toast_panel_updating'), 'info');

  try {
    const res = await api('api/panel/update', { method: 'POST' });
    const result = await res.json();
    if (res.ok && result.success) {
      toast(result.message || t('toast_panel_update_success'), 'success');

      // Poll until server restarts and comes back online
      let attempts = 0;
      const maxAttempts = 25;
      setTimeout(async function pollServer() {
        attempts++;
        try {
          const checkRes = await fetch('api/panel/version', { cache: 'no-store' });
          if (checkRes.ok) {
            toast(t('toast_panel_restarted'), 'success');
            setTimeout(() => window.location.reload(), 1500);
            return;
          }
        } catch (_) {
          // Expected while server process is restarting
        }

        if (attempts < maxAttempts) {
          setTimeout(pollServer, 2000);
        } else {
          toast(currentLanguage === 'en' ? 'Please refresh the page manually (F5).' : 'Пожалуйста, обновите страницу вручную (F5).', 'info');
          if (btn) btn.disabled = false;
          if (spinner) spinner.style.display = 'none';
        }
      }, 3000);

    } else {
      toast(result.message || t('toast_panel_update_failed'), 'danger');
      if (btn) btn.disabled = false;
      if (spinner) spinner.style.display = 'none';
    }
  } catch (e) {
    toast(t('toast_panel_update_failed'), 'danger');
    if (btn) btn.disabled = false;
    if (spinner) spinner.style.display = 'none';
  }
}


