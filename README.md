# Alpha Premier ID Maker

Makes the 54 × 85.6 mm (CR80) employee ID card for Alpha Premier Group of Companies.
Six short steps — name, job title, employee number, photo, signature, then a
check — and out comes a 300 DPI print file: a sheet of cards with crop
marks, front and back side by side, ready to cut out and stick together —
or, if you would rather print on both sides of the paper, a page of fronts
and a page of backs laid out to meet. Several people can go on one sheet.

**To use it:** double-click `Alpha Premier ID Maker.html`. It opens in your
browser, works offline, and nothing leaves the computer — photos, names and
signatures stay in the browser tab until you download the PDF.

Step-by-step instructions for making an ID (including how the photo is
prepared) are in **PROCESS.md**.

## What is in this folder

| Path | What it is |
| --- | --- |
| `Alpha Premier ID Maker.html` | The app. One self-contained file: templates, fonts and code are all inside it. This is the only file HR needs. |
| `PROCESS.md` | The ID-making process end to end, including batches, with the photo pipeline explained. |
| `BACKLOG.md` | Ideas considered and not built yet, and why. |
| `PHOTO-SPEC.md` | What the photo pipeline actually does, measured — including its known limits. |
| `photo-prompt.txt` | The prompt to paste into Gemini / ChatGPT to turn any photo into a green-screen portrait. (The app also shows it under the Photo field.) |
| `register/` | Optional shared register: a Google Apps Script (`Code.gs`) and set-up notes, so every HR computer checks duplicates against one Google Sheet. |
| `assets/front-template.png` | Front of the card, 638 × 1004 px = 54 × 85 mm at 300 DPI. |
| `assets/back.png` | Back of the card (terms, contacts, CEO signature). Same size. |
| `assets/example-cut-out.png` | The cut-out the sample card is built from. It has no button in the app; it is what `npm test` renders. |
| `assets/logo.png` | The peacock mark in the app's header — lifted from the card artwork, so the screen and the print agree. |
| `assets/fonts/` | Poppins 700 (name), Montserrat 800 (title), Arimo 400 (employee number) as latin + latin-ext WOFF2 subsets. |
| `src/` | The readable source code. See "How the app is built" below. |
| `build.mjs` | Rebuilds `Alpha Premier ID Maker.html` from `src/` + `assets/`. |
| `serve.mjs` | `npm start` — serves the folder on `http://localhost:8000`. No dependencies. |
| `test/golden.mjs` | Renders the example card at 300 DPI and compares it with `test/golden-front.png`, so a code or config change cannot silently move anything. |
| `package.json` | `npm run build`, `npm test`, `npm run dev`. |

## How the app is built

The app is plain HTML, CSS and JavaScript — no framework, no build tools
other than the small `build.mjs` script, and no network calls (the Google
Fonts link only styles the form; the card fonts are embedded).

```
src/
  index.html        page markup (also the development page)
  styles.css        appearance
  assets.dev.js     asset paths for development; the build swaps in base64 data URLs
  config.js         EVERY measurement: card size, text boxes, photo placement, QR,
                    signature box, employee-number format, register URL, the AI photo prompt
  register.js       issued-ID register: local (localStorage) + optional shared Sheet
  titles.js         the job-title list, plus custom titles this computer has used
  batch.js          CSV batch issuing: parsing, file matching, per-row checks
  text-fit.js       shrinks and wraps the name/title so they fit their boxes
  signature.js      the signature pad + drawing strokes onto the card
  qr.js             the QR code square (uses vendor/qrcode-generator.js, MIT)
  card.js           draws the front and back at any resolution
  photo.js          the photo pipeline: background check, chroma key, head
                    detection, automatic placement
  pdf.js            writes the PDF by hand (no library), any number of pages, and names the file
  app.js            form state, validation, live preview, review dialog
  vendor/qrcode-generator.js
```

Every module is a classic script sharing one global scope, in the order
listed in `index.html`. The card is drawn with the Canvas 2D API by
`drawFront()` / `drawBack()` in `card.js`; the on-screen preview calls them
at screen resolution and the PDF calls them at 300 DPI, so the preview and
the print file are the same drawing.

### Changing something

1. Edit the file in `src/` (most changes are numbers in `config.js`).
2. Run `node build.mjs` in this folder (Node 18+; nothing to install).
3. Run `npm test` (first time: `npm install`, which fetches Playwright and
   a headless Chromium). It renders the example card and diffs it against
   `test/golden-front.png`; any moved pixel fails with a magenta diff image
   in `test/diff-front.png`. If the change was intended, accept it with
   `npm test -- --update` and commit the new golden.
