/* ------------------------------------------------------------------
   Photo pipeline
   ------------------------------------------------------------------
   processPhotoFile(file) runs these steps and never throws:

     1. validate      PNG / JPG / WebP, under CONFIG.photo.maxFileMb
     2. decode        <img>, then draw to a canvas no taller than 2400 px
     3. classify      sample the border: transparent / green / blue /
                      checkerboard / other
     4. chroma key    green or blue -> alpha, with de-spill on the edges,
                      then a half-pixel alpha erosion to trim the halo
     5. find the head on a copy no taller than 800 px (fast), using the
                      alpha silhouette: top of head, widest row (head
                      width), narrowest row below it (neck), shoulders.
                      Reject if the key ate clothing the same colour as the
                      backdrop; warn if the face is blurred
     6. place         scale so the head is CONFIG.photo.headWidth mm wide,
                      head top at headTop, centred at headCenterX; scale
                      up if the body would not reach the dark band

   Result:
     { image, url, fileName, background,
       check: { ok, head?, placement?, warning?, problem? } }
   or { error } when the file could not be read at all.
   ------------------------------------------------------------------ */

const MSG_HAS_BACKGROUND = 'This photo still has its background. Use the photo prompt below to get a version with a solid green background, or upload a cut-out PNG.';
const MSG_CHECKERBOARD = 'This photo has a grey checkerboard painted in, not a real transparent background. Ask for a solid green background instead (see the photo prompt below).';

const ALPHA_SOLID = 128;        // alpha at/above this counts as "person"
// Clothing the same colour as the backdrop is keyed out with it. Two signs:
// holes inside the body outline, or the body collapsing below the shoulders.
const MAX_HOLE_FRACTION = 0.06;
const MIN_TORSO_RATIO = 0.6;    // width just below the shoulders, vs the shoulders
const MIN_ROWS_TO_JUDGE_BODY = 8;
// Sharpness: variance of the Laplacian over the face, measured on the
// 800 px analysis copy. Below this the face prints visibly soft.
const BLUR_VARIANCE = 55;
const MIN_HEAD_PX = 40;         // narrower than this: the face cannot be seen
const ANALYSIS_MAX_H = 800;     // head detection works on a copy this tall
const WORKING_MAX_H = 2400;     // the keyed image kept for the card
const ACCEPTED = /\.(png|jpe?g|webp)$/i;
const HEIC = /\.(heic|heif)$/i;

/* ---- 3. classify the background ---------------------------------- */

function classifyBackground(img) {
  const { width: w, height: h, data } = img;
  const idx = [];
  const step = Math.max(1, Math.floor((w + h) / 400));
  for (let x = 0; x < w; x += step) idx.push(x, (h - 1) * w + x, w + x);   // top two rows + bottom
  for (let y = 0; y < h; y += step) idx.push(y * w, y * w + w - 1);        // left + right columns

  let transparent = 0, green = 0, blue = 0, grey = 0;
  const greyLevels = new Map();
  for (const i of idx) {
    const o = i * 4;
    const r = data[o], g = data[o + 1], b = data[o + 2], a = data[o + 3];
    if (a < 20) { transparent++; continue; }
    if (g > 120 && g - Math.max(r, b) > 60) green++;
    else if (b > 120 && b - Math.max(r, g) > 60) blue++;
    if (Math.max(r, g, b) - Math.min(r, g, b) < 16 && r > 170) {
      grey++;
      const level = Math.round(r / 12);
      greyLevels.set(level, (greyLevels.get(level) ?? 0) + 1);
    }
  }
  const n = idx.length;
  if (transparent / n > 0.4) return 'transparent';
  if (green / n > 0.5) return 'green';
  if (blue / n > 0.5) return 'blue';
  if (grey / n > 0.6) {
    // two distinct light greys along the border = a painted checkerboard
    const counts = [...greyLevels.values()].sort((a, b) => b - a);
    if (counts.length >= 2 && counts[1] / grey > 0.2) return 'checkerboard';
  }
  return 'other';
}

/* ---- 4. chroma key ----------------------------------------------- */

