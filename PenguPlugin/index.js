/**
 * Optional Pengu Loader visual layer.
 *
 * This plugin deliberately only changes Chromium client DOM/CSS. Match automation,
 * LCU actions, game data and account configuration remain in the external C# app.
 * The panel uses localStorage so Pengu upgrades do not require a custom config file.
 */
const ROOT_ID = 'lol-game-assistant-visual-root';
const STYLE_ID = 'lol-game-assistant-visual-style';
const STORAGE_KEY = 'lol-game-assistant.visuals.v1';
const BADGE_ATTR = 'data-lol-game-assistant-tier-badge';
const POSITION_ATTR = 'data-lol-game-assistant-original-position';
const VERSION = '0.2.0';
const CACHE_TTL = 30 * 60 * 1000;
const MODE_NAMES = { ranked: '峡谷 / 排位', aram: '极地大乱斗', arena: '斗魂竞技场', urf: '无限火力' };

// 与 Sona 的客户端内挂载点保持一致；不同补丁的 DOM 只要仍保留这些组件名便可显示角标。
const BADGE_TARGETS = [
  { selector: '.champion-grid-champion-thumbnail', size: 22, left: -2, top: 0 },
  { selector: '.champion-card-component-click-target', size: 22, left: -2, top: 0 },
  { selector: '.bench-champion-icon', size: 18, left: -2, top: -2 },
  { selector: '.champion-icon-container', size: 20, left: -2, bottom: -2 },
];
const BADGE_TARGET_SELECTOR = BADGE_TARGETS.map((target) => target.selector).join(',');

let context = null;
let observer = null;
let mountedRoot = null;
let tierCache = new Map();
let tierRequestMode = '';
let tierCacheMode = '';
let tierGeneration = 0;
let tierPending = null;
let tierUpdatedAt = 0;
let active = false;
let lifecycle = 0;
let detectedMode = '';
let detectedQueue = '';
let retryTimer = 0;
let refreshTimer = 0;
let heartbeatTimer = 0;
let failures = 0;
let lastError = '';
let storageError = '';
let heartbeatError = '';
let modePending = false;
let subscriptions = [];
let runtimeWrite = Promise.resolve();
const requests = new Set();

const defaults = {
  showBadges: true,
  tierMode: 'auto',
  hideNavigation: false,
  wallpaperUrl: '',
};

function readConfig() {
  try {
    const saved = JSON.parse(localStorage.getItem(STORAGE_KEY) || '{}');
    return {
      showBadges: typeof saved?.showBadges === 'boolean' ? saved.showBadges : defaults.showBadges,
      tierMode: ['auto', ...Object.keys(MODE_NAMES)].includes(saved?.tierMode) ? saved.tierMode : 'auto',
      hideNavigation: saved?.hideNavigation === true,
      wallpaperUrl: validWallpaper(saved?.wallpaperUrl),
    };
  } catch {
    return { ...defaults };
  }
}

function writeConfig(config) {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(config));
    storageError = '';
  } catch { storageError = '无法保存设置，请检查客户端存储权限。'; }
  applyVisuals();
}

function validWallpaper(value) {
  if (typeof value !== 'string' || !value.trim()) return '';
  try {
    const url = new URL(value.trim());
    return ['http:', 'https:'].includes(url.protocol) ? url.href : '';
  } catch { return ''; }
}

/** Pengu calls init before League client scripts: keep only the context, no DOM work. */
export function init(penguContext) {
  context = penguContext;
}

