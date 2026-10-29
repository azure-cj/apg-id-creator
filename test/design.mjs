#!/usr/bin/env node
/* ------------------------------------------------------------------
   Card-design test
   ------------------------------------------------------------------
   Changes the card the way an operator would — the map first, then the
   artwork through the file inputs — and checks the things that would
   quietly ruin a card if they broke:

     1. opening the map changes nothing until a box is moved
     2. the built-in card keeps its stepped photo cut-out when the map
        is dragged, rather than falling back to a rectangle
     3. every way of closing the drawer also ends the editing, instead
        of stranding the boxes on a card with no form and no Done
     4. artwork of the wrong shape is refused, with a size in the message
     5. the right shape reaches the printed card
     6. dragging a box moves what is drawn, by the amount dragged
     7. it survives a restart, and "use the built-in" undoes it

     npm run test:design
   ------------------------------------------------------------------ */
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const app = pathToFileURL(join(here, '..', 'Alpha Premier ID Maker.html')).href;

const pw = await import(process.env.PLAYWRIGHT_MODULE || 'playwright');
const chromium = pw.chromium ?? pw.default.chromium;
const browser = await chromium.launch();
const page = await browser.newPage();
const errors = [];
page.on('pageerror', (e) => errors.push(e.message));

const fail = [];
const check = (ok, what) => { console.log(`${ok ? '  ok  ' : ' FAIL '} ${what}`); if (!ok) fail.push(what); };

await page.goto(app);
await page.waitForSelector('#card-box:not([hidden])', { timeout: 20000 });

/* ---- the fixtures, drawn in the page ----------------------------- */

await page.evaluate(() => {
  // a design that looks nothing like the built-in one, so a mix-up shows
  window.makeArtwork = (w, h, back, stripe) => {
    const c = document.createElement('canvas');
    c.width = w; c.height = h;
    const x = c.getContext('2d');
    x.fillStyle = back; x.fillRect(0, 0, w, h);
    x.fillStyle = stripe; x.fillRect(0, Math.round(h * 0.62), w, Math.round(h * 0.38));
    return c;
  };
  window.asFile = (canvas, name) => new Promise((res) => canvas.toBlob(
    (b) => res(new File([b], name, { type: 'image/png' })), 'image/png'));
  window.putFiles = async (id, files) => {
    const dt = new DataTransfer();
    for (const f of files) dt.items.add(f);
    const input = document.getElementById(id);
    input.files = dt.files;
    input.dispatchEvent(new Event('change', { bubbles: true }));
  };
  window.cardPixels = () => {
    const c = document.createElement('canvas');
    drawFront(c.getContext('2d'), state.templates, printPxPerMm(), cardData());
    return c.getContext('2d').getImageData(0, 0, c.width, c.height).data;
  };
  window.cornerColour = () => {
    const c = document.createElement('canvas');
    drawFront(c.getContext('2d'), state.templates, printPxPerMm(), null);
    const d = c.getContext('2d').getImageData(3, 3, 1, 1).data;
    return [d[0], d[1], d[2]].join(',');
  };
});

/* ---- 1. looking at the map changes nothing ----------------------- */

const pristine = await page.evaluate(() => cardPixels().join(','));
await page.evaluate(() => startAdjusting());
check(await page.isVisible('#tpl-overlay'), 'the map is drawn over the card');
check(await page.evaluate(() => !TemplateStore.isCustom), 'and opening it does not count as changing it');
await page.evaluate(() => { stopAdjusting(); render(); });
check(await page.evaluate(() => cardPixels().join(',')) === pristine, 'closing it again leaves the card byte for byte as it was');

/* ---- 2. the built-in cut-out survives being moved ----------------- */

