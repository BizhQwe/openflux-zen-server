const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const crypto = require('node:crypto');
const {spawnSync} = require('node:child_process');

test('release packaging contains only installers and checksums and removes stale bootstrap assets', {skip: process.platform !== 'linux'}, t => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'openflux-package-test-'));
  t.after(() => fs.rmSync(root, {recursive: true, force: true}));
  const scripts = path.join(root, 'scripts');
  const release = path.join(root, 'release');
  fs.mkdirSync(scripts);
  fs.mkdirSync(release);
  fs.copyFileSync(path.resolve(__dirname, '../package-release.sh'), path.join(scripts, 'package-release.sh'));
  const targetsFile = path.resolve(__dirname, '../targets.txt');
  fs.copyFileSync(targetsFile, path.join(scripts, 'targets.txt'));
  const targets = fs.readFileSync(targetsFile, 'utf8').trim().split(/\r?\n/);
  const expected = ['SHA256SUMS.txt'];
  const installers = [];
  for (const target of targets) {
    const asset = `openflux-zen-server-installer-${target}${target.startsWith('windows-') ? '.exe' : ''}`;
    const payload = Buffer.from(`dummy ${target}`);
    fs.writeFileSync(path.join(release, asset), payload);
    fs.writeFileSync(path.join(release, asset + '.sha256'), crypto.createHash('sha256').update(payload).digest('hex') + '  ' + asset + '\n');
    expected.push(asset, asset + '.sha256');
    installers.push(asset);
  }
  for (const name of ['install.sh', 'install.ps1', 'install.sh.sha256', 'install.ps1.sha256']) {
    fs.writeFileSync(path.join(release, name), 'stale bootstrap asset');
  }
  const result = spawnSync('/bin/sh', [path.join(scripts, 'package-release.sh'), '--skip-build', release], {
    encoding: 'utf8', timeout: 10000, maxBuffer: 64 * 1024,
    env: {PATH: '/usr/bin:/bin', HOME: root, TMPDIR: root, LC_ALL: 'C'},
  });
  assert.equal(result.error, undefined);
  assert.equal(result.status, 0, result.stderr);
  assert.deepEqual(fs.readdirSync(release).sort(), expected.sort());
  const aggregate = fs.readFileSync(path.join(release, 'SHA256SUMS.txt'), 'utf8').trim().split('\n');
  assert.equal(aggregate.length, targets.length);
  for (const line of aggregate) {
    const [digest, name] = line.split(/\s+/);
    assert.ok(installers.includes(name));
    assert.equal(digest, crypto.createHash('sha256').update(fs.readFileSync(path.join(release, name))).digest('hex'));
  }
});
