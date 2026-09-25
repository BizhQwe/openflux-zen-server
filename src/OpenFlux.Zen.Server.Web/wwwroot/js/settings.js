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
