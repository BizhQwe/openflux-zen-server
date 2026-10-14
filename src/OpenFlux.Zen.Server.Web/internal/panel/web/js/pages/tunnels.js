// OpenFlux Zen Server - Tunnels Management

async function loadTunnels(silent = false) {
  try {
    const res = await api('api/tunnels');
    if (!res.ok) {
      console.error('Failed to load tunnels: HTTP', res.status);
      if (!silent) {
        let errMsg = 'HTTP ' + res.status;
        try {
          const errData = await res.json();
          if (errData && (errData.detail || errData.error || errData.message)) {
            errMsg += ': ' + (errData.detail || errData.error || errData.message);
          }
        } catch {}
        toast('Ошибка загрузки туннелей (' + errMsg + ')', 'danger');
      }
      return;
    }
    const data = await res.json();
    tunnelsData = Array.isArray(data) ? data : [];

    if (silent && tunnelsData.length > 0) {
      let canUpdateInPlace = true;
      for (const item of tunnelsData) {
        const tid = item.id || item.Id;
        if (!tid || !document.getElementById('tunnel-card-' + tid)) {
          canUpdateInPlace = false;
          break;
        }
      }
      if (canUpdateInPlace) {
        updateTunnelsInPlace(tunnelsData);
        return;
      }
    }

    renderTunnels(tunnelsData);
    updateLogSelect(tunnelsData);
  } catch (err) {
    if (!silent) {
      console.error('Failed to load tunnels:', err);
      toast('Ошибка загрузки туннелей: ' + (err.message || err), 'danger');
    }
  }
}

function getClientStatusBadge(connectedClients, isRunning, clientStatus) {
  const isEn = typeof currentLanguage !== 'undefined' && currentLanguage === 'en';
  if (isRunning && (clientStatus === 'activity' || clientStatus === 'idle')) {
    const label = clientStatus === 'activity' ? (isEn ? 'Client activity' : 'Есть активность') : (isEn ? 'No client activity' : 'Нет активности');
    return `<span style="color: ${clientStatus === 'activity' ? '#34d399' : 'var(--text-dim)'};">${label}</span>`;
  }
  if (isRunning && connectedClients > 0) {
    return `<span style="color: #34d399; font-weight: 600;">● ${t('clients_connected') || (isEn ? 'Connected' : 'Подключен')}</span>`;
  }
  if (isRunning) {
    return `<span style="color: var(--text-dim);">${t('clients_none') || (isEn ? 'Not connected' : 'Не подключен')}</span>`;
  }
  return `<span style="color: var(--text-dim);">${isEn ? 'Stopped' : 'Остановлен'}</span>`;
}

function updateTunnelsInPlace(list) {
  if (!Array.isArray(list)) return;
  for (const tItem of list) {
    const tid = tItem.id || tItem.Id;
    if (!tid) continue;
    const connectedClients = tItem.connectedClients ?? tItem.ConnectedClients ?? 0;
    const uploadBytes = tItem.uploadBytes ?? tItem.UploadBytes ?? 0;
    const downloadBytes = tItem.downloadBytes ?? tItem.DownloadBytes ?? 0;
    const backendUpRate = tItem.uploadRateBytesPerSec ?? tItem.UploadRateBytesPerSec ?? 0;
    const backendDownRate = tItem.downloadRateBytesPerSec ?? tItem.DownloadRateBytesPerSec ?? 0;
    const clientStatus = tItem.clientStatus ?? tItem.ClientStatus;
    const uploadRate = (tItem.status ?? tItem.Status) === 2 ? backendUpRate : 0;
    const downloadRate = (tItem.status ?? tItem.Status) === 2 ? backendDownRate : 0;
    const status = tItem.status ?? tItem.Status ?? 0;
    const errorMsg = tItem.errorMessage || tItem.ErrorMessage;

    const isRunning = status === 2;

    const clientsEl = document.getElementById('tunnel-clients-' + tid);
    if (clientsEl) {
      const clientHtml = getClientStatusBadge(connectedClients, isRunning, clientStatus);
      if (clientsEl.innerHTML !== clientHtml) {
        clientsEl.innerHTML = clientHtml;
      }
    }

    const trafficEl = document.getElementById('tunnel-traffic-' + tid);
    if (trafficEl) {
      const totalBytes = uploadBytes + downloadBytes;
      const totalStr = fmtBytes(totalBytes);
      if (trafficEl.textContent !== totalStr) {
        trafficEl.textContent = totalStr;
      }
    }

    const uploadEl = document.getElementById('tunnel-upload-' + tid);
    if (uploadEl) {
      uploadEl.innerHTML = `<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M12 19V5M5 12l7-7 7 7"/></svg>${fmtSpeed(uploadRate)}`;
    }

    const downloadEl = document.getElementById('tunnel-download-' + tid);
    if (downloadEl) {
      downloadEl.innerHTML = `<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M12 5v14M19 12l-7 7-7-7"/></svg>${fmtSpeed(downloadRate)}`;
    }

    const startStopEl = document.getElementById('tunnel-startstop-' + tid);
    if (startStopEl) {
      const isRunning = status === 2;
      const expectedBtnHtml = isRunning
        ? `<button class="btn btn-danger btn-sm" onclick="stopTunnel('${tid}')"><svg width="11" height="11" viewBox="0 0 24 24" fill="currentColor"><rect x="4" y="4" width="16" height="16" rx="2"/></svg>${t('btn_stop')}</button>`
        : `<button class="btn btn-success btn-sm" onclick="startTunnel('${tid}')"><svg width="11" height="11" viewBox="0 0 24 24" fill="currentColor"><polygon points="6 3 20 12 6 21 6 3"/></svg>${t('btn_start')}</button>`;
      if (startStopEl.innerHTML !== expectedBtnHtml) {
        startStopEl.innerHTML = expectedBtnHtml;
      }
    }

    const statusEl = document.getElementById('tunnel-status-' + tid);
    if (statusEl) {
      let badgeHtml = `<span class="badge badge-status badge-stopped">${t('badge_stopped')}</span>`;
      if (status === 2) {
        badgeHtml = `<span class="badge badge-status badge-running" ${errorMsg ? `title="${escapeHtml(errorMsg)}"` : ''}><span class="pulse"></span>${t('badge_running')}</span>`;
      } else if (status === 1) {
        badgeHtml = `<span class="badge badge-status badge-starting" ${errorMsg ? `title="${escapeHtml(errorMsg)}"` : ''}>${t('badge_starting')}</span>`;
      } else if (status === 4 || (status === 0 && errorMsg)) {
        badgeHtml = `<span class="badge badge-status badge-failed" title="${escapeHtml(errorMsg || '')}">${t('badge_failed')}</span>`;
      }
      if (statusEl.innerHTML !== badgeHtml) {
        statusEl.innerHTML = badgeHtml;
      }
    }

    const errorBarEl = document.getElementById('tunnel-error-' + tid);
    if (errorBarEl && !errorMsg) {
      errorBarEl.remove();
    }
  }
}

