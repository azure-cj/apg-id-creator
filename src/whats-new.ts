/* ------------------------------------------------------------------
   What's new — the notes shown in the app, newest first
   ------------------------------------------------------------------
   Add an entry at the top for every release, written for the people at
   HR who use the app, not for whoever builds it.

   The first entry must be APP_VERSION. `npm run release` refuses
   otherwise, and copies its lines into latest.json, so a computer with
   an update waiting can show what the update brings before installing.

   Plain JavaScript inside (no type annotations): app/release.mjs reads
   this file as it is.
   ------------------------------------------------------------------ */

const WHATS_NEW = [
  {
    version: '1.21.0',
    date: '2026-11-17',
    items: [
      'The app keeps itself up to date. It looks for a new version when it opens, and "Check for updates" at the bottom of the list on the left looks again. "Install and restart" does the rest.',
      'Signing has a mode of its own. Press Ctrl + D on the signature step and the screen becomes the signing board. Enter keeps the signature and goes on to Check, Esc cancels, Ctrl + Z undoes a stroke and Backspace clears.',
      'With a pen tablet, the pen can only draw on the board while signing. It can no longer press buttons or reach other programs.',
      'The signature step says whether a pen tablet is plugged in.',
      'The photo is moved on the card itself, on the Photo step: drag it to move it, scroll to resize it, double-click to put it back. The "Move or resize it" panel is gone, and on the other steps a stray drag no longer moves the photo.',
      'The list on the left stays in view while the page scrolls.',
      'This page, so you can see what changed in each version.',
      'Batch from CSV has been removed.',
    ],
  },
  {
    version: '1.20.0',
    date: '2026-11-04',
    items: [
      'A new desktop window. Install it with the one setup file; nothing else is needed.',
      'Issued IDs, Printer setting and Card design moved to the list on the left, so the card has more room.',
      'Card design: put in new card artwork, and drag the boxes for the photo, name, job title, number, signature and QR code into place. Cancel puts them back.',
      'Photos are sized from the shoulders up, so hair no longer makes the head come out too small.',
      'The number step waits for the shared register before calling a number free, so the same number is not given out twice.',
      'The print preview on the last step no longer spills over the form.',
    ],
  },
];
