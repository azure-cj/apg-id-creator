# For agents working in this repository

Alpha Premier ID Maker makes employee ID cards. The app is the native
Windows program in `windows/` (C#, .NET 8, WPF) and the native Mac program
in `mac/` (C#, .NET 10, AppKit), which share `windows/IdMaker.Core`. Start
with `windows/README.md` and `mac/README.md`; the people who use the app
read `README.md`.

It replaced a web app (plain HTML and TypeScript in a Tauri window),
removed on 17 Nov 2026 and still in git history. `test/golden-front.png`
is the card that web app printed, and
`windows/IdMaker.Tests/Fixtures/web-reference.json` the numbers it
produced; the native app is held to both.

## Rules

- **Native only.** No web frontend, no browser shell, no WebView. A web
  skill's advice is translated into WPF and XAML (or AppKit), not followed
  literally.
- **The two apps do the same things.** A step, a tool or a message added
  to one window is added to the other in the same piece of work, or the
  commit says what is left and why.
- **Prefer what is already there**: the .NET standard library and
  Windows' own features, before a package. The one dependency is ONNX
  Runtime, for background removal. Adding another needs a reason written
  down where it is added.
- **`IdMaker.Core` decides, `IdMaker.App` shows.** A rule about the card
  or the form goes in Core with a test that runs without a window. The
  window draws the `Session` and passes on what the operator does.
- **The printed card must not move by accident.** Anything that can
  change what is drawn is checked by `IdMaker.App.Tests` against
  `test/golden-front.png`. If it moves on purpose, say so in the commit.
- **Photographs are personal data.** Never store one, never send one
  anywhere, never write one to a log or a test report.
- **Nothing about the register is in the source.** Which Firebase project
  it is (its name and web API key) comes from the repository secrets
  `FIREBASE_PROJECT_ID` and `FIREBASE_API_KEY` when a release is built.
  What opens it is a person's sign-in, which the app keeps wrapped by
  Windows (DPAPI) or by the Mac (a key in the Keychain). A password, a token, a key or a service-account file
  is never in the source or in a plain file. `SecretTests` holds this.
- **Tests never call the real project.** `Make.LocalRegister`,
  `FakeFirebase` and `FakeHttp` stand in, on a project that does not exist.
- **An issued ID is created, never changed.** The app has no request that
  updates or deletes one, and the rules (`firestore.rules` in
  `Deign86/apg-keystone`) allow none. A change to what the app sends is
  made there in the same piece of work; see `register/README.md`.
- **Every cast carries a `// SAFETY:` comment** saying why it cannot
  fail. A deliberate shortcut carries `// shortcut: <its limit>, <when to
  revisit>`.
- **An exception is caught by type, where something can be done about
  it**, and the operator is told what to do next in plain words. Nothing
  is swallowed.
- Never commit a password, a key or an `.env` file. Never force-push
  `main`.

## Verification

A change is not done because it was written. It is done when it was run
and seen to work.

- **Run the change, not only its tests.** For the window, that is the
  window tests (`IdMaker.App.Tests`, which drive the real window) or the
  app itself on Windows. For the register, that is a card issued and a
  duplicate refused.
- **Three kinds of test are worth the most here.** End to end: the
  window tests, one per thing an operator does. Property: a statement
  that must hold for any input, tried against many (`PropertyTests.cs`).
  Differential: the native app against what the web app produced
  (`web-reference.json`, `golden-front.png`).
- **Cheap checks first.** Build and the Core tests before a push; the
  Windows tests run in CI on the pull request.
- **Three rounds, then a person.** If a check still fails after three
  attempts at a fix, stop and say what is failing and what was tried.
- **A flaky test is a bug.** Never skip it, add a sleep, or run it again
  until it passes. Find the cause. If the cause cannot be found, delete
  the test and write down why.
- **A change does not edit its own judge.** `REVIEW.md` is read from
  `main`. A pull request that changes a check and the code it checks
  says so at the top.
- **Every commit says how it was verified**, in its last lines:
  `Verified: dotnet test windows/IdMaker.Tests, 125 passed` or
  `Verified: none (documents only)`. Never claim a run that did not
  happen.
- **A miss becomes a rule.** A bug that got past review is added to
  `REVIEW.md` with what in a diff would have shown it. Rules nobody has
  needed are pruned.

## Checks

```bash
dotnet build windows/IdMaker.sln
```

```bash
dotnet test windows/IdMaker.Tests
```

`IdMaker.App.Tests` needs Windows and runs in CI
(`.github/workflows/windows.yml`), which also packages the installer and
proves it installs and starts. The Mac app needs a Mac and is built,
tested (`--selftest`, `--windowtest`) and packaged in CI
(`.github/workflows/mac.yml`). Done means these pass locally, the change
is pushed, CI is watched, and anything it finds is fixed.

`GATES.md` lists what the native rebuild had to prove and what is still
waiting on a person with the hardware.

## How the Notion skills apply here

| Skill | Here |
| --- | --- |
| unlazy | `GATES.md`: gates written as commands with expected output, re-run before calling anything done |
| state-machine-bug-hunt | `docs/STATE-MACHINES.md`, kept current when a state or transition changes |
| untangle-codebase | the Core / App split; one place per decision (`FurthestStep`, `RenderTools`) |
| ponytail-review, ponytail-audit | findings as *what it is, the problem, the fix, what happens if skipped*; `shortcut:` comments |
| security-audit | findings only where a lower-trust input crosses a boundary: files opened or dropped, the register's answers, the installer's download |
| full-output-enforcement | no placeholders, no "rest follows the same pattern" |
| better-accessibility, fixing-accessibility | real controls, a visible focus ring, labels tied to inputs, errors as words beside the field, status announced, 40-pixel targets, Next never disabled |
| better-layout, better-typography, better-colors, better-ui, baseline-ui | `Theme.xaml`: two tiers of colour, four type sizes, spacing in steps, the company design system's fonts (Poppins, Orbitron, built in); motion only in `Motion.cs`, under a quarter of a second, by transform or opacity, and off when Windows' animation effects are off |
| design-taste-frontend (its app-screens chapter), redesign-existing-projects | "How the window is meant to look" in `windows/README.md`: gold as a fill only where `ThemeTests` allows (one forward button, the current step, switch thumbs, washes), the card as the biggest thing, panels raised like glass, one radius family. Judged on the screenshots CI keeps in `render-report` |
| better-writing | verb-first buttons that name the consequence, sentence case, errors that say how to recover |
| anti-slop | TypeScript-only as written; its principles are the `// SAFETY:` rule and parsing at the boundary (`Register.ParseEntry`) |
| i-have-adhd | how replies to Deign are shaped, not the app |
| grill-me, interface-review, break, variant | run by name, with a person answering |
| create-verification-skill | needs a real launch on Windows to count; not written yet |

The web-marketing and image-generation skills (brandkit, gpt-taste,
imagegen-*, image-to-code, stitch-design-taste, high-end-visual-design,
industrial-brutalist-ui, design-taste-frontend's page run, fixing-metadata,
fixing-motion-performance, e2e, typesafe-ai) do not apply to a native
desktop tool.
