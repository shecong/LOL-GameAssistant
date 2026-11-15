/**
 * Optional Pengu Loader visual layer.
 *
 * This plugin changes client DOM/CSS and reads local champion-select data.
 * Explicit counter-card clicks may preselect a champion through the LCU pick action. Player
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
const VERSION = '0.4.4';
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
      counterCache.clear(); counterIndexAt = 0; closeCounter();
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
  showCounters: true,
  tierMode: 'auto',
  hideNavigation: false,
  wallpaperUrl: '',
  wallpaperName: '',
};

function readConfig() {
  try {
    const saved = managedConfig || JSON.parse(localStorage.getItem(STORAGE_KEY) || '{}');
    if (managedConfig?.enabled === false) return { ...defaults, showBadges: false, showPlayerStats: false, showCounters: false };
    return {
      showBadges: typeof saved?.showBadges === 'boolean' ? saved.showBadges : defaults.showBadges,
      showPlayerStats: saved?.showPlayerStats !== false,
      showCounters: saved?.showCounters !== false,
      tierMode: ['auto', ...Object.keys(MODE_NAMES)].includes(saved?.tierMode) ? saved.tierMode : 'auto',
      hideNavigation: saved?.hideNavigation === true,
      wallpaperUrl: saved?.wallpaperEnabled === false ? '' : saved?.wallpaperUrl === 'local' ? 'local' : validWallpaper(saved?.wallpaperUrl),
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
    suppressNativeChampionPreview();
    if (counterHost && (!counterHost.isConnected || resolveChampionId(counterHost) !== counterChampion)) closeCounter();
  });
  observeDom();
  if (context?.socket?.observe) {
    for (const endpoint of ['/lol-gameflow/v1/session', '/lol-lobby/v2/lobby']) {
      try { subscriptions.push(context.socket.observe(endpoint, () => { void detectMode(); })); }
      catch { /* Polling remains available on older loaders. */ }
    }
  }
  startCounters();
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
  stopCounters();
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
  document.body?.classList.remove('lga-wallpaper', 'lga-hide-navigation', 'lga-counters');
  void reportRuntime();
}

function onStorage(event) { if (event.key === STORAGE_KEY) { syncPanel(); applyVisuals(); } }

function observeDom() {
  observer?.observe(document.documentElement, {
    childList: true, subtree: true, attributes: true,
    attributeFilter: ['class', 'src', 'style', 'data-champion-id', 'data-champion-id-value', 'data-champion-id-string', 'champion-id', 'data-cell-id', 'cell-id', 'data-summoner-id', 'data-puuid', 'puuid', 'data-id'],
  });
}

async function fetchJson(url, options = {}) {
  const controller = new AbortController();
  requests.add(controller);
  const timeout = window.setTimeout(() => controller.abort(), 10000);
  try {
    const response = await fetch(url, { ...options, signal: controller.signal });
    if (!response.ok) throw new Error(`请求失败：HTTP ${response.status}`);
    return response.status === 204 ? null : await response.json();
  } finally { window.clearTimeout(timeout); requests.delete(controller); }
}