/** Pengu calls load after the client window is available. */
export function load() {
  if (active) return;
  active = true;
  lifecycle++;
  mountPanel();
  applyVisuals();
  observer = new MutationObserver(() => {
    if (document.getElementById(ROOT_ID) !== mountedRoot) mountPanel();
    scheduleBadgeInjection();
  });
  observeDom();
  if (context?.socket?.observe) {
    for (const endpoint of ['/lol-gameflow/v1/session', '/lol-lobby/v2/lobby']) {
      try { subscriptions.push(context.socket.observe(endpoint, () => { void detectMode(); })); }
      catch { /* Polling remains available on older loaders. */ }
    }
  }
  void detectMode();
  refreshTimer = window.setInterval(() => {
    void detectMode();
    refreshTiers();
  }, 30000);
  heartbeatTimer = window.setInterval(() => { updateStatus(); void reportRuntime(); }, 10000);
  window.addEventListener('beforeunload', unload);
  window.addEventListener('storage', onStorage);
  void reportRuntime();
}

export function unload() {
  active = false;
  lifecycle++;
  tierGeneration++;
  tierPending = null;
  window.clearTimeout(badgeTimer);
  window.clearTimeout(retryTimer);
  window.clearInterval(refreshTimer);
  window.clearInterval(heartbeatTimer);
  badgeTimer = retryTimer = refreshTimer = heartbeatTimer = 0;
  for (const request of requests) request.abort();
  for (const subscription of subscriptions) subscription?.disconnect?.();
  subscriptions = [];
  window.removeEventListener('beforeunload', unload);
  window.removeEventListener('storage', onStorage);
  observer?.disconnect();
  observer = null;
  document.getElementById(ROOT_ID)?.remove();
  mountedRoot = null;
  document.getElementById(STYLE_ID)?.remove();
  document.querySelectorAll(`[${BADGE_ATTR}]`).forEach((node) => node.remove());
  restorePositions();
  document.body?.classList.remove('lga-wallpaper', 'lga-hide-navigation');
  void reportRuntime();
}

function onStorage(event) { if (event.key === STORAGE_KEY) { syncPanel(); applyVisuals(); } }

function observeDom() {
  observer?.observe(document.documentElement, {
    childList: true, subtree: true, attributes: true,
    attributeFilter: ['class', 'src', 'style', 'data-champion-id', 'data-champion-id-value', 'data-champion-id-string', 'champion-id'],
  });
}

async function fetchJson(url) {
  const controller = new AbortController();
  requests.add(controller);
  const timeout = window.setTimeout(() => controller.abort(), 10000);
  try {
    const response = await fetch(url, { signal: controller.signal });
    if (!response.ok) throw new Error(`请求失败：HTTP ${response.status}`);
    return await response.json();
  } finally { window.clearTimeout(timeout); requests.delete(controller); }
}

function modeFromQueue(queue) {
  const mode = String(queue?.gameMode || '').toUpperCase();
  const id = Number(queue?.id ?? queue?.queueId);
  if (mode.includes('ARAM') || [450, 2400].includes(id)) return 'aram';
  if (mode === 'CHERRY' || [1700, 1710].includes(id)) return 'arena';
  if (mode.includes('URF') || [900, 1900].includes(id)) return 'urf';
  if (mode === 'CLASSIC' || [400, 420, 430, 440, 490, 700, 830, 840, 850].includes(id)) return 'ranked';
  return '';
}

async function detectMode() {
  if (!active || modePending) return;
  modePending = true;
  const epoch = lifecycle;
  try {
    let queue;
    try {
      const session = await fetchJson('/lol-gameflow/v1/session');
      queue = session?.gameData?.queue;
    } catch { /* The lobby exists before a gameflow session is ready. */ }
    if (!active || epoch !== lifecycle) return;
    if (!queue || (!queue.id && !queue.gameMode)) {
      const lobby = await fetchJson('/lol-lobby/v2/lobby');
      queue = { id: lobby?.gameConfig?.queueId, gameMode: lobby?.gameConfig?.gameMode };
    }
    if (!active || epoch !== lifecycle) return;
    detectedMode = modeFromQueue(queue);
    detectedQueue = queue?.id ? `队列 ${queue.id}` : '等待进入大厅';
  } catch {
    if (!active || epoch !== lifecycle) return;
    detectedMode = '';
    detectedQueue = '等待客户端登录或大厅就绪';
  } finally {
    modePending = false;
    if (active && epoch === lifecycle) { refreshTiers(); updateStatus(); }
  }
}

