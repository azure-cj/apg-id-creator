/* ------------------------------------------------------------------
   Batch issuing from a CSV
   ------------------------------------------------------------------
   HR picks a CSV, the photo files and (optionally) scanned signature
   files. Each row is validated and rendered exactly like a single card;
   the result is ONE PDF — either two pages per person, or A4 sheets of
   nine cards with crop marks — plus a report of what was skipped.

   CSV columns (header row required, order free, extra columns ignored):
     last, first, title, seq     required
     middle, suffix              optional parts of the name
     name                        accepted instead of last/first for older
                                 lists: printed as given, surname guessed
                                 for the file name
     year                        optional, defaults to this year
     photo                       file name of the photo
     signature                   file name of a scanned signature (optional)
     replacement                 yes/true to allow an already-issued number

   Rows are never half-issued: a row that fails is skipped and reported,
   and only the rows that made it into the PDF are written to the register.
   ------------------------------------------------------------------ */

const BATCH_MAX_ROWS = 200;

/* ---- CSV --------------------------------------------------------- */

// Minimal RFC-4180 parser: quoted fields, doubled quotes, CR/LF/CRLF.
function parseCsv(text) {
  const rows = [];
  let row = [], field = '', quoted = false;
  const s = text.replace(/^﻿/, '');
  for (let i = 0; i < s.length; i++) {
    const c = s[i];
    if (quoted) {
      if (c === '"') {
        if (s[i + 1] === '"') { field += '"'; i++; } else quoted = false;
      } else field += c;
      continue;
    }
    if (c === '"') { quoted = true; continue; }
    if (c === ',') { row.push(field); field = ''; continue; }
    if (c === '\r') continue;
    if (c === '\n') { row.push(field); rows.push(row); row = []; field = ''; continue; }
    field += c;
  }
  if (field.length || row.length) { row.push(field); rows.push(row); }
  return rows.filter((r) => r.some((v) => v.trim() !== ''));
}

function csvToObjects(text) {
  const rows = parseCsv(text);
  if (!rows.length) return { error: 'This CSV is empty.' };
  const headers = rows[0].map((h) => h.trim().toLowerCase());
  for (const need of ['title', 'seq']) {
    if (!headers.includes(need)) return { error: `The CSV needs a “${need}” column. Found: ${headers.join(', ')}.` };
  }
  if (!headers.includes('name') && !(headers.includes('last') && headers.includes('first'))) {
    return { error: `The CSV needs “last” and “first” columns (or a single “name” column). Found: ${headers.join(', ')}.` };
  }
  const out = rows.slice(1).map((r, i) => {
    const o = { _line: i + 2 };
    headers.forEach((h, c) => { o[h] = (r[c] ?? '').trim(); });
    return o;
  });
  if (!out.length) return { error: 'The CSV has a header row but no people in it.' };
  if (out.length > BATCH_MAX_ROWS) return { error: `That is ${out.length} rows. Please split it into batches of ${BATCH_MAX_ROWS} or fewer.` };
  return { rows: out };
}

const CSV_TEMPLATE = 'last,first,middle,suffix,title,seq,year,photo,signature,replacement\r\n'
  + 'Aureo,"Christopher Joseph",Villanueva,,"IT Associate",122,2026,aureo.png,aureo-sign.jpg,\r\n'
  + 'Reyes,Ana,Santos,,"HR Associate",123,2026,reyes.png,,\r\n'
  + '"dela Cruz",Juan,Santos,Jr.,Driver,124,2026,cruz.png,,\r\n';

/* ---- matching uploaded files ------------------------------------- */

// Files are matched on the file name, ignoring case, folders and — as a
// fallback — the extension, so "Aureo.JPG" finds "aureo.png".
function fileIndex(fileList) {
  const byName = new Map(), byStem = new Map();
  for (const f of fileList ?? []) {
    const base = f.name.split(/[\\/]/).pop().toLowerCase();
    byName.set(base, f);
    const stem = base.replace(/\.[^.]+$/, '');
    if (!byStem.has(stem)) byStem.set(stem, f);
  }
  return (wanted) => {
    const w = String(wanted || '').split(/[\\/]/).pop().trim().toLowerCase();
    if (!w) return null;
    return byName.get(w) ?? byStem.get(w.replace(/\.[^.]+$/, '')) ?? null;
  };
}

/* ---- one row ----------------------------------------------------- */

