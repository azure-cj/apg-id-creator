/* ------------------------------------------------------------------
   Dragging the placeholders
   ------------------------------------------------------------------
   An overlay that sits exactly on top of the card preview, with one
   handle per placeholder. Drag the middle to move it, the corner to
   resize it; the card underneath redraws as it goes, so the design is
   checked against the real thing rather than against a wireframe.

   Everything is held in millimetres — the unit the card is printed in —
   and converted to percentages of the preview only to position the
   handles. That way the editor does not care how large the preview is,
   and a layout dragged on a laptop prints the same as one dragged on a
   monitor.

   Keyboard works too: tab to a handle, arrow keys move it, shift and
   arrow keys resize it. Some of this is done by people who cannot use
   a mouse well, and nudging by 0.1 mm is easier typed than dragged.
   ------------------------------------------------------------------ */

const PLACEHOLDERS = [
  { key: 'photo',      label: 'Photo' },
  { key: 'name',       label: 'Name' },
  { key: 'title',      label: 'Job title' },
  { key: 'employeeNo', label: 'Number' },
  { key: 'signature',  label: 'Signature' },
  { key: 'qr',         label: 'QR' },
];
const NUDGE_MM = 0.25;        // one arrow key
const MIN_MM = 2;             // a handle smaller than this cannot be grabbed

const TemplateEdit = {
  root: null,
  onChange: null,
  selected: 'photo',
  open: false,
  handles: new Map(),

  /** Build the overlay inside `root` (which must sit over the preview). */
  mount(root, onChange) {
    this.root = root;
    this.onChange = onChange;
    root.textContent = '';
    this.handles.clear();
    for (const { key, label } of PLACEHOLDERS) {
      const el = document.createElement('div');
      el.className = 'tpl-ph';
      el.dataset.key = key;
      el.tabIndex = 0;
      el.setAttribute('role', 'button');
      el.setAttribute('aria-label', `${label} placeholder. Arrow keys move it, shift and arrow keys resize it.`);
      el.innerHTML = `<i class="tpl-shape" aria-hidden="true"></i>`
        + `<span class="tpl-tag">${label}</span><i class="tpl-grip" aria-hidden="true"></i>`;
      el.addEventListener('pointerdown', (e) => this._grab(e, key));
      el.addEventListener('keydown', (e) => this._key(e, key));
      el.addEventListener('focus', () => this._select(key));
      root.append(el);
      this.handles.set(key, el);
    }
  },

  show() { this.open = true; this.root.hidden = false; this.refresh(); },
  hide() { this.open = false; if (this.root) this.root.hidden = true; },

  /**
   * Put every handle back where the design says it is.
   *
   * The photo window covers most of the card and the text boxes sit
   * inside it, so the order they stack in is not cosmetic: whichever is
   * on top is the only one that can be grabbed. Smallest on top means
   * every handle stays reachable, and a drag on bare card moves the
   * photo window, which is the one thing bare card belongs to.
   */
  refresh() {
    if (!this.root) return;
    const W = CONFIG.card.widthMm, H = CONFIG.card.heightMm;
    const boxes = [];
    for (const [key, el] of this.handles) {
      const b = TemplateStore.box(key);
      if (!b) { el.hidden = true; continue; }
      el.hidden = false;
      el.style.left = `${b.x / W * 100}%`;
      el.style.top = `${b.y / H * 100}%`;
      el.style.width = `${b.w / W * 100}%`;
      el.style.height = `${b.h / H * 100}%`;
      el.classList.toggle('on', key === this.selected);
      // the photo window is not a rectangle on the built-in card; show
      // the shape that will actually be cut, not just its bounding box
      if (key === 'photo') {
        const pts = TemplateStore.photoOutline()
          .map(([x, y]) => `${((x - b.x) / b.w * 100).toFixed(2)}% ${((y - b.y) / b.h * 100).toFixed(2)}%`);
        el.querySelector('.tpl-shape').style.clipPath = `polygon(${pts.join(', ')})`;
      }
      boxes.push({ el, area: b.w * b.h });
    }
    boxes.sort((a, b) => b.area - a.area);
    boxes.forEach(({ el }, i) => { el.style.zIndex = String(i + 1); });
  },

  /** What the selected placeholder is, and where: the panel's readout. */
  readout(key = this.selected) {
    const b = TemplateStore.box(key);
    if (!b) return '';
    const label = PLACEHOLDERS.find((h) => h.key === key)?.label ?? key;
    const n = (v) => v.toFixed(1);
    return `${label}: ${n(b.w)} × ${n(b.h)} mm, ${n(b.x)} mm from the left and ${n(b.y)} mm from the top.`;
  },

  _select(key) {
    this.selected = key;
    for (const [k, el] of this.handles) el.classList.toggle('on', k === key);
    this.onChange?.({ selected: key, moved: false });
  },

  _set(key, box) {
    TemplateStore.setBox(key, box);
    this.refresh();
    this.onChange?.({ selected: key, moved: true });
  },

  /* ---- pointer ---------------------------------------------------- */

  _grab(e, key) {
    if (e.button !== 0) return;
    const el = this.handles.get(key);
    const rect = this.root.getBoundingClientRect();
    if (!rect.width) return;
    const mmPerPxX = CONFIG.card.widthMm / rect.width;
    const mmPerPxY = CONFIG.card.heightMm / rect.height;
    const start = TemplateStore.box(key);
    const sizing = e.target.classList.contains('tpl-grip');
    const x0 = e.clientX, y0 = e.clientY;

    this._select(key);
    el.setPointerCapture(e.pointerId);
    el.classList.add('dragging');
    e.preventDefault();

    const move = (ev) => {
      const dx = (ev.clientX - x0) * mmPerPxX;
      const dy = (ev.clientY - y0) * mmPerPxY;
      this._set(key, sizing
        ? { ...start, w: Math.max(MIN_MM, start.w + dx), h: Math.max(MIN_MM, start.h + dy) }
        : { ...start, x: start.x + dx, y: start.y + dy });
    };
    const drop = () => {
      el.removeEventListener('pointermove', move);
      el.removeEventListener('pointerup', drop);
      el.removeEventListener('pointercancel', drop);
      el.classList.remove('dragging');
    };
    el.addEventListener('pointermove', move);
    el.addEventListener('pointerup', drop);
    el.addEventListener('pointercancel', drop);
  },

  /* ---- keyboard --------------------------------------------------- */

  _key(e, key) {
    const step = e.shiftKey ? NUDGE_MM : NUDGE_MM;
    const d = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] }[e.key];
    if (!d) return;
    e.preventDefault();
    const b = TemplateStore.box(key);
    this._set(key, e.shiftKey
      ? { ...b, w: Math.max(MIN_MM, b.w + d[0]), h: Math.max(MIN_MM, b.h + d[1]) }
      : { ...b, x: b.x + d[0], y: b.y + d[1] });
  },
};
