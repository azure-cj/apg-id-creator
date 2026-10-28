/* ------------------------------------------------------------------
   The card design in use
   ------------------------------------------------------------------
   Two things can be changed without rebuilding the app:

     the artwork   two PNGs the same shape as a card, front and back
     the map       where the photo, name, job title, number, signature
                   and QR sit on it

   The map is not a file. It is the boxes drawn over the preview, and it
   is always there to look at — on the built-in card as much as on
   uploaded artwork. Drag a box and the card redraws under it.

   Only before an ID is started. Half-way through, the photo has already
   been placed against the old map and the preview is of a card that no
   longer exists, so it is offered on step 1 and nowhere else.

   Kept in this browser's storage: it survives closing the app, it does
   not travel to another computer, and the built-in design is never
   overwritten — "Use the built-in design" always works.
   ------------------------------------------------------------------ */

const TEMPLATE_KEY = 'apg-id-template';
const TEMPLATE_W = 638;                 // a design is stored at the printing size:
const TEMPLATE_H = 1011;                // 54 x 85.6 mm at 300 DPI
const TEMPLATE_SHAPE_SLACK = 0.02;      // how far off the card's proportions artwork may be

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
 * window be a single box to drag instead of four numbers to type.
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

const PHOTO_BASE = polygonBounds_(CONFIG.photoArea.polygon);

/** White shapes on a transparent card-sized canvas: what a mask is. */
function shapeMask_(polygons) {
  const c = document.createElement('canvas');
  c.width = TEMPLATE_W; c.height = TEMPLATE_H;
  const ctx = c.getContext('2d');
  const sx = TEMPLATE_W / CONFIG.card.widthMm, sy = TEMPLATE_H / CONFIG.card.heightMm;
  ctx.fillStyle = '#fff';
  ctx.beginPath();
  for (const poly of polygons) {
    poly.forEach(([x, y], i) => (i ? ctx.lineTo(x * sx, y * sy) : ctx.moveTo(x * sx, y * sy)));
    ctx.closePath();
  }
  ctx.fill();
  return c;
}

const rectPolygon_ = (r) => [[r.x, r.y], [r.x + r.w, r.y], [r.x + r.w, r.y + r.h], [r.x, r.y + r.h]];

/**
 * Move and scale a polygon so its bounding box becomes `to`.
 *
 * This is what lets the built-in card's photo window stay the shape it
 * is — stepped, to clear the gold line — while still being dragged
 * around as a simple box. Without it, adjusting the built-in design
 * would mean giving up its cut-out and settling for a rectangle.
 */
function fitPolygon_(poly, from, to) {
  const kx = to.w / from.w, ky = to.h / from.h;
  return poly.map(([x, y]) => [to.x + (x - from.x) * kx, to.y + (y - from.y) * ky]);
}