function selectedMode() { const config = readConfig(); return config.tierMode === 'auto' ? detectedMode : config.tierMode; }

function refreshTiers(force = false) {
  if (!active || !readConfig().showBadges) return;
  const epoch = lifecycle;
  const mode = selectedMode();
  if (!mode) {
    window.clearTimeout(retryTimer);
    retryTimer = 0;
    lastError = '';
    tierGeneration++;
    tierPending = null;
    tierCache = new Map();
    tierCacheMode = '';
    document.querySelectorAll(`[${BADGE_ATTR}]`).forEach((node) => node.remove());
    restorePositions();
    updateStatus();
    return;
  }
  if (retryTimer && !force && mode === tierRequestMode) return;
  window.clearTimeout(retryTimer);
  retryTimer = 0;
  const pending = fetchTierData(mode, force);
  const generation = tierGeneration;
  void pending.then(() => {
    if (!active || epoch !== lifecycle || generation !== tierGeneration || mode !== selectedMode() || !readConfig().showBadges) return;
    failures = 0;
    lastError = '';
    scheduleBadgeInjection();
    updateStatus();
  }, (error) => {
    if (!active || epoch !== lifecycle || generation !== tierGeneration || mode !== selectedMode() || !readConfig().showBadges) return;
    lastError = error?.name === 'AbortError' ? '数据请求超时' : String(error?.message || error);
    failures++;
    updateStatus();
    retryTimer = window.setTimeout(() => { retryTimer = 0; refreshTiers(true); }, Math.min(60000, 2000 * 2 ** Math.min(failures - 1, 5)));
  });
}

function mountPanel() {
  const existing = document.getElementById(ROOT_ID);
  if (existing && existing === mountedRoot) return;
  // Client bootstrap can rewrite body HTML, retaining our markup but dropping listeners.
  existing?.remove();
  const config = readConfig();
  const root = document.createElement('section');
  root.id = ROOT_ID;
  root.innerHTML = `
    <button type="button" class="lga-toggle" title="LOL GameAssistant 视觉设置">✦</button>
    <div class="lga-panel" hidden>
      <strong>LOL GameAssistant · 视觉层</strong>
      <label><input data-key="showBadges" type="checkbox"> 选人 T 级角标</label>
      <label>角标模式<select data-key="tierMode"><option value="auto">自动跟随客户端</option><option value="ranked">峡谷 / 排位</option><option value="aram">极地大乱斗</option><option value="arena">斗魂竞技场</option><option value="urf">无限火力</option></select></label>
      <label><input data-key="hideNavigation" type="checkbox"> 隐藏客户端导航菜单</label>
      <label>壁纸 URL<input data-key="wallpaperUrl" type="url" placeholder="https://…/wallpaper.jpg"></label>
      <button type="button" data-action="refresh">刷新数据</button>
      <button type="button" data-action="reset">恢复默认</button>
      <output class="lga-status" aria-live="polite"></output>
      <small>v${VERSION} · 仅修改客户端展示。壁纸可能被页面自身背景遮挡。</small>
    </div>`;
  document.body.appendChild(root);
  mountedRoot = root;

  const panel = root.querySelector('.lga-panel');
  root.querySelector('.lga-toggle').setAttribute('aria-expanded', 'false');
  root.querySelector('.lga-toggle').addEventListener('click', (event) => {
    panel.hidden = !panel.hidden;
    event.currentTarget.setAttribute('aria-expanded', String(!panel.hidden));
    updateStatus();
  });
  root.querySelector('[data-action="refresh"]').addEventListener('click', () => { void detectMode(); refreshTiers(true); });
  root.querySelector('[data-action="reset"]').addEventListener('click', () => { writeConfig({ ...defaults }); syncPanel(); });
  root.querySelectorAll('[data-key]').forEach((input) => {
    const key = input.dataset.key;
    if (input.type === 'checkbox') input.checked = Boolean(config[key]);
    else input.value = config[key] || '';
    input.addEventListener('change', () => {
      const next = readConfig();
      const value = input.type === 'checkbox' ? input.checked : input.value.trim();
      if (key === 'wallpaperUrl' && value && !validWallpaper(value)) {
        input.setCustomValidity('请输入 http 或 https 图片地址');
        input.reportValidity();
        return;
      }
      input.setCustomValidity('');
      next[key] = value;
      writeConfig(next);
    });
  });
}

