# Shared register

Every card the ID Maker issues is written to one Google Sheet, and every
number typed is checked against it. That sheet, not the laptop, is the
record of who holds which ID.

**It is already set up and built into the app — there is nothing to do.**
These notes are for rebuilding it, moving it, or replacing the address.
Set-up takes about ten minutes and needs no server.

## 1. Deploy the script in your Sheet

1. Open the register spreadsheet in Google Sheets.
2. *Extensions → Apps Script*. Delete the sample code, paste the whole of
   `Code.gs` from this folder, and save.
3. Rename the project from *Untitled project* to `APG ID Register` — that
   name is what Google shows on the permission screen and in the account's
   list of connected apps, so leaving it blank makes it unidentifiable
   later.
4. *Deploy → New deployment* → type **Web app**.
   - Description: `ID register`
   - Execute as: **Me**
   - Who has access: **Anyone**. The dropdown offers *Only myself*
     (the default), *Anyone with Google account* and *Anyone*, and only
     the last one works. The app calls the script from the browser
     without sign-in cookies, so either of the other two answers with a
     redirect towards a login page — and a login page sends no
     `Access-Control-Allow-Origin` header, so the browser blocks it and
     every check fails. Opening the address in your own browser tab is
     not a test of this: you are signed in there, so it answers you
     whatever the setting. See the warning below for what "Anyone" means.
5. Google asks for permission and warns that *this app hasn't been verified*.
   That is expected: the "app" is the script you just pasted into your own
   spreadsheet, and the developer it names is you. Click **Advanced**, then
   **Go to APG ID Register (unsafe)**, then **Allow**. It is asking for
   Sheets access because it reads and writes the `Register` tab.
6. Copy the **Web app URL**. It looks like
   `https://script.google.com/macros/s/AKfy…/exec` and must end in `/exec`.

Check it: open that URL in a browser. You should see
`{"ok":true,"service":"apg-id-register","rows":0}` and a `Register` tab
appears in the Sheet.

## 2. Tell the app about it

Put the web-app address in `CONFIG.register.url` in `src/config.js` and run
`node build.mjs`. There is no setting for it in the app: it is on, and it
cannot be switched off by accident.

> **Treat that address as a password.** The deployment is open to "Anyone",
> so whoever holds the address can read every issued ID and add rows. It is
> in the source because this repository is private and one laptop issues
> the cards — those two facts are the whole justification, and if either
> changes the address should come back out.
>
> If it leaks, or the repository is made public: *Deploy → Manage
> deployments*, archive the deployment, create a new one, and put the new
> address in `config.js`. The old one stops working the moment it is
> archived.

For a development copy pointed at a local stand-in, set
`localStorage['apg-id-register-url']` in the browser console; it overrides
the built-in address without touching the source.

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

Paste older IDs straight into the `Register` tab, one per row. The full
column order is:

```
id, year, seq, name, title, issuedAt, replacement, recordedBy,
last, first, middle, suffix, role1, role2
```

Only **`id`** has to be filled in — that is the column the duplicate check
matches on (`APG-2026-122`). `name` and `issuedAt` make the warning message
useful ("already issued to … on …"), and `issuedAt` can be a date cell. The
rest can be left blank for historical rows.

## Changing the script later

After editing `Code.gs`, use *Deploy → Manage deployments → ✎ → Version:
New version → Deploy*. The URL stays the same, so the computers keep
working.
