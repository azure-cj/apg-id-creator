# The Tauri window

This is the desktop window around the ID Maker. It draws nothing itself:
the card, the photo pipeline and the PDF all come from the one HTML file
built at the top of this repo, exactly as they do in a browser.

It replaced `desktop/shell/`, a WinForms + WebView2 window written in C#
(removed 4 Nov 2026; see git history).
On Windows **Tauri renders in WebView2 too**, so the engine under the card
has not changed — only the window around it. The printed card is governed
by the same `npm test` as before.

This is step one of moving to Tauri + React + TypeScript. The app inside
the window is still the plain-JavaScript one; porting it to React is a
separate piece of work, and nothing here has to change when that happens.

---

## What you need on the laptop, once

The C# shell needed the .NET SDK. Tauri needs Rust instead, and Rust on
Windows needs Microsoft's C++ linker. Budget about **4 GB** and half an
hour. Both are free and neither needs administrator rights beyond the
normal installer prompt.

1. **Visual Studio Build Tools** — <https://visualstudio.microsoft.com/visual-cpp-build-tools/>
   In the installer tick **Desktop development with C++**. Nothing else.
   (If Visual Studio itself is already installed with that workload, skip
   this.)
2. **Rust** — <https://rustup.rs>. Take the default option (1), which
   installs the `stable-x86_64-pc-windows-msvc` toolchain.
3. Close and reopen the terminal, then check both:
   ```
   rustc --version
   cargo --version
   ```
4. **WebView2 Runtime** is already on this laptop (the old app used it),
   and the installer this produces fetches it on any machine that lacks
   it. Nothing to do.

Node is already installed — it is what builds the HTML file.

---

## Building a release

Releases are **signed**, so installed copies can update themselves and
refuse anything that is not genuinely from us. Once, on the machine that
builds releases, make the signing key (it asks for a password; choose
one and keep it):

```
cd app
npm install
npx tauri signer generate -w "%USERPROFILE%\.tauri\apg-id-maker.key"
```

It prints a **public key**. That goes in `src-tauri/tauri.conf.json`
under `plugins.updater.pubkey`. The private key and its password stay
on this machine — **back both up somewhere else**. Lose them and every
installed copy has to be reinstalled by hand.

Then, for every release (set the password in the same terminal first):

```
$env:TAURI_SIGNING_PRIVATE_KEY_PASSWORD = "<the key's password>"
npm run release
```

`npm run release` builds the app, builds and signs the installer, and
leaves two files in `app\release\`:

| File | What it is |
| --- | --- |
| `Alpha.Premier.ID.Maker_<version>_x64-setup.exe` | The installer HR runs |
| `latest.json` | What installed copies read to learn there is a new version |

Publish both on GitHub as a release tagged `v<version>`, **not** ticked as
a pre-release. Installed copies look at the repository's *Latest*
release when they start, and when "Check for updates" is pressed at the
bottom of the window.

The first `npm run release` is slow — Rust compiles Tauri from source,
which takes several minutes. Later builds take seconds. Plain
`npm run build` no longer works on its own: the build signs the
installer, so it needs the key that `npm run release` passes it.

To run the window without building an installer:

```
npm run dev
```

---

## Updates

At start-up the window asks GitHub for
`releases/latest/download/latest.json`. If it names a newer version,
the footer says so and offers **Install version … and restart**.
Installing downloads the installer, checks its signature against the
public key built into the app, and runs it without questions
(`installMode: passive`). The app closes and the new version opens; the
issued-ID list, printer setting and card design are kept. A card in
progress is asked about first.

Offline, the start-up check says nothing. Pressing **Check for updates**
when it cannot get an answer says so, and suggests trying again later.

---

## What the window does

Everything the C# shell did, for the same reasons:

| | |
| --- | --- |
| **A real origin** | The app is served from `http://tauri.localhost`, not from a `file://` path. A `file://` page has no real origin, which makes browser storage fragile and cross-site requests — the shared register among them — behave unlike anywhere the app was tested. |
| **Its own browser data** | The issued-ID list, the register address, the printer settings and any custom card design belong to this app, not to whoever is signed in to Edge. |
| **Downloads land somewhere visible** | The PDFs, the batch ZIP and the register CSV are saved and then **shown in Explorer**. Saving them silently would leave the operator with no idea where the print went. |
| **Links open in a real browser** | A link to somewhere else opens in Edge or Chrome. An application window that can be navigated away from is no longer an application window. |
| **DevTools stay on** | Deliberately, same as before: it is how a problem on this laptop gets diagnosed, and nobody finds it by accident. It needs the `devtools` Cargo feature, which is already set in `Cargo.toml`. |

---

## One thing to do before switching over

**The issued-ID list on this laptop will look empty in the new app.**

Browser storage belongs to an origin, and the origin changes from
`https://idmaker.alphapremier.local` (the old shell's made-up host name)
to `http://tauri.localhost` (Tauri's). There is no way to carry it
across: one origin cannot read another's storage, which is the whole
point of the rule.

What this does and does not cost:

- **The shared register sheet is untouched.** It is the real record, and
  the duplicate check reads it, so issuing cards carries on working
  normally from the first minute.
- **The local list is a convenience copy.** Download it from the old app
  first — *Issued IDs → Download register (CSV)* — if you want to keep it.
- **Printer setting and any custom card design reset.** Both are a few
  clicks to set again.

---

## Files

| Path | What it is |
| --- | --- |
| `src-tauri/src/main.rs` | The window: where the app may navigate, where downloads go, and letting the cursor go on focus loss and exit. |
| `src-tauri/src/desktop.rs` | What the page can ask of the window: update check and install, the list of pen tablets, holding the cursor in the signature pad. |
| `release.mjs` | `npm run release`: the signed installer and `latest.json`. |
| `src-tauri/tauri.conf.json` | Window size, icon, installer settings. Keep `version` in step with `APP_VERSION` in `src/config.ts` (`npm run release` refuses if they differ). `plugins.updater` holds the update address and the public key. |
| `src-tauri/Cargo.toml` | Rust dependencies. Also carries a version to keep in step. |
| `sync-web.mjs` | Copies the built HTML into `web/index.html`. Runs automatically before every dev run and build, so the window can never show a stale copy. |
| `web/` | That copy. Generated, not committed. |
