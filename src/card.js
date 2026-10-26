/* ------------------------------------------------------------------
   Drawing the card
   ------------------------------------------------------------------
   drawFront / drawBack render into any 2D context at any pixels-per-mm.
   The on-screen preview calls them at screen resolution, the PDF at
   300 DPI, so what you see is what prints.

   Front layer order (bottom to top):
     1. front template
     2. photo, clipped to CONFIG.photoArea
     3. front template again, clipped to CONFIG.frontOutline
        (puts the dark band + gold line back IN FRONT of the photo)
     4. name, title, employee number
     5. signature (white)
     6. QR code
   ------------------------------------------------------------------ */

/* ---- assets ------------------------------------------------------ */

function loadImage(src) {
  return new Promise((resolve, reject) => {
    const img = new Image();
    img.onload = () => resolve(img);
    img.onerror = () => reject(new Error(`Could not load ${String(src).slice(0, 40)}`));
    img.src = src;
  });
}

async function loadTemplates() {
  const [front, back] = await Promise.all([loadImage(ASSETS.front), loadImage(ASSETS.back)]);
  return { front, back };
}

// Google-Fonts style subsets: latin + latin-ext per family.
const UNICODE_LATIN = 'U+0000-00FF, U+0131, U+0152-0153, U+02BB-02BC, U+02C6, U+02DA, U+02DC, U+0304, U+0308, U+0329, U+2000-206F, U+20AC, U+2122, U+2191, U+2193, U+2212, U+2215, U+FEFF, U+FFFD';
const UNICODE_LATIN_EXT = 'U+0100-02BA, U+02BD-02C5, U+02C7-02CC, U+02CE-02D7, U+02DD-02FF, U+0304, U+0308, U+0329, U+1D00-1DBF, U+1E00-1E9F, U+1EF2-1EFF, U+2020, U+20A0-20AB, U+20AD-20C0, U+2113, U+2C60-2C7F, U+A720-A7FF';

async function loadCardFonts() {
  const faces = [];
  for (const key of Object.keys(ASSETS.fonts)) {
    const { family, weight } = CONFIG.fonts[key];
    const [latin, latinExt] = ASSETS.fonts[key];
    faces.push(new FontFace(family, `url(${latin}) format('woff2')`, { weight: String(weight), unicodeRange: UNICODE_LATIN }));
    faces.push(new FontFace(family, `url(${latinExt}) format('woff2')`, { weight: String(weight), unicodeRange: UNICODE_LATIN_EXT }));
  }
  await Promise.all(faces.map((f) => f.load()));
  faces.forEach((f) => document.fonts.add(f));
  for (const key of Object.keys(ASSETS.fonts)) {
    const { family, weight } = CONFIG.fonts[key];
    const ok = await document.fonts.load(`${weight} 20px "${family}"`, 'AÑ');
    if (!ok.length) throw new Error(`The ${key} font did not load.`);
  }
}

/* ---- helpers ----------------------------------------------------- */

function prepareCanvas(ctx, pxPerMm) {
  const size = cardSizePx(pxPerMm);
  ctx.canvas.width = size.w;
  ctx.canvas.height = size.h;
  ctx.imageSmoothingEnabled = true;
  ctx.imageSmoothingQuality = 'high';
  return size;
}

function tracePolygons(ctx, polygons, pxPerMm) {
  ctx.beginPath();
  for (const poly of polygons) {
    poly.forEach(([x, y], i) => (i === 0 ? ctx.moveTo(x * pxPerMm, y * pxPerMm) : ctx.lineTo(x * pxPerMm, y * pxPerMm)));
    ctx.closePath();
  }
}

// Fit decisions are made once, at print resolution, from a scratch context.
let measureCtx = null;
function layoutText(data) {
  measureCtx ??= document.createElement('canvas').getContext('2d');
  const ppm = printPxPerMm();
  return {
    name: fitText(measureCtx, CONFIG.text.name, data.name, ppm),
    title: fitText(measureCtx, CONFIG.text.title, data.title, ppm),
    employeeNo: fitText(measureCtx, CONFIG.text.employeeNo, employeeNoLine(data.year, data.seq), ppm),
  };
}

/* ---- the two sides ----------------------------------------------- */

/**
 * drawFront(ctx, templates, pxPerMm, data, options)
 *   data: { name, title, year, seq, photo: {image, placement}|null, signature|null }
 *   options.showGuides: outline the photo area and front-outline polygons
 *   Returns the text layout (so the form can check `fits`).
 */
function drawFront(ctx, templates, pxPerMm, data = null, options = {}) {
  const { w, h } = prepareCanvas(ctx, pxPerMm);
  ctx.drawImage(templates.front, 0, 0, w, h);

  if (data?.photo) {
    const p = data.photo.placement;   // mm
    ctx.save();
    tracePolygons(ctx, [CONFIG.photoArea.polygon], pxPerMm);
    ctx.clip();
    ctx.drawImage(data.photo.image, p.x * pxPerMm, p.y * pxPerMm, p.w * pxPerMm, p.h * pxPerMm);
    ctx.restore();
  }

  // band + gold line back on top of the photo
  ctx.save();
  tracePolygons(ctx, CONFIG.frontOutline.polygons, pxPerMm);
  ctx.clip();
  ctx.drawImage(templates.front, 0, 0, w, h);
  ctx.restore();

  if (options.showGuides) {
    ctx.save();
    tracePolygons(ctx, [CONFIG.photoArea.polygon], pxPerMm);
    ctx.strokeStyle = '#d0167a'; ctx.lineWidth = Math.max(1, pxPerMm * 0.12); ctx.stroke();
    tracePolygons(ctx, CONFIG.frontOutline.polygons, pxPerMm);
    ctx.strokeStyle = '#00b3ff'; ctx.stroke();
    ctx.restore();
  }

  if (!data) return null;

  const layout = layoutText(data);
  ctx.save();
  drawFittedText(ctx, CONFIG.text.name, layout.name, pxPerMm);
  drawFittedText(ctx, CONFIG.text.title, layout.title, pxPerMm);
  drawFittedText(ctx, CONFIG.text.employeeNo, layout.employeeNo, pxPerMm);
  ctx.restore();

  if (data.signature) { ctx.save(); drawSignatureOnCard(ctx, data.signature, pxPerMm); ctx.restore(); }

  ctx.save(); drawQr(ctx, pxPerMm); ctx.restore();
  return layout;
}

function drawBack(ctx, templates, pxPerMm, rotate180 = false) {
  const { w, h } = prepareCanvas(ctx, pxPerMm);
  ctx.save();
  if (rotate180) { ctx.translate(w, h); ctx.rotate(Math.PI); }
  ctx.drawImage(templates.back, 0, 0, w, h);
  ctx.restore();
}

/** Render one side at print resolution (300 DPI) into a fresh canvas. */
function renderSideCanvas(templates, side, data, rotateBack) {
  const canvas = document.createElement('canvas');
  const ctx = canvas.getContext('2d');
  if (side === 'front') drawFront(ctx, templates, printPxPerMm(), data);
  else drawBack(ctx, templates, printPxPerMm(), rotateBack);
  return canvas;
}
