# What a review of this repository looks for

For whoever reviews a change here, person or agent. It is read from
`main`, never from the change under review: a change does not get to edit
the list it is judged by. `AGENTS.md` has the rules for writing a change;
this is what to check once one is written.

Each item says when it applies (readable from the diff), what to check,
and where it came from. Check only the items whose trigger matches.

Tag each finding **Important** (would cost a wrong card, a lost record,
exposed personal data, or a broken release), **Nit**, or **Pre-existing**
(was already there; not held against this change).

## The register

| When the diff touches | Check | From |
| --- | --- | --- |
| A new field on an issued ID | The rules in apg-keystone accept it and are published before the app that sends it is released; otherwise every card waits in the outbox | `kind`, v2.0.4 |
| `Register.cs` requests, or anything sent to Firestore | The app still only *creates* documents: no PATCH, PUT or DELETE. The fields sent are exactly those in `register/README.md` and in `issuedId` in apg-keystone's `firestore.rules` | STATE-MACHINES issue 6: the old register wrote a second card over the first |
| How a refused write is handled | A refusal is decided by reading the number's document, never from the status alone (409 and 403 both mean "already there"; 403 also means "not a member") | Pull request 9 |
| `_index`, `_loading`, `_refreshing`, or sign-in state | Every change of sign-in advances `_version`, and a task already finished is never kept as "in flight" | STATE-MACHINES issues 2 and 7 |
| The outbox or the refused list | A card that was issued is in the register, the outbox or the refused list. Nothing drops one | STATE-MACHINES issue 1 |
| `Config.cs`, a workflow, a script, a document | No project name, API key, password, token or key file is written in. `SecretTests` passes | The Apps Script address in git history |
| Sign-in | The password is not kept: not on disk, not in a field, not in a log. What is kept goes through `ISecretKeeper` | AGENTS.md |

## The card

| When the diff touches | Check | From |
| --- | --- | --- |
| `CardRenderer.cs`, `TextFit.cs`, `Geometry.cs`, fonts, `assets/` | `RenderTests` still pass against `test/golden-front.png`. If the card moved on purpose, the commit says so | AGENTS.md |
| Photos or signatures | Nothing stores one, sends one, or writes one to a log, a test report or the register | AGENTS.md |
| `Session.cs` steps or `MarkIssued` | Nothing is issued until the PDF is known to be saved | STATE-MACHINES issue 5 |
| `SavePrint`, `MarkIssued`, or anything read after an `await` in a job | What is issued or written is what was fixed before the wait (the print job's own cards), never the form read again afterwards: the form stays live while a file is written | Untangle round 3: a card removed during the write was printed and not issued |

## The code

| When the diff touches | Check | From |
| --- | --- | --- |
| Any `.csproj` | No new package without the reason written where it is added | AGENTS.md |
| `IdMaker.App/*.cs` | A rule about the card or the form is in Core with a test, not in the window | AGENTS.md |
| A cast, or a `catch` | The cast has `// SAFETY:`. The catch names a type and tells the operator what to do next | AGENTS.md |
| `Theme.xaml`, `Colors.*.xaml`, `MainWindow.xaml` | `ThemeTests` pass; gold only on the forward button and the current step; no `StaticResource Color.` | windows/README.md |
| Any test | No test was skipped, given a sleep, or retried until it passed. A fixed wait is a finding; wait for the thing itself | AGENTS.md, Verification |
| `docs/STATE-MACHINES.md` territory (a state or transition changed) | The document changed with it | AGENTS.md |

## How the change was verified

The commit says, in a `Verified:` line, what was run and what it showed,
or `Verified: none` and why. A claim with no run behind it is an
**Important** finding.
