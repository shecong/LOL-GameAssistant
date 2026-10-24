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
let tierCache = new Map();
let tierRequestMode = '';

const defaults = {
  showBadges: true,
  tierMode: 'ranked',
  hideNavigation: false,
  wallpaperUrl: '',
};

function readConfig() {
  try {
    return { ...defaults, ...JSON.parse(localStorage.getItem(STORAGE_KEY) || '{}') };
  } catch {
    return { ...defaults };
  }
}

function writeConfig(config) {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(config));
  applyVisuals();
}

/** Pengu calls init before League client scripts: keep only the context, no DOM work. */
export function init(penguContext) {
  context = penguContext;
}

/** Pengu calls load after the client window is available. */
export function load() {
  mountPanel();
  applyVisuals();
  observer = new MutationObserver(() => scheduleBadgeInjection());
  observer.observe(document.documentElement, { childList: true, subtree: true });
}

export function unload() {
  observer?.disconnect();
  observer = null;
  document.getElementById(ROOT_ID)?.remove();
  document.getElementById(STYLE_ID)?.remove();
  document.querySelectorAll(`[${BADGE_ATTR}]`).forEach((node) => node.remove());
  document.body?.classList.remove('lga-wallpaper', 'lga-hide-navigation');
}

function mountPanel() {
  if (document.getElementById(ROOT_ID)) return;
  const config = readConfig();
  const root = document.createElement('section');
  root.id = ROOT_ID;
  root.innerHTML = `
    <button type="button" class="lga-toggle" title="LOL GameAssistant 视觉设置">✦</button>
    <div class="lga-panel" hidden>
      <strong>LOL GameAssistant · 视觉层</strong>
      <label><input data-key="showBadges" type="checkbox"> 选人 T 级角标</label>
      <label>角标模式<select data-key="tierMode"><option value="ranked">峡谷 / 排位</option><option value="aram">极地大乱斗</option><option value="arena">斗魂竞技场</option><option value="urf">无限火力</option></select></label>
      <label><input data-key="hideNavigation" type="checkbox"> 隐藏客户端导航菜单</label>
      <label>壁纸 URL<input data-key="wallpaperUrl" type="url" placeholder="https://…/wallpaper.jpg"></label>
      <small>只修改客户端展示；关闭或清空壁纸即可还原。</small>
    </div>`;
  document.body.appendChild(root);

  const panel = root.querySelector('.lga-panel');
  root.querySelector('.lga-toggle').addEventListener('click', () => { panel.hidden = !panel.hidden; });
  root.querySelectorAll('[data-key]').forEach((input) => {
    const key = input.dataset.key;
    if (input.type === 'checkbox') input.checked = Boolean(config[key]);
    else input.value = config[key] || '';
    input.addEventListener('change', () => {
      const next = readConfig();
      next[key] = input.type === 'checkbox' ? input.checked : input.value.trim();
      writeConfig(next);
    });
  });
}

function applyVisuals() {
  const config = readConfig();
  document.body?.classList.toggle('lga-wallpaper', Boolean(config.wallpaperUrl));
  document.body?.classList.toggle('lga-hide-navigation', config.hideNavigation);
  ensureStyle(config.wallpaperUrl);
  if (config.showBadges) void fetchTierData(config.tierMode).finally(scheduleBadgeInjection);
  else document.querySelectorAll(`[${BADGE_ATTR}]`).forEach((node) => node.remove());
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
    body.lga-hide-navigation [class*="navigation" i],
    body.lga-hide-navigation [class*="nav-bar" i],
    body.lga-hide-navigation [data-testid*="navigation" i] { display: none !important; }
    #${ROOT_ID} { position: fixed; right: 18px; bottom: 18px; z-index: 2147483647; color: #eaf6ff; font: 12px/1.4 Arial, sans-serif; }
    #${ROOT_ID} .lga-toggle { width: 34px; height: 34px; border: 1px solid #64c8ff; border-radius: 50%; color: #fff; background: #102d4d; cursor: pointer; box-shadow: 0 4px 16px #0008; }
    #${ROOT_ID} .lga-panel { position: absolute; right: 0; bottom: 42px; width: 260px; padding: 12px; border: 1px solid #3e8fbb; border-radius: 8px; background: #0a1b2de8; box-shadow: 0 8px 28px #000b; }
    #${ROOT_ID} .lga-panel label { display: block; margin-top: 8px; }
    #${ROOT_ID} .lga-panel input[type="url"] { display: block; box-sizing: border-box; width: 100%; margin-top: 4px; padding: 5px; color: #eaf6ff; background: #10263c; border: 1px solid #397da4; }
    #${ROOT_ID} .lga-panel small { display: block; margin-top: 9px; color: #a9bfd0; }
    [${BADGE_ATTR}] { position: absolute; z-index: 8; padding: 2px 4px; border: 1px solid #ffd56a; border-radius: 9px; color: #1d1600; background: #ffd56a; font: 700 10px/1 Arial; pointer-events: none; filter: drop-shadow(0 1px 2px rgba(0,0,0,.95)); }
  `;
}

let badgeTimer = 0;
function scheduleBadgeInjection() {
  window.clearTimeout(badgeTimer);
  badgeTimer = window.setTimeout(injectTierBadges, 180);
}

/**
 * 直接挂到英雄选择缩略图和队伍英雄头像上。只读 DOM 中客户端已有的英雄 ID，
 * 不读游戏内存、不篡改任何 LCU 请求；数据来自 OP.GG 的公开统计接口。
 */
function injectTierBadges() {
  if (!readConfig().showBadges) return;
  document.querySelectorAll(BADGE_TARGET_SELECTOR).forEach((host) => {
    if (!(host instanceof HTMLElement)) return;
    const target = BADGE_TARGETS.find((item) => host.matches(item.selector));
    if (!target) return;
    const championId = resolveChampionId(host);
    const tier = tierCache.get(championId);
    const existing = host.querySelector(`:scope > [${BADGE_ATTR}]`);
    if (!tier) {
      existing?.remove();
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

async function fetchTierData(mode) {
  const normalized = ['ranked', 'aram', 'arena', 'urf'].includes(mode) ? mode : 'ranked';
  if (tierRequestMode === normalized && tierCache.size > 0) return;
  tierRequestMode = normalized;
  const tier = normalized === 'arena' ? 'all' : 'gold_plus';
  const response = await fetch(`https://lol-api-champion.op.gg/api/global/champions/${normalized}?tier=${tier}`);
  if (!response.ok) throw new Error(`OP.GG tier request failed: ${response.status}`);
  const payload = await response.json();
  const next = new Map();
  for (const champion of payload?.data || []) {
    const stats = normalized === 'ranked'
      ? [...(champion.positions || [])].filter((position) => Number.isFinite(position.stats?.tier_data?.tier)).sort((a, b) => a.stats.tier_data.tier - b.stats.tier_data.tier)[0]?.stats
      : champion.average_stats;
    const value = stats?.tier_data?.tier ?? stats?.tier;
    if (champion?.id > 0 && Number.isFinite(Number(value)) && Number(value) >= 0 && Number(value) <= 5) {
      next.set(Number(champion.id), Number(value) === 0 ? 'OP' : `T${Number(value)}`);
    }
  }
  tierCache = next;
  document.querySelectorAll(`[${BADGE_ATTR}]`).forEach((node) => node.remove());
}
