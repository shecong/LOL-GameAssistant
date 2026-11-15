const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
let browser;
before(async () => { browser = await chromium.launch({ headless: true, channel: process.env.PENGU_TEST_BROWSER_CHANNEL || undefined }); });
after(async () => { await browser?.close(); });

const source = fs.readFileSync(process.env.PENGU_PLUGIN_SOURCE || path.join(__dirname, '..', 'index.js'), 'utf8');
const moduleUrl = 'data:text/javascript;base64,' + Buffer.from(source).toString('base64');
const root = '#lol-game-assistant-visual-root';
const badge = '[data-lol-game-assistant-tier-badge]';
const html = `<!doctype html><html><head><meta charset="utf-8"><style>
body {margin:0;background:#091422;color:#dce9f7;font:16px Arial} header{padding:25px;background:#13273a}
main{padding:48px} .grid{display:flex;gap:22px;margin:30px 0}.champion-grid-champion-thumbnail,.bench-champion-icon{width:90px;height:90px;background:linear-gradient(135deg,#23536a,#172d46);display:grid;place-items:center;border:1px solid #658b9b}
</style></head><body><header class="navigation-bar">LEAGUE CLIENT · 插件 DOM 验证环境</header><main><h1>选择你的英雄</h1><p>验证动态头像角标、自动模式和视觉设置</p><div class="grid">
<div id="hero" class="champion-grid-champion-thumbnail" data-champion-id="1">安妮</div>
<div class="bench-champion-icon" data-champion-id="2">奥拉夫</div></div></main></body></html>`;

