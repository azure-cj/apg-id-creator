#!/usr/bin/env node
/* ------------------------------------------------------------------
   Moving the photo on the card
   ------------------------------------------------------------------
   The photo is placed by itself; when it needs a nudge, it is moved on
   the card: drag to move, scroll to resize, double-click (or Reset
   under the card) to put it back. With the card focused, the arrow
   keys move it (Shift: four times as far), + and − resize it, 0 puts
   it back. There is no separate panel of buttons for it any more.

   A gold-edged bar under the card says so (Drag · Scroll ·
   Double-click, the last turning into a Reset button once the photo
   has moved), and the first time, a hand on the card shows the drag
   until the photo is first moved.

   Only on the photo step, and only on the front. On every other step
   the card is just looked at: a drag does nothing and scrolling over
   it scrolls the page, so checking a name cannot shift the photo.

     npm run test:photo-adjust
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
const check = (ok, what) => { console.log(`${ok ? '  ok  ' : ' FAIL '} ${what}`); if (!ok) fail.push(what); };

const page = await browser.newPage({ viewport: { width: 1366, height: 768 } });
page.on('pageerror', (e) => errors.push(e.message));
await page.route(/^https?:/, (r) => r.abort());
await page.goto(app);
await page.waitForSelector('#card-box:not([hidden])', { timeout: 20000 });

const look = () => page.evaluate(() => {
  const c = document.getElementById('card-canvas');
  return {
    step: STEPS[state.step].id,
    adjust: { ...state.adjust },
    tools: !document.getElementById('photo-tools').hidden,
    reset: !document.getElementById('btn-photo-reset').disabled,
    coach: !document.getElementById('photo-coach').hidden,
    badge: document.getElementById('photo-zoom-badge').hidden ? null : document.getElementById('photo-zoom-badge').textContent,
    draggable: c.classList.contains('draggable'),
    tabIndex: c.tabIndex,
    focus: document.activeElement?.id,
  };
});

/** Drag across the card by (dx, dy) screen pixels, from its middle. */
async function drag(dx, dy) {
  const box = await page.locator('#card-canvas').boundingBox();
  const x = box.x + box.width / 2, y = box.y + box.height / 2;
  await page.mouse.move(x, y);
  await page.mouse.down();
  await page.mouse.move(x + dx / 2, y + dy / 2);
  await page.mouse.move(x + dx, y + dy);
  await page.mouse.up();
}
/** Scroll over the card; says whether the app took the scroll for itself. */
async function wheel(deltaY) {
  const box = await page.locator('#card-canvas').boundingBox();
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
  await page.evaluate(() => { window.__wheelTaken = null; addEventListener('wheel', (e) => { window.__wheelTaken = e.defaultPrevented; }, { once: true }); });
  await page.mouse.wheel(0, deltaY);
  await page.waitForTimeout(50);
  return page.evaluate(() => window.__wheelTaken);
}
const reset = () => page.evaluate(() => { state.adjust = { dx: 0, dy: 0, zoom: 1 }; render(); });
const near = (a, b, tol = 0.15) => Math.abs(a - b) <= tol;

/* ---- the old panel is gone ------------------------------------------- */
check(await page.evaluate(() => !document.getElementById('adjust') && !document.querySelector('.nudge') && !document.getElementById('f-zoom')),
  'no "Move or resize it" panel, arrow buttons or size slider in the form');

/* ---- the photo step ---------------------------------------------------- */
await page.evaluate(() => loadExample());
await page.waitForFunction(() => state.photo, null, { timeout: 20000 });
await page.evaluate(() => goStep(PHOTO_STEP, { force: true }));
await reset();
let s = await look();
check(s.step === 'photo' && s.tools && s.draggable && s.tabIndex === 0,
  'on the photo step the card can be moved: the bar shows under it, it has a move cursor and takes keyboard focus');
check(await page.evaluate(() => document.getElementById('photo-tools').innerText.replace(/\s+/g, ' ').trim().toLowerCase())
  === 'adjust the photo on the card drag to move scroll to resize double-click to reset', 'the bar names all three: drag to move, scroll to resize, double-click to reset');
check(!s.reset, 'Reset is not offered before anything has moved');
check(s.coach, 'the first time, a hand on the card shows the drag');

const cardW = await page.evaluate(() => document.getElementById('card-box').clientWidth);
const mmPerPx = 54 / cardW;
await drag(40, 20);
s = await look();
await page.waitForTimeout(400);
check(!(await look()).coach && await page.evaluate(() => localStorage.getItem('apg-id-photo-hint-done') === '1'),
  'it goes as soon as the photo is first moved, for good');
check(near(s.adjust.dx, 40 * mmPerPx) && near(s.adjust.dy, 20 * mmPerPx),
  `a drag moves the photo with the pointer (${s.adjust.dx} mm, ${s.adjust.dy} mm for 40 × 20 px)`);
check(s.reset && await page.evaluate(() => document.getElementById('btn-photo-reset').innerText.replace(/\s+/g, ' ').trim()) === 'Reset or double-click',
  'once moved, "Double-click to reset" becomes a Reset button');