function renderTunnels(list) {
  const container = document.getElementById('tunnel-list');
  if (!container) return;
  if (!Array.isArray(list) || list.length === 0) {
    container.innerHTML = `
      <div class="card" style="text-align: center; padding: 40px; color: var(--text-dim);">
        <div style="margin-bottom: 14px; opacity: 0.65;">
          <svg width="44" height="44" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
            <rect x="2" y="2" width="20" height="8" rx="2" ry="2"/>
            <rect x="2" y="14" width="20" height="8" rx="2" ry="2"/>
            <line x1="6" y1="6" x2="6.01" y2="6"/>
            <line x1="6" y1="18" x2="6.01" y2="18"/>
          </svg>
        </div>
        <h4>${t('no_tunnels')}</h4>
        <p style="margin-top: 6px;">${t('no_tunnels_sub')}</p>
      </div>
    `;
    return;
  }

  container.innerHTML = list.map(tItem => {
    const tid = tItem.id || tItem.Id || '';
    const tname = tItem.name || tItem.Name || 'Default Tunnel';
    const ttransport = tItem.transport || tItem.Transport || 'yandex';
    const ttransports = tItem.transports || tItem.Transports || '';
    const tmode = tItem.mode || tItem.Mode || 'l4';
    const tcodec = tItem.codec || tItem.Codec || 'batched';
    const status = tItem.status ?? tItem.Status ?? 0;
    const errorMsg = tItem.errorMessage || tItem.ErrorMessage;
    const uploadBytes = tItem.uploadBytes ?? tItem.UploadBytes ?? 0;
    const downloadBytes = tItem.downloadBytes ?? tItem.DownloadBytes ?? 0;
    const backendUpRate = tItem.uploadRateBytesPerSec ?? tItem.UploadRateBytesPerSec ?? 0;
    const backendDownRate = tItem.downloadRateBytesPerSec ?? tItem.DownloadRateBytesPerSec ?? 0;
    const clientStatus = tItem.clientStatus ?? tItem.ClientStatus;
    const uploadRate = (tItem.status ?? tItem.Status) === 2 ? backendUpRate : 0;
    const downloadRate = (tItem.status ?? tItem.Status) === 2 ? backendDownRate : 0;
    const clientLimit = tItem.clientLimit ?? tItem.ClientLimit ?? 0;
    const connectedClients = tItem.connectedClients ?? tItem.ConnectedClients ?? 0;
    const trafficLimitBytes = tItem.trafficLimitBytes ?? tItem.TrafficLimitBytes ?? 0;

    let statusBadge = `<span class="badge badge-status badge-stopped">${t('badge_stopped')}</span>`;
    if (status === 2) {
      statusBadge = `<span class="badge badge-status badge-running" ${errorMsg ? `title="${escapeHtml(errorMsg)}"` : ''}><span class="pulse"></span>${t('badge_running')}</span>`;
    } else if (status === 1) {
      statusBadge = `<span class="badge badge-status badge-starting" ${errorMsg ? `title="${escapeHtml(errorMsg)}"` : ''}>${t('badge_starting')}</span>`;
    } else if (status === 4 || (status === 0 && errorMsg)) {
      statusBadge = `<span class="badge badge-status badge-failed" title="${escapeHtml(errorMsg || '')}">${t('badge_failed')}</span>`;
    }

    const isRunning = status === 2;
    const totalBytes = uploadBytes + downloadBytes;
    const clientStatusHtml = getClientStatusBadge(connectedClients, isRunning, clientStatus);

    return `
      <div class="tunnel-item" id="tunnel-card-${tid}">
        <div class="tunnel-top">
          <div class="tunnel-title-group">
            <div class="tunnel-name">${escapeHtml(tname)}</div>
            <span id="tunnel-status-${tid}">${statusBadge}</span>
            <div class="badges">
              <span class="badge badge-tag">${escapeHtml(ttransport)}</span>
              ${ttransports ? `<span class="badge badge-tag" title="${escapeHtml(ttransports)}">${escapeHtml(ttransports)}</span>` : ''}
              <span class="badge badge-tag">${escapeHtml(tmode)}</span>
              <span class="badge badge-tag">${escapeHtml(tcodec)}</span>
            </div>
          </div>
        </div>

        ${errorMsg ? `
          <div class="tunnel-error-bar" id="tunnel-error-${tid}" style="margin: 8px 0 10px; padding: 6px 12px; background: rgba(239,68,68,0.08); border-left: 3px solid #ef4444; border-radius: 4px; font-size: 12px; display: flex; align-items: center; justify-content: space-between; gap: 8px;">
            <span style="color: #ef4444; line-height: 1.4;">${escapeHtml(errorMsg)}</span>
            <div style="display: flex; gap: 6px; flex-shrink: 0;">
              <button class="btn btn-primary btn-xs" style="white-space: nowrap; font-size: 11px; padding: 3px 8px;" onclick="openCookiesModal('${tid}')" title="Вставить куки для обхода капчи">Ввести куки</button>
              <button class="btn btn-danger btn-xs" style="white-space: nowrap; font-size: 11px; padding: 3px 8px;" onclick="clearTunnelCookies('${tid}')" title="Очистить куки и перезапустить">Сбросить куки</button>
            </div>
          </div>
        ` : ''}

        <div class="tunnel-details">
          <div class="detail-item">
            <span class="detail-label">${t('detail_clients')}</span>
            <span class="detail-val" id="tunnel-clients-${tid}">${clientStatusHtml}</span>
          </div>
          <div class="detail-item">
            <span class="detail-label">${t('detail_upload')}</span>
            <span class="detail-val" id="tunnel-upload-${tid}" style="display: inline-flex; align-items: center; gap: 4px;">
              <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M12 19V5M5 12l7-7 7 7"/></svg>
              ${fmtSpeed(uploadRate)}
            </span>
          </div>
          <div class="detail-item">
            <span class="detail-label">${t('detail_download')}</span>
            <span class="detail-val" id="tunnel-download-${tid}" style="display: inline-flex; align-items: center; gap: 4px;">
              <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M12 5v14M19 12l-7 7-7-7"/></svg>
              ${fmtSpeed(downloadRate)}
            </span>
          </div>
          <div class="detail-item">
            <span class="detail-label">${t('detail_traffic')}</span>
            <span class="detail-val" id="tunnel-traffic-${tid}">${fmtBytes(totalBytes)}</span>
          </div>
        </div>

        <div class="tunnel-actions">
          <div class="action-group">
            <span id="tunnel-startstop-${tid}">
              ${isRunning ? 
                `<button class="btn btn-danger btn-sm" onclick="stopTunnel('${tid}')"><svg width="11" height="11" viewBox="0 0 24 24" fill="currentColor"><rect x="4" y="4" width="16" height="16" rx="2"/></svg>${t('btn_stop')}</button>` :
                `<button class="btn btn-success btn-sm" onclick="startTunnel('${tid}')"><svg width="11" height="11" viewBox="0 0 24 24" fill="currentColor"><polygon points="6 3 20 12 6 21 6 3"/></svg>${t('btn_start')}</button>`}
            </span>
            <button class="btn btn-outline btn-sm" onclick="showTunnelConnect('${tid}')" title="${t('btn_connect_qr') || 'Подключение'}">
              <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <rect x="3" y="3" width="7" height="7"/><rect x="14" y="3" width="7" height="7"/><rect x="14" y="14" width="7" height="7"/><rect x="3" y="14" width="7" height="7"/>
              </svg>
              <span>${t('btn_connect_qr') || 'Подключение'}</span>
            </button>
            <button class="btn btn-outline btn-sm" onclick="openCookiesModal('${tid}')" title="${(typeof currentLanguage !== 'undefined' && currentLanguage === 'en') ? 'Enter or update cookies' : 'Ввести или обновить куки'}">
              <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 2a10 10 0 1 0 10 10 4 4 0 0 1-5-5 4 4 0 0 1-5-5"/><circle cx="8.5" cy="8.5" r="1"/><circle cx="16" cy="15.5" r="1"/><circle cx="12" cy="12" r="1"/></svg>
              <span>${(typeof currentLanguage !== 'undefined' && currentLanguage === 'en') ? 'Cookies' : 'Куки'}</span>
            </button>
          </div>

          <div class="action-group">
            <button class="btn btn-outline btn-sm" onclick="viewTunnelLogs('${tid}')"><svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/></svg>${t('btn_logs')}</button>
            <button class="btn btn-outline btn-sm" onclick="editTunnel('${tid}')"><svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/></svg>${t('btn_edit')}</button>
            <button class="btn btn-outline btn-sm" style="color: #ef4444;" onclick="deleteTunnel('${tid}')"><svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/><line x1="10" y1="11" x2="10" y2="17"/><line x1="14" y1="11" x2="14" y2="17"/></svg>${t('btn_delete')}</button>
          </div>
        </div>
      </div>
    `;
  }).join('');
}

