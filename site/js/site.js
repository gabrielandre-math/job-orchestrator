import { ART, P } from './iso-art.js';
import { PT, LABELS } from './i18n.js';

const { gsap, ScrollTrigger, DrawSVGPlugin } = window;
gsap.registerPlugin(ScrollTrigger, DrawSVGPlugin);

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
  if (formCard) paintForm();
  if (board) paintStory();
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

// Forms: an application form filling itself, each value with where it came from --------------------------------------
const FORM = {
  en: {
    job: 'Senior Backend Engineer (.NET)', where: 'Norte Pagamentos · Remote, Latin America', via: 'Workable',
    rows: [
      { q: 'Full name', v: 'Ana Lima', src: 'profile', k: 'profile' },
      { q: 'Email', v: 'ana.lima@example.com', src: 'profile', k: 'profile' },
      { q: 'Resume', file: 'Ana-Lima-Resume.pdf', src: 'written for this job', k: 'profile' },
      { q: 'Expected monthly salary (USD)', v: '2,000', src: 'your floor', k: 'data' },
      { q: 'Do you have at least 3 years of experience with C#?', radio: ['Yes', 'No'], src: 'dates of your jobs', k: 'data' },
      { q: 'Which frameworks have you worked with?', chips: ['Angular', 'React'], src: 'local model', k: 'model' },
      { q: 'Walk us through a feature you owned end to end.', v: 'At Acme I owned the billing migration to .NET 8 and PostgreSQL, from the first sketch to the release.', src: 'Claude', k: 'model',
        ladder: [['Llama', 'named a tool not in your facts'], ['ChatGPT', 'answered in another language'], ['Claude', 'checked against your facts']] },
      { box: 'I accept the Privacy Notice and the processing of my data for this application.', src: 'your consent', k: 'you' },
    ],
    submit: 'Submit application', done: 'Your application has been submitted successfully.', proof: 'confirmation seen',
  },
  pt: {
    job: 'Desenvolvedor Back-end .NET Pleno', where: 'Brisa Sistemas · Remoto, Brasil', via: 'Quickin',
    rows: [
      { q: 'Nome completo', v: 'Ana Lima', src: 'perfil', k: 'profile' },
      { q: 'E-mail', v: 'ana.lima@exemplo.com.br', src: 'perfil', k: 'profile' },
      { q: 'Currículo', file: 'Ana-Lima-Curriculo.pdf', src: 'escrito para esta vaga', k: 'profile' },
      { q: 'Pretensão salarial como PJ', v: 'R$ 12.000', src: 'seu piso PJ', k: 'data' },
      { q: 'Você tem no mínimo 3 anos de experiência com C#?', radio: ['Sim', 'Não'], src: 'datas das suas experiências', k: 'data' },
      { q: 'Quais frameworks você já usou?', chips: ['Angular', 'React'], src: 'modelo local', k: 'model' },
      { q: 'Conte uma entrega que você conduziu do começo ao fim.', v: 'Na Acme conduzi a migração do faturamento para .NET 8 e PostgreSQL, do primeiro desenho até a entrega.', src: 'Claude', k: 'model',
        ladder: [['Llama', 'citou uma ferramenta fora dos seus fatos'], ['ChatGPT', 'respondeu em outra língua'], ['Claude', 'conferido com os seus fatos']] },
      { box: 'Li e aceito o Aviso de Privacidade e o tratamento dos meus dados para esta candidatura.', src: 'seu consentimento', k: 'you' },
    ],
    submit: 'Enviar candidatura', done: 'Candidatura enviada com sucesso.', proof: 'confirmação vista',
  },
};
function h(tag, attrs = {}, ...kids) {
  const e = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs)) if (v != null) e.setAttribute(k, v);
  e.append(...kids);
  return e;
}
const CHECK = '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M3.5 8.5 6.5 11.5 12.5 4.5"/></svg>';
const CROSS = '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M4.5 4.5 11.5 11.5M11.5 4.5 4.5 11.5"/></svg>';
const svgIcon = (markup, cls) => { const s = h('span', { class: cls }); s.innerHTML = markup; return s; };
// Builds the card in the page's language; the timeline only switches classes and text on it.
function buildForm() {
  const d = FORM[lang];
  const root = $('#apply');
  root.replaceChildren();
  root.append(h('header', { class: 'apply-head' }, h('div', {}, h('b', {}, d.job), h('span', {}, d.where)), h('span', { class: 'apply-via' }, d.via)));
  const list = h('ol', { class: 'apply-fields' });
  const rows = d.rows.map((r) => {
    const src = h('span', { class: 'af-src', 'data-k': r.k }, r.src);
    let value, li;
    if (r.box) {
      value = h('span', { class: 'af-box' }, svgIcon(CHECK, 'tick'));
      li = h('li', { class: 'af box' }, value, h('span', { class: 'af-q' }, r.box), src);
    } else {
      if (r.file) value = h('span', { class: 'af-v file' }, svgIcon(CHECK, 'ok'), h('span', { class: 'typed', 'data-full': r.file }));
      else if (r.radio) value = h('span', { class: 'af-v radios' }, ...r.radio.map((o) => h('b', {}, o)));
      else if (r.chips) value = h('span', { class: 'af-v chips' }, ...r.chips.map((o) => h('b', {}, o)));
      else value = h('span', { class: `af-v${r.ladder ? ' text' : ''}` }, h('span', { class: 'typed', 'data-full': r.v }));
      li = h('li', { class: `af${r.ladder ? ' open' : ''}` }, h('span', { class: 'af-q' }, r.q), value, src);
      if (r.ladder) {
        const steps = r.ladder.map(([m, why], i) => h('li', { class: 'step-m', 'data-last': String(i === r.ladder.length - 1) },
          svgIcon(i === r.ladder.length - 1 ? CHECK : CROSS, 'mark'), h('b', {}, m), h('em', {}, why)));
        li.append(h('ol', { class: 'ladder' }, ...steps));
      }
    }
    list.append(li);
    return { li, r, value, src };
  });
  root.append(list);
  const done = h('span', { class: 'apply-done' }, svgIcon(CHECK, 'ok'), h('span', {}, d.done), h('em', {}, d.proof));
  root.append(h('footer', { class: 'apply-foot' }, h('span', { class: 'apply-btn' }, d.submit), done));
  return { root, rows, done, btn: root.querySelector('.apply-btn') };
}
// The finished card, for reduced motion and for the first paint.
function formDone(f) {
  for (const { li, r, value } of f.rows) {
    li.classList.add('filled');
    for (const t of value.querySelectorAll('.typed')) t.textContent = t.dataset.full;
    if (r.radio) value.children[0].classList.add('pick');
    if (r.ladder) li.querySelectorAll('.step-m').forEach((s) => s.classList.add(s.dataset.last === 'true' ? 'yes' : 'no'));
    if (r.box) li.classList.add('ticked');
  }
  f.done.classList.add('on');
}
function formReset(f) {
  for (const { li, value } of f.rows) {
    li.classList.remove('filled', 'ticked', 'asking');
    for (const t of value.querySelectorAll('.typed')) t.textContent = '';
    value.querySelectorAll('.pick').forEach((b) => b.classList.remove('pick'));
    li.querySelectorAll('.step-m').forEach((s) => s.classList.remove('yes', 'no', 'now'));
  }
  f.done.classList.remove('on');
  f.btn.classList.remove('press');
}
let formTl = null, formTrigger = null;
function formLoop(f) {
  formTl?.kill();
  formTrigger?.kill();
  const list = f.root.querySelector('.apply-fields');
  // Each round fades out before the next one starts, instead of the values vanishing at once.
  const tl = gsap.timeline({ repeat: -1, paused: true });
  tl.call(() => formReset(f)).set([list, f.done], { opacity: 1 }).to({}, { duration: .5 });
  const type = (t) => {
    const full = t.dataset.full, o = { n: 0 };
    return gsap.to(o, { n: full.length, duration: Math.min(1.5, .18 + full.length * .022), ease: 'none', onUpdate: () => { t.textContent = full.slice(0, Math.round(o.n)); } });
  };
  for (const { li, r, value } of f.rows) {
    const at = tl.duration();
    if (r.ladder) {
      const steps = [...li.querySelectorAll('.step-m')];
      tl.call(() => li.classList.add('asking'), null, at);
      steps.forEach((s, i) => {
        const last = i === steps.length - 1;
        tl.call(() => s.classList.add('now'), null, at + .15 + i * .85)
          .call(() => { s.classList.remove('now'); s.classList.add(last ? 'yes' : 'no'); }, null, at + .75 + i * .85);
      });
      tl.add(type(value.querySelector('.typed')), at + .75 + (steps.length - 1) * .85 + .1);
    } else if (r.radio) tl.call(() => value.children[0].classList.add('pick'), null, at + .1);
    else if (r.chips || r.box) tl.to({}, { duration: .25 }, at);
    else tl.add(type(value.querySelector('.typed')), at);
    tl.call(() => li.classList.add(r.box ? 'ticked' : 'filled'), null, '>').to({}, { duration: .28 });
  }
  tl.call(() => f.btn.classList.add('press'), null, '+=.35').call(() => f.done.classList.add('on'), null, '+=.45')
    .to([list, f.done], { opacity: 0, duration: .5, ease: 'power1.in' }, '+=2.6').to({}, { duration: .3 });
  formTl = tl;
  formTrigger = ScrollTrigger.create({ trigger: '.ans-form', start: 'top bottom', end: 'bottom top', onToggle: (self) => (self.isActive ? tl.play() : tl.pause()) });
  tl.play();
}
let formCard = null;
function paintForm() {
  formCard = buildForm();
  if (formTl && !matchMedia('(prefers-reduced-motion: reduce)').matches) formLoop(formCard);
  else formDone(formCard);
}