check(await wheel(-100) === true, 'scrolling over the card is taken by the card, not the page');
s = await look();
check(near(s.adjust.zoom, 1.05, 0.001), `scrolling up enlarges it (${s.adjust.zoom})`);
check(s.badge === '105 %', `the size shows big on the card while it changes (${s.badge})`);
await page.waitForTimeout(1000);
check((await look()).badge === null, 'and goes away again');
for (let i = 0; i < 30; i++) await wheel(100);
s = await look();
check(near(s.adjust.zoom, 0.5, 0.001) && s.badge === '50 % · smallest', `it cannot be made smaller than 50 %, and says so (${s.badge})`);

await page.click('#btn-photo-reset');
s = await look();
check(s.adjust.dx === 0 && s.adjust.dy === 0 && s.adjust.zoom === 1 && !s.reset, 'Reset puts it back where the app placed it, and goes back to "Double-click to reset"');

await drag(-30, 0);
await page.dblclick('#card-canvas');
s = await look();
check(s.adjust.dx === 0 && s.adjust.zoom === 1, 'a double-click on the card puts it back too');

/* ---- the keyboard ------------------------------------------------------ */
await page.focus('#card-canvas');
await page.keyboard.press('ArrowRight');
await page.keyboard.press('ArrowRight');
await page.keyboard.press('ArrowUp');
s = await look();
check(s.adjust.dx === 1 && s.adjust.dy === -0.5, `arrow keys move it half a millimetre a press (${s.adjust.dx}, ${s.adjust.dy})`);
await page.keyboard.press('Shift+ArrowDown');
s = await look();
check(s.adjust.dy === 1.5, `Shift moves it 2 mm (${s.adjust.dy})`);
await page.keyboard.type('+');
await page.keyboard.type('=');
s = await look();
check(near(s.adjust.zoom, 1.1, 0.001), `+ enlarges it (${s.adjust.zoom})`);
await page.keyboard.type('-');
s = await look();
check(near(s.adjust.zoom, 1.05, 0.001), `− makes it smaller (${s.adjust.zoom})`);
check(s.step === 'photo' && s.focus === 'card-canvas', 'the keys stay on the card: no step change, focus stays');
await page.keyboard.type('0');
s = await look();
check(s.adjust.dx === 0 && s.adjust.dy === 0 && s.adjust.zoom === 1, '0 puts it back');

/* ---- the back of the card --------------------------------------------- */
await page.click('#side-back');
s = await look();
check(!s.tools && !s.draggable && s.tabIndex === -1, 'on the back there is nothing to move: no hint, no move cursor, no focus');
await drag(40, 40);
check((await look()).adjust.dx === 0, 'and a drag there moves nothing');
await page.click('#side-front');

/* ---- every other step -------------------------------------------------- */
await drag(20, 0);
const kept = (await look()).adjust.dx;
for (const id of ['name', 'title', 'number', 'sign']) {
  await page.evaluate((id) => goStep(STEPS.findIndex((x) => x.id === id), { force: true }), id);
  s = await look();
  const shape = !s.tools && !s.draggable && s.tabIndex === -1;
  await drag(40, 40);
  const taken = await wheel(-100);
  const after = await look();
  check(shape && after.adjust.dx === kept && after.adjust.zoom === 1 && taken === false,
    `on the ${id} step the card is only looked at: a drag or a scroll leaves the photo alone, and the scroll is the page's`);
}
await page.evaluate(() => goStep(PHOTO_STEP, { force: true }));
s = await look();
check(s.adjust.dx === kept, 'what was set on the photo step is kept when coming back to it');
check(!s.coach, 'and the hand does not come back once the photo has been moved');

/* ---- it fits ----------------------------------------------------------- */
const fit = await page.evaluate(() => {
  const r = document.getElementById('photo-tools').getBoundingClientRect();
  return { bottom: Math.round(r.bottom), inner: innerHeight, over: document.scrollingElement.scrollHeight - innerHeight };
});
check(fit.bottom <= fit.inner, `at 1366 × 768 the hint under the card is on screen (bottom ${fit.bottom} of ${fit.inner})`);
check(fit.over <= 0, `and the photo step still does not scroll (${fit.over} px over)`);

/* ---- a new photo starts afresh ---------------------------------------- */
check(await page.evaluate(async () => {
  const c = Object.assign(document.createElement('canvas'), { width: 4, height: 4 });
  const blob = await new Promise((r) => c.toBlob(r, 'image/png'));
  const input = document.getElementById('f-photo');
  const dt = new DataTransfer(); dt.items.add(new File([blob], 'x.png', { type: 'image/png' }));
  input.files = dt.files; input.dispatchEvent(new Event('change', { bubbles: true }));
  await new Promise((r) => setTimeout(r, 1500));
  return state.adjust.dx === 0 && state.adjust.zoom === 1;
}), 'uploading another photo starts it afresh');

await browser.close();
if (errors.length) { console.error('\npage errors:'); errors.forEach((e) => console.error('  ' + e)); }
if (fail.length || errors.length) {
  console.error(`\nFAIL — ${fail.length} check(s) failed${errors.length ? `, ${errors.length} page error(s)` : ''}`);
  process.exit(1);
}
console.log('\nPASS — the photo is moved on the card, on the photo step only');