// In place. dominance = key channel minus the larger of the other two.
//   <= 0      untouched
//   0..30     colour only de-spilled (hair edges, reflections)
//   30..110   alpha fades out linearly
//   >= 110    fully transparent
function chromaKey(img, key) {
  const d = img.data;
  for (let i = 0; i < d.length; i += 4) {
    const r = d[i], g = d[i + 1], b = d[i + 2];
    const dom = key === 'green' ? g - Math.max(r, b) : b - Math.max(r, g);
    if (dom <= 0) continue;
    if (key === 'green') d[i + 1] = Math.max(r, b); else d[i + 2] = Math.max(r, g);   // de-spill
    if (dom <= 30) continue;
    const keep = dom >= 110 ? 0 : 1 - (dom - 30) / 80;
    d[i + 3] = Math.round(d[i + 3] * keep);
  }
}

// After keying, the outermost semi-transparent pixels can still carry a
// faint tinted halo. Pull each edge pixel's alpha halfway towards the
// minimum alpha in its 3x3 neighbourhood: a half-pixel erosion that trims
// the fringe without eating into hair.
function trimEdgeHalo(img) {
  const { width: w, height: h, data } = img;
  const src = new Uint8ClampedArray(data.length);
  for (let i = 3; i < data.length; i += 4) src[i] = data[i];
  for (let y = 1; y < h - 1; y++) {
    for (let x = 1; x < w - 1; x++) {
      const i = (y * w + x) * 4 + 3;
      const a = src[i];
      if (a === 0 || a === 255) continue;
      let m = a;
      for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) {
        const n = src[((y + dy) * w + (x + dx)) * 4 + 3];
        if (n < m) m = n;
      }
      data[i] = Math.round((a + m) / 2);
    }
  }
}

/* ---- 5. find the head, 6. place it ------------------------------- */

/**
 * Variance of the Laplacian over a box — the standard cheap sharpness
 * measure. High on a crisp face, near zero on a blurred one.
 */
function sharpnessOf(img, x0, y0, x1, y1) {
  const { width: w, height: h, data } = img;
  x0 = Math.max(1, Math.round(x0)); y0 = Math.max(1, Math.round(y0));
  x1 = Math.min(w - 2, Math.round(x1)); y1 = Math.min(h - 2, Math.round(y1));
  if (x1 - x0 < 8 || y1 - y0 < 8) return Infinity;      // too small to judge
  const lum = (i) => (data[i * 4] * 299 + data[i * 4 + 1] * 587 + data[i * 4 + 2] * 114) / 1000;
  let sum = 0, sumSq = 0, n = 0;
  for (let y = y0; y <= y1; y++) {
    for (let x = x0; x <= x1; x++) {
      const i = y * w + x;
      if (data[i * 4 + 3] < ALPHA_SOLID) continue;      // only the person
      const l = 4 * lum(i) - lum(i - 1) - lum(i + 1) - lum(i - w) - lum(i + w);
      sum += l; sumSq += l * l; n++;
    }
  }
  if (n < 64) return Infinity;
  const mean = sum / n;
  return sumSq / n - mean * mean;
}