async function startTunnel(id) {
  try {
    const res = await api(`api/tunnels/${id}/start`, { method: 'POST' });
    if (res.ok) {
      toast('Туннель запущен', 'success');
      loadTunnels();
    } else {
      const d = await res.json();
      toast(d.error || 'Ошибка запуска', 'danger');
    }
  } catch {
    toast('Ошибка соединения', 'danger');
  }
}

async function stopTunnel(id) {
  try {
    const res = await api(`api/tunnels/${id}/stop`, { method: 'POST' });
    if (res.ok) {
      toast('Туннель остановлен', 'info');
      loadTunnels();
    } else {
      toast('Ошибка остановки', 'danger');
    }
  } catch {
    toast('Ошибка соединения', 'danger');
  }
}

async function resetStats(id) {
  const tObj = tunnelsData.find(x => x.id === id);
  const name = tObj ? tObj.name : '';
  if (!confirm(t('confirm_reset', { name }))) return;
  try {
    await api(`api/tunnels/${id}/reset-stats`, { method: 'POST' });
    toast(t('toast_reset'), 'info');
    loadTunnels();
  } catch {
    toast(t('toast_error'), 'danger');
  }
}

async function deleteTunnel(id) {
  const tObj = tunnelsData.find(x => x.id === id);
  const name = tObj ? tObj.name : '';
  if (!confirm(t('confirm_delete', { name }))) return;
  try {
    const res = await api(`api/tunnels/${id}`, { method: 'DELETE' });
    if (res.ok) {
      toast(t('toast_deleted'), 'success');
      loadTunnels();
    } else {
      toast(t('toast_error'), 'danger');
    }
  } catch {
    toast(t('toast_error'), 'danger');
  }
}

async function clearTunnelCookies(id) {
  const item = tunnelsData.find(x => (x.id || x.Id) === id);
  const restart = Boolean(item?.isEnabled ?? item?.IsEnabled);
  if (!confirm(restart
    ? cookiesText('Сбросить все сохранённые куки и перезапустить туннель?', 'Reset all saved cookies and restart the tunnel?')
    : cookiesText('Сбросить все сохранённые куки?', 'Reset all saved cookies?'))) return;
  try {
    const res = await api(`api/tunnels/${id}/clear-cookies`, { method: 'POST' });
    if (res.ok) {
      toast(restart ? cookiesText('Куки сброшены, туннель перезапущен', 'Cookies reset, tunnel restarted') : cookiesText('Куки сброшены', 'Cookies reset'), 'success');
      loadTunnels();
    } else {
      toast('Ошибка сброса куки', 'danger');
    }
  } catch {
    toast('Ошибка соединения', 'danger');
  }
}


const COOKIE_TRANSPORTS = new Set(['yandex', 'vyandex', 'boards', 'mailru', 'cupsonline', 'avito', 'script']);
let cookiesDrafts = new Map();
let currentCookiesTransport = '';
let cookiesModalVersion = 0;
function cookiesText(ru, en) { return currentLanguage === 'en' ? en : ru; }

