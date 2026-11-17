#!/usr/bin/env node
/* ------------------------------------------------------------------
   Signing test
   ------------------------------------------------------------------
   Signing has a mode of its own. On the signature step a three-line
   guide says how; Ctrl+D (or Enter while nothing is signed, or a click
   on the pad or "Start signing") opens the signing board over the whole
   window. On it:

     Enter      keep it and go on to Check
     Ctrl+Z     undo the last stroke
     Backspace  clear and start again
     Esc        cancel; the signature from before stays

   The same four are listed under the board. A pen's pointer roams the
   whole screen (Windows does not hold it as it holds the mouse), so a
   pen or mouse pressing them does nothing; a finger can, for a tablet PC
   without its keyboard. The board opens empty each time, so signing again
   replaces the signature; Clear can be undone.

   The board takes the pen from anywhere in the window and pins every
   point to its edge, so a stroke that runs off it stays on it, and a
   tap beside it marks nothing. Nothing behind the board can be clicked.

   Also checks the pen-tablet line notices a pen, and that a browser
   (which cannot update itself) shows no "Check for updates".

   Filling the screen and holding the real cursor on the board are the
   desktop app's job (full_screen and clip_cursor in
   app/src-tauri/src/desktop.rs) and cannot be seen from a browser; this
   covers everything around them.

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
  signing: signing.on,
  sheet: !document.getElementById('sign-sheet').hidden,
  ink: hasInk(state.signature),
  strokes: board.value()?.strokes.length || 0,
  guide: [...document.querySelectorAll('#sign-guide li')].map((li) => li.dataset.state || '-').join(' '),
  button: document.getElementById('btn-sign-text').textContent,
  status: document.getElementById('sign-sheet-status').textContent,
  tone: document.getElementById('sign-sheet-status').dataset.tone,
  pen: document.getElementById('pen-status').dataset.state,
  penText: document.getElementById('pen-status-text').textContent,
  sheetPen: document.getElementById('sheet-pen-status').dataset.state,
  focus: document.activeElement?.id || document.activeElement?.tagName,
}));

// a complete card, then back to the signature with the pad empty
await page.evaluate(() => loadExample());
await page.waitForFunction(() => state.photo && state.signature, null, { timeout: 20000 });
await page.evaluate(() => { goStep(SIGN_STEP, { force: true }); clearSignature(); });
await page.waitForTimeout(300);

let s = await look();
check(s.step === 'sign' && !s.signing && !s.sheet, 'the signature step opens with signing not started');
check(s.guide === 'active - -' && s.button === 'Start signing', `the guide points at step 1 (${s.guide}), the button says "${s.button}"`);
check(await page.isVisible('#sign-guide') && /Ctrl/.test(await page.textContent('#sign-guide')), 'the guide names Ctrl + D');
check(!(await page.isVisible('#update')), 'a browser shows no "Check for updates"');
check(s.pen === 'none', `no pen yet: "${s.penText}"`);

/* ---- drawing on the small pad does nothing; the board is for signing - */

const pad = await page.locator('#f-signature').boundingBox();
await page.mouse.move(pad.x + 30, pad.y + 30);
await page.mouse.down(); await page.mouse.move(pad.x + 120, pad.y + 60); await page.mouse.up();
s = await look();
check(!s.ink, 'a stroke on the small pad is not kept');
check(s.signing && s.sheet, 'a click on the pad opens the signing board');
await page.keyboard.press('Escape');
s = await look();
check(!s.signing && !s.sheet && !s.ink, 'Esc closes it, nothing signed');
check(s.focus === 'btn-sign', `focus goes back to "Start signing" (${s.focus})`);

/* ---- Ctrl+D opens the board ---------------------------------------- */

await page.evaluate(() => document.activeElement.blur());
await page.keyboard.press('Control+d');
s = await look();
check(s.signing && s.sheet, 'Ctrl+D opens the signing board');
check(/Waiting/.test(s.status), `it says it is waiting for the pen: "${s.status}"`);
check(s.focus === 'sign-sheet', `the board has the keyboard (${s.focus})`);

const bd = await page.locator('#sign-board-canvas').boundingBox();
check(bd.width > pad.width * 1.5, `the board is much bigger than the pad (${Math.round(bd.width)} vs ${Math.round(pad.width)} px)`);

const stroke = async (x0, y0, dx, dy, n = 12) => {
  await page.mouse.move(x0, y0);
  await page.mouse.down();
  for (let i = 1; i <= n; i++) await page.mouse.move(x0 + dx * i, y0 + dy * (i % 2 ? 1 : -1));
  await page.mouse.up();
};

await stroke(bd.x + 60, bd.y + bd.height * 0.6, 20, 12);
s = await look();
check(s.strokes === 1 && /1 stroke/.test(s.status), `a stroke shows on the board: "${s.status}"`);
check(await page.isVisible('#sign-cursor'), 'the pen dot shows where the pen is');

