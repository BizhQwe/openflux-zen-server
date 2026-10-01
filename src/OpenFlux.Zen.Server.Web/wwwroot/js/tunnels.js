// OpenFlux Zen Server - Tunnels Management

async function loadTunnels(silent = false) {
  try {
    const res = await api('api/tunnels');
    if (!res.ok) return;
    tunnelsData = await res.json();

    if (silent && tunnelsData.length > 0) {
      let canUpdateInPlace = true;
      for (const item of tunnelsData) {
        if (!document.getElementById('tunnel-card-' + item.id)) {
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
  for (const tItem of list) {
    const clientLimitStr = tItem.clientLimit > 0 ? `${tItem.connectedClients || 0} / ${tItem.clientLimit}` : `${tItem.connectedClients || 0} (∞)`;
    const upRateStr = tItem.uploadRateBytesPerSec > 0 ? ` <span class="rate-badge">↑ ${fmtSpeed(tItem.uploadRateBytesPerSec)}</span>` : '';
    const downRateStr = tItem.downloadRateBytesPerSec > 0 ? ` <span class="rate-badge">↓ ${fmtSpeed(tItem.downloadRateBytesPerSec)}</span>` : '';

    const clientsEl = document.getElementById('tunnel-clients-' + tItem.id);
    if (clientsEl && clientsEl.textContent !== clientLimitStr) {
      clientsEl.textContent = clientLimitStr;
    }

    const uploadEl = document.getElementById('tunnel-upload-' + tItem.id);
    if (uploadEl) {
      uploadEl.innerHTML = `<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M12 19V5M5 12l7-7 7 7"/></svg>${fmtBytes(tItem.uploadBytes)}${upRateStr}`;
    }

    const downloadEl = document.getElementById('tunnel-download-' + tItem.id);
    if (downloadEl) {
      downloadEl.innerHTML = `<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M12 5v14M19 12l-7 7-7-7"/></svg>${fmtBytes(tItem.downloadBytes)}${downRateStr}`;
    }


    const startStopEl = document.getElementById('tunnel-startstop-' + tItem.id);
    if (startStopEl) {
      const isRunning = tItem.status === 2;
      const expectedBtnHtml = isRunning
        ? `<button class="btn btn-danger btn-sm" onclick="stopTunnel('${tItem.id}')"><svg width="11" height="11" viewBox="0 0 24 24" fill="currentColor"><rect x="4" y="4" width="16" height="16" rx="2"/></svg>${t('btn_stop')}</button>`
        : `<button class="btn btn-success btn-sm" onclick="startTunnel('${tItem.id}')"><svg width="11" height="11" viewBox="0 0 24 24" fill="currentColor"><polygon points="6 3 20 12 6 21 6 3"/></svg>${t('btn_start')}</button>`;
      if (startStopEl.innerHTML !== expectedBtnHtml) {
        startStopEl.innerHTML = expectedBtnHtml;
      }
    }

    const statusEl = document.getElementById('tunnel-status-' + tItem.id);
    if (statusEl) {
      let badgeHtml = `<span class="badge badge-status badge-stopped">${t('badge_stopped')}</span>`;
      if (tItem.errorMessage) {
        badgeHtml = `<span class="badge badge-status badge-failed" title="${escapeHtml(tItem.errorMessage)}">${t('badge_failed')}</span>`;
      } else if (tItem.status === 2) {
        badgeHtml = `<span class="badge badge-status badge-running"><span class="pulse"></span>${t('badge_running')}</span>`;
      } else if (tItem.status === 1) {
        badgeHtml = `<span class="badge badge-status badge-starting">${t('badge_starting')}</span>`;
      } else if (tItem.status === 4) {
        badgeHtml = `<span class="badge badge-status badge-failed" title="${escapeHtml(tItem.errorMessage || '')}">${t('badge_failed')}</span>`;
      }
      if (statusEl.innerHTML !== badgeHtml) {
        statusEl.innerHTML = badgeHtml;
      }
    }

    const warningEl = document.getElementById('tunnel-warning-' + tItem.id);
    if (warningEl) {
      // The status badge is the single source of truth for the error state.
      if (warningEl.innerHTML) warningEl.innerHTML = '';
    }

    const captchaActionEl = document.getElementById('tunnel-captcha-action-' + tItem.id);
    if (captchaActionEl) {
      const needBrowserCheck = isBrowserCheckRequired(tItem);
      const expectedCaptchaHtml = needBrowserCheck ? renderCaptchaActionBtn(tItem.id, tItem.pendingCaptchaReason) : '';
      if (captchaActionEl.innerHTML !== expectedCaptchaHtml) {
        captchaActionEl.innerHTML = expectedCaptchaHtml;
      }
    }

    if (tItem.trafficLimitBytes > 0) {
      const totalBytes = (tItem.uploadBytes || 0) + (tItem.downloadBytes || 0);
      const trafficPct = Math.min(100, Math.round(totalBytes / tItem.trafficLimitBytes * 100));
      const progEl = document.getElementById('tunnel-prog-' + tItem.id);
      if (progEl) progEl.style.width = trafficPct + '%';
    }
  }
}

function isBrowserCheckRequired(tItem) {
  if (!tItem) return false;
  // The core uses the same IPC message for captcha, login and generic cookie
  // requests. Show the action only while a live challenge URL exists and its
  // reason explicitly identifies a human check. Error text is never a signal:
  // old panel versions reused it for ordinary document/network failures.
  if (!tItem.pendingCaptchaUrl) return false;
  const reason = String(tItem.pendingCaptchaReason || '').toLowerCase();
  return reason.includes('captcha') || reason.includes('smartcaptcha') ||
    reason.includes('showcaptcha') || reason.includes('anti-bot') ||
    reason.includes('antibot') || reason.includes('human') ||
    reason.includes('login') || reason.includes('auth') ||
    reason.includes('account') || reason.includes('private');
}

function isCaptchaRequired(tItem) {
  if (!isBrowserCheckRequired(tItem)) return false;
  const reason = String(tItem.pendingCaptchaReason || '').toLowerCase();
  return reason.includes('captcha') || reason.includes('smartcaptcha') ||
    reason.includes('showcaptcha') || reason.includes('anti-bot') ||
    reason.includes('antibot') || reason.includes('human');
}

function renderCaptchaActionBtn(tunnelId, reason = '') {
  const normalized = String(reason || '').toLowerCase();
  const isLogin = normalized.includes('login') || normalized.includes('auth') ||
    normalized.includes('account') || normalized.includes('private');
  const label = isLogin
    ? (currentLanguage === 'en' ? 'Open sign-in' : 'Открыть вход')
    : (t('btn_solve_captcha') || 'Пройти капчу');
  return `<button class="btn btn-warning btn-sm" onclick="openCaptchaSolverModal('${tunnelId}')" title="${escapeHtml(label)}" style="display: inline-flex; align-items: center; gap: 5px; font-weight: 600;">
    <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
      <rect x="3" y="11" width="18" height="11" rx="2" ry="2"/>
      <path d="M7 11V7a5 5 0 0 1 10 0v4"/>
    </svg>
    <span>${escapeHtml(label)}</span>
  </button>`;
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
    if (tItem.errorMessage) {
      statusBadge = `<span class="badge badge-status badge-failed" title="${escapeHtml(tItem.errorMessage)}">${t('badge_failed')}</span>`;
    } else if (tItem.status === 2) {
      statusBadge = `<span class="badge badge-status badge-running"><span class="pulse"></span>${t('badge_running')}</span>`;
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
            </div>
          </div>
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
            <span id="tunnel-startstop-${tItem.id}">
              ${isRunning ? 
                `<button class="btn btn-danger btn-sm" onclick="stopTunnel('${tItem.id}')"><svg width="11" height="11" viewBox="0 0 24 24" fill="currentColor"><rect x="4" y="4" width="16" height="16" rx="2"/></svg>${t('btn_stop')}</button>` :
                `<button class="btn btn-success btn-sm" onclick="startTunnel('${tItem.id}')"><svg width="11" height="11" viewBox="0 0 24 24" fill="currentColor"><polygon points="6 3 20 12 6 21 6 3"/></svg>${t('btn_start')}</button>`}
            </span>
            <span id="tunnel-captcha-action-${tItem.id}">
              ${isBrowserCheckRequired(tItem) ? renderCaptchaActionBtn(tItem.id, tItem.pendingCaptchaReason) : ''}
            </span>
            <button class="btn btn-outline btn-sm" onclick="showTunnelConnect('${tItem.id}')" title="${t('btn_connect_qr') || 'Подключение'}">
              <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <rect x="3" y="3" width="7" height="7"/><rect x="14" y="3" width="7" height="7"/><rect x="14" y="14" width="7" height="7"/><rect x="3" y="14" width="7" height="7"/>
              </svg>
              <span>${t('btn_connect_qr') || 'Подключение'}</span>
            </button>
          </div>

          <div class="action-group">
            <button class="btn btn-outline btn-sm" onclick="viewTunnelLogs('${tItem.id}')"><svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/></svg>${t('btn_logs')}</button>
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
    titleEl.textContent = name || t('modal_qr_title') || 'Подключение клиента OpenFlux';
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

function showTunnelConnect(id) {
  const tItem = tunnelsData.find(x => x.id === id);
  if (!tItem) return;
  if (!tItem.shareLink) {
    toast(currentLanguage === 'en'
      ? 'Connection link will be available after starting the tunnel.'
      : 'Ссылка подключения формируется при запуске туннеля.', 'info');
    return;
  }
  openQrModal(tItem.shareLink, tItem.name);
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

  renderMultiTransportRows(tunnel);
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
  // The exit listener is shared by a multi session.  Keep it visible there;
  // the Direct card itself only describes that it uses this listener.
  const hasMultiDirect = transport === 'multi' && Array.from(document.querySelectorAll('.multi-transport-type')).some(select => select.value === 'direct');
  document.getElementById('group-direct').style.display = (transport === 'direct' || hasMultiDirect) ? 'grid' : 'none';
  document.getElementById('group-multi').style.display = transport === 'multi' ? 'block' : 'none';
  if (transport === 'multi' && !document.querySelector('.multi-transport-row')) {
    renderMultiTransportRows(null);
  }

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
    urlLabel.textContent = 'Основной URL контекста (--url, опционально)';
    urlInput.placeholder = 'https://disk.yandex.ru/i/...';
  } else {
    urlLabel.textContent = 'URL документа (--url)';
    urlInput.placeholder = 'https://disk.yandex.ru/i/...';
  }
}

const MULTI_TRANSPORT_TYPES = [
  ['direct', 'Direct'],
  ['yandex', 'Yandex.Docs'],
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
    <div class="multi-transport-row-header">
      <div class="multi-transport-row-title">
        <span class="multi-transport-row-number"></span>
        <select class="form-control multi-transport-type" aria-label="Тип транспорта">${MULTI_TRANSPORT_TYPES.map(([v, l]) => `<option value="${v}">${l}</option>`).join('')}</select>
      </div>
      <label class="multi-transport-priority-field">Приоритет
        <input class="form-control multi-transport-priority" type="number" min="0" max="1000" step="1" value="${Number.isFinite(Number(config.priority)) ? Number(config.priority) : 50}" aria-label="Приоритет" title="Приоритет: больше = раньше" />
      </label>
      <button type="button" class="btn btn-outline btn-sm multi-transport-remove" title="Удалить транспорт" aria-label="Удалить транспорт">Удалить</button>
    </div>
    <div class="multi-transport-row-fields">
      <label class="multi-transport-main-field">Значение
        <input class="form-control multi-transport-value" type="text" aria-label="Параметр транспорта" />
      </label>
      <label class="multi-transport-secondary-field">UID MAX
        <input class="form-control multi-transport-secondary" type="text" aria-label="UID пользователя MAX" />
      </label>
    </div>
    <div class="multi-transport-row-help form-hint"></div>`;
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
      updateMultiTransportRowNumbers();
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
  const mainLabel = row.querySelector('.multi-transport-main-field');
  const secondary = row.querySelector('.multi-transport-secondary-field');
  const help = row.querySelector('.multi-transport-row-help');
  const labels = {
    direct: ['Параметры Direct', 'Для exit адрес не требуется', 'Direct в exit использует порт прослушивания --direct-listen выше. --direct-dial относится к клиентскому режиму и здесь не заполняется.'],
    yandex: ['Ссылка на документ Yandex', 'https://disk.yandex.ru/i/...', 'Ссылка документа, который будет использован этим транспортом.'],
    vyandex: ['Ссылка на документ Yandex Volga', 'https://disk.yandex.ru/i/...', 'Ссылка документа Yandex Volga.'],
    boards: ['Ссылка на Yandex Board', 'https://boards.yandex.ru/p/...', 'Публичная ссылка доски Yandex.'],
    mailru: ['Публичная ссылка Mail.ru', 'https://cloud.mail.ru/public/...', 'Публичная ссылка документа Mail.ru.'],
    cupsonline: ['Комнаты cups.online (необязательно)', 'Оставьте пустым для автосоздания', 'Можно оставить пустым: OpenFlux создаст комнаты самостоятельно.'],
    oneme: ['Токен MAX (--maxToken)', 'Токен MAX', 'UID MAX укажите во втором поле этой карточки.']
  };
  const current = labels[type] || labels.yandex;
  mainLabel.childNodes[0] && (mainLabel.childNodes[0].textContent = `${current[0]} `);
  value.placeholder = current[1];
  value.title = current[0];
  help.textContent = current[2];
  secondary.style.display = type === 'oneme' ? '' : 'none';
  if (type === 'direct') {
    value.value = '';
    value.disabled = true;
    value.placeholder = 'Адрес не нужен для exit';
  } else {
    value.disabled = false;
  }
  mainLabel.style.gridColumn = type === 'oneme' ? 'span 1' : '1 / -1';
  updateMultiTransportRowNumbers();
}

function updateMultiTransportRowNumbers() {
  document.querySelectorAll('.multi-transport-row').forEach((row, index) => {
    const number = row.querySelector('.multi-transport-row-number');
    if (number) number.textContent = `${index + 1}.`;
  });
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
  const trafficMB = parseInt(document.getElementById('tunnel-traffic-limit').value) || 0;
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
  const savedUrlForTransport = selectedTransport === 'yandex' ? existing?.yandexUrl
    : selectedTransport === 'vyandex' ? existing?.vyandexUrl
      : selectedTransport === 'boards' ? existing?.boardsUrl
        : selectedTransport === 'mailru' ? existing?.mailruUrl
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
    directListen: selectedTransport === 'multi'
      ? (multiHas('direct') ? (document.getElementById('tunnel-direct-listen').value.trim() || null) : null)
      : (document.getElementById('tunnel-direct-listen').value.trim() || null),
    directDial: selectedTransport === 'multi' ? multiValue('direct') : (existing ? (existing.directDial || null) : null),
    shareHost: document.getElementById('tunnel-share-host').value.trim() || null,
    // A non-multi tunnel must never inherit the hidden field from the default
    // multi cards. That field used to turn every single save into a multi run.
    transports: selectedTransport === 'multi' ? (document.getElementById('tunnel-transports').value.trim() || null) : null,
    yandexUrl: selectedTransport === 'multi' ? multiValue('yandex') : (existing?.yandexUrl || null),
    vyandexUrl: selectedTransport === 'multi' ? multiValue('vyandex') : (existing?.vyandexUrl || null),
    boardsUrl: selectedTransport === 'multi' ? multiValue('boards') : (existing?.boardsUrl || null),
    mailruUrl: selectedTransport === 'multi' ? multiValue('mailru') : (existing?.mailruUrl || null),
    cupsonlineUrl: selectedTransport === 'multi' ? multiValue('cupsonline') : (existing?.cupsonlineUrl || null),
    onemeToken: selectedTransport === 'multi' ? multiValue('oneme') : (existing?.onemeToken || null),
    onemeUid: selectedTransport === 'multi' ? (multiSecondary('oneme') || null) : (existing?.onemeUid || null),
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

// Tunnel error recommendations modal completely disabled per user request
function openTunnelErrorModal() { return false; }
function closeTunnelErrorModal() { return false; }
function onEditFromErrorModal() { return false; }
function onSolveCaptchaFromErrorModal() { return false; }

// ---- Captcha Solver Modal & Handlers ----
let currentCaptchaTunnelId = null;
let currentCaptchaTransport = '';
let captchaSolvedListenerInstalled = false;

function setupCaptchaMessageListener() {
  if (captchaSolvedListenerInstalled) return;
  captchaSolvedListenerInstalled = true;
  window.addEventListener('message', async (e) => {
    if (e.data && e.data.type === 'openflux-captcha-solved') {
      toast('Капча успешно пройдена! Туннель возобновил работу.', 'success');
      closeCaptchaSolverModal();
      if (typeof loadTunnels === 'function') {
        await loadTunnels();
      }
    } else if (e.data && e.data.type === 'openflux-captcha-waiting') {
      toast('Куки переданы. Ожидается подтверждение доступа ядром OpenFlux.', 'info');
      if (typeof loadTunnels === 'function') {
        await loadTunnels();
      }
    }
  });
}

async function openCaptchaSolverModal(tunnelId) {
  setupCaptchaMessageListener();
  currentCaptchaTunnelId = tunnelId;
  const modal = document.getElementById('captcha-solver-modal');
  if (!modal) return;

  const idInput = document.getElementById('captcha-tunnel-id');
  if (idInput) idInput.value = tunnelId;

  // Keep the transport name from the IPC request. In a Session the core asks
  // for cookies by carrier (mailru/yandex/...), never by the synthetic name
  // "multi". The server uses it to keep the browser jar and offer aligned.
  const token = localStorage.getItem('zen_token') || '';
  let captchaTransport = '';
  try {
    const status = await api(`api/tunnels/${tunnelId}/captcha`);
    if (status.ok) {
      const data = await status.json();
      captchaTransport = data.transport || '';
      currentCaptchaTransport = captchaTransport;
      const title = document.getElementById('captcha-solver-title-text');
      const reason = String(data.reason || '').toLowerCase();
      const loginRequired = reason.includes('login') || reason.includes('auth') ||
        reason.includes('account') || reason.includes('private');
      if (title) {
        title.textContent = loginRequired
          ? `Вход в документ (${captchaTransport || 'OpenFlux'})`
          : `Решение капчи (${captchaTransport || 'OpenFlux'})`;
      }
    }
  } catch { }
  const params = new URLSearchParams();
  if (token) params.set('token', token);
  if (captchaTransport) params.set('transport', captchaTransport);
  const tokenQuery = params.toString() ? '?' + params.toString() : '';
  const viewUrl = `api/tunnels/${tunnelId}/captcha/view${tokenQuery}`;

  const iframe = document.getElementById('captcha-solver-iframe');
  const spinner = document.getElementById('captcha-iframe-spinner');
  const fsBtn = document.getElementById('captcha-fullscreen-btn');

  if (fsBtn) {
    fsBtn.href = viewUrl;
  }

  if (iframe) {
    if (spinner) spinner.style.display = 'flex';
    iframe.onload = () => {
      if (spinner) spinner.style.display = 'none';
    };
    iframe.src = viewUrl;
  }

  modal.classList.add('open');
}

function reloadCaptchaIframe() {
  if (!currentCaptchaTunnelId) return;
  const iframe = document.getElementById('captcha-solver-iframe');
  const spinner = document.getElementById('captcha-iframe-spinner');
  if (iframe) {
    if (spinner) spinner.style.display = 'flex';
    const params = new URLSearchParams({ _t: String(Date.now()) });
    const token = localStorage.getItem('zen_token') || '';
    if (token) params.set('token', token);
    if (currentCaptchaTransport) params.set('transport', currentCaptchaTransport);
    iframe.src = `api/tunnels/${currentCaptchaTunnelId}/captcha/view?${params.toString()}`;
  }
}

function closeCaptchaSolverModal() {
  const modal = document.getElementById('captcha-solver-modal');
  if (modal) modal.classList.remove('open');
  const iframe = document.getElementById('captcha-solver-iframe');
  if (iframe) iframe.src = 'about:blank';
  currentCaptchaTunnelId = null;
  currentCaptchaTransport = '';
}

function switchCaptchaTab(tab) {
  const tabOnline = document.getElementById('captcha-tab-online');
  const tabNewDoc = document.getElementById('captcha-tab-newdoc');
  const tabDirect = document.getElementById('captcha-tab-direct');
  const tabManual = document.getElementById('captcha-tab-manual');

  const btnOnline = document.getElementById('tab-btn-online');
  const btnNewDoc = document.getElementById('tab-btn-newdoc');
  const btnDirect = document.getElementById('tab-btn-direct');
  const btnManual = document.getElementById('tab-btn-manual');

  if (tabOnline) tabOnline.style.display = (tab === 'online') ? 'block' : 'none';
  if (tabNewDoc) tabNewDoc.style.display = (tab === 'newdoc') ? 'block' : 'none';
  if (tabDirect) tabDirect.style.display = (tab === 'direct') ? 'block' : 'none';
  if (tabManual) tabManual.style.display = (tab === 'manual') ? 'block' : 'none';

  if (btnOnline) btnOnline.classList.toggle('active-solver-tab', tab === 'online');
  if (btnNewDoc) btnNewDoc.classList.toggle('active-solver-tab', tab === 'newdoc');
  if (btnDirect) btnDirect.classList.toggle('active-solver-tab', tab === 'direct');
  if (btnManual) btnManual.classList.toggle('active-solver-tab', tab === 'manual');

  if (tab === 'manual') {
    const input = document.getElementById('captcha-manual-input');
    if (input) input.focus();
  } else if (tab === 'newdoc') {
    const input = document.getElementById('captcha-new-doc-url');
    if (input) input.focus();
  }
}

async function submitQuickDocumentUrl() {
  const id = currentCaptchaTunnelId || document.getElementById('captcha-tunnel-id')?.value;
  if (!id) return;

  const input = document.getElementById('captcha-new-doc-url');
  const newUrl = input ? input.value.trim() : '';
  if (!newUrl) {
    toast('Вставьте ссылку на новый документ', 'warning');
    return;
  }

  try {
    const res = await api(`api/tunnels/${id}/quick-update-url`, {
      method: 'POST',
      body: JSON.stringify({ url: newUrl })
    });
    const data = await res.json();
    if (res.ok && data.success) {
      toast('Ссылка туннеля обновлена! Туннель перезапущен без капчи.', 'success');
      closeCaptchaSolverModal();
      if (typeof loadTunnels === 'function') await loadTunnels();
    } else {
      toast(data.error || 'Не удалось обновить ссылку', 'danger');
    }
  } catch (e) {
    toast('Ошибка соединения: ' + (e.message || e), 'danger');
  }
}

async function submitSwitchToDirect() {
  const id = currentCaptchaTunnelId || document.getElementById('captcha-tunnel-id')?.value;
  if (!id) return;

  if (!confirm('Переключить транспорт туннеля на Direct (прямое соединение без капч)?')) {
    return;
  }

  try {
    const res = await api(`api/tunnels/${id}/switch-to-direct`, { method: 'POST' });
    const data = await res.json();
    if (res.ok && data.success) {
      toast('Транспорт переключен на Direct! Капчи отключены.', 'success');
      closeCaptchaSolverModal();
      if (typeof loadTunnels === 'function') await loadTunnels();
    } else {
      toast(data.error || 'Не удалось переключить транспорт', 'danger');
    }
  } catch (e) {
    toast('Ошибка соединения: ' + (e.message || e), 'danger');
  }
}

function openEditTunnelFromCaptchaModal() {
  const id = currentCaptchaTunnelId || document.getElementById('captcha-tunnel-id')?.value;
  closeCaptchaSolverModal();
  if (id && typeof editTunnel === 'function') {
    editTunnel(id);
  }
}

async function pasteFromClipboardToManualInput() {
  const input = document.getElementById('captcha-manual-input');
  if (!input) return;

  try {
    if (navigator.clipboard && navigator.clipboard.readText) {
      const text = await navigator.clipboard.readText();
      if (text && text.trim().length > 0) {
        input.value = text.trim();
        toast('Куки вставлены из буфера обмена', 'success');
        return;
      }
    }
  } catch (err) {
    console.warn('Clipboard read permission denied', err);
  }

  input.focus();
  toast('Нажмите Ctrl + V в поле ввода', 'info');
}

async function pasteAndSubmitCaptchaCookies() {
  await pasteFromClipboardToManualInput();
}

async function submitManualCaptchaCookies() {
  const id = currentCaptchaTunnelId || document.getElementById('captcha-tunnel-id').value;
  if (!id) return;

  const input = document.getElementById('captcha-manual-input');
  const rawVal = input ? input.value.trim() : '';
  if (!rawVal) {
    toast('Пожалуйста, вставьте строку кук или токен', 'warning');
    return;
  }

  const btn = document.getElementById('btn-submit-captcha-cookies');
  const spinner = document.getElementById('btn-captcha-spinner');
  if (btn) btn.disabled = true;
  if (spinner) spinner.style.display = 'inline-block';

  try {
    const res = await api(`api/tunnels/${id}/cookies`, {
      method: 'POST',
      body: JSON.stringify({ cookies: rawVal })
    });

    const data = await res.json().catch(() => ({}));
    if (res.ok && data && data.success) {
      toast(data.message || t('toast_captcha_applied') || 'Куки переданы; ожидается подтверждение доступа ядром.', 'success');
      closeCaptchaSolverModal();
      if (typeof loadTunnels === 'function') {
        await loadTunnels();
      }
    } else {
      toast((t('toast_captcha_error') || 'Ошибка: ') + (data && data.error ? data.error : 'Не удалось применить куки'), 'error');
    }
  } catch (err) {
    console.error('Failed to submit captcha cookies', err);
    toast((t('toast_captcha_error') || 'Ошибка: ') + (err.message || err), 'error');
  } finally {
    if (btn) btn.disabled = false;
    if (spinner) spinner.style.display = 'none';
  }
}