async function prepareBatchRow(row, findPhoto, findSignature, seen) {
  const fail = (problem) => ({ ok: false, problem });
  const title = collapseSpaces(row.title || '');

  // either separate parts, or a whole name from an older list
  const parts = {
    last: collapseSpaces(row.last || ''),
    first: collapseSpaces(row.first || ''),
    middle: collapseSpaces(row.middle || ''),
    suffix: collapseSpaces(row.suffix || ''),
  };
  const whole = collapseSpaces(row.name || '');
  const split = !!(parts.last || parts.first);
  if (split && !parts.last) return fail('No last name.');
  if (split && !parts.first) return fail('No first name.');
  if (!split && !whole) return fail('No name.');
  const name = split ? composeName(parts) : whole;
  if (!/^[\p{L}\p{M} .,'’-]+$/u.test(name)) return fail('The name has characters the card can’t print.');
  if (!title) return fail('No job title.');

  const seq = /^\d{1,4}$/.test(row.seq) && Number(row.seq) > 0 ? Number(row.seq) : null;
  if (seq === null) return fail(`“${row.seq}” is not an employee number (1–4 digits).`);
  const year = /^\d{4}$/.test(row.year || '') ? Number(row.year) : new Date().getFullYear();
  const id = employeeId(year, seq);

  if (seen.has(id)) return fail(`${id} appears twice in this CSV.`);
  const allowReplacement = /^(yes|y|true|1)$/i.test(row.replacement || '');
  const already = Register.find(year, seq) ?? (Register.shared ? await Register.findShared(year, seq) : null);
  if (already && !allowReplacement) {
    return fail(`${id} is already issued to ${already.name}. Put “yes” in the replacement column to re-print it.`);
  }

  // text must fit the card
  const probe = { name, title, year, seq, photo: null, signature: null };
  const layout = layoutText(probe);
  if (!layout.name.fits) return fail('The name is too long for the card.');
  if (!layout.title.fits) return fail('The job title is too long for the card.');

  const photoFile = findPhoto(row.photo || name);  // fall back to a file named after the person
  if (!photoFile) return fail(row.photo ? `No photo file named “${row.photo}”.` : 'No photo column and no file matching the name.');
  const photo = await processPhotoFile(photoFile);
  if ('error' in photo) return fail(`${photoFile.name}: ${photo.error}`);
  if (!photo.check.ok) return fail(`${photoFile.name}: ${photo.check.problem}`);

  let signature = null;
  if (row.signature) {
    const sigFile = findSignature(row.signature);
    if (!sigFile) return fail(`No signature file named “${row.signature}”.`);
    const sig = await signatureFromImage(sigFile);
    if ('error' in sig) return fail(`${sigFile.name}: ${sig.error}`);
    signature = sig;
  }

  seen.add(id);
  return {
    ok: true,
    id, year, seq, name, title, parts: split ? parts : null,
    warning: photo.check.warning,
    noSignature: !signature,
    data: { name, title, year, seq, photo: { image: photo.image, placement: photo.check.placement }, signature },
    entry: {
      id, year, seq, name, title,
      last: split ? parts.last : lastNameForFile(whole),
      first: split ? parts.first : '', middle: split ? parts.middle : '', suffix: split ? parts.suffix : '',
      issuedAt: new Date().toISOString(), replacement: !!already,
    },
  };
}

/* ---- the whole batch --------------------------------------------- */

/**
 * runBatch({ csvText, photos, signatures, templates, rotateBack, layout,
 *            onProgress }) -> { error } | { results, pages, fileName, title }
 * `layout` is 'cards' (two pages per person) or 'sheets' (A4, 9 per sheet).
 */
async function runBatch({ csvText, photos, signatures, templates, rotateBack, layout, onProgress }) {
  const parsed = csvToObjects(csvText);
  if (parsed.error) return { error: parsed.error };

  const findPhoto = fileIndex(photos);
  const findSignature = fileIndex(signatures);
  const seen = new Set();
  const results = [];
  for (const row of parsed.rows) {
    onProgress?.('check', results.length + 1, parsed.rows.length);
    const r = await prepareBatchRow(row, findPhoto, findSignature, seen);
    const label = collapseSpaces(row.name || [row.last, row.first].filter(Boolean).join(', ')) || `line ${row._line}`;
    results.push({ line: row._line, label, ...r });
  }

  const good = results.filter((r) => r.ok);
  if (!good.length) return { results, error: 'None of the rows could be used — see the list below.' };

  // Render each card once; the PDF writer pulls them in page order.
  const pages = [];
  if (layout === 'sheets') {
    const per = sheetCapacity();
    for (let i = 0; i < good.length; i += per) {
      const group = good.slice(i, i + per);
      pages.push(() => composeSheet(group.map((r) => renderSideCanvas(templates, 'front', r.data, false)), { mirrorColumns: false }));
      pages.push(() => composeSheet(group.map((r) => renderSideCanvas(templates, 'back', r.data, rotateBack)), { mirrorColumns: true }));
    }
    for (const p of pages) { p.widthMm = CONFIG.sheet.widthMm; p.heightMm = CONFIG.sheet.heightMm; }
  } else {
    for (const r of good) {
      pages.push(() => renderSideCanvas(templates, 'front', r.data, false));
      pages.push(() => renderSideCanvas(templates, 'back', r.data, rotateBack));
    }
  }

  const stamp = new Date().toISOString().slice(0, 10);
  return {
    results,
    good,
    pages,
    fileName: layout === 'sheets' ? `apg-id-sheets-${stamp}.pdf` : `apg-id-batch-${stamp}.pdf`,
    title: `Alpha Premier IDs — ${good.length} card${good.length === 1 ? '' : 's'} — ${stamp}`,
  };
}

// A page thunk may carry its own page size; buildPdf reads it off the
// returned object, so wrap the sheet thunks to pass it through.
function withPageSize(thunk) {
  if (!thunk.widthMm) return thunk;
  return async () => ({ canvas: await thunk(), widthMm: thunk.widthMm, heightMm: thunk.heightMm });
}
