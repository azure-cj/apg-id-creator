#!/usr/bin/env node
/* ------------------------------------------------------------------
   Template-map test
   ------------------------------------------------------------------
   A card can be described by a map: a PNG with every zone painted a
   flat colour. This checks the machinery that reads one.

     npm run test:map

   It builds a map from the geometry currently in config.js, reads it
   back, and checks three things:

     1. every zone is found, and lands where config.js put it, within
        the precision a pixel grid allows;
     2. a card rendered through the map matches the card rendered from
        config.js everywhere except sub-pixel text placement;
     3. the contrast check refuses a template whose text would be
        invisible on it.
   ------------------------------------------------------------------ */
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import pw from 'playwright';

const here = dirname(fileURLToPath(import.meta.url));
const app = pathToFileURL(join(here, '..', 'Alpha Premier ID Maker.html')).href;
const chromium = pw.chromium ?? pw.default.chromium;

// one pixel of a 638-wide map is 0.085 mm; a box edge can be half of
// that out in each direction
const MM_TOLERANCE = 0.09;
const TEXT_SHIFT_BUDGET = 0.02;   // fraction of the card that may differ

const browser = await chromium.launch();
const page = await browser.newPage();
const errors = [];
page.on('pageerror', (e) => errors.push(e.message));
await page.goto(app);
await page.waitForSelector('#card-box:not([hidden])', { timeout: 20000 });
await page.evaluate(() => loadExample());
await page.waitForFunction(() => state.photo && state.signature);
await page.evaluate(() => { state.year = 2026; });

const r = await page.evaluate(async () => {
  const before = renderSideCanvas(state.templates, 'front', cardData(), false);
  const map = readTemplateMap(await createImageBitmap(mapFromConfig()));
  if (!map) return { fatal: 'readTemplateMap returned null for a map built from config' };

  const d = (a, b) => Math.abs(a - b);
  const drift = {};
  const boxes = {
    name: CONFIG.text.name.box, title: CONFIG.text.title.box,
    employeeNo: CONFIG.text.employeeNo.box, signature: CONFIG.signature.box,
  };
  for (const k of Object.keys(boxes)) {
    if (!map.boxes[k]) return { fatal: `zone "${k}" not found in the map` };
    drift[k] = Math.max(d(map.boxes[k].x, boxes[k].x), d(map.boxes[k].y, boxes[k].y),
                        d(map.boxes[k].w, boxes[k].w), d(map.boxes[k].h, boxes[k].h));
  }
  if (!map.boxes.qr) return { fatal: 'the QR zone was not found' };
  drift.qr = Math.max(d(map.boxes.qr.x, CONFIG.qr.square.x), d(map.boxes.qr.y, CONFIG.qr.square.y));
  if (!map.head) return { fatal: 'the head marker was not found' };
  drift.head = Math.max(d(map.head.top, CONFIG.photo.headTop),
                        d(map.head.centerX, CONFIG.photo.headCenterX),
                        d(map.head.width, CONFIG.photo.headWidth));
  if (!map.masks.photo || !map.masks.front) return { fatal: 'a region mask was missing' };

  useTemplateMap(map);
  const after = renderSideCanvas(state.templates, 'front', cardData(), false);
  const warnings = checkTemplateContrast(map, state.templates.front);
  // a template whose name box sits on white must be refused
  const white = document.createElement('canvas');
  white.width = state.templates.front.width; white.height = state.templates.front.height;
  const wc = white.getContext('2d'); wc.fillStyle = '#fff'; wc.fillRect(0, 0, white.width, white.height);
  const refused = checkTemplateContrast(map, white);
  useTemplateMap(null);

  const A = before.getContext('2d').getImageData(0, 0, before.width, before.height).data;
  const B = after.getContext('2d').getImageData(0, 0, after.width, after.height).data;
  let differing = 0;
  for (let i = 0; i < A.length; i += 4) {
    if (Math.max(Math.abs(A[i] - B[i]), Math.abs(A[i + 1] - B[i + 1]), Math.abs(A[i + 2] - B[i + 2])) > 16) differing++;
  }
  return { drift, differing, total: A.length / 4, warningsOnRealArt: warnings, refusedOnWhite: refused.length };
});
await browser.close();

const fail = (m) => { console.error('FAIL — ' + m); process.exit(1); };
if (errors.length) fail('the page threw: ' + errors.join(' | '));
if (r.fatal) fail(r.fatal);

console.log('zone drift (mm):', JSON.stringify(r.drift));
const worst = Math.max(...Object.values(r.drift));
if (worst > MM_TOLERANCE) fail(`a zone moved ${worst.toFixed(3)} mm, over the ${MM_TOLERANCE} mm a pixel grid costs`);

const share = r.differing / r.total;
console.log(`pixels differing: ${r.differing} of ${r.total} (${(share * 100).toFixed(2)} %) — sub-pixel text placement`);
if (share > TEXT_SHIFT_BUDGET) fail(`${(share * 100).toFixed(2)} % of the card changed; that is more than text nudging`);

console.log('contrast on the real artwork:', r.warningsOnRealArt.length ? r.warningsOnRealArt : 'no complaints');
if (r.warningsOnRealArt.length) fail('the card in use failed its own contrast check');
if (r.refusedOnWhite < 3) fail('a template with white artwork behind the text was not refused');
console.log(`contrast check refused ${r.refusedOnWhite} boxes on white artwork, as it should`);

console.log('PASS — a map built from config.js reproduces the card');