function openCookiesModal(tunnelId) {
	++cookiesModalVersion;
  const item = tunnelsData.find(x => (x.id || x.Id) === tunnelId);
  const modal = document.getElementById('cookies-modal');
  if (!modal || !item) return;
  document.getElementById('cookies-tunnel-id').value = tunnelId;
  document.getElementById('cookies-modal-title').textContent = `${cookiesText('Куки', 'Cookies')}: ${item.name || item.Name || ''}`;
  cookiesDrafts = new Map();
  const multi = (item.transport || item.Transport) === 'multi';
  const types = multi ? String(item.transports || item.Transports || '').split(',').map(x => x.trim().split(':')[0]) : [item.transport || item.Transport];
  const available = [...new Set(types)].filter(x => COOKIE_TRANSPORTS.has(x));
  const select = document.getElementById('cookies-transport-select');
  select.innerHTML = available.map(kind => {
    const name = MULTI_TRANSPORT_TYPES.find(([key]) => key === kind)?.[1] || kind;
    return `<option value="${escapeHtml(kind)}">${escapeHtml(name)}</option>`;
  }).join('');
  const requested = item.challenge?.transport || item.Challenge?.transport;
  select.value = available.includes(requested) ? requested : (available[0] || '');
  currentCookiesTransport = select.value;
  document.getElementById('cookies-transport-group').hidden = !multi;
  document.querySelector('label[for="cookies-transport-select"]').textContent = cookiesText('Транспорт Multi', 'Multi transport');
  const input = document.getElementById('cookies-input-text');
  input.value = '';
  input.disabled = !available.length;
  input.placeholder = available.length ? cookiesText('Вставьте cookies для выбранного транспорта', 'Paste cookies for the selected transport') : cookiesText('Эти транспорты не используют cookies', 'These transports do not use cookies');
  document.getElementById('btn-save-cookies').disabled = !available.length;
  document.getElementById('btn-save-cookies').textContent = Number(item.status ?? item.Status) === 2 ? cookiesText('Применить куки', 'Apply cookies') : cookiesText('Сохранить куки', 'Save cookies');
  document.getElementById('cookies-modal-description').textContent = cookiesText('Куки можно сохранить до запуска туннеля. Поддерживаются Cookie-заголовок, JSON из расширений и Netscape cookies.txt.', 'Cookies can be saved before starting the tunnel. Cookie headers, extension JSON and Netscape cookies.txt are supported.');
  document.getElementById('btn-clear-cookies').textContent = multi ? cookiesText('Сбросить все куки', 'Reset all cookies') : cookiesText('Сбросить куки', 'Reset cookies');
  modal.classList.add('open');
}

function switchCookiesTransport() {
  const input = document.getElementById('cookies-input-text');
  if (currentCookiesTransport) cookiesDrafts.set(currentCookiesTransport, input.value);
  currentCookiesTransport = document.getElementById('cookies-transport-select').value;
  input.value = cookiesDrafts.get(currentCookiesTransport) || '';
}

function closeCookiesModal() {
	++cookiesModalVersion;
  document.getElementById('cookies-modal')?.classList.remove('open');
  document.getElementById('cookies-input-text').value = '';
  cookiesDrafts.clear();
  currentCookiesTransport = '';
}

async function submitCookiesModal() {
  const tunnelId = document.getElementById('cookies-tunnel-id')?.value;
  const target = currentCookiesTransport;
  const version = cookiesModalVersion;
  const raw = document.getElementById('cookies-input-text')?.value.trim();
  if (!tunnelId || !target) return;
  if (!raw) { toast(cookiesText('Вставьте куки перед применением', 'Paste cookies first'), 'warning'); return; }
  const button = document.getElementById('btn-save-cookies');
  button.disabled = true;
  try {
    const response = await api(`api/tunnels/${tunnelId}/cookies?transport=${encodeURIComponent(target)}`, {method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({cookies:raw})});
    if (!response.ok) { const error = await response.json(); throw new Error(error.error || cookiesText('Ошибка отправки куки', 'Failed to send cookies')); }
    const result = await response.json();
    toast(result.mode === 'stored'
      ? `${cookiesText('Куки сохранены для следующего запуска', 'Cookies saved for the next start')}: ${target}`
      : `${cookiesText('Куки отправлены', 'Cookies sent')}: ${target}`, 'success');
    if (version === cookiesModalVersion) {
      if (cookiesDrafts.get(target)?.trim() === raw) cookiesDrafts.delete(target);
      const input = document.getElementById('cookies-input-text');
      if (currentCookiesTransport === target && input.value.trim() === raw) input.value = '';
    }
    // Keep other transports' drafts while the user answers each challenge.
    await loadTunnels();
  } catch (error) { toast(error.message || cookiesText('Ошибка соединения', 'Connection error'), 'danger'); }
  finally { if (version === cookiesModalVersion) button.disabled = false; }
}

async function clearCurrentModalCookies() {
  const tunnelId = document.getElementById('cookies-tunnel-id')?.value;
  if (!tunnelId) return;
  closeCookiesModal();
  await clearTunnelCookies(tunnelId);
}

function generateRandomKey() {
  const bytes = new Uint8Array(32);
  crypto.getRandomValues(bytes);
  return Array.from(bytes).map(b => b.toString(16).padStart(2, '0')).join('');
}

function generateAndSetKey() {
  const key = generateRandomKey();
  document.getElementById('tunnel-encryption').value = key;
  toast(currentLanguage === 'ru' ? 'Сгенерирован новый 32-байтный hex-ключ' : 'Generated new 32-byte hex key', 'info');
}

async function copyShareLink(link, inputEl) {
  if (!link) return;
  const ok = await copyToClipboard(link, inputEl);
  if (ok) {
    toast(t('toast_link_copied') || 'Ссылка openflux:// скопирована в буфер обмена', 'success');
  } else {
    toast(t('toast_copied') || 'Скопировано в буфер обмена', 'success');
  }
}

let currentQrCode = null;
let qrRequestSequence = 0;

