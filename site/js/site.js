import { ART, P } from './iso-art.js';
import { PT, LABELS } from './i18n.js';

const { gsap, ScrollTrigger, MotionPathPlugin, DrawSVGPlugin } = window;
gsap.registerPlugin(ScrollTrigger, MotionPathPlugin, DrawSVGPlugin);

const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];
const NS = 'http://www.w3.org/2000/svg';
const C = Math.cos(Math.PI / 6);
const REPO = 'https://github.com/gabrielandre-math/job-orchestrator';
const DL = `${REPO}/releases/latest/download/`;

function el(tag, attrs = {}, ...kids) {
  const e = document.createElementNS(NS, tag);
  for (const [k, v] of Object.entries(attrs)) if (v != null) e.setAttribute(k, v);
  e.append(...kids);
  return e;
}

// Isometric drawing: the app's projection (iso-art.ts) and the same face classes as jo-iso.
const r2 = (n) => Math.round(n * 100) / 100;
const pt = (v) => P(v).map(r2).join(' ');
const poly = (pts) => 'M ' + pts.map(pt).join(' L ') + ' Z';
const polyline = (pts) => 'M ' + pts.map(pt).join(' L ');
const shift = (pts, o) => pts.map(([a, b, c]) => [a + o[0], b + o[1], c + o[2]]);
function faces({ x, y, z, w, d, h }) {
  return [
    poly([[x, y + d, z], [x + w, y + d, z], [x + w, y + d, z + h], [x, y + d, z + h]]),
    poly([[x + w, y, z], [x + w, y + d, z], [x + w, y + d, z + h], [x + w, y, z + h]]),
    poly([[x, y, z + h], [x + w, y, z + h], [x + w, y + d, z + h], [x, y + d, z + h]]),
  ];
}
function boxEl(b, i, o = [0, 0, 0], mat = b.m) {
  const g = el('g', { class: `b b${i}` });
  faces({ ...b, x: b.x + o[0], y: b.y + o[1], z: b.z + o[2] }).forEach((d, k) => g.append(el('path', { d, class: `iso-f iso-${'lrt'[k]} iso-${mat}` })));
  for (const l of b.lines ?? []) g.append(el('path', { d: polyline(shift(l.pts, o)), class: `iso-line iso-${l.tone}`, 'stroke-width': l.w ?? 1.4 }));
  return g;
}
function station(name, o) {
  const g = el('g', { class: `st st-${name}` });
  (ART[name] ?? ART.model).forEach((b, i) => g.append(boxEl(b, i, o)));
  return g;
}
function icon(name) {
  const s = el('svg', { viewBox: '-34 -24 68 62', 'aria-hidden': 'true' });
  s.append(station(name));
  return s;
}

// A small idle motion for each station, the same spirit as the README icons.
function idle(name, g, o = [0, 0, 0]) {
  const tl = gsap.timeline({ repeat: -1, paused: true });
  const B = (i) => g.querySelector(`.b${i}`);
  const all = (i, s = '.iso-accent') => B(i)?.querySelectorAll(s) ?? [];
  switch (name) {
    case 'radar': {
      const sweep = B(0).querySelector('.iso-accent');
      const s = { a: Math.atan2(-9, 12) };
      tl.to(s, { a: `+=${Math.PI * 2}`, duration: 4.5, ease: 'none', onUpdate: () => sweep.setAttribute('d', polyline(shift([[18, 18, 4], [18 + Math.cos(s.a) * 15, 18 + Math.sin(s.a) * 15, 4]], o))) });
      break;
    }
    case 'chart': {
      const bar = ART.chart[3];
      const ps = B(3).querySelectorAll('path');
      const s = { h: bar.h };
      const draw = () => faces({ ...bar, h: s.h, x: bar.x + o[0], y: bar.y + o[1], z: bar.z + o[2] }).forEach((d, k) => ps[k].setAttribute('d', d));
      tl.to(s, { h: 9, duration: 1.3, ease: 'power2.inOut', onUpdate: draw }).to(s, { h: bar.h, duration: 1.1, ease: 'back.out(2.2)', onUpdate: draw }).to({}, { duration: .9 });
      break;
    }
    case 'writer': tl.to([B(2), B(3)], { x: -6 * C, y: -3, duration: .9, ease: 'sine.inOut', yoyo: true, repeat: 1 }).to({}, { duration: .5 }); break;
    case 'guardian': tl.to(all(1), { opacity: .2, duration: .8, ease: 'sine.inOut', yoyo: true, repeat: 1 }).to({}, { duration: .4 }); break;
    case 'inbox': tl.to(B(2), { y: -3.5, duration: .9, ease: 'sine.inOut', yoyo: true, repeat: 1 }).to({}, { duration: .6 }); break;
    case 'model': tl.to(B(ART.model.length - 1), { opacity: .4, duration: .8, ease: 'sine.inOut', yoyo: true, repeat: 1 }); break;
    case 'router': tl.to(B(3), { opacity: .25, duration: .5, yoyo: true, repeat: 1 }).to(all(1), { opacity: .2, duration: .35, stagger: .15, yoyo: true, repeat: 1 }, 0).to({}, { duration: .4 }); break;
    case 'queue': tl.to(B(4), { y: -3.5, duration: .8, ease: 'sine.inOut', yoyo: true, repeat: 1 }).to({}, { duration: .5 }); break;
    case 'identity': tl.to(B(2), { opacity: .4, duration: .9, ease: 'sine.inOut', yoyo: true, repeat: 1 }); break;
    case 'gate': tl.to(B(3), { y: -4.5, duration: 1, ease: 'power2.inOut', yoyo: true, repeat: 1 }).to({}, { duration: .4 }); break;
    case 'you': tl.to(all(2), { opacity: .2, duration: .7, yoyo: true, repeat: 1 }).to({}, { duration: .4 }); break;
    case 'receipt': tl.fromTo(all(1)[0], { drawSVG: '0%' }, { drawSVG: '100%', duration: .9, ease: 'power2.out' }).to({}, { duration: 1.3 }); break;
    case 'form': tl.to(all(2)[0], { opacity: .2, duration: .45, yoyo: true, repeat: 3 }).to({}, { duration: .7 }); break;
    case 'send': tl.to(all(1), { x: 1.8, y: -1.8, duration: .7, ease: 'sine.inOut', yoyo: true, repeat: 1 }).to({}, { duration: .3 }); break;
  }
  return tl;
}

