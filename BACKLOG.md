# Backlog

Ideas considered and deliberately not built yet, with enough detail to pick
them up later. Shipped work is described in `README.md` and `windows/README.md`.

## Left on `release/1.21`: web-app work not yet in the native app

Pull request #1 (the Windows and Mac native apps) was merged into `main`
on 2026-11-23. It branched before the 1.21 work, which was built on the
web/Tauri app that #1 removes, so none of `release/1.21` came with it.
That branch stays on GitHub (tag `v.1.21.0`) as the reference for porting.
What it holds, against what the native app does today:

| 1.21 change (commit) | In the native app? | To do |
|---|---|---|
| Auto-update: check, download, "Install version … and restart", signed `latest.json` via `npm run release` (76d14c5, 594d223, a43a0a6) | No | Pick a Windows updater (e.g. the Inno Setup installer from a GitHub release, or Velopack) and sign updates. The Tauri public key in a43a0a6 does not carry over; the private key at `%USERPROFILE%\.tauri\apg-id-maker.key` is only useful if the new updater uses minisign. "No answer" from the check must not blame the connection (594d223). |
| What's new tool, opens once after an update, "New" badge, version and "Check for updates" under the tools (6ead03d) | No | Port `src/whats-new.ts` notes and the panel; needs the updater first for the update-waiting part. |
| Signing mode: full screen, pointer held, strokes pinned to the board edge (54d1721, ff3f2dd) | Yes (`SignWindow`, `PenTablet.cs`) | Check against a pen tablet. |
| Signing keys: Ctrl+D (or Enter while unsigned) opens signing; Enter keeps it, Ctrl+Z undoes, Backspace clears, Esc cancels; key line a pen cannot press; Enter shown as a gold pill that glows once there is ink (54d1721, ff3f2dd, 3779086) | Partly: Backspace/Delete clear and Esc cancel | Add Ctrl+D, Enter to keep, Ctrl+Z undo, the key line and the Enter highlight. Note on the board when more than one screen is attached. |
| Batch from CSV removed from the tool rail; tools pinned while the Check step scrolls (ff3f2dd) | No: the native app still has batch | Decide whether batch stays in the native app. |
| Photo moved on the card: drag, wheel resize 50–150 % (3779086) | Yes | Not ported: double-click to reset, arrow keys 0.5 mm (Shift 2 mm), + / − / 0, the "Adjust the photo on the card" bar, the first-time hand hint, the size readout. |
| New Alpha Premier Group peacock logo for the header and the app icon (150bac7) | No: #1 brings its own `assets/logo.png` and `AppIcon.ico` | Agree which logo is current; if 1.21's, take `assets/logo.png` and `app/app-icon.png` from `release/1.21` and remake `windows/IdMaker.App/AppIcon.ico`. |

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
