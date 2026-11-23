// Copies the rows of the old register (the Google Sheet) into the new one
// (Firestore, in the company's Firebase project). Run once, by someone with
// a Keystone account, after the register's rules are deployed.
//
//   node register/migrate.mjs <Register.csv>            copy the rows
//   node register/migrate.mjs <Register.csv> --dry-run  only say what would be copied
//
// <Register.csv> is the sheet's "Register" tab saved from Google Sheets:
// File -> Download -> Comma-separated values (.csv).
//
// It asks for the email and password of a Keystone account, signs in the way
// the app does, and creates one document per number. It never changes or
// removes anything: a number already in the new register is left as it is
// and reported. A second row for the same number (a replacement card) goes
// under the first. Running it twice copies nothing twice.
//
// Needs Node 18 or later and nothing else. It is a script, not part of the
// app: the app has no Node in it. It is here because a one-off copy between
// two services is not worth a screen in the app.
//
// With FIREBASE_AUTH_EMULATOR_HOST and FIRESTORE_EMULATOR_HOST set (as
// `firebase emulators:exec` sets them) it talks to the emulators instead.

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { createInterface } from 'node:readline';

/** The project to copy into. It is not written in this repository: it comes from the environment, as it does for a release build. */
function project() {
  const projectId = (process.env.IDMAKER_FIREBASE_PROJECT_ID ?? '').trim();
  const apiKey = (process.env.IDMAKER_FIREBASE_API_KEY ?? '').trim();
  if (!projectId || !apiKey) throw new Error('Set IDMAKER_FIREBASE_PROJECT_ID and IDMAKER_FIREBASE_API_KEY first (the Firebase console: Project settings › Your apps › Web app).');
  return { projectId, apiKey, company: 'apg' };
}

function endpoints({ projectId, apiKey, company }) {
  const auth = process.env.FIREBASE_AUTH_EMULATOR_HOST;
  const store = process.env.FIRESTORE_EMULATOR_HOST;
  if (Boolean(auth) !== Boolean(store)) throw new Error('Set both FIREBASE_AUTH_EMULATOR_HOST and FIRESTORE_EMULATOR_HOST, or neither.');
  const signIn = auth
    ? `http://${auth}/identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=${encodeURIComponent(apiKey)}`
    : `https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=${encodeURIComponent(apiKey)}`;
  const base = store ? `http://${store}` : 'https://firestore.googleapis.com';
  return {
    signIn,
    list: `${base}/v1/projects/${encodeURIComponent(projectId)}/databases/(default)/documents/keystone/${encodeURIComponent(company)}/idreg`,
    where: auth ? `the emulators (${store})` : `the project ${projectId}`,
  };
}

/** Minimal RFC 4180 reader: quoted fields, doubled quotes, CR / LF / CRLF. The same rules as the app's (Register.cs, Csv.Parse). */
export function parseCsv(text) {
  const rows = [];
  let row = [];
  let field = '';
  let quoted = false;
  const s = text.startsWith('﻿') ? text.slice(1) : text;
  for (let i = 0; i < s.length; i++) {
    const c = s[i];
    if (quoted) {
      if (c === '"') {
        if (s[i + 1] === '"') { field += '"'; i++; } else quoted = false;
      } else field += c;
      continue;
    }
    if (c === '"') quoted = true;
    else if (c === ',') { row.push(field); field = ''; }
    else if (c === '\r') { /* the \n that follows ends the row */ }
    else if (c === '\n') { row.push(field); rows.push(row); row = []; field = ''; }
    else field += c;
  }
  if (field.length > 0 || row.length > 0) { row.push(field); rows.push(row); }
  return rows.filter((r) => r.some((v) => v.trim().length > 0));
}