// Hero: how the app works, told as a day on a split-flap departures board, one station at a time --------------------------
// A cell is two still halves and the two halves that turn over during a flip, like the flaps of a station board.
const FLAP_CHARS = 'ABCDEFGHIJKLMNOPRSTUVWXYZ0123456789';
const TONES = ['w', 'f', 's', 'r', 'q'];
const FALL = 70, RISE = 95;
function flapCell(tile = false) {
  const half = (cls) => h('span', { class: `sf-h ${cls}` }, h('i'));
  const c = { el: h('span', { class: 'sf' }), top: half('sf-t'), bot: half('sf-b'), fall: half('sf-t sf-fall'), rise: half('sf-b sf-rise'), face: ' ', goal: ' ', queue: [], busy: false, timer: 0, tile };
  c.el.append(c.top, c.bot, c.fall, c.rise);
  return c;
}
// A face is one character, or "#" and a tone for a colored tile.
function paintHalf(half, face) {
  const tone = face.length > 1 ? face.slice(1) : '';
  half.firstChild.textContent = tone ? '' : face;
  if (tone) half.dataset.t = tone; else delete half.dataset.t;
}
function setFace(c, face) {
  clearTimeout(c.timer);
  c.queue = [];
  c.face = c.goal = face;
  paintHalf(c.top, face);
  paintHalf(c.bot, face);
}
function turn(c) {
  const to = c.queue.shift();
  if (to === undefined) { c.busy = false; return; }
  c.busy = true;
  paintHalf(c.top, to);
  paintHalf(c.fall, c.face);
  paintHalf(c.rise, to);
  c.fall.animate([{ transform: 'rotateX(0deg)', opacity: 1 }, { transform: 'rotateX(-90deg)', opacity: 1 }], { duration: FALL, easing: 'cubic-bezier(.55, 0, 1, .45)' });
  const rise = c.rise.animate([{ transform: 'rotateX(90deg)', opacity: 1 }, { transform: 'rotateX(0deg)', opacity: 1 }], { duration: RISE, delay: FALL, easing: 'cubic-bezier(.25, 1.55, .55, 1)', fill: 'forwards' });
  rise.onfinish = () => { paintHalf(c.bot, to); c.face = to; rise.cancel(); turn(c); };
}
// Turns through a few random flaps before landing, the way a real board settles.
function flipTo(c, face, delay = 0, spin = 2) {
  if (face === c.goal) return;
  c.goal = face;
  clearTimeout(c.timer);
  const route = [];
  for (let i = 0; i < spin; i++) route.push(c.tile ? '#' + TONES[(Math.random() * TONES.length) | 0] : FLAP_CHARS[(Math.random() * FLAP_CHARS.length) | 0]);
  route.push(face);
  const go = () => { c.queue = route; if (!c.busy) turn(c); };
  if (delay > 0) c.timer = setTimeout(go, delay); else go();
}

