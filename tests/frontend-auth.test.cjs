const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { test } = require('node:test');
const vm = require('node:vm');

const html = readFileSync(join(__dirname, '../src/OpenFlux.Zen.Server.Web/wwwroot/index.html'), 'utf8');
const source = html.match(/<script>([\s\S]*?)<\/script>/)[1];

// Compile the entire shipped script: a syntax error anywhere disables login.
test('the complete frontend script parses', () => {
  assert.doesNotThrow(() => new vm.Script(source));
});

async function createApp({ token, loginStatus = 200, sessionStatus = 200 } = {}) {
  const storage = new Map(token ? [['zen_token', token]] : []);
  const elements = new Map([...html.matchAll(/id="([^"]+)"/g)].map(([, id]) => [id, {
    value: '', style: {}, textContent: '', innerHTML: '', options: [], children: [],
    appendChild(child) { this.children.push(child); }
  }]));
  const requests = [];
  const timers = new Set();
  const context = vm.createContext({
    document: {
      getElementById: id => elements.get(id),
      createElement: () => ({ remove() {} })
    },
    localStorage: {
      getItem: key => storage.get(key) ?? null,
      setItem: (key, value) => storage.set(key, value),
      removeItem: key => storage.delete(key)
    },
    console: { warn() {} },
    setTimeout() {},
    setInterval(fn) { timers.add(fn); return fn; },
    clearInterval(fn) { timers.delete(fn); },
    fetch: async (url, options) => {
      requests.push({ url, options });
      let status = 200;
      let data = {};
      if (url === 'api/auth/login') {
        status = loginStatus;
        data = status === 200 ? { success: true, token: 'test-token' } : { success: false, message: 'Invalid credentials' };
      } else if (url === 'api/auth/me') {
        status = sessionStatus;
        data = { username: 'test-admin', secretPath: 'test-panel' };
      } else if (url === 'api/tunnels') {
        data = [];
      } else if (url === 'api/stats') {
        data = { activeTunnels: 0, totalTunnels: 0, cpuUsagePercent: 0, memoryUsedBytes: 0,
          memoryTotalBytes: 0, memoryUsagePercent: 0, totalUploadBytes: 0, totalDownloadBytes: 0 };
      } else if (url !== 'api/auth/logout') {
        throw new Error(`Unexpected request: ${url}`);
      }
      return { status, ok: status === 200, json: async () => data };
    }
  });
  // The script's final expression is initApp(), including the real startup flow.
  await new vm.Script(source).runInContext(context);
  await new Promise(resolve => setImmediate(resolve));
  return { context, storage, elements, requests, timers };
}

test('successful login sends credentials and opens the panel; logout clears the session', async () => {
  const app = await createApp();
  const el = id => app.elements.get(id);
  assert.equal(el('login-view').style.display, 'block');
  el('login-user').value = ' test-admin ';
  el('login-pass').value = 'test-password';
  await app.context.doLogin();
  assert.equal(app.storage.get('zen_token'), 'test-token');
  assert.equal(el('login-view').style.display, 'none');
  assert.equal(el('main-view').style.display, 'block');
  assert.equal(el('app-header').style.display, 'block');
  const login = app.requests.find(r => r.url === 'api/auth/login');
  assert.equal(login.options.method, 'POST');
  assert.equal(login.options.credentials, 'same-origin');
  assert.deepEqual(JSON.parse(login.options.body), { username: 'test-admin', password: 'test-password' });
  assert.equal(app.requests.find(r => r.url === 'api/auth/me').options.headers.Authorization, 'Bearer test-token');
  assert.equal(app.timers.size, 1);
  await app.context.logout();
  assert.equal(app.storage.has('zen_token'), false);
  assert.equal(el('main-view').style.display, 'none');
  assert.equal(el('login-view').style.display, 'block');
  assert.equal(app.timers.size, 0);
});

test('invalid credentials keep the login visible and display the error', async () => {
  const app = await createApp({ loginStatus: 401 });
  app.elements.get('login-user').value = 'test-admin';
  app.elements.get('login-pass').value = 'wrong';
  await app.context.doLogin();
  assert.equal(app.storage.has('zen_token'), false);
  assert.equal(app.elements.get('login-view').style.display, 'block');
  assert.equal(app.elements.get('main-view').style.display, 'none');
  assert.equal(app.elements.get('toast-container').children.at(-1).textContent, 'Invalid credentials');
});

test('a saved valid session opens the panel on page load', async () => {
  const app = await createApp({ token: 'saved-token' });
  assert.equal(app.elements.get('main-view').style.display, 'block');
  assert.equal(app.elements.get('setting-username').value, 'test-admin');
  assert.equal(app.elements.get('setting-secret-path').value, '/test-panel/');
});

test('an expired session returns to login and clears the token', async () => {
  const app = await createApp({ token: 'expired-token', sessionStatus: 401 });
  assert.equal(app.storage.has('zen_token'), false);
  assert.equal(app.elements.get('login-view').style.display, 'block');
  assert.equal(app.elements.get('main-view').style.display, 'none');
  assert.equal(app.timers.size, 0);
});
