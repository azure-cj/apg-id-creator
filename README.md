# Alpha Premier ID Maker

Makes employee ID cards for Alpha Premier Group of Companies.

You type in the person's details, add their photo and signature, and the
app gives you a PDF to print. The cards come out bank-card size, ready to
cut.

---

## Installing it

1. Get **`AlphaPremierIdMaker-Setup`** from IT, or from the Releases page
   of this repository.
2. Double-click it.
3. If Windows says **"Windows protected your PC"**, click **More info**,
   then **Run anyway**. It says that about any program it has not seen
   before.
4. Click through the setup. It does not ask for an administrator password.

It installs like any other Windows app. You will find **Alpha Premier ID
Maker** in the Start menu; right-click it there to pin it to the taskbar.
Tick **Put a shortcut on the desktop** during setup if you want one.

**The first time on each computer: sign in.** Open the app, click
**Issued IDs** on the left, and sign in with your Keystone email and
password. That connects this computer to the company's register of
issued IDs, so a number is never given twice. It is asked once. No
Keystone account yet? Ask the CEO to make one.

**The pen tablet.** If this computer has no tablet driver, setup offers to
install the **XP-Pen Deco Fun** driver, already ticked. That one step does
ask for permission ("Do you want to allow this app to make changes"),
because a driver is installed for the whole computer. Say **Yes**, then
unplug the tablet and plug it back in. If you say No, the app still works;
you sign with the mouse, or use a scan.

**Updating.** Run the newer setup over the top. It replaces the old
version and keeps your issued-ID list and settings.

**Removing it.** Settings → Apps → Installed apps → Alpha Premier ID Maker
→ Uninstall. Your issued-ID list is left on the computer.

Nothing else needs installing first.

### On a Mac

The same app, made for the Mac. Everything below works the same way;
where this guide says a job title "chip", the Mac has the same titles in a
menu.

1. Get **`AlphaPremierIdMaker-Mac-AppleSilicon`** (any Mac from late 2020
   on: M1, M2 and later) or **`AlphaPremierIdMaker-Mac-Intel`** (older
   Macs) from IT, or from the Releases page. Not sure which? Apple menu →
   About This Mac: "Chip: Apple…" means Apple Silicon.
2. Double-click it, then drag **Alpha Premier ID Maker** onto
   **Applications**.
3. Open it from Applications. The first time, macOS says it **cannot
   check the app**, because the company has no Apple developer account.
   Click **Done**, open **System Settings → Privacy & Security**, scroll
   down and click **Open Anyway**. That is asked once.
4. Sign in under **Issued IDs**, as on Windows.

**The pen tablet.** The first time it opens on a Mac with no tablet
driver, the app offers to install the **XP-Pen** driver. Say **Install the
driver**: it downloads XP-Pen's own installer and opens it. The installer
asks for the Mac's password. Afterwards macOS asks you to allow the
driver under **Privacy & Security → Input Monitoring** and
**Accessibility**; switch it on in both, then open the app again. If you
skip it, there is a button for it on the signature step, and you can sign
with the mouse or trackpad meanwhile.

**Updating.** Drag the newer app onto Applications and choose **Replace**.
Your issued-ID list and settings are kept.

**Removing it.** Drag it from Applications to the Bin. Your issued-ID list
stays in your Library folder (Application Support → Alpha Premier → ID
Maker).

---

## Making an ID

Open the app and work through the five steps. It will not let you move on
until the step you are on is complete, so you cannot forget anything.

**1. Name** — last name, first name, middle name, suffix. Type them
normally; if you type all in small letters the app fixes the capitals when
you leave the box. The line underneath shows exactly what will be printed
on the card.

**2. Job and employee number** — first choose who the ID is
for: **An employee** or **An intern**. The app does not guess, and will
not move on until you choose. The attendance system keeps interns only
and takes new ones from this register, so get this one right.

