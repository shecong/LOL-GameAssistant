/**
 * Optional Pengu Loader visual layer.
 *
 * This plugin deliberately only changes Chromium client DOM/CSS. Match automation,
 * LCU writes and account configuration remain in the external C# app. Player
 * summaries read only the local client's champion-select session and match history.
 * The panel uses localStorage so Pengu upgrades do not require a custom config file.
 */
const ROOT_ID = 'lol-game-assistant-visual-root';
const WALLPAPER_ID = 'lol-game-assistant-wallpaper';
const STYLE_ID = 'lol-game-assistant-visual-style';
const STORAGE_KEY = 'lol-game-assistant.visuals.v1';
const LOCAL_WALLPAPER_KEY = 'lol-game-assistant.wallpaper.local.v1';
const BADGE_ATTR = 'data-lol-game-assistant-tier-badge';
const POSITION_ATTR = 'data-lol-game-assistant-original-position';
const VERSION = '0.4.0';
let managedConfig = null;
let managedText = '';
let configTimer = 0;
let configError = '';

function pollManagedConfig() {
  if (!context?.visualControl || !active) return;
  try {
    const text = context.visualControl.readConfig();
    if (!text) return;
    if (text === managedText) { configError = ''; return; }
    const config = JSON.parse(text);
    if (!config || typeof config !== 'object' || Array.isArray(config)) throw new Error('invalid config');
    const forceRefresh = config.refreshNonce && config.refreshNonce !== managedConfig?.refreshNonce;
    managedConfig = config;
    managedText = text;
    configError = '';
    applyVisuals();
    if (forceRefresh) {
      void detectMode(); refreshTiers(true); void refreshPlayerSession(true);
      applyWallpaper(readConfig().wallpaperUrl, true);
    }
    void reportRuntime();
  } catch { configError = '无法读取 sc助手视觉设置，继续使用上次设置'; }
}

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
  showPlayerStats: true,
  tierMode: 'auto',
  hideNavigation: false,
  wallpaperUrl: '',
  wallpaperName: '',
};

