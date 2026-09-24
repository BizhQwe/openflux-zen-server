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
  toast('Конфигурация выгружена', 'success');
}

async function copyConfigToClipboard() {
  const res = await api('api/config/export');
  const text = await res.text();
  await navigator.clipboard.writeText(text);
  toast('JSON скопирован в буфер обмена', 'success');
}

async function importConfig() {
  const text = document.getElementById('import-json').value.trim();
  if (!text) { toast('Вставьте JSON', 'danger'); return; }
  try {
    const res = await api('api/config/import', { method: 'POST', body: text });
    const d = await res.json();
    toast(d.message, d.errors > 0 ? 'warning' : 'success');
    loadTunnels();
  } catch {
    toast('Ошибка импорта JSON', 'danger');
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
    const autostartInput = document.getElementById('setting-autostart');
    const autostartLabel = document.getElementById('setting-autostart-label');
    if (autostartInput) {
      autostartInput.checked = s.autoStartEnabled !== false;
      if (autostartLabel) autostartLabel.textContent = autostartInput.checked ? 'Включён' : 'Выключен';
    }
  }
}

async function toggleAutostart(enabled) {
  const label = document.getElementById('setting-autostart-label');
  if (label) label.textContent = enabled ? 'Включён' : 'Выключен';
  try {
    const res = await api('api/settings/autostart', {
      method: 'POST',
      body: JSON.stringify({ enabled })
    });
    if (res.ok) {
      toast(enabled ? 'Автозапуск службы включён' : 'Автозапуск службы отключён', 'success');
    } else {
      toast('Не удалось обновить настройки автозапуска', 'danger');
    }
  } catch {
    toast('Ошибка соединения при изменении автозапуска', 'danger');
  }
}

async function updateAccountProfile() {
  const cur = document.getElementById('cur-pass').value;
  const u = document.getElementById('setting-username').value.trim();
  const nw = document.getElementById('new-pass').value;

  if (!cur) {
    toast('Введите текущий пароль для подтверждения', 'danger');
    return;
  }

  try {
    const res = await api('api/auth/change-profile', {
      method: 'POST',
      body: JSON.stringify({ currentPassword: cur, newUsername: u, newPassword: nw })
    });
    const data = await res.json();
    if (res.ok && data.success) {
      toast('Данные учётной записи успешно обновлены', 'success');
      document.getElementById('cur-pass').value = '';
      document.getElementById('new-pass').value = '';
      if (data.username) {
        document.getElementById('setting-username').value = data.username;
      }
    } else {
      toast(data.error || data.message || 'Ошибка обновления учётных данных', 'danger');
    }
  } catch {
    toast('Не удалось сохранить изменения', 'danger');
  }
}

async function regenerateSecretPath() {
  if (!confirm('Сгенерировать новый секретный URL? Текущая ссылка станет недействительной!')) return;
  const res = await api('api/settings/regenerate-secret', { method: 'POST' });
  if (res.ok) {
    const d = await res.json();
    toast('Секретный путь обновлён! Перенаправление...', 'success');
    setTimeout(() => {
      window.location.pathname = '/' + d.secretPath.replace(/^\/+|\/+$/g, '') + '/';
    }, 1500);
  }
}
