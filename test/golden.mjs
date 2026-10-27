#!/usr/bin/env node
/* ------------------------------------------------------------------
   Golden-render test
   ------------------------------------------------------------------
   Opens the built app in headless Chromium, loads "Show an example",
   renders the FRONT at 300 DPI exactly as the PDF would, and compares
   it pixel by pixel with test/golden-front.png.

     npm test                 compare (fails if anything moved)
     npm test -- --update     accept the current render as the new golden

   Also prints the text layout (lines + point sizes) and the photo
   placement so a change is explained, not just detected.
   Requires: npm install (installs Playwright + Chromium).
   ------------------------------------------------------------------ */
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, '..');
const app = pathToFileURL(join(root, 'Alpha Premier ID Maker.html')).href;
const goldenPath = join(here, 'golden-front.png');
const diffPath = join(here, 'diff-front.png');
const update = process.argv.includes('--update');
const TOLERANCE = 8;        // per-channel difference that counts as "changed" (anti-aliasing noise)
const MAX_CHANGED = 0.001;  // fraction of pixels allowed to differ

const pw = await import(process.env.PLAYWRIGHT_MODULE || 'playwright');
const chromium = pw.chromium ?? pw.default.chromium;
const browser = await chromium.launch();
const page = await browser.newPage();
const errors = [];
page.on('pageerror', (e) => errors.push(e.message));
await page.goto(app);
await page.waitForSelector('#card-box:not([hidden])', { timeout: 20000 });
await page.evaluate(() => loadExample());      // no button for it: see src/app.js
await page.waitForFunction(() => state.photo && state.signature);
await page.waitForTimeout(300);

// pin the year so the golden does not expire on New Year's Day
await page.evaluate(() => { state.year = 2026; render(); });
const info = await page.evaluate(() => ({
  layout: layoutText(cardData()),
  placement: state.photo.check.placement,
  png: renderSideCanvas(state.templates, 'front', cardData(), false).toDataURL('image/png'),
}));
console.log('layout:', JSON.stringify(info.layout));
console.log('placement (mm):', JSON.stringify(Object.fromEntries(Object.entries(info.placement).map(([k, v]) => [k, +v.toFixed(2)]))));
const current = Buffer.from(info.png.split(',')[1], 'base64');

if (update || !existsSync(goldenPath)) {
  writeFileSync(goldenPath, current);
  console.log(`${update ? 'updated' : 'created'} ${goldenPath}`);
  await browser.close();
  process.exit(0);
}

// diff inside the browser so no image library is needed
const golden = 'data:image/png;base64,' + readFileSync(goldenPath).toString('base64');
const result = await page.evaluate(async ({ a, b, tol }) => {
  const load = (src) => new Promise((res, rej) => { const i = new Image(); i.onload = () => res(i); i.onerror = rej; i.src = src; });
  const [ia, ib] = await Promise.all([load(a), load(b)]);
  if (ia.width !== ib.width || ia.height !== ib.height) return { sizeMismatch: [ia.width, ia.height, ib.width, ib.height] };
  const px = (img) => { const c = document.createElement('canvas'); c.width = img.width; c.height = img.height; const x = c.getContext('2d'); x.drawImage(img, 0, 0); return x.getImageData(0, 0, c.width, c.height); };
  const A = px(ia), B = px(ib), D = new ImageData(A.width, A.height);
  let changed = 0;
  for (let i = 0; i < A.data.length; i += 4) {
    const d = Math.max(Math.abs(A.data[i] - B.data[i]), Math.abs(A.data[i + 1] - B.data[i + 1]), Math.abs(A.data[i + 2] - B.data[i + 2]));
    const hot = d > tol;
    if (hot) changed++;
    D.data[i] = hot ? 255 : A.data[i] >> 2; D.data[i + 1] = hot ? 0 : A.data[i + 1] >> 2; D.data[i + 2] = hot ? 255 : A.data[i + 2] >> 2; D.data[i + 3] = 255;
  }
  const c = document.createElement('canvas'); c.width = A.width; c.height = A.height; c.getContext('2d').putImageData(D, 0, 0);
  return { changed, total: A.width * A.height, diff: c.toDataURL('image/png') };
}, { a: golden, b: info.png, tol: TOLERANCE });
await browser.close();

if (errors.length) { console.error('page errors:', errors); process.exit(1); }
if (result.sizeMismatch) { console.error('size mismatch (golden vs current):', result.sizeMismatch); process.exit(1); }
const frac = result.changed / result.total;
console.log(`changed pixels: ${result.changed} of ${result.total} (${(frac * 100).toFixed(3)} %)`);
if (frac > MAX_CHANGED) {
  writeFileSync(diffPath, Buffer.from(result.diff.split(',')[1], 'base64'));
  console.error(`FAIL — the front render moved. Changed pixels are magenta in ${diffPath}. If the change is intended, run: npm test -- --update`);
  process.exit(1);
}
console.log('PASS — front render matches golden-front.png');
