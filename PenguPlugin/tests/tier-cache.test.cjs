const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function fixture() {
  const pending = [];
  const ctx = vm.createContext({
    window: { setTimeout, clearTimeout }, AbortController, URL,
    fetch: url => new Promise((resolve, reject) => pending.push({ url, resolve, reject })),
    document: { querySelectorAll: () => [] },
  });
  vm.runInContext(fs.readFileSync(process.env.PENGU_PLUGIN_SOURCE || path.join(__dirname, '..', 'index.js'), 'utf8')
    .replaceAll('export function ', 'function '), ctx);
  return {
    pending,
    fetch: mode => vm.runInContext(`fetchTierData(${JSON.stringify(mode)})`, ctx),
    tier: () => vm.runInContext('tierCache.get(1)', ctx),
    mode: () => vm.runInContext('tierCacheMode', ctx),
    run: code => vm.runInContext(code, ctx),
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

test('expired cache refreshes and a failed refresh keeps the last successful data', async () => {
  const f = fixture();
  const initial = f.fetch('aram'); f.resolve(0, 2); await initial;
  f.run('tierUpdatedAt = Date.now() - CACHE_TTL - 1');
  const refresh = f.fetch('aram');
  assert.equal(f.pending.length, 2);
  assert.equal(f.tier(), 'T2');
  f.pending[1].resolve({ ok: false, status: 503 });
  await assert.rejects(refresh, /503/);
  assert.equal(f.tier(), 'T2');
  const retry = f.fetch('aram'); f.resolve(2, 1); await retry;
  assert.equal(f.tier(), 'T1');
});

test('unsupported queues stay unknown instead of borrowing ranked data', () => {
  const f = fixture();
  assert.equal(f.run('modeFromQueue({ id: 450 })'), 'aram');
  assert.equal(f.run('modeFromQueue({ gameMode: "CHERRY" })'), 'arena');
  assert.equal(f.run('modeFromQueue({ id: 1900 })'), 'urf');
  assert.equal(f.run('modeFromQueue({ id: 420 })'), 'ranked');
  assert.equal(f.run('modeFromQueue({ id: 99999, gameMode: "UNKNOWN" })'), '');
});

test('missing tier is not converted into an OP badge', async () => {
  const f = fixture();
  const request = f.fetch('aram');
  f.pending[0].resolve({ ok: true, json: async () => ({ data: [{ id: 1, average_stats: { tier: null } }] }) });
  await request;
  assert.equal(f.tier(), undefined);
});


test('player statistics use matching identity and mode, exclude invalid games, and deduplicate', () => {
  const f = fixture();
  const result = f.run(`playerSummary([
    { gameId: 1, gameDuration: 1200, queueId: 450, participantIdentities: [{ participantId: 2, player: { puuid: 'a' } }], participants: [
      { participantId: 1, stats: { kills: 99, deaths: 1, assists: 99, win: false } },
      { participantId: 2, stats: { kills: 3, deaths: 0, assists: 6, win: true } }] },
    { gameId: 1, gameDuration: 1200, queueId: 450, participants: [{ stats: { kills: 3, deaths: 0, assists: 6, win: true } }] },
    { gameId: 2, gameDuration: 1200, queueId: 420, participants: [{ stats: { kills: 0, deaths: 9, assists: 0, win: false } }] },
    { gameId: 3, gameDuration: 120, queueId: 450, participants: [{ stats: { kills: 0, deaths: 9, assists: 0, win: false } }] },
    { gameId: 4, gameDuration: 1200, queueId: 450, participants: [{ stats: { win: true } }] }
  ], 'a', 'aram')`);
  assert.deepEqual(JSON.parse(JSON.stringify(result)), { kda: '9.00', rate: 100, count: 1, scope: '同模式' });
  assert.equal(f.run(`playerSummary([], 'a', 'aram')`), null);
  assert.equal(f.run(`playerSummary([{ gameDuration: 900, queueId: 420, participants: [{stats: {kills: 1, deaths: 2, assists: 3, win: false}}] }], 'a', 'aram').scope`), '全模式');
});

test('party groups stay within a team and missing party metadata stays unknown', () => {
  const f = fixture();
  assert.equal(f.run(`partyLabel({ partyId: '0' }, []).text`), '开黑未知');
  assert.equal(f.run(`partyLabel({ partyId: 'x' }, [{partyId:'x'}, {partyId:'x'}]).text`), '开黑1 · 2人');
  assert.equal(f.run(`partyLabel({ partyId: 'x' }, [{partyId:'x'}]).text`), '未见组队');
});


test('Unsplash photo pages are distinguished from actual image URLs', () => {
  const f = fixture();
  assert.equal(f.run(`isWallpaperPage('https://unsplash.com/photos/vernazza-coastal-village-in-italy-cYrMQA7a3Wc')`), true);
  assert.equal(f.run(`isWallpaperPage('https://images.unsplash.com/photo-1499678329028-101435549a4e?auto=format')`), false);
  assert.equal(f.run(`validWallpaper('javascript:alert(1)')`), '');
});
