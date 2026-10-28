/* ------------------------------------------------------------------
   Template map
   ------------------------------------------------------------------
   A card used to be described twice: by its artwork, and by 84 numbers
   in config.js that somebody measured off that artwork by hand. Move the
   photo window in the design and the numbers are silently wrong — the
   card still renders, it is just wrong.

   A map says it once. It is a third PNG the designer draws, the same
   size as the artwork, with every zone painted a flat colour. The app
   reads it and works the geometry out itself:

     magenta   the photo window — where the person goes
     cyan      artwork that is drawn back on top of the person
     yellow    the name box
     orange    the job-title box
     red       the employee-number box
     green     the signature box
     blue      the QR square
     purple    a short line where the top of the head should land

   The two regions stay as masks rather than becoming polygons, so the
   band over the shoulders can be any shape at all — curved, torn,
   feathered — instead of the six-vertex approximation it is today.

   No map, no change: the app falls back to CONFIG's polygons and boxes.
   ------------------------------------------------------------------ */

const MAP_COLOURS = {
  photo:      [255, 0, 255],
  front:      [0, 255, 255],
  name:       [255, 255, 0],
  title:      [255, 128, 0],
  employeeNo: [255, 0, 0],
  signature:  [0, 255, 0],
  qr:         [0, 0, 255],
  head:       [128, 0, 255],
};
const MAP_TOLERANCE = 60;        // export softening, JPEG-ish fringes, Canva's own blending
const MAP_AMBIGUITY = 4;         // see below
const MASK_KEYS = ['photo', 'front'];

/**
 * Which zone a pixel belongs to, or null.
 *
 * Nearest-colour alone is not enough, and the failure is quiet. Where two
 * zones meet, the edge pixels are blends of their two colours — and a
 * blend can land nearer a third colour than either parent. Purple sits
 * close to the magenta/cyan line, so every antialiased pixel along the
 * photo window's edge read as a head marker, and the head came out twice
 * its width.
 *
 * So a pixel has to be not just close to a colour but clearly closer to
 * it than to any other. Blends are ambiguous by definition and get
 * thrown away, which is what you want: they belong to neither zone.
 */
function nearest_(r, g, b, a) {
  if (a < 128) return null;
  let best = null, bestD = Infinity, secondD = Infinity;
  for (const key of Object.keys(MAP_COLOURS)) {
    const [cr, cg, cb] = MAP_COLOURS[key];
    const d = (r - cr) ** 2 + (g - cg) ** 2 + (b - cb) ** 2;
    if (d < bestD) { secondD = bestD; bestD = d; best = key; }
    else if (d < secondD) { secondD = d; }
  }
  if (bestD > MAP_TOLERANCE * MAP_TOLERANCE * 3) return null;
  if (secondD < bestD * MAP_AMBIGUITY) return null;
  return best;
}

/**
 * Read a map image into masks (canvases), boxes (mm) and the head target.
 * Returns null when the image carries none of the colours, which is how a
 * file that is not a map is told apart from one that is.
 */