function readConfig() {
  try {
    const saved = managedConfig || JSON.parse(localStorage.getItem(STORAGE_KEY) || '{}');
    if (managedConfig?.enabled === false) return { ...defaults, showBadges: false, showPlayerStats: false };
    return {
      showBadges: typeof saved?.showBadges === 'boolean' ? saved.showBadges : defaults.showBadges,
      showPlayerStats: saved?.showPlayerStats !== false,
      tierMode: ['auto', ...Object.keys(MODE_NAMES)].includes(saved?.tierMode) ? saved.tierMode : 'auto',
      hideNavigation: saved?.hideNavigation === true,
      wallpaperUrl: saved?.wallpaperUrl === 'local' ? 'local' : validWallpaper(saved?.wallpaperUrl),
      wallpaperName: typeof saved?.wallpaperName === 'string' ? saved.wallpaperName : '',
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

function isWallpaperPage(value) {
  try { const url = new URL(value); return /(^|\.)unsplash\.com$/i.test(url.hostname) && !url.hostname.startsWith('images.') && /\/photos\//.test(url.pathname); }
  catch { return false; }
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
  if (context?.visualControl) {
    pollManagedConfig();
    configTimer = window.setInterval(pollManagedConfig, 1000);
  } else mountPanel();
  applyVisuals();
  observer = new MutationObserver(() => {
    if (!context?.visualControl && document.getElementById(ROOT_ID) !== mountedRoot) mountPanel();
    scheduleBadgeInjection();
    schedulePlayerInjection();
    scheduleWallpaperSync();
  });
  observeDom();
  if (context?.socket?.observe) {
    for (const endpoint of ['/lol-gameflow/v1/session', '/lol-lobby/v2/lobby']) {
      try { subscriptions.push(context.socket.observe(endpoint, () => { void detectMode(); })); }
      catch { /* Polling remains available on older loaders. */ }
    }
  }
  startPlayerStats();
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
  stopPlayerStats();
  stopWallpaper();
  lifecycle++;
  tierGeneration++;
  tierPending = null;
  window.clearTimeout(badgeTimer);
  window.clearTimeout(retryTimer);
  window.clearInterval(refreshTimer);
  window.clearInterval(heartbeatTimer);
  window.clearInterval(configTimer);
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
    attributeFilter: ['class', 'src', 'style', 'data-champion-id', 'data-champion-id-value', 'data-champion-id-string', 'champion-id', 'data-cell-id', 'cell-id', 'data-summoner-id', 'data-puuid'],
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
      <label><input data-key="showPlayerStats" type="checkbox"> 选人开黑 / KDA / 胜率</label>
      <label>角标模式<select data-key="tierMode"><option value="auto">自动跟随客户端</option><option value="ranked">峡谷 / 排位</option><option value="aram">极地大乱斗</option><option value="arena">斗魂竞技场</option><option value="urf">无限火力</option></select></label>
      <label><input data-key="hideNavigation" type="checkbox"> 隐藏客户端导航菜单</label>
      <label>壁纸图片直链<input data-key="wallpaperUrl" type="url" placeholder="https://…/wallpaper.jpg"></label>
      <button type="button" data-action="local-wallpaper">选择本地图片</button>
      <button type="button" data-action="clear-wallpaper">清除壁纸</button>
      <input class="lga-file" type="file" accept="image/png,image/jpeg,image/webp,image/bmp,image/gif" hidden>
      <small class="lga-wallpaper-source"></small>
      <button type="button" data-action="refresh">刷新数据</button>
      <button type="button" data-action="reset">恢复默认</button>
      <output class="lga-status" aria-live="polite"></output>
      <small>v${VERSION} · 仅修改客户端展示。壁纸启用时收起首页活动宣传页，清空地址恢复。</small>
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
  root.querySelector('[data-action="refresh"]').addEventListener('click', () => { void detectMode(); refreshTiers(true); void refreshPlayerSession(true); applyWallpaper(readConfig().wallpaperUrl, true); });
  root.querySelector('[data-action="reset"]').addEventListener('click', () => { wallpaperSelection++; writeConfig({ ...defaults }); syncPanel(); });
  const fileInput = root.querySelector('.lga-file');
  root.querySelector('[data-action="local-wallpaper"]').addEventListener('click', () => { fileInput.value = ''; fileInput.click(); });
  fileInput.addEventListener('change', () => { if (fileInput.files?.[0]) void selectLocalWallpaper(fileInput.files[0]); });
  root.querySelector('[data-action="clear-wallpaper"]').addEventListener('click', () => {
    wallpaperSelection++;
    const next = readConfig();
    next.wallpaperUrl = next.wallpaperName = '';
    writeConfig(next);
    try { localStorage.removeItem(LOCAL_WALLPAPER_KEY); } catch { /* Clearing the active setting still restores the client. */ }
    syncPanel();
  });
  root.querySelectorAll('[data-key]').forEach((input) => {
    const key = input.dataset.key;
    if (input.type === 'checkbox') input.checked = Boolean(config[key]);
    else input.value = key === 'wallpaperUrl' && config[key] === 'local' ? '' : config[key] || '';
    input.addEventListener('change', () => {
      const next = readConfig();
      const value = input.type === 'checkbox' ? input.checked : input.value.trim();
      if (key === 'wallpaperUrl' && value && isWallpaperPage(value)) {
        input.setCustomValidity('这是照片介绍页，请右键照片复制图片地址（Unsplash 图片直链以 images.unsplash.com 开头）');
        input.reportValidity();
        return;
      }
      if (key === 'wallpaperUrl' && value && !validWallpaper(value)) {
        input.setCustomValidity('请输入 http 或 https 图片地址');
        input.reportValidity();
        return;
      }
      input.setCustomValidity('');
      if (key === 'wallpaperUrl') { wallpaperSelection++; next.wallpaperName = ''; }
      next[key] = value;
      writeConfig(next);
      syncWallpaperSource();
    });
  });
  syncWallpaperSource();
}

function syncPanel() {
  const config = readConfig();
  document.getElementById(ROOT_ID)?.querySelectorAll('[data-key]').forEach((input) => {
    if (input.type === 'checkbox') input.checked = config[input.dataset.key];
    else input.value = input.dataset.key === 'wallpaperUrl' && config.wallpaperUrl === 'local' ? '' : config[input.dataset.key];
    input.setCustomValidity('');
  });
  syncWallpaperSource();
}

function syncWallpaperSource() {
  const output = document.querySelector(`#${ROOT_ID} .lga-wallpaper-source`);
  if (!output) return;
  const config = readConfig();
  output.textContent = config.wallpaperUrl === 'local' ? `本地图片：${config.wallpaperName || '已保存'}（重启后保留）` : '支持图片链接或本地 JPG、PNG、WebP、BMP、GIF（静态）';
}

let wallpaperSelection = 0;
function fileDataUrl(file) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result);
    reader.onerror = () => reject(new Error('无法读取所选图片'));
    reader.readAsDataURL(file);
  });
}