// Plays an idle timeline only while its element is on screen.
function whileVisible(target, tl) {
  ScrollTrigger.create({ trigger: target, start: 'top bottom', end: 'bottom top', onToggle: (self) => (self.isActive ? tl.play() : tl.pause()) });
  return tl;
}

// Mono label pills inside SVG scenes, sized after the font loads.
function pill(key, cls = '') {
  const g = el('g', { class: `pill ${cls}` });
  g.append(el('rect', { rx: 3, height: 12, y: -8.4 }), el('text', { 'text-anchor': 'middle', 'data-label': key }));
  return g;
}
function sizePills(root = document) {
  for (const g of $$('.pill', root)) {
    const t = g.querySelector('text'), r = g.querySelector('rect');
    const fs = parseFloat(getComputedStyle(t).fontSize) || 7.2;
    const pad = fs * 0.8, w = t.getComputedTextLength() + pad * 2, h = fs * 1.7;
    r.setAttribute('width', r2(w)); r.setAttribute('x', r2(-w / 2)); r.setAttribute('height', r2(h)); r.setAttribute('y', r2(-h / 2 - fs * 0.34));
  }
}

// Language and theme -------------------------------------------------------------------------------------------------
let lang = 'en';
const EN = new Map();
const label = (k) => LABELS[lang][k] ?? LABELS.en[k] ?? k;
function captureEnglish() {
  for (const e of $$('[data-i18n]')) EN.set(e.dataset.i18n, e.textContent);
  for (const e of $$('[data-i18n-html]')) EN.set(e.dataset.i18nHtml, e.innerHTML);
  for (const e of $$('[data-i18n-alt]')) EN.set('alt:' + e.dataset.i18nAlt, e.alt);
  EN.set('title', document.title);
}
const fmt = (n) => new Intl.NumberFormat(lang === 'pt' ? 'pt-BR' : 'en-US').format(n);
function applyLang(next) {
  lang = next;
  const pt = lang === 'pt';
  document.documentElement.lang = pt ? 'pt-BR' : 'en';
  for (const e of $$('[data-i18n]')) e.textContent = (pt && PT[e.dataset.i18n]) || EN.get(e.dataset.i18n);
  for (const e of $$('[data-i18n-html]')) e.innerHTML = (pt && PT[e.dataset.i18nHtml]) || EN.get(e.dataset.i18nHtml);
  for (const e of $$('[data-i18n-alt]')) e.alt = (pt && PT[e.dataset.i18nAlt]) || EN.get('alt:' + e.dataset.i18nAlt);
  for (const t of $$('[data-label]')) {
    t.textContent = label(t.dataset.label);
    if (t.dataset.num) t.append(el('tspan', { class: 'n', dx: 5 }, fmt(+t.dataset.num)), el('tspan', { dx: 3 }, label(t.dataset.unit)));
  }
  for (const n of $$('[data-count]')) if (!n.dataset.running) n.textContent = fmt(+n.dataset.count);
  document.title = (pt && PT.title) || EN.get('title');
  const b = $('#lang');
  b.textContent = pt ? 'EN' : 'PT';
  b.setAttribute('aria-label', pt ? 'English' : 'Português');
  paintRelease();
  setStageName();
  sizePills();
  try { localStorage.setItem('jo.site.lang', lang); } catch {}
}
function initLang() {
  captureEnglish();
  let saved = null;
  try { saved = localStorage.getItem('jo.site.lang'); } catch {}
  const pick = saved || ((navigator.language || '').toLowerCase().startsWith('pt') ? 'pt' : 'en');
  $('#lang').addEventListener('click', () => applyLang(lang === 'pt' ? 'en' : 'pt'));
  return pick;
}
function initTheme() {
  const meta = $('meta[name="theme-color"]');
  const paint = () => meta.setAttribute('content', document.documentElement.dataset.theme === 'light' ? '#f7f7f7' : '#141414');
  paint();
  $('#theme').addEventListener('click', () => {
    const next = document.documentElement.dataset.theme === 'light' ? 'dark' : 'light';
    document.documentElement.dataset.theme = next;
    try { localStorage.setItem('jo.site.theme', next); } catch {}
    paint();
  });
}

