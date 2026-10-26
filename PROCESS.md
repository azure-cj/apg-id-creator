# Making an Alpha Premier ID — the process

From a new hire's details to a printed card. Part 1 is the day-to-day
procedure. Part 2 explains what the app does with the photo and the rest of
the card, for whoever maintains it.

---

## Part 1 — Procedure (HR / admin)

**You need:** the new hire's full name and job title, the employee number
HR assigned, a photo of them, and the person present to sign (or a signed
file you can trace — see step 5).

### 1. Open the app

Double-click `Alpha Premier ID Maker.html`. It opens in the browser. Wait for
the card template to appear on the left.

### 2. Name, title, number

| Field | Rule |
| --- | --- |
| Full name | Exactly as it should read on the card. Capitals are kept as typed (`Christopher Joseph V. Aureo`, `Juan dela Cruz`). Letters, spaces, periods, commas, hyphens and apostrophes only. |
| Job title | Printed in capitals. If the card says "too long", shorten it (`Sr. HR Business Partner`, not `Senior Human Resources Business Partner`). |
| Employee number | Pick the year of issue (defaults to this year), then type the 1–4 digit sequence HR assigned. The card prints `APG - <year> - <number>` with the number padded to 3 digits (`7` → `007`). The app checks the number against the **register** of cards already issued — on this computer, and in the shared Google Sheet if one is set up (see `register/README.md`). A number that is already taken turns the field red and names the holder. If this really is a re-print of the same person's card (lost, damaged, name change), tick **Replacement card**; otherwise get the right number from HR. |

The preview updates as you type. The name shrinks and wraps onto up to three
lines by itself; the title onto two.

### 3. Prepare the photo

The app needs a **chest-up, forward-facing cut-out**: either

* a PNG with a real transparent background, or
* any photo on a **solid, pure green background** (`#00FF00`) — the app
  removes the green itself.

Most phone photos have a normal background, so turn them into a green-screen
portrait first:

1. Open Gemini or ChatGPT **using the company's approved account** (employee
   photos are personal data).
2. Attach the photo.
3. Paste the prompt from `photo-prompt.txt` (also under "Photo prompt for
   Gemini or ChatGPT" in the app → *Copy prompt*) and send it.
4. **Look at the result.** The face must still look like the person — same
   features, skin tone, hair, glasses, expression. If it does not, ask again
   or use a different original photo. Never accept a "beautified" face.
5. Download it as PNG.

Tips:

* If the person is wearing green, change `#00FF00` to `#0000FF` (solid
  blue) in the prompt; the app removes blue the same way.
* The photo must show the whole head with a little space above the hair,
  and enough of the chest/shoulders that the body runs down behind the dark
  band on the card. Head-only crops are scaled up and warned about.
* Bigger is better: at least 1200 px tall. Below ~180 px head width the app
  warns that the face may print soft.
* Not accepted: photos with a background still in them, and "transparent"
  PNGs that actually have a grey checkerboard painted in. The app tells you
  which problem it found.

### 4. Upload the photo

Click **Upload photo** (or drag the file onto the Photo box). The app checks
it, removes the green, finds the head and places it: head 15 mm wide,
centred under the gold diagonal, top of the head 15.4 mm from the card top.
The message under the button says what happened:

* *Green background removed · Placed automatically* — good.
* An amber warning — usable, but read it (small photo, tight crop, head
  touching the edge).
* A red message — the photo was rejected; it says why and what to upload
  instead.

### 5. Signature

The new hire signs in the box with a finger, pen or mouse. **Clear** starts
over. The signature is trimmed, fitted into its slot under the title and
printed in white. A slow, deliberate signature prints thicker; a fast one
thinner, like a real pen.

### 6. Review & Generate

When the status line reads *Everything is filled in*, click **Review &
Generate**. Check both sides against the list in the dialog:

* name spelled correctly, capitals right;
* title and employee number right;
* the photo looks like the person and is not blurry;
* the signature is visible below the title.

**Back to edit** returns to the form. **Download PDF** saves
`APG-<year>-<number>_<Lastname>.pdf` (e.g. `APG-2026-122_Aureo.pdf`) to the
browser's download folder and records the card in the register (the
example card is never recorded). Keep the PDF with the employee's HR record.

