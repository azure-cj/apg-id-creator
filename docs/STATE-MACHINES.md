# State machines in the Windows app

Every part of the native app (`windows/`) that can be in one of several
states, written down: the states, what must always be true, what moves
it from one to another, and the invalid states that were actually found
to be reachable. Paths are relative to `windows/`.

Each finding is marked **Confirmed** (traced through the code, with the
test that now fails without the fix) or **Suspected** (with what would
confirm it).

Machines:

1. [The form's steps](#1-the-forms-steps)
2. [The duplicate check](#2-the-duplicate-check)
3. [The shared register's index](#3-the-shared-registers-index)
4. [Writes to the shared register](#4-writes-to-the-shared-register)
5. [A photo being looked at](#5-a-photo-being-looked-at)
6. [Saving a print](#6-saving-a-print)
7. [The tool drawer and the map editor](#7-the-tool-drawer-and-the-map-editor)
8. [The card design kept on disk](#8-the-card-design-kept-on-disk)
9. [A stroke on the signature pad](#9-a-stroke-on-the-signature-pad)
10. [The pen tablet context](#10-the-pen-tablet-context)
11. [Light or dark](#11-light-or-dark)
12. [The sign-in to the shared register](#12-the-sign-in-to-the-shared-register)

Issues, ranked by what they could cost, are at the [end](#issues).

---

## 1. The form's steps

**Purpose.** Which of the six steps is open. **Code:** `IdMaker.Core/Session.cs`
(`FurthestStep`, `GoStep`, `NextStep`, `ChipPressed`, `AddAnother`,
`CancelAdding`, `RemoveFromQueue`, `ClearCard`).

**States.** `StepIndex` 0–5: Name, Job title, Number, Photo, Signature,
Check. On Check there are two sub-states the screen tells apart: a
finished card on the form (`Ready()`), or a blank form with cards in
`Queue` ("printing what is already queued").

**Invariants.**

- I1. `StepIndex ≤ FurthestStep()` — no step is open over an unfinished
  one — except after a forced move.
- I2. A forced move happens in exactly two places: `LoadExample` (the
  example is complete) and `CancelAdding` (the print it returns to is
  already made).
- I3. On Check there is always something to print: `JobCards()` is not
  empty.

| From | Event | Guard | To | Side effects | Code |
| --- | --- | --- | --- | --- | --- |
| any | Next | a step before this one is unfinished | first unfinished step | refusal names that step | `NextStep` |
| n | Next | this step unfinished | n | its fields marked touched; refusal names the reason | `NextStep` |
| n < 5 | Next | this step finished | n + 1 | arriving at 5 fixes `ReviewEntry` | `NextStep` → `GoStep` |
| any | chip i | i ≤ furthest | i | | `ChipPressed` |
| any | chip i | i > furthest | furthest | refusal names the step in the way | `ChipPressed` |
| n > 0 | Back | | n − 1 | | `Back` |
| 5 | Add another ID | card finished | 0 | card moved to `Queue`; form cleared | `AddAnother` |
| < 5 | Stop adding | queue not empty | 5 (forced) | form cleared, queue kept | `CancelAdding` |
| 5 | Remove queued card | it was the last, and the form is blank | 0 | | `RemoveFromQueue` |
| 5 | Start a new ID | print saved | 0 | form and queue cleared | `NewId` |
| any | register answers late | the number is a duplicate | 2 | refusal about step 3 | `SettleDuplicate` |

**Invalid states and how they are prevented.**

- *A later step open over an unfinished earlier one.* Every unforced move
  is capped in `GoStep`; `NextStep` and `ChipPressed` send the form back
  when something behind it has come undone.
- *Check open with nothing to print* — see issue 4.

---

## 2. The duplicate check

**Purpose.** What is known about the number on the form. **Code:**
`IdMaker.Core/Session.cs` (`RefreshDuplicate`, `SettleWhenLoaded`,
`SettleDuplicate`, `Duplicate`).

**States.** `DupShared`: `Free`, `Taken` (with `SharedEntry`), `Pending`,
`Unreachable`. Alongside it `DupLocal` (this computer's list) and the
queue of the print in progress.

**Invariants.**

- I1. `Pending` is a problem on the Number step: not knowing is not the
  same as knowing the number is free.
- I2. `Unreachable` is not a problem: the register being down, or nobody
  being signed in to it on this computer, cannot stop HR working, and
  the local list still catches repeats from this computer. The window
  says which it is under the number. What it lets through is caught at
  the write (machine 4): the register refuses a number it already holds.
- I3. An answer is applied only to the number it was asked about
  (`_dupToken`).
- I4. `Taken` blocks unless "Replacement card" is ticked; a number
  already in this print's queue blocks regardless.

| From | Event | Guard | To | Side effects | Code |
| --- | --- | --- | --- | --- | --- |
| any | number or year typed | no shared register, or number invalid | Free | token advanced | `RefreshDuplicate` |
| any | number or year typed | index in memory | Free / Taken | answered at once, no request | `RefreshDuplicate` |
| any | number or year typed | index not read yet | Pending | one read started | `RefreshDuplicate` |
| Pending | read succeeds | token unchanged | Free / Taken | may send the form back to step 3 | `SettleWhenLoaded` |
| Pending | read fails, or nobody is signed in | token unchanged | Unreachable | | `SettleWhenLoaded` |
| Pending | read finishes | token changed | (unchanged) | answer dropped | `SettleWhenLoaded` |

**Invalid transitions prevented.** Pressing Next twice quickly while
`Pending` cannot reach the Photo step (I1; test
`While_the_register_has_not_answered_the_number_is_not_cleared`). An
answer for a number since deleted is dropped (I3; test
`An_answer_for_a_number_that_has_since_changed_is_ignored`).

---

## 3. The shared register's index

**Purpose.** The whole shared register (a list in Firestore), read once
and kept in memory. **Code:** `IdMaker.Core/Register.cs` (`LoadIndex`,
`LoadIndexCore`, `FindSharedNow`, `NoteShared`).

**States.** Implicit, from two fields: `_index` (null or a dictionary)
and `_loading` (null or a task). *Unread* (null, null), *Reading*
(null, task), *Read* (index, null), *Refreshing* (index, task — only
after `LoadIndex(force: true)`).

**Invariants.**

- I1. Any number of callers while *Reading* share one request.
- I2. `_index` only ever holds what was read as the sign-in currently in
  force (machine 12). Signing in, signing out, or a sign-in being turned
  away empties it.
- I3. A number the register is known to hold stays *taken* in `_index`
  whatever its document looks like: the document's own name is the
  number.
- I4. `_loading` is a read that has not finished. A finished one is never
  kept as "in flight".

| From | Event | Guard | To | Side effects | Code |
| --- | --- | --- | --- | --- | --- |
| Unread | `LoadIndex` | signed in | Reading | pages of 300 read until there is no next page | `LoadIndex` |
| Unread | `LoadIndex` | no shared register, or nobody signed in | Unread | caller told "not read"; `LastProblem` says why; no request | `LoadIndex` |
| Reading | `LoadIndex` | not forced | Reading | same task returned | `LoadIndex` |
| Reading | every page read | sign-in unchanged | Read | | `LoadIndexCore` |
| Reading | every page read | sign-in changed meanwhile | Unread | rows dropped | `LoadIndexCore` |
| Reading | failure | | Unread | caller told "not read"; `LastProblem` says why | `LoadIndexCore` |
| any | sign in, sign out, sign-in turned away | | Unread | sign-in version advanced | `SignInChanged` |
| Read | card issued here | | Read | entry added, no re-read | `NoteShared` |
| Read | a card of ours refused | | Read | the number set to whoever holds it | `NoteRefused` |

**Invalid states found.** `_index` holding rows read as a sign-in no
longer in force — issue 2. A finished read kept as `_loading` — issue 7.

---

## 4. Writes to the shared register

**Purpose.** Getting every issued card into the shared register,
including those issued while it could not be reached, and never letting
one number become two cards' record. **Code:**
`IdMaker.Core/Register.cs` (`RecordShared`, `Create`, `SendWaiting`,
`NoteRefused`; the `register-outbox` and `register-refused` store keys).

**States per issued card.** *Local only* (in this computer's list),
*Waiting* (also in the outbox), *Shared* (its document is in the
register), *Refused* (the register holds that number for somebody else;
in the refused list until the operator has dealt with the card).

**Invariants.**

- I1. A card that was issued and is not in the register is in the outbox
  or the refused list. It is never simply dropped.
- I2. A card is in the outbox at most once.
- I3. The app only ever *creates* a document. It has no request that
  changes or removes one, and the register's rules allow none, so a
  number's first card is never overwritten.
- I4. A card is *Refused* only when the register was seen to hold that
  number for a different card. "Could not tell" is *Waiting*.
- I5. A refused card is never sent again: it would be refused for ever.
- I6. The same card sent twice is one card (same issue time, name and
  account: the answer to the first send was lost).

| From | Event | Guard | To | Side effects | Code |
| --- | --- | --- | --- | --- | --- |
| Local only | create attempted | created | Shared | | `Create` |
| Local only | create attempted | nobody signed in, out of reach, or an error | Waiting | kept on disk; operator told; `LastProblem` says why | `RecordShared` |
| Local only / Waiting | create refused: the number has a document | it is this same card | Shared | removed from outbox | `Create` |
| Local only / Waiting | create refused: the number has a document | "Replacement card" was ticked | Shared | a document created under the first card | `Create` |
| Local only / Waiting | create refused: the number has a document | it is somebody else's | Refused | removed from outbox; kept in the refused list; index set to the holder; operator told the card is a duplicate | `Create`, `NoteRefused` |
| Local only / Waiting | create refused, and no document is there | | Waiting | the account may not write: `LastProblem` says so | `Create` |
| Waiting | app starts or somebody signs in, register reached | create succeeds | Shared | removed from outbox | `SendWaiting` |
| Waiting | resend | still out of reach | Waiting | the rest are left for next time | `SendWaiting` |
| Refused | "These cards are dealt with" | | (gone) | refused list emptied | `ClearRefused` |

A create of a document that is already there is refused with 409 when
Firestore looks before asking the rules and with 403 when the rules
answer first. 403 is also what an account that may not write gets, so
the app never decides from the status alone: it reads the number's
document and decides from what is there.

**Invalid states found.** *Issued, not shared, and forgotten* — issue 1.
*Two cards on one number, the second written over the first* — issue 6.

---

## 5. A photo being looked at

**Purpose.** A chosen picture on its way through decoding, background
removal and placement, off the window's thread. The same shape applies
to a scanned signature being read. **Code:**
`IdMaker.App/MainWindow.xaml.cs` (`AcceptPhotoFile`,
`AcceptSignatureFile`), `IdMaker.Core/Session.cs` (`CardSerial`,
`AcceptPhoto`, `PhotoFailed`).

**States.** *Idle*, *Looking* (`_checkingPhoto`), then one of *Accepted*
(`Session.Photo` set), *Refused* (`PhotoError` set).

**Invariants.**

- I1. One picture at a time: a second is not started while *Looking*.
- I2. A result is applied only to the card it was started for.

| From | Event | Guard | To | Side effects | Code |
| --- | --- | --- | --- | --- | --- |
| Idle | picture chosen or dropped | wrong type or too large | Refused | reason shown | `AcceptPhotoFile` |
| Idle | picture chosen or dropped | | Looking | button says so and is disabled | `AcceptPhotoFile` |
| Looking | picture chosen again | | Looking | ignored | `AcceptPhotoFile` |
| Looking | finished | card serial unchanged, usable | Accepted | manual adjustment reset | `AcceptPhoto` |
| Looking | finished | card serial unchanged, not usable | Refused | reason shown | `AcceptPhoto` / `PhotoFailed` |
| Looking | finished | card serial changed | Idle | result dropped | `AcceptPhotoFile` |

**Invalid transition found.** A result landing on a different card —
issue 3.

---

## 6. Saving a print

**Purpose.** Turning the print on the Check step into a PDF and issuing
its cards. **Code:** `IdMaker.App/MainWindow.Print.cs` (`SavePrint`,
`WritePdf`), `IdMaker.Core/Session.cs` (`MarkIssued`, `RecordShared`).

**States.** *Reviewing*, *Saving* (`_saving`), *Saved* (`_saved`),
*Failed* (back to Reviewing with an error shown).

**Invariants.**

- I1. Cards are issued (written to the register, queue emptied) only
  after the PDF is complete on disk.
- I2. A half-written PDF never has the final file's name: it is written
  as `name.pdf.part` and renamed.
- I3. One save at a time.
- I4. The example card is never issued.
- I5. The cards issued are the cards in the PDF. What a print issues is
  fixed when the print is made (`Session.Print`, `PrintIssue`) and is not
  read from the form again when the write ends: the form stays live
  while the file is written (a queued card can be removed, a name typed
  again).

| From | Event | Guard | To | Side effects | Code |
| --- | --- | --- | --- | --- | --- |
| Reviewing | Save the PDF | a card to print | Saving | button says so and is disabled | `SavePrint` |
| Saving | Save pressed again | | Saving | ignored | `SavePrint` |
| Saving | file written and renamed | | Saved | the print's cards issued locally, as printed; shared writes begin | `Session.SavePrint` |
| Saving | write fails | | Reviewing | `.part` removed; error says how to recover; nothing issued | `WritePdf` |
| Saved | Start a new ID | | (step 1) | | `NewId` |

Saving as pictures never issues a card: pictures are a proof to look at.

**Checked and holding.** A print that cannot be saved issues nothing
(test `A_print_that_cannot_be_saved_issues_nothing`). A card removed
from the print, or a name typed again, while the file is written is
still issued as printed (tests
`A_queued_card_removed_while_the_file_is_written_is_still_issued_as_printed`,
`A_name_typed_again_while_the_file_is_written_does_not_change_what_is_issued`;
I5 was broken in both windows until untangle round 3). The web app could
not make this promise — a browser download gives no answer — so it
recorded the cards the moment the download was started.

---

## 7. The tool drawer and the map editor

**Purpose.** Which of the four tools is open, and whether the boxes on
the card are being dragged. **Code:** `IdMaker.App/MainWindow.Print.cs`
(`RenderTools`, `StartAdjusting`, `StopAdjusting`, the three design
buttons).

**States.** `_tool`: none, register, batch, printer, design. Editing:
`MapOverlay` visible or not.

**Invariants.**

- I1. Editing implies `_tool == design`. The "Keep this map" button lives
  in that drawer; without it there is no way to finish.
- I2. The design tool is only reachable on step 1 of a blank card with
  nothing queued (`Session.DesignAllowed`): a photo placed against one
  map is wrong for another.
- I3. While editing, the form is disabled.
- I4. The form is hidden only while a tool is open and the window is too
  narrow for rail, tool, card and form together (`FitColumns`, derived on
  every render and resize, never stored). Closing the tool always brings
  it back.

| From | Event | Guard | To | Side effects | Code |
| --- | --- | --- | --- | --- | --- |
| any tool | rail button | same tool | none | focus returns to the rail | `Rail_Click` |
| any | rail button | different tool | that tool | editing stopped if it was on | `RenderTools` |
| design | Show the map | design allowed | design + editing | snapshot taken for Discard | `StartAdjusting` |
| editing | Keep this map | | design | saved, or cleared if nothing moved | `StopAdjusting` |
| editing | Discard changes | | design | snapshot restored | `BtnTplCancel_Click` |
| editing | Close, Escape, another tool | | that state | changes kept, as with Keep | `RenderTools` |
| design | card started | | none | drawer closed | `RenderTools` |

**Checked and holding.** Closing the drawer by another route while
editing does not strand the boxes on the card with the form disabled
(test `Every_tool_opens_and_the_map_can_be_moved_kept_and_reset`). This
is the bug the web app's `renderTools` comment describes; the one place
that enforces it was carried over.

---

## 8. The card design kept on disk

**Purpose.** A moved map and replaced artwork surviving a restart.
**Code:** `IdMaker.Core/Geometry.cs` (`TemplateStore.Save`, `Restore`,
`Clear`), `IdMaker.App/MainWindow.xaml.cs` (`Boot`),
`IdMaker.App/MainWindow.Print.cs` (`UseArtwork`, `BtnTplReset_Click`).

**States.** Two things on disk: `template.json` (the record) and
`template-front.png` / `template-back.png` (the pictures). *Built-in*
(neither), *Map moved* (record, `artwork: false`), *Own artwork* (record
with `artwork: true`, and both pictures).

**Invariant.** A record saying `artwork: true` is only acted on when
both pictures open.

| From | Event | Guard | To | Side effects | Code |
| --- | --- | --- | --- | --- | --- |
| any | Use this artwork | both card-shaped | Own artwork | pictures written first, then the record | `UseArtwork` |
| any | Keep this map | something moved | Map moved / Own artwork | record written | `StopAdjusting` |
| any | Go back to the built-in design | confirmed | Built-in | record removed, then pictures | `BtnTplReset_Click` |
| on start | record unreadable or incomplete | | Built-in | record removed | `TemplateStore.Restore` |
| on start | record says artwork, a picture will not open | | Built-in | record removed | `Boot` |

**Failure between two writes.** Pictures are written before the record,
and the record is removed before the pictures. A crash in between leaves
pictures with no record — harmless, nothing points at them — never a
record with no pictures that is trusted.

---

## 9. A stroke on the signature pad

**Purpose.** One line being drawn. **Code:** `IdMaker.App/SignaturePad.cs`.

**States.** *Idle* (`_current` null), *Pen stroke* (`_current` set,
`_stylusStroke` true), *Mouse stroke* (`_current` set, `_stylusStroke`
false).

**Invariants.**

- I1. One stroke at a time, from one kind of device.
- I2. Every stroke that begins also ends, and ends exactly once: losing
  the pointer mid-stroke finishes it rather than leaving it open.
- I3. Points stay inside the pad (they are clamped to it).

| From | Event | Guard | To | Side effects | Code |
| --- | --- | --- | --- | --- | --- |
| Idle | pen down | pad can be drawn on | Pen stroke | pen captured | `OnStylusDown` |
| Idle | mouse down | not a pen in disguise | Mouse stroke | mouse captured | `OnMouseLeftButtonDown` |
| Pen stroke | mouse events | | Pen stroke | ignored: Windows sends both for a pen | mouse handlers |
| stroke | move | moved more than jitter | stroke | point added, with pressure if there is any | `Extend` |
| stroke | up | | Idle | stroke kept; `InkChanged` | `End` |
| stroke | capture lost | | Idle | stroke kept; `InkChanged` | `OnLost…Capture` |
| stroke | button found released on a move | | Idle | stroke kept | `OnMouseMove` |
| any | Clear | | Idle | strokes dropped; `InkChanged` | `Clear` |

`End` does nothing when no stroke is open, which is what makes "exactly
once" hold when an up and a lost-capture both arrive.

**Around the pad: holding the pointer.** `SignWindow` (Windows, in
`SignaturePad.cs`; Mac, `SignWindow.cs`) is *Free* or *Holding*
(`_holding`). While Holding, the pointer is clipped to the pad (Windows
only; the Mac cannot clip it), a click past the pad presses nothing, and
only Enter, Backspace, Delete and Esc do anything.

| From | Event | Guard | To | Side effects | Code |
| --- | --- | --- | --- | --- | --- |
| Free | stroke starts | | Holding | pointer clipped to the pad; ring shown; status says how to finish | `HoldOnPad` |
| Holding | Enter | pad has ink | closed | signature kept | `KeysWhileHolding`, `Finish` |
| Holding | Backspace or Delete | | Holding | strokes dropped | `KeysWhileHolding` |
| Holding | Esc | | Free | pointer clipped to the window again; ring hidden | `LetGo` |
| Holding | window loses focus | | Holding | pointer let go; taken again on return | `Deactivated`, `Activated` |
| any | window closes | | closed | pointer let go | `Closed` |

---

## 10. The pen tablet context

**Purpose.** The connection to the tablet driver's WinTab interface while
the signing window is open. **Code:** `IdMaker.App/PenTablet.cs`.

**States.** *None* (no driver, no tablet, or it refused), *Open and
listening* (window active), *Open and paused* (window not active),
*Closed* (`_context` zero).

**Invariants.**

- I1. Nothing depends on it: in every state signing works, by speed when
  there is no pressure to be had.
- I2. A pressure value is only used while it is fresh (120 ms) and the
  pen has not left the tablet.
- I3. Once *Closed*, no call into the driver is made again.

| From | Event | Guard | To | Side effects | Code |
| --- | --- | --- | --- | --- | --- |
| None | signing window opens | driver present, tablet answers, reports pressure | Open and listening | window hooked for packets | `TryOpen` |
| listening | packet | for this context | listening | pressure and time kept | `Hook` |
| listening | pen leaves the tablet | | listening | pressure marked stale | `Hook` |
| listening | window loses focus | | paused | | `SetActive(false)` |
| paused | window regains focus | | listening | context brought to the top | `SetActive(true)` |
| open | window closes | | Closed | hook removed, context closed | `Dispose` |
| open | the driver faults | | Closed | as above | `Hook`, `SetActive` |

---

## 11. Light or dark

**Purpose.** Which palette the window is drawn in. **Code:**
`IdMaker.App/ThemeManager.cs`, `IdMaker.App/App.xaml.cs` (`OnStartup`),
`IdMaker.App/MainWindow.xaml.cs` (constructor).

**States.** `ThemeManager.Current`: *Light* or *Dark*. The palette in
`Application.Resources.MergedDictionaries[0]` is `Colors.Light.xaml` or
`Colors.Dark.xaml`.

**Invariants.**

- I1. `Current` and the palette in use always agree: `Apply` is the only
  place either changes, and it changes both.
- I2. Exactly one palette is merged. `Apply` replaces the one there, and
  only inserts when none is.
- I3. Every window's title bar matches `Current`: `Apply` sets every open
  window's, and a window sets its own as it is created.

| From | Event | Guard | To | Side effects | Code |
| --- | --- | --- | --- | --- | --- |
| (start) | app starts | Windows set to dark | Dark | palette swapped before the window exists | `OnStartup` |
| (start) | app starts | light, or the setting cannot be read | Light | | `OnStartup` |
| any | Windows announces `ImmersiveColorSet` | the setting now differs from `Current` | the setting | palette swapped; title bars set; window redrawn | `Follow` |
| any | Windows announces `ImmersiveColorSet` | the setting is unchanged | (unchanged) | nothing: the same message also means an accent-colour change | `Follow` |

**Invalid state prevented.** A window left light after the switch
because it took a brush by value. Every colour is a `DynamicResource` or
`Theme.Paint`, and `ThemeTests` fails on a `StaticResource` colour or a
`Theme.Brush` call. The window's `Changed` handler is removed when it
closes, so a closed window is never asked to redraw.

## 12. The sign-in to the shared register

**Purpose.** Who this computer is to the register. Asked once, kept
between runs, renewed without asking. **Code:**
`IdMaker.Core/Register.cs` (`SignIn`, `SignOut`, `Token`, `Renew`,
`SignInState`, `KeepSignIn`), `IdMaker.App/WindowsKeeper.cs`,
`IdMaker.App/MainWindow.Print.cs` (`RenderSignIn`).

**States.** *Signed out* (nothing kept), *Signed in, pass good* (a
renewal token kept on disk, wrapped; a pass in memory, not yet run out),
*Signed in, pass run out* (the renewal token only), *Renewing* (one
request out for a new pass), *Turned away* (the renewal token was
refused: back to *Signed out*, with `SignInProblem` saying to sign in
again).

**Invariants.**

- I1. The password is never kept: not on disk, not in memory after the
  request, not in the box it was typed in.
- I2. What is on disk can be opened only by the same Windows account on
  the same computer. With no way to wrap it, nothing is written and the
  sign-in lasts for the run.
- I3. A sign-in that is not a member of the company's records is not
  kept.
- I4. Any number of requests needing a new pass share one renewal.
- I5. Nothing is sent to the register while *Signed out*.
- I6. Every change of sign-in empties the index (machine 3) and makes
  answers still in flight for the old one worthless.
- I7. Signing out, or being turned away, loses no card: the outbox
  (machine 4) is the computer's, not the account's.

| From | Event | Guard | To | Side effects | Code |
| --- | --- | --- | --- | --- | --- |
| Signed out | app starts | a kept sign-in opens | Signed in, pass run out | | `SignInState` |
| Signed out | app starts | none kept, or it will not open (another account, damaged) | Signed out | | `SignInState` |
| Signed out | Sign in | accepted, and a member | Signed in, pass good | renewal token wrapped and written; version advanced; waiting cards sent | `SignIn` |
| Signed out | Sign in | wrong email or password, turned off, too many tries, not a member, out of reach | Signed out | told which, and what to do | `SignIn`, `SignInRefusal` |
| Signed in, pass good | an hour passes | | Signed in, pass run out | | `Token` |
| Signed in, pass run out | a request needs a pass | | Renewing | one renewal request | `Token` |
| Signed in, pass good | the register answers 401 | | Renewing | renewed once, the request sent again | `Ask` |
| Renewing | renewed | sign-in unchanged | Signed in, pass good | a new renewal token, if one came, is written | `Renew` |
| Renewing | refused: expired, turned off, removed | sign-in unchanged | Turned away | kept sign-in removed; version advanced; `SignInProblem` set | `Renew` |
| Renewing | out of reach | | Signed in, pass run out | the request fails as "not reached"; nothing is forgotten | `Renew` |
| Renewing | answer | sign-in changed meanwhile | (unchanged) | answer dropped | `Renew` |
| any signed in | Sign out | | Signed out | kept sign-in removed; version advanced | `SignOut` |

**Invalid states prevented.** A pass or rows from a sign-in no longer in
force being used after it changed (I6; tests
`Signing_out_while_a_read_is_in_flight_does_not_keep_what_it_read`,
`A_sign_in_that_is_no_longer_accepted_asks_again_and_loses_no_card`). A
readable sign-in on disk (I2; tests
`Signing_in_is_asked_once_and_remembered_wrapped_never_in_plain_text`,
`Without_windows_protection_the_sign_in_lasts_for_this_run_only`).

---

## Issues

Ranked by what each could cost. All were fixed with a test that fails
without the fix.

### 1. A card saved while the register was down was never written to it — Confirmed

**Machine 4.** `RecordShared` returned `false` on failure and nothing
kept the entry (this is how the earlier web app behaved too: its
`recordShared` swallowed the failure). The card was issued, printed
and recorded on one computer, and no other computer would ever learn the
number was taken — the duplicate the whole register exists to prevent.

**Fix.** Failed writes go to an outbox on disk and are sent again the
next time the app starts with the register in reach; the operator is
told at the time. **Tests:**
`A_card_saved_while_offline_is_kept_and_sent_when_the_register_is_back`,
`A_card_saved_before_anyone_signed_in_waits_for_the_sign_in`.

### 2. Rows read as one sign-in kept after it changed — Confirmed

**Machine 3.** First found with the old register, where the thing that
could change was its address: `SetUrl` cleared `_index` and `_loading`,
but a read already in flight finished afterwards and stored the *old*
sheet's rows as the index for the new address. With Firestore the thing
that can change is who is signed in, and the same race is there: sign
out (or have the sign-in turned away) while the opening read is still
out, and its rows would be kept.

**Fix.** Each read carries the sign-in version it started under and
stores nothing if that has moved on. **Test:**
`Signing_out_while_a_read_is_in_flight_does_not_keep_what_it_read`.

### 3. A photo still being looked at could land on a different card — Confirmed

**Machine 5.** With a card queued, choose a photo for the next one and
press "Stop adding and go back to the print" before it finishes (large
photos take a second or more with background removal). `CancelAdding`
cleared the form; the result then arrived and was applied to the blank
form, leaving a half-started card on the Check step that the queued
print no longer matched.

**Fix.** `Session.CardSerial` goes up whenever the form is cleared; a
result whose serial is stale is dropped. The same guard covers a scanned
signature. **Test:**
`Putting_the_card_aside_changes_its_serial_so_late_work_for_it_can_stand_down`
covers the serial; the window's use of it is two lines traced by hand
(`AcceptPhotoFile`, `AcceptSignatureFile`).

### 4. The Check step left open with nothing to print — Confirmed

**Machine 1, invariant I3.** After "Stop adding" the form is blank and
the Check step shows the queue. Removing the last queued card from there
left the Check step open with no cards: a Save button with nothing to
save. Present in the earlier web app (its `removeFromQueue`).

**Fix.** Removing the last card from a blank Check step returns to
step 1. **Test:**
`Cancelling_an_extra_id_returns_to_the_print_that_was_waiting`.

### 5. Cards issued before the file was known to be saved — Confirmed (in the web app; prevented here)

**Machine 6, invariant I1.** Not a bug in this code — a property the
port had to establish rather than inherit. See machine 6.

### 6. Two cards on one number, the second written over the first — Confirmed (in the old register; prevented here)

**Machine 4, invariant I3.** The form checks a number against the
register as it was last read. Two computers that both read it while a
number was free could both issue that number. The old register's script
then *replaced* the first card's row with the second's
(`if (existing) … setValues`), so the register showed one holder, the
wrong one, and nobody was told. Two people held the same number and the
record of the first was gone.

**Fix.** The number is the document's name and the document is only
created. The second create is refused, the first card stands, and the
second computer is told at once that its card is a duplicate and must
not be handed out; the card stays listed until someone has dealt with
it. The rules refuse any change or removal as well, so no other program
signed in as a member can do what the script did. **Tests:**
`A_number_another_computer_took_first_is_refused_and_is_not_sent_again`,
`The_form_says_a_refused_card_is_a_duplicate_and_must_not_be_handed_out`;
in the Keystone repository, `e2e/cloud/id-register.e2e.ts`.

**What it does not do.** The duplicate card has already been made when
the refusal arrives, because the card is recorded after its PDF is
saved (machine 6, I1: nothing is issued until the file is known to be
saved). Reserving the number before saving would close that, at the
cost of numbers spent on prints that then fail. Not done.

### 7. A read that finished at once kept as "in flight" — Confirmed (not reachable over a real network)

**Machine 3, invariant I4.** `LoadIndex` stored the task it had just
started as `_loading`. A task that had already finished by then (its
`finally` had already cleared `_loading`) was put back, and a failed
one would then be handed to every later caller: the register would stay
"not read" until the app was restarted. A request over a real network
never finishes before `LoadIndex` returns, so this needed a stand-in
that answers at once, which is what the tests use; the same shape was
in the new code that renews a sign-in.

**Fix.** A task that has already finished is not kept, for the read and
for the renewal. **Test:**
`A_sign_in_that_is_no_longer_accepted_asks_again_and_loses_no_card`
(the read after the sign-in is turned away must be a new one).

### Not confirmed

- **Suspected: a tablet driver that stops sending pointer events to a
  window once a WinTab context is open on it.** Some drivers treat the
  two as exclusive. If XP-Pen's does, a pen with "Windows Ink" ticked
  would draw with the pressure from WinTab (through the mouse path)
  rather than fail, because the pad falls back by design — but this has
  not been tried on the hardware. **What would confirm it:** signing
  with the Deco Fun, Windows Ink on, and seeing no pressure variation.
