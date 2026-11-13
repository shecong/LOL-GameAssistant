const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
let browser;
before(async () => { browser = await chromium.launch({ headless: true, channel: process.env.PENGU_TEST_BROWSER_CHANNEL || undefined }); });
after(async () => { await browser?.close(); });

const source = fs.readFileSync(path.join(__dirname, '..', 'index.js'), 'utf8');
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
    if (url.includes('lol-api-champion.op.gg')) {
      requests++;
      if (options.failFirst && requests === 1) return route.fulfill({ status: 503, body: '{}' });
      const tier = url.includes('/ranked?') ? 3 : 1;
      return route.fulfill({ json: { data: [1, 2].map(id => ({ id, average_stats: { tier }, positions: [{ stats: { tier_data: { tier } } }] })) } });
    }
    if (url.includes('/lol-gameflow/v1/session')) return route.fulfill({ json: { gameData: { queue: { id: 450, gameMode: 'ARAM' } } } });
    if (url.includes('/lol-lobby/v2/lobby')) return route.fulfill({ json: { gameConfig: { queueId: 450 } } });
    return route.fulfill({ contentType: 'text/html', body: html });
  });
  await page.goto('http://client.test');
  await page.evaluate(async url => {
    window.runtimeWrites = [];
    window.plugin = await import(url);
    window.plugin.init({ fs: { write: async (name, content) => { window.runtimeWrites.push(JSON.parse(content)); return true; } } });
    window.plugin.load();
  }, moduleUrl);
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