function findHeadAndPlace(analysis, fullWidth, keyed) {
  const { width: w, height: h, data } = analysis;
  const scale = fullWidth / w;              // analysis px -> full-size px
  const P = CONFIG.photo;

  // must really be a cut-out: enough transparent pixels, and a mostly clear top row
  let transparent = 0;
  for (let i = 3; i < data.length; i += 4) if (data[i] < 20) transparent++;
  let topClear = 0;
  for (let x = 0; x < w; x++) if (data[x * 4 + 3] < 20) topClear++;
  if (transparent / (w * h) < 0.08 || topClear / w < 0.5) return { ok: false, problem: MSG_HAS_BACKGROUND };

  // per-row silhouette: first/last solid column and count of solid pixels
  const first = new Int32Array(h).fill(-1), last = new Int32Array(h).fill(-1), count = new Int32Array(h);
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    if (data[(y * w + x) * 4 + 3] >= ALPHA_SOLID) { if (first[y] < 0) first[y] = x; last[y] = x; count[y]++; }
  }
  const minCount = Math.max(2, Math.round(w * 0.01));
  let top = 0;
  while (top < h && count[top] < minCount) top++;
  if (top >= h) return { ok: false, problem: 'No person found in this photo. Please upload the cut-out PNG.' };

  const rowWidth = (y) => (count[y] < minCount ? 0 : last[y] - first[y] + 1);

  // walk down: head widens to headW, narrows to the neck, then widens fast at the shoulders
  let headW = 0, narrowing = false, neckY = -1, neckW = Infinity, shoulderY = -1;
  for (let y = top; y < h; y++) {
    const rw = rowWidth(y);
    if (!narrowing) {
      if (rw > headW) headW = rw;
      else if (rw < headW * 0.85 && y > top + 3) narrowing = true;
    }
    if (narrowing) {
      if (rw < neckW) { neckW = rw; neckY = y; }
      if (rw > headW * 1.5) { shoulderY = y; break; }
    }
  }
  if (shoulderY < 0 || neckY < 0) {
    return { ok: false, problem: 'Head and shoulders aren’t both visible. Please upload a chest-up cut-out, facing forward.' };
  }

  // head centre = mean centre of the wide rows between the top and the neck
  let sum = 0, n = 0;
  for (let y = top; y <= neckY; y++) if (rowWidth(y) >= headW * 0.8) { sum += (first[y] + last[y]) / 2; n++; }
  const centerX = sum / Math.max(1, n);

  const head = { top: top * scale, width: headW * scale, centerX: centerX * scale, neckY: neckY * scale };
  if (head.width < MIN_HEAD_PX) return { ok: false, head, problem: 'This photo is too small to show the face. Please upload a larger photo.' };
  if (top <= 1) return { ok: false, head, problem: 'The top of the head is cut off. Please upload a photo with space above the head.' };

  // Did the key eat the clothing? Holes inside the outline below the neck,
  // or a body that collapses below the shoulders, both mean yes.
  let span = 0, holes = 0;
  for (let y = neckY; y < h; y++) {
    const rw = rowWidth(y);
    if (!rw) continue;
    span += rw;
    holes += rw - count[y];
  }
  const holeFraction = span ? holes / span : 0;

  // the band directly under the shoulders: a torso has to be there
  const shoulderW = Math.max(rowWidth(shoulderY), headW);
  const bandEnd = Math.min(h - 1, shoulderY + headW);
  const bandWidths = [];
  for (let y = shoulderY; y <= bandEnd; y++) bandWidths.push(rowWidth(y));
  bandWidths.sort((a, b) => a - b);
  const torsoRatio = bandWidths.length >= MIN_ROWS_TO_JUDGE_BODY
    ? bandWidths[Math.floor(bandWidths.length / 2)] / shoulderW
    : 1;

  if (keyed && (holeFraction > MAX_HOLE_FRACTION || torsoRatio < MIN_TORSO_RATIO)) {
    return {
      ok: false, head, holeFraction, torsoRatio,
      problem: 'Clothing is too close to the background colour — redo the photo with a blue background or different top.',
    };
  }

  const sharpness = sharpnessOf(analysis, head.centerX / scale - headW / 2, top, head.centerX / scale + headW / 2, neckY);

  const warnings = [];
  if (head.width < P.minHeadPx) warnings.push('This is a small photo, so the face may print slightly soft. A larger photo prints sharper, but this one can be used.');
  if (sharpness < BLUR_VARIANCE) warnings.push('This photo looks blurred. Check the face closely before printing — a sharper photo is better.');
  for (let y = top; y <= neckY; y++) {
    if (count[y] >= minCount && (first[y] <= 0 || last[y] >= w - 1)) {
      warnings.push('The head touches the side of the photo. Check that nothing important is cut off.');
      break;
    }
  }

  // placement in mm: scale so the head is headWidth mm wide
  const fullW = fullWidth, fullH = h * scale;
  let mmPerPx = P.headWidth / head.width;
  const belowHeadTopPx = fullH - head.top;
  if (P.headTop + belowHeadTopPx * mmPerPx < P.reachY) {
    // the photo is cropped high: enlarge so the body still disappears behind the band
    const needed = (P.reachY - P.headTop) / belowHeadTopPx;
    if (needed / mmPerPx > 1.08) {
      warnings.push('This photo is cropped close, so the head is shown larger than usual. A photo that shows more of the chest gives a more consistent card.');
    }
    mmPerPx = needed;
  }
  return {
    ok: true,
    head,
    sharpness, holeFraction, torsoRatio,            // kept for diagnosis
    placement: {
      x: P.headCenterX - head.centerX * mmPerPx,
      y: P.headTop - head.top * mmPerPx,
      w: fullW * mmPerPx,
      h: fullH * mmPerPx,
    },
    warnings,
  };
}