/** The sheet's rows as register entries, oldest first. Rows without a number are counted and left out. */
export function entriesOf(csvText) {
  const rows = parseCsv(csvText);
  if (rows.length === 0) return { entries: [], skipped: [] };
  const head = rows[0].map((h) => h.trim().toLowerCase());
  if (!head.includes('id')) throw new Error('That file has no "id" column. Save the sheet\'s "Register" tab as CSV, header row included.');
  const cell = (r, name) => { const i = head.indexOf(name); return i >= 0 && i < r.length ? r[i].trim() : ''; };
  const entries = [];
  const skipped = [];
  rows.slice(1).forEach((r, at) => {
    const id = cell(r, 'id');
    const line = at + 2;
    if (!/^APG-\d{4}-\d{3,6}$/.test(id)) { skipped.push(`row ${line}: "${id}" is not an employee number`); return; }
    const [, year, seq] = /^APG-(\d{4})-(\d+)$/.exec(id);
    const name = cell(r, 'name');
    if (name.length === 0) { skipped.push(`row ${line}: ${id} has no name`); return; }
    const issued = cell(r, 'issuedat');
    const when = new Date(issued);
    entries.push({
      id, year: Number(cell(r, 'year')) || Number(year), seq: Number(cell(r, 'seq')) || Number(seq), name, title: cell(r, 'title'),
      // the sheet may hold a date cell rather than the app's own text; both become the app's form
      issuedAt: Number.isNaN(when.getTime()) ? issued : when.toISOString(),
      replacement: ['yes', 'true'].includes(cell(r, 'replacement').toLowerCase()),
      last: cell(r, 'last'), first: cell(r, 'first'), middle: cell(r, 'middle'), suffix: cell(r, 'suffix'),
      role1: cell(r, 'role1'), role2: cell(r, 'role2'),
    });
  });
  entries.sort((a, b) => a.issuedAt.localeCompare(b.issuedAt));
  return { entries, skipped };
}

const text = (v) => ({ stringValue: v });
const whole = (v) => ({ integerValue: String(v) });

function documentOf(e, replacement, who) {
  return {
    fields: {
      id: text(e.id), year: whole(e.year), seq: whole(e.seq), name: text(e.name), title: text(e.title), issuedAt: text(e.issuedAt),
      replacement: { booleanValue: replacement },
      last: text(e.last), first: text(e.first), middle: text(e.middle), suffix: text(e.suffix), role1: text(e.role1), role2: text(e.role2),
      by: text(who.uid), byEmail: text(who.email),
    },
  };
}

const replacementName = (e) => e.issuedAt.replace(/[^A-Za-z0-9]/g, '') || 'undated';

async function ask(question, hidden = false) {
  const rl = createInterface({ input: process.stdin, output: process.stdout, terminal: true });
  if (hidden) {
    // show the question, then nothing of what is typed
    rl._writeToOutput = (s) => { if (s.includes(question)) rl.output.write(question); };
  }
  const answer = await new Promise((resolve) => rl.question(question, resolve));
  rl.close();
  if (hidden) process.stdout.write('\n');
  return answer;
}

async function signIn(url, email, password) {
  const res = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email, password, returnSecureToken: true }) });
  const body = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(`Signing in was refused (${body?.error?.message ?? `HTTP ${res.status}`}). Check the email and password.`);
  return { token: body.idToken, uid: body.localId, email: body.email ?? email };
}

/** Every number in the new register. */
async function numbersIn(list, token) {
  const ids = [];
  let page = '';
  do {
    const res = await fetch(`${list}?pageSize=300${page ? `&pageToken=${encodeURIComponent(page)}` : ''}`, { headers: { Authorization: `Bearer ${token}` } });
    if (res.status === 403) throw new Error('This account may not use the ID register. Ask the CEO to add it in Keystone (Settings › User Accounts), and check the register\'s rules are deployed.');
    if (!res.ok) throw new Error(`The register answered with an error (HTTP ${res.status}).`);
    const body = await res.json();
    for (const d of body.documents ?? []) ids.push(d.name.slice(d.name.lastIndexOf('/') + 1));
    page = body.nextPageToken ?? '';
  } while (page);
  return ids;
}