await page.evaluate(() => startAdjusting());
const cutOut = await page.evaluate(() => {
  // the photo window is stepped, to clear the gold line. Move it and the
  // step must move with it — a rectangle here would be a silent downgrade.
  TemplateStore.setBox('photo', { ...TemplateStore.box('photo'), x: TemplateStore.box('photo').x + 2 });
  const m = geo().masks.photo;
  const px = m.getContext('2d').getImageData(0, 0, m.width, m.height).data;
  const rowWidth = (y) => {
    let first = -1, last = -1;
    for (let x = 0; x < m.width; x++) { if (px[(y * m.width + x) * 4 + 3] > 128) { if (first < 0) first = x; last = x; } }
    return first < 0 ? 0 : last - first + 1;
  };
  // the step is at y = 17.624 mm: above it the window is 2.5 mm narrower
  const mm = (v) => Math.round(v / CONFIG.card.heightMm * m.height);
  const above = rowWidth(mm(14));
  const below = rowWidth(mm(24));
  return { above, below, step: below - above, want: Math.round(2.5 / CONFIG.card.widthMm * m.width) };
});
check(Math.abs(cutOut.step - cutOut.want) <= 2 && cutOut.above > 300,
  `the cut-out keeps its step after the drag (${cutOut.above}px wide above it, ${cutOut.below}px below — a ${cutOut.step}px step, expected ${cutOut.want})`);
check(await page.evaluate(() => !!geo().masks.front), 'and the artwork still sits in front of the photo');
await page.evaluate(() => { TemplateStore.resetBoxes(); stopAdjusting(); render(); });
check(await page.evaluate(() => !TemplateStore.isCustom), 'putting the boxes back drops the override entirely');
check(await page.evaluate(() => cardPixels().join(',')) === pristine, 'and the card is the built-in one again');

/* ---- 3. every way out of the map leaves the app usable ----------- */

// The Done button lives in the Card design drawer, and the form stands
// down while the boxes are being dragged. So every route that closes
// that drawer has to end the editing too, or the boxes stay on the card
// with no form, no Done and nothing to do but reload.
const escapes = {
  'the drawer\u2019s \u00d7': () => page.click('#drawer-close'),
  'Escape': () => page.keyboard.press('Escape'),
  'opening another tool': () => page.click('#rail-printer'),
};
for (const [how, leave] of Object.entries(escapes)) {
  await page.evaluate(() => startAdjusting());
  await page.waitForTimeout(150);
  await leave();
  await page.waitForTimeout(250);
  const after = await page.evaluate(() => ({
    editing: TemplateEdit.open,
    boxes: !document.getElementById('tpl-overlay').hidden,
    form: getComputedStyle(document.querySelector('.panel')).display !== 'none',
  }));
  check(!after.editing && !after.boxes && after.form,
    `leaving by ${how}: editing ${after.editing ? 'stuck on' : 'ended'}, `
    + `boxes ${after.boxes ? 'STILL ON THE CARD' : 'gone'}, form ${after.form ? 'back' : 'MISSING'}`);
  await page.evaluate(() => { state.tool = null; renderTools(); render(); });
}

/* ---- 4. the wrong shape is refused ------------------------------- */

await page.evaluate(async () => {
  const square = await asFile(makeArtwork(600, 600, '#102030', '#c0a000'), 'square.png');
  const ok = await asFile(makeArtwork(638, 1011, '#102030', '#c0a000'), 'back.png');
  await putFiles('f-tpl-front', [square]);
  await putFiles('f-tpl-back', [ok]);
  await useUploadedDesign();
});
const refusal = await page.textContent('#design-msg');
check(/600 × 600/.test(refusal) && /not the shape of an ID card/.test(refusal), `square artwork refused: "${refusal.slice(0, 60)}…"`);
check(await page.evaluate(() => !TemplateStore.isCustom), 'and the built-in design is still in use');

/* ---- 5. the right shape reaches the card ------------------------- */

const beforeColour = await page.evaluate(() => cornerColour());
await page.evaluate(async () => {
  const front = await asFile(makeArtwork(638, 1011, '#0b3d2e', '#f0e6c8'), 'Spring 2027.png');
  const back = await asFile(makeArtwork(638, 1011, '#0b3d2e', '#f0e6c8'), 'back.png');
  await putFiles('f-tpl-front', [front]);
  await putFiles('f-tpl-back', [back]);
  await useUploadedDesign();
});
const afterColour = await page.evaluate(() => cornerColour());
check(await page.evaluate(() => TemplateStore.isCustom), 'the uploaded design is in use');
check(await page.evaluate(() => TemplateStore.name) === 'Spring 2027', 'and is named after the file');
check(beforeColour !== afterColour && afterColour === '11,61,46', `the printed card is the new artwork (${beforeColour} → ${afterColour})`);
check(await page.isVisible('#tpl-overlay'), 'the placeholders are offered straight away');