// Download: the hero button and the card for the visitor's system -----------------------------------------------------
let os = null, arch = 'x64';
const OS_NAME = { linux: 'Linux', macos: 'macOS', windows: 'Windows' };
const FILE = {
  linux: { x64: 'job-orchestrator-linux-x64.run', arm64: 'job-orchestrator-linux-arm64.run' },
  macos: { x64: 'job-orchestrator-macos-x64.dmg', arm64: 'job-orchestrator-macos-arm64.dmg' },
  windows: { x64: 'job-orchestrator-windows-x64-setup.exe', arm64: 'job-orchestrator-windows-arm64-setup.exe' },
};
const SIZE = {
  'job-orchestrator-linux-x64.run': '105.8 MB', 'job-orchestrator-linux-arm64.run': '103.3 MB',
  'job-orchestrator-macos-arm64.dmg': '106.9 MB', 'job-orchestrator-macos-x64.dmg': '110.7 MB',
  'job-orchestrator-windows-x64-setup.exe': '102.6 MB', 'job-orchestrator-windows-arm64-setup.exe': '101.6 MB',
};
let version = null;
const mb = (bytes) => `${(bytes / 1048576).toFixed(1)} MB`;
// The version and file sizes of the latest release, so the page never lags behind one; the HTML keeps the last known values.
async function latestRelease() {
  try {
    const r = await fetch('https://api.github.com/repos/gabrielandre-math/job-orchestrator/releases/latest', { headers: { Accept: 'application/vnd.github+json' } });
    if (!r.ok) return;
    const rel = await r.json();
    version = String(rel.tag_name || '').replace(/^v/, '') || null;
    for (const a of rel.assets ?? []) SIZE[a.name] = mb(a.size);
    paintRelease();
  } catch {}
}
function paintRelease() {
  if (version) for (const v of $$('[data-version]')) v.textContent = version;
  for (const link of $$('.os li a')) {
    const size = SIZE[link.href.split('/').pop()];
    if (size) link.querySelector('.size').textContent = size;
  }
  setDownload();
}
async function detect() {
  const ua = navigator.userAgent;
  const platform = (navigator.userAgentData?.platform || navigator.platform || '').toLowerCase();
  if (/android|iphone|ipad|ipod/i.test(ua) || navigator.userAgentData?.mobile) os = null;
  else if (platform.includes('win') || /windows/i.test(ua)) os = 'windows';
  else if (platform.includes('mac') || /mac os x/i.test(ua)) os = 'macos';
  else if (platform.includes('linux') || /linux|x11/i.test(ua)) os = 'linux';
  if (os === 'macos') arch = 'arm64';
  try {
    const hint = await navigator.userAgentData?.getHighEntropyValues?.(['architecture']);
    if (hint?.architecture === 'arm') arch = 'arm64';
    else if (hint?.architecture === 'x86' && os !== 'macos') arch = 'x64';
  } catch {}
  if (/aarch64|arm64/i.test(ua) && os === 'linux') arch = 'arm64';
  for (const card of $$('.os')) card.classList.toggle('mine', card.dataset.os === os);
  setDownload();
}
function setDownload() {
  const a = $('#hero-download'), span = a.querySelector('span');
  const size = $('#hero-size');
  if (os) {
    a.href = DL + FILE[os][arch];
    span.textContent = label('downloadFor').replace('{os}', OS_NAME[os]);
    size.textContent = SIZE[FILE[os][arch]] ?? '';
  } else {
    a.href = '#download';
    span.textContent = label('download');
    size.textContent = '';
  }
}

