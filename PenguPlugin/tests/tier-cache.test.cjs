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
  assert.equal(f.run('modeFromQueue({ id: 3140, gameMode: "PRACTICETOOL" })'), 'ranked');
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

test('party labels omit missing and solo metadata and distinguish confirmed groups', () => {
  const f = fixture();
  assert.equal(f.run(`partyLabels([{partyId:'0'}, {partyId:'00000000-0000-0000-0000-000000000000'}]).size`), 0);
  assert.equal(f.run(`[...partyLabels([{partyId:'x'}, {partyId:'x'}]).values()][0].text`), '开黑1 · 2人');
  assert.equal(f.run(`partyLabels([{partyId:'x'}, {partyId:'y'}]).size`), 0);
});

test('premade fallback merges owner-only histories and requires two entire-group same-team matches', () => {
  const f = fixture();
  f.run(`
    function sample(puuid, gameId, teamId = 100, overrides = {}) {
      return {gameId, gameType:'MATCHED_GAME', gameDuration:1200,
        participantIdentities:[{participantId:1,player:{puuid}}], participants:[{participantId:1,teamId,stats:{}}], ...overrides};
    }
    const team = ['alice','bob','carol','dan'].map(puuid => ({puuid}));
    playerHistory.set('alice',{games:[sample('alice',1),sample('alice',2),sample('alice',1)]});
    playerHistory.set('bob',{games:[sample('bob',1),sample('bob',2),sample('bob',3),sample('bob',4)]});
    playerHistory.set('carol',{games:[sample('carol',3),sample('carol',4)]});
    playerHistory.set('dan',{games:[sample('dan',1,200),sample('dan',2,200)]});
  `);
  assert.equal(f.run(`partyLabels(team).get(team[0]).text`), '疑似开黑1 · 2人');
  assert.equal(f.run(`partyLabels(team).get(team[1]).text`), '疑似开黑1 · 2人');
  assert.equal(f.run(`partyLabels(team).has(team[2])`), false); // A-B and B-C do not imply a trio.
  assert.equal(f.run(`partyLabels(team).has(team[3])`), false); // Same matches, opposing side.
  f.run(`playerHistory.set('bob',{games:[sample('bob',1),sample('bob',1)]});`);
  assert.equal(f.run(`partyLabels(team).size`), 0); // Duplicates cannot reach the threshold.
  f.run(`playerHistory.set('bob',{games:[sample('bob',1),sample('bob',2,100,{gameType:'CUSTOM_GAME'})]});`);
  assert.equal(f.run(`partyLabels(team).size`), 0);
  f.run(`playerHistory.set('bob',{games:[sample('bob',1),sample('bob',2)]}); team[0].partyId='a'; team[1].partyId='b';`);
  assert.equal(f.run(`partyLabels(team).size`), 0); // Explicit different parties override inference.
  f.run(`delete team[0].partyId; delete team[1].partyId;`);
  assert.equal(f.run(`partyLabels([team[0]]).size`), 0); // Never join members of different current teams.
});


test('premade fallback keeps two separate duos separate and excludes remakes and conflicting summaries', () => {
  const f = fixture();
  f.run(`
    const members = ['a','b','c','d','e'].map(puuid => ({puuid}));
    function record(id, puuid, teamId = 100) {
      return {gameId:id,gameType:'MATCHED_GAME',gameDuration:1200,
        participantIdentities:[{participantId:1,player:{puuid}}],participants:[{participantId:1,teamId,stats:{}}]};
    }
    for (const [puuid,ids] of [['a',[1,2]],['b',[1,2]],['c',[3,4]],['d',[3,4]],['e',[]]])
      playerHistory.set(puuid,{games:ids.map(id=>record(id,puuid))});
  `);
  assert.equal(f.run(`partyLabels(members).size`), 4);
  assert.equal(f.run(`partyLabels(members).get(members[0]).group === partyLabels(members).get(members[1]).group`), true);
  assert.equal(f.run(`partyLabels(members).get(members[0]).group === partyLabels(members).get(members[2]).group`), false);
  f.run(`playerHistory.get('b').games[1].participants[0].stats.gameEndedInEarlySurrender = true;`);
  assert.equal(f.run(`partyLabels(members).has(members[0])`), false);
  f.run(`playerHistory.get('b').games[1] = record(2,'b'); playerHistory.get('a').games.push(record(2,'b',200));`);
  assert.equal(f.run(`partyLabels(members).has(members[0])`), false);
});

test('Unsplash photo pages are distinguished from actual image URLs', () => {
  const f = fixture();
  assert.equal(f.run(`isWallpaperPage('https://unsplash.com/photos/vernazza-coastal-village-in-italy-cYrMQA7a3Wc')`), true);
  assert.equal(f.run(`isWallpaperPage('https://images.unsplash.com/photo-1499678329028-101435549a4e?auto=format')`), false);
  assert.equal(f.run(`validWallpaper('javascript:alert(1)')`), '');
});
