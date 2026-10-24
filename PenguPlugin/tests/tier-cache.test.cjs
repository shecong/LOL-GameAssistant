const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function fixture() {
  const pending = [];
  const ctx = vm.createContext({
    fetch: url => new Promise((resolve, reject) => pending.push({ url, resolve, reject })),
    document: { querySelectorAll: () => [] },
  });
  vm.runInContext(fs.readFileSync(path.join(__dirname, '..', 'index.js'), 'utf8')
    .replaceAll('export function ', 'function '), ctx);
  return {
    pending,
    fetch: mode => vm.runInContext(`fetchTierData(${JSON.stringify(mode)})`, ctx),
    tier: () => vm.runInContext('tierCache.get(1)', ctx),
    mode: () => vm.runInContext('tierCacheMode', ctx),
    resolve(index, tier, empty = false) {
      pending[index].resolve({ ok: true, json: async () => ({ data: empty ? [] : [{
        id: 1, positions: [{ stats: { tier_data: { tier } } }], average_stats: { tier },
      }] }) });
    },
  };
}

test('late ranked response cannot replace completed aram cache', async () => {
  const f = fixture();
  const ranked = f.fetch('ranked');
  const aram = f.fetch('aram');
  f.resolve(1, 1); await aram;
  f.resolve(0, 5); await ranked;
  assert.equal(f.mode(), 'aram'); assert.equal(f.tier(), 'T1');
  await f.fetch('aram'); assert.equal(f.pending.length, 2);
});

test('A-B-A switching ignores both older requests', async () => {
  const f = fixture();
  const oldA = f.fetch('ranked'); const b = f.fetch('aram'); const a = f.fetch('ranked');
  f.resolve(2, 2); await a;
  f.resolve(1, 3); await b;
  f.resolve(0, 5); await oldA;
  assert.equal(f.mode(), 'ranked'); assert.equal(f.tier(), 'T2');
});

test('failed new mode clears old data and can be retried', async () => {
  const f = fixture();
  const a = f.fetch('ranked'); f.resolve(0, 5); await a;
  const b = f.fetch('aram');
  assert.equal(f.tier(), undefined);
  f.pending[1].resolve({ ok: false, status: 503 });
  await assert.rejects(b, /503/);
  assert.equal(f.mode(), ''); assert.equal(f.tier(), undefined);
  const retry = f.fetch('aram'); f.resolve(2, 1); await retry;
  assert.equal(f.tier(), 'T1');
});

test('same-mode requests share pending work and empty success is cached', async () => {
  const f = fixture();
  const a = f.fetch('aram'); const b = f.fetch('aram');
  assert.equal(f.pending.length, 1);
  f.resolve(0, 0, true); await Promise.all([a, b]);
  await f.fetch('aram');
  assert.equal(f.pending.length, 1); assert.equal(f.mode(), 'aram');
  assert.equal(f.tier(), undefined);
});

test('a failed obsolete request does not clear the newer pending request', async () => {
  const f = fixture();
  const old = f.fetch('ranked'); const current = f.fetch('aram');
  f.pending[0].reject(new Error('offline')); await assert.rejects(old, /offline/);
  const duplicate = f.fetch('aram'); assert.equal(f.pending.length, 2);
  f.resolve(1, 1); await Promise.all([current, duplicate]);
  assert.equal(f.tier(), 'T1');
});
