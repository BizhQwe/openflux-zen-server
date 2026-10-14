// OpenFlux Zen Server - API & Utilities

function toast(msg, type = 'info') {
  const container = document.getElementById('toast-container');
  const t = document.createElement('div');
  t.className = `toast toast-${type}`;
  t.textContent = msg;
  container.appendChild(t);
  setTimeout(() => {
    t.style.opacity = '0';
    t.style.transform = 'translateY(10px)';
    setTimeout(() => t.remove(), 250);
  }, 3500);
}

function fmtBytes(bytes) {
  if (!bytes || isNaN(bytes) || bytes === 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let i = 0;
  while (bytes >= 1024 && i < units.length - 1) {
    bytes /= 1024;
    i++;
  }
  return bytes.toFixed(1) + ' ' + units[i];
}

function fmtSpeed(bytesPerSec) {
  if (!bytesPerSec || isNaN(bytesPerSec) || bytesPerSec === 0) return '0 B/s';
  const units = ['B/s', 'KB/s', 'MB/s', 'GB/s'];
  let i = 0;
  while (bytesPerSec >= 1024 && i < units.length - 1) {
    bytesPerSec /= 1024;
    i++;
  }
  return bytesPerSec.toFixed(1) + ' ' + units[i];
}

function escapeHtml(str) {
  if (!str) return '';
  return str.replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
}

async function api(path, opts = {}) {
  opts.headers = opts.headers || {};
  const token = localStorage.getItem('zen_token');
  if (token) {
    opts.headers['Authorization'] = 'Bearer ' + token;
  }
  if (!opts.headers['Accept']) {
    opts.headers['Accept'] = 'application/json';
  }
  opts.credentials = 'same-origin';
  if (opts.body && typeof opts.body === 'string' && !opts.headers['Content-Type']) {
    opts.headers['Content-Type'] = 'application/json';
  }

  const res = await fetch(path, opts);
  if (res.status === 401 && !path.includes('api/auth/login')) {
    logout();
    throw new Error('Unauthorized');
  }
  return res;
}

async function copyToClipboard(text, inputEl) {
  if (!text) return false;
  let copied = false;

  // 1. Try modern Async Clipboard API if supported and available (requires Secure Context / HTTPS / localhost)
  if (window.isSecureContext && navigator.clipboard && typeof navigator.clipboard.writeText === 'function') {
    try {
      await navigator.clipboard.writeText(text);
      copied = true;
    } catch (_) {
      // Fallback to execCommand below
    }
  }

  // 2. Fallback to execCommand('copy') - works in non-secure HTTP contexts across all browsers
  if (!copied) {
    try {
      let targetEl = inputEl;
      let createdTemp = false;

      if (!targetEl || typeof targetEl.select !== 'function') {
        targetEl = document.createElement('textarea');
        targetEl.value = text;
        targetEl.setAttribute('readonly', '');
        targetEl.style.position = 'fixed';
        targetEl.style.top = '-9999px';
        targetEl.style.left = '-9999px';
        targetEl.style.opacity = '0';
        document.body.appendChild(targetEl);
        createdTemp = true;
      }

      targetEl.focus();
      targetEl.select();
      if (typeof targetEl.setSelectionRange === 'function') {
        targetEl.setSelectionRange(0, targetEl.value.length);
      }

      copied = document.execCommand('copy');

      if (createdTemp) {
        document.body.removeChild(targetEl);
      }
    } catch (err) {
      console.warn('Fallback execCommand copy failed:', err);
    }
  }

  // 3. Ensure inputEl remains selected if provided, so the user can easily press Ctrl+C if needed
  if (inputEl && typeof inputEl.select === 'function') {
    try {
      inputEl.focus();
      inputEl.select();
      if (typeof inputEl.setSelectionRange === 'function') {
        inputEl.setSelectionRange(0, inputEl.value.length);
      }
    } catch (_) {}
  }

  return copied;
}

