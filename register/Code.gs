/**
 * Alpha Premier ID Maker — shared register (Google Apps Script)
 * ---------------------------------------------------------------
 * Backs the ID Maker's duplicate check with one Google Sheet so every
 * HR computer sees the same list of issued IDs.
 *
 * Deploy as a Web App (see README.md in this folder), then paste the
 * web-app URL into CONFIG.register.url in src/config.js and rebuild.
 *
 * API (JSON):
 *   GET  ?action=find&id=APG-2026-122   -> entry | null
 *   GET  ?action=list                   -> [entry, ...]
 *   POST body {"action":"record","entry":{...}}  -> {"ok":true}
 *
 * entry = { id, year, seq, name, title, issuedAt, replacement }
 */

var SHEET_NAME = 'Register';
var HEADERS = ['id', 'year', 'seq', 'name', 'title', 'issuedAt', 'replacement', 'recordedBy'];

function sheet_() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var sh = ss.getSheetByName(SHEET_NAME);
  if (!sh) {
    sh = ss.insertSheet(SHEET_NAME);
    sh.appendRow(HEADERS);
    sh.setFrozenRows(1);
  }
  return sh;
}

function rows_() {
  var sh = sheet_();
  var values = sh.getDataRange().getValues();
  var out = [];
  for (var i = 1; i < values.length; i++) {
    var r = values[i];
    if (!r[0]) continue;
    out.push({
      id: String(r[0]), year: Number(r[1]), seq: Number(r[2]), name: String(r[3]), title: String(r[4]),
      issuedAt: r[5] instanceof Date ? r[5].toISOString() : String(r[5]),
      replacement: r[6] === true || String(r[6]).toLowerCase() === 'yes' || String(r[6]) === 'true',
      recordedBy: String(r[7] || ''),
      _row: i + 1,
    });
  }
  return out;
}

function json_(obj) {
  return ContentService.createTextOutput(JSON.stringify(obj)).setMimeType(ContentService.MimeType.JSON);
}

function clean_(e) {
  delete e._row;
  return e;
}

function doGet(e) {
  var p = (e && e.parameter) || {};
  if (p.action === 'list') return json_(rows_().map(clean_));
  if (p.action === 'find') {
    var id = String(p.id || '').trim();
    var hit = rows_().filter(function (r) { return r.id === id; })[0] || null;
    return json_(hit ? clean_(hit) : null);
  }
  return json_({ ok: true, service: 'apg-id-register' });
}

function doPost(e) {
  var lock = LockService.getScriptLock();
  lock.waitLock(10000);
  try {
    var body = JSON.parse(e.postData.contents || '{}');
    if (body.action !== 'record' || !body.entry || !body.entry.id) return json_({ ok: false, error: 'bad request' });
    var en = body.entry;
    var sh = sheet_();
    var existing = rows_().filter(function (r) { return r.id === en.id; })[0];
    var line = [en.id, en.year, en.seq, en.name, en.title, en.issuedAt, en.replacement ? 'yes' : 'no', Session.getActiveUser().getEmail() || ''];
    if (existing) sh.getRange(existing._row, 1, 1, line.length).setValues([line]);
    else sh.appendRow(line);
    return json_({ ok: true });
  } finally {
    lock.releaseLock();
  }
}