async function selectLocalWallpaper(file) {
  const selection = ++wallpaperSelection;
  const currentLifecycle = lifecycle;
  try {
    if (!/^image\/(png|jpeg|webp|bmp|gif)$/.test(file.type)) throw new Error('请选择 JPG、PNG、WebP、BMP 或 GIF 图片');
    if (file.size > 20 * 1024 * 1024) throw new Error('图片超过 20 MB，请选择较小的图片');
    const image = new Image();
    image.src = await fileDataUrl(file);
    try { await image.decode(); } catch { throw new Error('无法解码图片，请选择有效的图片文件'); }
    const scale = Math.min(1, 2560 / Math.max(image.naturalWidth, image.naturalHeight));
    const canvas = document.createElement('canvas');
    canvas.width = Math.max(1, Math.round(image.naturalWidth * scale));
    canvas.height = Math.max(1, Math.round(image.naturalHeight * scale));
    const ctx = canvas.getContext('2d');
    ctx.fillStyle = '#07101b';
    ctx.fillRect(0, 0, canvas.width, canvas.height);
    ctx.drawImage(image, 0, 0, canvas.width, canvas.height);
    let data = canvas.toDataURL('image/jpeg', .85);
    if (data.length > 2 * 1024 * 1024) data = canvas.toDataURL('image/jpeg', .65);
    if (data.length > 2 * 1024 * 1024) throw new Error('图片压缩后仍过大，请选择较小的图片');
    if (!active || currentLifecycle !== lifecycle || selection !== wallpaperSelection) return;
    // Keep image bytes separate so frequent configuration reads remain small.
    try { localStorage.setItem(LOCAL_WALLPAPER_KEY, data); }
    catch { throw new Error('本地图片保存失败：客户端存储空间不足'); }
    const next = readConfig();
    next.wallpaperUrl = 'local';
    next.wallpaperName = file.name;
    writeConfig(next);
    if (readConfig().wallpaperUrl === 'local') applyWallpaper('local', true);
    syncPanel();
  } catch (error) {
    if (!active || currentLifecycle !== lifecycle || selection !== wallpaperSelection) return;
    wallpaperStatus = error.message || '本地图片加载失败';
    updateStatus();
  }
}

function runtimeSnapshot() {
  return {
    managed: Boolean(context?.visualControl), revision: managedConfig?.revision || '',
    wallpaper: wallpaperStatus, configError, enabled: managedConfig?.enabled !== false,
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
    readConfig().showPlayerStats ? `选人信息 ${document.querySelectorAll(`[${PLAYER_ATTR}]`).length} 人${playerError ? ` · ${playerError}` : ''}` : '选人信息已关闭',
    wallpaperStatus,
    heartbeatError,
  ].filter(Boolean).join('\n');
  if (output.textContent !== text) output.textContent = text;
}

