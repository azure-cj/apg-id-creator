/* ------------------------------------------------------------------
   Signature: pad widget + drawing onto the card
   ------------------------------------------------------------------
   A signature is { strokes: [ [ {x,y}, ... ], ... ] } in the pad's own
   CSS-pixel space. Nothing about the card is baked in, so the same
   strokes can be re-fitted into the box on the card at any resolution.
   ------------------------------------------------------------------ */

// A signature is either drawn — { strokes: [[{x,y}…]…] } — or scanned,
// { kind: 'image', mask } where mask is a trimmed canvas holding the ink
// as alpha. Both are fitted into the same box and printed in white.
const hasInk = (sig) => !!(sig && (sig.kind === 'image' ? sig.mask : sig.strokes?.some((s) => s.length > 0)));

// Drop near-duplicate points (< 0.5 px apart) and run two passes of a
// 1-2-1 smoothing kernel so hand jitter does not print as wobble.
function smoothStroke(points) {
  const kept = [];
  for (const p of points) {
    const last = kept[kept.length - 1];
    if (!last || Math.hypot(p.x - last.x, p.y - last.y) >= 0.5) kept.push({ x: p.x, y: p.y });
  }
  if (kept.length < 3) return kept;
  let pts = kept;
  for (let pass = 0; pass < 2; pass++) {
    pts = pts.map((p, i) => {
      if (i === 0 || i === pts.length - 1) return p;
      const a = pts[i - 1], b = pts[i + 1];
      return { x: (a.x + 2 * p.x + b.x) / 4, y: (a.y + 2 * p.y + b.y) / 4 };
    });
  }
  return pts;
}

// Median distance between consecutive raw points: a feel for how fast
// this person signs, used to normalise pen speed -> stroke width.
function medianStep(sig) {
  const d = [];
  for (const s of sig.strokes) for (let i = 1; i < s.length; i++) d.push(Math.hypot(s[i].x - s[i - 1].x, s[i].y - s[i - 1].y));
  if (!d.length) return 1;
  d.sort((a, b) => a - b);
  return Math.max(0.5, d[Math.floor(d.length / 2)]);
}

// Per-point width: slow = thick (maxW), fast = thin (minW), low-pass filtered.
function strokeWidths(points, median, minW, maxW) {
  const out = [];
  let w = maxW * 0.85;
  for (let i = 0; i < points.length; i++) {
    const a = points[Math.max(0, i - 1)], b = points[Math.min(points.length - 1, i + 1)];
    const speed = Math.hypot(b.x - a.x, b.y - a.y) / (i === 0 || i === points.length - 1 ? 1 : 2);
    const t = Math.min(1, speed / (median * 2.2));
    w = w * 0.75 + (maxW - (maxW - minW) * t) * 0.25;
    out.push(w);
  }
  return out;
}

/**
 * Draw the strokes with Catmull-Rom style curves.
 *   toX/toY  map pad coordinates -> canvas pixels
 *   scale    multiplies the widths (which are given in pad units)
 */
function strokeSignature(ctx, sig, { toX, toY, scale, minW, maxW, color }) {
  ctx.strokeStyle = color;
  ctx.fillStyle = color;
  ctx.lineCap = 'round';
  ctx.lineJoin = 'round';
  const median = medianStep(sig);
  for (const raw of sig.strokes) {
    const p = smoothStroke(raw);
    if (!p.length) continue;
    if (p.length === 1) {                 // a dot
      ctx.beginPath();
      ctx.arc(toX(p[0].x), toY(p[0].y), (maxW * scale) / 2, 0, Math.PI * 2);
      ctx.fill();
      continue;
    }
    const widths = strokeWidths(p, median, minW, maxW);
    for (let i = 0; i < p.length - 1; i++) {
      const p0 = p[Math.max(0, i - 1)], p1 = p[i], p2 = p[i + 1], p3 = p[Math.min(p.length - 1, i + 2)];
      ctx.beginPath();
      ctx.moveTo(toX(p1.x), toY(p1.y));
      ctx.bezierCurveTo(
        toX(p1.x + (p2.x - p0.x) / 6), toY(p1.y + (p2.y - p0.y) / 6),
        toX(p2.x - (p3.x - p1.x) / 6), toY(p2.y - (p3.y - p1.y) / 6),
        toX(p2.x), toY(p2.y));
      ctx.lineWidth = ((widths[i] + widths[i + 1]) / 2) * scale;
      ctx.stroke();
    }
  }
}