function modeFromQueue(queue) {
  const mode = String(queue?.gameMode || '').toUpperCase();
  const id = Number(queue?.id ?? queue?.queueId);
  if (mode.includes('ARAM') || [450, 2400].includes(id)) return 'aram';
  if (mode === 'CHERRY' || [1700, 1710].includes(id)) return 'arena';
  if (mode.includes('URF') || [900, 1900].includes(id)) return 'urf';
  if (mode === 'CLASSIC' || mode === 'PRACTICETOOL' || [400, 420, 430, 440, 490, 700, 830, 840, 850, 3140].includes(id)) return 'ranked';
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
      <label><input data-key="showCounters" type="checkbox"> 英雄克制悬停卡片</label>
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
    countersEnabled: readConfig().showCounters, counterMounted: Boolean(counterRoot?.isConnected),
    playerTargets: document.querySelectorAll(PLAYER_TARGETS).length,
    playerPanels: document.querySelectorAll(`[${PLAYER_ATTR}]`).length, playerError,
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
  document.body?.classList.toggle('lga-counters', config.showCounters);
  ensureStyle();
  suppressNativeChampionPreview();
  if (!config.showCounters) closeCounter();
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
    body.lga-counters .champion-preview { display: none !important; }
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
    #lol-game-assistant-counters { position: fixed; z-index: 2147483646; width: 310px; max-height: min(620px, calc(100vh - 16px)); overflow: auto; box-sizing: border-box; padding: 12px; background: #091724f5; border: 1px solid #7b6942; border-radius: 4px; color: #dce9f7; font: 12px/1.4 Arial,sans-serif; box-shadow: 0 4px 18px #0009; }
    #lol-game-assistant-counters header { display: flex; align-items: center; gap: 8px; padding: 0; background: transparent; }
    #lol-game-assistant-counters strong { flex: 1; color: #f0e6d2; }
    #lol-game-assistant-counters button, #lol-game-assistant-counters select { background: #123046; border: 1px solid #456074; color: #e5d39c; border-radius: 3px; padding: 3px 6px; cursor: pointer; font: inherit; }
    #lol-game-assistant-counters button:disabled { opacity: .45; cursor: default; }
    #lol-game-assistant-counters h4 { font-size: 12px; margin: 10px 0 4px; color: #e5c77b; }
    #lol-game-assistant-counters .lga-counter-row { display: flex; align-items: center; gap: 6px; margin: 4px 0; }
    #lol-game-assistant-counters .lga-counter-row img { width: 25px; height: 25px; }
    #lol-game-assistant-counters .lga-counter-name { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    #lol-game-assistant-counters .lga-counter-rate { font-variant-numeric: tabular-nums; color: #a9d5ec; }
    #lol-game-assistant-counters .lga-counter-note { color: #a9bfd0; margin-top: 7px; font-size: 11px; }
    #lol-game-assistant-counters [role="status"] { min-height: 17px; margin-top: 5px; overflow-wrap: anywhere; }
    [${PLAYER_ATTR}] { position: absolute; left: calc(100% + 10px); top: calc(100% - 18px); z-index: 2; box-sizing: border-box; width: 110px; padding: 0; border: 0; background: transparent; color: #dce9f7; font: 10px/1 Arial,sans-serif; white-space: nowrap; pointer-events: none; text-shadow: 0 1px 2px #000; }
    [${PLAYER_ATTR}][data-side="enemy"] { left: auto; right: calc(100% + 10px); text-align: right; }
    [${PLAYER_ATTR}] > div { overflow: hidden; text-overflow: ellipsis; }
    [${PLAYER_ATTR}] .lga-party { color: #e5c77b; }
    [${PLAYER_ATTR}] .lga-party[data-group="1"] { color: #72ddcf; }
    [${PLAYER_ATTR}] .lga-party[data-group="2"] { color: #d1a1ff; }
    [${BADGE_ATTR}] { position: absolute; z-index: 8; padding: 1px 2px; border: 0; color: #ffd56a; background: transparent; font: 700 10px/1 Arial; pointer-events: none; filter: drop-shadow(0 1px 2px rgba(0,0,0,.95)); }
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
    target.closest('.grid-champion'),
    target.closest('[data-champion-id]'),
    target.closest('[data-champion-id-value]'),
    target.closest('[data-champion-id-string]'),
  ].filter(Boolean);

  const gridId = target.closest('.grid-champion')?.getAttribute('data-id');
  if (gridId && /^\d+$/.test(gridId)) return Number(gridId);
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

function playerPartyId(member) {
  const value = String(member.partyId || '').trim();
  return validPlayerId(value) ? value : '';
}

function partyLabels(team) {
  const labels = new Map();
  const groups = [...new Set(team.map(playerPartyId).filter(Boolean))].sort();
  let index = 0;
  for (const id of groups) {
    const members = team.filter(member => playerPartyId(member) === id);
    if (members.length < 2) continue;
    index++;
    for (const member of members) labels.set(member, { text: `开黑${index} · ${members.length}人`, group: String(index), detail: '本局相同房间标识' });
  }
  // LCU history summaries often contain just their owner's participant. Merge them
  // by game ID and team ID, rather than requiring a complete ten-player match.
  const games = new Map();
  const identities = new Set(team.map(member => member.puuid).filter(validPlayerId));
  for (const puuid of identities) {
    for (const game of (playerHistory.get(puuid)?.games || []).slice(0, 20)) {
      if (!(Number(game.gameId) > 0) || String(game.gameId) === String(playerSession?.gameId) || game.gameType !== 'MATCHED_GAME' || !(game.gameDuration >= 300)) continue;
      const key = `${game.platformId || ''}:${game.gameId}`;
      let players = games.get(key);
      if (!players) games.set(key, players = new Map());
      for (const identity of game.participantIdentities || []) {
        const id = identity.player?.puuid;
        if (!identities.has(id)) continue;
        const participant = game.participants?.find(item => item.participantId === identity.participantId);
        const side = Number(participant?.teamId);
        if (![100, 200].includes(side) || participant?.stats?.gameEndedInEarlySurrender === true) continue;
        // Conflicting summaries are not evidence of being on the same team.
        players.set(id, players.has(id) && players.get(id) !== side ? 0 : side);
      }
    }
  }
  let remaining = team.filter(member => !labels.has(member) && validPlayerId(member.puuid))
    .filter((member, i, all) => all.findIndex(other => other.puuid === member.puuid) === i)
    .sort((a, b) => a.puuid.localeCompare(b.puuid));
  if (remaining.length > 10) return labels;
  while (remaining.length >= 2) {
    let best = null;
    for (let mask = 1; mask < (1 << remaining.length); mask++) {
      const members = remaining.filter((_, i) => mask & (1 << i));
      if (members.length < 2 || (best && members.length <= best.members.length)) continue;
      if (new Set(members.map(playerPartyId).filter(Boolean)).size > 1) continue;
      const count = [...games.values()].filter(players => {
        const side = players.get(members[0].puuid);
        return side > 0 && members.every(member => players.get(member.puuid) === side);
      }).length;
      // Require the entire group to share two matches; a chain of pairs is not a trio.
      if (count >= 2) best = { members, count };
    }
    if (!best) break;
    index++;
    for (const member of best.members) labels.set(member, { text: `疑似开黑${index} · ${best.members.length}人`, group: String(index), detail: `近期20场内整组共同同队${best.count}场，不能确认本局预组队` });
    remaining = remaining.filter(member => !labels.has(member));
  }
  return labels;
}

function resolvePlayer(host) {
  if (!playerSession) return null;
  const all = [playerSession.myTeam, playerSession.theirTeam || []].flat();
  // Prefer explicit identity on the portrait or its player row. Never map by DOM index.
  let row = host;
  for (let depth = 0; row && depth < 4; depth++, row = row.parentElement) {
    if (depth && row.querySelectorAll(PLAYER_TARGETS).length > 1) break;
    for (const [attributes, field] of [[['data-cell-id', 'cell-id'], 'cellId'], [['data-puuid', 'puuid'], 'puuid'], [['data-summoner-id'], 'summonerId']]) {
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
    const allyParties = partyLabels(playerSession?.myTeam || []);
    const enemyParties = partyLabels(playerSession?.theirTeam || []);
    document.querySelectorAll(PLAYER_TARGETS).forEach(host => {
      const member = resolvePlayer(host);
      let panel = host.querySelector(`:scope > [${PLAYER_ATTR}]`);
      if (!member) { panel?.remove(); restorePosition(host); return; }
      const ally = playerSession.myTeam.includes(member);
      const party = (ally ? allyParties : enemyParties).get(member) || { text: '' };
      const history = playerHistory.get(member.puuid);
      const summary = history?.games ? playerSummary(history.games, member.puuid, detectedMode) : null;
      const statsLine = summary ? `KDA ${summary.kda} · 胜率 ${summary.rate}%` : 'KDA — · 胜率 —';
      const lines = [party.text, statsLine, summary ? `近20场内 · ${summary.scope} ${summary.count}场` : !validPlayerId(member.puuid) ? '身份未公开' : history?.error ? '战绩暂不可用' : history?.games ? '暂无有效战绩' : '战绩加载中'].filter(Boolean);
      if (!panel) {
        ensurePositionContext(host);
        panel = document.createElement('span');
        panel.setAttribute(PLAYER_ATTR, 'true');
        host.appendChild(panel);
      }
      panel.dataset.side = ally ? 'ally' : 'enemy';
      // Use the native name's bottom edge; client UI scaling must not change offsets.
      const name = host.closest('.summoner-object')?.querySelector('.player-details .summoner-name');
      const portraitBox = host.getBoundingClientRect();
      const nameBox = name?.getBoundingClientRect();
      const scale = host.offsetHeight ? portraitBox.height / host.offsetHeight : 0;
      panel.style.top = nameBox?.height && scale > 0
        ? `${(nameBox.bottom - portraitBox.top) / scale + 1}px` : '';

      const title = `${lines.join('；')}。KDA=(击杀+助攻)/死亡；仅统计返回的最近20场中时长至少5分钟的有效非自定义对局。${party.detail || '未识别到开黑分组'}。`;
      if (panel.title !== title) {
        panel.title = title;
        panel.replaceChildren(...[...(party.text ? [party.text] : []), statsLine].map((line, index) => {
          const node = document.createElement('div');
          node.textContent = line;
          if (party.text && index === 0) { node.className = 'lga-party'; node.dataset.group = party.group || ''; }
          return node;
        }));
      }
    });
    updateStatus();
  } finally { if (active) observeDom(); }
}


const COUNTER_ID = 'lol-game-assistant-counters';
const COUNTER_ROLES = { top: '上路', jungle: '打野', mid: '中路', adc: '下路', support: '辅助' };
let counterHost = null;
let counterChampion = 0;
let counterRoot = null;
let counterHideTimer = 0;
let counterGeneration = 0;
let counterCatalog = null;
let counterCatalogPending = null;
let counterIndex = null;
let counterIndexPending = null;
let counterIndexAt = 0;
let counterPickPending = false;
const counterCache = new Map();

function startCounters() {
  document.addEventListener('play', onNativePreviewPlay, true);
  document.addEventListener('mouseover', onCounterOver);
  document.addEventListener('mouseout', onCounterOut);
  document.addEventListener('keydown', onCounterKey);
  window.addEventListener('resize', closeCounter);
}

function stopCounters() {
  document.removeEventListener('play', onNativePreviewPlay, true);
  document.removeEventListener('mouseover', onCounterOver);
  document.removeEventListener('mouseout', onCounterOut);
  document.removeEventListener('keydown', onCounterKey);
  window.removeEventListener('resize', closeCounter);
  closeCounter();
}

// Only the native hover skill preview is suppressed; champion splashes and wallpapers
// retain their playback. Capture play because media events do not bubble.
function onNativePreviewPlay(event) {
  if (active && readConfig().showCounters && event.target instanceof HTMLMediaElement && event.target.closest('.champion-preview')) event.target.pause();
}

function suppressNativeChampionPreview() {
  if (!active || !readConfig().showCounters) return;
  document.querySelectorAll('.champion-preview video, .champion-preview audio').forEach(media => {
    if (!media.paused) media.pause();
  });
}

function closeCounter() {
  window.clearTimeout(counterHideTimer);
  counterHideTimer = 0;
  counterGeneration++;
  counterRoot?.remove();
  counterRoot = counterHost = null;
  counterChampion = 0;
}

function onCounterKey(event) { if (event.key === 'Escape') closeCounter(); }
function onCounterOut(event) {
  if (!counterRoot) return;
  if (counterRoot.contains(event.relatedTarget) || counterHost?.contains(event.relatedTarget)) return;
  window.clearTimeout(counterHideTimer);
  counterHideTimer = window.setTimeout(closeCounter, 350);
}

function onCounterOver(event) {
  if (!active || !readConfig().showCounters || !(event.target instanceof Element)) return;
  if (counterRoot?.contains(event.target)) { window.clearTimeout(counterHideTimer); return; }
  const host = event.target.closest(BADGE_TARGET_SELECTOR);
  if (!host || !host.closest('.champion-select, .champion-select-summoner-array-container, .champion-grid, .summoner-object')) return;
  const championId = resolveChampionId(host);
  if (!championId) return;
  window.clearTimeout(counterHideTimer);
  if (host === counterHost && championId === counterChampion) return;
  closeCounter();
  counterHost = host;
  counterChampion = championId;
  counterRoot = document.createElement('section');
  counterRoot.id = COUNTER_ID;
  counterRoot.setAttribute('aria-label', '英雄克制');
  document.body.appendChild(counterRoot);
  void renderCounter(championId);
}

function counterText(tag, text, className = '') {
  const node = document.createElement(tag);
  node.textContent = text;
  if (className) node.className = className;
  return node;
}

function positionCounter() {
  if (!counterHost?.isConnected || !counterRoot) return;
  const host = counterHost.getBoundingClientRect(), card = counterRoot.getBoundingClientRect();
  const width = window.innerWidth, height = window.innerHeight;
  let left = host.right + 8;
  if (left + card.width > width - 8) left = host.left - card.width - 8;
  counterRoot.style.left = `${Math.max(8, Math.min(left, width - card.width - 8))}px`;
  counterRoot.style.top = `${Math.max(8, Math.min(host.top, height - card.height - 8))}px`;
}

async function loadCounterCatalog() {
  if (counterCatalog) return counterCatalog;
  if (!counterCatalogPending) {
    counterCatalogPending = fetchJson('/lol-game-data/assets/v1/champion-summary.json').then(data => {
      if (!Array.isArray(data)) throw new Error('英雄名称暂不可用');
      return counterCatalog = new Map(data.filter(item => item.id > 0).map(item => [item.id, item.name]));
    }).finally(() => { counterCatalogPending = null; });
  }
  return counterCatalogPending;
}

async function loadCounterIndex() {
  if (counterIndex && Date.now() - counterIndexAt < CACHE_TTL) return counterIndex;
  if (!counterIndexPending) {
    counterIndexPending = fetchJson('https://lol-api-champion.op.gg/api/global/champions/ranked?tier=gold_plus').then(payload => {
      if (!Array.isArray(payload?.data)) throw new Error('对位数据格式不兼容');
      counterIndexAt = Date.now();
      return counterIndex = new Map(payload.data.map(item => [item.id, item]));
    }).finally(() => { counterIndexPending = null; });
  }
  return counterIndexPending;
}

function normalizeCounters(entries, championId) {
  if (!Array.isArray(entries)) throw new Error('对位数据格式不兼容');
  const seen = new Set();
  return entries.filter(item => Number.isInteger(item.champion_id) && item.champion_id > 0 && item.champion_id !== championId &&
    Number.isFinite(item.play) && item.play >= 100 && Number.isFinite(item.win) && item.win >= 0 && item.win <= item.play)
    .filter(item => !seen.has(item.champion_id) && seen.add(item.champion_id))
    .map(item => ({ id: item.champion_id, games: item.play, rate: 100 * (1 - item.win / item.play) }));
}

async function loadCounters(championId, role) {
  const key = `${championId}:${role}`;
  const cached = counterCache.get(key);
  if (cached && Date.now() - cached.at < CACHE_TTL) return cached.pending;
  const entry = { at: Date.now(), pending: null };
  entry.pending = fetchJson(`https://lol-api-champion.op.gg/api/global/champions/ranked/${championId}/${role}/counters?tier=gold_plus`)
    .then(payload => normalizeCounters(payload?.data, championId)).catch(error => { if (counterCache.get(key) === entry) counterCache.delete(key); throw error; });
  if (counterCache.size >= 100) counterCache.delete(counterCache.keys().next().value);
  counterCache.set(key, entry);
  return entry.pending;
}

async function renderCounter(championId, requestedRole = '') {
  const generation = ++counterGeneration, root = counterRoot;
  if (!root) return;
  const current = () => active && readConfig().showCounters && generation === counterGeneration && root === counterRoot;
  root.replaceChildren(counterText('strong', '英雄克制'), counterText('div', '加载对位数据…', 'lga-counter-note'));
  positionCounter();
  if (selectedMode() !== 'ranked') {
    root.replaceChildren(counterText('strong', '英雄克制'), counterText('div', '当前模式暂无分路克制数据', 'lga-counter-note'));
    positionCounter(); return;
  }
  try {
    const [catalog, index] = await Promise.all([loadCounterCatalog(), loadCounterIndex()]);
    if (!current()) return;
    const positions = (index.get(championId)?.positions || []).filter(item => COUNTER_ROLES[String(item.name).toLowerCase()])
      .sort((a, b) => (b.stats?.play || 0) - (a.stats?.play || 0));
    const role = requestedRole || String(positions[0]?.name || '').toLowerCase();
    if (!role) throw new Error('该英雄暂无分路克制数据');
    const rows = await loadCounters(championId, role);
    if (!current()) return;
    const name = catalog.get(championId) || `英雄 ${championId}`;
    const header = document.createElement('header');
    header.appendChild(counterText('strong', `${name} · 英雄克制`));
    const select = document.createElement('select');
    select.setAttribute('aria-label', '克制数据分路');
    for (const position of positions) {
      const value = String(position.name).toLowerCase();
      const option = counterText('option', COUNTER_ROLES[value]); option.value = value; select.appendChild(option);
    }
    select.value = role;
    select.addEventListener('change', () => { void renderCounter(championId, select.value); });
    const close = counterText('button', '×'); close.type = 'button'; close.setAttribute('aria-label', '关闭英雄克制'); close.addEventListener('click', closeCounter);
    header.append(select, close);
    root.replaceChildren(header, counterText('div', `列表英雄对 ${name} 的对位胜率`, 'lga-counter-note'));
    const status = counterText('div', ''); status.setAttribute('role', 'status');
    const addGroup = (title, values) => {
      root.appendChild(counterText('h4', title));
      if (!values.length) root.appendChild(counterText('div', '暂无足够样本', 'lga-counter-note'));
      for (const row of values.slice(0, 5)) {
        const node = document.createElement('div'); node.className = 'lga-counter-row';
        const image = document.createElement('img'); image.src = `/lol-game-data/assets/v1/champion-icons/${row.id}.png`; image.alt = '';
        const button = counterText('button', '选用'); button.type = 'button'; button.dataset.championId = row.id;
        button.title = '预选该英雄，保留手动锁定'; button.disabled = counterPickPending;
        button.addEventListener('click', async () => {
          status.textContent = '正在检查选人状态…';
          try { await pickCounterChampion(row.id); status.textContent = `已预选 ${catalog.get(row.id) || row.id}`; }
          catch (error) { status.textContent = error.message || '选用失败，请重试'; }
        });
        const rate = counterText('span', `${row.rate.toFixed(1)}%`, 'lga-counter-rate'); rate.title = `${row.games.toLocaleString()} 场对位样本`;
        node.append(image, counterText('span', catalog.get(row.id) || `英雄 ${row.id}`, 'lga-counter-name'), rate, button);
        root.appendChild(node);
      }
    };
    addGroup('克制他的英雄', rows.filter(row => row.rate > 50).sort((a, b) => b.rate - a.rate || b.games - a.games));
    addGroup('他克制的英雄', rows.filter(row => row.rate < 50).sort((a, b) => a.rate - b.rate || b.games - a.games));
    root.append(counterText('div', 'OP.GG · 全球黄金以上 · 至少100场 · 点击选用后手动锁定', 'lga-counter-note'), status);
    positionCounter();
  } catch (error) {
    if (!current()) return;
    const retry = counterText('button', '重试'); retry.type = 'button'; retry.addEventListener('click', () => { void renderCounter(championId, requestedRole); });
    root.replaceChildren(counterText('strong', '英雄克制'), counterText('div', error.message || '对位数据暂不可用', 'lga-counter-note'), retry);
    positionCounter();
  }
}

function localPickAction(session) {
  if (!Number.isInteger(session?.localPlayerCellId) || !Array.isArray(session?.actions)) return null;
  return session.actions.flat().find(action => Number.isInteger(action?.id) && action.actorCellId === session.localPlayerCellId &&
    action.type === 'pick' && action.isInProgress === true && action.completed === false) || null;
}

function assertPickable(session, championId, allowed, disabled) {
  const action = localPickAction(session);
  if (!action) throw new Error('当前未轮到你选英雄，或已经锁定');
  if (!Array.isArray(allowed) || !Array.isArray(disabled)) throw new Error('无法确认英雄是否可选');
  const banned = [...(session.bans?.myTeamBans || []), ...(session.bans?.theirTeamBans || []),
    ...session.actions.flat().filter(item => item.type === 'ban' && item.completed).map(item => item.championId)];
  const picked = [...(session.myTeam || []), ...(session.theirTeam || [])].filter(member => member.cellId !== session.localPlayerCellId).map(member => member.championId);
  if (!allowed.includes(championId) || disabled.includes(championId) || banned.includes(championId) || picked.includes(championId)) throw new Error('该英雄不可选、已被禁用或已被选用');
  return action;
}

async function pickCounterChampion(championId) {
  if (counterPickPending) throw new Error('正在选用，请稍候');
  if (!active || !readConfig().showCounters || !Number.isInteger(championId) || championId <= 0) throw new Error('英雄克制功能已关闭');
  counterPickPending = true;
  document.querySelectorAll(`#${COUNTER_ID} button[data-champion-id]`).forEach(button => { button.disabled = true; });
  const epoch = lifecycle;
  try {
    const [session, allowed, disabled] = await Promise.all([
      fetchJson('/lol-champ-select/v1/session'), fetchJson('/lol-champ-select/v1/pickable-champion-ids'), fetchJson('/lol-champ-select/v1/disabled-champion-ids'),
    ]);
    const originalAction = assertPickable(session, championId, allowed, disabled);
    const fresh = await fetchJson('/lol-champ-select/v1/session');
    const action = assertPickable(fresh, championId, allowed, disabled);
    if (!active || lifecycle !== epoch || !readConfig().showCounters || originalAction.id !== action.id || session.localPlayerCellId !== fresh.localPlayerCellId || session.gameId !== fresh.gameId) throw new Error('选人状态已变化，请重试');
    // Same action endpoint as Select_Api.PerformActionAsync; an explicit click only preselects.
    await fetchJson(`/lol-champ-select/v1/session/actions/${action.id}`, { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ championId, completed: false }) });
    void refreshPlayerSession();
  } finally {
    counterPickPending = false;
    document.querySelectorAll(`#${COUNTER_ID} button[data-champion-id]`).forEach(button => { button.disabled = false; });
  }
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