/* ---- 6. dragging moves what is drawn ----------------------------- */

const box = await page.$('.tpl-ph[data-key="name"]');
const r = await box.boundingBox();
const card = await (await page.$('#card-box')).boundingBox();
const mmPerPx = 54 / card.width;
const before = await page.evaluate(() => TemplateStore.box('name'));
const dragBy = async (handle, px, py) => {
  const b = await handle.boundingBox();
  await page.mouse.move(b.x + b.width / 2, b.y + b.height / 2);
  await page.mouse.down();
  await page.mouse.move(b.x + b.width / 2 + px, b.y + b.height / 2 + py, { steps: 6 });
  await page.mouse.up();
};
await dragBy(box, -20, 10);
const after = await page.evaluate(() => TemplateStore.box('name'));
const dx = after.x - before.x, dy = after.y - before.y;
check(Math.abs(dx + 20 * mmPerPx) < 0.3 && Math.abs(dy - 10 * mmPerPx) < 0.3,
  `dragging the name moved it ${dx.toFixed(2)} × ${dy.toFixed(2)} mm (asked for ${(-20 * mmPerPx).toFixed(2)} × ${(10 * mmPerPx).toFixed(2)})`);
check(await page.evaluate(() => geo().text.name.box.x) === after.x, 'and the renderer is drawing it there');

// a placeholder dragged off the card is no use to anyone
await dragBy(box, 600, 600);
const shoved = await page.evaluate(() => TemplateStore.box('name'));
check(Math.abs(shoved.x + shoved.w - 54) < 0.01 && Math.abs(shoved.y + shoved.h - 85.6) < 0.01,
  `dragged off the card it stops at the edge (${shoved.x.toFixed(1)}, ${shoved.y.toFixed(1)})`);

const back0 = await page.evaluate(() => TemplateStore.box('name'));
await page.evaluate(() => { TemplateStore.resetBoxes(); TemplateEdit.refresh(); });
const back1 = await page.evaluate(() => TemplateStore.box('name'));
const configX = await page.evaluate(() => CONFIG.text.name.box.x);
check(back0.x !== back1.x && Math.abs(back1.x - configX) < 0.001, 'putting the boxes back undoes the drag');

/* ---- 7. it survives a restart, and can be undone ------------------ */

await page.evaluate(() => { TemplateEdit.hide(); TemplateStore.save(); });
await page.reload();
await page.waitForSelector('#card-box:not([hidden])', { timeout: 20000 });
await page.waitForFunction(() => TemplateStore.isCustom || !localStorage.getItem('apg-id-template'));
await page.evaluate(() => {
  window.cornerColour = () => {
    const c = document.createElement('canvas');
    drawFront(c.getContext('2d'), state.templates, printPxPerMm(), null);
    const d = c.getContext('2d').getImageData(3, 3, 1, 1).data;
    return [d[0], d[1], d[2]].join(',');
  };
});
check(await page.evaluate(() => TemplateStore.isCustom), 'the design is still there after a restart');
check(await page.evaluate(() => cornerColour()) === '11,61,46', 'and still what the card is drawn from');

await page.evaluate(() => { TemplateStore.clear(); state.templates = builtInTemplates; render(); });
check(await page.evaluate(() => cornerColour()) === beforeColour, 'the built-in design comes back unchanged');

/* ---- done -------------------------------------------------------- */

await browser.close();
if (errors.length) { console.error('\npage errors:'); errors.forEach((e) => console.error('  ' + e)); }
if (fail.length || errors.length) {
  console.error(`\nFAIL — ${fail.length} check(s) failed${errors.length ? `, ${errors.length} page error(s)` : ''}`);
  process.exit(1);
}
console.log('\nPASS — the map and the artwork can both be changed, kept and undone');
