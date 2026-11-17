#!/usr/bin/env node
/* ------------------------------------------------------------------
   Layout test
   ------------------------------------------------------------------
   The window is four columns — the tool rail, whichever tool it has
   open, the card, the form — and the one way that goes wrong is a
   column that will not shrink. Something inside the tray keeps its
   natural width, the tray overflows, and the preview is drawn across
   the form. It looks like a z-index problem and it is not: it is an
   auto-sized track refusing to go below its content's min-content.

   It has happened twice. So: open the app at the sizes HR actually
   uses, in the states that hold the widest content, and check that
   nothing inside the tray reaches past it and that the page never
   scrolls sideways. Also that the rail and the open tool stay in view
   when the page scrolls.

     npm run test:layout
   ------------------------------------------------------------------ */
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const app = pathToFileURL(join(here, '..', 'Alpha Premier ID Maker.html')).href;

const pw = await import(process.env.PLAYWRIGHT_MODULE || 'playwright');
const chromium = pw.chromium ?? pw.default.chromium;
const browser = await chromium.launch();
const errors = [];
const fail = [];

// the laptop the cards are issued from, a larger monitor, the width the
// form drops under the card, and a phone
const SIZES = [[1258, 748], [1600, 900], [1024, 800], [430, 900]];

/** Does anything inside the tray stick out of it? */
async function measure(page) {
  return page.evaluate(() => {
    const stage = document.querySelector('.stage');
    if (!stage || stage.hidden) return null;
    const box = stage.getBoundingClientRect();
    let worst = { over: 0, what: '' };
    for (const el of stage.querySelectorAll('*')) {
      const r = el.getBoundingClientRect();
      if (!r.width) continue;
      const over = Math.max(Math.round(r.right - box.right), Math.round(box.left - r.left));
      if (over > worst.over) worst = { over, what: `${el.tagName.toLowerCase()}.${el.className || el.id}` };
    }
    return {
      ...worst,
      sideways: document.documentElement.scrollWidth > window.innerWidth + 1,
      stage: Math.round(box.width),
    };
  });
}

const states = {
  'step 1': async () => {},
  'step 1, a tool open': async (page) => { await page.click('#rail-printer'); },
  'the map open': async (page) => { await page.evaluate(() => startAdjusting()); },
  'step 6': async (page) => {
    await page.evaluate(() => loadExample());
    await page.waitForFunction(() => state.step === LAST_STEP && state.photo);
  },
  'step 6, a tool open': async (page) => {
    await page.evaluate(() => loadExample());
    await page.waitForFunction(() => state.step === LAST_STEP && state.photo);
    await page.click('#rail-printer');
  },
  'step 6, both sides': async (page) => {
    await page.evaluate(() => loadExample());
    await page.waitForFunction(() => state.step === LAST_STEP && state.photo);
    await page.evaluate(() => setLayout('duplex'));
  },
};

for (const [w, h] of SIZES) {
  for (const [name, prepare] of Object.entries(states)) {
    const page = await browser.newPage({ viewport: { width: w, height: h } });
    page.on('pageerror', (e) => errors.push(`${w}×${h} ${name}: ${e.message}`));
    await page.goto(app);
    await page.waitForSelector('#card-box:not([hidden])', { timeout: 20000 });
    await prepare(page);
    await page.waitForTimeout(400);
    const m = await measure(page);
    await page.close();
    if (!m) continue;
    const label = `${w}×${h}  ${name}`.padEnd(34);
    const bad = m.over > 1 || m.sideways;
    console.log(`${bad ? ' FAIL ' : '  ok  '} ${label} tray ${String(m.stage).padStart(4)}px`
      + (m.over > 1 ? `, ${m.what} sticks out ${m.over}px` : '')
      + (m.sideways ? ', the page scrolls sideways' : ''));
    if (bad) fail.push(`${w}×${h} ${name}`);
  }
}

/* ---- the tools stay put while the page scrolls ----------------------
   The last step scrolls by most of a screen on the laptop. The rail and
   the open tool are pinned like the card, so they must still be in view
   at the bottom of it. (On a phone they sit under the work instead.) */
for (const [w, h] of SIZES.filter(([w]) => w > 820)) {
  const page = await browser.newPage({ viewport: { width: w, height: h } });
  page.on('pageerror', (e) => errors.push(`${w}×${h} pinned: ${e.message}`));
  await page.goto(app);
  await page.waitForSelector('#card-box:not([hidden])', { timeout: 20000 });
  await page.evaluate(() => loadExample());
  await page.waitForFunction(() => state.step === LAST_STEP && state.photo);
  await page.click('#rail-register');
  await page.waitForTimeout(300);
  const p = await page.evaluate(() => {
    window.scrollTo(0, document.documentElement.scrollHeight);
    const top = (sel) => Math.round(document.querySelector(sel).getBoundingClientRect().top);
    return { scrolled: Math.round(scrollY), rail: top('.rail'), drawer: top('.drawer') };
  });
  await page.close();
  // a window tall enough not to scroll has nothing to pin against
  const ok = p.scrolled === 0 || (p.rail >= 0 && p.rail < 40 && p.drawer >= 0 && p.drawer < 40);
  const label = `${w}×${h}  tools pinned`.padEnd(34);
  console.log(`${ok ? '  ok  ' : ' FAIL '} ${label} ${p.scrolled ? `scrolled ${p.scrolled}px, rail at ${p.rail}px, panel at ${p.drawer}px` : 'fits without scrolling'}`);
  if (!ok) fail.push(`${w}×${h} tools pinned`);
}

await browser.close();
if (errors.length) { console.error('\npage errors:'); errors.forEach((e) => console.error('  ' + e)); }
if (fail.length || errors.length) {
  console.error(`\nFAIL — ${fail.length} layout(s) overflow${errors.length ? `, ${errors.length} page error(s)` : ''}`);
  process.exit(1);
}
console.log('\nPASS — nothing escapes the tray at any size, and the tools stay pinned');