const STATIONS = ['radar', 'chart', 'writer', 'guardian', 'inbox'];
const STORY = {
  en: {
    rail: 'How it works, step by step',
    steps: ['Find', 'Sort', 'Write', 'Send', 'Follow up'],
    lines: [
      'The Scout checks 53 job sources, and the Explorer adds new ones from your CV.',
      'The same job on two boards becomes one, jobs limited to countries where you cannot work leave, and the rest get a score from 0 to 100.',
      'The Writer makes a résumé for each job from the facts in your CV, and a checker ties every line to its source.',
      'The copilot opens each job filled in your browser, the AI answers the fields left empty, and you click send.',
      'The AI reads the replies in your e-mail and moves each application along. The conversation with the recruiter stays with you.',
    ],
    cols: { company: 'Company', source: 'Source', score: 'Score', model: 'Model', status: 'Status' },
    st: { new: 'NEW', queued: 'QUEUED', dup: 'DUPLICATE', abroad: 'US ONLY', writing: 'WRITING', ready: 'READY', filling: 'FILLING', sent: 'SENT', interview: 'INTERVIEW', replied: 'REPLIED', declined: 'DECLINED' },
    count: { jobs: 'JOBS', kept: 'KEPT', resumes: 'RÉSUMÉS', today: 'TODAY', replies: 'REPLIES' },
  },
  pt: {
    rail: 'Como funciona, passo a passo',
    steps: ['Encontrar', 'Separar', 'Escrever', 'Enviar', 'Acompanhar'],
    lines: [
      'O Batedor consulta 53 fontes de vagas, e o Explorador acrescenta novas a partir do seu currículo.',
      'A mesma vaga em dois quadros vira uma só, as restritas a países onde você não pode trabalhar saem, e as outras ganham nota de 0 a 100.',
      'O Redator escreve um currículo para cada vaga com os fatos do seu currículo, e o Conferente liga cada linha à origem.',
      'O copiloto abre cada vaga preenchida no seu navegador, a IA responde os campos vazios e você clica em enviar.',
      'A IA lê as respostas no seu e-mail e move cada candidatura no funil. A conversa com o recrutador fica com você.',
    ],
    cols: { company: 'Empresa', source: 'Fonte', score: 'Nota', model: 'Modelo', status: 'Status' },
    st: { new: 'NOVA', queued: 'NA FILA', dup: 'DUPLICADA', abroad: 'SÓ NOS EUA', writing: 'ESCREVENDO', ready: 'PRONTA', filling: 'PREENCHENDO', sent: 'ENVIADA', interview: 'ENTREVISTA', replied: 'RESPONDEU', declined: 'RECUSADA' },
    count: { jobs: 'VAGAS', kept: 'MANTIDAS', resumes: 'CURRÍCULOS', today: 'HOJE', replies: 'RESPOSTAS' },
  },
};
const TONE = { new: '', queued: '', dup: 'q', abroad: 'q', writing: 'w', ready: 'w', filling: 'f', sent: 's', interview: 'r', replied: 'r', declined: 'q' };
// Fictional companies: [name, where the job was found, score].
const JOBS = {
  atlas: ['ATLAS SAÚDE', 'GREENHOUSE', 87], fjord: ['FJORD CLOUD', 'LEVER', 83], vela: ['VELA LOG', 'E-MAIL', 64],
  ipe: ['IPÊ DIGITAL', 'SITE', 81], prisma: ['PRISMA DATA', 'GREENHOUSE', 71], tucano: ['TUCANO', 'WORKABLE', 76],
  koru: ['KORU LABS', 'ASHBY', 89], serra: ['SERRA TECH', 'LEVER', 0], norte: ['NORTE PAY', 'WORKABLE', 92],
  marea: ['MAREA', 'GREENHOUSE', 0], brisa: ['BRISA', 'SITE', 85], caju: ['CAJU DADOS', 'ASHBY', 58],
};
const KEPT = ['norte', 'koru', 'atlas', 'brisa', 'fjord', 'ipe'];
const WRITER = { norte: 'LLAMA', koru: 'LLAMA', atlas: 'CLAUDE', brisa: 'LLAMA', fjord: 'LLAMA', ipe: 'LLAMA' };
const ROWS = 6, LIMIT = 40;
const job = (j, st = 'new', more = {}) => ({ j, st, score: false, model: '', ...more });
const upd = (j, patch) => (s) => { const r = s.rows.find((x) => x?.j === j); if (r) Object.assign(r, patch); };
const both = (...fns) => (s) => fns.forEach((f) => f(s));
const count = (k, n) => (s) => { s.count = [k, n]; };
const screen = (...js) => (s) => { s.rows = js.map((j) => job(j)); };
// The script: [seconds into the chapter, change]. The board flips whatever each change leaves different.
const CHAPTERS = [
  { d: 4.4, ops: [
    [0, both(count('jobs', 0), (s) => { s.rows = Array(ROWS).fill(null); })],
    [0.3, both(count('jobs', 214), screen('atlas', 'fjord', 'vela', 'ipe', 'prisma', 'tucano'))],
    [1.0, count('jobs', 402)],
    [1.8, both(count('jobs', 618), screen('ipe', 'prisma', 'tucano', 'koru', 'serra', 'norte'))],
    [2.5, count('jobs', 845)],
    [3.2, both(count('jobs', 1067), screen('koru', 'serra', 'norte', 'marea', 'brisa', 'caju'))],
  ] },
  { d: 4.8, ops: [
    [0.3, upd('koru', { score: true, st: 'queued' })],
    [0.6, upd('serra', { st: 'dup' })],
    [0.9, upd('norte', { score: true, st: 'queued' })],
    [1.2, upd('marea', { st: 'abroad' })],
    [1.5, upd('brisa', { score: true, st: 'queued' })],
    [1.8, upd('caju', { score: true, st: 'queued' })],
    [2.6, count('kept', 322)],
    [3.0, (s) => { s.rows = KEPT.map((j) => job(j, 'queued', { score: true })); }],
  ] },
  { d: 4.6, ops: [
    [0, count('resumes', 49)],
    ...KEPT.flatMap((j, i) => [
      [0.3 + i * 0.42, upd(j, { st: 'writing', model: WRITER[j] })],
      [1.25 + i * 0.42, both(upd(j, { st: 'ready' }), count('resumes', 50 + i))],
    ]),
  ] },
  { d: 6.6, ops: [
    [0, count('today', 36)],
    [0.3, upd('norte', { st: 'filling', model: 'LLAMA' })],
    [0.9, upd('koru', { st: 'filling', model: 'LLAMA' })],
    [1.4, both(upd('norte', { st: 'sent' }), count('today', 37))],
    [1.7, upd('koru', { model: 'CHATGPT' })],
    [1.9, upd('atlas', { st: 'filling', model: 'LLAMA' })],
    [2.6, upd('koru', { model: 'CLAUDE' })],
    [2.9, both(upd('atlas', { st: 'sent' }), count('today', 38))],
    [3.0, upd('brisa', { st: 'filling', model: 'LLAMA' })],
    [3.5, both(upd('koru', { st: 'sent' }), count('today', 39))],
    [3.8, upd('brisa', { model: 'CHATGPT' })],
    [4.6, both(upd('brisa', { st: 'sent' }), count('today', 40))],
    [5.0, upd('fjord', { st: 'ready', model: '' })],
    [5.3, upd('ipe', { st: 'ready', model: '' })],
  ] },
  { d: 7.2, ops: [
    [0, count('replies', 0)],
    [0.8, both(upd('koru', { st: 'interview' }), count('replies', 1))],
    [2.0, both(upd('norte', { st: 'replied' }), count('replies', 2))],
    [3.1, both(upd('brisa', { st: 'declined' }), count('replies', 3))],
  ] },
];
const STATE = { ch: 0, count: ['jobs', 0], rows: Array(ROWS).fill(null), blank: true };
// The board at the start of chapter i, or at its end with through.
function stateAt(i, through = false) {
  const s = { ch: 0, count: ['jobs', 0], rows: Array(ROWS).fill(null), blank: false };
  for (let k = 0; k < (through ? i + 1 : i); k++) { s.ch = k; for (const [, op] of CHAPTERS[k].ops) op(s); }
  if (!through) { s.ch = i; for (const [t, op] of CHAPTERS[i].ops) if (t === 0) op(s); }
  return s;
}

