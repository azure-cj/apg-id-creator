/* ------------------------------------------------------------------
   Register of issued IDs
   ------------------------------------------------------------------
   An entry: { id: 'APG-2026-122', year, seq, name, title, issuedAt (ISO),
               replacement: true|false }

   Two layers:
     local   — this browser's localStorage. Always on. Instant.
     shared  — optional Google Sheet behind a Google Apps Script web app.
               Its URL is entered in the app (Issued IDs → Shared register)
               and kept per computer, so the capability URL never has to be
               committed to the source. Checked asynchronously; the local
               layer answers first and the shared answer refines it.

   The shared layer uses only "simple" requests (GET with a query string,
   POST with a text/plain body) because Apps Script web apps do not answer
   CORS preflights.
   ------------------------------------------------------------------ */

const Register = {
  /* ---- local ---- */
  _read() {
    try { return JSON.parse(localStorage.getItem(CONFIG.register.storageKey) || '[]'); }
    catch { return []; }
  },
  _write(list) {
    try { localStorage.setItem(CONFIG.register.storageKey, JSON.stringify(list)); } catch { /* storage blocked */ }
  },

  /** Newest first. */
  all() {
    return this._read().sort((a, b) => (b.issuedAt || '').localeCompare(a.issuedAt || ''));
  },

  /** The local entry for this number, or null. */
  find(year, seq) {
    if (seq === null) return null;
    const id = employeeId(year, seq);
    return this._read().find((e) => e.id === id) ?? null;
  },

  /** Insert or replace by id. */
  record(entry) {
    const list = this._read().filter((e) => e.id !== entry.id);
    list.push(entry);
    this._write(list);
  },

  /* ---- shared (optional) ---- */
  _url: null,
  get url() {
    if (this._url === null) {
      try { this._url = localStorage.getItem(CONFIG.register.urlStorageKey) || ''; } catch { this._url = ''; }
    }
    return this._url || CONFIG.register.url || '';
  },
  get shared() { return !!this.url; },

  /** Save (or clear, with '') the shared-register URL on this computer. */
  setUrl(url) {
    const clean = String(url || '').trim();
    this._url = clean;
    try {
      if (clean) localStorage.setItem(CONFIG.register.urlStorageKey, clean);
      else localStorage.removeItem(CONFIG.register.urlStorageKey);
    } catch { /* storage blocked: the URL lasts for this page only */ }
  },

  /** A web-app URL looks like https://script.google.com/macros/s/…/exec */
  urlLooksWrong(url) {
    const u = String(url || '').trim();
    if (!u) return '';
    const localhost = /^http:\/\/(localhost|127\.0\.0\.1)([:/]|$)/i.test(u);   // for testing a local stand-in
    if (!localhost && !/^https:\/\//i.test(u)) return 'The address must start with https://';
    if (/docs\.google\.com\/spreadsheets/i.test(u)) {
      return 'That is the spreadsheet itself. Deploy the Apps Script in it (register/README.md) and paste the web-app address, which ends in /exec.';
    }
    if (!/\/exec(\?|$)/i.test(u)) return 'A web-app address ends in /exec. Check Deploy → Manage deployments in the Apps Script editor.';
    return '';
  },

  /** Check the service is answering: { ok, rows } or { error }. */
  async ping(url = this.url) {
    if (!url) return { error: 'No address set.' };
    try {
      const data = await this._fetch(`${url}?action=ping`, { method: 'GET' });
      if (!data || data.service !== 'apg-id-register') return { error: 'That address answered, but it is not the ID register script.' };
      return { ok: true, rows: data.rows ?? 0, sheet: data.sheet ?? '' };
    } catch (e) {
      return { error: 'No answer from that address. Check that the deployment is set to “Anyone with the link” and that the address ends in /exec.' };
    }
  },

  async _fetch(url, init) {
    const ctrl = new AbortController();
    const t = setTimeout(() => ctrl.abort(), CONFIG.register.timeoutMs);
    try {
      const r = await fetch(url, { ...init, signal: ctrl.signal, redirect: 'follow' });
      if (!r.ok) throw new Error(`HTTP ${r.status}`);
      return await r.json();
    } finally { clearTimeout(t); }
  },

  /** Shared entry for this number: entry | null, or undefined when the shared register could not be reached. */
  async findShared(year, seq) {
    if (!this.shared || seq === null) return null;
    try {
      const data = await this._fetch(`${this.url}?action=find&id=${encodeURIComponent(employeeId(year, seq))}`, { method: 'GET' });
      return data && data.id ? data : null;
    } catch { return undefined; }
  },

  /** Record in the shared register. Returns true when it got there. */
  async recordShared(entry) {
    if (!this.shared) return false;
    try {
      const data = await this._fetch(this.url, {
        method: 'POST',
        headers: { 'Content-Type': 'text/plain;charset=utf-8' },
        body: JSON.stringify({ action: 'record', entry }),
      });
      return !!data?.ok;
    } catch { return false; }
  },

  /* ---- export ---- */
  csv() {
    const esc = (v) => `"${String(v ?? '').replace(/"/g, '""')}"`;
    const rows = [['id', 'year', 'seq', 'last', 'first', 'middle', 'suffix', 'name', 'title', 'issuedAt', 'replacement']];
    for (const e of this.all()) {
      rows.push([e.id, e.year, e.seq, e.last ?? '', e.first ?? '', e.middle ?? '', e.suffix ?? '',
                 e.name, e.title, e.issuedAt, e.replacement ? 'yes' : 'no']);
    }
    return '﻿' + rows.map((r) => r.map(esc).join(',')).join('\r\n');
  },
};

function formatIssuedAt(iso) {
  const d = new Date(iso);
  return isNaN(d) ? '' : d.toLocaleDateString('en-PH', { day: 'numeric', month: 'short', year: 'numeric' });
}
