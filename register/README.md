# Shared register (optional)

Out of the box the ID Maker remembers the cards downloaded **on each
computer** and warns when a number is reused there. To catch duplicates
across several HR computers, point every copy of the app at one Google
Sheet. Set-up takes about ten minutes and needs no server.

## 1. Deploy the script in your Sheet

1. Open the register spreadsheet in Google Sheets.
2. *Extensions → Apps Script*. Delete the sample code, paste the whole of
   `Code.gs` from this folder, and save.
3. *Deploy → New deployment* → type **Web app**.
   - Description: `ID register`
   - Execute as: **Me**
   - Who has access: **Anyone within Alpha Premier** if your Google
     Workspace offers it — then only signed-in company accounts can reach
     it. Otherwise **Anyone with the link**; see the warning below.
4. Authorise when asked, then copy the **Web app URL**. It looks like
   `https://script.google.com/macros/s/AKfy…/exec` and must end in `/exec`.

Check it: open that URL in a browser. You should see
`{"ok":true,"service":"apg-id-register","rows":0}` and a `Register` tab
appears in the Sheet.

## 2. Tell each computer about it

In the app, open **Issued IDs → Shared register**, paste the web-app
address and press **Test and save**. The app checks the address answers
before saving it, and reports how many cards the shared register holds.
**Use this computer only** switches it back off.

Do this once on each HR computer. No rebuild is needed, and the address is
remembered on that computer.

> **Why it is not in the source code.** With "Anyone with the link" access
> that URL *is* the password: anyone who has it can read the register and
> add rows. So it is entered per computer and never committed to the
> repository. If it leaks, open *Deploy → Manage deployments*, archive the
> deployment and create a new one — the old URL stops working.

## What changes in the app

- Typing a number now also asks the Sheet. If it is already there, the
  field turns red with who has it and when, and a **Replacement card** tick
  box appears for genuine re-prints.
- Every downloaded card — single or batch — is written to the Sheet, and
  still kept locally.
- If the Sheet cannot be reached (offline, wrong address, deployment
  archived), the app says so under the number field and falls back to the
  local list. It never blocks issuing a card.

## Importing what was issued before

Paste older IDs straight into the `Register` tab, one per row, with the
columns `id, year, seq, name, title, issuedAt, replacement`. `issuedAt` can
be a date cell. The `id` column is what the duplicate check matches on
(`APG-2026-122`).

## Changing the script later

After editing `Code.gs`, use *Deploy → Manage deployments → ✎ → Version:
New version → Deploy*. The URL stays the same, so the computers keep
working.
