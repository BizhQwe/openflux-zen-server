const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const {spawnSync} = require('node:child_process');

const bootstrap = path.resolve(__dirname, '../install.sh');

function fixture(t, scenario) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'openflux-bootstrap-test-'));
  t.after(() => fs.rmSync(root, {recursive: true, force: true}));
  const bin = path.join(root, 'bin');
  fs.mkdirSync(bin);
  fs.writeFileSync(path.join(bin, 'id'), '#!/bin/sh\nif [ "$1" = -u ]; then echo 0; else exec /usr/bin/id "$@"; fi\n', {mode: 0o700});
  fs.writeFileSync(path.join(root, 'dummy-installer'), `#!/usr/bin/node
require('node:fs').writeFileSync(require('node:path').join(process.env.OPENFLUX_BOOTSTRAP_FIXTURE, 'executed.json'), JSON.stringify(process.argv.slice(2)));
`, {mode: 0o700});
  fs.writeFileSync(path.join(bin, 'curl'), `#!/usr/bin/node
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const root = process.env.OPENFLUX_BOOTSTRAP_FIXTURE;
const scenario = process.env.OPENFLUX_BOOTSTRAP_CASE;
const args = process.argv.slice(2);
const output = args[args.indexOf('-o') + 1];
const url = args.find(arg => arg.startsWith('https://github.com/'));
if (!output || !url) process.exit(2);
fs.appendFileSync(path.join(root, 'downloads.jsonl'), JSON.stringify(output) + '\\n');
const payload = fs.readFileSync(path.join(root, 'dummy-installer'));
if (url.endsWith('.sha256')) {
  if (scenario === 'missing') process.exit(22);
  const digest = scenario === 'mismatch' ? '0'.repeat(64) : crypto.createHash('sha256').update(payload).digest('hex');
  const filename = scenario === 'wrong-name' ? 'other-installer' : path.basename(url).replace(/\\.sha256$/, '');
  fs.writeFileSync(output, digest + '  ' + filename + '\\n');
} else {
  fs.writeFileSync(output, payload);
}
`, {mode: 0o700});
  return {
    root,
    run(args = []) {
      return spawnSync('/bin/sh', [bootstrap, ...args], {
        encoding: 'utf8', timeout: 10000, maxBuffer: 64 * 1024,
        env: {
          PATH: bin + ':/usr/bin:/bin', HOME: root, LC_ALL: 'C',
          OPENFLUX_BOOTSTRAP_FIXTURE: root, OPENFLUX_BOOTSTRAP_CASE: scenario,
        },
      });
    },
    assertDownloadsCleaned() {
      const downloads = fs.readFileSync(path.join(root, 'downloads.jsonl'), 'utf8').trim().split('\n').map(JSON.parse);
      for (const output of downloads) assert.equal(fs.existsSync(path.dirname(output)), false, 'temporary download directory retained');
    },
  };
}

test('Linux bootstrap executes only the verified installer and preserves literal arguments', {skip: process.platform !== 'linux'}, t => {
  const setup = fixture(t, 'matching');
  const args = ['--silent', '--dir', '/scratch/install with spaces;$(must-stay-literal)'];
  const result = setup.run(args);
  assert.equal(result.error, undefined);
  assert.equal(result.status, 0, result.stderr);
  assert.deepEqual(JSON.parse(fs.readFileSync(path.join(setup.root, 'executed.json'))), args);
  setup.assertDownloadsCleaned();
});

for (const scenario of ['mismatch', 'missing', 'wrong-name']) {
  test(`Linux bootstrap refuses ${scenario} checksum without execution`, {skip: process.platform !== 'linux'}, t => {
    const setup = fixture(t, scenario);
    const result = setup.run(['--silent']);
    assert.equal(result.error, undefined);
    assert.notEqual(result.status, 0);
    assert.equal(fs.existsSync(path.join(setup.root, 'executed.json')), false, 'unverified installer executed');
    setup.assertDownloadsCleaned();
  });
}