// a stroke that runs off the right and the bottom of the board stays on it
await stroke(bd.x + bd.width - 80, bd.y + bd.height - 60, 40, 30, 8);
const pts = await page.evaluate(() => board.value().strokes.flat());
const w = await page.evaluate(() => board.width());
check(pts.every((p) => p.x >= 0 && p.y >= 0 && p.x <= w + 0.5 && p.y <= bd.height + 0.5), 'a stroke that runs off the board is pinned to its edge');

// a tap beside the board marks nothing, and nothing behind it is clicked
const next = await page.locator('#btn-next').boundingBox();
const before = (await look()).strokes;
const under = await page.evaluate(([x, y]) => document.elementFromPoint(x, y)?.closest('#sign-sheet') !== null, [next.x + next.width / 2, next.y + next.height / 2]);
check(under, 'Next is under the board, out of the pen’s reach');
await page.mouse.click(4, 4);
await page.mouse.click(1250, 890);
s = await look();
check(s.step === 'sign' && s.signing && s.strokes === before, 'a tap beside the board marks nothing and clicks nothing behind it');

await page.keyboard.press('Tab');
s = await look();
check(s.signing && s.focus === 'sign-sheet', 'Tab does not leave the board');

/* ---- Ctrl+Z, Backspace, Enter on nothing ---------------------------- */

await page.keyboard.press('Control+z');
check((await look()).strokes === 1, 'Ctrl+Z takes back the last stroke');

await page.keyboard.press('Backspace');
s = await look();
check(s.strokes === 0 && s.signing && /Ctrl \+ Z/.test(s.status), `Backspace clears the board and keeps signing: "${s.status}"`);
await page.keyboard.press('Control+z');
check((await look()).strokes === 1, 'Ctrl+Z after Backspace brings the signature back');
await page.keyboard.press('Backspace');

await page.keyboard.press('Enter');
s = await look();
check(s.signing && s.tone === 'warn' && /Esc/.test(s.status), `Enter with nothing signed stays and says why: "${s.status}"`);

/* ---- Enter keeps it and moves on ------------------------------------ */

await stroke(bd.x + 100, bd.y + bd.height * 0.55, 30, 18);
await page.keyboard.press('Enter');
await page.waitForTimeout(300);
s = await look();
check(!s.signing && !s.sheet && s.step === 'check' && s.ink, `Enter keeps the signature and moves on to Check (on ${s.step})`);
const kept1 = await page.evaluate(() => ({ sig: state.signature.strokes.length, pad: pad.value()?.strokes.length }));
check(kept1.sig === 1 && kept1.pad === 1, `the kept signature is the one on the board, and the pad shows it (${JSON.stringify(kept1)})`);
await page.keyboard.press('Control+d');
check(!(await look()).sheet, 'Ctrl+D does nothing on another step');

/* ---- back on the step: the guide shows it is done ------------------- */

await page.evaluate(() => goStep(SIGN_STEP, { force: true }));
await page.waitForTimeout(200);
s = await look();
check(s.guide === 'done done active' && s.button === 'Sign again', `signed: the guide is at step 3 (${s.guide}), the button says "${s.button}"`);

// Sign again opens an empty board; Esc keeps the signature that was there
const kept = await page.evaluate(() => JSON.stringify(state.signature));
await page.click('#btn-sign');
s = await look();
check(s.signing && s.strokes === 0, 'Sign again opens an empty board, not the old signature to draw over');
await stroke(bd.x + 300, bd.y + bd.height * 0.4, 25, 10);
await page.keyboard.press('Escape');
s = await look();
check(!s.signing && (await page.evaluate(() => JSON.stringify(state.signature))) === kept, 'Esc cancels: the signature from before stays');
check(s.focus === 'btn-next', `after Esc, a signed step puts the keyboard on Next (${s.focus})`);

// with a signature, Enter on the step moves on
await page.keyboard.press('Enter');
await page.waitForTimeout(300);
s = await look();
check(s.step === 'check' && !s.sheet, `signed, Enter on the step moves on to Check (on ${s.step})`);

/* ---- with nothing signed, Enter opens the board; Next says why not --- */

await page.evaluate(() => { goStep(SIGN_STEP, { force: true }); clearSignature(); document.activeElement.blur(); });
await page.waitForTimeout(200);
await page.keyboard.press('Enter');
s = await look();
check(s.signing && s.sheet, 'unsigned, Enter on the step opens the board');
await page.keyboard.press('Escape');
await page.click('#btn-next');
await page.waitForTimeout(300);
s = await look();
const blocked = await page.evaluate(() => document.getElementById('blocked').textContent);
check(s.step === 'sign' && !s.signing && /Can’t continue/.test(blocked), `Next with no signature stays and says so ("${blocked.trim()}")`);

// leaving the step some other way closes the board
await page.keyboard.press('Control+d');
await page.evaluate(() => goStep(SIGN_STEP - 1, { force: true }));
s = await look();
check(!s.signing && !s.sheet && !(await page.evaluate(() => document.body.classList.contains('signing'))), 'changing step closes the board');