// Hero: the brand mark as a road, with the agents' stations along it and a light running the route ----------------------
const ROUTE = 'M 24 0 L 24 180 A 52 52 0 0 0 128 180 L 128 76 A 52 52 0 0 1 232 76 L 232 256';
const SEG = [['l', [24, 0], [24, 180]], ['a', [76, 180], -1], ['l', [128, 180], [128, 76]], ['a', [180, 76], 1], ['l', [232, 76], [232, 256]]];
const LEN = [180, Math.PI * 52, 104, Math.PI * 52, 180];
function pointAt(f) {
  let d = Math.min(Math.max(f, 0), 1) * LEN.reduce((a, b) => a + b, 0);
  for (let i = 0; i < SEG.length; i++) {
    const s = SEG[i];
    if (d > LEN[i] && i < SEG.length - 1) { d -= LEN[i]; continue; }
    const t = d / LEN[i];
    if (s[0] === 'l') return [s[1][0] + (s[2][0] - s[1][0]) * t, s[1][1] + (s[2][1] - s[1][1]) * t];
    const a = s[2] === -1 ? Math.PI - t * Math.PI : Math.PI + t * Math.PI;
    return [s[1][0] + Math.cos(a) * 52, s[1][1] + Math.sin(a) * 52];
  }
  return [232, 256];
}
const MARK = 'M 180 0 C 221.974 0 256 34.026 256 76 L 256 256 L 208 256 L 208 76 C 208 60.536 195.464 48 180 48 C 164.536 48 152 60.536 152 76 L 152 180 C 152 221.974 117.974 256 76 256 C 34.026 256 0 221.974 0 180 L 0 0 L 48 0 L 48 180 C 48 195.464 60.536 208 76 208 C 91.464 208 104 195.464 104 180 L 104 76 C 104 34.026 138.026 0 180 0 Z';
const ROAD_H = 10;
// The isometric projection as an SVG matrix: the mark lies flat on the ground at height z.
const ground = (z) => `matrix(${r2(C)} .5 ${r2(-C)} .5 0 ${-z})`;
const STOPS = [
  { at: 0.06, icon: 'radar', key: 'scout', num: 1067, unit: 'found' }, { at: 0.3, icon: 'chart', key: 'analyst', num: 322, unit: 'eligible' },
  { at: 0.49, icon: 'writer', key: 'writer', num: 55, unit: 'queued' }, { at: 0.72, icon: 'guardian', key: 'guardian' }, { at: 0.94, icon: 'send', key: 'sender' },
];
function token() {
  const g = el('g', { class: 'token' });
  g.append(boxEl({ x: -6, y: -7.5, z: ROAD_H, w: 12, d: 15, h: 1.6, m: 'paper', lines: [
    { pts: [[-3.6, -4, ROAD_H + 1.6], [3.6, -4, ROAD_H + 1.6]], tone: 'accent', w: 1.1 },
    { pts: [[-3.6, 0, ROAD_H + 1.6], [3.6, 0, ROAD_H + 1.6]], tone: 'ink', w: 1.1 },
    { pts: [[-3.6, 4, ROAD_H + 1.6], [1.2, 4, ROAD_H + 1.6]], tone: 'ink', w: 1.1 },
  ] }, 0));
  return g;
}
function buildHero() {
  const svg = el('svg', { viewBox: '-236 -52 472 334', role: 'presentation' });
  const defs = el('defs');
  const glow = el('radialGradient', { id: 'jo-glow' });
  glow.append(el('stop', { offset: '0', style: 'stop-color: var(--accent); stop-opacity: .9' }), el('stop', { offset: '.35', style: 'stop-color: var(--accent); stop-opacity: .35' }), el('stop', { offset: '1', style: 'stop-color: var(--accent); stop-opacity: 0' }));
  defs.append(glow);
  svg.append(defs);
  const road = el('g', { class: 'road' });
  for (let z = 0; z < ROAD_H; z++) road.append(el('path', { d: MARK, class: 'road-side', transform: ground(z) }));
  road.append(el('path', { d: MARK, class: 'road-top', transform: ground(ROAD_H) }));
  const lane = el('path', { d: ROUTE, class: 'road-lane', transform: ground(ROAD_H) });
  const path = el('path', { d: ROUTE, fill: 'none', stroke: 'none', transform: ground(ROAD_H + 1) });
  road.append(lane, path);
  svg.append(road);
  const tokens = el('g', { class: 'tokens' });
  const light = el('g', { class: 'route-light', opacity: 0 }, el('ellipse', { rx: 20, ry: 12, fill: 'url(#jo-glow)' }), el('circle', { r: 3, class: 'route-core' }));
  svg.append(tokens, light);
  const stops = STOPS.map((s) => {
    const [u, v] = pointAt(s.at);
    const [x, y] = P([u, v, ROAD_H]);
    const outer = el('g', { transform: `translate(${r2(x)} ${r2(y)})` });
    const lift = el('g', { class: 'lift' });
    const st = station(s.icon);
    lift.append(el('g', { transform: 'scale(.88) translate(0 -18)' }, st));
    const p = pill(s.key);
    if (s.num) p.querySelector('text').dataset.num = s.num, p.querySelector('text').dataset.unit = s.unit;
    p.setAttribute('transform', 'translate(0 20)');
    outer.append(lift, p);
    svg.append(outer);
    return { ...s, lift, pill: p, st };
  });
  $('#hero-art').append(svg);
  return { svg, path, light, tokens, stops };
}

// How it works: a stage with the station of the active step ------------------------------------------------------------
const STEP_LABEL = ['scout', 'analyst', 'writer', 'guardian', 'tracker'];
let stageIndex = 0;
function setStageName() {
  const n = $('#stage-name');
  if (n) n.textContent = label(STEP_LABEL[stageIndex]);
}
function buildStage() {
  const svg = el('svg', { viewBox: '-42 -34 84 80' });
  const items = $$('.step').map((step) => {
    const g = el('g', { class: 'stage-st', opacity: 0 });
    const st = station(step.dataset.station);
    g.append(st);
    svg.append(g);
    const mini = icon(step.dataset.station);
    step.querySelector('.step-mini').append(mini);
    return { step, g, idle: idle(step.dataset.station, st), mini, miniIdle: idle(step.dataset.station, mini.firstChild) };
  });
  $('#stage-art').append(svg);
  return items;
}