const TemplateStore = {
  custom: null,     // { name, front, back, boxes, photoRect } — all serialisable
  images: null,     // the uploaded artwork, decoded; null when the built-in is in use
  builtIn: null,    // the artwork the app ships with
  note: '',         // anything the operator should know about the last change

  /** Is anything at all different from the card the app was built with? */
  get isCustom() { return !!this.custom; },
  /** Has the artwork itself been replaced, as opposed to just the map? */
  get hasArtwork() { return !!(this.custom && this.custom.front); },
  get name() {
    if (!this.custom) return 'the built-in design';
    return this.custom.front ? this.custom.name : 'the built-in design, with the map moved';
  },

  /** The artwork the card is drawn from. */
  get templates() { return this.images || this.builtIn; },

  /* ---- the map ----------------------------------------------------- */

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
      photoRect: { ...PHOTO_BASE },
    };
  },

  /**
   * The map the rest of the app reads its geometry out of.
   *
   * null while nothing has been changed, which is deliberate: the
   * built-in card then renders through config's own clipping path,
   * exactly as it did before any of this existed.
   */
  asMap() {
    if (!this.custom) return null;
    const { boxes, photoRect } = this.custom;
    const masks = {};
    if (this.custom.front) {
      // uploaded artwork: nothing is known about its shapes, so the
      // photo is a plain window sitting on top of it
      masks.photo = shapeMask_([rectPolygon_(photoRect)]);
    } else {
      masks.photo = shapeMask_([fitPolygon_(CONFIG.photoArea.polygon, PHOTO_BASE, photoRect)]);
      masks.front = shapeMask_(CONFIG.frontOutline.polygons);   // the band stays with the artwork
    }
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

  /**
   * The photo window's real outline, in mm — stepped on the built-in
   * card, a plain rectangle on uploaded artwork. The editor draws the
   * handle in this shape, so what is dragged looks like what is cut.
   */
  photoOutline() {
    const rect = this.box('photo');
    if (this.hasArtwork) return rectPolygon_(rect);
    return fitPolygon_(CONFIG.photoArea.polygon, PHOTO_BASE, rect);
  },

  /** Hand the current design to the renderer. Call after any change. */
  apply() { return useTemplateMap(this.asMap()); },

  /**
   * Nothing has actually been changed: the built-in artwork with the
   * built-in map. Opening the map to look at it should leave the card
   * exactly as the app shipped it, mask path and all, so this is what
   * decides whether there is anything to keep.
   */
  get isPristine() {
    if (!this.custom || this.custom.front) return false;
    const d = this.defaults();
    return JSON.stringify([this.custom.boxes, this.custom.photoRect])
        === JSON.stringify([d.boxes, d.photoRect]);
  },

  /** Start moving the map about on whatever artwork is in use. */
  beginEditing() {
    if (!this.custom) {
      this.custom = { name: 'Built-in design', front: null, back: null, ...this.defaults() };
      this.apply();
    }
    return this.custom;
  },

  /* ---- new artwork -------------------------------------------------- */

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
   * Take new artwork, keeping wherever the map has been dragged to.
   * Returns { ok, warnings } or { error }.
   */
  async adopt({ name, frontImage, backImage }) {
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

    const map = this.custom ? { boxes: this.custom.boxes, photoRect: this.custom.photoRect } : this.defaults();
    this.custom = {
      name: name || 'Uploaded design',
      front: this._normalise(frontImage).toDataURL('image/png'),
      back: this._normalise(backImage).toDataURL('image/png'),
      ...map,
    };
    await this._decode();
    this.apply();
    this.note = resized.length ? `Resized to ${TEMPLATE_W} × ${TEMPLATE_H} (${resized.join(', ')}).` : '';
    return { ok: true, warnings: this.warnings() };
  },

  /** Text that would not read against the artwork behind it. */
  warnings() {
    const art = this.templates;
    if (!this.custom || !art) return [];
    return checkTemplateContrast(this.asMap(), art.front);
  },

  /* ---- editing ------------------------------------------------------ */

  /** Move or resize one box. `key` is a box name, or 'photo'. */
  setBox(key, box) {
    this.beginEditing();
    const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
    const W = CONFIG.card.widthMm, H = CONFIG.card.heightMm;
    const w = clamp(box.w, 2, W), h = clamp(box.h, 2, H);
    const fixed = {
      x: +clamp(box.x, 0, W - w).toFixed(3), y: +clamp(box.y, 0, H - h).toFixed(3),
      w: +w.toFixed(3), h: +h.toFixed(3),
    };
    if (key === 'photo') this.custom.photoRect = fixed;
    else if (this.custom.boxes[key]) this.custom.boxes[key] = fixed;
    this.apply();
  },

  box(key) {
    const from = this.custom || this.defaults();
    return key === 'photo' ? { ...from.photoRect } : { ...from.boxes[key] };
  },

  /** Back to where the built-in design puts the boxes. */
  resetBoxes() {
    if (!this.custom) return;
    Object.assign(this.custom, this.defaults());
    this.apply();
  },

  /* ---- keeping it --------------------------------------------------- */

  save() {
    if (!this.custom) { try { localStorage.removeItem(TEMPLATE_KEY); } catch { /* private mode */ } return true; }
    try {
      localStorage.setItem(TEMPLATE_KEY, JSON.stringify(this.custom));
      return true;
    } catch {
      this.note = 'This artwork is too large to keep on this computer, so it will be used until the app is closed. '
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
      if (!saved?.boxes || !saved?.photoRect) throw new Error('incomplete');
      this.custom = saved;
      await this._decode();
      this.apply();
      return true;
    } catch {
      this.clear();
      return false;
    }
  },

  async _decode() {
    this.images = this.custom?.front
      ? { front: await loadImage(this.custom.front), back: await loadImage(this.custom.back) }
      : null;
  },

  /** Back to the card the app was built with, artwork and map. */
  clear() {
    this.custom = null;
    this.images = null;
    this.note = '';
    useTemplateMap(null);
    try { localStorage.removeItem(TEMPLATE_KEY); } catch { /* private mode */ }
  },
};
