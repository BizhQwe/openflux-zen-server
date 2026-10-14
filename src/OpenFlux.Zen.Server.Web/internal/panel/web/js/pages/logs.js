// OpenFlux Zen Server - Live Logs & Export

let logsInterval = null;
let lastLogDataHash = '';

function startLogsPolling() {
  stopLogsPolling();
  loadActiveLogs(false);
  logsInterval = setInterval(() => {
    if (document.hidden) return;
    const tab = document.getElementById('tab-logs');
    if (!tab || !tab.classList.contains('active')) {
      stopLogsPolling();
      return;
    }
    loadActiveLogs(true);
  }, 1500);
}

function stopLogsPolling() {
  if (logsInterval) {
    clearInterval(logsInterval);
    logsInterval = null;
  }
}

function updateLogSelect(list) {
  const sel = document.getElementById('log-target-select');
  if (!sel) return;
  const cur = sel.value;
  sel.innerHTML = `<option value="system">${t('logs_panel_system')}</option>` +
    (Array.isArray(list) ? list.map(t => `<option value="${t.id || t.Id}">${escapeHtml(t.name || t.Name || 'Tunnel')}</option>`).join('') : '');
  if (Array.from(sel.options).some(o => o.value === cur)) sel.value = cur;
}

function viewTunnelLogs(id) {
  const sel = document.getElementById('log-target-select');
  if (sel) sel.value = id;
  lastLogDataHash = '';
  switchTab('logs');
}

async function loadActiveLogs(isAuto = false) {
  const sel = document.getElementById('log-target-select');
  if (!sel) return;
  const target = sel.value;
  const term = document.getElementById('terminal-output');
  if (!term) return;

  if (!isAuto) {
    lastLogDataHash = '';
  }

  try {
    let url = target === 'system' ? 'api/logs/system?tail=200' : `api/tunnels/${target}/logs?tail=200`;
    const res = await api(url);
    if (!res.ok) return;
    const lines = await res.json();

    const currentHash = target + ':' + JSON.stringify(lines);
    if (isAuto && currentHash === lastLogDataHash) {
      return;
    }

    const wasNearBottom = (term.scrollHeight - term.scrollTop - term.clientHeight) < 60;

    if (!lines || lines.length === 0) {
      term.innerHTML = `<div class="log-line log-system">${t('logs_empty')}</div>`;
    } else if (target === 'system') {
      term.innerHTML = lines.map(l => {
        const isErr = /error|failed|fatal|panic/i.test(l);
        const isWarn = /warn|retry|retrying/i.test(l);
        const cls = isErr ? 'log-stderr' : (isWarn ? 'log-system' : 'log-stdout');
        return `<div class="log-line ${cls}">${escapeHtml(l)}</div>`;
      }).join('');
    } else {
      term.innerHTML = lines.map(l => {
        const msg = l.message || '';
        const isErr = l.stream === 'stderr' || /error|failed|fatal|cannot open|timeout/i.test(msg);
        const isWarn = /warn|retry|retrying|dropped|reconnecting/i.test(msg);
        const cls = isErr ? 'log-stderr' : (isWarn ? 'log-system' : (l.stream === 'system' ? 'log-system' : 'log-stdout'));
        const timeStr = l.timestamp ? new Date(l.timestamp).toLocaleTimeString() : '';
        const timePrefix = timeStr ? `[${timeStr}] ` : '';
        const streamPrefix = l.stream ? `[${l.stream.toUpperCase()}] ` : '';
        return `<div class="log-line ${cls}">${timePrefix}${streamPrefix}${escapeHtml(msg)}</div>`;
      }).join('');
    }

    lastLogDataHash = currentHash;

    if (!isAuto || wasNearBottom) {
      term.scrollTop = term.scrollHeight;
    }
  } catch {}
}

async function clearCurrentLogs() {
  const target = document.getElementById('log-target-select').value;
  const url = target === 'system' ? 'api/logs/system' : `api/tunnels/${target}/logs`;
  const response = await api(url, { method: 'DELETE' });
  if (!response.ok) { toast(t('toast_logs_fetch_fail'), 'danger'); return; }
  toast(t('toast_logs_cleared'), 'info');
  lastLogDataHash = '';
  loadActiveLogs(false);
}

async function downloadCurrentLogs() {
  const select = document.getElementById('log-target-select');
  const target = select.value;
  const selectedOption = select.options[select.selectedIndex];
  const targetName = (selectedOption ? selectedOption.text : target).replace(/[^a-zA-Z0-9_\u0400-\u04FF-]/g, '_');
  try {
    let url = target === 'system' ? 'api/logs/system?tail=2000' : `api/tunnels/${target}/logs?tail=2000`;
    const res = await api(url);
    if (!res.ok) {
      toast(t('toast_logs_fetch_fail'), 'danger');
      return;
    }
    const lines = await res.json();
    if (!lines || lines.length === 0) {
      toast(t('toast_logs_empty'), 'warning');
      return;
    }
    let textContent = '';
    if (target === 'system') {
      textContent = lines.join('\r\n');
    } else {
      textContent = lines.map(l => {
        const timeStr = l.timestamp ? new Date(l.timestamp).toISOString() : '';
        return `[${timeStr}] [${(l.stream || '').toUpperCase()}] ${l.message || ''}`;
      }).join('\r\n');
    }

    const blob = new Blob([textContent], { type: 'text/plain;charset=utf-8' });
    const a = document.createElement('a');
    const dateStr = new Date().toISOString().slice(0, 19).replace(/[:T]/g, '-');
    a.href = URL.createObjectURL(blob);
    a.download = `openflux-log-${targetName}-${dateStr}.txt`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(a.href);
    toast(t('toast_logs_download_success'), 'success');
  } catch (err) {
    toast(t('toast_logs_download_error'), 'danger');
  }
}
