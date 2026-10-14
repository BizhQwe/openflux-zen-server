// OpenFlux Zen Server - Settings & Backup

function formatDateTimeCustom(dateInput) {
  if (!dateInput) return '—';
  const d = new Date(dateInput);
  if (isNaN(d.getTime())) return '—';
  const day = String(d.getDate()).padStart(2, '0');
  const month = String(d.getMonth() + 1).padStart(2, '0');
  const year = String(d.getFullYear()).slice(-2);
  const hours = String(d.getHours()).padStart(2, '0');
  const minutes = String(d.getMinutes()).padStart(2, '0');
  const seconds = String(d.getSeconds()).padStart(2, '0');
  return `${day}.${month}.${year} : ${hours}:${minutes}:${seconds}`;
}

function renderMarkdown(md) {
  if (!md || !md.trim()) return '<p style="color: var(--text-muted); margin: 0;">—</p>';

  let text = md.replace(/\r\n/g, '\n').replace(/\r/g, '\n');

  // Escape HTML entities to prevent XSS
  text = text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');

  // Fenced code blocks
  text = text.replace(/```([\s\S]*?)```/g, (match, code) => {
    return `<pre><code>${code.trim()}</code></pre>`;
  });

  // Inline code
  text = text.replace(/`([^`\n]+)`/g, '<code>$1</code>');

  // Headers
  text = text.replace(/^### (.*$)/gim, '<h5>$1</h5>');
  text = text.replace(/^## (.*$)/gim, '<h4>$1</h4>');
  text = text.replace(/^# (.*$)/gim, '<h3>$1</h3>');

  // Bold
  text = text.replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>');
  text = text.replace(/__(.*?)__/g, '<strong>$1</strong>');

  // Italic
  text = text.replace(/(^|[^\*])\*([^\*\n]+)\*([^\*]|$)/g, '$1<em>$2</em>$3');

  // Markdown links: [text](url)
  text = text.replace(/\[([^\]]+)\]\((https?:\/\/[^\s\)]+)\)/g, '<a href="$2" target="_blank" rel="noopener noreferrer">$1</a>');

  // Standalone URLs not already in href
  text = text.replace(/(^|[^">])(https?:\/\/[^\s<]+)/g, '$1<a href="$2" target="_blank" rel="noopener noreferrer">$2</a>');

  // Lists & paragraphs
  const lines = text.split('\n');
  let inList = false;
  const resultLines = [];

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    const listMatch = line.match(/^(\s*)[\*\-]\s+(.*)$/);
    if (listMatch) {
      if (!inList) {
        resultLines.push('<ul>');
        inList = true;
      }
      resultLines.push(`<li>${listMatch[2]}</li>`);
    } else {
      if (inList) {
        resultLines.push('</ul>');
        inList = false;
      }
      if (line.trim().length > 0) {
        if (/^<(h[1-6]|pre|ul|ol|blockquote)/.test(line.trim())) {
          resultLines.push(line);
        } else {
          resultLines.push(`<p>${line}</p>`);
        }
      }
    }
  }
  if (inList) {
    resultLines.push('</ul>');
  }

  return resultLines.join('\n');
}

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
  try {
    const res = await api('api/config/export');
    const text = await res.text();
    const ok = await copyToClipboard(text);
    toast(t('toast_copied'), ok ? 'success' : 'info');
  } catch (err) {
    toast(t('toast_error'), 'danger');
  }
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
    await loadNetworkPlacementStatus();
  }

  loadPanelVersionInfo(false);
  loadCoreVersionInfo(false);
}

let localtunnelPollInterval = null;
let networkPlacementInfo = null;

async function loadNetworkPlacementStatus() {
  try {
    const res = await api('api/settings/network-placement');
    if (!res.ok) return;
    const d = await res.json();
    networkPlacementInfo = d;
    const portInput = document.getElementById('setting-panel-port');
    if (portInput) portInput.value = d.panelPort;

    const checkLt = document.getElementById('check-placement-localhost');
    if (checkLt) { checkLt.checked = true; checkLt.disabled = true; }

    const checkLan = document.getElementById('check-placement-lan');
    if (checkLan) {
      checkLan.checked = !!d.lanEnabled;
      checkLan.disabled = d.lanAvailable === false;
      checkLan.closest('label')?.classList.toggle('disabled', checkLan.disabled);
    }
    const subnetInput = document.getElementById('setting-lan-subnets');
    if (subnetInput) subnetInput.value = d.lanSubnets || '';

    const checkLocaltunnel = document.getElementById('check-placement-localtunnel');
    if (checkLocaltunnel) checkLocaltunnel.checked = !!d.localtunnelEnabled;

    const checkDomain = document.getElementById('check-placement-domain');
    if (checkDomain) checkDomain.checked = !!d.domainEnabled;

    const domInput = document.getElementById('setting-domain-input');
    if (domInput) domInput.value = d.domain || '';

    const urlLocalhost = document.getElementById('url-placement-localhost');
    if (urlLocalhost && d.localhostUrl) urlLocalhost.value = d.localhostUrl;

    const urlLan = document.getElementById('url-placement-lan');
    if (urlLan) {
      urlLan.value = d.lanUrl || '';
      urlLan.placeholder = currentLanguage === 'en' ? 'No private interface address' : 'Адрес частного интерфейса не найден';
    }

    const urlLt = document.getElementById('url-placement-localtunnel');
    if (urlLt) {
      urlLt.value = d.localtunnelUrl || (d.localtunnelEnabled ? (currentLanguage === 'en' ? 'Connecting to Localtunnel...' : 'Подключение к Localtunnel...') : '');
    }

    const urlDomain = document.getElementById('url-placement-domain');
    if (urlDomain && d.domainUrl) urlDomain.value = d.domainUrl;

    updateLocaltunnelStatusUi(d);
    onPlacementOptionToggle();

    if (d.localtunnelEnabled && (d.localtunnelStatus === 'connecting' || !d.localtunnelUrl)) {
      startLocaltunnelPolling();
    } else {
      stopLocaltunnelPolling();
    }
  } catch (e) {
    console.error('Failed to load network placement status', e);
  }
}

function updateLocaltunnelStatusUi(d) {
}

function startLocaltunnelPolling() {
  if (localtunnelPollInterval) return;
  let attempts = 0;
  localtunnelPollInterval = setInterval(async () => {
    attempts++;
    if (attempts > 20) {
      stopLocaltunnelPolling();
      return;
    }
    try {
      const res = await api('api/settings/network-placement');
      if (res.ok) {
        const d = await res.json();
        const urlLt = document.getElementById('url-placement-localtunnel');
        if (urlLt && d.localtunnelUrl) {
          urlLt.value = d.localtunnelUrl;
        }

        updateLocaltunnelStatusUi(d);

        if (d.localtunnelStatus === 'connected' && d.localtunnelUrl) {
          stopLocaltunnelPolling();
        }
      }
    } catch {}
  }, 2000);
}

function stopLocaltunnelPolling() {
  if (localtunnelPollInterval) {
    clearInterval(localtunnelPollInterval);
    localtunnelPollInterval = null;
  }
}

function onPlacementOptionToggle() {
  const checkLan = document.getElementById('check-placement-lan');
  const checkLt = document.getElementById('check-placement-localtunnel');
  const checkDom = document.getElementById('check-placement-domain');

  const boxLan = document.getElementById('placement-box-lan');
  const boxLt = document.getElementById('placement-box-localtunnel');
  const boxDom = document.getElementById('placement-box-domain');

  const offLan = document.getElementById('off-label-lan');
  const offLt = document.getElementById('off-label-localtunnel');
  const offDom = document.getElementById('off-label-domain');

  const isLan = checkLan && checkLan.checked;
  const isLt = checkLt && checkLt.checked;
  const isDom = checkDom && checkDom.checked;
  const subnetField = document.getElementById('placement-lan-subnets');
  if (subnetField) subnetField.style.display = isLan ? 'block' : 'none';

  if (boxLan) boxLan.style.display = isLan ? 'flex' : 'none';
  if (offLan) offLan.style.display = !isLan ? 'inline-flex' : 'none';
  if (offLan) {
    offLan.dataset.i18n = networkPlacementInfo?.lanAvailable === false ? 'placement_lan_unavailable' : 'placement_disabled';
    offLan.textContent = t(offLan.dataset.i18n);
  }

  if (boxLt) boxLt.style.display = isLt ? 'flex' : 'none';
  if (offLt) offLt.style.display = !isLt ? 'inline-flex' : 'none';

  if (boxDom) boxDom.style.display = isDom ? 'flex' : 'none';
  if (offDom) offDom.style.display = !isDom ? 'inline-flex' : 'none';

  const itemLocalhost = document.getElementById('placement-item-localhost');
  const itemLan = document.getElementById('placement-item-lan');
  const itemLt = document.getElementById('placement-item-localtunnel');
  const itemDom = document.getElementById('placement-item-domain');

  if (itemLocalhost) itemLocalhost.classList.add('active');
  if (itemLan) itemLan.classList.toggle('active', !!isLan);
  if (itemLt) itemLt.classList.toggle('active', !!isLt);
  if (itemDom) itemDom.classList.toggle('active', !!isDom);

  const modeBadge = document.getElementById('setting-network-mode');
  if (modeBadge) {
    const active = ['Localhost'];
    if (checkLan && checkLan.checked) active.push('LAN');
    if (checkLt && checkLt.checked) active.push('Localtunnel');
    if (checkDom && checkDom.checked) active.push('Domain');
    modeBadge.textContent = active.join(' + ');
  }
}

function updateDomainPreviewUrl() {
  const domInput = document.getElementById('setting-domain-input');
  const urlDomain = document.getElementById('url-placement-domain');
  const secretInput = document.getElementById('setting-secret-path');
  if (!domInput || !urlDomain) return;

  const dom = domInput.value.trim();
  const secret = networkPlacementInfo?.secretPath || (secretInput ? secretInput.value.replace(/^\/+|\/+$/g, '') : window.location.pathname.split('/')[1]);
  if (!dom) {
    urlDomain.value = '';
    return;
  }
  try {
    const explicit = dom.includes('://');
    const scheme = networkPlacementInfo?.domainScheme || networkPlacementInfo?.panelScheme || window.location.protocol.slice(0, -1);
    const url = new URL(explicit ? dom : `${scheme}://${dom}`);
    if (!explicit && (url.hostname === 'localhost' || url.hostname.includes(':') || /^\d+\.\d+\.\d+\.\d+$/.test(url.hostname))) url.protocol = 'http:';
    if (!explicit) url.port = document.getElementById('setting-panel-port')?.value || networkPlacementInfo?.panelPort || window.location.port;
    url.pathname = `/${secret}/`;
    urlDomain.value = url.toString();
  } catch { urlDomain.value = ''; }
}

