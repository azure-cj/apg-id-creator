# Shared register

Every card the ID Maker issues is written to one shared register, and
every number typed is checked against it. The register, not the laptop,
is the record of who holds which ID.

It is a list in the company's Firebase project, `apg-keystone`, the same
project APG Keystone keeps its records in. HR signs in to it with their
Keystone account, once on each computer.

It replaced a Google Sheet behind an Apps Script web app (removed on
17 Nov 2026, in git history before that). That web app was open to
anyone who held its address, so the address worked as a password. Now
nothing in the app or in this repository opens the register: a person's
sign-in does.

## For whoever issues cards

1. Ask the CEO for a Keystone account, if you do not have one: in
   Keystone, **Settings › User Accounts › Make account**.
2. In the ID Maker, click **Issued IDs** on the left, type that email and
   password under **Shared register**, and click **Sign in**.

That is asked once on each computer. From then on every number is checked
against every computer's cards, and every card you save is written to
the register under your name.

- **Forgot the password?** Reset it from Keystone's sign-in screen
  (**Forgot password?**), then sign in here again.
- **"This account may not use the ID register."** The account exists but
  the CEO has not added it in Keystone, or has removed it.
- **Not signed in, or offline?** The app still works. It checks numbers
  against this computer only and says so, keeps each card you save, and
  writes them to the register by itself once it is signed in and online.

## What is kept, and where

In Firestore, one document per employee number, named by the number:

```
keystone/apg/idreg/APG-2026-124
keystone/apg/idreg/APG-2026-124/replacements/20261201T020000000Z
```

| Field | What |
| --- | --- |
| `id`, `year`, `seq` | the employee number and its two parts |
| `name`, `title` | as printed on the card |
| `last`, `first`, `middle`, `suffix`, `role1`, `role2` | as typed |
| `issuedAt` | when the PDF was saved, ISO 8601, UTC |
| `replacement` | whether it was issued as a replacement card |
| `kind` | `intern` or `employee`: who the card is for. Left out on cards issued before the form asked (v2.0.3 and earlier) |
| `by`, `byEmail` | the account that issued it |

**No photograph and no signature.** Neither ever leaves the computer.

`kind` is for the attendance system, which keeps interns only: it is how
a new intern can be added there from the register (not built yet, as of
v2.0.4). The form asks it of every card and never guesses.

On the computer, in `%LOCALAPPDATA%\Alpha Premier\ID Maker\`:

| File | What |
| --- | --- |
| `register-signin.json` | this computer's sign-in, wrapped by Windows (DPAPI) so only the same Windows account on the same computer can open it. It holds the renewal token Firebase gave, never the password |
| `register-outbox.json` | cards issued but not yet written to the register |
| `register-refused.json` | cards the register refused because the number was already somebody else's |

## Why a number cannot be issued twice

- **Each number is one document, and it is only ever created.** The app
  asks Firestore to *create* `idreg/APG-2026-124`. If that document is
  already there, the create is refused. Two computers issuing the same
  number at the same moment cannot both succeed.
- **The rules allow nothing else.** A member may read the register and
  create a document, named by its own number and signed as themselves.
  Nobody may change or delete one from an app, the CEO included.
- **A replacement card is the one exception, and is recorded as one**:
  a new document under the card it replaces, which stays as it was.
- **When the app is told a number was taken**, it says so at once: the
  card just made is a duplicate, is not to be handed out, and is listed
  under **Issued IDs** until someone has dealt with it.

The form's own check (the number box turning red) reads the register
once and answers from memory, so it is fast. The create is the check
that cannot be wrong.

## The rules

They live in the Keystone repository, with the rest of that project's
rules: `firestore.rules` in `Deign86/apg-keystone`, the `idreg` block
(its `docs/adr/0025` says why). They are deployed from there:

```bash
npx -y firebase-tools@latest deploy --only firestore:rules --project apg-keystone
```

The app sends exactly the fields in the table above. If the rules'
`issuedId` function and that table ever disagree, every write is refused
and the cards wait in the outbox: change both together.

## How the app talks to it

Over HTTPS with `HttpClient`, with no Firebase package
(`windows/IdMaker.Core/Register.cs`):

| What | Request |
| --- | --- |
| Sign in | `POST identitytoolkit.googleapis.com/v1/accounts:signInWithPassword` |
| Renew the hour-long pass | `POST securetoken.googleapis.com/v1/token` |
| Read the register | `GET firestore.googleapis.com/v1/projects/apg-keystone/databases/(default)/documents/keystone/apg/idreg`, 300 a page |
| Issue a number | `POST …/idreg?documentId=APG-2026-124` (a create) |
| Record a replacement | `POST …/idreg/APG-2026-124/replacements?documentId=<issue time>` |

REST is enough: the app reads one list and creates documents. It needs
no live updates and no offline cache beyond its own outbox, which are
what the Firebase SDK would add.

## Telling the app which project

The project's name and its web API key are not written in the source.
The release build puts them into the app from two repository secrets:

| Secret | Value |
| --- | --- |
| `FIREBASE_PROJECT_ID` | `apg-keystone` |
| `FIREBASE_API_KEY` | the web API key: Firebase console → Project settings → Your apps → the Web app's `apiKey` |

Set them once, with the GitHub CLI:

```bash
gh secret set FIREBASE_PROJECT_ID --repo Deign86/apg-id-creator --body "apg-keystone"
```

```bash
gh secret set FIREBASE_API_KEY --repo Deign86/apg-id-creator
```

The second asks for the value: paste the key and press Enter. Or add
both under the repository's **Settings → Secrets and variables →
Actions → New repository secret**.

Every release built after that has the register built in. A release
without them stops with a message saying so, so an app with no shared
register is never shipped by accident. Builds on a developer's computer,
and the checks run on pull requests, have no shared register: they keep
their own list.

Neither value opens the register. Signing in does, and the rules decide
what a sign-in may do. Anyone holding the released `.exe` can read both
values out of it, because the app has to send them; that gives them
nothing without an account the CEO made.

## Moving to another project

Change the two secrets, deploy the rules there, make the accounts, and
release a new version. Every computer signs in once more. To carry the
cards over, export them from the old project and run the copy below
against the new one.

## Copying the old Sheet's rows in

One-off, after the rules are deployed. Needs Node 18 or later.

1. Open the old spreadsheet, go to its **Register** tab, and choose
   *File → Download → Comma-separated values (.csv)*.
2. Say which project, the same two values as the secrets above (on
   Windows PowerShell, `$env:NAME = "value"` instead of `export`):

   ```bash
   export IDMAKER_FIREBASE_PROJECT_ID="apg-keystone"
   export IDMAKER_FIREBASE_API_KEY="<the web API key>"
   ```

3. See what would be copied, without writing anything:

   ```bash
   node register/migrate.mjs Register.csv --dry-run
   ```

4. Copy it:

   ```bash
   node register/migrate.mjs Register.csv
   ```

It asks for a Keystone email and password, prints how many numbers the
new register holds before and after, and never changes a number that is
already there. Run twice, it copies nothing twice. A later row for the
same number goes under the first as a replacement.

Then archive the Apps Script deployment (*Deploy → Manage deployments →
Archive*) so the old address stops working, and delete the `REGISTER_URL`
repository secret, which nothing reads any more.
