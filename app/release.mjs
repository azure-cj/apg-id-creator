#!/usr/bin/env node
/* ------------------------------------------------------------------
   Build a release that installed copies can update to
   ------------------------------------------------------------------
     cd app
     npm run release

   1. builds the app (../build.mjs) and the signed installer (tauri build)
   2. puts two files in app/release/, named the way GitHub will serve them:
        Alpha.Premier.ID.Maker_<version>_x64-setup.exe   the installer
        latest.json                                       what the app reads
   3. says what to do with them

   Every installed copy asks
     https://github.com/azure-cj/apg-id-creator/releases/latest/download/latest.json
   so the release must be the repository's **Latest** release: not a
   draft, and not ticked as a pre-release.

   Signing: the installer is signed with the private key made by
     npx tauri signer generate -w "%USERPROFILE%\.tauri\apg-id-maker.key"
   and installed copies refuse anything not signed with it. The key is
   read from TAURI_SIGNING_PRIVATE_KEY if set, otherwise from that path.
   If it has a password, set TAURI_SIGNING_PRIVATE_KEY_PASSWORD in the
   terminal first. Lose the key and installed copies can never update
   again — keep a backup somewhere that is not this laptop.
   ------------------------------------------------------------------ */
import { execSync } from 'node:child_process';
import { copyFileSync, existsSync, mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { homedir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, '..');
const REPO = 'azure-cj/apg-id-creator';
const die = (msg) => { console.error(`\n${msg}\n`); process.exit(1); };

// one version everywhere, or the app would offer itself an update forever
const version = JSON.parse(readFileSync(join(here, 'src-tauri', 'tauri.conf.json'), 'utf8')).version;
const appVersion = /APP_VERSION\s*=\s*'([^']+)'/.exec(readFileSync(join(root, 'src', 'config.ts'), 'utf8'))?.[1];
if (appVersion !== version) die(`Version mismatch: tauri.conf.json says ${version}, src/config.ts says ${appVersion}.`);

// what this release brings, from the notes the app itself shows
// (src/whats-new.ts is plain JavaScript, so it can be run as it is)
const WHATS_NEW = new Function(`${readFileSync(join(root, 'src', 'whats-new.ts'), 'utf8')}\nreturn WHATS_NEW;`)();
if (WHATS_NEW[0]?.version !== version) die(`src/whats-new.ts has no notes for ${version} yet. Add them at the top, then release.`);

if (!process.env.TAURI_SIGNING_PRIVATE_KEY) {
  const keyFile = join(homedir(), '.tauri', 'apg-id-maker.key');
  if (!existsSync(keyFile)) die(`No signing key. Make one first (see the top of app/release.mjs):\n  npx tauri signer generate -w "${keyFile}"`);
  process.env.TAURI_SIGNING_PRIVATE_KEY = readFileSync(keyFile, 'utf8');
}

console.log(`\nBuilding ${version}…\n`);
execSync('node build.mjs', { cwd: root, stdio: 'inherit' });
execSync('npx tauri build', { cwd: here, stdio: 'inherit', env: process.env });

const nsis = join(here, 'src-tauri', 'target', 'release', 'bundle', 'nsis');
const exe = readdirSync(nsis).find((f) => f.endsWith(`_${version}_x64-setup.exe`));
if (!exe || !existsSync(join(nsis, `${exe}.sig`))) die(`No signed installer for ${version} in ${nsis}.`);

// GitHub turns spaces in an uploaded file's name into dots; name it that
// way here so the address in latest.json is the address GitHub serves
const asset = exe.replace(/ /g, '.');
const out = join(here, 'release');
rmSync(out, { recursive: true, force: true });
mkdirSync(out);
copyFileSync(join(nsis, exe), join(out, asset));

const target = {
  signature: readFileSync(join(nsis, `${exe}.sig`), 'utf8').trim(),
  url: `https://github.com/${REPO}/releases/latest/download/${asset}`,
};
writeFileSync(join(out, 'latest.json'), `${JSON.stringify({
  version,
  notes: WHATS_NEW[0].items.join('\n'),
  pub_date: new Date().toISOString(),
  platforms: { 'windows-x86_64': target, 'windows-x86_64-nsis': target },
}, null, 2)}\n`);

console.log(`
Release ${version} is ready in app\\release\\:
  ${asset}
  latest.json

On GitHub (https://github.com/${REPO}/releases/new):
  1. Tag: v${version}   Title: ${version}
  2. Attach BOTH files above.
  3. Leave "Set as a pre-release" UNticked, and publish.
Installed copies see it the next time they start or press "Check for updates".
`);
