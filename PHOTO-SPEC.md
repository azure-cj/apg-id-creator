# ID photo handling — spec and current state

**Status: FINAL for v1. Decisions confirmed by the product owner on
27 Oct 2026 and implemented in app v1.3.0.** Findings below are measured
against the real pipeline, not estimated. See "How this was verified".

## 0. Decisions (settled)

| Decision | Ruling |
| --- | --- |
| Clothing the colour of the backdrop | **Reject.** "Clothing is too close to the background colour — redo the photo with a blue background or different top." |
| Blurred photo | **Warn, never block.** The warning also repeats on the final check step. |
| Gemini / ChatGPT step | **Kept**, on the company's **approved enterprise account**. Stated in the UI and the README. |
| Retention | **None.** The app stays stateless; re-issuing a card means sourcing the photo again. |
| Operators | **HR only, desktop browser.** Employee self-service is an explicit **non-goal** for v1. |

## 1. What this actually is

It is **not** a photo-processing service. There is no server, no queue, no
face detector, no segmentation model, no AI API and no account. It is a
**photo step inside the ID card maker**: a browser-side chroma key plus a
silhouette measurement, used to place a cut-out onto the card artwork.

| | |
| --- | --- |
| Operator | **HR staff only**, on an office desktop browser, one card at a time or a CSV batch. Employee self-service is a non-goal |
| Where it runs | The user's own browser. `src/photo.js`, no network |
| Input | One file chosen by HR (or many file names in a batch CSV) |
| Output | **No photo file is produced.** The cut-out is drawn onto the card; the deliverable is the card PDF |
| Volume handled | Single file ~26 ms; batch capped at 200 rows, rendered one page at a time |
| Cost | Zero. Nothing is billed, because nothing is called |

The one third-party step is **manual and outside the app**: HR is told to
paste a prompt and the employee's photo into Gemini or ChatGPT to get a
green-screen portrait. The app never does this; it only shows the prompt.

## 2. The pipeline, every operation

`processPhotoFile()` in `src/photo.js`:

1. **Validate** — PNG/JPG/WebP by extension or MIME; HEIC/HEIF rejected with
   instructions; over 15 MB rejected.
2. **Decode** to a canvas capped at 2400 px tall. EXIF orientation is applied
   by the browser (verified).
3. **Classify the background** by sampling the border only:
   `transparent` / `green` / `blue` / `checkerboard` / `other`.
   `checkerboard` and `other` are rejected.
4. **Chroma key** (green or blue only), per pixel, on dominance
   `key − max(other two)`: ≤0 untouched; 0–30 de-spill only; 30–110 alpha
   ramps to 0; ≥110 transparent.
5. **Trim edge halo** — half-pixel alpha erosion over the soft edge.
6. **Measure the silhouette** on an 800 px copy: top of head, widest row
   (= head width), narrowest row below (= neck), first row >1.5× head width
   (= shoulders), head centre from rows ≥80 % of head width.
7. **Place** in millimetres so the head is 15 mm wide, its top at 15.4 mm,
   centred at 36.6 mm; scaled up if the body would not reach the band at
   66 mm. HR can then nudge and zoom it by hand.

**No generative model touches the pixels.** The operations applied are:
scale (bicubic via canvas), per-pixel channel clamp (de-spill), per-pixel
alpha, 3×3 alpha erosion, and a final scale when drawn to the card. Nothing
reconstructs or invents image content — but note that the *input* may have
been produced by Gemini/ChatGPT before it ever reaches the app, and the app
cannot tell.

## 3. Output

| | |
| --- | --- |
| Card | 54 × 85 mm at 300 DPI = 638 × 1004 px, portrait, fixed |
| Photo crop | **Not a fixed ratio.** The photo is scaled and positioned, then clipped by the `photoArea` polygon |
| Framing | Head width 15 mm, head top 15.4 mm, centre x 36.6 mm, body must reach y 66 mm — all in `CONFIG.photo`, identical for every photo |
| Files | 2-page PDF (`/DeviceRGB`, `/FlateDecode`, lossless) and optional front/back PNGs |
| Colour profile | **None embedded.** Untagged DeviceRGB, treated as sRGB by most RIPs |
| Background | `#00FF00` is only what the *prompt asks for*. The key is tolerance-based, so any strong green or blue works; no pixel is ever compared against `#00FF00` |

## 4. Confirmed weaknesses