function readTemplateMap(image, cardWidthMm = CONFIG.card.widthMm, cardHeightMm = CONFIG.card.heightMm) {
  const w = image.naturalWidth || image.width;
  const h = image.naturalHeight || image.height;
  const src = document.createElement('canvas');
  src.width = w; src.height = h;
  src.getContext('2d').drawImage(image, 0, 0);
  const px = src.getContext('2d').getImageData(0, 0, w, h).data;

  const masks = {};
  for (const key of MASK_KEYS) {
    const c = document.createElement('canvas');
    c.width = w; c.height = h;
    masks[key] = { canvas: c, data: c.getContext('2d').createImageData(w, h), count: 0 };
  }
  const bounds = {};   // key -> [minX, minY, maxX, maxY]

  for (let i = 0, p = 0; i < px.length; i += 4, p++) {
    const key = nearest_(px[i], px[i + 1], px[i + 2], px[i + 3]);
    if (!key) continue;
    if (MASK_KEYS.includes(key)) {
      const m = masks[key];
      m.data.data[i] = 255; m.data.data[i + 1] = 255; m.data.data[i + 2] = 255;
      m.data.data[i + 3] = 255;
      m.count++;
    }
    const x = p % w, y = (p / w) | 0;
    const b = bounds[key];
    if (!b) bounds[key] = [x, y, x, y];
    else {
      if (x < b[0]) b[0] = x;
      if (y < b[1]) b[1] = y;
      if (x > b[2]) b[2] = x;
      if (y > b[3]) b[3] = y;
    }
  }

  // The classifier above throws boundary pixels away, which leaves a mask
  // with a hard edge where a traced path would have had a soft one. Give
  // those pixels back as partial coverage: a pixel halfway between the
  // photo window and the band belongs half to each, and saying so is what
  // keeps the person's shoulder from going jagged against the band.
  for (const key of MASK_KEYS) {
    const [mr, mg, mb] = MAP_COLOURS[key];
    const others = Object.keys(MAP_COLOURS).filter((k) => k !== key)
      .map((k) => MAP_COLOURS[k]);
    const d = masks[key].data.data;
    for (let i = 0; i < px.length; i += 4) {
      if (d[i + 3] === 255 || px[i + 3] < 128) continue;
      const dm = (px[i] - mr) ** 2 + (px[i + 1] - mg) ** 2 + (px[i + 2] - mb) ** 2;
      if (dm > MAP_TOLERANCE * MAP_TOLERANCE * 12) continue;      // nowhere near
      let dOther = Infinity;
      for (const [r2, g2, b2] of others) {
        const dd = (px[i] - r2) ** 2 + (px[i + 1] - g2) ** 2 + (px[i + 2] - b2) ** 2;
        if (dd < dOther) dOther = dd;
      }
      const cover = Math.round(255 * (dOther / (dm + dOther)));
      if (cover <= 0) continue;
      d[i] = 255; d[i + 1] = 255; d[i + 2] = 255; d[i + 3] = cover;
      masks[key].count++;
    }
  }

  if (!Object.keys(bounds).length) return null;

  const mmX = (v) => +(v / w * cardWidthMm).toFixed(3);
  const mmY = (v) => +(v / h * cardHeightMm).toFixed(3);
  const boxOf = (key) => {
    const b = bounds[key];
    if (!b) return null;
    return { x: mmX(b[0]), y: mmY(b[1]), w: mmX(b[2] - b[0] + 1), h: mmY(b[3] - b[1] + 1) };
  };

  const out = { width: w, height: h, masks: {}, boxes: {}, head: null, found: Object.keys(bounds) };
  for (const key of MASK_KEYS) {
    if (!masks[key].count) continue;
    masks[key].canvas.getContext('2d').putImageData(masks[key].data, 0, 0);
    out.masks[key] = masks[key].canvas;
  }
  for (const key of ['name', 'title', 'employeeNo', 'signature', 'qr']) {
    const box = boxOf(key);
    if (box) out.boxes[key] = box;
  }
  const photo = boxOf('photo');
  // how far down the card the person may reach: the bottom of their window
  if (photo) out.photoBottom = +(photo.y + photo.h).toFixed(3);
  const head = boxOf('head');
  // the marker is a line: its top is where the head goes, its ends say
  // how wide the head should be and where its centre is
  if (head) out.head = { top: head.y, centerX: +(head.x + head.w / 2).toFixed(3), width: head.w };
  return out;
}

/**
 * The geometry the card should be drawn with: the map's answers where it
 * has them, CONFIG's where it does not.
 */
function templateGeometry(map) {
  const g = {
    text: CONFIG.text,
    photo: CONFIG.photo,
    qr: CONFIG.qr,
    signature: CONFIG.signature,
    masks: null,
  };
  if (!map) return g;
  g.masks = map.masks;
  const text = {};
  for (const key of Object.keys(CONFIG.text)) {
    text[key] = map.boxes[key] ? { ...CONFIG.text[key], box: map.boxes[key] } : CONFIG.text[key];
  }
  g.text = text;
  if (map.boxes.signature) g.signature = { ...CONFIG.signature, box: map.boxes.signature };
  if (map.boxes.qr) {
    g.qr = { ...CONFIG.qr, square: { x: map.boxes.qr.x, y: map.boxes.qr.y, size: Math.min(map.boxes.qr.w, map.boxes.qr.h) } };
  }
  if (map.head) {
    g.photo = {
      ...CONFIG.photo,
      headTop: map.head.top,
      headCenterX: map.head.centerX,
      headWidth: map.head.width,
      reachY: map.head.reachY ?? map.photoBottom ?? CONFIG.photo.reachY,
    };
  }
  return g;
}

/**
 * The current card's geometry, drawn as a map.
 *
 * Two jobs. It is how the mask path is proved — build a map from the
 * polygons in use, render through it, and the card must come out the
 * same. And it is what a designer starts from: today's layout, as a
 * layer they can move things around on.
 *
 * Open the app with #map to download it.
 */
function mapFromConfig(w = 638, h = 1011) {
  const c = document.createElement('canvas');
  c.width = w; c.height = h;
  const ctx = c.getContext('2d');
  ctx.imageSmoothingEnabled = false;
  const sx = w / CONFIG.card.widthMm, sy = h / CONFIG.card.heightMm;
  const rgb = (k) => `rgb(${MAP_COLOURS[k].join(',')})`;
  const fillPoly = (poly, key) => {
    ctx.fillStyle = rgb(key);
    ctx.beginPath();
    poly.forEach(([x, y], i) => (i ? ctx.lineTo(x * sx, y * sy) : ctx.moveTo(x * sx, y * sy)));
    ctx.closePath();
    ctx.fill();
  };
  const fillBox = (b, key) => {
    ctx.fillStyle = rgb(key);
    ctx.fillRect(Math.round(b.x * sx), Math.round(b.y * sy),
                 Math.max(1, Math.round(b.w * sx)), Math.max(1, Math.round(b.h * sy)));
  };

  fillPoly(CONFIG.photoArea.polygon, 'photo');
  for (const poly of CONFIG.frontOutline.polygons) fillPoly(poly, 'front');
  fillBox(CONFIG.text.name.box, 'name');
  fillBox(CONFIG.text.title.box, 'title');
  fillBox(CONFIG.text.employeeNo.box, 'employeeNo');
  fillBox(CONFIG.signature.box, 'signature');
  fillBox({ ...CONFIG.qr.square, w: CONFIG.qr.square.size, h: CONFIG.qr.square.size }, 'qr');
  // the head marker: a line whose TOP edge is where the head goes
  const P = CONFIG.photo;
  fillBox({ x: P.headCenterX - P.headWidth / 2, y: P.headTop, w: P.headWidth, h: 0.3 }, 'head');
  return c;
}

