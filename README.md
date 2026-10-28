# Alpha Premier ID Maker

Makes employee ID cards for Alpha Premier Group of Companies.

You type in the person's details, add their photo and signature, and the
app gives you a PDF to print. The cards come out bank-card size, ready to
cut.

---

## Installing it

1. Download **`AP-ID-Maker-Setup.exe`** from the Releases page of this
   repository.
2. Double-click it.
3. Windows will say **"Windows protected your PC"**. This is normal — it
   says that about any program it has not seen before. Click **More
   info**, then **Run anyway**.
4. Follow the prompts. It does not ask for an administrator password.

You will find **Alpha Premier ID Maker** in the Start menu and on your
desktop. Right-click it on the taskbar to pin it there.

You need Microsoft Edge, which is already on every Windows computer. The
installer checks and tells you if it is missing.

---

## Making an ID

Open the app and work through the six steps. It will not let you move on
until the step you are on is complete, so you cannot forget anything.

**1. Name** — last name, first name, middle name, suffix. Type them
normally; if you type all in small letters the app fixes the capitals when
you leave the box. The line underneath shows exactly what will be printed
on the card.

**2. Job title** — pick from the list. If someone does two jobs, tick the
second role and pick that too; the card will say something like
*IT/Admin Associate*. If the title you need is not listed, choose
**Other…** and type it.

**3. Employee number** — type the number HR assigned, for example `124`.
The app turns it into `APG-2026-124`.

> If that number has been used before, the box turns **red** and tells you
> who already has it. Either use a different number, or — if you are
> reprinting a lost card for the same person — tick **Replacement card**.

**4. Photo** — the photo needs a plain green background. If you do not
have one, the app shows you a short instruction to copy; paste it into the
company's Gemini or ChatGPT account along with the photo, and it will
produce one for you.

> Use the **company account**, never your own personal one.

Upload the result. The app cuts out the person and places them on the
card. If something is wrong — too blurry, or clothing the same colour as
the background — it tells you and asks for a different photo.

**5. Signature** — the new hire signs in the box with the mouse, or you
upload a photo of their signature on white paper.

**6. Check and print** — you will see the actual sheet that will come out
of the printer. Check the name, title and number, then press **Download
PDF**.

---

## Printing

Open the PDF and print it at **100%** — not "fit to page", or the cards
come out the wrong size.

On the last step you choose how the cards sit on the paper:

- **Side by side** (normal) — the front and the back are printed next to
  each other on one sheet. Cut both out and stick them back to back. Four
  cards per A4 sheet.
- **Both sides** — the fronts print on one page and the backs on the next,
  for printing on both sides of the paper. Nine cards per A4 sheet, but
  your printer has to line the two sides up accurately. **Print one sheet
  and check before doing a whole batch.**

### Several people at once

On the last step, press **+ Add another ID to this print**. Finish that
person, and both go on the same sheet. Repeat for as many as fit. Everyone
waiting is listed under the preview.

Changed your mind? **Cancel — back to printing** takes you back with
everyone already added still there.

---

## Changing the card design

You can put new artwork in yourself, without waiting for a new version of
the app.

1. Get the two pictures from the designer: the **front** and the **back**,
   both 638 × 1011 pixels. (`TEMPLATE-SPEC.md` is the brief to give them.)
2. Start a **new, blank ID** — the design can only be changed before you
   start filling one in.
3. Open **Card design** at the bottom of the form, choose the two files
   and press **Use this design**.
4. Boxes appear on the card for the name, job title, number, signature,
   QR and photo. **Drag them where they belong**, and drag the round
   corner to resize. Arrow keys nudge the selected one. Press **Done**.

The app warns you if any of the text would be too pale to read against the
new artwork. Move that box somewhere darker, or ask the designer to put a
darker shape behind it.

The design stays on this computer until you change it. **Use the built-in
design** puts the original card back at any time.

> If the designer also sends a **map** — a third picture with the zones
> painted in flat colours — add it under *Add a map*. Then nothing needs
> dragging: the app places everything itself, and the artwork can overlap
> the photo.

---

## Things that might confuse you

**The number box turned red.** That number is already used. The message
says who has it. Use another, or tick *Replacement card* if you are
reprinting someone's lost ID.

**It says the photo is no good.** Common reasons: the background is not
plain green, the person is wearing something the same colour as the
background, or the photo is blurry. Take or request another one.

**The back of the card prints upside down.** Open **Printer setting** at
the bottom of the form and tick *Turn the back 180°*.

**The backs end up behind the wrong people.** Only happens with the *Both
sides* layout. In **Printer setting**, change *How your printer turns the
paper over* to **short edge**, and print again.

**The cards come out the wrong size.** The printer was set to shrink the
page. Print at 100% or "actual size".

---

## What the app keeps

**It never stores photographs.** The photo is used to make the card and is
then gone. If someone loses their ID, you will need the photo again to
reprint it. That is on purpose — there is no store of employee photos to
protect.

**Every ID you issue is recorded** in the company's register sheet: the
number, the name, the job title and the date. That is what lets the app
warn you about a number that has already been used. There is nothing to
set up; it is already connected.

**The register is the real record, not your laptop.** If the laptop is
replaced or wiped, the sheet still has everything.

---

## Getting help

The full procedure, including making many cards at once from a
spreadsheet, is in **[PROCESS.md](PROCESS.md)**.

If you look after the app itself, see **[DEVELOPING.md](DEVELOPING.md)**.

If someone is redesigning the card, give them
**[TEMPLATE-SPEC.md](TEMPLATE-SPEC.md)** — it says what size to work at and
which parts of the card the app draws itself.