function reportRuntime() {
  if (context?.visualControl) {
    try { heartbeatError = context.visualControl.reportStatus(JSON.stringify(runtimeSnapshot())) ? '' : '状态回传失败'; }
    catch { heartbeatError = '状态回传失败'; }
    return;
  }
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
  document.body?.classList.toggle('lga-hide-navigation', config.hideNavigation);
  ensureStyle();
  applyWallpaper(config.wallpaperUrl);
  if (config.showPlayerStats) { void refreshPlayerSession(); schedulePlayerInjection(); }
  else clearPlayerStats();
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

function ensureStyle() {
  let style = document.getElementById(STYLE_ID);
  if (!style) {
    style = document.createElement('style');
    style.id = STYLE_ID;
    document.head.appendChild(style);
  }
  style.textContent = `
    body.lga-wallpaper { isolation: isolate; background: #07101b !important; }
    #${WALLPAPER_ID} { position: fixed; inset: 0; z-index: -1; pointer-events: none; background-position: center; background-size: cover; background-repeat: no-repeat; }
    body.lga-wallpaper #rcp-fe-viewport-root,
    body.lga-wallpaper .rcp-fe-viewport-main,
    body.lga-wallpaper .activity-center,
    body.lga-wallpaper #activity-center,
    body.lga-wallpaper .activity-center-application { background: transparent !important; }
    /* Home promotions are cross-origin iframes: their own opaque page cannot be themed.
       Only the home activity frame is hidden; other routes and modal frames stay intact. */
    body.lga-wallpaper #activity-center .managed-iframe,
    body.lga-wallpaper #activity-center .managed-iframe-wrapper,
    body.lga-wallpaper .activity-center__contents .managed-iframe-wrapper,
    body.lga-wallpaper .activity-center .managed-iframe,
    body.lga-wallpaper .activity-center .managed-iframe-wrapper,
    body.lga-wallpaper .activity-center-contents .managed-iframe-wrapper,
    body.lga-wallpaper .activity-center-contents iframe.ember-league-client-viewport { visibility: hidden !important; pointer-events: none !important; }
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
    #${ROOT_ID} .lga-file { display: none !important; }
    #${ROOT_ID} .lga-panel button { margin: 10px 6px 0 0; padding: 5px 8px; background: #163c58; color: #eaf6ff; border: 1px solid #397da4; border-radius: 4px; cursor: pointer; }
    #${ROOT_ID} .lga-status { display: block; margin-top: 10px; color: #a9d5ec; white-space: pre-line; overflow-wrap: anywhere; }
    [${PLAYER_ATTR}] { position: absolute; left: calc(100% + 6px); top: 0; z-index: 9; box-sizing: border-box; min-width: 108px; padding: 4px 6px; border: 1px solid #456074; border-radius: 4px; background: #081522ed; color: #dce9f7; font: 11px/1.5 Arial,sans-serif; white-space: nowrap; pointer-events: none; }
    [${PLAYER_ATTR}][data-side="enemy"] { left: auto; right: calc(100% + 6px); }
    [${PLAYER_ATTR}] .lga-party { color: #e5c77b; }
    [${PLAYER_ATTR}] .lga-party[data-group="1"] { color: #72ddcf; }
    [${PLAYER_ATTR}] .lga-party[data-group="2"] { color: #d1a1ff; }
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
  if (element.querySelector(`[${BADGE_ATTR}], [${PLAYER_ATTR}]`)) return;
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

// Player information belongs to roster portraits, never to the champion picker grid.
const PLAYER_ATTR = 'data-lol-game-assistant-player-stats';
const PLAYER_TARGETS = '.champion-icon-container';
let playerSession = null;
let playerPoll = 0;
let playerTimer = 0;
let playerPending = false;
let playerError = '';
const playerHistory = new Map();

function startPlayerStats() {
  if (context?.socket?.observe) {
    try {
      subscriptions.push(context.socket.observe('/lol-champ-select/v1/session', () => { void refreshPlayerSession(); }));
    } catch { /* Polling is available on older loaders. */ }
  }
  playerPoll = window.setInterval(() => { void refreshPlayerSession(); }, 3000);
  void refreshPlayerSession();
}

function clearPlayerStats() {
  playerSession = null;
  playerHistory.clear();
  document.querySelectorAll(`[${PLAYER_ATTR}]`).forEach(node => node.remove());
  restorePositions();
}

function stopPlayerStats() {
  window.clearInterval(playerPoll);
  window.clearTimeout(playerTimer);
  playerPoll = playerTimer = 0;
  clearPlayerStats();
}

async function refreshPlayerSession(force = false) {
  if (!active || !readConfig().showPlayerStats || playerPending) return;
  playerPending = true;
  const epoch = lifecycle;
  try {
    const session = await fetchJson('/lol-champ-select/v1/session');
    if (!active || epoch !== lifecycle || !readConfig().showPlayerStats) return;
    if (!Array.isArray(session?.myTeam)) { clearPlayerStats(); return; }
    playerSession = session;
    playerError = '';
    const members = [...session.myTeam, ...(session.theirTeam || [])];
    const identities = new Set(members.map(member => member.puuid).filter(validPlayerId));
    for (const key of playerHistory.keys()) if (!identities.has(key)) playerHistory.delete(key);
    if (force) playerHistory.clear();
    schedulePlayerInjection();
    // Bound concurrency; champion/intent changes reuse history for the same player.
    const queue = [...identities].filter(id => !playerHistory.has(id) || playerHistory.get(id).retryAt <= Date.now());
    const pending = new Map(queue.map(puuid => [puuid, { loading: true }]));
    for (const [puuid, entry] of pending) playerHistory.set(puuid, entry);
    void Promise.all(Array.from({ length: Math.min(3, queue.length) }, async () => {
      while (queue.length && active && epoch === lifecycle) {
        const puuid = queue.shift();
        const entry = pending.get(puuid);
        if (playerHistory.get(puuid) !== entry) continue;
        try {
          const payload = await fetchJson(`/lol-match-history/v1/products/lol/${encodeURIComponent(puuid)}/matches?begIndex=0&endIndex=19`);
          if (!Array.isArray(payload?.games?.games)) throw new Error('战绩格式不兼容');
          if (active && epoch === lifecycle && playerHistory.get(puuid) === entry) playerHistory.set(puuid, { games: payload.games.games });
        } catch {
          if (active && epoch === lifecycle && playerHistory.get(puuid) === entry) playerHistory.set(puuid, { error: true, retryAt: Date.now() + 15000 });
        }
        schedulePlayerInjection();
      }
    }));
  } catch (error) {
    if (active && epoch === lifecycle) {
      clearPlayerStats();
      playerError = '等待选人会话';
    }
  } finally { playerPending = false; }
}

function validPlayerId(value) {
  return typeof value === 'string' && value.trim() !== '' && value !== '0' && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/i.test(value);
}

function playerSummary(games, puuid, mode) {
  const valid = [];
  const seen = new Set();
  for (const game of games || []) {
    if (game.gameId && seen.has(game.gameId)) continue;
    if (game.gameId) seen.add(game.gameId);
    if (game.gameType === 'CUSTOM_GAME' || game.gameDuration < 300) continue;
    const identity = game.participantIdentities?.find(item => item.player?.puuid === puuid);
    const participant = identity
      ? game.participants?.find(item => item.participantId === identity.participantId)
      : game.participants?.length === 1 ? game.participants[0] : null;
    const stats = participant?.stats;
    if (!stats || !['kills', 'deaths', 'assists'].every(key => typeof stats[key] === 'number' && Number.isFinite(stats[key]) && stats[key] >= 0) || typeof stats.win !== 'boolean' || stats.gameEndedInEarlySurrender === true) continue;
    valid.push({ stats, mode: modeFromQueue({ id: game.queueId, gameMode: game.gameMode }) });
  }
  const sameMode = mode ? valid.filter(item => item.mode === mode) : [];
  const sample = sameMode.length ? sameMode : valid;
  if (!sample.length) return null;
  const totals = sample.reduce((sum, { stats }) => ({ kills: sum.kills + stats.kills, deaths: sum.deaths + stats.deaths, assists: sum.assists + stats.assists, wins: sum.wins + Number(stats.win) }), { kills: 0, deaths: 0, assists: 0, wins: 0 });
  return { kda: ((totals.kills + totals.assists) / Math.max(1, totals.deaths)).toFixed(2), rate: Math.round(100 * totals.wins / sample.length), count: sample.length, scope: sameMode.length ? '同模式' : '全模式' };
}

function partyLabel(member, team) {
  const party = String(member.partyId || '').trim();
  const valid = value => value && value !== '0' && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/i.test(value);
  if (!valid(party)) return { text: '开黑未知' };
  const groups = [...new Set(team.map(item => String(item.partyId || '').trim()).filter(valid))].filter(key => team.filter(item => String(item.partyId || '').trim() === key).length >= 2).sort();
  const index = groups.indexOf(party);
  return index >= 0 ? { text: `开黑${index + 1} · ${team.filter(item => String(item.partyId || '').trim() === party).length}人`, group: String(index + 1) } : { text: '未见组队' };
}

function resolvePlayer(host) {
  if (!playerSession) return null;
  const all = [playerSession.myTeam, playerSession.theirTeam || []].flat();
  // Prefer explicit identity on the portrait or its player row. Never map by DOM index.
  let row = host;
  for (let depth = 0; row && depth < 4; depth++, row = row.parentElement) {
    if (depth && row.querySelectorAll(PLAYER_TARGETS).length > 1) break;
    for (const [attributes, field] of [[['data-cell-id', 'cell-id'], 'cellId'], [['data-puuid'], 'puuid'], [['data-summoner-id'], 'summonerId']]) {
      for (const attribute of attributes) {
        const value = row.getAttribute(attribute);
        if (value === null || (field !== 'cellId' && (!value || value === '0'))) continue;
        const matches = all.filter(member => String(member[field]) === value);
        return matches.length === 1 ? matches[0] : null;
      }
    }
  }
  // A unique selected champion is a safe fallback when the client exposes no row ID.
  const championId = resolveChampionId(host);
  const matches = all.filter(member => championId > 0 && Number(member.championId || member.championPickIntent) === championId);
  return matches.length === 1 ? matches[0] : null;
}

function schedulePlayerInjection() {
  if (!active || playerTimer) return;
  playerTimer = window.setTimeout(() => { playerTimer = 0; injectPlayerStats(); }, 180);
}

function injectPlayerStats() {
  if (!active || !readConfig().showPlayerStats) return;
  observer?.disconnect();
  try {
    document.querySelectorAll(PLAYER_TARGETS).forEach(host => {
      const member = resolvePlayer(host);
      let panel = host.querySelector(`:scope > [${PLAYER_ATTR}]`);
      if (!member) { panel?.remove(); restorePosition(host); return; }
      const ally = playerSession.myTeam.includes(member);
      const team = ally ? playerSession.myTeam : playerSession.theirTeam;
      const party = partyLabel(member, team);
      const history = playerHistory.get(member.puuid);
      const summary = history?.games ? playerSummary(history.games, member.puuid, detectedMode) : null;
      const lines = [party.text, summary ? `KDA ${summary.kda} · 胜率 ${summary.rate}%` : 'KDA — · 胜率 —', summary ? `近20场内 · ${summary.scope} ${summary.count}场` : !validPlayerId(member.puuid) ? '身份未公开' : history?.error ? '战绩暂不可用' : history?.games ? '暂无有效战绩' : '战绩加载中'];
      if (!panel) {
        ensurePositionContext(host);
        panel = document.createElement('span');
        panel.setAttribute(PLAYER_ATTR, 'true');
        host.appendChild(panel);
      }
      panel.dataset.side = ally ? 'ally' : 'enemy';
      const title = `${lines.join('；')}。KDA=(击杀+助攻)/死亡；仅统计返回的最近20场中时长至少5分钟的有效非自定义对局。开黑依据本局房间标识，缺失时无法确认。`;
      if (panel.title !== title) {
        panel.title = title;
        panel.replaceChildren(...lines.map((line, index) => {
          const node = document.createElement('div');
          node.textContent = line;
          if (index === 0) { node.className = 'lga-party'; node.dataset.group = party.group || ''; }
          return node;
        }));
      }
    });
    updateStatus();
  } finally { if (active) observeDom(); }
}


let wallpaperUrl = '';
let wallpaperLoaded = '';
let wallpaperStatus = '';
let wallpaperImage = null;
let wallpaperTimeout = 0;
let wallpaperSyncTimer = 0;
let wallpaperGeneration = 0;

function stopWallpaper() {
  wallpaperGeneration++;
  window.clearTimeout(wallpaperTimeout);
  window.clearTimeout(wallpaperSyncTimer);
  wallpaperTimeout = wallpaperSyncTimer = 0;
  if (wallpaperImage) { wallpaperImage.onload = wallpaperImage.onerror = null; wallpaperImage.src = ''; }
  wallpaperImage = null;
  wallpaperUrl = wallpaperLoaded = wallpaperStatus = '';
  document.body?.classList.remove('lga-wallpaper');
  document.getElementById(WALLPAPER_ID)?.remove();
}

function applyWallpaper(url, force = false) {
  if (!active) return;
  if (url === wallpaperUrl && !force) { syncWallpaperLayer(); return; }
  stopWallpaper();
  wallpaperUrl = url;
  if (!url) { updateStatus(); return; }
  const generation = wallpaperGeneration;
  const image = new Image();
  wallpaperImage = image;
  wallpaperStatus = '壁纸加载中…';
  image.referrerPolicy = 'no-referrer';
  const finish = success => {
    if (!active || generation !== wallpaperGeneration) return;
    window.clearTimeout(wallpaperTimeout);
    wallpaperTimeout = 0;
    image.onload = image.onerror = null;
    wallpaperImage = null;
    wallpaperLoaded = success ? source : '';
    wallpaperStatus = success ? '壁纸已加载 · 首页活动宣传页已收起' : url === 'local' ? '本地壁纸读取失败，请重新选择图片' : isWallpaperPage(url) ? '这是 Unsplash 照片介绍页，请复制图片地址（images.unsplash.com）' : '壁纸加载失败：请使用可直接打开的图片地址；可点击刷新重试';
    syncWallpaperLayer();
    updateStatus();
  };
  image.onload = () => finish(image.naturalWidth > 0);
  image.onerror = () => finish(false);
  wallpaperTimeout = window.setTimeout(() => finish(false), 12000);
  let source = url;
  if (url === 'local') {
    try { source = localStorage.getItem(LOCAL_WALLPAPER_KEY) || ''; } catch { source = ''; }
    if (!/^data:image\/jpeg;base64,/.test(source)) { finish(false); return; }
  }
  image.src = source;
  updateStatus();
}

function syncWallpaperLayer() {
  if (!active) return;
  if (!wallpaperLoaded) {
    if (document.body?.classList.contains('lga-wallpaper')) document.body.classList.remove('lga-wallpaper');
    document.getElementById(WALLPAPER_ID)?.remove();
    return;
  }
  if (!document.body?.classList.contains('lga-wallpaper')) document.body?.classList.add('lga-wallpaper');
  let layer = document.getElementById(WALLPAPER_ID);
  if (!layer) {
    layer = document.createElement('div');
    layer.id = WALLPAPER_ID;
    layer.setAttribute('aria-hidden', 'true');
    layer.style.backgroundImage = `linear-gradient(rgba(4,10,18,.35),rgba(4,10,18,.35)), url(${JSON.stringify(wallpaperLoaded)})`;
    document.body?.prepend(layer);
  }
}

function scheduleWallpaperSync() {
  if (!active || !wallpaperLoaded || wallpaperSyncTimer) return;
  wallpaperSyncTimer = window.setTimeout(() => { wallpaperSyncTimer = 0; syncWallpaperLayer(); }, 180);
}