async function copyPlacementUrl(elementId) {
  const inp = document.getElementById(elementId);
  if (inp && inp.value) {
    const ok = await copyToClipboard(inp.value, inp);
    toast(t('toast_copied'), ok ? 'success' : 'info');
  }
}

async function saveNetworkPlacement() {
  const checkLan = document.getElementById('check-placement-lan');
  const checkLt = document.getElementById('check-placement-localtunnel');
  const checkDom = document.getElementById('check-placement-domain');
  const domInput = document.getElementById('setting-domain-input');
  const btn = document.getElementById('btn-save-network-mode');
  const spinner = document.getElementById('btn-save-network-spinner');

  const lanEnabled = checkLan ? checkLan.checked : true;
  const localtunnelEnabled = checkLt ? checkLt.checked : false;
  const domainEnabled = checkDom ? checkDom.checked : false;
  const domain = domInput ? domInput.value.trim() : '';
  const panelPort = Number(document.getElementById('setting-panel-port')?.value || networkPlacementInfo?.panelPort);
  if (!Number.isInteger(panelPort) || panelPort < 1 || panelPort > 65535) {
    toast(currentLanguage === 'en' ? 'Panel port must be between 1 and 65535.' : 'Порт панели должен быть от 1 до 65535.', 'danger');
    return;
  }

  try {
    if (btn) btn.disabled = true;
    if (spinner) spinner.style.display = 'inline-block';

    const res = await api('api/settings/network-placement', {
      method: 'POST',
      body: JSON.stringify({
        localhostEnabled: true,
        lanEnabled,
        localtunnelEnabled,
        domainEnabled,
        domain,
        panelPort,
        lanSubnets: document.getElementById('setting-lan-subnets')?.value.trim() || ''
      })
    });

    const d = await res.json();
    if (res.ok && d.success) {
      const previousNetwork = networkPlacementInfo;
      networkPlacementInfo = d;
      if (checkLan) checkLan.checked = !!d.lanEnabled;
      toast(t('toast_network_mode_updated'), 'success');

      const previousPort = window.location.port || (window.location.protocol === 'https:' ? '443' : '80');
      const direct = String(previousNetwork?.panelPort) === previousPort && previousNetwork?.requestScheme + ':' === window.location.protocol;
      if (direct && String(d.panelPort) !== previousPort) {
        const next = new URL(window.location.href);
        next.port = d.panelPort;
        window.location.href = next.toString();
        return;
      }

      if (d.sslDetectedDesc) {
        const isRu = (typeof currentLanguage !== 'undefined' ? currentLanguage : 'ru') === 'ru';
        toast((isRu ? 'SSL подключен: ' : 'SSL attached: ') + d.sslDetectedDesc, 'info');
      }

      const urlDom = document.getElementById('url-placement-domain');
      if (urlDom && d.domainUrl) urlDom.value = d.domainUrl;

      const urlLt = document.getElementById('url-placement-localtunnel');
      if (urlLt && d.localtunnelUrl) urlLt.value = d.localtunnelUrl;

      updateLocaltunnelStatusUi(d);
      onPlacementOptionToggle();

      if (localtunnelEnabled) {
        startLocaltunnelPolling();
      } else {
        stopLocaltunnelPolling();
      }
      await loadNetworkPlacementStatus();
    } else {
      toast(d.message || t('toast_network_mode_failed'), 'danger');
    }
  } catch (err) {
    toast(t('toast_network_mode_failed'), 'danger');
  } finally {
    if (btn) btn.disabled = false;
    if (spinner) spinner.style.display = 'none';
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
  const isEn = (typeof currentLanguage !== 'undefined' && currentLanguage === 'en');
  const msg = isEn
    ? 'Are you sure you want to regenerate the secret access path? You will be redirected to the new URL.'
    : 'Вы уверены, что хотите перегенерировать секретный путь панели? Вы будете перенаправлены на новый адрес.';
  if (!confirm(msg)) return;

  try {
    const res = await api('api/settings/regenerate-secret', { method: 'POST' });
    if (res.ok) {
      const d = await res.json();
      const newSecret = d.secretPath ? d.secretPath.replace(/^\/+|\/+$/g, '') : '';
      toast(isEn ? 'Secret path updated! Redirecting...' : 'Секретный путь обновлён! Перенаправление...', 'success');
      setTimeout(() => {
        window.location.pathname = '/' + newSecret + '/';
      }, 1500);
    } else {
      toast(t('toast_error'), 'danger');
    }
  } catch {
    toast(t('toast_error'), 'danger');
  }
}

// OpenFlux Core Updates
let coreInfoCache = null;
let isCheckingCore = false;

async function loadCoreVersionInfo(forceCheck = false) {
  if (forceCheck && isCheckingCore) return;
  try {
    if (forceCheck) isCheckingCore = true;
    const url = forceCheck ? 'api/core/check-update' : 'api/core/version';
    const method = forceCheck ? 'POST' : 'GET';
    const res = await api(url, { method });
    if (!res.ok) return;
    const info = await res.json();
    coreInfoCache = info;
    renderCoreVersionInfo(info);
  } catch (e) {
    console.warn('Failed to load core version info:', e);
  } finally {
    if (forceCheck) isCheckingCore = false;
  }
}

function renderCoreVersionInfo(info) {
  if (!info) return;

  const isRu = (typeof currentLanguage !== 'undefined' ? currentLanguage : 'ru') === 'ru';

  const curBadge = document.getElementById('core-current-version-badge');
  if (curBadge) {
    curBadge.textContent = `OpenFlux: ${info.currentVersion || '0.7.3'}`;
  }

  const curVer = document.getElementById('core-current-version');
  if (curVer) curVer.textContent = info.currentVersion || '0.7.3';

  const latVer = document.getElementById('core-latest-version');
  if (latVer) latVer.textContent = info.latestVersion || info.currentVersion || '—';

  const relDate = document.getElementById('core-release-date');
  if (relDate) {
    if (info.publishedAt) {
      relDate.textContent = formatDateTimeCustom(info.publishedAt);
    } else {
      relDate.textContent = '—';
    }
  }

  const lastChecked = document.getElementById('core-last-checked');
  if (lastChecked) {
    if (info.lastCheckedAt) {
      lastChecked.textContent = formatDateTimeCustom(info.lastCheckedAt);
    } else {
      lastChecked.textContent = t('core_not_checked') || '—';
    }
  }

  const statusBadge = document.getElementById('core-status-badge');
  const btnCheck = document.getElementById('btn-check-core');
  const btnUpdate = document.getElementById('btn-update-core');
  const releaseBox = document.getElementById('core-release-notes-box');
  const releaseText = document.getElementById('core-release-notes-text');
  const releaseLink = document.getElementById('core-release-link');

  if (info.isUpdateAvailable) {
    if (statusBadge) {
      statusBadge.style.display = 'inline-flex';
      statusBadge.textContent = t('core_update_available');
    }
    if (btnUpdate) btnUpdate.style.display = 'inline-flex';
    if (btnCheck) btnCheck.style.display = 'none';
    if (releaseBox) {
      releaseBox.style.display = 'block';
      if (releaseText) releaseText.innerHTML = renderMarkdown(info.releaseNotes || '');
      if (releaseLink && info.releaseUrl) releaseLink.href = info.releaseUrl;
    }
  } else {
    if (statusBadge) {
      statusBadge.style.display = 'none';
      statusBadge.textContent = '';
    }
    if (btnUpdate) btnUpdate.style.display = 'none';
    if (btnCheck) btnCheck.style.display = 'inline-flex';
    if (releaseBox) releaseBox.style.display = 'none';
  }

  updateHeaderStatusBadge();
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
let isCheckingPanel = false;

async function loadPanelVersionInfo(forceCheck = false) {
  if (forceCheck && isCheckingPanel) return;
  try {
    if (forceCheck) isCheckingPanel = true;
    const url = forceCheck ? 'api/panel/check-update' : 'api/panel/version';
    const method = forceCheck ? 'POST' : 'GET';
    const res = await api(url, { method });
    if (!res.ok) return;
    const info = await res.json();
    panelInfoCache = info;
    renderPanelVersionInfo(info);
  } catch (e) {
    console.warn('Failed to load panel version info:', e);
  } finally {
    if (forceCheck) isCheckingPanel = false;
  }
}

function renderPanelVersionInfo(info) {
  if (!info) return;

  const isRu = (typeof currentLanguage !== 'undefined' ? currentLanguage : 'ru') === 'ru';

  const curBadge = document.getElementById('panel-current-version-badge');
  if (curBadge) {
    curBadge.textContent = `Server: ${info.currentVersion || '2.0.0'}`;
  }

  const curVer = document.getElementById('panel-current-version');
  if (curVer) curVer.textContent = info.currentVersion || '2.0.0';

  const latVer = document.getElementById('panel-latest-version');
  if (latVer) latVer.textContent = info.latestVersion || info.currentVersion || '2.0.0';

  const relDate = document.getElementById('panel-release-date');
  if (relDate) {
    if (info.publishedAt) {
      relDate.textContent = formatDateTimeCustom(info.publishedAt);
    } else {
      relDate.textContent = '—';
    }
  }

  const lastChecked = document.getElementById('panel-last-checked');
  if (lastChecked) {
    if (info.lastCheckedAt) {
      lastChecked.textContent = formatDateTimeCustom(info.lastCheckedAt);
    } else {
      lastChecked.textContent = t('panel_not_checked') || '—';
    }
  }

  const statusBadge = document.getElementById('panel-status-badge');
  const btnCheck = document.getElementById('btn-check-panel');
  const btnUpdate = document.getElementById('btn-update-panel');
  const releaseBox = document.getElementById('panel-release-notes-box');
  const releaseText = document.getElementById('panel-release-notes-text');
  const releaseLink = document.getElementById('panel-release-link');

  if (info.isUpdateAvailable) {
    if (statusBadge) {
      statusBadge.style.display = 'inline-flex';
      statusBadge.textContent = t('panel_update_available');
    }
    if (btnUpdate) btnUpdate.style.display = 'inline-flex';
    if (btnCheck) btnCheck.style.display = 'none';
    if (releaseBox) {
      releaseBox.style.display = 'block';
      if (releaseText) releaseText.innerHTML = renderMarkdown(info.releaseNotes || '');
      if (releaseLink && info.releaseUrl) releaseLink.href = info.releaseUrl;
    }
  } else {
    if (statusBadge) {
      statusBadge.style.display = 'none';
      statusBadge.textContent = '';
    }
    if (btnUpdate) btnUpdate.style.display = 'none';
    if (btnCheck) btnCheck.style.display = 'inline-flex';
    if (releaseBox) releaseBox.style.display = 'none';
  }

  updateHeaderStatusBadge();
}

function updateHeaderStatusBadge() {
  const badge = document.getElementById('header-update-badge');
  const dot = document.getElementById('header-update-dot');
  const text = document.getElementById('header-status-text');
  if (!badge) return;

  const panelHasUpdate = Boolean(panelInfoCache && panelInfoCache.isUpdateAvailable);
  const coreHasUpdate = Boolean(coreInfoCache && coreInfoCache.isUpdateAvailable);
  const hasUpdate = Boolean(panelHasUpdate || coreHasUpdate);

  if (hasUpdate) {
    badge.classList.add('visible');
    badge.style.setProperty('display', 'inline-flex', 'important');
    if (dot) {
      dot.style.background = '#f59e0b';
      dot.style.boxShadow = '0 0 8px rgba(245, 158, 11, 0.6)';
    }
    if (text) {
      text.textContent = t('header_status_update_available');
      text.style.color = '#fbbf24';
    }
    badge.style.borderColor = 'rgba(245, 158, 11, 0.4)';
    badge.style.background = 'rgba(245, 158, 11, 0.08)';
    badge.title = (typeof currentLanguage !== 'undefined' && currentLanguage === 'ru')
      ? 'Доступно обновление'
      : 'Update is available';
  } else {
    badge.classList.remove('visible');
    badge.style.setProperty('display', 'none', 'important');
  }
}

// Immediate initial call
updateHeaderStatusBadge();

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
            setTimeout(() => {
              window.location.href = window.location.pathname + '?_v=' + Date.now();
            }, 1500);
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

// Rollback & Version Selection
let currentRollbackType = 'panel'; // 'panel' or 'core'
let rollbackReleases = [];

async function openRollbackModal(type) {
  currentRollbackType = type;
  const modal = document.getElementById('rollback-modal');
  if (!modal) return;

  const titleEl = document.getElementById('rollback-modal-title');
  const descEl = document.getElementById('rollback-modal-desc');
  const selectEl = document.getElementById('rollback-version-select');
  const btnSubmit = document.getElementById('btn-submit-rollback');
  const spinner = document.getElementById('btn-rollback-spinner');

  if (spinner) spinner.style.display = 'none';
  if (btnSubmit) btnSubmit.disabled = true;

  const isRu = currentLanguage === 'ru';

  if (type === 'panel') {
    if (titleEl) titleEl.textContent = isRu ? 'Смена версии OpenFlux Zen Server' : 'Change OpenFlux Zen Server Version';
    const curVer = (panelInfoCache && panelInfoCache.currentVersion) ? panelInfoCache.currentVersion : 'v1.1.2';
    if (descEl) {
      descEl.innerHTML = isRu 
        ? `Текущая версия: <strong>${escapeHtml(curVer)}</strong>. Выберите версию из официальных релизов GitHub (BizhQwe/openflux-zen-server) для установки:`
        : `Current version: <strong>${escapeHtml(curVer)}</strong>. Select a version from official GitHub releases (BizhQwe/openflux-zen-server) to install:`;
    }
  } else {
    if (titleEl) titleEl.textContent = isRu ? 'Смена версии OpenFlux' : 'Change OpenFlux Version';
    const curVer = (coreInfoCache && coreInfoCache.currentVersion) ? coreInfoCache.currentVersion : 'unknown';
    if (descEl) {
      descEl.innerHTML = isRu
        ? `Текущая версия: <strong>${escapeHtml(curVer)}</strong>. Выберите версию из официальных релизов GitHub (p1neappleXpress/OpenFlux) для установки:`
        : `Current version: <strong>${escapeHtml(curVer)}</strong>. Select a version from official GitHub releases (p1neappleXpress/OpenFlux) to install:`;
    }
  }

  if (selectEl) {
    selectEl.innerHTML = `<option value="">${isRu ? 'Загрузка списка версий с GitHub...' : 'Loading release list from GitHub...'}</option>`;
  }

  modal.classList.add('open');

  try {
    const endpoint = type === 'panel' ? 'api/panel/releases' : 'api/core/releases';
    const res = await api(endpoint);
    if (!res.ok) throw new Error('API returned status ' + res.status);
    rollbackReleases = await res.json();

    if (!Array.isArray(rollbackReleases) || rollbackReleases.length === 0) {
      if (selectEl) {
        selectEl.innerHTML = `<option value="">${isRu ? 'Список релизов пуст' : 'No releases found'}</option>`;
      }
      return;
    }

    if (selectEl) {
      selectEl.innerHTML = '';
      const curVer = type === 'panel' 
        ? ((panelInfoCache && panelInfoCache.currentVersion) || '')
        : ((coreInfoCache && coreInfoCache.currentVersion) || '');

      rollbackReleases.forEach((rel) => {
        const opt = document.createElement('option');
        opt.value = rel.tagName;
        const dateStr = rel.publishedAt ? formatDateTimeCustom(rel.publishedAt) : '';
        const isCurrent = curVer && (rel.tagName.toLowerCase() === curVer.toLowerCase() || ('v' + rel.tagName.toLowerCase()) === curVer.toLowerCase());
        const label = `${rel.tagName}${rel.prerelease ? ' (pre-release)' : ''} ${dateStr ? '— ' + dateStr : ''}${isCurrent ? (isRu ? ' [Текущая]' : ' [Current]') : ''}`;
        opt.textContent = label;
        selectEl.appendChild(opt);
      });
      if (btnSubmit) btnSubmit.disabled = false;
    }
  } catch (err) {
    console.error('Failed to fetch releases:', err);
    if (selectEl) {
      selectEl.innerHTML = `<option value="">${isRu ? 'Ошибка загрузки релизов' : 'Failed to load releases'}</option>`;
    }
    toast(t('toast_releases_failed'), 'danger');
  }
}

function closeRollbackModal() {
  const modal = document.getElementById('rollback-modal');
  if (modal) modal.classList.remove('open');
}

function onRollbackVersionChange() {
  // Can be used to preview release details
}

async function submitRollback() {
  const selectEl = document.getElementById('rollback-version-select');
  const targetVer = selectEl ? selectEl.value : '';
  if (!targetVer) return;

  const isRu = currentLanguage === 'ru';
  const confirmMsg = currentRollbackType === 'panel'
    ? t('confirm_rollback_panel', { version: targetVer })
    : t('confirm_rollback_core', { version: targetVer });

  if (!confirm(confirmMsg)) return;

  const btnSubmit = document.getElementById('btn-submit-rollback');
  const spinner = document.getElementById('btn-rollback-spinner');
  if (btnSubmit) btnSubmit.disabled = true;
  if (spinner) spinner.style.display = 'inline-block';

  try {
    if (currentRollbackType === 'core') {
      toast(t('toast_core_updating'), 'info');
      const res = await api('api/core/update', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ targetVersion: targetVer })
      });
      const data = await res.json();
      if (res.ok && data.success) {
        toast(data.message || t('toast_core_update_success'), 'success');
        closeRollbackModal();
        await loadCoreVersionInfo(false);
        if (typeof loadTunnels === 'function') {
          loadTunnels();
        }
      } else {
        toast(data.message || t('toast_core_update_failed'), 'danger');
      }
    } else {
      // Panel rollback / switch
      toast(t('toast_panel_updating'), 'info');
      const res = await api('api/panel/update', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ targetVersion: targetVer })
      });
      const data = await res.json();
      if (res.ok && data.success) {
        toast(data.message || t('toast_panel_update_success'), 'success');
        closeRollbackModal();

        // Poll until server restarts and comes back online
        let attempts = 0;
        const maxAttempts = 30;
        setTimeout(async function pollServer() {
          attempts++;
          try {
            const checkRes = await fetch('api/panel/version', { cache: 'no-store' });
            if (checkRes.ok) {
              toast(t('toast_panel_restarted'), 'success');
              setTimeout(() => {
                window.location.href = window.location.pathname + '?_v=' + Date.now();
              }, 1500);
              return;
            }
          } catch (_) { }

          if (attempts < maxAttempts) {
            setTimeout(pollServer, 2000);
          } else {
            toast(isRu ? 'Пожалуйста, обновите страницу вручную (F5).' : 'Please refresh the page manually (F5).', 'info');
          }
        }, 3000);
      } else {
        toast(data.message || t('toast_panel_update_failed'), 'danger');
      }
    }
  } catch (err) {
    toast(t('toast_error'), 'danger');
  } finally {
    if (btnSubmit) btnSubmit.disabled = false;
    if (spinner) spinner.style.display = 'none';
  }
}
