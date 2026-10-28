# Designing a new ID template

For whoever designs the card. The app draws the person, the name, the job
title, the signature, the employee number and the QR square on top of your
artwork, always in the same places. Design around those places and the new
card works the moment it is dropped in.

**Everything in this document is fixed.** Move one of these zones and the
app has to be re-measured in code, which is a developer job, not a
drag-and-drop. Change colours, shapes, patterns, the logo, the whole look
— freely.

## The canvas

| | |
| --- | --- |
| Size | **638 × 1011 px** |
| Same thing in mm | 54 × 85.6 mm (CR80 — a bank card) |
| Resolution | 300 DPI |
| Format | PNG, no transparency needed |
| Two files | the front and the back, same size |

In Canva: **Create a design → Custom size → 638 × 1011 px**. Export as
**PNG**, at 1× (do not tick "2x" — the file must come out 638 × 1011).

> The current artwork is 638 × **1004** px and the app stretches it by
> 0.7 % to fit. Designing at 1011 removes that stretch, so a new template
> will be very slightly sharper than the one in use today.

## How the app builds the card

Three layers, in this order:

1. **Your front artwork**, drawn whole.
2. **The person**, clipped to the photo window.
3. **Parts of your artwork again, on top of the person** — the dark band
   across the bottom and the gold diagonal. That is what makes the person
   appear to stand behind the band.

So the bottom-left region is drawn twice: once underneath, once over the
top. You do not have to do anything about this — it is why the band can
overlap the shoulders — but it explains why the person is hidden there.

## The zones

| Zone | mm (x, y, w, h) | px at 300 DPI | What it is |
| --- | --- | --- | --- |
| **Photo window** | 17, 10.071, 36.5, 57.402 | 201, 119, 431, 678 | where the person appears; artwork here is covered |
| **Name** | 34.4, 59.719, 18, 5.237 | 406, 705, 213, 62 | drawn by the app in gold #E8CD77 |
| **Job title** | 34.4, 65.459, 18, 3.223 | 406, 773, 213, 38 | drawn by the app in white, CAPITALS |
| **Signature** | 34.9, 69.084, 17, 5.64 | 412, 816, 201, 67 | drawn by the app in white |
| **Employee number** | 28.6, 78.047, 23, 1.511 | 338, 922, 272, 18 | drawn by the app in white, right-aligned |
| **QR square** | 2, 66.567, 8.8, 8.8 | 24, 786, 104, 104 | white square the app paints over your artwork |

Use **template-guide.png** (638 × 1011, transparent) as a locked layer in
Canva and design underneath it. Hide it before exporting.
**template-guide-annotated.png** shows the same zones over the current
card, labelled, if you would rather see how it is used today.

## Rules that will break the card if ignored

**The name, title, signature and employee number are printed in light
ink** — gold `#E8CD77` for the name, white for the rest. **Those four
boxes need a dark background in your artwork**, or the text is invisible.
The current card uses a near-black band for exactly this.

**Leave the four text boxes empty.** The app writes in them. Anything you
put there will be printed over.

**The QR square is painted white by the app**, including a quiet border.
Whatever you design under it will not be seen, so do not plan anything
there.

**Keep the photo window clear of anything that matters.** Everything in
it is covered by the person — except the parts listed above that are
redrawn on top.

**The face sits 183 px (15.509 mm) from the top**, centred
432 px (36.6 mm) across, about 177 px (15 mm) wide. Keep
that area free of detail you want seen.

**Text is set in fixed fonts** — Poppins 700 for the name, Montserrat 800
for the title, Arimo 400 for the number — sized automatically to fit its
box. You cannot change them from the artwork.

## The back

The back has no zones at all: nothing is drawn on it except the signature
of whoever signs the cards, which is part of your artwork, not something
the app adds. Design it freely at the same 638 × 1011 px.

## If a zone has to move

Everything above assumes the zones stay where they are. They do not have
to. The zones are not a file you draw — they are boxes HR can drag on the
card itself, in **Card design → Show the map**. So if your design wants
the photo on the other side, or the name somewhere else, say so in a note
with the artwork and HR moves the boxes to match; nobody measures
anything and nothing is rebuilt.

What dragging can and cannot do:

- **It moves and resizes every zone**, including the photo window, and
  the face is framed inside that window the same way it is framed today.
- **On the built-in card the photo window keeps its stepped shape** — it
  is moved and scaled, not flattened into a rectangle.
- **On your artwork the photo window is a plain rectangle** sitting on
  top of the design. The app has no way to know which parts of a new
  picture belong in front of the person, so nothing overlaps the photo.
  If your design needs a band across the shoulders, it has to be printed
  on the card *behind* the photo window instead, or the window has to
  stop above it.

## Submitting it

Hand over the two PNGs, named clearly, for example `front-2027.png` and
`back-2027.png`, with a note of anything that has moved.

Nothing needs rebuilding. HR clicks **Card design** in the tool rail on a
blank ID, opens **Use different artwork**, chooses the two files and
presses **Use this artwork**. The zones stay where they were, so the next
thing on screen is the map, ready to be dragged onto the new design.

Either way the app checks the text against the artwork before accepting
it and says so if the name, job title, number or signature would be too
pale to read. That check is the one thing that cannot be dragged away:
fix it in the artwork.

A design put in this way lives on that computer. To make it the design the
app ships with, hand the files to whoever maintains the app: the built-in
card is covered by a test that renders a known ID and compares it pixel
for pixel with the last one, so a shift shows up there rather than on
printed stock.