async function openQrModal(link, name, tunnelId) {
  const modal = document.getElementById('qr-modal');
  if (!modal || !tunnelId) return;
  const requestId = ++qrRequestSequence;
  if (currentQrCode) URL.revokeObjectURL(currentQrCode);
  currentQrCode = null;
  document.getElementById('qr-modal-title').textContent = name || t('modal_qr_title');
  document.getElementById('qr-modal-link-input').value = link;
  const container = document.getElementById('qrcode-canvas');
  container.textContent = cookiesText('Создание QR…', 'Creating QR…');
  const download = document.getElementById('qr-download-link');
  download.hidden = true;
  download.style.display = 'none';
  download.removeAttribute('href');
  modal.classList.add('open');
  try {
    const response = await api('api/qr', {method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({link})});
    if (!response.ok) throw new Error(cookiesText('Не удалось создать QR. Скопируйте ссылку подключения.', 'Could not create QR. Copy the connection link.'));
    const modules = Number(response.headers.get('X-QR-Modules'));
    const blob = await response.blob();
    const imageSource = await new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(reader.result);
      reader.onerror = () => reject(new Error(cookiesText('Не удалось загрузить QR.', 'Could not load QR.')));
      reader.readAsDataURL(blob);
    });
    if (requestId !== qrRequestSequence) return;
    const url = URL.createObjectURL(blob);
    currentQrCode = url;
    const image = document.createElement('img');
    image.alt = cookiesText('QR подключения OpenFlux', 'OpenFlux connection QR');
    image.style.imageRendering = 'pixelated';
    image.src = imageSource;
    // The full-size PNG keeps integral modules; the compact preview uses pixelated scaling.
    const available = Math.max(1,Math.min(640,modal.querySelector('.modal-body').clientWidth - 48,window.innerHeight - 210));
    const side = Math.max(1,Math.round(modules * Math.max(1,Math.floor(available / modules)) / 1.5));
    image.width = side;
    image.height = side;
    image.style.maxWidth = 'none';
    container.replaceChildren(image);
    download.href = url;
    download.download = `openflux-${tunnelId}.png`;
    download.hidden = false;
    download.style.display = 'inline-flex';
  } catch (error) {
    if (requestId === qrRequestSequence) container.textContent = error.message;
  }
}

function closeQrModal() {
  ++qrRequestSequence;
  document.getElementById('qr-modal')?.classList.remove('open');
  document.getElementById('qrcode-canvas')?.replaceChildren();
  const download = document.getElementById('qr-download-link');
  download?.removeAttribute('href');
  if (download) { download.hidden = true; download.style.display = 'none'; }
  if (currentQrCode) URL.revokeObjectURL(currentQrCode);
  currentQrCode = null;
}

function copyQrModalLink() {
  const input = document.getElementById('qr-modal-link-input');
  if (input && input.value) {
    copyShareLink(input.value, input);
  }
}

function showTunnelConnect(id) {
  const tItem = tunnelsData.find(x => x.id === id);
  if (!tItem) return;
  if (!tItem.shareLink) {
    toast(currentLanguage === 'en'
      ? 'Connection link will be available after starting the tunnel.'
      : 'Ссылка подключения формируется при запуске туннеля.', 'info');
    return;
  }
  openQrModal(tItem.shareLink, tItem.name, id);
}

function selectTransport(transportName) {
  const select = document.getElementById('tunnel-transport');
  if (select) {
    select.value = transportName;
    onTransportChange();
  }
}

function updateTransportGuideBox(transport) {
  // Streamlined UI: verbose instruction box removed
}

function validateTunnelUrlInput() {
  const transport = document.getElementById('tunnel-transport')?.value;
  const urlInput = document.getElementById('tunnel-url');
  const warnBox = document.getElementById('tunnel-url-warning');
  if (!urlInput || !warnBox) return;

  const urlVal = (urlInput.value || '').trim();
  if (!urlVal) {
    warnBox.style.display = 'none';
    warnBox.textContent = '';
    return;
  }

  const lower = urlVal.toLowerCase();
  const isEn = (typeof currentLanguage !== 'undefined' && currentLanguage === 'en');

  if (transport === 'yandex') {
    if (lower.includes('/view') || lower.includes('docview')) {
      warnBox.style.display = 'flex';
      warnBox.innerHTML = `<span>⚠️</span><span>${isEn ? 'Warning! Link appears to be view-only. Make sure you set "Anyone with the link can edit" in Yandex Disk!' : 'Внимание! Ссылка похожа на режим просмотра. Убедитесь, что в Яндекс Диске включено «Редактирование для всех по ссылке»!'}</span>`;
      return;
    }
  }

  warnBox.style.display = 'none';
  warnBox.textContent = '';
}

function openTunnelModal(tunnel = null) {
  const modalBody = document.querySelector('#tunnel-modal .modal-body');
  if (modalBody) modalBody.scrollTop = 0;

  document.getElementById('tunnel-id').value = tunnel ? tunnel.id : '';
  document.getElementById('tunnel-modal-title').textContent = tunnel ? t('modal_edit_tunnel') : t('modal_new_tunnel');
  document.getElementById('tunnel-name').value = tunnel ? tunnel.name : 'Tunnel-' + Math.floor(Math.random() * 1000);
  document.getElementById('tunnel-role').value = 'exit';
  document.getElementById('tunnel-transport').value = tunnel ? tunnel.transport : 'yandex';
  document.getElementById('tunnel-mode').value = tunnel ? (tunnel.mode || 'l4') : 'l4';
  document.getElementById('tunnel-local-ip').value = tunnel ? (tunnel.localIp || '') : '';
  document.getElementById('tunnel-url').value = tunnel ? (tunnel.url || tunnel.avitoUrl || tunnel.yandexUrl || tunnel.mailruUrl || '') : '';
  document.getElementById('tunnel-maxtoken').value = tunnel ? (tunnel.maxToken || '') : '';
  const dlVal = tunnel ? (tunnel.directListen || '') : '';
  document.getElementById('tunnel-direct-listen').value = (dlVal && dlVal.includes(':')) ? dlVal : '0.0.0.0:8445';
  document.getElementById('tunnel-share-host').value = tunnel ? (tunnel.shareHost || '') : '';
  document.getElementById('tunnel-transports').value = tunnel ? (tunnel.transports || '') : '';
  document.getElementById('tunnel-codec').value = tunnel ? (tunnel.codec || 'batched') : 'batched';
  document.getElementById('tunnel-encryption').value = tunnel ? (tunnel.encryptionKey || '') : generateRandomKey();
  document.getElementById('tunnel-enable-share').checked = tunnel ? (tunnel.enableShare !== false) : true;
  document.getElementById('tunnel-session-context').value = tunnel ? (tunnel.sessionContext || '') : '';
  document.getElementById('tunnel-max-packet').value = tunnel && tunnel.maxPacketSize ? tunnel.maxPacketSize : 65000;
  document.getElementById('tunnel-script-path').value = tunnel?.scriptPath || '';
  document.getElementById('tunnel-script-pubkey').value = tunnel?.scriptPublicKey || '';
  document.getElementById('tunnel-script-name').value = tunnel?.scriptName || '';
  document.getElementById('tunnel-script-auto-update').checked = !!tunnel?.scriptAutoUpdate;
  document.querySelectorAll('[name=script-channel]').forEach(input => input.checked = input.value === (tunnel?.scriptUpdateChannel || 'stable'));
  document.getElementById('tunnel-negotiate').checked = tunnel ? !!tunnel.negotiate : false;
  document.getElementById('tunnel-extra-args').value = tunnel ? (tunnel.extraArgs || '') : '';

  const urlInput = document.getElementById('tunnel-url');
  if (urlInput && !urlInput._boundValidate) {
    urlInput.addEventListener('input', validateTunnelUrlInput);
    urlInput._boundValidate = true;
  }

  renderMultiTransportRows(tunnel);
  onTransportChange();
  validateTunnelUrlInput();
  document.getElementById('tunnel-modal').classList.add('open');
}