async function fixture(t, options = {}) {
  const page = await browser.newPage({ viewport: { width: 1280, height: 720 } });
  t.after(() => page.close());
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  let requests = 0;
  await page.route('**/*', async route => {
    const url = route.request().url();
    if (url.includes('/wallpaper.svg')) return route.fulfill({ contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="720"><rect width="1280" height="720" fill="#c33960"/></svg>' });
    if (url.includes('/broken-wallpaper.png')) return route.fulfill({ status: 404, body: '' });
    if (url.includes('/champion-summary.json')) return route.fulfill({ json: [ { id: 1, name: '安妮' }, { id: 2, name: '奥拉夫' }, { id: 3, name: '加里奥' } ] });
    if (url.includes('/session/actions/')) {
      (options.patches ||= []).push({ url, method: route.request().method(), body: route.request().postDataJSON() });
      return route.fulfill({ status: options.patchError ? 409 : 204, body: '' });
    }
    if (url.includes('/pickable-champion-ids')) {
      if (options.pickGate) await options.pickGate;
      return route.fulfill({ json: options.pickable || [1, 2, 3] });
    }
    if (url.includes('/disabled-champion-ids')) return route.fulfill({ json: options.disabled || [] });
    if (url.includes('/counters?')) {
      options.counterRequests = (options.counterRequests || 0) + 1;
      if (options.counterGate) await options.counterGate;
      return route.fulfill({ status: options.counterError ? 503 : 200, json: { data: options.counterData || [
        { champion_id: 2, play: 200, win: 80 }, { champion_id: 3, play: 300, win: 180 },
        { champion_id: 4, play: 2, win: 0 }, { champion_id: 5, play: 300, win: null },
      ] } });
    }
    if (url.includes('lol-api-champion.op.gg')) {
      requests++;
      if (options.failFirst && requests === 1) return route.fulfill({ status: 503, body: '{}' });
      const tier = url.includes('/ranked?') ? 3 : 1;
      return route.fulfill({ json: { data: [1, 2].map(id => ({ id, average_stats: { tier }, positions: [{ name: 'MID', stats: { play: 1000, tier_data: { tier } } }, { name: 'TOP', stats: { play: 200, tier_data: { tier } } }] })) } });
    }
    if (url.includes('/lol-champ-select/v1/session')) return route.fulfill({ status: options.playerSession ? 200 : 404, json: options.playerSession || {} });
    if (url.includes('/lol-match-history/')) {
      options.historyRequests = (options.historyRequests || 0) + 1;
      if (options.historyGate) await options.historyGate;
      const puuid = decodeURIComponent(new URL(url).pathname.split('/').at(-2));
      return route.fulfill({ status: options.historyError ? 503 : 200, json: { games: { games: options.histories?.[puuid] || options.history || [] } } });
    }
    if (url.includes('/lol-gameflow/v1/session')) return route.fulfill({ json: { gameData: { queue: options.queue || { id: 450, gameMode: 'ARAM' } } } });
    if (url.includes('/lol-lobby/v2/lobby')) return route.fulfill({ json: { gameConfig: { queueId: 450 } } });
    return route.fulfill({ contentType: 'text/html', body: html });
  });
  await page.goto('http://client.test');
  await page.evaluate(async ({ url, managed }) => {
    window.runtimeWrites = [];
    window.plugin = await import(url);
    window.controlText = managed ? JSON.stringify(managed) : '';
    const visualControl = managed ? {
      readConfig: () => window.controlText,
      reportStatus: content => { window.runtimeWrites.push(JSON.parse(content)); return true; },
    } : undefined;
    window.plugin.init({ visualControl, fs: { write: async (name, content) => { window.runtimeWrites.push(JSON.parse(content)); return true; } } });
    window.plugin.load();
  }, { url: moduleUrl, managed: options.managed });
  return { page, errors, requests: () => requests };
}

test('mounts actual DOM, follows mode, renders badges, persists settings and unloads cleanly', async t => {
  const { page, errors } = await fixture(t);
  await page.waitForFunction(() => document.querySelectorAll('[data-lol-game-assistant-tier-badge]').length === 2);
  assert.equal(await page.locator('#hero ' + badge).textContent(), 'T1');
  assert.equal(await page.locator('#hero').evaluate(node => node.style.position), 'relative');
  await page.locator(root + ' .lga-toggle').click();
  await page.locator('[data-key="tierMode"]').selectOption('ranked');
  await page.waitForFunction(() => document.querySelector('#hero [data-lol-game-assistant-tier-badge]')?.textContent === 'T3');
  await page.locator('[data-key="showBadges"]').uncheck();
  assert.equal(await page.locator(badge).count(), 0);
  assert.equal(await page.locator('#hero').evaluate(node => node.style.position), '');
  await page.locator('[data-key="showBadges"]').check();
  await page.waitForSelector(badge);
  await page.locator('[data-key="hideNavigation"]').check();
  assert.equal(await page.locator('header').isVisible(), false);
  await page.evaluate(() => window.plugin.load());
  assert.equal(await page.locator(root).count(), 1);
  await page.evaluate(() => window.plugin.unload());
  assert.equal(await page.locator(root).count(), 0);
  assert.equal(await page.locator(badge).count(), 0);
  assert.equal(await page.locator('#hero').evaluate(node => node.style.position), '');
  assert.equal(await page.locator('header').isVisible(), true);
  await page.waitForTimeout(350);
  assert.equal(await page.locator(root).count(), 0);
  assert.equal(await page.evaluate(() => window.runtimeWrites.at(-1).active), false);
  assert.deepEqual(errors, []);
});

test('reused champion nodes update and a replaced client root remounts the panel', async t => {
  const { page, errors } = await fixture(t);
  await page.waitForSelector(badge);
  await page.locator('#hero').evaluate(node => node.setAttribute('data-champion-id', '9999'));
  await page.waitForFunction(() => !document.querySelector('#hero [data-lol-game-assistant-tier-badge]'));
  assert.equal(await page.locator('#hero').evaluate(node => node.style.position), '');
  await page.locator('#hero').evaluate(node => node.setAttribute('data-champion-id', '2'));
  await page.waitForSelector('#hero ' + badge);
  await page.locator(root).evaluate(node => node.remove());
  await page.waitForSelector(root);
  await page.locator(root + ' .lga-toggle').click();
  await page.waitForFunction(() => document.querySelector('.lga-status')?.textContent.includes('显示 2 个角标'));
  const output = path.join(__dirname, '..', 'test-results');
  fs.mkdirSync(output, { recursive: true });
  await page.screenshot({ path: path.join(output, 'client-preview.png') });
  assert.deepEqual(errors, []);
});

test('failed tier request recovers automatically without another user interaction', async t => {
  const fixtureState = await fixture(t, { failFirst: true });
  const { page, errors } = fixtureState;
  await page.waitForFunction(() => document.querySelector('.lga-status')?.textContent.includes('503'));
  await page.waitForSelector(badge, { timeout: 8000 });
  assert.ok(fixtureState.requests() >= 2);
  assert.deepEqual(errors, []);
});

test('client bootstrap cloning panel markup restores interactive event handlers', async t => {
  const { page, errors } = await fixture(t);
  await page.locator(root).evaluate(node => node.replaceWith(node.cloneNode(true)));
  await page.locator(root + ' .lga-toggle').click();
  await page.waitForFunction(() => document.querySelector('.lga-panel')?.hidden === false);
  await page.locator('[data-key="showBadges"]').uncheck();
  assert.equal(await page.locator(badge).count(), 0);
  assert.deepEqual(errors, []);
});


const playerPanel = '[data-lol-game-assistant-player-stats]';
function match(puuid, id, overrides = {}) {
  return { gameId: id, queueId: 450, gameDuration: 1200, gameType: 'MATCHED_GAME',
    participantIdentities: [{ participantId: 2, player: { puuid } }],
    participants: [
      { participantId: 1, stats: { kills: 99, deaths: 1, assists: 99, win: false } },
      { participantId: 2, stats: { kills: 4, deaths: 2, assists: 8, win: true } },
    ], ...overrides };
}

test('roster information follows cell identity, supports hidden players and leaves grid alone', async t => {
  const options = { playerSession: { myTeam: [
    { cellId: 0, championId: 1, puuid: 'alice', partyId: 'party-a' },
    { cellId: 1, championId: 2, puuid: 'bob', partyId: 'party-a' },
  ], theirTeam: [{ cellId: 5, championId: 1, puuid: '' }] }, history: [match('alice', 1), match('bob', 2)] };
  const { page, errors } = await fixture(t, options);
  await page.locator('main').evaluate(node => node.insertAdjacentHTML('beforeend', `
    <div style="display:flex;justify-content:space-between;margin:70px 130px">
      <div><div data-cell-id="0"><div id="ally" class="champion-icon-container" data-champion-id="1" style="width:48px;height:64px;background:#234">安妮</div></div>
      <div data-cell-id="1" style="margin-top:25px"><div id="ally2" class="champion-icon-container" data-champion-id="2" style="width:48px;height:64px;background:#234">奥拉夫</div></div></div>
      <div data-cell-id="5"><div id="enemy" class="champion-icon-container" data-champion-id="1" style="width:48px;height:64px;background:#523">安妮</div></div>
    </div>`));
  await page.waitForFunction(() => document.querySelector('#ally [data-lol-game-assistant-player-stats]')?.textContent.includes('KDA 6.00'));
  assert.match(await page.locator('#ally ' + playerPanel).textContent(), /开黑1 · 2人.*胜率 100%/);
  assert.match(await page.locator('#enemy ' + playerPanel).getAttribute('title'), /KDA —.*身份未公开/);
  assert.equal(await page.locator('#enemy .lga-party').count(), 0);
  assert.doesNotMatch(await page.locator('#enemy ' + playerPanel).textContent(), /开黑|未见组队/);
  assert.equal(await page.locator('#enemy ' + playerPanel).getAttribute('data-side'), 'enemy');
  assert.equal(await page.locator('#hero ' + playerPanel).count(), 0);
  await page.screenshot({ path: path.join(__dirname, '..', 'test-results', 'champ-select-player-preview.png') });
  // A row recycled for a hidden player must lose its previous player's statistics.
  await page.locator('#ally').evaluate(node => node.parentElement.setAttribute('data-cell-id', '5'));
  await page.waitForFunction(() => document.querySelector('#ally [data-lol-game-assistant-player-stats]')?.title.includes('身份未公开'));
  await page.locator(root + ' .lga-toggle').click();
  await page.locator('[data-key="showPlayerStats"]').uncheck();
  assert.equal(await page.locator(playerPanel).count(), 0);
  assert.ok(await page.locator(badge).count() > 0);
  await page.locator('[data-key="showPlayerStats"]').check();
  await page.waitForSelector(playerPanel);
  options.playerSession = null;
  await page.locator('[data-action="refresh"]').click();
  await page.waitForFunction(() => document.querySelectorAll('[data-lol-game-assistant-player-stats]').length === 0);
  await page.evaluate(() => window.plugin.unload());
  assert.equal(await page.locator(playerPanel).count(), 0);
  assert.deepEqual(errors, []);
});

test('missing party IDs infer a group from owner-only histories and remove stale labels after refresh', async t => {
  const ownerMatch = (puuid, id, teamId) => match(puuid, id, {
    participantIdentities: [{ participantId: 2, player: { puuid } }],
    participants: [{ participantId: 2, teamId, stats: { kills: 4, deaths: 2, assists: 8, win: true } }],
  });
  const options = {
    playerSession: { myTeam: ['alice', 'bob', 'carol'].map((puuid, cellId) => ({ cellId, puuid })), theirTeam: [{ cellId: 5, puuid: 'eve' }] },
    histories: Object.fromEntries(['alice', 'bob', 'carol', 'eve'].map(puuid => [puuid, [1, 2].map(id => ownerMatch(puuid, id, puuid === 'carol' ? 200 : 100))])),
  };
  const { page, errors } = await fixture(t, options);
  await page.locator('main').evaluate(node => node.insertAdjacentHTML('beforeend', [0, 1, 2, 5].map(id =>
    `<div data-cell-id="${id}"><div id="player-${id}" class="champion-icon-container"></div></div>`).join('')));
  await page.waitForFunction(() => document.querySelector('#player-0 .lga-party')?.textContent === '疑似开黑1 · 2人');
  assert.equal(await page.locator('#player-1 .lga-party').textContent(), '疑似开黑1 · 2人');
  assert.equal(await page.locator('#player-2 .lga-party, #player-5 .lga-party').count(), 0);
  assert.match(await page.locator('#player-0 ' + playerPanel).getAttribute('title'), /整组共同同队2场，不能确认本局预组队/);
  assert.equal(await page.locator('#player-2 ' + playerPanel + ' > div').count(), 1);
  assert.match(await page.locator('#player-2 ' + playerPanel).textContent(), /^KDA 6.00/);
  options.histories.bob = [];
  await page.locator(root + ' .lga-toggle').click();
  await page.locator('[data-action="refresh"]').click();
  await page.waitForFunction(() => document.querySelectorAll('.lga-party').length === 0);
  assert.match(await page.locator('#player-0 ' + playerPanel).textContent(), /KDA/);
  assert.deepEqual(errors, []);
});

test('failed history shows unavailable and manual refresh retries successfully', async t => {
  const options = { playerSession: { myTeam: [{ cellId: 0, championId: 1, puuid: 'alice' }], theirTeam: [] }, historyError: true, history: [match('alice', 1)] };
  const { page, errors } = await fixture(t, options);
  await page.locator('main').evaluate(node => node.insertAdjacentHTML('beforeend', '<div data-cell-id="0"><div class="champion-icon-container"></div></div>'));
  await page.waitForFunction(() => document.querySelector('[data-lol-game-assistant-player-stats]')?.title.includes('战绩暂不可用'));
  options.historyError = false;
  await page.locator(root + ' .lga-toggle').click();
  await page.locator('[data-action="refresh"]').click();
  await page.waitForFunction(() => document.querySelector('[data-lol-game-assistant-player-stats]')?.textContent.includes('KDA 6.00'));
  assert.deepEqual(errors, []);
});


test('late history cannot restore information after disabling the feature', async t => {
  let release;
  const options = { playerSession: { myTeam: [{ cellId: 0, championId: 1, puuid: 'alice' }], theirTeam: [] },
    history: [match('alice', 1)], historyGate: new Promise(resolve => { release = resolve; }) };
  const { page, errors } = await fixture(t, options);
  await page.locator('main').evaluate(node => node.insertAdjacentHTML('beforeend', '<div data-cell-id="0"><div class="champion-icon-container"></div></div>'));
  await page.waitForSelector(playerPanel);
  await page.locator(root + ' .lga-toggle').click();
  await page.locator('[data-key="showPlayerStats"]').uncheck();
  release();
  await page.waitForTimeout(400);
  assert.equal(await page.locator(playerPanel).count(), 0);
  await page.locator('[data-key="showPlayerStats"]').check();
  await page.waitForFunction(() => document.querySelector('[data-lol-game-assistant-player-stats]')?.textContent.includes('KDA 6.00'));
  assert.equal(options.historyRequests, 2);
  assert.deepEqual(errors, []);
});


test('wallpaper loads above body background, clears opaque home surfaces and restores on removal', async t => {
  const { page, errors } = await fixture(t);
  await page.locator('main').evaluate(node => node.insertAdjacentHTML('beforeend', `<div id="rcp-fe-viewport-root" style="position:fixed;inset:0;background:#010a13;pointer-events:none"><section id="activity-center" class="activity-center-ready" style="background:#010a13;width:100%;height:100%"><main class="activity-center__contents"><div class="managed-iframe-wrapper"><iframe class="managed-iframe" style="width:100%;height:100%;background:black"></iframe></div></main></section></div><button id="foreground" style="position:fixed;left:20px;top:100px">导航仍可操作</button>`));
  await page.locator(root + ' .lga-toggle').click();
  const before = await page.screenshot();
  await page.locator('[data-key="wallpaperUrl"]').fill('http://client.test/wallpaper.svg');
  await page.locator('[data-key="wallpaperUrl"]').press('Tab');
  await page.waitForFunction(() => document.querySelector('.lga-status')?.textContent.includes('壁纸已加载'));
  assert.equal(await page.locator('#lol-game-assistant-wallpaper').count(), 1);
  assert.equal(await page.locator('.managed-iframe').isVisible(), false);
  assert.equal(await page.locator('#rcp-fe-viewport-root').evaluate(node => getComputedStyle(node).backgroundColor), 'rgba(0, 0, 0, 0)');
  // Every settings change reapplies the stylesheet, including the home-page fixes.
  await page.evaluate(() => { window.loadedWallpaper = document.getElementById('lol-game-assistant-wallpaper'); });
  for (const hidden of [true, false, true, false]) {
    await page.locator('[data-key="hideNavigation"]').setChecked(hidden);
    assert.equal(await page.locator('header').isVisible(), !hidden);
    assert.equal(await page.locator('.managed-iframe').isVisible(), false);
    assert.equal(await page.locator('#activity-center').evaluate(node => getComputedStyle(node).backgroundColor), 'rgba(0, 0, 0, 0)');
    assert.equal(await page.evaluate(() => document.getElementById('lol-game-assistant-wallpaper') === window.loadedWallpaper), true);
    assert.equal(await page.evaluate(() => JSON.parse(localStorage.getItem('lol-game-assistant.visuals.v1')).wallpaperUrl), 'http://client.test/wallpaper.svg');
  }
  await page.locator('#foreground').click();
  const after = await page.screenshot();
  assert.notDeepEqual(after, before);
  await page.screenshot({ path: path.join(__dirname, '..', 'test-results', 'wallpaper-preview.png') });
  // Client DOM reconstruction must restore the wallpaper element.
  await page.locator('#lol-game-assistant-wallpaper').evaluate(node => node.remove());
  await page.waitForSelector('#lol-game-assistant-wallpaper', { state: 'attached' });
  await page.locator('[data-key="wallpaperUrl"]').fill('');
  await page.locator('[data-key="wallpaperUrl"]').press('Tab');
  assert.equal(await page.locator('#lol-game-assistant-wallpaper').count(), 0);
  assert.equal(await page.locator('.managed-iframe').isVisible(), true);
  assert.equal(await page.locator('#rcp-fe-viewport-root').evaluate(node => getComputedStyle(node).backgroundColor), 'rgb(1, 10, 19)');
  assert.deepEqual(errors, []);
});

test('invalid image reports failure without hiding the native home page, and unload cancels wallpaper', async t => {
  const { page, errors } = await fixture(t);
  await page.locator(root + ' .lga-toggle').click();
  await page.locator('[data-key="wallpaperUrl"]').fill('http://client.test/broken-wallpaper.png');
  await page.locator('[data-key="wallpaperUrl"]').press('Tab');
  await page.waitForFunction(() => document.querySelector('.lga-status')?.textContent.includes('壁纸加载失败'));
  assert.equal(await page.locator('body').evaluate(node => node.classList.contains('lga-wallpaper')), false);
  await page.locator('[data-key="wallpaperUrl"]').fill('http://client.test/wallpaper.svg');
  await page.locator('[data-key="wallpaperUrl"]').press('Tab');
  await page.waitForSelector('#lol-game-assistant-wallpaper', { state: 'attached' });
  await page.evaluate(() => window.plugin.unload());
  await page.waitForTimeout(300);
  assert.equal(await page.locator('#lol-game-assistant-wallpaper').count(), 0);
  assert.equal(await page.locator('body').evaluate(node => node.classList.contains('lga-wallpaper')), false);
  assert.deepEqual(errors, []);
});

async function localPicture(page, color = '#29a3ba') {
  const data = await page.evaluate(color => {
    const canvas = document.createElement('canvas');
    canvas.width = 320; canvas.height = 180;
    const ctx = canvas.getContext('2d'); ctx.fillStyle = color; ctx.fillRect(0, 0, 320, 180);
    return canvas.toDataURL('image/png').split(',')[1];
  }, color);
  return { name: '我的壁纸.png', mimeType: 'image/png', buffer: Buffer.from(data, 'base64') };
}

test('local wallpaper persists across reload, survives navigation changes and switches to URL or clear', async t => {
  const { page, errors } = await fixture(t);
  await page.locator(root + ' .lga-toggle').click();
  const chooser = page.waitForEvent('filechooser');
  await page.locator('[data-action="local-wallpaper"]').click();
  await (await chooser).setFiles(await localPicture(page));
  await page.waitForFunction(() => document.querySelector('.lga-wallpaper-source')?.textContent.includes('我的壁纸.png'));
  await page.waitForSelector('#lol-game-assistant-wallpaper', { state: 'attached' });
  const saved = await page.evaluate(() => localStorage.getItem('lol-game-assistant.wallpaper.local.v1'));
  assert.match(saved, /^data:image\/jpeg;base64,/);
  assert.equal(await page.locator('[data-key="wallpaperUrl"]').inputValue(), '');
  await page.locator('[data-key="hideNavigation"]').check();
  assert.match(await page.locator('#lol-game-assistant-wallpaper').evaluate(n => n.style.backgroundImage), /data:image\/jpeg;base64,/);
  await page.reload();
  await page.evaluate(async url => { window.plugin = await import(url); window.plugin.load(); }, moduleUrl);
  await page.waitForSelector('#lol-game-assistant-wallpaper', { state: 'attached' });
  assert.match(await page.locator('#lol-game-assistant-wallpaper').evaluate(n => n.style.backgroundImage), /data:image\/jpeg;base64,/);
  await page.locator(root + ' .lga-toggle').click();
  await page.locator('[data-key="wallpaperUrl"]').fill('http://client.test/wallpaper.svg');
  await page.locator('[data-key="wallpaperUrl"]').press('Tab');
  await page.waitForFunction(() => document.getElementById('lol-game-assistant-wallpaper')?.style.backgroundImage.includes('wallpaper.svg'));
  await page.locator('.lga-file').setInputFiles(await localPicture(page, '#f08042'));
  await page.waitForFunction(() => document.getElementById('lol-game-assistant-wallpaper')?.style.backgroundImage.includes('data:image/jpeg'));
  await page.locator('[data-action="clear-wallpaper"]').click();
  assert.equal(await page.locator('#lol-game-assistant-wallpaper').count(), 0);
  assert.equal(await page.evaluate(() => localStorage.getItem('lol-game-assistant.wallpaper.local.v1')), null);
  assert.deepEqual(errors, []);
});

test('invalid local images and storage failure keep the existing wallpaper', async t => {
  const { page, errors } = await fixture(t);
  await page.locator(root + ' .lga-toggle').click();
  await page.locator('[data-key="wallpaperUrl"]').fill('http://client.test/wallpaper.svg');
  await page.locator('[data-key="wallpaperUrl"]').press('Tab');
  await page.waitForSelector('#lol-game-assistant-wallpaper', { state: 'attached' });
  await page.locator('.lga-file').setInputFiles({ name: 'invalid.png', mimeType: 'image/png', buffer: Buffer.from('not an image') });
  await page.waitForFunction(() => document.querySelector('.lga-status')?.textContent.includes('无法解码图片'));
  assert.match(await page.locator('#lol-game-assistant-wallpaper').evaluate(n => n.style.backgroundImage), /wallpaper.svg/);
  await page.evaluate(() => {
    const set = Storage.prototype.setItem;
    Storage.prototype.setItem = function(key, value) { if (key.includes('wallpaper.local')) throw new DOMException('Full', 'QuotaExceededError'); return set.call(this, key, value); };
  });
  await page.locator('.lga-file').setInputFiles(await localPicture(page));
  await page.waitForFunction(() => document.querySelector('.lga-status')?.textContent.includes('存储空间不足'));
  assert.match(await page.locator('#lol-game-assistant-wallpaper').evaluate(n => n.style.backgroundImage), /wallpaper.svg/);
  assert.deepEqual(errors, []);
});


test('assistant controls visuals without client panel, survives malformed config, and stops polling on unload', async t => {
  const { page, errors } = await fixture(t, { managed: { revision: 'one', showBadges: false, hideNavigation: true, wallpaperUrl: 'http://client.test/wallpaper.svg' } });
  await page.waitForFunction(() => document.getElementById('lol-game-assistant-wallpaper')?.style.backgroundImage.includes('wallpaper.svg'));
  assert.equal(await page.locator(root).count(), 0);
  assert.equal(await page.locator('header').isVisible(), false);
  assert.equal(await page.locator(badge).count(), 0);
  await page.evaluate(() => { window.controlText = '{broken'; });
  await page.waitForTimeout(1200);
  assert.equal(await page.locator('header').isVisible(), false);
  await page.evaluate(() => { window.controlText = JSON.stringify({ revision: 'two', showBadges: true, hideNavigation: false, wallpaperUrl: 'http://client.test/wallpaper.svg' }); });
  await page.waitForSelector(badge);
  assert.equal(await page.locator('header').isVisible(), true);
  assert.equal(await page.locator(root).count(), 0);
  assert.ok(await page.locator('#lol-game-assistant-wallpaper').count());
  assert.equal(await page.evaluate(() => window.runtimeWrites.at(-1).revision), 'two');
  assert.equal(await page.evaluate(() => window.runtimeWrites.at(-1).managed), true);
  await page.evaluate(() => { window.controlText = JSON.stringify({ revision: 'three', enabled: false, hideNavigation: true, wallpaperUrl: 'http://client.test/wallpaper.svg' }); });
  await page.waitForFunction(() => !document.querySelector('[data-lol-game-assistant-tier-badge]'));
  await page.waitForFunction(() => !document.getElementById('lol-game-assistant-wallpaper'));
  assert.equal(await page.locator('header').isVisible(), true);
  await page.evaluate(() => { window.plugin.unload(); window.controlText = JSON.stringify({ revision: 'four', enabled: true }); });
  await page.waitForTimeout(1200);
  assert.equal(await page.locator(badge).count(), 0);
  assert.equal(await page.locator(root).count(), 0);
  assert.equal(await page.evaluate(() => window.runtimeWrites.at(-1).active), false);
  assert.deepEqual(errors, []);
});


test('assistant refresh command bypasses successful tier cache', async t => {
  const state = await fixture(t, { managed: { revision: 'one', showBadges: true } });
  await state.page.waitForSelector(badge);
  const before = state.requests();
  await state.page.evaluate(() => { window.controlText = JSON.stringify({ revision: 'two', showBadges: true, refreshNonce: 'refresh-once' }); });
  await state.page.waitForTimeout(1500);
  assert.ok(state.requests() > before);
  const after = state.requests();
  await state.page.waitForTimeout(1200);
  assert.equal(state.requests(), after);
  assert.deepEqual(state.errors, []);
});


test('native grid IDs and halo identities work before picking, with transparent text clear of native names', async t => {
  const { page, errors } = await fixture(t, { queue: { id: 3140, gameMode: 'PRACTICETOOL' },
    playerSession: { myTeam: [{ cellId: 0, championId: 0, puuid: 'alice' }], theirTeam: [] }, history: [match('alice', 1)] });
  await page.locator('main').evaluate(node => node.insertAdjacentHTML('beforeend', `
    <div class="grid-champion" data-id="1"><div class="grid-champion-hitbox"><div id="native-hero" class="champion-grid-champion-thumbnail"></div></div></div>
    <div class="summoner-object" style="position:relative;width:320px;height:78px;margin-top:40px">
      <div style="position:absolute;left:40px;top:8px"><lol-parties-comm-halo puuid="alice"><div id="blank-portrait" class="champion-icon-container" style="width:62px;height:62px"></div></lol-parties-comm-halo></div>
      <div class="player-details" style="position:absolute;left:112px;top:20px;font:12px/14px Arial"><div>正在选用……</div><div class="summoner-name">玩家名字</div></div>
    </div>`));
  await page.waitForFunction(() => document.querySelector('#native-hero [data-lol-game-assistant-tier-badge]')?.textContent === 'T3');
  await page.waitForFunction(() => document.querySelector('#blank-portrait [data-lol-game-assistant-player-stats]')?.textContent.includes('KDA 6.00'));
  const layout = await page.locator('#blank-portrait ' + playerPanel).evaluate(panel => {
    const row = panel.closest('.summoner-object'), p = panel.getBoundingClientRect(), n = row.querySelector('.summoner-name').getBoundingClientRect(), r = row.getBoundingClientRect(), css = getComputedStyle(panel);
    return { clear: p.top >= n.bottom, within: p.bottom <= r.bottom && p.right < r.right - 40, bg: css.backgroundColor, border: css.borderTopWidth, lines: panel.children.length };
  });
  assert.deepEqual(layout, { clear: true, within: true, bg: 'rgba(0, 0, 0, 0)', border: '0px', lines: 1 });
  assert.deepEqual(errors, []);
});

test('wallpaper switch restores the saved image without clearing its URL', async t => {
  const config = { revision: 'on', wallpaperUrl: 'http://client.test/wallpaper.svg' };
  const { page, errors } = await fixture(t, { managed: config });
  await page.waitForSelector('#lol-game-assistant-wallpaper', { state: 'attached' });
  await page.evaluate(config => { window.controlText = JSON.stringify({ ...config, revision: 'off', wallpaperEnabled: false }); }, config);
  await page.waitForFunction(() => !document.querySelector('#lol-game-assistant-wallpaper'));
  assert.equal(await page.evaluate(() => JSON.parse(window.controlText).wallpaperUrl), config.wallpaperUrl);
  await page.evaluate(config => { window.controlText = JSON.stringify({ ...config, revision: 'restored', wallpaperEnabled: true }); }, config);
  await page.waitForSelector('#lol-game-assistant-wallpaper', { state: 'attached' });
  assert.deepEqual(errors, []);
});


const counterPanel = '#lol-game-assistant-counters';
function counterSession() {
  return { gameId: 123, localPlayerCellId: 0, myTeam: [{ cellId: 0, championId: 1, puuid: 'alice' }], theirTeam: [],
    actions: [[{ id: 99, actorCellId: 1, type: 'pick', isInProgress: true, completed: false },
      { id: 42, actorCellId: 0, type: 'pick', isInProgress: true, completed: false }]] };
}
async function counterFixture(t, options = {}) {
  Object.assign(options, { managed: { showBadges: false, showPlayerStats: false, ...(options.managed || {}) }, queue: options.queue || { id: 420, gameMode: 'CLASSIC' } });
  const result = await fixture(t, options);
  await result.page.locator('main').evaluate(node => node.classList.add('champion-select'));
  return { ...result, options };
}

test('counter hover separates matchup directions, filters bad samples, caches and explicitly preselects own action', async t => {
  const options = { playerSession: counterSession() };
  const { page, errors } = await counterFixture(t, options);
  await page.locator('#hero').hover();
  await page.waitForSelector(`${counterPanel} button[data-champion-id="2"]`);
  assert.match(await page.locator(counterPanel).textContent(), /列表英雄对 安妮 的对位胜率.*克制他的英雄.*奥拉夫60.0%.*他克制的英雄.*加里奥40.0%/);
  assert.equal(await page.locator(`${counterPanel} button[data-champion-id]`).count(), 2);
  assert.equal(options.patches?.length || 0, 0);
  await page.screenshot({ path: path.join(__dirname, '..', 'test-results', 'counter-hover-preview.png') });
  await page.locator(`${counterPanel} button[data-champion-id="2"]`).click();
  await page.waitForFunction(() => document.querySelector('#lol-game-assistant-counters [role="status"]')?.textContent.includes('已预选'));
  assert.deepEqual(options.patches.map(p => ({ method: p.method, action: p.url.split('/').at(-1), body: p.body })), [{ method: 'PATCH', action: '42', body: { championId: 2, completed: false } }]);
  await page.mouse.move(10, 10);
  await page.waitForSelector(counterPanel, { state: 'detached' });
  await page.locator('#hero').hover();
  await page.waitForSelector(`${counterPanel} button[data-champion-id="2"]`);
  assert.equal(options.counterRequests, 1);
  await page.locator(`${counterPanel} select`).selectOption('top');
  await page.waitForSelector(`${counterPanel} button[data-champion-id="2"]`);
  assert.equal(options.counterRequests, 2);
  assert.deepEqual(errors, []);
});

test('counter selection rejects bans, unavailable heroes, completed and nonlocal actions', async t => {
  const options = { playerSession: counterSession(), disabled: [2] };
  const { page, errors } = await counterFixture(t, options);
  await page.locator('#hero').hover();
  const button = page.locator(`${counterPanel} button[data-champion-id="2"]`);
  await button.click();
  await page.waitForFunction(() => document.querySelector('#lol-game-assistant-counters [role="status"]')?.textContent.includes('不可选'));
  options.disabled = []; options.playerSession.actions[0][1].completed = true;
  await button.click();
  await page.waitForFunction(() => document.querySelector('#lol-game-assistant-counters [role="status"]')?.textContent.includes('已经锁定'));
  options.playerSession.actions[0][1].completed = false; options.playerSession.bans = { myTeamBans: [2] };
  await button.click();
  await page.waitForFunction(() => document.querySelector('#lol-game-assistant-counters [role="status"]')?.textContent.includes('不可选'));
  assert.equal(options.patches?.length || 0, 0);
  assert.deepEqual(errors, []);
});

test('counter pick rechecks session after waiting and does not write a stale action', async t => {
  let release;
  const options = { playerSession: counterSession(), pickGate: new Promise(resolve => { release = resolve; }) };
  const { page, errors } = await counterFixture(t, options);
  await page.locator('#hero').hover();
  await page.locator(`${counterPanel} button[data-champion-id="2"]`).click();
  await page.waitForFunction(() => document.querySelector('#lol-game-assistant-counters button[data-champion-id]')?.disabled);
  options.playerSession = { ...counterSession(), gameId: 456 };
  release();
  await page.waitForFunction(() => document.querySelector('#lol-game-assistant-counters [role="status"]')?.textContent.includes('状态已变化'));
  assert.equal(options.patches?.length || 0, 0);
  assert.deepEqual(errors, []);
});

test('counter hover discards late responses, reports failure, retries and respects the master switch', async t => {
  let release;
  const options = { counterGate: new Promise(resolve => { release = resolve; }), counterError: true };
  const { page, errors } = await counterFixture(t, options);
  await page.locator('#hero').hover();
  await page.waitForSelector(counterPanel);
  await page.mouse.move(10, 10);
  await page.waitForSelector(counterPanel, { state: 'detached' });
  release();
  await page.waitForTimeout(100);
  assert.equal(await page.locator(counterPanel).count(), 0);
  await page.locator('#hero').hover();
  await page.waitForSelector(`${counterPanel} button:text("重试")`);
  options.counterError = false;
  await page.locator(`${counterPanel} button:text("重试")`).click();
  await page.waitForSelector(`${counterPanel} button[data-champion-id="2"]`);
  await page.evaluate(() => { window.controlText = JSON.stringify({ enabled: false }); });
  await page.waitForSelector(counterPanel, { state: 'detached' });
  await page.locator('#hero').hover();
  assert.equal(await page.locator(counterPanel).count(), 0);
  await page.evaluate(() => window.plugin.unload());
  assert.deepEqual(errors, []);
});

test('counter switch suppresses only native skill previews, including new media, and restores on disable or unload', async t => {
  const { page, errors } = await counterFixture(t);
  await page.evaluate(() => {
    document.body.insertAdjacentHTML('beforeend', '<div class="champion-preview"><div class="champion-preview-abilities"><video id="native-skill"></video></div></div><video id="splash"></video>');
    window.pausedMedia = [];
    HTMLMediaElement.prototype.pause = function () { window.pausedMedia.push(this.id); };
  });
  assert.equal(await page.locator('.champion-preview').isVisible(), false);
  await page.evaluate(() => {
    document.querySelector('#native-skill').dispatchEvent(new Event('play'));
    document.querySelector('#splash').dispatchEvent(new Event('play'));
  });
  assert.deepEqual(await page.evaluate(() => window.pausedMedia), ['native-skill']);
  await page.evaluate(() => { window.controlText = JSON.stringify({ enabled: true, showCounters: false }); });
  await page.waitForFunction(() => !document.body.classList.contains('lga-counters'));
  assert.equal(await page.locator('.champion-preview').evaluate(node => getComputedStyle(node).display), 'block');
  await page.evaluate(() => document.querySelector('#native-skill').dispatchEvent(new Event('play')));
  assert.deepEqual(await page.evaluate(() => window.pausedMedia), ['native-skill']);
  await page.evaluate(() => { window.controlText = JSON.stringify({ enabled: true, showCounters: true }); });
  await page.waitForFunction(() => document.body.classList.contains('lga-counters'));
  await page.evaluate(() => { window.controlText = JSON.stringify({ enabled: false }); });
  await page.waitForFunction(() => !document.body.classList.contains('lga-counters'));
  await page.evaluate(() => { window.controlText = JSON.stringify({ enabled: true, showCounters: true }); });
  await page.waitForFunction(() => document.body.classList.contains('lga-counters'));
  await page.evaluate(() => window.plugin.unload());
  assert.equal(await page.locator('body').evaluate(node => node.classList.contains('lga-counters')), false);
  await page.evaluate(() => document.querySelector('#native-skill').dispatchEvent(new Event('play')));
  assert.deepEqual(await page.evaluate(() => window.pausedMedia), ['native-skill']);
  assert.deepEqual(errors, []);
});

test('unsupported counter modes show no ranked matchup recommendations', async t => {
  const options = { queue: { id: 450, gameMode: 'ARAM' } };
  const { page, errors } = await counterFixture(t, options);
  await page.locator('#hero').hover();
  await page.waitForSelector(counterPanel);
  assert.match(await page.locator(counterPanel).textContent(), /当前模式暂无分路克制数据/);
  assert.equal(options.counterRequests || 0, 0);
  assert.deepEqual(errors, []);
});