/* ---- scanned signatures ------------------------------------------ */

const SIG_IMAGE_MAX_PX = 1600;

// Otsu's method: the luminance threshold that best separates ink from paper.
function otsuThreshold(hist, total) {
  let sum = 0;
  for (let i = 0; i < 256; i++) sum += i * hist[i];
  let sumB = 0, wB = 0, best = 0, bestVar = -1;
  for (let t = 0; t < 256; t++) {
    wB += hist[t];
    if (!wB) continue;
    const wF = total - wB;
    if (!wF) break;
    sumB += t * hist[t];
    const mB = sumB / wB, mF = (sum - sumB) / wF;
    const between = wB * wF * (mB - mF) ** 2;
    if (between > bestVar) { bestVar = between; best = t; }
  }
  return best;
}

/**
 * Turn a photo or scan of a signature on paper into an ink mask.
 * Returns { kind:'image', mask, fileName } or { error }.
 */
async function signatureFromImage(file) {
  if (/\.(heic|heif)$/i.test(file.name) || /^image\/hei[cf]$/.test(file.type)) {
    return { error: 'This is an iPhone HEIC photo, which browsers can’t open. Save or share it as JPEG and try again.' };
  }
  if (!/\.(png|jpe?g|webp)$/i.test(file.name) && !/^image\/(png|jpeg|webp)$/.test(file.type)) {
    return { error: 'Please upload a PNG, JPG or WebP picture of the signature.' };
  }
  const url = URL.createObjectURL(file);
  let img;
  try { img = await new Promise((res, rej) => { const i = new Image(); i.onload = () => res(i); i.onerror = rej; i.src = url; }); }
  catch { URL.revokeObjectURL(url); return { error: 'This file couldn’t be opened as an image.' }; }

  const k = Math.min(1, SIG_IMAGE_MAX_PX / Math.max(img.naturalWidth, img.naturalHeight));
  const w = Math.max(1, Math.round(img.naturalWidth * k)), h = Math.max(1, Math.round(img.naturalHeight * k));
  const src = document.createElement('canvas');
  src.width = w; src.height = h;
  const sctx = src.getContext('2d', { willReadFrequently: true });
  sctx.fillStyle = '#fff'; sctx.fillRect(0, 0, w, h);      // flatten any transparency onto paper white
  sctx.drawImage(img, 0, 0, w, h);
  URL.revokeObjectURL(url);
  const px = sctx.getImageData(0, 0, w, h);

  const lum = new Uint8ClampedArray(w * h);
  const hist = new Uint32Array(256);
  for (let i = 0, j = 0; i < px.data.length; i += 4, j++) {
    const v = (px.data[i] * 299 + px.data[i + 1] * 587 + px.data[i + 2] * 114) / 1000 | 0;
    lum[j] = v; hist[v]++;
  }
  const t = otsuThreshold(hist, w * h);
  const soft = Math.max(8, t * 0.35);          // ink fades out over this range

  let minX = w, minY = h, maxX = -1, maxY = -1, solid = 0;
  const alpha = new Uint8ClampedArray(w * h);
  for (let y = 0, j = 0; y < h; y++) {
    for (let x = 0; x < w; x++, j++) {
      const a = Math.max(0, Math.min(1, (t + soft - lum[j]) / soft));
      if (a <= 0.06) continue;
      alpha[j] = Math.round(a * 255);
      if (a > 0.5) solid++;
      if (x < minX) minX = x; if (x > maxX) maxX = x;
      if (y < minY) minY = y; if (y > maxY) maxY = y;
    }
  }
  const coverage = solid / (w * h);
  if (maxX < 0 || coverage < 0.0004) return { error: 'No signature found in this picture. Sign in dark ink on white paper and photograph it straight on.' };
  if (coverage > 0.5) return { error: 'This picture is too dark overall. Crop it to the signature on white paper, in even light, and try again.' };
  if (maxX - minX < 20 || maxY - minY < 8) return { error: 'The signature is too small in this picture. Crop closer to it and try again.' };

  const mw = maxX - minX + 1, mh = maxY - minY + 1;
  const mask = document.createElement('canvas');
  mask.width = mw; mask.height = mh;
  const out = new ImageData(mw, mh);
  for (let y = 0; y < mh; y++) {
    for (let x = 0; x < mw; x++) {
      out.data[(y * mw + x) * 4 + 3] = alpha[(y + minY) * w + (x + minX)];
    }
  }
  mask.getContext('2d').putImageData(out, 0, 0);
  return { kind: 'image', mask, fileName: file.name };
}

