// OpenFlux Zen Server - Tunnels Management

async function loadTunnels(silent = false) {
  try {
    const res = await api('api/tunnels');
    if (!res.ok) return;
    tunnelsData = await res.json();

    if (silent && tunnelsData.length > 0) {
      let canUpdateInPlace = true;
      for (const t of tunnelsData) {
        if (!document.getElementById('tunnel-card-' + t.id)) {
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
    if (!silent) console.error('Failed to load tunnels:', err);
  }
}

function updateTunnelsInPlace(list) {
  for (const t of list) {
    const clientLimitStr = t.clientLimit > 0 ? `${t.connectedClients || 0} / ${t.clientLimit}` : `${t.connectedClients || 0} (∞)`;
    const upRateStr = t.uploadRateBytesPerSec > 0 ? ` <span class="rate-badge">↑ ${fmtSpeed(t.uploadRateBytesPerSec)}</span>` : '';
    const downRateStr = t.downloadRateBytesPerSec > 0 ? ` <span class="rate-badge">↓ ${fmtSpeed(t.downloadRateBytesPerSec)}</span>` : '';

    const clientsEl = document.getElementById('tunnel-clients-' + t.id);
    if (clientsEl && clientsEl.textContent !== clientLimitStr) {
      clientsEl.textContent = clientLimitStr;
    }

    const uploadEl = document.getElementById('tunnel-upload-' + t.id);
    if (uploadEl) {
      uploadEl.innerHTML = `<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M12 19V5M5 12l7-7 7 7"/></svg>${fmtBytes(t.uploadBytes)}${upRateStr}`;
    }

    const downloadEl = document.getElementById('tunnel-download-' + t.id);
    if (downloadEl) {
      downloadEl.innerHTML = `<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M12 5v14M19 12l-7 7-7-7"/></svg>${fmtBytes(t.downloadBytes)}${downRateStr}`;
    }

    const statusEl = document.getElementById('tunnel-status-' + t.id);
    if (statusEl) {
      let badgeHtml = `<span class="badge badge-status badge-stopped">${t('badge_stopped')}</span>`;
      if (t.status === 2) {
        if (t.errorMessage) {
          badgeHtml = `<span class="badge badge-status badge-failed" title="${escapeHtml(t.errorMessage)}"><span class="pulse" style="background:#ef4444;"></span>${t('badge_conn_error')}</span>`;
        } else {
          badgeHtml = `<span class="badge badge-status badge-running"><span class="pulse"></span>${t('badge_running')}</span>`;
        }
      } else if (t.status === 1) {
        badgeHtml = `<span class="badge badge-status badge-starting">${t('badge_starting')}</span>`;
      } else if (t.status === 4) {
        badgeHtml = `<span class="badge badge-status badge-failed" title="${escapeHtml(t.errorMessage || '')}">${t('badge_failed')}</span>`;
      }
      if (statusEl.innerHTML !== badgeHtml) {
        statusEl.innerHTML = badgeHtml;
      }
    }

    const errEl = document.getElementById('tunnel-err-' + t.id);
    if (errEl) {
      if (t.errorMessage) {
        errEl.innerHTML = `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="flex-shrink:0"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg><span>${escapeHtml(t.errorMessage)}</span>`;
        errEl.style.display = 'flex';
      } else {
        errEl.style.display = 'none';
        errEl.innerHTML = '';
      }
    }

    if (t.trafficLimitBytes > 0) {
      const totalBytes = (t.uploadBytes || 0) + (t.downloadBytes || 0);
      const trafficPct = Math.min(100, Math.round(totalBytes / t.trafficLimitBytes * 100));
      const progEl = document.getElementById('tunnel-prog-' + t.id);
      if (progEl) progEl.style.width = trafficPct + '%';
    }
  }
}

function renderTunnels(list) {
  const container = document.getElementById('tunnel-list');
  if (list.length === 0) {
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
    let statusBadge = `<span class="badge badge-status badge-stopped">${t('badge_stopped')}</span>`;
    if (tItem.status === 2) {
      if (tItem.errorMessage) {
        statusBadge = `<span class="badge badge-status badge-failed" title="${escapeHtml(tItem.errorMessage)}"><span class="pulse" style="background:#ef4444;"></span>${t('badge_conn_error')}</span>`;
      } else {
        statusBadge = `<span class="badge badge-status badge-running"><span class="pulse"></span>${t('badge_running')}</span>`;
      }
    } else if (tItem.status === 1) {
      statusBadge = `<span class="badge badge-status badge-starting">${t('badge_starting')}</span>`;
    } else if (tItem.status === 4) {
      statusBadge = `<span class="badge badge-status badge-failed" title="${escapeHtml(tItem.errorMessage || '')}">${t('badge_failed')}</span>`;
    }

    const isRunning = tItem.status === 2;
    const totalBytes = (tItem.uploadBytes || 0) + (tItem.downloadBytes || 0);
    let trafficLimitStr = currentLanguage === 'en' ? 'Unlimited' : 'Без лимита';
    let trafficPct = 0;
    if (tItem.trafficLimitBytes > 0) {
      trafficLimitStr = fmtBytes(tItem.trafficLimitBytes);
      trafficPct = Math.min(100, Math.round(totalBytes / tItem.trafficLimitBytes * 100));
    }

    const clientLimitStr = tItem.clientLimit > 0 ? `${tItem.connectedClients || 0} / ${tItem.clientLimit}` : `${tItem.connectedClients || 0} (∞)`;
    const upRateStr = tItem.uploadRateBytesPerSec > 0 ? ` <span class="rate-badge">↑ ${fmtSpeed(tItem.uploadRateBytesPerSec)}</span>` : '';
    const downRateStr = tItem.downloadRateBytesPerSec > 0 ? ` <span class="rate-badge">↓ ${fmtSpeed(tItem.downloadRateBytesPerSec)}</span>` : '';

    return `
      <div class="tunnel-item" id="tunnel-card-${tItem.id}">
        <div class="tunnel-top">
          <div class="tunnel-title-group">
            <div class="tunnel-name">${escapeHtml(tItem.name)}</div>
            <span id="tunnel-status-${tItem.id}">${statusBadge}</span>
            <div class="badges">
              <span class="badge badge-tag">${tItem.transport}</span>
              ${tItem.transports ? `<span class="badge badge-tag" title="${escapeHtml(tItem.transports)}">${escapeHtml(tItem.transports)}</span>` : ''}
              <span class="badge badge-tag">${tItem.mode || 'l4'}</span>
              <span class="badge badge-tag">${tItem.codec}</span>
              ${tItem.shareLink ? `<span class="badge badge-tag" style="background: rgba(34, 197, 94, 0.15); color: #4ade80; border-color: rgba(34, 197, 94, 0.3);">${t('badge_share_ready') || 'Ссылка готова'}</span>` : ''}
            </div>
          </div>
        </div>

        ${tItem.shareLink ? `
        <div class="tunnel-share-banner">
          <div class="tunnel-share-info">
            <span class="tunnel-share-tag">openflux://</span>
            <span class="tunnel-share-text" title="${escapeHtml(tItem.shareLink)}">${escapeHtml(tItem.shareLink)}</span>
          </div>
          <div class="tunnel-share-actions">
            <button type="button" class="btn btn-outline btn-xs" onclick="copyShareLink('${escapeHtml(tItem.shareLink)}')" title="${t('btn_copy_openflux')}">
              <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="9" y="9" width="13" height="13" rx="2" ry="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/></svg>
              <span>${t('btn_copy') || 'Скопировать'}</span>
            </button>
            <button type="button" class="btn btn-primary btn-xs" onclick="openQrModal('${escapeHtml(tItem.shareLink)}', '${escapeHtml(tItem.name)}')" title="${t('btn_show_qr')}">
              <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="7" height="7"/><rect x="14" y="3" width="7" height="7"/><rect x="14" y="14" width="7" height="7"/><rect x="3" y="14" width="7" height="7"/></svg>
              <span>${t('btn_show_qr') || 'QR-код'}</span>
            </button>
          </div>
        </div>
        ` : ''}

        <div id="tunnel-err-${tItem.id}" class="tunnel-error-banner" style="margin: 8px 0 12px 0; padding: 7px 10px; background: rgba(239, 68, 68, 0.12); border-left: 3px solid #ef4444; border-radius: 4px; font-size: 0.82rem; color: #fca5a5; display: ${tItem.errorMessage ? 'flex' : 'none'}; align-items: center; gap: 8px;">
          ${tItem.errorMessage ? `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="flex-shrink:0"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg><span>${escapeHtml(tItem.errorMessage)}</span>` : ''}
        </div>

        <div class="tunnel-details">
          <div class="detail-item">
            <span class="detail-label">${t('detail_clients')}</span>
            <span class="detail-val" id="tunnel-clients-${tItem.id}">${clientLimitStr}</span>
          </div>
          <div class="detail-item">
            <span class="detail-label">${t('detail_upload')}</span>
            <span class="detail-val" id="tunnel-upload-${tItem.id}" style="display: inline-flex; align-items: center; gap: 4px;">
              <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M12 19V5M5 12l7-7 7 7"/></svg>
              ${fmtBytes(tItem.uploadBytes)}${upRateStr}
            </span>
          </div>
          <div class="detail-item">
            <span class="detail-label">${t('detail_download')}</span>
            <span class="detail-val" id="tunnel-download-${tItem.id}" style="display: inline-flex; align-items: center; gap: 4px;">
              <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M12 5v14M19 12l-7 7-7-7"/></svg>
              ${fmtBytes(tItem.downloadBytes)}${downRateStr}
            </span>
          </div>
          <div class="detail-item">
            <span class="detail-label">${t('detail_traffic')}</span>
            <span class="detail-val">${trafficLimitStr}</span>
            ${tItem.trafficLimitBytes > 0 ? `<div class="progress-bar-bg"><div class="progress-bar-fill" id="tunnel-prog-${tItem.id}" style="width: ${trafficPct}%"></div></div>` : ''}
          </div>
        </div>

        <div class="tunnel-actions">
          <div class="action-group">
            ${isRunning ? 
              `<button class="btn btn-danger btn-sm" onclick="stopTunnel('${tItem.id}')"><svg width="11" height="11" viewBox="0 0 24 24" fill="currentColor"><rect x="4" y="4" width="16" height="16" rx="2"/></svg>${t('btn_stop')}</button>` :
              `<button class="btn btn-success btn-sm" onclick="startTunnel('${tItem.id}')"><svg width="11" height="11" viewBox="0 0 24 24" fill="currentColor"><polygon points="6 3 20 12 6 21 6 3"/></svg>${t('btn_start')}</button>`}
            <button class="btn btn-outline btn-sm" onclick="viewTunnelLogs('${tItem.id}')"><svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/></svg>${t('btn_logs')}</button>
            <button class="btn btn-outline btn-sm" onclick="resetStats('${tItem.id}')"><svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="1 4 1 10 7 10"/><path d="M3.51 15a9 9 0 1 0 2.13-9.36L1 10"/></svg>${t('btn_reset')}</button>
          </div>

          <div class="action-group">
            <button class="btn btn-outline btn-sm" onclick="editTunnel('${tItem.id}')"><svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/></svg>${t('btn_edit')}</button>
            <button class="btn btn-outline btn-sm" style="color: #ef4444;" onclick="deleteTunnel('${tItem.id}')"><svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/><line x1="10" y1="11" x2="10" y2="17"/><line x1="14" y1="11" x2="14" y2="17"/></svg>${t('btn_delete')}</button>
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

function copyShareLink(link) {
  if (!link) return;
  navigator.clipboard.writeText(link).then(() => {
    toast(t('toast_link_copied') || 'Ссылка openflux:// скопирована в буфер обмена', 'success');
  }).catch(() => {
    toast(t('toast_copied'), 'success');
  });
}

let currentQrCode = null;

function openQrModal(link, name) {
  const modal = document.getElementById('qr-modal');
  if (!modal) return;
  const titleEl = document.getElementById('qr-modal-title');
  if (titleEl) {
    titleEl.textContent = (name ? `${name} - ` : '') + (t('modal_qr_title') || 'Подключение клиента OpenFlux');
  }
  const inputEl = document.getElementById('qr-modal-link-input');
  if (inputEl) inputEl.value = link;

  const canvasContainer = document.getElementById('qrcode-canvas');
  if (canvasContainer) {
    canvasContainer.innerHTML = '';
    try {
      if (typeof QRCode !== 'undefined') {
        currentQrCode = new QRCode(canvasContainer, {
          text: link,
          width: 220,
          height: 220,
          colorDark: '#000000',
          colorLight: '#ffffff',
          correctLevel: QRCode.CorrectLevel.M
        });
      } else {
        canvasContainer.innerHTML = `<p style="color:#ef4444;font-size:0.8rem;">Библиотека QR не загружена</p>`;
      }
    } catch (err) {
      console.error('Failed to generate QR code', err);
      canvasContainer.innerHTML = `<p style="color:#ef4444;font-size:0.8rem;">Ошибка создания QR-кода</p>`;
    }
  }

  modal.classList.add('open');
}

function closeQrModal() {
  const modal = document.getElementById('qr-modal');
  if (modal) modal.classList.remove('open');
}

function copyQrModalLink() {
  const input = document.getElementById('qr-modal-link-input');
  if (input && input.value) {
    copyShareLink(input.value);
  }
}

function openTunnelModal(tunnel = null) {
  document.getElementById('tunnel-id').value = tunnel ? tunnel.id : '';
  document.getElementById('tunnel-modal-title').textContent = tunnel ? t('modal_edit_tunnel') : t('modal_new_tunnel');
  document.getElementById('tunnel-name').value = tunnel ? tunnel.name : 'Tunnel-' + Math.floor(Math.random() * 1000);
  document.getElementById('tunnel-role').value = 'exit';
  document.getElementById('tunnel-transport').value = tunnel ? tunnel.transport : 'yandex';
  document.getElementById('tunnel-mode').value = tunnel ? (tunnel.mode || 'l4') : 'l4';
  document.getElementById('tunnel-local-ip').value = tunnel ? (tunnel.localIp || '') : '';
  document.getElementById('tunnel-url').value = tunnel ? (tunnel.url || '') : '';
  document.getElementById('tunnel-maxtoken').value = tunnel ? (tunnel.maxToken || '') : '';
  document.getElementById('tunnel-maxuid').value = tunnel ? (tunnel.maxUid || '') : '';
  document.getElementById('tunnel-direct-listen').value = tunnel ? (tunnel.directListen || '') : '0.0.0.0:8445';
  document.getElementById('tunnel-share-host').value = tunnel ? (tunnel.shareHost || '') : '';
  document.getElementById('tunnel-transports').value = tunnel ? (tunnel.transports || '') : '';
  document.getElementById('tunnel-codec').value = tunnel ? (tunnel.codec || 'batched') : 'batched';
  document.getElementById('tunnel-encryption').value = tunnel ? (tunnel.encryptionKey || '') : generateRandomKey();
  document.getElementById('tunnel-enable-share').checked = tunnel ? (tunnel.enableShare !== false) : true;
  document.getElementById('tunnel-session-context').value = tunnel ? (tunnel.sessionContext || '') : '';
  document.getElementById('tunnel-max-packet').value = tunnel && tunnel.maxPacketSize ? tunnel.maxPacketSize : 65000;
  document.getElementById('tunnel-negotiate').checked = tunnel ? !!tunnel.negotiate : false;
  document.getElementById('tunnel-client-limit').value = tunnel ? tunnel.clientLimit : 0;
  document.getElementById('tunnel-traffic-limit').value = tunnel && tunnel.trafficLimitBytes > 0 ? Math.round(tunnel.trafficLimitBytes / (1024 * 1024)) : 0;
  document.getElementById('tunnel-extra-args').value = tunnel ? (tunnel.extraArgs || '--debug') : '--debug';

  onTransportChange();
  document.getElementById('tunnel-modal').classList.add('open');
}

function closeTunnelModal() {
  document.getElementById('tunnel-modal').classList.remove('open');
}

function editTunnel(id) {
  const t = tunnelsData.find(x => x.id === id);
  if (t) openTunnelModal(t);
}

function onTransportChange() {
  const transport = document.getElementById('tunnel-transport').value;
  document.getElementById('group-oneme').style.display = transport === 'oneme' ? 'grid' : 'none';
  document.getElementById('group-direct').style.display = transport === 'direct' ? 'grid' : 'none';
  document.getElementById('group-multi').style.display = transport === 'multi' ? 'block' : 'none';

  const urlGroup = document.getElementById('group-url');
  urlGroup.style.display = (transport === 'oneme' || transport === 'direct') ? 'none' : 'block';

  const urlLabel = document.getElementById('tunnel-url-label');
  const urlInput = document.getElementById('tunnel-url');
  if (transport === 'boards') {
    urlLabel.textContent = 'URL Яндекс Доски (--url)';
    urlInput.placeholder = 'https://boards.yandex.ru/p/...';
  } else if (transport === 'mailru') {
    urlLabel.textContent = 'Публичная ссылка Mail.ru (--url)';
    urlInput.placeholder = 'https://cloud.mail.ru/public/...';
  } else if (transport === 'cupsonline') {
    urlLabel.textContent = 'Список комнат base64 (--url, опционально)';
    urlInput.placeholder = 'Оставьте пустым для автосоздания комнат';
  } else if (transport === 'vyandex') {
    urlLabel.textContent = 'URL документа Яндекс Волга (--url)';
    urlInput.placeholder = 'https://disk.yandex.ru/i/...';
  } else if (transport === 'multi') {
    urlLabel.textContent = 'Основной URL документа (опционально)';
    urlInput.placeholder = 'https://disk.yandex.ru/i/...';
  } else {
    urlLabel.textContent = 'URL документа (--url)';
    urlInput.placeholder = 'https://disk.yandex.ru/i/...';
  }
}

async function saveTunnel() {
  const id = document.getElementById('tunnel-id').value;
  const existing = id ? tunnelsData.find(x => x.id === id) : null;
  const trafficMB = parseInt(document.getElementById('tunnel-traffic-limit').value) || 0;
  const payload = {
    name: document.getElementById('tunnel-name').value.trim(),
    role: 'exit',
    transport: document.getElementById('tunnel-transport').value,
    mode: document.getElementById('tunnel-mode').value,
    localIp: document.getElementById('tunnel-local-ip').value.trim() || null,
    inbound: 'socks5',
    socks5Address: ':1080',
    url: document.getElementById('tunnel-url').value.trim() || null,
    maxToken: document.getElementById('tunnel-maxtoken').value.trim() || null,
    maxUid: document.getElementById('tunnel-maxuid').value.trim() || null,
    directListen: document.getElementById('tunnel-direct-listen').value.trim() || null,
    shareHost: document.getElementById('tunnel-share-host').value.trim() || null,
    transports: document.getElementById('tunnel-transports').value.trim() || null,
    codec: document.getElementById('tunnel-codec').value,
    encryptionKey: document.getElementById('tunnel-encryption').value.trim() || null,
    enableShare: document.getElementById('tunnel-enable-share').checked,
    sessionContext: document.getElementById('tunnel-session-context').value.trim() || null,
    maxPacketSize: parseInt(document.getElementById('tunnel-max-packet').value) || 65000,
    negotiate: document.getElementById('tunnel-negotiate').checked,
    clientLimit: parseInt(document.getElementById('tunnel-client-limit').value) || 0,
    trafficLimitBytes: trafficMB > 0 ? trafficMB * 1024 * 1024 : 0,
    extraArgs: document.getElementById('tunnel-extra-args').value.trim() || '--debug',
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
      toast('Ошибка сохранения туннеля', 'danger');
    }
  } catch {
    toast('Ошибка соединения', 'danger');
  }
}
