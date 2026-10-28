# Developing the ID Maker

Everything technical. The front page is for the people who use the app;
this is for whoever changes it.

The card-making procedure, the photo pipeline and the design decisions
behind them are in **PROCESS.md**. What the photo pipeline measurably does
and does not do is in **PHOTO-SPEC.md**.

## What is in this folder

| Path | What it is |
| --- | --- |
| `Alpha Premier ID Maker.html` | The app. One self-contained file: templates, fonts and code are all inside it. This is the only file HR needs. |
| `PROCESS.md` | The ID-making process end to end, including batches, with the photo pipeline explained. |
| `BACKLOG.md` | Ideas considered and not built yet, and why. |
| `PHOTO-SPEC.md` | What the photo pipeline actually does, measured — including its known limits. |
| `photo-prompt.txt` | The prompt to paste into Gemini / ChatGPT to turn any photo into a green-screen portrait. (The app also shows it under the Photo field.) |
| `desktop/` | Giving HR the app to install: a per-user installer that needs no admin rights and no signing, and a WebView2 shell for when a real `.exe` is wanted. See `desktop/README.md`. |
| `register/` | Optional shared register: a Google Apps Script (`Code.gs`) and set-up notes, so every HR computer checks duplicates against one Google Sheet. |
| `assets/front-template.png` | Front of the card, 638 × 1004 px = 54 × 85 mm at 300 DPI. |
| `assets/back.png` | Back of the card (terms, contacts, CEO signature). Same size. |
| `assets/example-cut-out.png` | The cut-out the sample card is built from. It has no button in the app; it is what `npm test` renders. |
| `TEMPLATE-SPEC.md` | The brief for whoever designs a new card: canvas size, the zones the app draws into, and the rules that break it. Hand this to the designer. |
| `assets/template-guide.png` | 638 × 1011 transparent overlay of those zones, to lock as a layer in Canva and design underneath. |
| `assets/template-guide-annotated.png` | The same zones over the current card, labelled, at 2×. |
| `assets/logo.png` | The peacock mark in the app's header — lifted from the card artwork, so the screen and the print agree. |
| `assets/fonts/` | Poppins 700 (name), Montserrat 800 (title), Arimo 400 (employee number) as latin + latin-ext WOFF2 subsets. |
| `src/` | The readable source code. See "How the app is built" below. |
| `build.mjs` | Rebuilds `Alpha Premier ID Maker.html` from `src/` + `assets/`. |
| `serve.mjs` | `npm start` — serves the folder on `http://localhost:8000`. No dependencies. |
| `test/map.mjs` | `npm run test:map` — builds a map from `config.js`, reads it back, and checks the zones land where they started, that the card still renders, and that the contrast check refuses artwork the text would vanish on. |
| `test/golden.mjs` | Renders the example card at 300 DPI and compares it with `test/golden-front.png`, so a code or config change cannot silently move anything. |
| `test/design.mjs` | `npm run test:design` — moves the map and puts different artwork in the way HR would, and checks that opening the map changes nothing, that the built-in photo cut-out keeps its step when dragged, that the wrong shape is refused, that new artwork reaches the printed card, that a drag moves what is drawn by the amount dragged, and that it all survives a restart. |
| `test/layout.mjs` | `npm run test:layout` — opens the app at four window sizes in six states and checks that nothing inside the preview tray reaches past it and that the page never scrolls sideways. A column that will not shrink draws the preview across the form; it looks like a z-index problem and it never is. |
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
  template-map.js   reads a colour-coded map PNG into masks, boxes and the
                    head target, so a template's layout can come from the
                    artwork instead of from hand-measured numbers
  card.js           draws the front and back at any resolution
  template-store.js the design in use: the map (where every zone sits) and,
                    optionally, uploaded artwork. Keeps both in localStorage
                    and falls back to the built-in card
  template-edit.js  the drag-and-resize handles laid over the preview
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
   `npm test -- --update` and commit the new golden. `npm run test:map` and
   `npm run test:design` cover the other two ways a card's layout can
   arrive, and `npm run test:layout` checks the window itself; run all
   four before a release. On a machine where Playwright is
   installed somewhere else, point the tests at it with
   `PLAYWRIGHT_MODULE=/path/to/playwright/index.js`.
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
| Move a zone, just for this computer | Nothing here: **Card design → Show the map** in the tool rail. It edits the built-in card's zones as readily as uploaded artwork's |
| New card artwork, just for this computer | Nothing here: **Card design → Use different artwork** takes the two PNGs. See `TEMPLATE-SPEC.md` |
| New card artwork, built into the app | Replace `assets/front-template.png` / `assets/back.png` (keep 638 × 1004 px). If the band or the photo window moved, re-trace `CONFIG.frontOutline.polygons` and `CONFIG.photoArea.polygon`, or ship a map |
| Different number format | `CONFIG.employeeNo` (`prefix`, `digits`, `label`, years offered) |
| Paper sizes, margins, gutters, crop marks | `CONFIG.sheet` |
| The card's physical size | `CONFIG.card` — currently CR80, 54 × 85.6 mm |
| Restyle the app after a rebrand | The tokens at the top of `src/styles.css` (`--gold`, `--gold-strong`, `--tray`, the fonts) and `assets/logo.png`. None of them touch the printed card. |
| Bump the version stamped into PDFs | `APP_VERSION` in `config.js` (and `package.json`) |
| Point the register at a different Google Sheet | `CONFIG.register.url` — the Apps Script web-app address. Treat it as a password; see `register/README.md` |
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

The window is four columns: a rail of the four occasional tools, whichever
one it has open, the card, and the form. The card's size comes from the
window's height, so the whole job fits on one screen without scrolling and
grows on a larger monitor. The four tools used to be collapsed panels
stacked under the form, which put them off the bottom of the screen and
made them read as steps of the card being made.

Three things about that grid are load-bearing, and all three failed the
same way — a box that would not shrink, drawn across the form.

- Every column is placed by name (`.rail`, `.drawer`, `.stage`, `.panel`
  each set `grid-column`). A closed drawer is `display: none`, and
  auto-placement would slide the form into the card's column and leave an
  empty track on the right.
- `.card-box` is width-led with a height cap, never the reverse. An
  explicit height plus an `aspect-ratio` makes a box that cannot shrink.
- Every grid inside the tray uses `minmax(0, 1fr)`, not the implicit
  `auto` track — `.stage`, `.queue-strip`, `.job-list`. An `auto` track
  never goes below its content's min-content, so the sheet preview's
  natural 1123px and a row of nowrap text each carried the tray out over
  the form. `test/layout.mjs` exists because this happened twice.

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