// Self-hosted: every part of the app on one platform, the visitor's computer ------------------------------------------
function buildMachine() {
  const W = 170, D = 124, Z = 8;
  const svg = el('svg', { viewBox: '0 0 10 10' });
  const scene = el('g');
  svg.append(scene);
  const ground = el('path', { d: polyline([[-12, -12, 0], [W + 12, -12, 0], [W + 12, D + 12, 0], [-12, D + 12, 0], [-12, -12, 0]]), class: 'edge-dash' });
  const platform = el('g', { class: 'platform' }, boxEl({ x: 0, y: 0, z: 0, w: W, d: D, h: Z, m: 'base' }, 0));
  const flowA = el('path', { d: polyline([[W + 16, 44, 0], [W + 50, 44, 0]]), class: 'flow' });
  const flowB = el('path', { d: polyline([[128, D + 16, 0], [128, D + 50, 0]]), class: 'flow' });
  const headA = el('path', { d: poly([[W + 50, 40, 0], [W + 58, 44, 0], [W + 50, 48, 0]]), class: 'flow-head' });
  const headB = el('path', { d: poly([[124, D + 50, 0], [128, D + 58, 0], [132, D + 50, 0]]), class: 'flow-head' });
  scene.append(ground, flowA, flowB, headA, headB, platform);
  const slots = [[12, 14, 'model', 'model'], [67, 14, 'queue', 'db'], [12, 74, 'radar', 'scraper'], [122, 14, 'guardian', 'keyring'], [67, 74, 'form', 'browser'], [122, 74, 'you', 'panel']];
  const parts = slots.map(([x, y, name, key]) => {
    const drop = el('g', { class: 'drop' });
    const st = station(name, [x, y, Z]);
    drop.append(st);
    scene.append(drop);
    return { drop, st, name, x, y, key, idle: idle(name, st, [x, y, Z]) };
  });
  const labels = parts.map((p) => {
    const [lx, ly] = P([p.x + 18, p.y + 18, Z + 36]);
    const [sx, sy] = P([p.x + 18, p.y + 18, Z + 28]);
    const g = el('g', { class: 'mlabel' });
    g.append(el('path', { d: `M ${r2(sx)} ${r2(sy)} L ${r2(lx)} ${r2(ly + 4)}`, class: 'edge-dash' }));
    const pl = pill(p.key);
    pl.setAttribute('transform', `translate(${r2(lx)} ${r2(ly)})`);
    g.append(pl);
    scene.append(g);
    return g;
  });
  const tags = [
    ['computer', P([W + 12, D + 12, 0]), [0, 22]],
    ['sources', P([W + 58, 44, 0]), [14, 12]],
    ['approved', P([128, D + 58, 0]), [-18, 12]],
  ].map(([key, [x, y], [dx, dy]]) => {
    const pl = pill(key, key === 'computer' ? 'on' : '');
    pl.setAttribute('transform', `translate(${r2(x + dx)} ${r2(y + dy)})`);
    scene.append(pl);
    return pl;
  });
  $('#machine-art').append(svg);
  const fit = () => {
    sizePills(svg);
    const b = scene.getBBox();
    svg.setAttribute('viewBox', `${r2(b.x - 12)} ${r2(b.y - 12)} ${r2(b.width + 24)} ${r2(b.height + 24)}`);
  };
  return { svg, fit, ground, platform, parts, labels, tags, flows: [flowA, flowB], heads: [headA, headB] };
}

// Recruiters: one call over a field of candidate machines; each lights up while it checks the job, only the yes answer --
function buildNetwork() {
  const W = 1200, H = 440, S = 54, sc = 0.78, rs = 1.35;
  const ox = W / 2, oy = H / 2 + 6;
  const g = (X, Y) => [ox + (X - Y) * C, oy + (X + Y) * 0.5];
  const pts = [];
  for (let i = -16; i <= 16; i++) for (let j = -16; j <= 16; j++) {
    if (Math.abs(i) + Math.abs(j) <= 1) continue;
    const [x, y] = g(i * S, j * S);
    if (x > 30 && x < W - 30 && y > 46 && y < H - 14) pts.push({ i, j, x, y, d: S * Math.hypot(i, j), a: ((Math.atan2(y - oy, x - ox) * 180) / Math.PI + 360) % 360 });
  }
  const r0 = 30, rmax = Math.max(...pts.map((p) => p.d)) + 30;
  const band = pts.filter((p) => p.d > 0.3 * rmax && p.d < 0.62 * rmax);
  const yes = new Set([18, 74, 128, 196, 250, 316].map((t) => band.reduce((best, q) => {
    const dq = Math.min(Math.abs(q.a - t), 360 - Math.abs(q.a - t)), db = Math.min(Math.abs(best.a - t), 360 - Math.abs(best.a - t));
    return dq < db ? q : best;
  })));
  const svg = el('svg', { viewBox: `0 0 ${W} ${H}`, preserveAspectRatio: 'xMidYMid slice' });
  const defs = el('defs');
  const chip = el('g', { id: 'net-chip' });
  ART.model.forEach((b, i) => chip.append(boxEl(b, i, [0, 0, 0], i === ART.model.length - 1 ? 'dark' : b.m)));
  const core = ART.model[ART.model.length - 1];
  const lit = el('g', { id: 'net-lit' }, el('ellipse', { cx: 0, cy: 8, rx: 17, ry: 10, fill: 'url(#net-glow)', opacity: .55 }), boxEl(core, 0));
  const glow = el('radialGradient', { id: 'net-glow' });
  glow.append(el('stop', { offset: '0', style: 'stop-color: var(--accent); stop-opacity: .95' }), el('stop', { offset: '.35', style: 'stop-color: var(--accent); stop-opacity: .42' }), el('stop', { offset: '1', style: 'stop-color: var(--accent); stop-opacity: 0' }));
  const fade = el('radialGradient', { id: 'net-fade', cx: '.5', cy: '.5', r: '.62' });
  fade.append(el('stop', { offset: '.45', 'stop-color': '#fff' }), el('stop', { offset: '1', 'stop-color': '#fff', 'stop-opacity': 0 }));
  const mask = el('mask', { id: 'net-mask' }, el('rect', { width: W, height: H, fill: 'url(#net-fade)' }));
  defs.append(chip, lit, glow, fade, mask);
  svg.append(defs);
  const field = el('g', { mask: 'url(#net-mask)' });
  const grid = [], L = 16 * S;
  for (let k = -16; k < 16; k++) {
    const v = (k + 0.5) * S;
    for (const [a, b] of [[g(v, -L), g(v, L)], [g(-L, v), g(L, v)]]) grid.push(`M ${r2(a[0])} ${r2(a[1])} L ${r2(b[0])} ${r2(b[1])}`);
  }
  field.append(el('path', { d: grid.join(' '), fill: 'none', style: 'stroke: var(--line)', 'stroke-width': 1 }));
  const rings = [[2, 0.85], [9, 0.16]].map(([w, a]) => {
    const e = el('ellipse', { cx: ox, cy: oy, rx: 0, ry: 0, fill: 'none', style: 'stroke: var(--accent)', 'stroke-width': w, opacity: 0 });
    e.dataset.alpha = a;
    field.append(e);
    return e;
  });
  const items = [...pts.map((p) => ({ y: p.y, p })), { y: oy, rec: true }].sort((a, b) => a.y - b.y);
  const chips = [];
  let recruiter, tip;
  for (const it of items) {
    if (it.rec) {
      recruiter = el('g', { transform: `translate(${ox} ${r2(oy - 18 * rs)}) scale(${rs})` }, station('router'));
      field.append(recruiter);
      continue;
    }
    const p = it.p;
    const litG = el('g', { opacity: 0 }, el('use', { href: '#net-lit' }));
    field.append(el('g', { transform: `translate(${r2(p.x)} ${r2(p.y - 18 * sc)}) scale(${sc})` }, el('use', { href: '#net-chip' }), litG));
    chips.push({ ...p, lit: litG, yes: yes.has(p) });
  }
  svg.append(field);
  tip = [ox + 12.99 * rs, oy - 18 * rs - 7.5 * rs];
  const answers = chips.filter((c) => c.yes).map((c) => {
    const sx = c.x, sy = c.y - 10 * sc;
    const p = pill('yes', 'yes big');
    const holder = el('g', { transform: `translate(${r2(sx)} ${r2(sy - 30)})` }, p);
    const pulse = el('g', { opacity: 0 }, el('circle', { r: 11, fill: 'url(#net-glow)' }), el('circle', { r: 3.2, class: 'route-core' }));
    svg.append(holder, pulse);
    return { ...c, pill: p, pulse, sx, sy };
  });
  const rec = pill('recruiter', 'big');
  rec.setAttribute('transform', `translate(${ox} ${oy + 32})`);
  svg.append(rec);
  $('#net-art').append(svg);
  return { svg, rings, chips, answers, recruiter, tip, r0, rmax };
}