/* ---- under the board: keys to press, nothing to click --------------- */
// A pen's pointer is not held on the board as the mouse is, so a pen or a
// mouse pressing the key line must do nothing. A finger can, so a tablet
// PC without its keyboard is not stuck in signing mode.

await page.evaluate(() => { goStep(SIGN_STEP, { force: true }); clearSignature(); });
await page.click('#btn-sign');
const off = (id) => page.evaluate((i) => document.getElementById(i).hasAttribute('data-off'), id);
check(await off('sheet-keep') && await off('sheet-clear') && await off('sheet-undo'), 'on an empty board, Keep, Clear and Undo are greyed');
const area = await page.evaluate(() => {
  const r = document.getElementById('sign-board').getBoundingClientRect();
  return (r.width * r.height) / (innerWidth * innerHeight);
});
check(area > 0.7, `the board fills most of the window (${Math.round(area * 100)} %)`);
const bd2 = await page.locator('#sign-board-canvas').boundingBox();
await stroke(bd2.x + 100, bd2.y + bd2.height * 0.5, 20, 12);
await stroke(bd2.x + 400, bd2.y + bd2.height * 0.5, 20, 12);

const press = (id, pointerType) => page.evaluate(([i, t]) => {
  const el = document.getElementById(i), r = el.getBoundingClientRect();
  const at = { bubbles: true, cancelable: true, composed: true, pointerType: t, isPrimary: true, clientX: r.x + 5, clientY: r.y + 5, button: 0, buttons: 1 };
  el.dispatchEvent(new PointerEvent('pointerdown', at));
  el.dispatchEvent(new PointerEvent('pointerup', { ...at, buttons: 0 }));
  el.dispatchEvent(new MouseEvent('click', at));
}, [id, pointerType]);

await press('sheet-cancel', 'pen');
await press('sheet-clear', 'pen');
s = await look();
check(s.signing && s.strokes === 2, 'a pen pressing Cancel or Clear under the board does nothing');
await page.click('#sheet-cancel');
s = await look();
check(s.signing && s.strokes === 2, 'nor does a mouse click');

await press('sheet-undo', 'touch');
check((await look()).strokes === 1, 'a finger on Undo takes back a stroke');
await press('sheet-clear', 'touch');
check((await look()).strokes === 0, 'a finger on Clear clears');
await press('sheet-undo', 'touch');
check((await look()).strokes === 1, 'and Undo brings it back');
await press('sheet-cancel', 'touch');
s = await look();
check(!s.signing && !s.ink, 'a finger on Cancel closes the board and keeps nothing');
await page.click('#btn-sign');
await stroke(bd2.x + 100, bd2.y + bd2.height * 0.5, 20, 12);
await press('sheet-keep', 'touch');
await page.waitForTimeout(300);
s = await look();
check(!s.signing && s.ink && s.step === 'check', `a finger on Keep keeps it and moves on (on ${s.step})`);

/* ---- a scanned signature still shows on the step -------------------- */

await page.evaluate(() => {
  const mask = document.createElement('canvas'); mask.width = 300; mask.height = 100;
  const c = mask.getContext('2d'); c.fillRect(20, 40, 260, 20);
  state.signature = { kind: 'image', mask, fileName: 'scan.png' };
  goStep(SIGN_STEP, { force: true }); render();
});
const scan = await page.evaluate(() => ({ pad: document.getElementById('sig-pad').getBoundingClientRect().height, img: document.getElementById('sig-image').getBoundingClientRect().height }));
check(scan.pad > 60 && scan.img > 40, `a scanned signature shows in the box (box ${Math.round(scan.pad)} px, picture ${Math.round(scan.img)} px tall)`);

/* ---- Ctrl+D works with a side panel open ---------------------------- */

await page.evaluate(() => { clearSignature(); document.activeElement.blur(); });
await page.click('text=Issued IDs');
await page.keyboard.press('Control+d');
s = await look();
check(s.signing && s.sheet, 'Ctrl+D opens the board with a side panel open');
await page.keyboard.press('Escape');
s = await look();
check(!s.signing && (await page.evaluate(() => !!state.tool)), 'Esc closes only the board, not the panel too');
await page.evaluate(() => { state.tool = null; renderTools(); render(); });

/* ---- a pen is noticed ---------------------------------------------- */

await page.evaluate(() => document.getElementById('f-signature').dispatchEvent(
  new PointerEvent('pointerover', { pointerType: 'pen', bubbles: true })));
s = await look();
check(s.pen === 'found' && s.sheetPen === 'found', `a pen over the page counts as detected, on the step and the board: "${s.penText}"`);

/* ---- done --------------------------------------------------------- */

await browser.close();
if (errors.length) { console.error('\npage errors:'); errors.forEach((e) => console.error('  ' + e)); }
if (fail.length || errors.length) {
  console.error(`\nFAIL — ${fail.length} check(s) failed${errors.length ? `, ${errors.length} page error(s)` : ''}`);
  process.exit(1);
}
console.log('\nPASS — signing mode opens with Ctrl+D, keeps the pen on the board, and the keyboard ends it');