Then the job title — pick from the list. If someone does two jobs, tick the
second role and pick that too; the card will say something like
*IT/Admin Associate*. If the title you need is not listed, choose
**Other…** and type it.

Below the job, on the same page, type the employee number HR assigned,
for example `124`. The app turns it into `APG-2026-124`.

> If that number has been used before, the box turns **red** and tells you
> who already has it. Either use a different number, or — if you are
> reprinting a lost card for the same person — tick **Replacement card**.

**3. Photo** — choose any clear photo of the person, or drag it onto the
window. The app removes the background itself, on this computer; the
photo is not sent anywhere. It then places the person on the card.

Take the photo against a plain wall: it gives the cleanest edge. A photo
with a room behind the person (a door, a window, shelves) is refused, and
the app says how to fix it. A cut-out PNG or a photo on a green background
works too, as before.

If the photo is blurry, small, or cropped very close, the app says so but
lets you carry on — how the photo was taken is the photographer's call.
Check the card, and use **Move or resize it** if the face needs shifting.
You can also drag the photo on the card and turn the mouse wheel over it.

**4. Signature** — the signing screen opens by itself and fills the whole
screen, so the pen has the whole tablet to work with and cannot wander
off the edge. The new hire signs with the pen (press harder for a thicker
line), a finger on a touch screen, or the mouse, then presses **Use this
signature**. **Clear** starts over; **Cancel** or the Esc key goes back
without signing.

No tablet? **Use a scan** takes a photo of their signature on
white paper.

**5. Check and print** — you will see the actual sheet that will come out
of the printer. Check the name, title and number, then press **Save the
PDF** (at the bottom right, where Next was) and choose where to keep it. The card counts as issued once the file
is saved.

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

On the last step, press **Add another ID to this print**. Finish that
person, and both go on the same sheet. Repeat for as many as fit. Everyone
waiting is listed under the preview.

Changed your mind? **Stop adding and go back to the print** takes you back
with everyone already added still there.

---

## Making many cards at once

Click **Batch from CSV** in the list on the left.

1. **Save an example list to fill in**, open it in Excel, and fill it in,
   one person per row. Save it as CSV.

   | Column | |
   | --- | --- |
   | `last`, `first`, `title`, `seq` | needed |
   | `middle`, `suffix` | the rest of the name, if any |
   | `title2` | a second role, joined with `title` the way the form does it |
   | `name` | an older list with one whole-name column still works |
   | `year` | leave empty for this year |
   | `photo` | the photo's file name, such as `aureo.png` |
   | `signature` | the file name of a scanned signature, if there is one |
   | `replacement` | `yes` to allow a number that is already issued |

2. **Choose the list**, then **all the photos**, then the scanned
   signatures if you have them. Files are matched by name, ignoring
   capitals and even the ending, so `Aureo.JPG` finds `aureo.png`.
3. Choose the **print layout** and the paper.
4. **Make the batch** and choose where to save the PDF.

Each row is checked exactly as a single card is: numbers already used,
names too long for the card, photos that are missing or cannot be used.
Good rows go into one PDF and are recorded as issued. Rows with a problem
are listed underneath with the reason and are left out — nothing is half
done. Fix those rows and run them again as a smaller list.

A row that names a signature file you did not choose is left out, rather
than quietly printed without it. A row with no signature at all is
printed with the signature space empty, and says so in the list.

Up to 200 people at a time.

---

## Changing the card design

Two things can be changed here without waiting for a new version of the
app, and both only on a **new, blank ID**.

### Moving the boxes

Click **Card design** in the list on the left, then **Show the map**. The
boxes for the photo, name, job title, number, signature and QR appear on
the card. Drag one to move it, drag its gold corner to resize it, or select
one and nudge it with the arrow keys (hold Shift to resize). The card
redraws as you go.

**Keep this map** keeps it on this computer. **Discard changes** puts the
boxes back where they were when you opened the map. **Put the boxes back
where the built-in design has them** goes all the way back to the
original positions, including moves you kept earlier.

### New artwork

