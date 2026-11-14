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
    if (url.includes('lol-api-champion.op.gg')) {
      requests++;
      if (options.failFirst && requests === 1) return route.fulfill({ status: 503, body: '{}' });
      const tier = url.includes('/ranked?') ? 3 : 1;
      return route.fulfill({ json: { data: [1, 2].map(id => ({ id, average_stats: { tier }, positions: [{ stats: { tier_data: { tier } } }] })) } });
    }
    if (url.includes('/lol-champ-select/v1/session')) return route.fulfill({ status: options.playerSession ? 200 : 404, json: options.playerSession || {} });
    if (url.includes('/lol-match-history/')) {
      options.historyRequests = (options.historyRequests || 0) + 1;
      if (options.historyGate) await options.historyGate;
      return route.fulfill({ status: options.historyError ? 503 : 200, json: { games: { games: options.history || [] } } });
    }
    if (url.includes('/lol-gameflow/v1/session')) return route.fulfill({ json: { gameData: { queue: { id: 450, gameMode: 'ARAM' } } } });
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
  assert.match(await page.locator('#ally ' + playerPanel).textContent(), /开黑1 · 2人.*胜率 100%.*同模式 1场/);
  assert.match(await page.locator('#enemy ' + playerPanel).textContent(), /开黑未知.*KDA —.*身份未公开/);
  assert.equal(await page.locator('#enemy ' + playerPanel).getAttribute('data-side'), 'enemy');
  assert.equal(await page.locator('#hero ' + playerPanel).count(), 0);
  await page.screenshot({ path: path.join(__dirname, '..', 'test-results', 'champ-select-player-preview.png') });
  // A row recycled for a hidden player must lose its previous player's statistics.
  await page.locator('#ally').evaluate(node => node.parentElement.setAttribute('data-cell-id', '5'));
  await page.waitForFunction(() => document.querySelector('#ally [data-lol-game-assistant-player-stats]')?.textContent.includes('身份未公开'));
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

test('failed history shows unavailable and manual refresh retries successfully', async t => {
  const options = { playerSession: { myTeam: [{ cellId: 0, championId: 1, puuid: 'alice' }], theirTeam: [] }, historyError: true, history: [match('alice', 1)] };
  const { page, errors } = await fixture(t, options);
  await page.locator('main').evaluate(node => node.insertAdjacentHTML('beforeend', '<div data-cell-id="0"><div class="champion-icon-container"></div></div>'));
  await page.waitForFunction(() => document.querySelector('[data-lol-game-assistant-player-stats]')?.textContent.includes('战绩暂不可用'));
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