/* ---- the geometry in force -------------------------------------- */

let TEMPLATE_GEO = null;

/** Where the card's parts go. CONFIG until a map says otherwise. */
function geo() {
  if (!TEMPLATE_GEO) TEMPLATE_GEO = templateGeometry(null);
  return TEMPLATE_GEO;
}

/** Hand the app a map (or null to go back to CONFIG). */
function useTemplateMap(map) {
  TEMPLATE_GEO = templateGeometry(map);
  return TEMPLATE_GEO;
}

/**
 * Paint `draw` through a mask: whatever it draws survives only where the
 * mask is opaque. This is what replaces clipping to a traced polygon, and
 * why a template's band can be any shape the designer likes.
 */
function throughMask(ctx, mask, w, h, draw) {
  const layer = document.createElement('canvas');
  layer.width = Math.max(1, Math.round(w));
  layer.height = Math.max(1, Math.round(h));
  const lc = layer.getContext('2d');
  lc.imageSmoothingEnabled = true;
  lc.imageSmoothingQuality = 'high';
  draw(lc);
  lc.globalCompositeOperation = 'destination-in';
  lc.drawImage(mask, 0, 0, layer.width, layer.height);
  ctx.drawImage(layer, 0, 0);
  layer.width = layer.height = 0;        // let it go straight away: these are 300 DPI
}

/* ---- is this template actually printable? ------------------------ */

/** WCAG relative luminance, 0..1. */
function luminance_(r, g, b) {
  const f = (v) => { v /= 255; return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4; };
  return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b);
}
function contrast_(a, b) {
  const l1 = Math.max(a, b), l2 = Math.min(a, b);
  return (l1 + 0.05) / (l2 + 0.05);
}

/**
 * The mistake a designer can make that nothing else would catch: putting
 * a text box over light artwork. The name prints in pale gold and the
 * rest in white, so on a light background the card comes out looking
 * blank in exactly the places that carry the person's identity — and it
 * looks fine on screen in Canva, because Canva is not drawing the text.
 *
 * Averages the artwork under each box and compares it with the ink.
 * Returns one entry per unreadable box — { key, label, ink, ratio, need }
 * — so the caller can say it in one sentence instead of four. Empty
 * means the card will read.
 */
function checkTemplateContrast(map, frontImage, minRatio = 3) {
  if (!map) return [];
  const w = frontImage.naturalWidth || frontImage.width;
  const h = frontImage.naturalHeight || frontImage.height;
  const c = document.createElement('canvas');
  c.width = w; c.height = h;
  c.getContext('2d').drawImage(frontImage, 0, 0, w, h);
  const ctx = c.getContext('2d');

  const ink = {
    name: CONFIG.text.name.color,
    title: CONFIG.text.title.color,
    employeeNo: CONFIG.text.employeeNo.color,
    signature: CONFIG.signature.color,
  };
  const label = { name: 'name', title: 'job title', employeeNo: 'employee number', signature: 'signature' };
  const hex = (s) => [parseInt(s.slice(1, 3), 16), parseInt(s.slice(3, 5), 16), parseInt(s.slice(5, 7), 16)];

  const out = [];
  for (const key of Object.keys(ink)) {
    const box = map.boxes[key];
    if (!box) continue;
    const x = Math.round(box.x / CONFIG.card.widthMm * w);
    const y = Math.round(box.y / CONFIG.card.heightMm * h);
    const bw = Math.max(1, Math.round(box.w / CONFIG.card.widthMm * w));
    const bh = Math.max(1, Math.round(box.h / CONFIG.card.heightMm * h));
    const px = ctx.getImageData(x, y, bw, bh).data;
    let r = 0, g = 0, b = 0, n = 0;
    for (let i = 0; i < px.length; i += 4) { r += px[i]; g += px[i + 1]; b += px[i + 2]; n++; }
    const back = luminance_(r / n, g / n, b / n);
    const fore = luminance_(...hex(ink[key]));
    const ratio = contrast_(fore, back);
    if (ratio < minRatio) out.push({ key, label: label[key], ink: ink[key], ratio: +ratio.toFixed(1), need: minRatio });
  }
  return out;
}
