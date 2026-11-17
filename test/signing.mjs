#!/usr/bin/env node
/* ------------------------------------------------------------------
   Signing test
   ------------------------------------------------------------------
   Putting the pen or mouse down on the signature pad starts signing.
   While signing, nothing but the pad answers the pointer — a pen that
   drifts over Next must not click it — and the keyboard ends it: Enter
   moves on, Backspace starts again, Esc stops.

   Also checks the pen-tablet line notices a pen, and that a browser
   (which cannot update itself) shows no "Check for updates".

   Holding the real cursor inside the pad is the desktop app's job
   (ClipCursor, app/src-tauri/src/desktop.rs) and cannot be seen from a
   browser; this covers everything around it.

     npm run test:signing
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

const page = await browser.newPage({ viewport: { width: 1258, height: 900 } });
page.on('pageerror', (e) => errors.push(e.message));
// No network: the shared register then settles as unreachable instead of
// holding the number step at "Checking…", which is not what this tests.
await page.route(/^https?:/, (r) => r.abort());
await page.goto(app);
await page.waitForSelector('#card-box:not([hidden])', { timeout: 20000 });

const look = () => page.evaluate(() => ({
  step: STEPS[state.step].id,
  signing: document.body.classList.contains('signing'),
  ink: hasInk(state.signature),
  pen: document.getElementById('pen-status').dataset.state,
  penText: document.getElementById('pen-status-text').textContent,
  keys: document.getElementById('sign-keys').textContent,
}));

// a complete card, then back to the signature with the pad empty
await page.evaluate(() => loadExample());
await page.waitForFunction(() => state.photo && state.signature, null, { timeout: 20000 });
await page.evaluate(() => { goStep(SIGN_STEP, { force: true }); clearSignature(); });
await page.waitForTimeout(300);

let s = await look();
check(s.step === 'sign' && !s.signing, 'the signature step opens without signing having started');
check(!(await page.isVisible('#update')), 'a browser shows no "Check for updates"');
check(s.pen === 'none', `no pen yet: "${s.penText}"`);

const box = await page.locator('#f-signature').boundingBox();
const draw = async (dx = 0) => {
  await page.mouse.move(box.x + 40 + dx, box.y + box.height * 0.6);
  await page.mouse.down();
  for (let i = 1; i <= 12; i++) await page.mouse.move(box.x + 40 + dx + i * 12, box.y + box.height * (0.6 - (i % 3) * 0.08));
  await page.mouse.up();
};

/* ---- putting the mouse down starts signing ------------------------- */

await draw();
s = await look();
check(s.signing, 'drawing on the pad starts signing');
check(s.ink, 'the stroke is kept');
check(/Enter/.test(s.keys), `the keys are spelled out: "${s.keys}"`);

/* ---- while signing, clicks elsewhere do nothing -------------------- */

const next = await page.locator('#btn-next').boundingBox();
await page.mouse.click(next.x + next.width / 2, next.y + next.height / 2);
await page.waitForTimeout(200);
s = await look();
check(s.step === 'sign' && s.signing, 'clicking Next while signing does nothing');

await page.evaluate(() => document.getElementById('btn-back').click());   // even a scripted click
await page.waitForTimeout(200);
check((await look()).step === 'sign', 'a click event on Back while signing does nothing');

await page.keyboard.press('Tab');
check((await look()).signing, 'Tab does not leave the pad');

/* ---- Backspace starts again, Esc stops ----------------------------- */

await page.keyboard.press('Backspace');
s = await look();
check(!s.ink && s.signing, 'Backspace clears the pad and keeps signing');

await draw(30);
await page.keyboard.press('Escape');
s = await look();
check(!s.signing && s.step === 'sign' && s.ink, 'Esc stops signing, keeps the signature, stays on the step');

/* ---- Enter finishes and moves on ----------------------------------- */

await draw(60);
check((await look()).signing, 'drawing again starts signing again');
await page.keyboard.press('Enter');
await page.waitForTimeout(400);
s = await look();
check(!s.signing && s.step === 'check', `Enter finishes and moves on to Check (on ${s.step})`);

/* ---- Enter on an empty pad says why it cannot move on --------------- */

await page.evaluate(() => { goStep(SIGN_STEP, { force: true }); clearSignature(); });
await page.waitForTimeout(200);
await page.mouse.click(box.x + 20, box.y + 20);         // a tap starts signing (and leaves a dot)
await page.keyboard.press('Backspace');                 // so the pad is empty
await page.keyboard.press('Enter');
await page.waitForTimeout(300);
s = await look();
const blocked = await page.evaluate(() => document.getElementById('blocked').textContent);
check(s.step === 'sign' && !s.signing && /Can’t continue/.test(blocked), `Enter with no signature stays and says so ("${blocked.trim()}")`);

/* ---- a pen is noticed ---------------------------------------------- */

await page.evaluate(() => document.getElementById('f-signature').dispatchEvent(
  new PointerEvent('pointerover', { pointerType: 'pen', bubbles: true })));
s = await look();
check(s.pen === 'found', `a pen over the page counts as detected: "${s.penText}"`);

/* ---- done --------------------------------------------------------- */

await browser.close();
if (errors.length) { console.error('\npage errors:'); errors.forEach((e) => console.error('  ' + e)); }
if (fail.length || errors.length) {
  console.error(`\nFAIL — ${fail.length} check(s) failed${errors.length ? `, ${errors.length} page error(s)` : ''}`);
  process.exit(1);
}
console.log('\nPASS — signing holds the pointer on the pad, and the keyboard ends it');