// Columns from left to right; tier 0 shows on phones, 1 from tablets, 2 on wide screens.
const BOARD_COLS = [
  { key: 'company', w: 11, tier: 0 }, { key: 'source', w: 10, tier: 2 }, { key: 'score', w: 2, tier: 1, right: true },
  { key: 'model', w: 7, tier: 1 }, { key: 'tile', w: 1, tier: 0 }, { key: 'status', w: 11, tier: 0 },
];
const tier = () => (matchMedia('(min-width: 1001px)').matches ? 2 : matchMedia('(min-width: 641px)').matches ? 1 : 0);
let board = null, rail = null, story = null;
function buildBoard() {
  const root = $('#board');
  const cols = BOARD_COLS.filter((c) => c.tier <= tier()).map((c) => ({ ...c }));
  let p = 0;
  cols.forEach((c, i) => { if (i) p++; c.start = p; p += c.w; });
  const n = p, tileAt = cols.find((c) => c.key === 'tile').start;
  root.style.setProperty('--n', n);
  const line = (cls, tiles) => {
    const el = h('div', { class: cls });
    const cells = Array.from({ length: n }, (_, k) => { const c = flapCell(tiles && k === tileAt); el.append(c.el); return c; });
    return { el, cells };
  };
  const head = line('board-row board-head', false);
  const labels = h('div', { class: 'board-cols' });
  const names = [];
  for (const c of cols) {
    if (c.key === 'tile') continue;
    const from = c.key === 'status' ? tileAt : c.start;
    const e = h('span', { class: c.right ? 'r' : null, style: `grid-column: ${from + 1} / span ${c.start + c.w - from}` });
    names.push([e, c.key]);
    labels.append(e);
  }
  const rows = Array.from({ length: ROWS }, () => line('board-row', true));
  root.replaceChildren(head.el, labels, h('div', { class: 'board-rows' }, ...rows.map((r) => r.el)), h('div', { class: 'board-plate' }, 'Job Orchestrator'));
  board = { cols, n, head: head.cells, rows: rows.map((r) => r.cells), names };
  for (const [e, key] of names) e.textContent = STORY[lang].cols[key];
}
function headFaces(s, n, words) {
  if (s.blank) return Array(n).fill(' ');
  const left = `0${s.ch + 1} ${words.steps[s.ch].toUpperCase()}`;
  const [k, v] = s.count;
  let right = k === 'today' ? `${v}/${LIMIT} ${words.count.today}` : `${v} ${words.count[k]}`;
  if (left.length + 1 + right.length > n) right = k === 'today' ? `${v}/${LIMIT}` : String(v);
  return [...(left.padEnd(n - right.length) + right).slice(0, n)];
}
function rowFaces(r, cols, words) {
  const out = [];
  cols.forEach((c, i) => {
    if (i) out.push(' ');
    if (c.key === 'tile') { const t = r ? TONE[r.st] : ''; out.push(t ? '#' + t : ' '); return; }
    let v = '';
    if (r) {
      const [name, source, score] = JOBS[r.j];
      v = { company: name, source, score: r.score ? String(score) : '', model: r.model, status: words.st[r.st] }[c.key];
    }
    v = v.slice(0, c.w);
    out.push(...(c.right ? v.padStart(c.w) : v.padEnd(c.w)));
  });
  return out;
}
// Only the cells that change turn, in a wave that starts at the first change: down the rows, then left to right.
function renderBoard(instant = false, spin = -1) {
  if (!board) return;
  const words = STORY[lang];
  const want = [headFaces(STATE, board.n, words), ...STATE.rows.map((r) => rowFaces(r, board.cols, words))];
  const lines = [board.head, ...board.rows];
  let r0 = -1, k0 = Infinity;
  lines.forEach((cells, r) => cells.forEach((c, k) => { if (c.goal !== want[r][k]) { if (r0 < 0) r0 = r; k0 = Math.min(k0, k); } }));
  lines.forEach((cells, r) => cells.forEach((c, k) => {
    const face = want[r][k];
    if (instant) setFace(c, face);
    else if (c.goal !== face) flipTo(c, face, (r - r0) * 70 + (k - k0) * 11, spin >= 0 ? spin : face === ' ' ? (Math.random() * 2) | 0 : 1 + ((Math.random() * 3) | 0));
  }));
}

