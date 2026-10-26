# Backlog

Ideas considered and deliberately not built yet, with enough detail to pick
them up later. Shipped work is described in `README.md` and `PROCESS.md`.

## On-device background removal (deferred)

**Idea.** Replace the "paste this prompt into Gemini / ChatGPT" step with a
segmentation model running in the browser, so a plain phone photo can be cut
out without sending the employee's face to a third party.

**What was found.** The only candidate that works fully offline is Google's
**MediaPipe Selfie Segmentation** (`@mediapipe/selfie_segmentation`,
Apache-2.0, about 4 MB including the WASM runtime and the `.tflite` model);
everything else — `@imgly/background-removal`, `transformers.js` with
RMBG/BiRefNet — downloads a 40–170 MB model from a CDN at runtime, which
defeats the point of a single offline file.

**Open question.** MediaPipe's mask is 256 × 256. The head on the card is
about 177 px wide at 300 DPI, so the mask must be upscaled roughly 2× around
the hairline. Whether that is clean enough to print, or only good enough as
a preview, has not been measured on real photos.

**How to decide.** Build a throwaway page that loads the package, runs both
model variants over a few real staff photos, and writes out the cut-outs;
compare the hair edge against the same photo put through the current
green-screen prompt. If the edge holds up, integrate it as a third accepted
input in `src/photo.js` (alongside `transparent` and `green`/`blue`), keeping
the prompt workflow as a fallback for awkward photos. If it does not, close
this item: the prompt plus the manual adjust already covers the cases.

**Cost if adopted.** The single-file app grows from ~1.4 MB to ~5.5 MB, and
the build has to inline a `.wasm` and a `.tflite` as base64.

## Signing on a phone

**Idea.** Show a QR code; the new hire signs on their own phone; the strokes
come back to the open tab.

**Why not yet.** It needs a relay the two devices can both reach — a small
server or a hosted channel. The app is deliberately a single offline file
with no backend, so this would be the first piece of infrastructure beyond
the optional Google Sheet. The "Upload a scan" button now covers most of the
same ground: the person signs on paper, HR photographs it, the app turns it
into clean white ink.

**If revisited.** The shared register's Apps Script could carry the strokes
as a short-lived row keyed by a random token, avoiding a new service.

## Smaller ideas not yet done

- **Job-title autocomplete** from titles already in the register.
- **Photo library**: remember a cut-out against an employee number so a
  replacement card can be re-printed without the original photo.
- **Register search** in the Issued IDs panel (currently shows the newest 25
  and exports the rest to CSV).
- **Per-row layout overrides in the batch CSV** (e.g. a card that needs the
  back rotated).