function closeTunnelModal() {
  document.getElementById('tunnel-modal').classList.remove('open');
}

function editTunnel(id) {
  const t = tunnelsData.find(x => x.id === id);
  if (t) openTunnelModal(t);
}

function updateScriptChannelState() {
  document.getElementById('tunnel-script-channel').disabled = !document.getElementById('tunnel-script-auto-update').checked;
}

function onTransportChange() {
  const transport = document.getElementById('tunnel-transport').value;

  // Sync transport cards active state and guarantee visibility
  document.querySelectorAll('#transport-cards-grid .transport-card').forEach(card => {
    card.style.display = '';
    if (card.getAttribute('data-transport') === transport) {
      card.classList.add('active');
    } else {
      card.classList.remove('active');
    }
  });

  // Render guide box
  updateTransportGuideBox(transport);

  document.getElementById('group-oneme').style.display = transport === 'oneme' ? 'grid' : 'none';
  // The exit listener is shared by a multi session. Keep it visible there;
  // the Direct card itself only describes that it uses this listener.
  const hasMultiDirect = transport === 'multi' && Array.from(document.querySelectorAll('.multi-transport-type')).some(select => select.value === 'direct');
  document.getElementById('group-direct').style.display = (transport === 'direct' || hasMultiDirect) ? 'grid' : 'none';
  document.getElementById('group-multi').style.display = transport === 'multi' ? 'block' : 'none';
  if (transport === 'multi' && !document.querySelector('.multi-transport-row')) {
    renderMultiTransportRows(null);
  }

  const usesScript = transport === 'script' || (transport === 'multi' && Array.from(document.querySelectorAll('.multi-transport-type')).some(select => select.value === 'script'));
  const scriptGroup = document.getElementById('group-script');
  scriptGroup.style.display = usesScript ? 'block' : 'none';
  if (usesScript) scriptGroup.closest('details').open = true;
  updateScriptChannelState();

  const urlGroup = document.getElementById('group-url');
  urlGroup.style.display = (transport === 'oneme' || transport === 'direct') ? 'none' : 'block';

  const urlLabel = document.getElementById('tunnel-url-label');
  const urlInput = document.getElementById('tunnel-url');
  if (transport === 'telemost') {
    urlLabel.textContent = 'Ссылка на встречу Яндекс Телемост (--url)';
    urlInput.placeholder = 'https://telemost.yandex.ru/j/...';
  } else if (transport === 'boards') {
    urlLabel.textContent = 'URL Яндекс Доски (--url)';
    urlInput.placeholder = 'https://boards.yandex.ru/p/...';
  } else if (transport === 'mailru') {
    urlLabel.textContent = 'Публичная ссылка Mail.ru (--url)';
    urlInput.placeholder = 'https://cloud.mail.ru/public/...';
  } else if (transport === 'cupsonline') {
    urlLabel.textContent = 'Список комнат base64 (--url, опционально)';
    urlInput.placeholder = 'Оставьте пустым для автосоздания комнат';
  } else if (transport === 'avito') {
    urlLabel.textContent = 'URL комнаты code.avito.ru (--url)';
    urlInput.placeholder = 'https://code.avito.ru/rooms/...';
  } else if (transport === 'vyandex') {
    urlLabel.textContent = 'URL документа Яндекс Волга (--url)';
    urlInput.placeholder = 'https://disk.yandex.ru/i/...';
  } else if (transport === 'multi') {
    urlLabel.textContent = 'Основной URL контекста (--url, опционально)';
    urlInput.placeholder = 'https://disk.yandex.ru/i/...';
  } else {
    urlLabel.textContent = 'URL документа (--url)';
    urlInput.placeholder = 'https://disk.yandex.ru/i/...';
  }

  validateTunnelUrlInput();
}

const MULTI_TRANSPORT_TYPES = [
  ['script', 'Script / .flux'],
  ['direct', 'Direct TCP'],
  ['avito', 'Avito'],
  ['telemost', 'Яндекс Телемост'],
  ['yandex', 'Yandex Docs'],
  ['vyandex', 'Yandex Volga'],
  ['boards', 'Yandex Boards'],
  ['mailru', 'Mail.ru Docs'],
  ['cupsonline', 'Cups.online'],
  ['oneme', 'MAX / OneMe']
];