// The "coming soon" mark: blocks stacked, the last one dropping into place.
function buildSoonIcon() {
  const svg = el('svg', { viewBox: '-34 -30 68 64' });
  const parts = [{ x: 0, y: 0, z: 0, w: 36, d: 36, h: 4, m: 'base' }, { x: 6, y: 13, z: 4, w: 24, d: 15, h: 7, m: 'dark' }, { x: 6, y: 13, z: 11, w: 12, d: 15, h: 7, m: 'paper' }];
  parts.forEach((b, i) => svg.append(boxEl(b, i)));
  const block = el('g', { class: 'soon-block' }, boxEl({ x: 18, y: 13, z: 11, w: 12, d: 15, h: 7, m: 'accent' }, 3));
  svg.append(block);
  $('#build-icon').append(svg);
  return block;
}

// Motion ---------------------------------------------------------------------------------------------------------------
function heroLoop(h) {
  // Jobs enter at the Scout, half leave the queue at the Analyst, all wait for your OK at the Guardian, the rest go out pink.
  const T = 10, N = 12, path = h.path;
  const along = (start, end) => ({ motionPath: { path, align: path, alignOrigin: [0.5, 0.5], start, end }, ease: 'none' });
  const mat = (tok, m) => tok.querySelectorAll('.iso-f').forEach((f) => f.setAttribute('class', f.getAttribute('class').replace(/iso-(paper|accent)$/, `iso-${m}`)));
  const loops = [];
  for (let k = 0; k < N; k++) {
    const tok = token();
    h.tokens.append(tok);
    const drop = k % 2 === 1;
    const tl = gsap.timeline({ repeat: -1, paused: true, delay: (k * T) / N });
    tl.set(tok, { opacity: 0 }).call(() => mat(tok, 'paper'))
      .to(tok, { ...along(0, 0.3), duration: T * 0.3 })
      .to(tok, { opacity: 1, duration: 0.4 }, 0);
    if (drop) tl.to(tok, { y: '+=22', opacity: 0, duration: 0.55, ease: 'power2.in' });
    else {
      tl.to(tok, { ...along(0.3, 0.72), duration: T * 0.42 })
        .to({}, { duration: 0.7 })
        .to(tok, { ...along(0.72, 0.94), duration: T * 0.22 })
        .call(() => mat(tok, 'accent'))
        .to(tok, { ...along(0.94, 1), duration: T * 0.06 })
        .to(tok, { opacity: 0, duration: 0.3 });
    }
    tl.to({}, { duration: Math.max(0, T + 1.2 - tl.duration()) });
    loops.push(tl);
  }
  const D = 7;
  const light = gsap.timeline({ repeat: -1, paused: true });
  light.set(h.light, { opacity: 1 }).to(h.light, { ...along(0, 1), duration: D }, 0);
  for (const s of h.stops) {
    const t = Math.max(0, s.at * D - 0.14);
    const back = Math.min(0.6, D - t - 0.24);
    light.to(s.lift, { y: -6, duration: 0.22, ease: 'power2.out' }, t)
      .to(s.lift, { y: 0, duration: back, ease: 'bounce.out' }, t + 0.22)
      .call(() => s.pill.classList.add('on'), null, t)
      .call(() => s.pill.classList.remove('on'), null, Math.min(D - 0.01, t + 1.1));
  }
  const idles = h.stops.map((s) => idle(s.icon, s.st));
  const all = [...loops, light, ...idles];
  all.forEach((tl) => tl.play());
  ScrollTrigger.create({ trigger: '.hero', start: 'top bottom', end: 'bottom top', onToggle: (self) => all.forEach((tl) => (self.isActive ? tl.play() : tl.pause())) });
}