function syncPanel() {
  const config = readConfig();
  document.getElementById(ROOT_ID)?.querySelectorAll('[data-key]').forEach((input) => {
    if (input.type === 'checkbox') input.checked = config[input.dataset.key];
    else input.value = config[input.dataset.key];
    input.setCustomValidity('');
  });
}

function runtimeSnapshot() {
  return {
    version: VERSION, active, timestamp: new Date().toISOString(),
    panelMounted: Boolean(document.getElementById(ROOT_ID)), mode: selectedMode(), queue: detectedQueue,
    badgesEnabled: readConfig().showBadges,
    targets: document.querySelectorAll(BADGE_TARGET_SELECTOR).length,
    badges: document.querySelectorAll(`[${BADGE_ATTR}]`).length,
    entries: tierCache.size, updatedAt: tierUpdatedAt ? new Date(tierUpdatedAt).toISOString() : null,
    error: lastError || storageError,
  };
}

function updateStatus() {
  const output = document.querySelector(`#${ROOT_ID} .lga-status`);
  if (!output) return;
  const state = runtimeSnapshot();
  const text = [
    `插件已加载 · ${MODE_NAMES[state.mode] || state.queue || '等待模式识别'}`,
    state.badgesEnabled ? `数据 ${state.entries} 位英雄 · 找到 ${state.targets} 个头像 · 显示 ${state.badges} 个角标` : '角标已关闭',
    state.updatedAt ? `数据更新 ${new Date(state.updatedAt).toLocaleTimeString()}` : '',
    state.error ? `${state.error}（自动重试中，可手动刷新）` : '',
    heartbeatError,
  ].filter(Boolean).join('\n');
  if (output.textContent !== text) output.textContent = text;
}

function reportRuntime() {
  if (!context?.fs?.write) { heartbeatError = '当前 Loader 未提供状态回传接口，请在客户端确认插件显示'; return; }
  runtimeWrite = runtimeWrite.then(async () => {
    try {
      const ok = await context.fs.write('runtime.json', JSON.stringify(runtimeSnapshot()));
      heartbeatError = ok ? '' : '无法写入运行状态，请检查插件目录权限';
    } catch { heartbeatError = '无法写入运行状态，请检查插件目录权限'; }
  });
  return runtimeWrite;
}

function applyVisuals() {
  const config = readConfig();
  document.body?.classList.toggle('lga-wallpaper', Boolean(config.wallpaperUrl));
  document.body?.classList.toggle('lga-hide-navigation', config.hideNavigation);
  ensureStyle(config.wallpaperUrl);
  window.clearTimeout(retryTimer);
  retryTimer = 0;
  if (config.showBadges) refreshTiers();
  else {
    tierGeneration++;
    tierPending = null;
    document.querySelectorAll(`[${BADGE_ATTR}]`).forEach((node) => node.remove());
    restorePositions();
  }
  updateStatus();
}