async function create(url, token, document) {
  const res = await fetch(url, { method: 'POST', headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' }, body: JSON.stringify(document) });
  return res.status;
}

async function exists(url, token) {
  return (await fetch(url, { headers: { Authorization: `Bearer ${token}` } })).status === 200;
}

export async function main(argv, credentials) {
  const dry = argv.includes('--dry-run');
  const file = argv.find((a) => !a.startsWith('--'));
  if (!file) {
    console.error('Usage: node register/migrate.mjs <Register.csv> [--dry-run]');
    return 2;
  }
  const { entries, skipped } = entriesOf(readFileSync(file, 'utf8'));
  const urls = endpoints(project());
  console.log(`Rows in the sheet: ${entries.length}${skipped.length ? ` (and ${skipped.length} left out)` : ''}`);
  for (const s of skipped) console.log(`  left out, ${s}`);

  const email = credentials?.email ?? await ask('Keystone email: ');
  const password = credentials?.password ?? await ask('Password: ', true);
  const who = await signIn(urls.signIn, email.trim(), password);

  const before = await numbersIn(urls.list, who.token);
  console.log(`Numbers in the new register (${urls.where}) before: ${before.length}`);

  const taken = new Set(before);
  let copied = 0;
  let replacements = 0;
  let already = 0;
  const failed = [];
  for (const e of entries) {
    const number = encodeURIComponent(e.id);
    if (!taken.has(e.id)) {
      if (dry) { console.log(`  would copy ${e.id}  ${e.name}`); taken.add(e.id); copied++; continue; }
      const status = await create(`${urls.list}?documentId=${number}`, who.token, documentOf(e, e.replacement, who));
      if (status === 200) { console.log(`  copied ${e.id}  ${e.name}`); taken.add(e.id); copied++; continue; }
      if (status !== 409 && !(status === 403 && await exists(`${urls.list}/${number}`, who.token))) { failed.push(`${e.id} (HTTP ${status})`); continue; }
      taken.add(e.id);      // somebody issued it in the new register meanwhile
    }
    if (!e.replacement) { console.log(`  already there, left as it is: ${e.id}`); already++; continue; }
    // a later row for a number that is already there: a replacement card, kept under the first
    const name = replacementName(e);
    const under = `${urls.list}/${number}/replacements`;
    if (dry) { console.log(`  would copy ${e.id}  ${e.name} (replacement)`); replacements++; continue; }
    const status = await create(`${under}?documentId=${encodeURIComponent(name)}`, who.token, documentOf(e, true, who));
    if (status === 200) { console.log(`  copied ${e.id}  ${e.name} (replacement)`); replacements++; }
    else if (status === 409 || (status === 403 && await exists(`${under}/${encodeURIComponent(name)}`, who.token))) { console.log(`  already there, left as it is: ${e.id} (replacement)`); already++; }
    else failed.push(`${e.id} replacement (HTTP ${status})`);
  }

  const after = dry ? before : await numbersIn(urls.list, who.token);
  console.log(`Numbers in the new register after: ${after.length}${dry ? ' (nothing was written: --dry-run)' : ''}`);
  console.log(`${dry ? 'Would copy' : 'Copied'}: ${copied} number${copied === 1 ? '' : 's'}, ${replacements} replacement${replacements === 1 ? '' : 's'}. Already there: ${already}. Failed: ${failed.length}.`);
  for (const f of failed) console.log(`  failed: ${f}`);
  const missing = dry ? [] : [...new Set(entries.map((e) => e.id))].filter((id) => !after.includes(id));
  if (missing.length > 0) console.log(`NOT in the new register: ${missing.join(', ')}`);
  else if (!dry) console.log('Every number in the sheet is in the new register.');
  return failed.length === 0 && missing.length === 0 ? 0 : 1;
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  main(process.argv.slice(2)).then((code) => { process.exitCode = code; }, (e) => { console.error(e.message); process.exitCode = 1; });
}