4. Open the rebuilt `Alpha Premier ID Maker.html` with **`#example`** on
   the end of the address to fill the form with the sample card and jump
   to the finished preview. Press Ctrl+Shift+G there to overlay the
   photo-area and front-outline guides when re-tracing a new template.
   Neither has a button: HR never needs them, and the fewer things in that
   panel the better.

Common edits:

| Want to… | Change |
| --- | --- |
| Move or resize the name / title / employee number | `CONFIG.text.*.box` (mm) and the `maxSizePt` / `minSizePt` / `maxLines` limits |
| Print the name surname-first, or show the middle name in full | `CONFIG.name` — `cardFormat` and `middle` |
| Stop capitalising names typed in lower case | `CONFIG.name.autoCapitalise` → `false` |
| Add or remove the job titles everyone sees | `CONFIG.titles.list` |
| How two roles are merged on the card | `CONFIG.titles.mergeJoiner` / `pairJoiner` |
| Change where the photo sits or how big the head is | `CONFIG.photo` — `headTop`, `headCenterX`, `headWidth`, `reachY`; manual-adjust limits `nudgeStepMm`, `zoomMin/Max` |
| Change the QR link | `CONFIG.qr.content` |
| New template artwork | Replace `assets/front-template.png` / `assets/back.png` (keep 638 × 1004 px). If the band or the photo window moved, re-trace `CONFIG.frontOutline.polygons` and `CONFIG.photoArea.polygon` |
| Different number format | `CONFIG.employeeNo` (`prefix`, `digits`, `label`, years offered) |
| Paper sizes, margins, gutters, crop marks | `CONFIG.sheet` |
| The card's physical size | `CONFIG.card` — currently CR80, 54 × 85.6 mm |
| Restyle the app after a rebrand | The tokens at the top of `src/styles.css` (`--gold`, `--gold-strong`, `--tray`, the fonts) and `assets/logo.png`. None of them touch the printed card. |
| Bump the version stamped into PDFs | `APP_VERSION` in `config.js` (and `package.json`) |
| Share the issued-ID register between computers | Nothing in the code: paste the web-app address into the app (Issued IDs → Shared register) — see `register/README.md` |
| Reword the AI photo prompt | `PHOTO_PROMPT` in `config.js` and `photo-prompt.txt` |

### Running it on this computer

Double-clicking `Alpha Premier ID Maker.html` is enough for normal use. To
serve it over `http://localhost` instead — needed to work on the files in
`src/`, and handy for testing:

```
npm start                 http://localhost:8000/
npm start -- --port 9000  if 8000 is taken
npm start -- --lan        also reachable from other computers on the office
                          network (Windows will ask to allow Node through
                          the firewall — allow it for private networks)
```

`/` serves the built single file; `/src/` serves the development page,
which loads the stylesheet, scripts and assets separately so you can edit
and just refresh. `serve.mjs` has no dependencies.

Note that the browser keeps the issued-ID register per address, so cards
issued from the double-clicked file and from `http://localhost` are kept in
separate lists. The shared Google Sheet register (see `register/README.md`)
is not affected.

## How it looks

The screen takes its colours from **alphapremiergroup.com**: brand gold
`#D4AF37`, black panels, and Plus Jakarta Sans (the fonts come from Google
Fonts; without a connection the app falls back to the system typeface and
everything still works). Gold is used for fills and on black. On white it
is only 2.1:1 against the background — too faint to read — so gold *text*
on a light background uses a darker `#8A6A14`, which passes WCAG AA.

**The printed card is not affected by any of this.** Its colours come from
the template artwork, and `npm test` fails if a single pixel of it moves.

## Data protection

- **No photograph ever leaves the computer from this app, and none is
  stored.** The photo lives in the browser tab until the card is
  downloaded, then it is gone. Re-issuing a lost card means sourcing the
  photograph again — that is deliberate, so there is no store of employee
  images to protect.
- **The one third-party step is manual.** HR is instructed to put the
  employee's photo through Gemini or ChatGPT to get a green-screen
  portrait. This **must** be done on the company's approved **enterprise**
  AI account, never a personal login. The app states this but cannot
  enforce it.
- **The register holds no images** — only the ID number, name, job title
  and issue date. With the shared Google Sheet it also records which Google
  account issued each card.
- The app is for **HR use on a desktop browser**. Employee self-service is
  not supported.

`PHOTO-SPEC.md` has the full measured behaviour of the photo pipeline.

## Requirements

A current Chrome, Edge, Firefox or Safari. The PDF export uses the
browser's built-in `CompressionStream`; on a very old browser without it
the pages are stored as high-quality JPEG instead of lossless.

## Credits

Fonts: Poppins, Montserrat and Arimo (Open Font License / Apache 2.0).
QR code: qrcode-generator by Kazuhiko Arase (MIT). "QR Code" is a
registered trademark of DENSO WAVE INCORPORATED.