function shotAndStats() {
  gsap.fromTo('.shot', { rotateX: 24, scale: .92, y: 30, transformPerspective: 1800 }, { rotateX: 0, scale: 1, y: 0, ease: 'none', scrollTrigger: { trigger: '.shot-wrap', start: 'top 92%', end: 'top 28%', scrub: .6 } });
  for (const n of $$('[data-count]')) {
    const end = +n.dataset.count, o = { v: 0 };
    gsap.to(o, {
      v: end, duration: 1.8, ease: 'power3.out',
      scrollTrigger: { trigger: n, start: 'top 90%', once: true },
      onStart: () => { n.dataset.running = '1'; },
      onUpdate: () => { n.textContent = fmt(Math.round(o.v)); },
      onComplete: () => { delete n.dataset.running; n.textContent = fmt(end); },
    });
  }
}

function howItWorks(items, desktop) {
  if (!desktop) {
    items.forEach((it) => whileVisible(it.step, it.miniIdle));
    return;
  }
  const texts = items.map((it) => it.step.querySelector('p'));
  gsap.set(texts, { height: 0, opacity: 0 });
  let cur = -1;
  const setStep = (i) => {
    if (i === cur) return;
    const prev = items[cur];
    cur = i;
    stageIndex = i;
    items.forEach((it, k) => it.step.classList.toggle('on', k === i));
    texts.forEach((p, k) => gsap.to(p, { height: k === i ? 'auto' : 0, opacity: k === i ? 1 : 0, duration: .5, ease: 'power2.inOut', overwrite: true }));
    if (prev) {
      prev.idle.pause();
      gsap.to(prev.g, { y: -26, opacity: 0, duration: .4, ease: 'power2.in', overwrite: true });
    }
    const next = items[i];
    gsap.fromTo(next.g, { y: 34, opacity: 0 }, { y: 0, opacity: 1, duration: .8, ease: 'back.out(1.5)', delay: prev ? .18 : 0, overwrite: true, onComplete: () => next.idle.play() });
    $('.stage-n').textContent = String(i + 1).padStart(2, '0');
    setStageName();
  };
  setStep(0);
  ScrollTrigger.create({
    trigger: '.how', start: 'top top', end: () => '+=' + window.innerHeight * 2.4, pin: '.how-pin', anticipatePin: 1,
    onUpdate: (self) => {
      setStep(Math.min(items.length - 1, Math.floor(self.progress * items.length * 0.9999)));
      gsap.set('.stage-bar i', { scaleX: self.progress });
    },
    onToggle: (self) => { if (!self.isActive) items[cur]?.idle.pause(); else items[cur]?.idle.play(); },
  });
  return () => { items.forEach((it) => { it.step.classList.remove('on'); it.idle.pause(); }); gsap.set(texts, { clearProps: 'all' }); };
}

function machine(m) {
  const tl = gsap.timeline({ paused: true, defaults: { ease: 'power3.out' } });
  tl.from(m.platform, { y: 40, autoAlpha: 0, duration: .9 })
    .from(m.ground, { drawSVG: '0%', duration: 1.2, ease: 'power2.inOut' }, '-=.5')
    .from(m.parts.map((p) => p.drop), { y: -46, autoAlpha: 0, duration: .8, ease: 'back.out(1.7)', stagger: .11 }, '-=.9')
    .from(m.labels, { autoAlpha: 0, y: 6, duration: .5, stagger: .07 }, '-=.5')
    .from(m.tags, { autoAlpha: 0, duration: .5, stagger: .1 }, '-=.3')
    .from(m.flows, { drawSVG: '0%', duration: .8, stagger: .15 }, '<')
    .from(m.heads, { autoAlpha: 0, duration: .3, stagger: .15 }, '-=.3')
    .add(() => { flow.play(); m.parts.forEach((p) => p.idle.play()); });
  const flow = gsap.to(m.flows, { attr: { 'stroke-dashoffset': -18 }, duration: .9, ease: 'none', repeat: -1, paused: true });
  ScrollTrigger.create({ trigger: '#machine-art', start: 'top 75%', once: true, onEnter: () => tl.play() });
  ScrollTrigger.create({ trigger: '#machine-art', start: 'top bottom', end: 'bottom top', onToggle: (self) => {
    if (tl.progress() < 1) return;
    if (self.isActive) { flow.play(); m.parts.forEach((p) => p.idle.play()); } else { flow.pause(); m.parts.forEach((p) => p.idle.pause()); }
  } });
}

