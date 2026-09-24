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
    statsInterval = setInterval(loadStats, 5000);
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
    document.getElementById('stat-active-tunnels').textContent = `${s.activeTunnels} / ${s.totalTunnels}`;
    document.getElementById('stat-total-tunnels').textContent = s.totalTunnels;
    document.getElementById('stat-cpu').textContent = `${Math.round(s.cpuUsagePercent)}% CPU`;
    document.getElementById('stat-ram').textContent = `${fmtBytes(s.memoryUsageBytes)} (${Math.round(s.memoryUsagePercent)}%)`;
    document.getElementById('stat-network-speed').textContent = fmtSpeed(s.uploadRateBytesPerSec + s.downloadRateBytesPerSec);
    document.getElementById('stat-network-up').textContent = fmtSpeed(s.uploadRateBytesPerSec);
    document.getElementById('stat-network-down').textContent = fmtSpeed(s.downloadRateBytesPerSec);
    document.getElementById('stat-traffic').textContent = fmtBytes(s.totalBytesSent + s.totalBytesReceived);
    document.getElementById('stat-upload').textContent = fmtBytes(s.totalBytesSent);
    document.getElementById('stat-download').textContent = fmtBytes(s.totalBytesReceived);
  } catch (err) {
    console.warn('Failed to load stats:', err);
  }
}

// Auto-login check on page load
initApp();
