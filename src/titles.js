/* ------------------------------------------------------------------
   Job titles
   ------------------------------------------------------------------
   CONFIG.titles.list is the company list. Anything HR types under
   "Other…" is remembered on that computer and offered next time, so the
   list grows into whatever the company actually uses without anyone
   editing the source.
   ------------------------------------------------------------------ */

const Titles = {
  _extra: null,

  _read() {
    if (this._extra === null) {
      try { this._extra = JSON.parse(localStorage.getItem(CONFIG.titles.storageKey) || '[]'); }
      catch { this._extra = []; }
      if (!Array.isArray(this._extra)) this._extra = [];
    }
    return this._extra;
  },

  _write(list) {
    this._extra = list;
    try { localStorage.setItem(CONFIG.titles.storageKey, JSON.stringify(list)); } catch { /* storage blocked */ }
  },

  /** Is this one of the titles that ship with the app? */
  isStandard(title) {
    const t = collapseSpaces(title).toLocaleLowerCase('en-US');
    return CONFIG.titles.list.some((x) => x.toLocaleLowerCase('en-US') === t);
  },

  /** The company list first, then titles this computer has used before. */
  all() {
    return [...CONFIG.titles.list, ...this._read()];
  },

  /** Remember a title typed under "Other…". Standard ones are ignored. */
  remember(title) {
    const t = collapseSpaces(title);
    if (!t || this.isStandard(t)) return;
    const lower = t.toLocaleLowerCase('en-US');
    const list = this._read().filter((x) => x.toLocaleLowerCase('en-US') !== lower);
    list.unshift(t);
    this._write(list.slice(0, CONFIG.titles.maxRemembered));
  },

  /** Drop a remembered title (not the company ones). */
  forget(title) {
    const lower = collapseSpaces(title).toLocaleLowerCase('en-US');
    this._write(this._read().filter((x) => x.toLocaleLowerCase('en-US') !== lower));
  },
};