function network(n) {
  const D = 6;
  const when = (d) => 0.5 * D * (d - n.r0) / (n.rmax - n.r0);
  const tl = gsap.timeline({ repeat: -1, paused: true });
  n.rings.forEach((r, k) => tl.fromTo(r, { attr: { rx: n.r0 * 1.2247, ry: n.r0 * 0.7071 }, opacity: +r.dataset.alpha }, { attr: { rx: n.rmax * 1.2247, ry: n.rmax * 0.7071 }, opacity: 0, duration: D * 0.5, ease: 'none' }, k * 0.32));
  for (const c of n.chips) {
    const t = when(c.d);
    tl.to(c.lit, { opacity: 1, duration: .12 }, t);
    tl.to(c.lit, { opacity: 0, duration: c.yes ? .5 : .9 }, c.yes ? D * 0.6 : t + .12);
  }
  for (const a of n.answers) {
    const t = when(a.d);
    tl.fromTo(a.pill, { opacity: 0, y: 6 }, { opacity: 1, y: 0, duration: .35, ease: 'back.out(2)' }, t + .25)
      .to(a.pill, { opacity: 0, duration: .3 }, D * 0.56)
      .fromTo(a.pulse, { x: a.sx, y: a.sy, opacity: 0 }, { opacity: 1, duration: .06 }, t + .45)
      .to(a.pulse, { x: n.tip[0], y: n.tip[1], duration: .95, ease: 'power1.in' }, t + .5)
      .to(a.pulse, { opacity: 0, duration: .12 }, t + 1.45);
  }
  tl.set({}, {}, D);
  const tipBox = n.recruiter.querySelector('.b3');
  const ping = gsap.timeline({ repeat: -1, paused: true }).to(tipBox, { opacity: .3, duration: .4, yoyo: true, repeat: 1 }).to({}, { duration: .8 });
  ScrollTrigger.create({ trigger: '#net-art', start: 'top bottom', end: 'bottom top', onToggle: (self) => { if (self.isActive) { tl.play(); ping.play(); } else { tl.pause(); ping.pause(); } } });
}

function soonIcon(block) {
  gsap.timeline({ repeat: -1, repeatDelay: .9 })
    .fromTo(block, { y: -16, opacity: 0 }, { opacity: 1, duration: .2 })
    .to(block, { y: 0, duration: .9, ease: 'bounce.out' }, 0)
    .to(block, { opacity: 0, duration: .4, delay: 1.4 });
}

function staticScenes(h, n) {
  for (const a of n.answers) { gsap.set(a.lit, { opacity: 1 }); gsap.set(a.pill, { opacity: 1 }); }
}

// Page wiring ----------------------------------------------------------------------------------------------------------
function tabs() {
  const buttons = $$('.tabs button');
  buttons.forEach((b) => b.addEventListener('click', () => {
    buttons.forEach((x) => x.setAttribute('aria-selected', String(x === b)));
    const key = b.dataset.tab;
    for (const s of $$('.screen')) {
      const on = s.dataset.screen === key;
      if (on && !s.classList.contains('on')) gsap.fromTo(s, { autoAlpha: 0, scale: 1.015 }, { autoAlpha: 1, scale: 1, duration: .5, ease: 'power2.out' });
      else if (!on && s.classList.contains('on')) gsap.to(s, { autoAlpha: 0, duration: .3 });
      s.classList.toggle('on', on);
    }
    for (const p of $$('[data-cap]')) p.classList.toggle('on', p.dataset.cap === key);
  }));
}
function copy() {
  for (const b of $$('.copy')) b.addEventListener('click', async () => {
    const code = $(b.dataset.copy).innerText.split('\n').map((l) => l.replace(/\s+#.*$/, '')).join('\n');
    try { await navigator.clipboard.writeText(code); } catch { return; }
    const was = b.textContent;
    b.textContent = lang === 'pt' ? 'Copiado' : 'Copied';
    b.classList.add('done');
    setTimeout(() => { b.textContent = was; b.classList.remove('done'); }, 1600);
  });
}
function navShadow() {
  const nav = $('#nav');
  const paint = () => nav.classList.toggle('scrolled', window.scrollY > 8);
  window.addEventListener('scroll', paint, { passive: true });
  paint();
}

async function main() {
  const firstLang = initLang();
  initTheme();
  const hero = buildHero();
  const stage = buildStage();
  const mach = buildMachine();
  const net = buildNetwork();
  const block = buildSoonIcon();
  for (const s of $$('.ico[data-icon]')) {
    const ic = icon(s.dataset.icon);
    s.append(ic);
    s._idle = idle(s.dataset.icon, ic.firstChild);
  }
  applyLang(firstLang);
  detect();
  latestRelease();
  tabs();
  copy();
  navShadow();
  await document.fonts.ready;
  sizePills();
  mach.fit();

  const mm = gsap.matchMedia();
  mm.add({ motion: '(prefers-reduced-motion: no-preference)', still: '(prefers-reduced-motion: reduce)', desktop: '(min-width: 1001px)' }, (ctx) => {
    const { motion, desktop } = ctx.conditions;
    if (!motion) {
      staticScenes(hero, net);
      stage.forEach((it, k) => gsap.set(it.g, { opacity: k === 0 ? 1 : 0 }));
      $$('.step').forEach((s) => s.classList.add('on'));
      return;
    }
    heroLoop(hero);
    shotAndStats();
    const undo = howItWorks(stage, desktop);
    machine(mach);
    network(net);
    soonIcon(block);
    gsap.from('.bar i', { scaleX: 0, duration: 1, ease: 'power3.out', stagger: .08, scrollTrigger: { trigger: '.registry', start: 'top 85%', once: true } });
    for (const s of $$('.ico[data-icon]')) whileVisible(s, s._idle);
    return undo;
  });
}

main();
