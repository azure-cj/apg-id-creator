/* ------------------------------------------------------------------
   Register of issued IDs
   ------------------------------------------------------------------
   An entry: { id: 'APG-2026-122', year, seq, name, title, issuedAt (ISO),
               replacement: true|false }

   Two layers:
     local   — this browser's localStorage. Always on. Instant.
     shared  — optional Google Sheet behind a Google Apps Script web app
               (CONFIG.register.url). Checked asynchronously; the local
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
  get shared() { return !!CONFIG.register.url; },

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
      const data = await this._fetch(`${CONFIG.register.url}?action=find&id=${encodeURIComponent(employeeId(year, seq))}`, { method: 'GET' });
      return data && data.id ? data : null;
    } catch { return undefined; }
  },

  /** Record in the shared register. Returns true when it got there. */
  async recordShared(entry) {
    if (!this.shared) return false;
    try {
      const data = await this._fetch(CONFIG.register.url, {
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
    const rows = [['id', 'year', 'seq', 'name', 'title', 'issuedAt', 'replacement']];
    for (const e of this.all()) rows.push([e.id, e.year, e.seq, e.name, e.title, e.issuedAt, e.replacement ? 'yes' : 'no']);
    return '﻿' + rows.map((r) => r.map(esc).join(',')).join('\r\n');
  },
};

function formatIssuedAt(iso) {
  const d = new Date(iso);
  return isNaN(d) ? '' : d.toLocaleDateString('en-PH', { day: 'numeric', month: 'short', year: 'numeric' });
}
