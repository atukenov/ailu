// Builds every Ailu logo and icon file from one drawing, the "ailu" wordmark: the "a" is a gold
// crescent moon (ай), the dot of the "i" is a pink heart. The app icon is its first two letters,
// "ai", which is how "ай", the moon, is spelled. Run from the repo root:  node brand/build.mjs
// Needs Playwright's Chromium (renders the SVGs) and Python 3 with Pillow (drops the alpha channel
// from icons that must be opaque, which App Store Connect requires).
import { createRequire } from 'node:module';
import { mkdirSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { dirname } from 'node:path';

// Plain require, so a globally installed Playwright is found too.
const { chromium } = createRequire(import.meta.url)('playwright');

const C = {
  night: '#170F1D', plumTop: '#3A1C47', plumMid: '#1F1228', plumLow: '#120B16',
  pink: ['#FFB3CF', '#FF6F9F'], gold: ['#FFF1C9', '#FFD27A', '#FFAE5E'], moon: '#FFE7B0',
  cream: '#FBF3EC',
};
const HEART = 'M50,88 C20,65 2,48 2,28 C2,13 14,2 28,2 C38,2 46,8 50,16 C54,8 62,2 72,2 C86,2 98,13 98,28 C98,48 80,65 50,88 Z';

// The wordmark lives in one coordinate system: baseline 640, x-height 230, stroke 58.
const B = 640, XH = 230, SW = 58, TOP = B - XH + SW / 2, BOT = B - SW / 2;
const G = {
  // The crescent: a full disc with a smaller one cut out on the right, so it opens
  // into the stem the way a "c" opens into the stem of an "a".
  a: { x0: 115, x1: 345 },
  i: { x0: 401, x1: 459 },
  l: { x0: 511, x1: 569 },
  u: { x0: 621, x1: 849 },
};

function glyphs(id, letters, { ink = C.cream, mono = null } = {}) {
  const gold = mono ?? `url(#${id}g)`, pink = mono ?? `url(#${id}p)`, line = mono ?? ink;
  const defs = `<defs>
<linearGradient id="${id}g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="${C.gold[0]}"/><stop offset=".5" stop-color="${C.gold[1]}"/><stop offset="1" stop-color="${C.gold[2]}"/></linearGradient>
<linearGradient id="${id}p" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="${C.pink[0]}"/><stop offset="1" stop-color="${C.pink[1]}"/></linearGradient>
<mask id="${id}c" maskUnits="userSpaceOnUse" x="0" y="0" width="1000" height="1000"><rect width="1000" height="1000" fill="#fff"/><circle cx="292" cy="${B - XH / 2}" r="92" fill="#000"/></mask></defs>`;
  const parts = {
    a: `<circle cx="230" cy="${B - XH / 2}" r="${XH / 2}" fill="${gold}" mask="url(#${id}c)"/>
<path d="M316,${TOP} V${BOT}" stroke="${line}"/>`,
    i: `<path d="M430,${TOP} V${BOT}" stroke="${line}"/>
<path d="${HEART}" transform="translate(430 ${TOP - 118}) scale(0.72) translate(-50 -45)" fill="${pink}"/>`,
    l: `<path d="M540,${B - XH - 110} V${BOT}" stroke="${line}"/>`,
    u: `<path d="M650,${TOP} V${B - 85 - SW / 2 + 0} A85,85 0 0 0 820,${B - 85 - SW / 2} V${TOP} M820,${B - 85 - SW / 2} V${BOT}" stroke="${line}"/>`,
  };
  return defs + `<g fill="none" stroke-width="${SW}" stroke-linecap="round">${[...letters].map((l) => parts[l]).join('')}</g>`;
}
// Bounding boxes in wordmark units (heart top for "i", ascender top for "l").
const BOX = { ai: [115, 290, 459, 640], ailu: [115, 271, 849, 640] };

// The night sky behind the icon: plum glowing from the top, a soft light behind the letters, a few stars.
function sky(stars = true) {
  const s = stars ? [[205, 200, 7, .55], [820, 190, 8, .7], [868, 280, 4, .5], [170, 800, 4, .35], [850, 820, 5, .4]]
    .map(([x, y, r, o]) => `<circle cx="${x}" cy="${y}" r="${r}" fill="${C.moon}" opacity="${o}"/>`).join('') : '';
  return `<defs><radialGradient id="sky" cx="50%" cy="28%" r="80%"><stop offset="0" stop-color="${C.plumTop}"/><stop offset=".55" stop-color="${C.plumMid}"/><stop offset="1" stop-color="${C.plumLow}"/></radialGradient>
<radialGradient id="glow" cx="50%" cy="50%" r="50%"><stop offset="0" stop-color="#FFD27A" stop-opacity=".16"/><stop offset=".6" stop-color="#FF8FB8" stop-opacity=".05"/><stop offset="1" stop-color="#FF8FB8" stop-opacity="0"/></radialGradient></defs>
<rect width="1024" height="1024" fill="url(#sky)"/><circle cx="512" cy="530" r="440" fill="url(#glow)"/>${s}`;
}
// Place a box of wordmark units so its centre sits at (cx, cy) and it is `w` pixels wide.
function fit([x0, y0, x1, y1], cx, cy, w) {
  const k = w / (x1 - x0);
  return `translate(${cx} ${cy}) scale(${k}) translate(${-(x0 + x1) / 2} ${-(y0 + y1) / 2})`;
}
const svg = (vb, body) => `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${vb}">${body}</svg>\n`;
const icon = (w = 560, bg = true, opts = {}) => svg('0 0 1024 1024', (bg ? sky() : '') + `<g transform="${fit(BOX.ai, 512, 528, w)}">${glyphs('i', 'ai', opts)}</g>`);

const pad = 24;
const [lx0, ly0, lx1, ly1] = BOX.ailu;
const logoVB = `${lx0 - pad} ${ly0 - pad} ${lx1 - lx0 + 2 * pad} ${ly1 - ly0 + 2 * pad}`;
const out = {
  'brand/ailu-icon.svg': icon(),
  'brand/ailu-logo.svg': svg(logoVB, glyphs('w', 'ailu')),
  'brand/ailu-logo-on-light.svg': svg(logoVB, glyphs('w', 'ailu', { ink: '#2A1534' })),
  'brand/ailu-logo-mono.svg': svg(logoVB, glyphs('w', 'ailu', { mono: C.cream })),
  'web/public/favicon.svg': svg('0 0 64 64', `<rect width="64" height="64" rx="14" fill="${C.night}"/><g transform="${fit(BOX.ai, 32, 33, 40)}">${glyphs('f', 'ai')}</g>`),
};
out['web/public/logo.svg'] = out['brand/ailu-logo.svg'];
for (const [p, s] of Object.entries(out)) { mkdirSync(dirname(p), { recursive: true }); writeFileSync(p, s); }

const fg = icon(430, false);                      // inside Android's 66/108 safe circle
const mono = icon(430, false, { mono: '#FFFFFF' });
const bg = svg('0 0 1024 1024', sky(false));
const aiTight = svg(`${BOX.ai[0] - 10} ${BOX.ai[1] - 10} ${BOX.ai[2] - BOX.ai[0] + 20} ${BOX.ai[3] - BOX.ai[1] + 20}`, glyphs('s', 'ai'));
const logoW = lx1 - lx0 + 2 * pad, logoH = ly1 - ly0 + 2 * pad;
// [file, svg, width, height, transparent?]
const pngs = [
  ['app/assets/images/icon.png', out['brand/ailu-icon.svg'], 1024, 1024, false],
  ['app/assets/images/android-icon-foreground.png', fg, 1024, 1024, true],
  ['app/assets/images/android-icon-background.png', bg, 1024, 1024, false],
  ['app/assets/images/android-icon-monochrome.png', mono, 1024, 1024, true],
  ['app/assets/images/splash-icon.png', aiTight, 3 * 120, Math.round(3 * 120 * 370 / 364), true],
  ['app/assets/images/favicon.png', out['web/public/favicon.svg'], 48, 48, true],
  ['app/assets/expo.icon/Assets/ailu-ai.png', icon(560, false), 1024, 1024, true],
  ['brand/png/ailu-icon-1024.png', out['brand/ailu-icon.svg'], 1024, 1024, false],
  ['brand/png/play-store-icon-512.png', out['brand/ailu-icon.svg'], 512, 512, false],
  ['brand/png/ailu-logo.png', out['brand/ailu-logo.svg'], Math.round(logoW * 1.5), Math.round(logoH * 1.5), true],
  ['web/public/apple-touch-icon.png', out['brand/ailu-icon.svg'], 180, 180, false],
  ['web/public/icon-512.png', out['brand/ailu-icon.svg'], 512, 512, false],
];
const browser = await chromium.launch();
const page = await browser.newPage({ deviceScaleFactor: 1 });
for (const [file, s, w, h, transparent] of pngs) {
  await page.setViewportSize({ width: w, height: h });
  await page.setContent(`<html><body style="margin:0;background:transparent">${s.replace('<svg ', `<svg width="${w}" height="${h}" `)}</body></html>`);
  mkdirSync(dirname(file), { recursive: true });
  await page.screenshot({ path: file, omitBackground: transparent, clip: { x: 0, y: 0, width: w, height: h } });
}
await browser.close();

const opaque = pngs.filter((p) => !p[4]).map((p) => p[0]);
execFileSync('python3', ['-c', 'import sys\nfrom PIL import Image\nfor f in sys.argv[1:]: Image.open(f).convert("RGB").save(f, optimize=True)', ...opaque], { stdio: 'inherit' });
console.log(`wrote ${Object.keys(out).length} SVGs and ${pngs.length} PNGs`);
