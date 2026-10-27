#!/usr/bin/env node
/* ------------------------------------------------------------------
   Builds the zip that HR send to each other.

     node desktop/pack.mjs

   Run `node build.mjs` first if anything in src/ changed: this packs
   the built single file, not the sources.
   ------------------------------------------------------------------ */
import { execFileSync } from 'node:child_process';
import { mkdirSync, rmSync, copyFileSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, '..');
const app = join(root, 'Alpha Premier ID Maker.html');
if (!existsSync(app)) { console.error('Run `node build.mjs` first.'); process.exit(1); }

const stage = join(here, 'dist', 'Alpha Premier ID Maker');
rmSync(join(here, 'dist'), { recursive: true, force: true });
mkdirSync(stage, { recursive: true });

for (const f of ['install.cmd', 'uninstall.cmd', 'shortcut.ps1', 'app.ico']) {
  copyFileSync(join(here, f), join(stage, f));
}
copyFileSync(app, join(stage, 'Alpha Premier ID Maker.html'));

const out = join(here, 'dist', 'Alpha Premier ID Maker - install.zip');
try {
  execFileSync('zip', ['-r', '-q', out, 'Alpha Premier ID Maker'], { cwd: join(here, 'dist') });
} catch {
  console.error('`zip` is not available here. The folder is ready at:\n  ' + stage + '\nZip it yourself and send that.');
  process.exit(1);
}
console.log('wrote ' + out);
