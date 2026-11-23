# Gates: rebuild the ID Maker as a native Windows app

OWNS: windows/**, .github/workflows/windows.yml, docs/STATE-MACHINES.md, AGENTS.md, GATES.md, README.md

Scope: The web ID Maker rebuilt as a native C# / WPF Windows program that prints the same card, signs full-screen with pen pressure from the tablet driver, removes photo backgrounds on the computer, and installs with the XP-Pen driver.

- [x] G1: Every rule about the card passes its tests, held to values captured from the web app (QR module for module, sheet plans, names, the example photo's placement), and the real background-removal model finds the person in an ordinary photo.
  CHECK: dotnet test windows/IdMaker.Tests
  EXPECT: /Passed!\s+- Failed:\s+0, Passed:\s+1\d\d/
  EVIDENCE: 104 passed, 0 failed, on Linux and on windows-latest (run 43999369620, step "Core tests").

- [x] G2: The whole solution, including the WPF program and its Windows-only tests, compiles with warnings treated as errors.
  CHECK: dotnet build windows/IdMaker.sln
  EXPECT: Build succeeded
  EVIDENCE: Build succeeded locally (compiled for Windows from Linux) and on windows-latest.

- [x] G3: On Windows, the natively drawn example card matches the card the web app printed, zone by zone, with the same fonts, line breaks and sizes.
  CHECK: dotnet test windows/IdMaker.App.Tests --filter "FullyQualifiedName~RenderTests"
  EXPECT: /Passed!\s+- Failed:\s+0/
  CWD: .
  EVIDENCE: Passed on windows-latest (run 43999369620, job "test"). Card fonts within 0.016 px of the web app's widths at 100 px; QR identical; photo 0.50 mean difference; whole card 0.57; every line's ink box within one pixel of the web app's.

- [x] G4: On Windows, the real window takes a card from an empty form to a saved PDF, refuses to move on with the reason, issues nothing when the save fails, and every tool opens.
  CHECK: dotnet test windows/IdMaker.App.Tests --filter "FullyQualifiedName~WindowTests"
  EXPECT: /Passed!\s+- Failed:\s+0/
  EVIDENCE: Passed on windows-latest (run 43999369620, job "test").

- [ ] G5: The setup program installs the app like a native Windows app (Start-menu shortcut, an Installed apps entry with version, publisher and uninstaller), with no administrator; upgrades in place; the installed app starts; and it uninstalls cleanly, leaving the user's data.
  CHECK: pwsh -File windows/installer/package.ps1; pwsh -File windows/installer/selftest.ps1
  EXPECT: Installed, upgraded in place, started, and uninstalled cleanly.
  EVIDENCE: pending

- [ ] G6: Signing with the XP-Pen Deco Fun draws a line that thickens with pressure, with "Windows Ink" both ticked and unticked in the XP-Pen settings, and the pen cannot leave the signing screen.
  EVIDENCE: pending

- [ ] G7: The installer, run on an office computer with no tablet driver, installs XP-Pen's driver after one Windows permission prompt and the tablet works afterwards.
  EVIDENCE: pending

- [ ] G8: A card printed at 100% from the native app measures the same as one printed from the web app.
  EVIDENCE: pending

- [ ] G9: Real staff photos, taken against an ordinary wall, come out of the background remover clean enough to print.
  EVIDENCE: pending

ABANDON: G6 needs the tablet in someone's hand; the machine this was built on is not Windows and has no tablet. The pressure path was written from the WinTab specification and the WPF stylus API, and compiles; it has not been drawn with.
ABANDON: G7 needs an office computer and an administrator to answer the prompt. The driver installer was downloaded and confirmed to be Inno Setup, whose silent switches are the ones used; the install itself was not run. CI always installs with the driver step left out.
ABANDON: G8 needs a printer and a ruler. The PDF's page size and pixel dimensions are tested (638 × 1011 at 54 × 85.6 mm).
ABANDON: G9 needs real photographs of real staff, which must not leave the company. The model was measured on the repository's sample portrait over test backgrounds only.

# Gates: move the shared register from the Google Sheet to Firebase

OWNS: windows/IdMaker.Core/Register.cs, windows/IdMaker.Core/Config.cs, windows/IdMaker.App/WindowsKeeper.cs, register/**, .github/workflows/release.yml; in Deign86/apg-keystone: firestore.rules (the idreg block), e2e/cloud/id-register.e2e.ts, docs/adr/0025

Scope: The register of issued IDs is a list in the company's Firebase project (apg-keystone), reached over Firestore's and Firebase Auth's REST interfaces, with each HR person signed in as their Keystone account. No Apps Script, no REGISTER_URL secret. Which project it is comes from the FIREBASE_PROJECT_ID and FIREBASE_API_KEY repository secrets at release; neither is in the source.

- [x] R1: Core compiles with warnings as errors and with no package added.
  CHECK: dotnet build windows/IdMaker.Core
  EXPECT: Build succeeded
  EVIDENCE: Build succeeded, 0 warnings, on Linux (.NET SDK 8.0.134), 17 Nov 2026.

- [x] R2: Every Core test passes: sign-in and its refusals, the read across pages, create-if-absent, a duplicate refused whichever way Firestore refuses it, offline then resent from the outbox, a sign-in turned away, nothing sent with nobody signed in, nothing that opens the register written in the repository.
  CHECK: dotnet test windows/IdMaker.Tests
  EXPECT: /Passed!\s+- Failed:\s+0, Passed:\s+1\d\d/
  EVIDENCE: 125 passed, 0 failed, under xunit on windows-latest (run 44082993924, job "test", pull request 9).

- [x] R3: The WPF program and its Windows-only tests compile and pass with the sign-in panel in place of the address box.
  CHECK: dotnet build windows/IdMaker.sln; dotnet test windows/IdMaker.App.Tests
  EXPECT: Build succeeded; /Passed!\s+- Failed:\s+0/
  EVIDENCE: Build succeeded; IdMaker.App.Tests 13 passed, 0 failed, on windows-latest (run 44082993924). The same run built the setup program and the single .exe, and the installed app started ("Installed, upgraded in place, started, and uninstalled cleanly.").

- [x] R4: The register's rules allow a member to read and to create an ID under its own number as themselves, and refuse a second create, any change, any removal, and anyone who is not a member.
  CHECK: (in apg-keystone) npm run test:e2e:cloud
  EXPECT: id-register.e2e.ts: 5 passed
  EVIDENCE: id-register.e2e.ts, 5 tests passed against the Firestore emulator with firestore.rules (apg-keystone run 44083720325, pull request 57).

- [ ] R5: The rules are deployed to apg-keystone, and the Sheet's rows are in the new register.
  CHECK: node register/migrate.mjs Register.csv
  EXPECT: Every number in the sheet is in the new register.
  EVIDENCE: pending. The Sheet's Register tab held 2 cards on 17 Nov 2026 (APG-2026-124, APG-2026-125). The script was run against a stand-in for the two services only: 2 numbers and 1 replacement copied, nothing copied on a second run.

- [ ] R6: On a Windows computer, signed in with a Keystone account: one ID issued appears in Firestore under its number, and a second attempt at the same number from another computer is refused and the operator is told.
  EVIDENCE: pending

ABANDON: none yet. R5 and R6 wait on the rules being deployed, the two repository secrets being set, and a Windows computer signed in with a Keystone account.

# Gates: a native Mac app (owner request, 2026-11-18)

OWNS: mac/**, .github/workflows/mac.yml

Scope: the ID Maker as a native Mac program (C# on AppKit, no web view) that shares `IdMaker.Core` with the Windows app, prints the same card, uses the same register, and installs the pen tablet driver. Not signed with an Apple Developer ID (the owner's choice): the first open is allowed by hand.

- [x] M1: The Mac app builds on a real Mac against the same Core, and Core's own tests pass there.
  CHECK: the "Mac app" workflow, steps "Core tests, on a Mac" and "Build"
  EVIDENCE: 132 passed on macos-26 (job 134508656319); `Alpha Premier ID Maker.app` built. The workload is pinned in `mac/workloads.json` to the one that builds with the runner's Xcode 26.6.

- [x] M2: The register sign-in is kept wrapped by the Mac: the key is in the user's Keychain, a damaged or foreign value does not open, and with the key gone nothing opens.
  CHECK: `AlphaPremierIdMaker --selftest`
  EVIDENCE: 6 checks passed on macos-26 (job 134508656319).

- [x] M3: The card drawn by the Mac app matches `test/golden-front.png`, zone by zone, as the Windows app's does, and the card fonts measure as they did in the web app.
  CHECK: `AlphaPremierIdMaker --selftest --repo <repository>`
  EVIDENCE: passed on macos-26 (Mac app workflow, commit after 037fd52). Fonts: cap heights and descents equal, worst width difference 0.0017 px at 100 px. Card 638 x 1011. Whole card mean difference 1.14 (limit 4.0); photo 1.28; name 5.99; job title 1.57; signature 0.50; QR 0.00 (identical). Ink boxes of the name, title and number within one pixel of the web app's. The employee number is the closest to its limit: 11.45% of pixels visibly different against 12% (11.71 mean against 14), as on Windows a matter of how a one-pixel stroke is smoothed. The drawn card is kept as the `mac-report` artifact. Not yet drawn on Mac: sheets of cards and the PDF pages (with M4).

- [x] M4: The real window takes a card from an empty form to a saved PDF, refuses to move on with the reason, issues nothing when the save fails, signs in to the register and shows a refused card. The Intern / Employee choice is there.
  CHECK: `AlphaPremierIdMaker --windowtest --repo <repository>` (a build made with `-p:WindowTest=true`)
  EVIDENCE: passed on macos-26 (Mac app workflow run 44231358076, job test, commit 1da9662): 29 checks in "A card can be made from start to a saved PDF" (848,576-byte PDF; APG-2026-007 issued to an intern only after the save; the same number then refused for somebody else by name), 6 in the sign-in test against the pretend project (wrong password refused, password in no file), 7 in the refused-card test. Screenshots of the window are in the job log and the `mac-report` artifact.

- [ ] M5: Signing with the pen draws a line that thickens with pressure, full screen.
  CHECK: on a Mac with the XP-Pen tablet and its driver, press Sign now and sign lightly, then hard
  EVIDENCE: pending (needs a person with the tablet). The window tests sign through the same code with a made-up signature; no test can press a pen.

- [x] M6: Photo background removal works on the Mac (ONNX Runtime's macOS build), on the computer.
  CHECK: the window test "The background remover runs on this Mac"; `mac/package.sh osx-x64 Intel` for the Intel build
  EVIDENCE: on macos-26 (run 44231358076) the model loaded from inside the app and answered with one value per pixel for a 480 x 640 picture. It is the same model file as on Windows, checked against the same SHA-256 when built. Not shown: a real photograph cut out on a Mac (photographs are not kept in the repository).

- [x] M7: The app offers to install XP-Pen's Mac driver: downloaded from XP-Pen's own server, checked against a pinned SHA-256, opened in Apple's installer. macOS asks for the administrator password and for Input Monitoring and Accessibility; those cannot be skipped by any app.
  CHECK: the window test "The pen tablet driver is offered once, and the one XP-Pen serves is the one pinned"
  EVIDENCE: on macos-26 (run 44231358076): offered once on a Mac with no tablet driver, not a second time, the button stays on the signature step; XP-Pen's server served XPPenMac_4.0.21_261026.zip with the pinned SHA-256 and it unpacked to XPPenMac_4.0.21_261026.pkg. Not shown: Apple's installer actually run to the end (it needs a person and an administrator password).

- [x] M8: A release attaches a `.dmg` for Apple Silicon and Intel beside the Windows setup program, built with the register's project from the same secrets, and the app in it starts.
  CHECK: `mac/package.sh osx-arm64 AppleSilicon` and `mac/package.sh osx-x64 Intel` (the Mac app workflow's job `package`; the Release workflow's job `mac` adds `--needs-register`)
  EVIDENCE: on macos-26 (run 44229876311, job package) both images were built, opened, and the app inside each passed its self-test and started ("The window opened and the card loaded."), the Intel one under Rosetta. Released: Release workflow run 44232710609, job mac, built both with `--needs-register` ("this build was given the register's project" passed for each), started each, and attached `AlphaPremierIdMaker-Mac-AppleSilicon-v2.0.4.dmg` and `AlphaPremierIdMaker-Mac-Intel-v2.0.4.dmg` to v2.0.4. Not shown: the app opened by a person on a Mac from a downloaded image, where macOS asks for Open Anyway.

- [x] M9: The Mac window does what the Windows window does: batch from a list, the card design (map, artwork, reset), dragging the photo, dropping a picture, a sideways phone photo stood upright.
  CHECK: `AlphaPremierIdMaker --windowtest --repo <repository>`
  EVIDENCE: on macos-26 (run 44231358076): batch 7 checks (2 cards saved, the row with no photo skipped with its reason, only those two issued); card design 11 checks (box moved 34.40 to 35.40 mm by the keys, discarded, kept and still there on reopening, wrong-shaped artwork refused, card-shaped taken, reset); photo dragged 40 points = 5.40 mm; a JPEG marked "rotate 90" opened 20 x 40. Not driven by a test: the mouse itself on the boxes and the photo (the tests call what the mouse handlers call), and dropping a file.
