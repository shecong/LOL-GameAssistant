const fs = require('node:fs');
const path = require('node:path');
const output = path.resolve(__dirname, '../../tmp/match-layout-designs');
fs.mkdirSync(output, { recursive: true });
const { chromium } = require('../../PenguPlugin/node_modules/playwright');
(async () => {
  const fragment = fs.readFileSync(path.join(__dirname, 'match-layout-options.html'), 'utf8');
  const browser = await chromium.launch({ headless: true });
  const page = await browser.newPage({ viewport: { width: 1280, height: 900 }, deviceScaleFactor: 1 });
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.setContent('<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>对局详情排版方案</title><style>body{margin:0;padding:20px;background:#07101a}button{cursor:pointer}</style><body>' + fragment + '</body></html>');
  const variants = ['inline', 'workspace', 'report'];
  const results = [];
  for (let i = 0; i < variants.length; i++) {
    await page.evaluate(index => {
      document.querySelectorAll('#match-layout-options > section').forEach((section, current) => { section.hidden = current !== index; });
    }, i);
    const target = page.locator('#match-concept-' + variants[i]);
    await target.screenshot({ path: path.join(output, 'design-' + variants[i] + '.png') });
    results.push({ design: variants[i], players: await target.locator('.mc-scorecard, .mc-playercard').count(), height: await target.evaluate(node => node.offsetHeight) });
    const toggle = target.locator('[data-collapse]').first();
    await toggle.click();
    if (await target.locator('.mc-report').isVisible()) throw new Error('Collapse failed: ' + variants[i]);
    await target.locator('[data-collapse]').first().click();
    if (!await target.locator('.mc-report').isVisible()) throw new Error('Expand failed: ' + variants[i]);
    for (const width of [1024, 736, 320]) {
      await page.setViewportSize({ width, height: 900 });
      const overflow = await target.evaluate(node => ({ outer: node.scrollWidth - node.clientWidth, document: document.documentElement.scrollWidth - innerWidth }));
      if (overflow.outer > 1 || overflow.document > 1) throw new Error('Overflow at ' + width + ': ' + variants[i] + ' ' + JSON.stringify(overflow));
    }
    await page.setViewportSize({ width: 1280, height: 900 });
  }
  await browser.close();
  if (errors.length) throw new Error(errors.join('\n'));
  console.log(JSON.stringify(results));
})();