The **Issued IDs** panel under the form lists what this computer has
issued; **Download register (CSV)** exports it for HR's records.

### 7. Print

Open the PDF and print it on the card printer:

* **100 % scale / actual size** — never "fit to page";
* **both sides** (page 1 front, page 2 back);
* page size is the card itself, 54 × 85 mm, 300 DPI.

If the printer flips the back upside down, open **Printer setting** at the
bottom of the form and tick **Turn the back 180°**. The browser remembers
this on that computer.

### 8. Next card

Click **Start a new ID**. Everything is cleared, including the photo and
signature.

---

## Part 2 — What the app does (technical)

### 2.1 Coordinates

Everything on the card is positioned in **millimetres from the top-left
corner** (`src/config.js`). The drawing code converts with one number,
*pixels per mm*: `clientWidth / 54` for the preview, `300 / 25.4 = 11.81`
for the print file. Text sizes are in points (1 pt = 0.3528 mm).

### 2.2 Photo pipeline (`src/photo.js`)

```
file ──► validate ──► decode ──► classify background
                                   │
             ┌─────────────────────┼─────────────────────┐
             ▼                     ▼                     ▼
        transparent          green / blue          checkerboard
             │                     │                 or other
             │               chroma key                 │
             │                     │                 reject
             └──────────┬──────────┘
                        ▼
              find head on 800 px copy
                        ▼
              compute placement (mm)
                        ▼
          { image, placement, warning | problem }
```

1. **Validate.** PNG, JPG or WebP, at most 15 MB.

2. **Decode and limit size.** The image is drawn onto a canvas no taller
   than 2400 px (`WORKING_MAX_H`). That is still 5× the card's 300 DPI
   height, so nothing is lost for printing, and the per-pixel passes stay
   fast.

3. **Classify the background** (`classifyBackground`). Pixels are sampled
   along the border only — top two rows, bottom row, left and right
   columns, every *(w+h)/400*-th pixel — and counted:

   | Border pixels | Verdict |
   | --- | --- |
   | > 40 % alpha < 20 | `transparent` — a real cut-out |
   | > 50 % with G > 120 and G − max(R,B) > 60 | `green` |
   | > 50 % with B > 120 and B − max(R,G) > 60 | `blue` |
   | > 60 % light grey (channels within 16, R > 170) **in two distinct brightness levels** | `checkerboard` — a fake transparency, rejected |
   | anything else | `other` — background still there, rejected |

4. **Chroma key** (`chromaKey`), only for `green`/`blue`. For every pixel,
   *dominance* = key channel − max(other two):

   | dominance | action |
   | --- | --- |
   | ≤ 0 | untouched |
   | 0 – 30 | **de-spill only**: the key channel is clamped to the max of the other two, removing green/blue cast on hair edges and reflections without changing alpha |
   | 30 – 110 | de-spill, and alpha fades linearly from 100 % to 0 % |
   | ≥ 110 | fully transparent |

   This is done in place on the 2400 px working image, which becomes the
   image drawn on the card.

5. **Find the head** (`findHeadAndPlace`), on a copy no taller than 800 px
   so the scan is quick. It works purely from the alpha silhouette:

   * Sanity check: at least 8 % of all pixels transparent and at least 50 %
     of the top row transparent, else "still has its background".
   * For each row: first and last column with alpha ≥ 128 and the count of
     such pixels. A row counts as "person" if it has at least 1 % of the
     width (min 2 px) solid.
   * **Top of head** = first person row.
   * Walking down, the row width grows to a maximum — the **head width** —
     then, once it has dropped below 85 % of that maximum (and we are more
     than 3 rows below the top), we are past the widest part of the head.
     From there the narrowest row is the **neck**; the first row wider than
     1.5 × head width is the **shoulders**. No neck or no shoulders → "head
     and shoulders aren't both visible".
   * **Head centre X** = mean centre of all rows between top and neck that
     are at least 80 % of head width (ignores a tilted ear or stray hair).
   * Rejections: head width under 40 px (face too small to see); top row
     ≤ 1 (head cut off at the top).
   * Warnings (photo still accepted): head width under 180 px (prints a bit
     soft); a head row touching the left or right edge.