function ensureStyle(wallpaperUrl) {
  let style = document.getElementById(STYLE_ID);
  if (!style) {
    style = document.createElement('style');
    style.id = STYLE_ID;
    document.head.appendChild(style);
  }
  // JSON.stringify yields a CSS-safe quoted URL string, avoiding stylesheet injection.
  const escapedUrl = wallpaperUrl ? JSON.stringify(String(wallpaperUrl)) : 'none';
  style.textContent = `
    body.lga-wallpaper { background: #07101b url(${escapedUrl}) center / cover fixed !important; }
    body.lga-wallpaper::before { content: ''; position: fixed; inset: 0; z-index: -1; background: rgba(4, 10, 18, .52); pointer-events: none; }
    body.lga-hide-navigation .navigation-bar,
    body.lga-hide-navigation lol-uikit-navigation-bar,
    body.lga-hide-navigation .rcp-fe-lol-navigation { display: none !important; }
    #${ROOT_ID} { position: fixed; right: 18px; bottom: 18px; z-index: 2147483647; color: #eaf6ff; font: 12px/1.4 Arial, sans-serif; }
    #${ROOT_ID} .lga-toggle { width: 34px; height: 34px; border: 1px solid #64c8ff; border-radius: 50%; color: #fff; background: #102d4d; cursor: pointer; box-shadow: 0 4px 16px #0008; }
    #${ROOT_ID} .lga-panel { position: absolute; right: 0; bottom: 42px; width: 260px; padding: 12px; border: 1px solid #3e8fbb; border-radius: 8px; background: #0a1b2de8; box-shadow: 0 8px 28px #000b; }
    #${ROOT_ID} .lga-panel label { display: block; margin-top: 8px; }
    #${ROOT_ID} .lga-panel input[type="url"] { display: block; box-sizing: border-box; width: 100%; margin-top: 4px; padding: 5px; color: #eaf6ff; background: #10263c; border: 1px solid #397da4; }
    #${ROOT_ID} .lga-panel small { display: block; margin-top: 9px; color: #a9bfd0; }
    #${ROOT_ID} .lga-panel[hidden] { display: none !important; }
    #${ROOT_ID} .lga-panel button { margin: 10px 6px 0 0; padding: 5px 8px; background: #163c58; color: #eaf6ff; border: 1px solid #397da4; border-radius: 4px; cursor: pointer; }
    #${ROOT_ID} .lga-status { display: block; margin-top: 10px; color: #a9d5ec; white-space: pre-line; overflow-wrap: anywhere; }
    [${BADGE_ATTR}] { position: absolute; z-index: 8; padding: 2px 4px; border: 1px solid #ffd56a; border-radius: 9px; color: #1d1600; background: #ffd56a; font: 700 10px/1 Arial; pointer-events: none; filter: drop-shadow(0 1px 2px rgba(0,0,0,.95)); }
  `;
}

let badgeTimer = 0;
function scheduleBadgeInjection() {
  if (!active || badgeTimer) return;
  badgeTimer = window.setTimeout(() => { badgeTimer = 0; injectTierBadges(); }, 180);
}

/**
 * 直接挂到英雄选择缩略图和队伍英雄头像上。只读 DOM 中客户端已有的英雄 ID，
 * 不读游戏内存、不篡改任何 LCU 请求；数据来自 OP.GG 的公开统计接口。
 */
function injectTierBadges() {
  if (!active || !readConfig().showBadges) return;
  // Disconnect during our own DOM writes: observing inserted badges would loop forever.
  observer?.disconnect();
  try {
  document.querySelectorAll(BADGE_TARGET_SELECTOR).forEach((host) => {
    if (!(host instanceof HTMLElement)) return;
    const target = BADGE_TARGETS.find((item) => host.matches(item.selector));
    if (!target) return;
    const championId = resolveChampionId(host);
    const tier = tierCache.get(championId);
    const existing = host.querySelector(`:scope > [${BADGE_ATTR}]`);
    if (!tier) {
      existing?.remove();
      restorePosition(host);
      return;
    }
    if (existing?.textContent === tier) return;
    existing?.remove();
    ensurePositionContext(host);
    const badge = document.createElement('span');
    badge.setAttribute(BADGE_ATTR, 'true');
    badge.textContent = tier;
    badge.title = `OP.GG ${readConfig().tierMode} ${tier}（公开统计，可能有缓存）`;
    badge.style.left = `${target.left}px`;
    if (target.top !== undefined) badge.style.top = `${target.top}px`;
    if (target.bottom !== undefined) badge.style.bottom = `${target.bottom}px`;
    host.appendChild(badge);
  });
  updateStatus();
  } finally { if (active) observeDom(); }
}