// White version of the mask, cached on the signature object.
function tintedSignature(sig) {
  if (sig._white) return sig._white;
  const c = document.createElement('canvas');
  c.width = sig.mask.width; c.height = sig.mask.height;
  const ctx = c.getContext('2d');
  ctx.drawImage(sig.mask, 0, 0);
  ctx.globalCompositeOperation = 'source-in';
  ctx.fillStyle = geo().signature.color;
  ctx.fillRect(0, 0, c.width, c.height);
  sig._white = c;
  return c;
}

/* ---- onto the card ----------------------------------------------- */

/** Draw the signature in white inside geo().signature.box on the card. */
function drawSignatureOnCard(ctx, sig, pxPerMm) {
  if (!hasInk(sig)) return;
  if (sig.kind === 'image') return drawSignatureImage(ctx, sig, pxPerMm);
  const pts = sig.strokes.flat();
  const minX = Math.min(...pts.map((p) => p.x)), maxX = Math.max(...pts.map((p) => p.x));
  const minY = Math.min(...pts.map((p) => p.y)), maxY = Math.max(...pts.map((p) => p.y));
  const inkW = Math.max(maxX - minX, 1), inkH = Math.max(maxY - minY, 1);

  const { box, color, strokeMm, minStrokeMm } = geo().signature;
  const pad = strokeMm / 2;                       // keep thick strokes inside the box
  const availW = box.w - 2 * pad, availH = box.h - 2 * pad;
  const mmPerUnit = Math.min(availW / inkW, availH / inkH);   // fit, no stretch
  const left = box.x + pad + (availW - inkW * mmPerUnit) / 2;
  const top = box.y + pad + (availH - inkH * mmPerUnit) / 2;

  strokeSignature(ctx, sig, {
    toX: (x) => (left + (x - minX) * mmPerUnit) * pxPerMm,
    toY: (y) => (top + (y - minY) * mmPerUnit) * pxPerMm,
    scale: pxPerMm,                                 // widths below are in mm
    minW: minStrokeMm,
    maxW: strokeMm,
    color,
  });
}

/** Scanned signature: fit the trimmed mask into the box, no stretching. */
function drawSignatureImage(ctx, sig, pxPerMm) {
  const { box, strokeMm } = geo().signature;
  const pad = strokeMm / 2;
  const availW = box.w - 2 * pad, availH = box.h - 2 * pad;
  const scale = Math.min(availW / sig.mask.width, availH / sig.mask.height);
  const drawW = sig.mask.width * scale, drawH = sig.mask.height * scale;
  const x = box.x + pad + (availW - drawW) / 2;
  const y = box.y + pad + (availH - drawH) / 2;
  ctx.imageSmoothingEnabled = true;
  ctx.imageSmoothingQuality = 'high';
  ctx.drawImage(tintedSignature(sig), x * pxPerMm, y * pxPerMm, drawW * pxPerMm, drawH * pxPerMm);
}

