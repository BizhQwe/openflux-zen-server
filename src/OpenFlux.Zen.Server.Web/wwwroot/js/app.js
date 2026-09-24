// OpenFlux Zen Server - Core App Initialization & Routing

let tunnelsData = [];
let statsInterval = null;

async function doLogin() {
  const u = document.getElementById('login-user').value.trim();
  const p = document.getElementById('login-pass').value;
  if (!u || !p) {
    toast('Введите имя пользователя и пароль', 'danger');
    return;
  }

  try {
    const res = await fetch('api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({ username: u, password: p })
    });
    const data = await res.json();
    if (res.ok && data.success) {
      localStorage.setItem('zen_token', data.token);
      await initApp();
      toast('Успешный вход в панель', 'success');
    } else {
      toast(data.message || 'Неверное имя пользователя или пароль', 'danger');
    }
  } catch (err) {
    toast('Ошибка соединения с сервером', 'danger');
  }
}

async function logout() {
  const token = localStorage.getItem('zen_token');
  localStorage.removeItem('zen_token');

  if (statsInterval) {
    clearInterval(statsInterval);
    statsInterval = null;
  }

  if (token) {
    try {
      await fetch('api/auth/logout', {
        method: 'POST',
        headers: { 'Authorization': 'Bearer ' + token, 'Content-Type': 'application/json' },
        credentials: 'same-origin'
      });
    } catch (_) {}
  }

  document.getElementById('app-header').style.display = 'none';
  document.getElementById('main-view').style.display = 'none';
  document.getElementById('login-view').style.display = 'block';
}

async function checkAuth() {
  const token = localStorage.getItem('zen_token');
  if (!token) return false;
  try {
    const res = await api('api/auth/me');
    if (res.ok) {
      const data = await res.json();
      const secretInput = document.getElementById('setting-secret-path');
      if (secretInput && data.secretPath) {
        secretInput.value = '/' + data.secretPath.replace(/^\/+|\/+$/g, '') + '/';
      }
      const userInput = document.getElementById('setting-username');
      if (userInput && data.username) {
        userInput.value = data.username;
      }
      return true;
    }
  } catch (err) {
    console.warn('Auth check failed:', err);
  }
  return false;
}

async function initApp() {
  const isAuthed = await checkAuth();
  if (!isAuthed) {
    localStorage.removeItem('zen_token');
    if (statsInterval) {
      clearInterval(statsInterval);
      statsInterval = null;
    }
    document.getElementById('app-header').style.display = 'none';
    document.getElementById('main-view').style.display = 'none';
    document.getElementById('login-view').style.display = 'block';
    return;
  }

  document.getElementById('login-view').style.display = 'none';
  document.getElementById('app-header').style.display = 'block';
  document.getElementById('main-view').style.display = 'block';

  loadStats();
  loadTunnels();
  if (!statsInterval) {
    statsInterval = setInterval(() => {
      loadStats();
      loadTunnels(true);
    }, 1000);
  }
}

function switchTab(tabId) {
  document.querySelectorAll('.nav-btn').forEach(b => b.classList.remove('active'));
  document.querySelectorAll('.tab-content').forEach(c => c.classList.remove('active'));
  const activeBtn = Array.from(document.querySelectorAll('.nav-btn')).find(b => b.getAttribute('onclick').includes(tabId));
  if (activeBtn) activeBtn.classList.add('active');
  const tabEl = document.getElementById('tab-' + tabId);
  if (tabEl) tabEl.classList.add('active');

  if (tabId === 'logs') loadActiveLogs();
  if (tabId === 'settings') loadSettings();
}

async function loadStats() {
  try {
    const res = await api('api/stats');
    if (!res.ok) return;
    const s = await res.json();
    const activeEl = document.getElementById('stat-active-tunnels');
    if (activeEl) activeEl.textContent = s.activeTunnels + ' / ' + s.totalTunnels;
    const totalEl = document.getElementById('stat-total-tunnels');
    if (totalEl) totalEl.textContent = s.totalTunnels;
    const cpuEl = document.getElementById('stat-cpu');
    if (cpuEl) cpuEl.textContent = Math.round(s.cpuUsagePercent) + '%';
    const ramEl = document.getElementById('stat-ram');
    if (ramEl) {
      if (s.memoryTotalBytes && s.memoryTotalBytes > 0) {
        ramEl.textContent = `${fmtBytes(s.memoryUsedBytes)} / ${fmtBytes(s.memoryTotalBytes)}`;
      } else {
        ramEl.textContent = fmtBytes(s.memoryUsedBytes);
      }
    }
    const ramPctEl = document.getElementById('stat-ram-pct');
    if (ramPctEl) ramPctEl.textContent = Math.round(s.memoryUsagePercent) + '%';
    const speedDown = s.downloadRateBytesPerSec || 0;
    const speedUp = s.uploadRateBytesPerSec || 0;
    const speedTotal = speedDown + speedUp;
    const speedTotalEl = document.getElementById('stat-speed-total');
    if (speedTotalEl) speedTotalEl.textContent = fmtSpeed(speedTotal);
    const speedUpEl = document.getElementById('stat-speed-up');
    if (speedUpEl) speedUpEl.textContent = fmtSpeed(speedUp);
    const speedDownEl = document.getElementById('stat-speed-down');
    if (speedDownEl) speedDownEl.textContent = fmtSpeed(speedDown);
    const upTraffic = s.totalUploadBytes || 0;
    const downTraffic = s.totalDownloadBytes || 0;
    const totalTraffic = upTraffic + downTraffic;
    const trafficEl = document.getElementById('stat-total-traffic');
    if (trafficEl) trafficEl.textContent = fmtBytes(totalTraffic);
    const uploadEl = document.getElementById('stat-upload');
    if (uploadEl) uploadEl.textContent = fmtBytes(upTraffic);
    const downloadEl = document.getElementById('stat-download');
    if (downloadEl) downloadEl.textContent = fmtBytes(downTraffic);
  } catch (err) {
    console.warn('Failed to load stats:', err);
  }
}

// Auto-login check on page load
initApp();
