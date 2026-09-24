// OpenFlux Zen Server - Live Logs & Export

function updateLogSelect(list) {
  const sel = document.getElementById('log-target-select');
  const cur = sel.value;
  sel.innerHTML = '<option value="system">Лог панели (System)</option>' +
    list.map(t => `<option value="${t.id}">${escapeHtml(t.name)}</option>`).join('');
  if (Array.from(sel.options).some(o => o.value === cur)) sel.value = cur;
}

function viewTunnelLogs(id) {
  switchTab('logs');
  const sel = document.getElementById('log-target-select');
  sel.value = id;
  loadActiveLogs();
}

async function loadActiveLogs() {
  const target = document.getElementById('log-target-select').value;
  const term = document.getElementById('terminal-output');
  try {
    let url = target === 'system' ? 'api/logs/system?tail=200' : `api/tunnels/${target}/logs?tail=200`;
    const res = await api(url);
    if (!res.ok) return;
    const lines = await res.json();
    if (lines.length === 0) {
      term.innerHTML = '<div class="log-line log-system">Нет записей в журнале логов.</div>';
      return;
    }

    if (target === 'system') {
      term.innerHTML = lines.map(l => `<div class="log-line log-stdout">${escapeHtml(l)}</div>`).join('');
    } else {
      term.innerHTML = lines.map(l => {
        const cls = l.stream === 'stderr' ? 'log-stderr' : (l.stream === 'system' ? 'log-system' : 'log-stdout');
        return `<div class="log-line ${cls}">[${new Date(l.timestamp).toLocaleTimeString()}] [${l.stream.toUpperCase()}] ${escapeHtml(l.message)}</div>`;
      }).join('');
    }
    term.scrollTop = term.scrollHeight;
  } catch {}
}

async function clearCurrentLogs() {
  const target = document.getElementById('log-target-select').value;
  if (target !== 'system') {
    await api(`api/tunnels/${target}/logs`, { method: 'DELETE' });
    toast('Лог туннеля очищен', 'info');
    loadActiveLogs();
  }
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
      toast('Не удалось получить логи для скачивания', 'danger');
      return;
    }
    const lines = await res.json();
    if (!lines || lines.length === 0) {
      toast('Логи пусты, нечего скачивать', 'warning');
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
    toast('Логи успешно сохранены в файл', 'success');
  } catch (err) {
    toast('Ошибка при сохранении логов', 'danger');
  }
}
