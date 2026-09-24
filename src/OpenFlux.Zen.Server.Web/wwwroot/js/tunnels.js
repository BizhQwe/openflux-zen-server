// OpenFlux Zen Server - Tunnels Management

async function loadTunnels() {
  try {
    const res = await api('api/tunnels');
    if (!res.ok) return;
    tunnelsData = await res.json();
    renderTunnels(tunnelsData);
    updateLogSelect(tunnelsData);
  } catch (err) {
    console.error('Failed to load tunnels:', err);
  }
}

function renderTunnels(list) {
  const container = document.getElementById('tunnel-list');
  if (!list || list.length === 0) {
    container.innerHTML = `
      <div class="card" style="text-align: center; color: var(--text-dim); padding: 40px 20px;">
        <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" style="margin: 0 auto 12px; display: block; opacity: 0.6;"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
        Нет настроенных туннелей.<br>Нажмите «Добавить туннель», чтобы создать первый туннель OpenFlux.
      </div>
    `;
    return;
  }

  container.innerHTML = list.map(t => {
    const isRunning = t.status === 'Running' || t.status === 1;
    const badgeClass = isRunning ? 'badge-running' : (t.status === 'Error' || t.status === 3 ? 'badge-error' : 'badge-stopped');
    const badgeText = isRunning ? 'АКТИВЕН' : (t.status === 'Error' || t.status === 3 ? 'ОШИБКА' : 'ОСТАНОВЛЕН');

    return `
      <div class="tunnel-item" id="tunnel-${t.id}">
        <div class="tunnel-top">
          <div class="tunnel-title-group">
            <span class="tunnel-name">${escapeHtml(t.name)}</span>
            <div class="badges">
              <span class="badge badge-status ${badgeClass}">
                <span class="badge-dot"></span>${badgeText}
              </span>
              <span class="badge badge-role">${t.role.toUpperCase()}</span>
              <span class="badge badge-transport">${t.transport.toUpperCase()}</span>
              <span class="badge badge-transport">${t.mode ? t.mode.toUpperCase() : 'L4'}</span>
              <span class="badge badge-transport">${t.codec ? t.codec.toUpperCase() : 'BATCHED'}</span>
            </div>
          </div>
        </div>

        <div class="tunnel-stats-grid">
          <div class="tunnel-stat">
            <span class="tunnel-stat-label">Клиенты</span>
            <span class="tunnel-stat-val">${t.activeClients || 0} <small>(${t.clientLimit > 0 ? t.clientLimit : '∞'})</small></span>
          </div>
          <div class="tunnel-stat">
            <span class="tunnel-stat-label">Отдано (Upload)</span>
            <span class="tunnel-stat-val">↑ ${fmtBytes(t.bytesSent)}</span>
          </div>
          <div class="tunnel-stat">
            <span class="tunnel-stat-label">Принято (Download)</span>
            <span class="tunnel-stat-val">↓ ${fmtBytes(t.bytesReceived)}</span>
          </div>
          <div class="tunnel-stat">
            <span class="tunnel-stat-label">Лимит трафика</span>
            <span class="tunnel-stat-val">${t.trafficLimitBytes > 0 ? fmtBytes(t.trafficLimitBytes) : 'Без лимита'}</span>
          </div>
        </div>

        <div class="tunnel-actions">
          ${isRunning
            ? `<button class="btn btn-danger btn-sm" onclick="stopTunnel('${t.id}')"><svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor"><rect x="4" y="4" width="16" height="16" rx="2"/></svg>Остановить</button>`
            : `<button class="btn btn-success btn-sm" onclick="startTunnel('${t.id}')"><svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor"><polygon points="5 3 19 12 5 21 5 3"/></svg>Запустить</button>`
          }
          <button class="btn btn-outline btn-sm" onclick="viewTunnelLogs('${t.id}')"><svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/><polyline points="10 9 9 9 8 9"/></svg>Логи</button>
          <button class="btn btn-outline btn-sm" onclick="resetStats('${t.id}')"><svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8"/><path d="M3 3v5h5"/></svg>Сброс трафика</button>

          <div style="margin-left: auto; display: flex; gap: 8px;">
            <button class="btn btn-outline btn-sm" onclick="editTunnel('${t.id}')"><svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/></svg>Настроить</button>
            <button class="btn btn-outline btn-sm btn-danger-hover" onclick="deleteTunnel('${t.id}')"><svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/></svg>Удалить</button>
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
  try {
    await api(`api/tunnels/${id}/reset-stats`, { method: 'POST' });
    toast('Статистика туннеля сброшена', 'info');
    loadTunnels();
  } catch {
    toast('Ошибка соединения', 'danger');
  }
}

async function deleteTunnel(id) {
  if (!confirm('Вы уверены, что хотите удалить этот туннель?')) return;
  try {
    const res = await api(`api/tunnels/${id}`, { method: 'DELETE' });
    if (res.ok) {
      toast('Туннель удален', 'success');
      loadTunnels();
    } else {
      toast('Не удалось удалить туннель', 'danger');
    }
  } catch {
    toast('Ошибка соединения', 'danger');
  }
}

function openTunnelModal(tunnel = null) {
  document.getElementById('tunnel-id').value = tunnel ? tunnel.id : '';
  document.getElementById('tunnel-modal-title').textContent = tunnel ? 'Настройка туннеля' : 'Новый туннель';
  document.getElementById('tunnel-name').value = tunnel ? tunnel.name : 'Tunnel-' + Math.floor(Math.random()*1000);
  document.getElementById('tunnel-role').value = tunnel ? tunnel.role : 'exit';
  document.getElementById('tunnel-transport').value = tunnel ? tunnel.transport : 'yandex';
  document.getElementById('tunnel-mode').value = tunnel ? (tunnel.mode || 'l4') : 'l4';
  document.getElementById('tunnel-local-ip').value = tunnel ? (tunnel.localIp || '') : '';
  document.getElementById('tunnel-inbound').value = tunnel ? (tunnel.inbound || 'socks5') : 'socks5';
  document.getElementById('tunnel-socks5').value = tunnel ? (tunnel.socks5Address || ':1080') : ':1080';
  document.getElementById('tunnel-url').value = tunnel ? (tunnel.url || '') : '';
  document.getElementById('tunnel-maxtoken').value = tunnel ? (tunnel.maxToken || '') : '';
  document.getElementById('tunnel-maxuid').value = tunnel ? (tunnel.maxUid || '') : '';
  document.getElementById('tunnel-codec').value = tunnel ? (tunnel.codec || 'batched') : 'batched';
  document.getElementById('tunnel-encryption').value = tunnel ? (tunnel.encryptionKey || '') : '';
  document.getElementById('tunnel-client-limit').value = tunnel ? tunnel.clientLimit : 0;
  document.getElementById('tunnel-traffic-limit').value = tunnel && tunnel.trafficLimitBytes > 0 ? Math.round(tunnel.trafficLimitBytes / (1024*1024)) : 0;
  document.getElementById('tunnel-extra-args').value = tunnel ? (tunnel.extraArgs || '') : '';
  document.getElementById('tunnel-enabled').checked = tunnel ? tunnel.isEnabled : true;

  onRoleChange();
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

function onRoleChange() {
  const role = document.getElementById('tunnel-role').value;
  document.getElementById('group-exit').style.display = role === 'exit' ? 'grid' : 'none';
  document.getElementById('group-client').style.display = role === 'client' ? 'grid' : 'none';
}

function onTransportChange() {
  const transport = document.getElementById('tunnel-transport').value;
  document.getElementById('group-oneme').style.display = transport === 'oneme' ? 'grid' : 'none';
  const urlLabel = document.getElementById('tunnel-url-label');
  if (transport === 'mailru') urlLabel.textContent = 'Публичная ссылка Mail.ru (--url)';
  else if (transport === 'cupsonline') urlLabel.textContent = 'Список комнат base64 (--url)';
  else urlLabel.textContent = 'URL документа (--url)';
}

async function saveTunnel() {
  const id = document.getElementById('tunnel-id').value;
  const trafficMB = parseInt(document.getElementById('tunnel-traffic-limit').value) || 0;
  const payload = {
    name: document.getElementById('tunnel-name').value.trim(),
    role: document.getElementById('tunnel-role').value,
    transport: document.getElementById('tunnel-transport').value,
    mode: document.getElementById('tunnel-mode').value,
    localIp: document.getElementById('tunnel-local-ip').value.trim() || null,
    inbound: document.getElementById('tunnel-inbound').value,
    socks5Address: document.getElementById('tunnel-socks5').value.trim() || ':1080',
    url: document.getElementById('tunnel-url').value.trim() || null,
    maxToken: document.getElementById('tunnel-maxtoken').value.trim() || null,
    maxUid: document.getElementById('tunnel-maxuid').value.trim() || null,
    codec: document.getElementById('tunnel-codec').value,
    encryptionKey: document.getElementById('tunnel-encryption').value.trim() || null,
    clientLimit: parseInt(document.getElementById('tunnel-client-limit').value) || 0,
    trafficLimitBytes: trafficMB > 0 ? trafficMB * 1024 * 1024 : 0,
    extraArgs: document.getElementById('tunnel-extra-args').value.trim() || null,
    isEnabled: document.getElementById('tunnel-enabled').checked
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
      loadTunnels();
    } else {
      toast('Ошибка сохранения туннеля', 'danger');
    }
  } catch {
    toast('Ошибка соединения', 'danger');
  }
}
