#!/usr/bin/env node
/* ------------------------------------------------------------------
   What's new, the version and updates
   ------------------------------------------------------------------
   Under the tools on the left: the version, and in the desktop app
   "Check for updates". With nothing newer it says the app is up to
   date; with a newer version it offers to install it, and What's new
   shows what that version brings.

   What's new lists src/whats-new.ts, this version first. On the first
   start after an update it opens by itself, once, and the update line
   says "Updated to version …". Not over a card in progress: then a
   "New" badge waits on the rail instead.

   The desktop app is imitated with a stand-in window.__TAURI__ whose
   check_update answers as told, so this runs in a plain browser.

     npm run test:news
   ------------------------------------------------------------------ */
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { readFileSync } from 'node:fs';

const here = dirname(fileURLToPath(import.meta.url));
const app = pathToFileURL(join(here, '..', 'Alpha Premier ID Maker.html')).href;
const WHATS_NEW = new Function(`${readFileSync(join(here, '..', 'src', 'whats-new.ts'), 'utf8')}\nreturn WHATS_NEW;`)();
const APP_VERSION = /APP_VERSION\s*=\s*'([^']+)'/.exec(readFileSync(join(here, '..', 'src', 'config.ts'), 'utf8'))[1];

const pw = await import(process.env.PLAYWRIGHT_MODULE || 'playwright');
const chromium = pw.chromium ?? pw.default.chromium;
const browser = await chromium.launch();
const errors = [];
const fail = [];
const check = (ok, what) => { console.log(`${ok ? '  ok  ' : ' FAIL '} ${what}`); if (!ok) fail.push(what); };

/**
 * A page of the app. `seen`: the version the app last ran as (null: never).
 * `desktop`: what check_update answers — 'none', 'error', or an update.
 */
async function open({ seen = null, issued = false, draft = false, desktop = null } = {}) {
  const context = await browser.newContext({ viewport: { width: 1366, height: 768 } });
  const page = await context.newPage();
  page.on('pageerror', (e) => errors.push(e.message));
  await page.route(/^https?:/, (r) => r.abort());
  await page.addInitScript(({ seen, issued, draft, desktop }) => {
    if (sessionStorage.getItem('primed')) return;      // set things up once, not on every reload
    sessionStorage.setItem('primed', '1');
    if (seen) localStorage.setItem('apg-id-seen-version', seen);
    if (issued) localStorage.setItem('apg-id-register', JSON.stringify([{ id: 'APG-2026-001', year: 2026, seq: 1, name: 'A', title: 'B', issuedAt: '2026-11-01T00:00:00Z' }]));
    if (draft) sessionStorage.setItem('apg-id-draft', JSON.stringify({ last: 'Santos', first: 'Ana' }));
    if (!desktop) return;
    window.__TAURI__ = {
      core: {
        invoke: async (cmd) => {
          if (cmd === 'check_update') {
            if (desktop === 'error') throw new Error('offline');
            return desktop === 'none' ? null : desktop;
          }
          return cmd === 'pen_tablets' ? [] : null;
        },
        Channel: class { onmessage() {} },
      },
    };
  }, { seen, issued, draft, desktop });
  await page.goto(app);
  await page.waitForSelector('#card-box:not([hidden])', { timeout: 20000 });
  await page.waitForTimeout(300);
  return { page, close: () => context.close() };
}

const look = (page) => page.evaluate(() => ({
  version: document.getElementById('app-version').textContent,
  updateShown: !document.getElementById('update').hidden,
  button: document.getElementById('btn-update').textContent,
  status: document.getElementById('update-status').textContent,
  state: document.getElementById('update').dataset.state,
  tool: state.tool,
  badge: !document.getElementById('rail-news-new').hidden,
  first: document.querySelector('#news-list .news-release h3')?.textContent,
  items: document.querySelector('#news-list .news-release ul')?.children.length,
  next: document.querySelector('#news-list .news-next h3')?.textContent ?? null,
  nextItems: document.querySelector('#news-list .news-next ul')?.children.length ?? 0,
}));

check(WHATS_NEW[0].version === APP_VERSION, `whats-new.ts starts with this version (${WHATS_NEW[0].version} / ${APP_VERSION})`);