function resolveChampionId(target) {
  const candidates = [
    target,
    target.parentElement,
    target.closest('.champion-card-component'),
    target.closest('.bench-champion'),
    target.closest('.champion-grid-champion'),
    target.closest('[data-champion-id]'),
    target.closest('[data-champion-id-value]'),
    target.closest('[data-champion-id-string]'),
  ].filter(Boolean);

  for (const element of candidates) {
    for (const attribute of ['data-champion-id', 'data-champion-id-value', 'data-champion-id-string', 'champion-id']) {
      const value = element.getAttribute(attribute);
      if (value && /^\d+$/.test(value)) return Number(value);
    }
  }
  for (const element of candidates) {
    const image = element.querySelector('img[src*="champion-icons"]');
    const source = image?.getAttribute('src') || element.outerHTML || '';
    const match = source.match(/champion-icons\/(\d+)\.png/i);
    if (match) return Number(match[1]);
    const styled = element.querySelector('[style*="champion-icons"]');
    const background = `${element.style?.backgroundImage || ''} ${styled?.style?.backgroundImage || ''}`;
    const backgroundMatch = background.match(/champion-icons\/(\d+)\.png/i);
    if (backgroundMatch) return Number(backgroundMatch[1]);
  }
  return 0;
}

function ensurePositionContext(element) {
  if (getComputedStyle(element).position !== 'static') return;
  element.setAttribute(POSITION_ATTR, element.style.position);
  element.style.position = 'relative';
}

function restorePosition(element) {
  if (!element.hasAttribute(POSITION_ATTR)) return;
  if (element.style.position === 'relative') element.style.position = element.getAttribute(POSITION_ATTR);
  element.removeAttribute(POSITION_ATTR);
}

function restorePositions() { document.querySelectorAll(`[${POSITION_ATTR}]`).forEach(restorePosition); }

async function fetchTierData(mode, force = false) {
  const normalized = ['ranked', 'aram', 'arena', 'urf'].includes(mode) ? mode : 'ranked';
  if (tierRequestMode === normalized && tierPending) return tierPending;
  const generation = ++tierGeneration;
  tierRequestMode = normalized;
  tierPending = null;
  if (!force && tierCacheMode === normalized && Date.now() - tierUpdatedAt < CACHE_TTL) return;
  if (tierCacheMode !== normalized) {
    tierCache = new Map();
    tierCacheMode = '';
    document.querySelectorAll(`[${BADGE_ATTR}]`).forEach((node) => node.remove());
  }
  const request = (async () => {
    const tier = normalized === 'arena' ? 'all' : 'gold_plus';
    const payload = await fetchJson(`https://lol-api-champion.op.gg/api/global/champions/${normalized}?tier=${tier}`);
    if (!Array.isArray(payload?.data)) throw new Error('OP.GG 数据格式不兼容');
    const next = new Map();
    for (const champion of payload?.data || []) {
      const stats = normalized === 'ranked'
        ? [...(champion.positions || [])].filter((position) => Number.isFinite(position.stats?.tier_data?.tier)).sort((a, b) => a.stats.tier_data.tier - b.stats.tier_data.tier)[0]?.stats
        : champion.average_stats;
      const value = stats?.tier_data?.tier ?? stats?.tier;
      if (champion?.id > 0 && value !== null && value !== undefined && value !== '' && Number.isFinite(Number(value)) && Number(value) >= 0 && Number(value) <= 5) {
        next.set(Number(champion.id), Number(value) === 0 ? 'OP' : `T${Number(value)}`);
      }
    }
    if (generation !== tierGeneration) return;
    tierCache = next;
    tierCacheMode = normalized;
    tierUpdatedAt = Date.now();
    document.querySelectorAll(`[${BADGE_ATTR}]`).forEach((node) => node.remove());
  })();
  tierPending = request;
  try { await request; }
  finally { if (generation === tierGeneration) tierPending = null; }
}