/* ---- the on-screen pad ------------------------------------------- */

const PAD_ASPECT = 2.6;   // width / height

/**
 * createSignaturePad(canvas, onChange)
 * Captures pointer strokes, draws them live with the same curve code
 * the card uses, and calls onChange(sig|null) after every stroke.
 * Returns { clear(), setValue(sig), destroy() }.
 */
function createSignaturePad(canvas, onChange) {
  const ctx = canvas.getContext('2d');
  let strokes = [];
  let current = null;
  let cssW = 0, cssH = 0;

  const redraw = () => {
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    const dpr = Math.max(1, window.devicePixelRatio || 1);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    const all = current ? [...strokes, current] : strokes;
    if (all.some((s) => s.length)) {
      strokeSignature(ctx, { strokes: all }, {
        toX: (x) => x, toY: (y) => y, scale: 1,
        minW: 0.9, maxW: 2.8, color: '#1d1c1a',
      });
    }
  };

  const resize = () => {
    const w = canvas.parentElement.clientWidth;
    if (!w || w === cssW) return;
    const prevW = cssW;
    cssW = w; cssH = Math.round(w / PAD_ASPECT);
    const dpr = Math.max(1, window.devicePixelRatio || 1);
    canvas.width = cssW * dpr; canvas.height = cssH * dpr;
    canvas.style.height = `${cssH}px`;
    if (strokes.length) {
      if (prevW) {                                     // same pad, new width: scale along
        const k = w / prevW;
        strokes = strokes.map((s) => s.map((p) => ({ x: p.x * k, y: p.y * k })));
      } else {                                         // strokes from elsewhere (example): fit them
        const pts = strokes.flat();
        const maxX = Math.max(...pts.map((p) => p.x), 1), maxY = Math.max(...pts.map((p) => p.y), 1);
        const k = Math.min((cssW * 0.9) / maxX, (cssH * 0.9) / maxY);
        strokes = strokes.map((s) => s.map((p) => ({ x: p.x * k, y: p.y * k })));
      }
    }
    redraw();
  };

  const pos = (e) => {
    const r = canvas.getBoundingClientRect();
    return { x: e.clientX - r.left, y: e.clientY - r.top };
  };
  const emit = () => onChange(strokes.some((s) => s.length) ? { strokes: strokes.map((s) => s.map((p) => ({ ...p }))) } : null);

  const down = (e) => {
    if (e.button !== undefined && e.button !== 0) return;
    e.preventDefault();
    canvas.setPointerCapture?.(e.pointerId);
    current = [pos(e)];
    redraw();
  };
  const move = (e) => {
    if (!current) return;
    e.preventDefault();
    const p = pos(e), last = current[current.length - 1];
    if (Math.hypot(p.x - last.x, p.y - last.y) < 2) return;   // min distance: ignore jitter
    current.push(p);
    redraw();
  };
  const up = (e) => {
    if (!current) return;
    e.preventDefault();
    strokes.push(current);
    current = null;
    redraw();
    emit();
  };

  canvas.addEventListener('pointerdown', down);
  canvas.addEventListener('pointermove', move);
  canvas.addEventListener('pointerup', up);
  canvas.addEventListener('pointercancel', up);
  canvas.addEventListener('pointerleave', (e) => { if (current && !canvas.hasPointerCapture?.(e.pointerId)) up(e); });

  const ro = new ResizeObserver(resize);
  ro.observe(canvas.parentElement);
  resize();

  return {
    clear() { strokes = []; current = null; redraw(); onChange(null); },
    setValue(sig) {
      // a scanned signature is shown as a picture, not re-drawn in the pad
      strokes = sig && sig.kind !== 'image' ? sig.strokes.map((s) => s.map((p) => ({ x: p.x, y: p.y }))) : [];
      current = null;
      const w = cssW; cssW = 0; resize();            // force a re-fit
      if (!w) redraw();
    },
    destroy() { ro.disconnect(); },
  };
}