/* ---- 2. decode + glue -------------------------------------------- */

function decodeImage(url) {
  return new Promise((resolve, reject) => {
    const img = new Image();
    img.onload = () => resolve(img);
    img.onerror = () => reject(new Error('decode'));
    img.src = url;
  });
}

function shrinkImageData(img, maxH) {
  const k = Math.min(1, maxH / img.height);
  if (k === 1) return img;
  const src = document.createElement('canvas');
  src.width = img.width; src.height = img.height;
  src.getContext('2d').putImageData(new ImageData(new Uint8ClampedArray(img.data), img.width, img.height), 0, 0);
  const dst = document.createElement('canvas');
  dst.width = Math.max(1, Math.round(img.width * k)); dst.height = Math.max(1, Math.round(img.height * k));
  const ctx = dst.getContext('2d', { willReadFrequently: true });
  ctx.drawImage(src, 0, 0, dst.width, dst.height);
  const out = ctx.getImageData(0, 0, dst.width, dst.height);
  return { width: out.width, height: out.height, data: out.data };
}

function analysePhoto(imgEl, url, fileName) {
  const k = Math.min(1, WORKING_MAX_H / imgEl.naturalHeight);
  const canvas = document.createElement('canvas');
  canvas.width = Math.max(1, Math.round(imgEl.naturalWidth * k));
  canvas.height = Math.max(1, Math.round(imgEl.naturalHeight * k));
  const ctx = canvas.getContext('2d', { willReadFrequently: true });
  ctx.drawImage(imgEl, 0, 0, canvas.width, canvas.height);
  const pixels = ctx.getImageData(0, 0, canvas.width, canvas.height);

  const background = classifyBackground(pixels);
  let check;
  if (background === 'checkerboard') check = { ok: false, problem: MSG_CHECKERBOARD };
  else if (background === 'other') check = { ok: false, problem: MSG_HAS_BACKGROUND };
  else {
    const keyed = background === 'green' || background === 'blue';
    if (keyed) { chromaKey(pixels, background); trimEdgeHalo(pixels); }
    check = findHeadAndPlace(shrinkImageData(pixels, ANALYSIS_MAX_H), pixels.width, keyed);
  }

  if (background === 'green' || background === 'blue') {
    ctx.putImageData(pixels, 0, 0);          // keep the keyed version
    return { image: canvas, url: canvas.toDataURL('image/png'), fileName, check, background };
  }
  return { image: k === 1 ? imgEl : canvas, url, fileName, check, background };
}

/** Entry point for an uploaded File. */
async function processPhotoFile(file) {
  if (HEIC.test(file.name) || /^image\/hei[cf]$/.test(file.type)) {
    return { error: 'This is an iPhone HEIC photo, which browsers can’t open. On the iPhone, share it as JPEG (Settings → Camera → Formats → Most Compatible, or share via Mail/Messages), or open it in Windows Photos and save as JPG, then upload that.' };
  }
  if (!ACCEPTED.test(file.name) && !/^image\/(png|jpeg|webp)$/.test(file.type)) {
    return { error: 'Please upload a PNG, JPG or WebP photo.' };
  }
  if (file.size > CONFIG.photo.maxFileMb * 1024 * 1024) {
    return { error: `This file is over ${CONFIG.photo.maxFileMb} MB. Please upload a smaller photo.` };
  }
  const url = URL.createObjectURL(file);
  let img;
  try { img = await decodeImage(url); }
  catch { URL.revokeObjectURL(url); return { error: 'This file couldn’t be opened as an image. Please try saving it again and re-upload.' }; }
  return analysePhoto(img, url, file.name);
}

/** Same pipeline for a bundled example (data URL). */
async function processPhotoUrl(url, fileName = 'example-cut-out.png') {
  return analysePhoto(await decodeImage(url), url, fileName);
}
