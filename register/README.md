# Shared register (optional)

Out of the box the ID Maker remembers the cards downloaded **on each
computer** and warns when a number is reused there. To catch duplicates
across several HR computers, point every copy of the app at one Google
Sheet. Set-up takes about ten minutes and needs no server.

## 1. Make the Sheet and script

1. In the company Google account, create a new Google Sheet. Name it
   something like **APG ID Register**.
2. *Extensions → Apps Script*. Delete the sample code, paste the contents
   of `Code.gs` from this folder, save.
3. *Deploy → New deployment* → type **Web app**.
   - Description: `ID register`
   - Execute as: **Me**
   - Who has access: **Anyone with the link** *(the URL is unguessable; the
     script only ever returns ID numbers, names and titles, and only writes
     rows the app sends)*. If your Workspace allows it, **Anyone within
     Alpha Premier** is stricter and still works for HR computers signed
     in to the company account.
4. Authorise when asked, then copy the **Web app URL**
   (`https://script.google.com/macros/s/…/exec`).

Test it: open the URL in a browser. You should see
`{"ok":true,"service":"apg-id-register"}` and a `Register` tab appears in
the Sheet.

## 2. Point the app at it

In `src/config.js`:

```js
register: {
  storageKey: 'apg-id-register',
  url: 'https://script.google.com/macros/s/…/exec',   // ← paste here
  timeoutMs: 6000,
},
```

Run `node build.mjs` and give the new `Alpha Premier ID Maker.html` to every
HR computer.

## What changes in the app

- Typing a number now also asks the Sheet. If it is already there, the
  field turns red with who has it and when, and a **Replacement card** tick
  box appears for genuine re-prints.
- Every downloaded card is written to the Sheet (and still kept locally).
- If the Sheet cannot be reached (offline, URL wrong), the app says so under
  the number field and falls back to the local list — it never blocks
  issuing a card.

## Importing what was issued before

Paste older IDs straight into the `Register` tab, one per row, with the
columns `id, year, seq, name, title, issuedAt, replacement`. `issuedAt` can
be a date cell. The `id` column is what the duplicate check matches on
(`APG-2026-122`).

## Changing the script later

After editing `Code.gs`, use *Deploy → Manage deployments → ✎ → Version:
New version → Deploy*. The URL stays the same.
