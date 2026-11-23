# Backlog

Ideas considered and deliberately not built yet, with enough detail to pick
them up later. Shipped work is described in `README.md` and `windows/README.md`.

## On-device background removal (done, in the Windows app)

Built into the native app (`windows/IdMaker.Vision`): an ordinary photo
is cut out on the computer by MODNet, a 25 MB portrait-matting model, so
an employee's face no longer goes to Gemini or ChatGPT to make their ID.

The open question this item used to carry — whether a 256 × 256
MediaPipe mask was good enough to print — did not need answering: MODNet
works at 512 pixels on the short side and returns a soft matte rather
than a mask, and on the sample portrait over plain and office-like
backgrounds it overlaps the true outline by 99 %. Bold stripes or busy
artwork directly behind the person are its weak spot (73–85 %).

**Still to do:** measure it on real staff photos, which was not possible
from where it was built. If hair edges disappoint, the next step up is
BiRefNet-portrait (MIT licence): better edges, but about 1 GB and several
seconds a photo without a graphics card.

## Signing on a phone

**Idea.** Show a QR code; the new hire signs on their own phone; the strokes
come back to the app.

**Why not yet.** It needs a relay the two devices can both reach — a small
server or a hosted channel. The app deliberately has no backend, so this
would be the first piece of infrastructure beyond the shared register. Two things now cover most of the same ground: the full-screen
signing screen with the pen tablet, and **Use a scan instead** — the
person signs on paper, HR photographs it, the app turns it into clean
white ink.

**If revisited.** The Firebase project the register is in could carry the
strokes as a short-lived document keyed by a random token, avoiding a new
service. It would need its own rule, and a signature must not be kept
there once it has arrived.

## Smaller ideas not yet done

- **Job-title autocomplete** from titles already in the register.
- **Photo library**: remember a cut-out against an employee number so a
  replacement card can be re-printed without the original photo.
- **Register search** in the Issued IDs panel (currently shows the newest 25
  and exports the rest to CSV).
- **Per-row layout overrides in the batch CSV** (e.g. a card that needs the
  back rotated).
