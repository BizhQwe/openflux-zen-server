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
