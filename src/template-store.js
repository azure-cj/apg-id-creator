/* ------------------------------------------------------------------
   The card design in use
   ------------------------------------------------------------------
   The app ships with one card design built in. This lets a different one
   be put in without rebuilding the app: two PNGs the same shape as a
   card, and then the placeholders — name, job title, number, signature,
   QR, photo — are dragged into place on the preview.

   Optionally a third PNG, the map (template-map.js), which says where
   everything goes in colour. With a map there is nothing to drag: the
   placeholders land where the designer painted them, and the photo can
   be any shape, with artwork in front of it. Without one the photo is a
   plain rectangle sitting on top of the artwork.

   Only before an ID is started. Half-way through, the photo has already
   been placed against the old design and the preview is of a card that
   no longer exists, so the panel is offered on step 1 and nowhere else.

   Kept in this browser's storage: it survives closing the app, it does
   not travel to another computer, and the built-in design is never
   overwritten — "Use the built-in design" always works.
   ------------------------------------------------------------------ */

const TEMPLATE_KEY = 'apg-id-template';
const TEMPLATE_W = 638;                 // a design is stored at the printing size:
const TEMPLATE_H = 1011;                // 54 x 85.6 mm at 300 DPI
const TEMPLATE_SHAPE_SLACK = 0.02;      // how far off the card's proportions may be

/** The bounding box, in mm, of a polygon from config. */
function polygonBounds_(poly) {
  const xs = poly.map((p) => p[0]), ys = poly.map((p) => p[1]);
  const x = Math.min(...xs), y = Math.min(...ys);
  return { x, y, w: Math.max(...xs) - x, h: Math.max(...ys) - y };
}

/**
 * Where the head sits inside the photo window, as fractions of it.
 *
 * Taken from the built-in design, so a photo window dragged somewhere
 * else frames the face the same way this one does. It is what lets the
 * window be a single rectangle to drag instead of four numbers to type.
 */
const HEAD_RATIO = (() => {
  const r = polygonBounds_(CONFIG.photoArea.polygon);
  const P = CONFIG.photo;
  return {
    centerX: (P.headCenterX - r.x) / r.w,
    top: (P.headTop - r.y) / r.h,
    width: P.headWidth / r.w,
    reach: (P.reachY - r.y) / r.h,
  };
})();

/** A white rectangle on a transparent card-sized canvas: a photo window. */
function rectMask_(rect) {
  const c = document.createElement('canvas');
  c.width = TEMPLATE_W; c.height = TEMPLATE_H;
  const ctx = c.getContext('2d');
  ctx.fillStyle = '#fff';
  ctx.fillRect(
    Math.round(rect.x / CONFIG.card.widthMm * TEMPLATE_W),
    Math.round(rect.y / CONFIG.card.heightMm * TEMPLATE_H),
    Math.max(1, Math.round(rect.w / CONFIG.card.widthMm * TEMPLATE_W)),
    Math.max(1, Math.round(rect.h / CONFIG.card.heightMm * TEMPLATE_H)),
  );
  return c;
}