function buildRail() {
  const root = $('#rail');
  const fill = h('i', { class: 'rail-fill' });
  root.replaceChildren(h('span', { class: 'rail-track' }, fill));
  const stops = STATIONS.map((name, i) => {
    const svg = icon(name);
    const label = h('span', { class: 'rail-name' });
    const bar = h('i');
    const b = h('button', { type: 'button', class: 'rail-st' }, h('span', { class: 'rail-art' }, svg), h('span', { class: 'rail-dot' }),
      h('span', { class: 'rail-label' }, h('b', {}, String(i + 1).padStart(2, '0')), label), h('span', { class: 'rail-bar' }, bar));
    b.addEventListener('click', () => jumpTo(i));
    root.append(b);
    return { b, bar, label, idle: idle(name, svg.firstChild) };
  });
  rail = { root, fill, stops };
}
function paintStory() {
  const words = STORY[lang];
  rail.root.setAttribute('aria-label', words.rail);
  rail.stops.forEach((s, i) => { s.label.textContent = words.steps[i]; });
  for (const [e, key] of board.names) e.textContent = words.cols[key];
  $('#story-text').textContent = words.lines[STATE.ch];
  renderBoard(!story);
}
function showChapter(i, instant = false) {
  rail.stops.forEach((s, k) => {
    s.b.classList.toggle('on', k === i);
    s.b.classList.toggle('done', k < i);
    if (k === i) s.b.setAttribute('aria-current', 'step'); else s.b.removeAttribute('aria-current');
    if (story && k === i) s.idle.play(); else s.idle.pause();
  });
  gsap.to(rail.fill, { scaleX: i / (STATIONS.length - 1), duration: instant ? 0 : .7, ease: 'power2.inOut', overwrite: true });
  const text = $('#story-text');
  text.textContent = STORY[lang].lines[i];
  if (!instant) gsap.fromTo(text, { opacity: 0 }, { opacity: 1, duration: .45, overwrite: true });
}
function storyLoop() {
  const tl = gsap.timeline({ repeat: -1, paused: true });
  const bars = rail.stops.map((s) => s.bar);
  CHAPTERS.forEach((ch, i) => {
    const at = tl.duration();
    tl.addLabel(`c${i}`, at);
    tl.call(() => {
      STATE.ch = i;
      STATE.blank = false;
      if (i === 0) gsap.set(bars, { scaleX: 0 });
      for (const [t, op] of ch.ops) if (t === 0) op(STATE);
      showChapter(i);
      renderBoard();
    }, null, at);
    for (const [t, op] of ch.ops) if (t > 0) tl.call(() => { op(STATE); renderBoard(); }, null, at + t);
    tl.fromTo(bars[i], { scaleX: 0 }, { scaleX: 1, duration: ch.d, ease: 'none', immediateRender: false }, at);
  });
  // The day ends: the board clears before the next one starts.
  const end = tl.duration();
  tl.call(() => { STATE.rows = Array(ROWS).fill(null); STATE.blank = true; renderBoard(false, 0); }, null, end).set({}, {}, end + 1.3);
  return tl;
}
function jumpTo(i) {
  if (!story) {
    Object.assign(STATE, stateAt(i, true));
    showChapter(i, true);
    renderBoard(true);
    return;
  }
  story.pause();
  Object.assign(STATE, stateAt(i));
  rail.stops.forEach((s, k) => gsap.set(s.bar, { scaleX: k < i ? 1 : 0 }));
  showChapter(i);
  renderBoard();
  story.seek(story.labels[`c${i}`] + 0.001, true);
  story.play();
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

function staticScenes(n) {
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
  buildBoard();
  buildRail();
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
  formCard = buildForm();
  formDone(formCard);
  detect();
  latestRelease();
  tabs();
  copy();
  navShadow();
  await document.fonts.ready;
  sizePills();
  mach.fit();

  // The board keeps its state when a width change adds or removes columns.
  for (const q of ['(min-width: 1001px)', '(min-width: 641px)']) matchMedia(q).addEventListener('change', () => { buildBoard(); renderBoard(true); });

  const mm = gsap.matchMedia();
  mm.add({ motion: '(prefers-reduced-motion: no-preference)', still: '(prefers-reduced-motion: reduce)' }, (ctx) => {
    if (!ctx.conditions.motion) {
      const last = CHAPTERS.length - 1;
      Object.assign(STATE, stateAt(last, true));
      showChapter(last, true);
      renderBoard(true);
      return;
    }
    story = storyLoop();
    ScrollTrigger.create({ trigger: '.hero', start: 'top bottom', end: 'bottom top', onToggle: (self) => (self.isActive ? story?.play() : story?.pause()) });
    story.play();
    return () => { story = null; };
  });
  mm.add({ motion: '(prefers-reduced-motion: no-preference)', still: '(prefers-reduced-motion: reduce)', desktop: '(min-width: 1001px)' }, (ctx) => {
    const { motion, desktop } = ctx.conditions;
    if (!motion) {
      formDone(formCard);
      staticScenes(net);
      stage.forEach((it, k) => gsap.set(it.g, { opacity: k === 0 ? 1 : 0 }));
      $$('.step').forEach((s) => s.classList.add('on'));
      return;
    }
    formLoop(formCard);
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
