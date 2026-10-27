#!/usr/bin/env node
/* ------------------------------------------------------------------
   Builds the zip that HR send to each other.

     node desktop/pack.mjs

   Run `node build.mjs` first if anything in src/ changed: this packs
   the built single file, not the sources.
   ------------------------------------------------------------------ */
import { mkdirSync, rmSync, copyFileSync, existsSync, readFileSync, writeFileSync, readdirSync } from 'node:fs';
import { zipSync } from './zip.mjs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, '..');
const app = join(root, 'Alpha Premier ID Maker.html');
if (!existsSync(app)) { console.error('Run `node build.mjs` first.'); process.exit(1); }

const stage = join(here, 'dist', 'Alpha Premier ID Maker');
rmSync(join(here, 'dist'), { recursive: true, force: true });
mkdirSync(stage, { recursive: true });

for (const f of ['install.cmd', 'uninstall.cmd', 'setup.ps1', 'app.ico']) {
  copyFileSync(join(here, f), join(stage, f));
}
copyFileSync(app, join(stage, 'Alpha Premier ID Maker.html'));

// the installer stamps this into the Windows entry, so what Settings >
// Apps reports is the version actually installed rather than whatever was
// typed into a batch file months ago
const cfg = readFileSync(join(root, 'src', 'config.js'), 'utf8');
const version = /APP_VERSION\s*=\s*'([^']+)'/.exec(cfg)?.[1];
if (!version) { console.error('No APP_VERSION in src/config.js'); process.exit(1); }
writeFileSync(join(stage, 'version.txt'), version);

const out = join(here, 'dist', 'Alpha Premier ID Maker - install.zip');
const folder = 'Alpha Premier ID Maker';
writeFileSync(out, zipSync(
  readdirSync(stage).sort().map((name) => ({
    name: `${folder}/${name}`,
    data: readFileSync(join(stage, name)),
  })),
));
console.log(`wrote ${out}  (version ${version})`);