const TemplateStore = {
  custom: null,       // { name, front, back, mapPng, boxes, photoRect } — all serialisable
  images: null,       // { front, back } once decoded
  mapMasks: null,     // the shapes the map PNG drew, if there was one
  mapBoxes: null,     // and where it put the boxes, before anybody dragged them
  mapPhotoRect: null,
  note: '',         // anything the operator should know about the last load

  get isCustom() { return !!this.custom; },
  get name() { return this.custom ? this.custom.name : 'Built-in design'; },
  get hasMap() { return !!(this.custom && this.custom.mapPng); },

  /* ---- the placeholders -------------------------------------------- */

  /** Where the built-in design puts everything: the starting point. */
  defaults() {
    return {
      boxes: {
        name: { ...CONFIG.text.name.box },
        title: { ...CONFIG.text.title.box },
        employeeNo: { ...CONFIG.text.employeeNo.box },
        signature: { ...CONFIG.signature.box },
        qr: { x: CONFIG.qr.square.x, y: CONFIG.qr.square.y, w: CONFIG.qr.square.size, h: CONFIG.qr.square.size },
      },
      photoRect: polygonBounds_(CONFIG.photoArea.polygon),
    };
  },

  /** The map object the rest of the app reads its geometry out of. */
  asMap() {
    if (!this.custom) return null;
    const { boxes, photoRect } = this.custom;
    const masks = {};
    if (this.mapMasks?.photo) masks.photo = this.mapMasks.photo;
    else masks.photo = rectMask_(photoRect);
    if (this.mapMasks?.front) masks.front = this.mapMasks.front;
    return {
      masks,
      boxes,
      head: {
        top: photoRect.y + HEAD_RATIO.top * photoRect.h,
        centerX: photoRect.x + HEAD_RATIO.centerX * photoRect.w,
        width: HEAD_RATIO.width * photoRect.w,
        reachY: photoRect.y + HEAD_RATIO.reach * photoRect.h,
      },
    };
  },

  /** Hand the current design to the renderer. Call after any change. */
  apply() {
    return useTemplateMap(this.asMap());
  },

  /** The two images to draw the card from. */
  templatesFor(builtIn) {
    return this.images ? { ...builtIn, front: this.images.front, back: this.images.back } : builtIn;
  },

  /* ---- taking a new design ----------------------------------------- */

  /** Draw an image at the stored size, so everything downstream agrees. */
  _normalise(img) {
    const c = document.createElement('canvas');
    c.width = TEMPLATE_W; c.height = TEMPLATE_H;
    const ctx = c.getContext('2d');
    ctx.imageSmoothingQuality = 'high';
    ctx.drawImage(img, 0, 0, TEMPLATE_W, TEMPLATE_H);
    return c;
  },

  /**
   * Take new artwork. Returns { ok, warnings } or { error }.
   * `mapImage` is optional; without it the placeholders start where the
   * built-in design has them, to be dragged.
   */
  async adopt({ name, frontImage, backImage, mapImage = null }) {
    const resized = [];
    for (const [side, img] of [['front', frontImage], ['back', backImage]]) {
      const w = img.naturalWidth || img.width, h = img.naturalHeight || img.height;
      const want = CONFIG.card.widthMm / CONFIG.card.heightMm;
      if (Math.abs(w / h - want) > TEMPLATE_SHAPE_SLACK) {
        return { error: `The ${side} is ${w} × ${h} pixels, which is not the shape of an ID card. `
          + `Export it at ${TEMPLATE_W} × ${TEMPLATE_H} pixels (or anything with the same proportions).` };
      }
      if (w !== TEMPLATE_W || h !== TEMPLATE_H) resized.push(`${side} ${w} × ${h}`);
    }

    const front = this._normalise(frontImage);
    const back = this._normalise(backImage);
    const next = { name: name || 'Uploaded design', front: front.toDataURL('image/png'), back: back.toDataURL('image/png'), mapPng: null, ...this.defaults() };

    let masks = null;
    if (mapImage) {
      const map = readTemplateMap(mapImage);
      if (!map) {
        return { error: 'That map has none of the zone colours on it. Check the colours against TEMPLATE-SPEC.md, '
          + 'or leave the map out and drag the placeholders instead.' };
      }
      masks = map.masks;
      for (const key of ['name', 'title', 'employeeNo', 'signature', 'qr']) {
        if (map.boxes[key]) next.boxes[key] = map.boxes[key];
      }
      if (map.masks.photo) next.photoRect = this._maskBounds(map.masks.photo);
      next.mapPng = this._normalise(mapImage).toDataURL('image/png');
      this._remember(next);
    }

    this.custom = next;
    this.mapMasks = masks;
    await this._decode();
    this.apply();
    this.note = resized.length ? `Resized to ${TEMPLATE_W} × ${TEMPLATE_H} (${resized.join(', ')}).` : '';
    return { ok: true, warnings: this.warnings() };
  },

  /** Keep the map's own answers, so a dragged box can be put back. */
  _remember(from) {
    this.mapBoxes = JSON.parse(JSON.stringify(from.boxes));
    this.mapPhotoRect = { ...from.photoRect };
  },

  /** Back to where the map — or the built-in design — put the boxes. */
  resetBoxes() {
    if (!this.custom) return;
    const d = this.defaults();
    this.custom.boxes = this.mapBoxes ? JSON.parse(JSON.stringify(this.mapBoxes)) : d.boxes;
    this.custom.photoRect = this.mapPhotoRect ? { ...this.mapPhotoRect } : d.photoRect;
    this.apply();
  },

  /** The rectangle a mask occupies, in mm. */
  _maskBounds(mask) {
    const { width: w, height: h } = mask;
    const px = mask.getContext('2d').getImageData(0, 0, w, h).data;
    let x0 = w, y0 = h, x1 = -1, y1 = -1;
    for (let i = 3, p = 0; i < px.length; i += 4, p++) {
      if (px[i] < 32) continue;
      const x = p % w, y = (p / w) | 0;
      if (x < x0) x0 = x;
      if (y < y0) y0 = y;
      if (x > x1) x1 = x;
      if (y > y1) y1 = y;
    }
    if (x1 < 0) return this.defaults().photoRect;
    const mmX = (v) => +(v / w * CONFIG.card.widthMm).toFixed(3);
    const mmY = (v) => +(v / h * CONFIG.card.heightMm).toFixed(3);
    return { x: mmX(x0), y: mmY(y0), w: mmX(x1 - x0 + 1), h: mmY(y1 - y0 + 1) };
  },

  /** Text that would not read against the artwork behind it. */
  warnings() {
    if (!this.custom || !this.images) return [];
    return checkTemplateContrast(this.asMap(), this.images.front);
  },

  /* ---- editing ----------------------------------------------------- */

  /** Move or resize one placeholder. `key` is a box name, or 'photo'. */
  setBox(key, box) {
    if (!this.custom) return;
    const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
    const W = CONFIG.card.widthMm, H = CONFIG.card.heightMm;
    const w = clamp(box.w, 2, W), h = clamp(box.h, 2, H);
    const fixed = { x: +clamp(box.x, 0, W - w).toFixed(3), y: +clamp(box.y, 0, H - h).toFixed(3), w: +w.toFixed(3), h: +h.toFixed(3) };
    if (key === 'photo') this.custom.photoRect = fixed;
    else if (this.custom.boxes[key]) this.custom.boxes[key] = fixed;
    this.apply();
  },

  box(key) {
    if (!this.custom) return null;
    return key === 'photo' ? { ...this.custom.photoRect } : { ...this.custom.boxes[key] };
  },

  /* ---- keeping it -------------------------------------------------- */

  save() {
    if (!this.custom) { try { localStorage.removeItem(TEMPLATE_KEY); } catch { /* private mode */ } return true; }
    try {
      localStorage.setItem(TEMPLATE_KEY, JSON.stringify(this.custom));
      return true;
    } catch {
      this.note = 'This design is too large to keep on this computer, so it will be used until the app is closed. '
        + 'Keep the files and load them again next time.';
      return false;
    }
  },

  async restore() {
    let raw = null;
    try { raw = localStorage.getItem(TEMPLATE_KEY); } catch { return false; }
    if (!raw) return false;
    try {
      const saved = JSON.parse(raw);
      if (!saved?.front || !saved?.back || !saved?.boxes || !saved?.photoRect) throw new Error('incomplete');
      this.custom = saved;
      await this._decode();
      if (saved.mapPng) {
        const mapImg = await loadImage(saved.mapPng);
        const map = readTemplateMap(mapImg);
        this.mapMasks = map ? map.masks : null;
        if (map) {
          const fresh = this.defaults();
          for (const key of Object.keys(fresh.boxes)) if (map.boxes[key]) fresh.boxes[key] = map.boxes[key];
          if (map.masks.photo) fresh.photoRect = this._maskBounds(map.masks.photo);
          this._remember(fresh);
        }
      }
      this.apply();
      return true;
    } catch {
      this.clear();
      return false;
    }
  },

  async _decode() {
    this.images = this.custom
      ? { front: await loadImage(this.custom.front), back: await loadImage(this.custom.back) }
      : null;
  },

  /** Back to the design the app was built with. */
  clear() {
    this.custom = null;
    this.images = null;
    this.mapMasks = null;
    this.mapBoxes = null;
    this.mapPhotoRect = null;
    this.note = '';
    useTemplateMap(null);
    try { localStorage.removeItem(TEMPLATE_KEY); } catch { /* private mode */ }
  },
};
