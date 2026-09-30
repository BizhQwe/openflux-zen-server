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
      if (tItem.status === 2) {
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
      const hasError = Boolean(tItem.errorMessage);
      const expectedWarningHtml = hasError
        ? `<span class="badge badge-warning-error" onclick="openTunnelErrorModal('${tItem.id}')" title="${escapeHtml(tItem.errorMessage)}"><svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" style="flex-shrink:0;"><path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3Z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg><span>${t('badge_error')}</span></span>`
        : '';
      if (warningEl.innerHTML !== expectedWarningHtml) {
        warningEl.innerHTML = expectedWarningHtml;
      }
    }

    const captchaActionEl = document.getElementById('tunnel-captcha-action-' + tItem.id);
    if (captchaActionEl) {
      const needCaptcha = isCaptchaRequired(tItem);
      const expectedCaptchaHtml = needCaptcha ? renderCaptchaActionBtn(tItem.id) : '';
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

function isCaptchaRequired(tItem) {
  if (!tItem) return false;
  if (tItem.pendingCaptchaUrl) return true;
  const msg = (tItem.errorMessage || '').toLowerCase();
  if (msg.includes('капч') || msg.includes('captcha') || msg.includes('smartcaptcha')) {
    return true;
  }
  if ((tItem.transport === 'yandex' || tItem.transport === 'vyandex') && tItem.errorMessage) {
    if (msg.includes('403') || msg.includes('проверк') || msg.includes('check') || msg.includes('robot') || msg.includes('block')) {
      return true;
    }
  }
  return false;
}

function renderCaptchaActionBtn(tunnelId) {
  const label = t('btn_solve_captcha') || 'Пройти капчу';
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
    if (tItem.status === 2) {
      statusBadge = `<span class="badge badge-status badge-running"><span class="pulse"></span>${t('badge_running')}</span>`;
    } else if (tItem.status === 1) {
      statusBadge = `<span class="badge badge-status badge-starting">${t('badge_starting')}</span>`;
    } else if (tItem.status === 4) {
      statusBadge = `<span class="badge badge-status badge-failed" title="${escapeHtml(tItem.errorMessage || '')}">${t('badge_failed')}</span>`;
    }

    const isRunning = tItem.status === 2;
    const hasError = Boolean(tItem.errorMessage);
    const warningBadge = hasError
      ? `<span class="badge badge-warning-error" onclick="openTunnelErrorModal('${tItem.id}')" title="${escapeHtml(tItem.errorMessage)}"><svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" style="flex-shrink:0;"><path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3Z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg><span>${t('badge_error')}</span></span>`
      : '';
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
            <span id="tunnel-warning-${tItem.id}">${warningBadge}</span>
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
            <button class="btn btn-outline btn-sm" onclick="showTunnelConnect('${tItem.id}')" title="${t('btn_connect_qr') || 'Подключение'}">
              <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <rect x="3" y="3" width="7" height="7"/><rect x="14" y="3" width="7" height="7"/><rect x="14" y="14" width="7" height="7"/><rect x="3" y="14" width="7" height="7"/>
              </svg>
              <span>${t('btn_connect_qr') || 'Подключение'}</span>
            </button>
            <span id="tunnel-captcha-action-${tItem.id}">
              ${isCaptchaRequired(tItem) ? renderCaptchaActionBtn(tItem.id) : ''}
            </span>
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

let currentErrorTunnelId = null;

function openTunnelErrorModal(id) {
  currentErrorTunnelId = id;
  const modal = document.getElementById('tunnel-error-modal');
  if (!modal) return;

  const tItem = (typeof tunnelsData !== 'undefined' && Array.isArray(tunnelsData))
    ? tunnelsData.find(x => x.id === id)
    : null;

  const titleEl = document.getElementById('tunnel-error-modal-title');
  if (titleEl) {
    const tunnelName = tItem ? tItem.name : '';
    titleEl.innerHTML = `
      <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="#f87171" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" style="flex-shrink:0;">
        <path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3Z"/>
        <line x1="12" y1="9" x2="12" y2="13"/>
        <line x1="12" y1="17" x2="12.01" y2="17"/>
      </svg>
      <span>${escapeHtml(tunnelName ? `${tunnelName} — ` : '')}${t('modal_tunnel_error_title')}</span>
    `;
  }

  const isRu = (typeof currentLanguage !== 'undefined' ? currentLanguage : 'ru') === 'ru';
  const transport = (tItem && tItem.transport ? tItem.transport.toLowerCase() : '');
  const rawErr = (tItem && tItem.errorMessage) ? tItem.errorMessage.trim() : '';

  let errorText = '';
  let recs = [];

  const isCaptchaOrYandex = transport === 'yandex' || /капч|captcha|smartcaptcha|яндекс|yandex/i.test(rawErr);
  const isMailRu = transport === 'mailru' || /mail\.ru|mailru/i.test(rawErr);
  const isDirect = transport === 'direct' || /direct/i.test(rawErr);
  const isKeyMismatch = /несовпадение|ключ|контекст|mismatch|context|encryption/i.test(rawErr);

  if (isKeyMismatch) {
    errorText = isRu
      ? 'Несовпадение ключа или контекста шифрования: подключающийся клиент использует другой ключ или устаревший контекст.'
      : 'Encryption key or context mismatch: the connecting client is using a different key or outdated context.';
    recs = isRu
      ? [
          'Скопируйте актуальную ссылку подключения (Share link) или QR-код и заново импортируйте её на клиенте.',
          'Убедитесь, что на клиенте и сервере используется идентичный ключ шифрования.'
        ]
      : [
          'Copy the latest share link or QR code and re-import it on the client.',
          'Ensure the client and server use the identical encryption key.'
        ];
  } else if (isCaptchaOrYandex) {
    errorText = isRu
      ? 'Обнаружена проблема связи: возможно, документ заблокирован капчей (SmartCaptcha) от Яндекса, либо документ просто недоступен (закрыт или удалён).'
      : 'Connection issue detected: the document may be blocked by Yandex SmartCaptcha, or the document is simply inaccessible (closed or deleted).';
    recs = isRu
      ? [
          'Создайте новый публичный документ (Word или Excel) на Яндекс Диске и укажите новую ссылку в настройках туннеля.',
          'Или переключитесь на прямое подключение <strong>Direct</strong> либо транспорт <strong>Mail.ru</strong>.'
        ]
      : [
          'Create a new public document (Word or Excel) on Yandex Disk and update the URL in tunnel settings.',
          'Or switch to a <strong>Direct</strong> connection or <strong>Mail.ru</strong> transport.'
        ];
  } else if (isMailRu) {
    errorText = isRu
      ? 'Обнаружена проблема связи с транспортом Mail.ru: файл недоступен, ссылка устарела или сервис временно заблокировал запросы.'
      : 'Connection issue detected with Mail.ru transport: file is inaccessible, the link has expired, or requests are blocked.';
    recs = isRu
      ? [
          'Проверьте правильность и публичность ссылки на файл в Облаке Mail.ru в настройках туннеля.',
          'Либо переключитесь на прямое подключение <strong>Direct</strong> либо транспорт <strong>Яндекс</strong>.'
        ]
      : [
          'Verify the public file link in Mail.ru Cloud within tunnel settings.',
          'Or switch to a <strong>Direct</strong> connection or <strong>Yandex</strong> transport.'
        ];
  } else if (isDirect) {
    errorText = isRu
      ? 'Обнаружена проблема прямого соединения (Direct): удалённый узел недоступен, порт закрыт или соединение разорвано.'
      : 'Direct connection issue detected: remote host is unreachable, port is closed, or connection was interrupted.';
    recs = isRu
      ? [
          'Проверьте адрес прослушивания (Direct Listen), номер порта и правила брандмауэра на сервере.',
          'Убедитесь, что порт открыт для внешних входящих подключений (NAT/порт-форвардинг).'
        ]
      : [
          'Check the Direct Listen address, port number, and server firewall rules.',
          'Ensure the port is open for external incoming connections (NAT/port-forwarding).'
        ];
  } else {
    // Universal error text for any other tunnel / transport
    errorText = isRu
      ? (rawErr ? `Обнаружена ошибка соединения: ${rawErr}` : 'Обнаружена проблема соединения. Проверьте параметры туннеля.')
      : (rawErr ? `Connection issue detected: ${rawErr}` : 'Connection issue detected. Please check tunnel settings.');
    recs = isRu
      ? [
          'Проверьте параметры туннеля, правильность URL / адреса и ключа шифрования.',
          'Попробуйте перезапустить туннель или выбрать другой сетевой транспорт.'
        ]
      : [
          'Check tunnel settings, verify the URL/address and encryption key.',
          'Try restarting the tunnel or selecting a different network transport.'
        ];
  }

  const textEl = document.getElementById('tunnel-error-modal-text');
  if (textEl) {
    textEl.innerHTML = escapeHtml(errorText).replace(/&lt;strong&gt;/g, '<strong>').replace(/&lt;\/strong&gt;/g, '</strong>');
  }

  const recsListEl = document.getElementById('tunnel-error-modal-recs-list');
  if (recsListEl) {
    recsListEl.innerHTML = recs.map(r => `<li style="margin-bottom: 4px;">${r}</li>`).join('');
  }

  const detailsEl = document.getElementById('tunnel-error-modal-details');
  if (detailsEl) {
    if (rawErr && rawErr !== errorText && !isCaptchaOrYandex) {
      detailsEl.textContent = rawErr;
      detailsEl.style.display = 'block';
    } else {
      detailsEl.style.display = 'none';
    }
  }

  const solveBtn = document.getElementById('btn-open-solver-from-error');
  if (solveBtn) {
    solveBtn.style.display = isCaptchaOrYandex ? 'inline-flex' : 'none';
  }

  modal.classList.add('open');
}

function closeTunnelErrorModal() {
  const modal = document.getElementById('tunnel-error-modal');
  if (modal) modal.classList.remove('open');
  currentErrorTunnelId = null;
}

function onEditFromErrorModal() {
  const id = currentErrorTunnelId;
  closeTunnelErrorModal();
  if (id && typeof editTunnel === 'function') {
    editTunnel(id);
  }
}

function onSolveCaptchaFromErrorModal() {
  const id = currentErrorTunnelId;
  closeTunnelErrorModal();
  if (id) {
    openCaptchaSolverModal(id);
  }
}

// ---- Captcha Solver Modal & Handlers ----
let currentCaptchaTunnelId = null;
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

  const manualInput = document.getElementById('captcha-manual-input');
  if (manualInput) manualInput.value = '';

  const tItem = (typeof tunnelsData !== 'undefined' && Array.isArray(tunnelsData))
    ? tunnelsData.find(x => x.id === tunnelId) : null;

  // Default to the online interactive solver
  switchCaptchaTab('online');

  // Prepare iframe URL with token
  const token = localStorage.getItem('zen_token') || '';
  const tokenQuery = token ? '?token=' + encodeURIComponent(token) : '';
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

  // Pre-fill quick URL input if on newdoc tab
  const newDocInput = document.getElementById('captcha-new-doc-url');
  if (newDocInput && tItem && tItem.url) {
    newDocInput.value = '';
    newDocInput.placeholder = tItem.url;
  }

  modal.classList.add('open');
}

function reloadCaptchaIframe() {
  if (!currentCaptchaTunnelId) return;
  const iframe = document.getElementById('captcha-solver-iframe');
  const spinner = document.getElementById('captcha-iframe-spinner');
  if (iframe) {
    if (spinner) spinner.style.display = 'flex';
    const token = localStorage.getItem('zen_token') || '';
    const tokenQuery = token ? '?token=' + encodeURIComponent(token) : '';
    const sep = tokenQuery ? '&' : '?';
    iframe.src = `api/tunnels/${currentCaptchaTunnelId}/captcha/view${tokenQuery}${sep}_t=${Date.now()}`;
  }
}

function closeCaptchaSolverModal() {
  const modal = document.getElementById('captcha-solver-modal');
  if (modal) modal.classList.remove('open');
  const iframe = document.getElementById('captcha-solver-iframe');
  if (iframe) iframe.src = 'about:blank';
  currentCaptchaTunnelId = null;
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

    if (res && res.success) {
      toast(t('toast_captcha_applied') || 'Куки успешно применены! Туннель возобновил работу.', 'success');
      closeCaptchaSolverModal();
      if (typeof loadTunnels === 'function') {
        await loadTunnels();
      }
    } else {
      toast((t('toast_captcha_error') || 'Ошибка: ') + (res && res.error ? res.error : 'Не удалось применить куки'), 'error');
    }
  } catch (err) {
    console.error('Failed to submit captcha cookies', err);
    toast((t('toast_captcha_error') || 'Ошибка: ') + (err.message || err), 'error');
  } finally {
    if (btn) btn.disabled = false;
    if (spinner) spinner.style.display = 'none';
  }
}