function createMultiTransportRow(config = {}) {
  const row = document.createElement('div');
  row.className = 'multi-transport-row';
  const type = String(config.type || 'yandex').toLowerCase();
  row.innerHTML = `
    <div class="multi-transport-row-main">
      <select class="form-control multi-transport-type" aria-label="Тип транспорта">
        ${MULTI_TRANSPORT_TYPES.map(([v, l]) => `<option value="${v}">${l}</option>`).join('')}
      </select>
      <input class="form-control multi-transport-value" type="text" aria-label="Параметр транспорта" />
      <input class="form-control multi-transport-secondary" type="text" placeholder="UID MAX" aria-label="UID MAX" style="display: none;" />
      <div class="multi-transport-priority-wrap" title="Приоритет: чем больше, тем выше">
        <span class="multi-transport-prio-label">Приоритет:</span>
        <input class="form-control multi-transport-priority" type="number" min="0" max="1000" step="1" value="${Number.isFinite(Number(config.priority)) ? Number(config.priority) : 50}" aria-label="Приоритет" />
      </div>
      <button type="button" class="btn btn-outline btn-sm multi-transport-remove" title="Удалить транспорт" aria-label="Удалить">✕</button>
    </div>`;
  const typeSelect = row.querySelector('.multi-transport-type');
  typeSelect.value = MULTI_TRANSPORT_TYPES.some(([v]) => v === type) ? type : 'yandex';
  row.querySelector('.multi-transport-value').value = config.value || '';
  row.querySelector('.multi-transport-secondary').value = config.secondary || '';
  typeSelect.addEventListener('change', () => {
    updateMultiTransportRow(row);
    onTransportChange();
  });
  row.querySelector('.multi-transport-remove').addEventListener('click', () => {
    const rows = document.querySelectorAll('.multi-transport-row');
    if (rows.length > 1) {
      row.remove();
      onTransportChange();
    }
    else toast('Оставьте хотя бы один транспорт', 'warning');
  });
  updateMultiTransportRow(row);
  return row;
}

function updateMultiTransportRow(row) {
  const type = row.querySelector('.multi-transport-type').value;
  const value = row.querySelector('.multi-transport-value');
  const secondary = row.querySelector('.multi-transport-secondary');
  const placeholders = {
    direct: 'Direct (exit слушает порт выше)',
    avito: 'https://code.avito.ru/rooms/...',
    telemost: 'https://telemost.yandex.ru/j/...',
    yandex: 'https://disk.yandex.ru/i/...',
    vyandex: 'https://disk.yandex.ru/i/...',
    boards: 'https://boards.yandex.ru/p/...',
    mailru: 'https://cloud.mail.ru/public/...',
    cupsonline: 'Авто (или ссылка на комнату)',
    oneme: 'Токен MAX'
  };
  value.placeholder = placeholders[type] || 'URL документа';
  if (type === 'direct') {
    value.value = '';
    value.disabled = true;
  } else {
    value.disabled = false;
  }
  secondary.style.display = type === 'oneme' ? 'block' : 'none';
}

function updateMultiTransportRowNumbers() {
  // Numbering omitted in streamlined row view
}

function addMultiTransportRow(config = {}) {
  const container = document.getElementById('multi-transport-rows');
  if (!container) return;
  const selected = new Set(Array.from(container.querySelectorAll('.multi-transport-type')).map(select => select.value));
  const nextType = MULTI_TRANSPORT_TYPES.find(([value]) => !selected.has(value))?.[0] || 'yandex';
  container.appendChild(createMultiTransportRow({ type: nextType, ...config }));
  if (document.getElementById('tunnel-transport')?.value === 'multi') onTransportChange();
}

function renderMultiTransportRows(tunnel) {
  const container = document.getElementById('multi-transport-rows');
  if (!container) return;
  container.innerHTML = '';
  const raw = String(tunnel?.transports || '').trim();
  const specs = raw ? raw.split(',').map(part => {
    const [kind, priorityText] = part.trim().split(':');
    const priority = parseInt(priorityText, 10);
    return { type: kind || 'yandex', priority: Number.isNaN(priority) ? 50 : priority };
  }) : [{ type: 'direct', priority: 100 }, { type: 'yandex', priority: 50 }];
  const values = {
    yandex: tunnel?.yandexUrl || tunnel?.url || '',
    vyandex: tunnel?.vyandexUrl || '', boards: tunnel?.boardsUrl || '',
    mailru: tunnel?.mailruUrl || '', cupsonline: tunnel?.cupsonlineUrl || '',
    avito: tunnel?.avitoUrl || '',
    telemost: tunnel?.telemostUrl || '',
    // DirectTransport is an exit listener in this panel; --direct-dial is a
    // client-only option and must not be copied into the multi editor.
    direct: '', oneme: tunnel?.onemeToken || tunnel?.maxToken || ''
  };
  specs.forEach(spec => addMultiTransportRow({
    ...spec,
    value: values[spec.type] || '',
    secondary: spec.type === 'oneme' ? (tunnel?.onemeUid || tunnel?.maxUid || '') : ''
  }));
  // Do not serialize the default rows while editing a single-transport tunnel.
  // saveTunnel() deliberately reads this field only when multi is selected.
  if (tunnel?.transport === 'multi') syncMultiTransportField();
}

function readMultiTransportRows() {
  return Array.from(document.querySelectorAll('.multi-transport-row')).map(row => ({
    type: row.querySelector('.multi-transport-type').value,
    priority: Math.max(0, Number.isNaN(parseInt(row.querySelector('.multi-transport-priority').value, 10)) ? 50 : parseInt(row.querySelector('.multi-transport-priority').value, 10)),
    value: row.querySelector('.multi-transport-value').value.trim(),
    secondary: row.querySelector('.multi-transport-secondary').value.trim()
  }));
}

function syncMultiTransportField() {
  const rows = readMultiTransportRows();
  const field = document.getElementById('tunnel-transports');
  if (field) field.value = rows.map(row => `${row.type}:${row.priority}`).join(',');
  return rows;
}