Measured by running synthetic cases through the real pipeline:

| Case | Result | Severity |
| --- | --- | --- |
| **Green or blue clothing** | **FIXED.** Rejected: holes inside the body outline reach 38 % (green) and 42 % (blue) against ≤0.5 % for every good photo | Closed |
| Blurry photo | **FIXED.** Accepted with a warning on the photo step and again on the check step. Laplacian variance 2 for the blurred case against 384–2055 for sharp ones | Closed |
| Face occluded (hand, object) | Accepted silently. No face detection, so nothing can see it | Medium |
| Two people on one green background | Rejected, but the message says "Head and shoulders aren't both visible" | Medium (wrong message) |
| Hat / wide headwear | Rejected with the same misleading message | Medium |
| Dimly lit green screen (G ≈ 110) | Classified `other` → "This photo still has its background" | Medium (wrong message) |
| Low resolution (head 70 px) | Accepted with a soft-print warning | Working as intended |
| Tilted head (15°) | Accepted, placement acceptable | Working as intended |
| EXIF-rotated JPEG | Handled correctly by the browser | Working as intended |

The last line of defence for all of these is the human: step 6 shows both
sides at print resolution with a checklist before anything downloads.

## 5. Privacy and audit

- **No image leaves the machine from the app.** The only network calls are
  the optional Google Sheet register (`src/register.js`) and the Google
  Fonts stylesheet for the UI.
- **No photo is stored anywhere, by decision.** Not in `localStorage`, not
  in the register, not in the draft. Closing the tab discards it. The
  original is not kept, and there is no hash or before/after record.
  **Consequence, accepted:** re-issuing a lost card means sourcing the
  photograph again. There is nothing to leak and nothing to purge.
- **The register stores** id, year, number, name parts, title, roles,
  issue date and replacement flag — no images.
- **Audit trail**: the Google Sheet records `recordedBy` from the signed-in
  Google account. The local-only register records no operator.
- **The compliance exposure is the manual AI step**, not the app: employee
  photographs are uploaded by hand to Gemini or ChatGPT. Policy requires
  the **company's approved enterprise account**; the photo guide and the
  README say so in bold, but **nothing in the app can enforce it** — it
  rests on HR following the instruction.

## 6. v1 scope

**Shipped in v1.3.0:**

1. **Clothing guard.** After keying, two measures are taken from the
   silhouette: `holeFraction` (non-solid pixels inside the body outline
   below the neck) and `torsoRatio` (median width in the head-height band
   under the shoulders, over the shoulder width). Either `holeFraction`
   above 6 % or `torsoRatio` below 0.6 rejects the photo. Measured
   separation: 38–42 % holes for keyed-out clothing, ≤0.5 % for every good
   photo — a 70× margin.
2. **Blur warning.** Variance of the Laplacian over the face box on the
   800 px analysis copy; below 55 adds a warning. Never blocks.
3. **Enterprise account requirement** stated in the photo guide and README.

**Explicitly deferred:** face detection, model-based matting, photo
retention, server-side processing, employee self-service.

**Known limits accepted for v1:**

- Hats and wide headwear are rejected with a misleading message ("Head and
  shoulders aren't both visible"). Two people on one background and a dimly
  lit green screen are likewise rejected with the wrong reason. All three
  fail safe; only the wording is wrong.
- An occluded face is accepted — there is no face detection. The human
  check at step 6 is the only defence.
- The `torsoRatio` half of the clothing guard has never fired in testing;
  only `holeFraction` has been exercised against real failures.

## 7. How this was verified

Thirteen synthetic cases were run through `processPhotoFile()` in headless
Chromium and the accepted ones rendered to cards at 300 DPI
(`green-shirt`, `occluded`, `tilted`, `blurry` inspected by eye). Timing is
the median of nine runs on a 720 × 1195 cut-out: **26 ms** (min 24, max 34),
synchronous, on the UI thread. Network, storage, colour space and audit
claims were checked by reading the built file and a generated PDF.

**Not tested: a single real employee photograph.** Every case above is
synthetic, built from one stock cut-out. The pipeline's behaviour on real
hair edges, real lighting and real AI-generated green screens is still
unmeasured, and the thresholds above (6 %, 0.6, 55) are tuned against
synthetic data. **The first pilot cards should be watched for false
rejections**, particularly the clothing guard on dark suits against a
poorly lit backdrop.
