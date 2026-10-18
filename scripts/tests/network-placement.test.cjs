const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '../../src/OpenFlux.Zen.Server.Web/internal/panel/web/js/pages/settings.js'), 'utf8');

function setup(href = 'http://panel.example.test:5000/secret/') {
  const nodes = new Map();
  for (const id of ['setting-domain-input', 'setting-panel-port', 'url-placement-domain', 'domain-tls-status', 'btn-save-network-mode', 'btn-save-network-spinner']) {
    nodes.set(id, {value: '', style: {}, disabled: false, textContent: ''});
  }
  nodes.get('setting-domain-input').value = 'panel.example.test';
  nodes.get('setting-panel-port').value = '5000';
  const location = new URL(href);
  const copy = {disabled: false};
  let poll;
  const context = vm.createContext({
    URL, window: {location}, currentLanguage: 'en', console: {error() {}},
    document: {getElementById: id => nodes.get(id) || null, querySelector: () => copy},
    t: key => key, toast() {},
    setInterval: callback => { poll = callback; return 1; }, clearInterval() {},
  });
  vm.runInContext(source, context);
  const previous = {domain: 'panel.example.test', domainEnabled: true, panelPort: '5000', requestScheme: 'http', domainTLSState: 'needs_certificate', domainUrl: ''};
  context.previous = previous;
  vm.runInContext('networkPlacementInfo = previous', context);
  return {context, nodes, copy, location, previous, run: code => vm.runInContext(code, context), tick: () => poll()};
}

test('a domain without TLS has no copyable HTTP address', () => {
  const ui = setup();
  ui.run('updateDomainPreviewUrl()');
  assert.equal(ui.nodes.get('url-placement-domain').value, '');
  assert.equal(ui.copy.disabled, true);
});

test('only an applied domain has a usable HTTPS link', () => {
  const ui = setup();
  Object.assign(ui.previous, {domainTLSState: 'ready', domainUrl: 'https://panel.example.test:5000/secret/'});
  ui.run('updateDomainPreviewUrl()');
  assert.equal(ui.nodes.get('url-placement-domain').value, ui.previous.domainUrl);
  assert.equal(ui.copy.disabled, false);
  ui.nodes.get('setting-domain-input').value = 'other.example.test';
  ui.run('updateDomainPreviewUrl()');
  assert.equal(ui.nodes.get('url-placement-domain').value, '');
  assert.equal(ui.copy.disabled, true);
});

test('the direct domain upgrades to HTTPS and keeps the configured port', () => {
  const ui = setup();
  ui.context.next = {...ui.previous, panelPort: '5443', domainUrl: 'https://panel.example.test:5443/secret/'};
  assert.equal(ui.run('redirectAfterPlacement(next, previous)'), true);
  assert.equal(ui.location.href, 'https://panel.example.test:5443/secret/');
});

test('LAN access stays HTTP while following the new listener port', () => {
  const ui = setup('http://192.168.1.2:5000/secret/');
  ui.context.next = {...ui.previous, panelPort: '5443', domainUrl: 'https://panel.example.test:5443/secret/'};
  assert.equal(ui.run('redirectAfterPlacement(next, previous)'), true);
  assert.equal(ui.location.href, 'http://192.168.1.2:5443/secret/');
});

test('a proxy URL does not follow the internal listener port', () => {
  const ui = setup('https://proxy.example.test/secret/');
  ui.context.next = {...ui.previous, domain: 'https://proxy.example.test', panelPort: '5443'};
  assert.equal(ui.run('redirectAfterPlacement(next, previous)'), false);
  assert.equal(ui.location.href, 'https://proxy.example.test/secret/');
});

test('prepared TLS is committed before redirecting to the new endpoint', async () => {
  const ui = setup();
  const calls = [];
  ui.context.api = async (route, options = {}) => {
    calls.push({route, options});
    if (options.method === 'POST') {
      assert.deepEqual(JSON.parse(options.body), {domainSetupID: 'prepared-job'});
      return {ok: true, json: async () => ({...ui.previous, success: true, panelPort: '5443', domainUrl: 'https://panel.example.test:5443/secret/'})};
    }
    return {ok: true, json: async () => ({...ui.previous, domainSetup: {id: 'prepared-job', state: 'ready'}})};
  };
  ui.run('startDomainSetupPolling()');
  await ui.tick();
  assert.equal(calls.length, 2);
  assert.equal(ui.location.href, 'https://panel.example.test:5443/secret/');
});

test('a failed certificate keeps the current address and offers retry', async () => {
  const ui = setup();
  let commits = 0;
  ui.context.api = async (route, options = {}) => {
    if (options.method === 'POST') commits++;
    return {ok: true, json: async () => ({...ui.previous, domainSetup: {state: 'failed', error: 'ACME refused'}})};
  };
  ui.run('startDomainSetupPolling()');
  await ui.tick();
  assert.equal(commits, 0);
  assert.equal(ui.location.href, 'http://panel.example.test:5000/secret/');
  assert.equal(ui.nodes.get('domain-tls-status').textContent, 'ACME refused');
  assert.equal(ui.nodes.get('btn-save-network-mode').disabled, false);
});