1. Get the two pictures from the designer: the **front** and the **back**,
   both 638 × 1011 pixels. (`TEMPLATE-SPEC.md` is the brief to give them.)
2. In **Card design**, under **Use different artwork**, choose the two
   files and press **Use this artwork**.
3. The boxes stay where they were, which is unlikely to suit a new
   design, so the map opens straight away. Drag the boxes into place and
   press **Keep this map**.

The app warns you if any of the text would be too pale to read against
the new artwork. Move that box somewhere darker, or ask the designer to
put a darker shape behind it.

**Go back to the built-in design** puts the original card and its original
layout back at any time.

---

## Light and dark

The app follows Windows: if Windows is set to dark mode, so is the app,
and it changes over by itself when you change the setting (Settings →
Personalisation → Colours → *Choose your app mode*). To keep the app light
or dark whatever Windows is set to, choose **Light** or **Dark** under
**Appearance** at the bottom left; **Match Windows** goes back to
following Windows. The choice is remembered on this computer. The printed
card looks the same either way. (On the Mac the app follows macOS, as
before.)

---

## Things that might confuse you

**The number box turned red.** That number is already used. The message
says who has it. Use another, or tick *Replacement card* if you are
reprinting someone's lost ID. You cannot move on from that step until
the number is one nobody has.

**It says it is checking the shared register.** Only on the first number
after the app opens, and only for a moment. The whole register is read
once when the app starts, so every number after that is checked
instantly. If the register cannot be reached at all, or nobody has signed
in to it on this computer, the app says so and lets you carry on — it
still checks against this computer.

**It says to sign in under Issued IDs.** The shared register asks who
you are, once on each computer. Click **Issued IDs** on the left and sign
in with your Keystone email and password. No account? Ask the CEO to
make one in Keystone.

**It says a card is a duplicate and must not be handed out.** Another
computer issued that number a moment before yours did. Take the card
back, make it again with a number that is free, then click **These cards
are dealt with** under **Issued IDs**.

**The cut-out has bits of background in it, or cuts into the person.**
The background remover works best when the person stands in front of a
plain wall. Busy backgrounds — stripes, posters, shelves right behind
them — confuse it. Take the photo again against a wall. If that is not
possible, open **If the cut-out looks wrong** under the photo for another
way to get a clean one.

**The pen does nothing, or draws but the line never gets thicker.** Unplug
the tablet and plug it back in. If the signing screen says no tablet
driver was found, run the setup again and say **Yes** when Windows
asks. The mouse always works in the meantime.

**The back of the card prints upside down.** Click **Printer setting** on
the left and tick *Turn the back upside down*.

**The backs end up behind the wrong people.** Only happens with the *Both
sides* layout. In **Printer setting** on the left, change *How your printer
turns the paper over* to **short edge**, and print again.

**The cards come out the wrong size.** The printer was set to shrink the
page. Print at 100% or "actual size".

---

## What the app keeps

**It never stores photographs, and never sends them anywhere.** The photo
is used to make the card, on this computer, and is then gone. If someone loses their ID, you will need the photo again to
reprint it. That is on purpose — there is no store of employee photos to
protect.

**Every ID you issue is recorded** in the company's register: the
number, the name, the job title, the date and who issued it. That is what
lets the app warn you about a number that has already been used. The only
thing to set up is signing in, once on each computer: **Issued IDs** on
the left, with your Keystone email and password.

If the register cannot be reached when you save a card, the app tells you,
keeps the card on this computer, and writes it to the register by itself
the next time it starts with the internet working.

**The register is the real record, not your laptop.** If the laptop is
replaced or wiped, the register still has everything. A card in it cannot
be changed or removed from the app, by anyone.

---

## Getting help

If you look after the app itself, see **[windows/README.md](windows/README.md)**.

If someone is redesigning the card, give them
**[TEMPLATE-SPEC.md](TEMPLATE-SPEC.md)** — it says what size to work at and
which parts of the card the app draws itself.