/* ---- a browser, first time ----------------------------------------- */
{
  const { page, close } = await open();
  let s = await look(page);
  check(s.version === `Version ${APP_VERSION}`, `the version is on the left: "${s.version}"`);
  check(await page.evaluate(() => document.getElementById('app-version').closest('.rail') !== null), 'it sits under the tools, not in a footer');
  check(!s.updateShown, 'a browser shows no "Check for updates"');
  check(s.tool === null && !s.badge, 'a first start opens nothing and shows no "New"');
  await page.click('#rail-news');
  s = await look(page);
  check(s.tool === 'news' && s.first === `Version ${APP_VERSION}This version`, `What's new opens with this version first: "${s.first}"`);
  check(s.items === WHATS_NEW[0].items.length, `with every line of its notes (${s.items})`);
  check(await page.evaluate(() => document.querySelectorAll('#news-list .news-release').length) === WHATS_NEW.length, 'and every earlier version under it');
  await close();
}

/* ---- the first start after an update --------------------------------- */
{
  const { page, close } = await open({ seen: '1.20.0', desktop: 'none' });
  let s = await look(page);
  check(s.tool === 'news', 'after an update, What\'s new opens by itself');
  check(/Updated to version/.test(s.status), `and the update line says so: "${s.status}"`);
  await page.waitForTimeout(4500);                     // the quiet check at start-up
  s = await look(page);
  check(s.state === 'current' && s.status === `Updated to version ${APP_VERSION}. The app is up to date.`, `then: "${s.status}"`);
  await page.reload();
  await page.waitForSelector('#card-box:not([hidden])', { timeout: 20000 });
  await page.waitForTimeout(300);
  s = await look(page);
  check(s.tool === null && !/Updated/.test(s.status), 'the next start opens nothing and no longer says "Updated"');
  await close();
}

/* ---- 1.20 kept no version, but it kept the issued-ID list ------------ */
{
  const { page, close } = await open({ issued: true });
  check((await look(page)).tool === 'news', 'coming from 1.20 (an issued-ID list, no version noted) counts as an update');
  await close();
}

/* ---- not over a card in progress ------------------------------------ */
{
  const { page, close } = await open({ seen: '1.20.0', draft: true });
  let s = await look(page);
  check(s.tool === null && s.badge, 'with a card in progress it does not open; "New" waits on the rail');
  await page.click('#rail-news');
  await page.click('#drawer-close');
  s = await look(page);
  check(!s.badge, 'opening What\'s new once clears "New"');
  await close();
}

/* ---- Check for updates: nothing newer -------------------------------- */
{
  const { page, close } = await open({ desktop: 'none', seen: APP_VERSION });
  let s = await look(page);
  check(s.updateShown && s.button === 'Check for updates', 'the desktop app shows "Check for updates" on the left');
  await page.click('#btn-update');
  await page.waitForTimeout(300);
  s = await look(page);
  check(s.state === 'current' && s.status === 'The app is up to date.', `nothing newer: "${s.status}"`);
  await close();
}

/* ---- Check for updates: a newer version ------------------------------ */
{
  const offer = { version: '9.9.0', current: APP_VERSION, notes: 'First new thing\nSecond new thing' };
  const { page, close } = await open({ desktop: offer, seen: APP_VERSION });
  await page.click('#btn-update');
  await page.waitForTimeout(300);
  let s = await look(page);
  check(s.state === 'available' && s.button === 'Install version 9.9.0 and restart', `a newer version is offered: "${s.button}"`);
  await page.click('#rail-news');
  s = await look(page);
  check(s.next === 'Version 9.9.0 is ready to install' && s.nextItems === 2, `What's new shows what it brings first (${s.next}, ${s.nextItems} lines)`);
  await close();
}

/* ---- Check for updates: no answer ------------------------------------ */
{
  const { page, close } = await open({ desktop: 'error', seen: APP_VERSION });
  await page.click('#btn-update');
  await page.waitForTimeout(300);
  const s = await look(page);
  check(s.state === 'error' && /Could not check/.test(s.status), `no answer says so: "${s.status.slice(0, 40)}…"`);
  await close();
}

await browser.close();
if (errors.length) { console.error('\npage errors:'); errors.forEach((e) => console.error('  ' + e)); }
if (fail.length || errors.length) {
  console.error(`\nFAIL — ${fail.length} check(s) failed${errors.length ? `, ${errors.length} page error(s)` : ''}`);
  process.exit(1);
}
console.log('\nPASS — the version and updates sit under the tools, and What\'s new says what changed');
