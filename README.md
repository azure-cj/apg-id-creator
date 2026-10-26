# Alpha Premier ID Maker

Makes the 54 × 85 mm employee ID card for Alpha Premier Group of Companies:
fill in a form, check the card on screen, download a two-page 300 DPI PDF
(front and back) ready for the card printer.

**To use it:** double-click `Alpha Premier ID Maker.html`. It opens in your
browser, works offline, and nothing leaves the computer — photos, names and
signatures stay in the browser tab until you download the PDF.

Step-by-step instructions for making an ID (including how the photo is
prepared) are in **PROCESS.md**.

## What is in this folder

| Path | What it is |
| --- | --- |
| `Alpha Premier ID Maker.html` | The app. One self-contained file: templates, fonts and code are all inside it. This is the only file HR needs. |
| `PROCESS.md` | The ID-making process end to end, with the photo pipeline explained. |
| `photo-prompt.txt` | The prompt to paste into Gemini / ChatGPT to turn any photo into a green-screen portrait. (The app also shows it under the Photo field.) |
| `register/` | Optional shared register: a Google Apps Script (`Code.gs`) and set-up notes, so every HR computer checks duplicates against one Google Sheet. |
| `assets/front-template.png` | Front of the card, 638 × 1004 px = 54 × 85 mm at 300 DPI. |
| `assets/back.png` | Back of the card (terms, contacts, CEO signature). Same size. |
| `assets/example-cut-out.png` | The cut-out used by "Show an example". |
| `assets/fonts/` | Poppins 700 (name), Montserrat 800 (title), Arimo 400 (employee number) as latin + latin-ext WOFF2 subsets. |
| `src/` | The readable source code. See "How the app is built" below. |
| `build.mjs` | Rebuilds `Alpha Premier ID Maker.html` from `src/` + `assets/`. |
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
  text-fit.js       shrinks and wraps the name/title so they fit their boxes
  signature.js      the signature pad + drawing strokes onto the card
  qr.js             the QR code square (uses vendor/qrcode-generator.js, MIT)
  card.js           draws the front and back at any resolution
  photo.js          the photo pipeline: background check, chroma key, head
                    detection, automatic placement
  pdf.js            writes the two-page PDF by hand (no library) and names the file
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
4. Open the rebuilt `Alpha Premier ID Maker.html` and check the card with
   "Show an example". Press Ctrl+Shift+G to overlay the photo-area and
   front-outline guides when re-tracing a new template.

Common edits:

| Want to… | Change |
| --- | --- |
| Move or resize the name / title / employee number | `CONFIG.text.*.box` (mm) and the `maxSizePt` / `minSizePt` / `maxLines` limits |
| Change where the photo sits or how big the head is | `CONFIG.photo` — `headTop`, `headCenterX`, `headWidth`, `reachY`; manual-adjust limits `nudgeStepMm`, `zoomMin/Max` |
| Change the QR link | `CONFIG.qr.content` |
| New template artwork | Replace `assets/front-template.png` / `assets/back.png` (keep 638 × 1004 px). If the band or the photo window moved, re-trace `CONFIG.frontOutline.polygons` and `CONFIG.photoArea.polygon` |
| Different number format | `CONFIG.employeeNo` (`prefix`, `digits`, `label`, years offered) |
| Share the issued-ID register between computers | `CONFIG.register.url` — see `register/README.md` |
| Reword the AI photo prompt | `PHOTO_PROMPT` in `config.js` and `photo-prompt.txt` |

### Working on the source without rebuilding

Browsers block fonts and canvas images loaded from `file://`, so serve the
folder over HTTP while developing, then open `http://localhost:8000/src/`:

```
# from this folder, any one of:
python -m http.server 8000
npx serve .
```

## Requirements

A current Chrome, Edge, Firefox or Safari. The PDF export uses the
browser's built-in `CompressionStream`; on a very old browser without it
the pages are stored as high-quality JPEG instead of lossless.

## Credits

Fonts: Poppins, Montserrat and Arimo (Open Font License / Apache 2.0).
QR code: qrcode-generator by Kazuhiko Arase (MIT). "QR Code" is a
registered trademark of DENSO WAVE INCORPORATED.