6. **Placement** (mm, into `data.photo.placement`). The scale is chosen so
   the detected head is exactly `headWidth` = 15 mm wide on the card, with
   the head top at `headTop` = 15.4 mm and the head centre at
   `headCenterX` = 36.6 mm. If, at that scale, the bottom of the photo would
   end above `reachY` = 66 mm (the body would stop before the dark band and
   leave a visible edge), the scale is increased until it reaches; if that
   enlarges the head by more than 8 % the user is warned that the photo is
   cropped close.

7. **Drawing** (`drawFront` in `src/card.js`). The photo is clipped to the
   `photoArea` polygon (so it never covers the logo on the left or runs past
   the thin black edge on the right), then the template is redrawn clipped
   to the `frontOutline` polygons so the dark band and the gold line sit in
   front of the shoulders.

### 2.3 Text (`src/text-fit.js`)

For each box the fitter tries sizes from `maxSizePt` down to `minSizePt` in
¼-pt steps. At each size it first tries one line; then 2…`maxLines` lines
using a *balanced* break (the split whose widest line is narrowest, so a
name becomes "Christopher / Joseph V. Aureo" rather than "Christopher Joseph
V. / Aureo"). The block height is cap-height + (lines − 1) × line-height +
descender, measured from the actual font. If nothing fits even at the
minimum size, the text is drawn anyway and the form shows "too long".
Fitting is always measured at 300 DPI so the preview and the PDF break lines
identically.

### 2.4 Signature (`src/signature.js`)

Strokes are recorded as raw pointer positions. When drawn: points closer
than 0.5 px are dropped, two passes of a 1-2-1 smoothing kernel remove
jitter, segments become cubic curves (Catmull-Rom tangents), and the line
width follows pen speed — from `strokeMm` 0.38 mm when slow to `minStrokeMm`
0.16 mm when fast, relative to the signer's median point spacing, with a
low-pass filter so width changes are gradual. The ink's bounding box is
scaled to fit the 17 × 5.6 mm slot without stretching and centred.

### 2.5 QR code (`src/qr.js`)

Content `https://alphapremiergroup.com/`, error correction M. Drawn into an
8.8 mm white square at (2, 66.1) mm with a 2-module quiet zone; module edges
are snapped to whole 300 DPI pixels so the print is crisp.

### 2.6 PDF (`src/pdf.js`)

Both sides are rendered to 638 × 1004 px canvases (54 × 85 mm at 300 DPI)
and written into a hand-built PDF 1.4: two pages whose MediaBox is the card
size, each containing one full-bleed `/DeviceRGB` image compressed losslessly
with `/FlateDecode` (via the browser's `CompressionStream`). The document
title is `APG-<year>-<number> <Full name>`. The file name uses the last
word of the name, after dropping Jr./Sr./II/III/IV/V, taking the part after
the last hyphen, and stripping accents and non-letters:
`María Dela Cruz-Santos Jr.` → `APG-2026-007_Santos.pdf`.

### 2.7 Register (`src/register.js`, `register/`)

Each downloaded card is stored as `{ id, year, seq, name, title, issuedAt,
replacement }` in `localStorage` under `apg-id-register`. The number field
looks the id up on every keystroke. With `CONFIG.register.url` set, the app
also queries a Google Apps Script web app in front of a Google Sheet
(`GET ?action=find&id=…`) and records there (`POST {action:'record', entry}`);
the shared answer overrides the local one, and an unreachable Sheet degrades
to local-only with a note under the field rather than blocking. Only simple
CORS requests are used because Apps Script cannot answer preflights.

### 2.8 Privacy

The app makes no network requests (the optional Google Fonts stylesheet
only affects the form's typography and fails silently offline). Photos,
names and signatures exist only in the open tab and in the PDF you download.
Closing the tab or clicking **Start a new ID** discards them. Stored on the computer: the "Turn the back 180°" preference and the
register of issued IDs (number, name, title, date — no photos or
signatures). With a shared register, those same fields are also written to
the company Google Sheet.