async function saveTunnel() {
  const id = document.getElementById('tunnel-id').value;
  const existing = id ? tunnelsData.find(x => x.id === id) : null;
  const selectedTransport = document.getElementById('tunnel-transport').value;
  const multiRows = selectedTransport === 'multi' ? syncMultiTransportField() : [];
  if (selectedTransport === 'multi') {
    const duplicates = multiRows.filter((row, index, rows) => rows.findIndex(other => other.type === row.type) !== index);
    if (duplicates.length) {
      toast('Добавьте каждый тип транспорта только один раз: его параметры хранятся в одной карточке.', 'warning');
      return;
    }
  }
  const multiValue = (type) => multiRows.find(row => row.type === type)?.value || null;
  const multiSecondary = (type) => multiRows.find(row => row.type === type)?.secondary || null;
  const multiHas = (type) => multiRows.some(row => row.type === type);
  const urlInput = document.getElementById('tunnel-url').value.trim();

  // Validate document / conference URL requirements
  if (['yandex', 'mailru', 'vyandex', 'boards', 'telemost', 'avito'].includes(selectedTransport) && !urlInput) {
    toast(currentLanguage === 'en' ? 'Please provide the connection URL' : 'Пожалуйста, укажите ссылку для подключения', 'warning');
    return;
  }

  const savedUrlForTransport = selectedTransport === 'yandex' ? existing?.yandexUrl
    : selectedTransport === 'vyandex' ? existing?.vyandexUrl
      : selectedTransport === 'telemost' ? existing?.telemostUrl
        : selectedTransport === 'boards' ? existing?.boardsUrl
          : selectedTransport === 'mailru' ? existing?.mailruUrl
            : selectedTransport === 'avito' ? existing?.avitoUrl
              : selectedTransport === 'cupsonline' ? existing?.cupsonlineUrl : null;
  const payload = {
    name: document.getElementById('tunnel-name').value.trim(),
    role: 'exit',
    transport: selectedTransport,
    mode: document.getElementById('tunnel-mode').value,
    localIp: document.getElementById('tunnel-local-ip').value.trim() || null,
    inbound: 'socks5',
    socks5Address: ':1080',
    // When switching from multi to a single carrier, carry its card URL into
    // the legacy --url field so the single session remains runnable.
    url: urlInput || savedUrlForTransport || null,
    maxToken: selectedTransport === 'multi' ? multiValue('oneme') : (document.getElementById('tunnel-maxtoken').value.trim() || null),
    maxUid: selectedTransport === 'multi' ? multiSecondary('oneme') : (document.getElementById('tunnel-maxuid').value.trim() || null),
    directListen: (() => {
      let dl = selectedTransport === 'multi'
        ? (multiHas('direct') ? (document.getElementById('tunnel-direct-listen').value.trim() || null) : null)
        : (document.getElementById('tunnel-direct-listen').value.trim() || null);
      if (dl && !dl.includes(':')) {
        dl = /^\d+$/.test(dl) ? `0.0.0.0:${dl}` : `${dl}:8445`;
      }
      return dl;
    })(),
    directDial: selectedTransport === 'multi' ? multiValue('direct') : (existing ? (existing.directDial || null) : null),
    shareHost: document.getElementById('tunnel-share-host').value.trim() || null,
    // A non-multi tunnel must never inherit the hidden field from the default
    // multi cards. That field used to turn every single save into a multi run.
    transports: selectedTransport === 'multi' ? (document.getElementById('tunnel-transports').value.trim() || null) : null,
    yandexUrl: selectedTransport === 'multi' ? multiValue('yandex') : (selectedTransport === 'yandex' ? urlInput : (existing?.yandexUrl || null)),
    vyandexUrl: selectedTransport === 'multi' ? multiValue('vyandex') : (selectedTransport === 'vyandex' ? urlInput : (existing?.vyandexUrl || null)),
    telemostUrl: selectedTransport === 'multi' ? multiValue('telemost') : (selectedTransport === 'telemost' ? urlInput : (existing?.telemostUrl || null)),
    boardsUrl: selectedTransport === 'multi' ? multiValue('boards') : (selectedTransport === 'boards' ? urlInput : (existing?.boardsUrl || null)),
    mailruUrl: selectedTransport === 'multi' ? multiValue('mailru') : (selectedTransport === 'mailru' ? urlInput : (existing?.mailruUrl || null)),
    cupsonlineUrl: selectedTransport === 'multi' ? multiValue('cupsonline') : (selectedTransport === 'cupsonline' ? urlInput : (existing?.cupsonlineUrl || null)),
    avitoUrl: selectedTransport === 'multi' ? multiValue('avito') : (selectedTransport === 'avito' ? urlInput : (existing?.avitoUrl || null)),
    onemeToken: selectedTransport === 'multi' ? multiValue('oneme') : (existing?.onemeToken || null),
    onemeUid: selectedTransport === 'multi' ? (multiSecondary('oneme') || null) : (existing?.onemeUid || null),
    codec: document.getElementById('tunnel-codec').value,
    encryptionKey: document.getElementById('tunnel-encryption').value.trim() || null,
    enableShare: document.getElementById('tunnel-enable-share').checked,
    sessionContext: document.getElementById('tunnel-session-context').value.trim() || null,
    maxPacketSize: parseInt(document.getElementById('tunnel-max-packet').value) || 65000,
    negotiate: (selectedTransport === 'direct' || (selectedTransport === 'multi' && multiHas('direct'))) ? true : document.getElementById('tunnel-negotiate').checked,
    clientLimit: 0,
    trafficLimitBytes: 0,
    scriptPath: document.getElementById('tunnel-script-path').value.trim() || null,
    scriptPublicKey: document.getElementById('tunnel-script-pubkey').value.trim() || null,
    scriptName: document.getElementById('tunnel-script-name').value.trim() || null,
    scriptAutoUpdate: (selectedTransport === 'script' || multiHas('script')) && document.getElementById('tunnel-script-auto-update').checked,
    scriptUpdateChannel: document.querySelector('[name=script-channel]:checked')?.value || 'stable',
    extraArgs: document.getElementById('tunnel-extra-args').value.trim() || null,
    isEnabled: existing ? existing.isEnabled : false
  };

  try {
    let res;
    if (id) {
      payload.id = id;
      res = await api(`api/tunnels/${id}`, { method: 'PUT', body: JSON.stringify(payload) });
    } else {
      res = await api('api/tunnels', { method: 'POST', body: JSON.stringify(payload) });
    }

    if (res.ok) {
      toast('Настройки туннеля сохранены', 'success');
      closeTunnelModal();
      await loadTunnels();
    } else {
      let errDetails = '';
      try {
        const errJson = await res.json();
        if (errJson && errJson.error) {
          errDetails = ': ' + errJson.error;
        } else if (errJson && errJson.message) {
          errDetails = ': ' + errJson.message;
        }
      } catch {
        try {
          const txt = await res.text();
          if (txt) errDetails = ': ' + txt.slice(0, 100);
        } catch {}
      }
      toast('Ошибка сохранения туннеля' + errDetails, 'danger');
    }
  } catch (err) {
    toast('Ошибка соединения: ' + (err.message || err), 'danger');
  }
}

// Tunnel error recommendations modal completely disabled per user request
function openTunnelErrorModal() { return false; }
function closeTunnelErrorModal() { return false; }
function onEditFromErrorModal() { return false; }
function onSolveCaptchaFromErrorModal() { return false; }

// Captcha solver modal removed
function openCaptchaSolverModal() { return false; }
function closeCaptchaSolverModal() { return false; }

