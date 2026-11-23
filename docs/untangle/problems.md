# ID Maker: where the code gets "should these pieces change together?" wrong

Read-only structural audit of `/home/claude/apg-id-creator` at `16939ab` (main, 18 Nov 2026).
Nothing was built or run. Paths are relative to the repository root. Short names used below:

- **Win** = `windows/IdMaker.App/MainWindow.xaml.cs`, **WinPrint** = `windows/IdMaker.App/MainWindow.Print.cs`, **XAML** = `windows/IdMaker.App/MainWindow.xaml`
- **Mac** = `mac/IdMaker.Mac/MainWindow.cs`, **MacTools** = `mac/IdMaker.Mac/MainWindow.Tools.cs`
- **Core/** = `windows/IdMaker.Core/`

Project reference graph (from the `.csproj` files):

```
IdMaker.Core        (net8.0, no references, no packages)
IdMaker.Vision      -> Core, package Microsoft.ML.OnnxRuntime 1.20.1
IdMaker.App  (WPF)  -> Core, Vision
IdMaker.Tests       -> Core, Vision
IdMaker.App.Tests   -> App; compiles ..\IdMaker.Tests\FakeFirebase.cs by link
IdMaker.Mac (net10) -> Core, Vision (by ..\..\windows\ path); bundles assets/*.png,
                       windows/IdMaker.App/Fonts/*.ttf, windows/IdMaker.Vision/Models/modnet.onnx;
                       with -p:WindowTest=true also compiles windows/IdMaker.Tests/FakeFirebase.cs
windows/IdMaker.sln holds the five Windows projects; the Mac project is in no solution.
```

Core has no platform reference and no package: that rule holds. What does not hold is
"Core decides, App shows": the two windows are 3,881 lines (Win 935 + WinPrint 878, Mac 1,543 + MacTools 525)
and most of what they decide is decided twice, once per window. Those places are marked **[Core decides]**.

What `git log --name-only` adds (93 non-merge commits): `IdMaker.App.csproj` and `app.manifest` changed
together in 5 of 5 commits and with `Core/Config.cs` in 4 (the version); `release.yml` and `windows.yml`
together in 4; the one-field change `kind` (f58ef94) touched 12 files before the Mac app existed. The Mac
window arrived in one pull request (#12), so there is no history yet of the two windows drifting; the
drift listed below is what is already there on day one.

---

## 1. Hidden links (must change together, and the code does not show it)

### 1a. The same decision written in both windows [Core decides]

- **H1** Win:277-281 = Mac:641-645. The help line under the number ("Replacement for…", "Checked against this computer only…", "Could not reach the shared register…") is the same five-way rule in both windows; it is a rule about the form and is in neither Core nor a test.
- **H2** Win:301-308 = Mac:663-670. The "Still needed: …" line, with its `queueOnly` exception, is computed twice.
- **H3** Win:351-378 = Mac:689-708. Which step chip is locked, current or finished (`done = StepDone && i != LastStep && !current && !locked`), its screen-reader name and its tooltip are worked out twice.
- **H4** Win:219,245,267,601 = Mac:583,609,631,834. "An error shows only once its field was touched" (and the three exceptions: name fit needs last and first touched, title fit needs title touched, a photo error shows at once) is a rule each window re-implements from `Session.Touched`.
- **H5** Win:603-615,632-633 = Mac:836-849,870. The photo step's message ladder (checking, error, warnings, "background was removed", three empty-state texts by remover state), the button's three titles and the guide label are the same code twice.
- **H6** Win:232-234,893-895,918-920 = Mac:594-596,1263-1265,1282-1284. The stage caption, the card's accessibility sentence and the sheet caption ("Backs, reversed to match." / "Fronts." / "Page n of m · paper") are composed twice.
- **H7** Win:314-320 = Mac:676-680. When Back and Next show, when Next reads "Check the card", and that Next is enabled only once the card loaded.
- **H8** Win:383-422 = Mac:748-785. `InputFor`, `LandingFor` and `AfterMove` (which control takes focus after a move, when the side flips back to the front, when `_saved` is dropped, when signing opens by itself) are the same state rules twice.
- **H9** WinPrint:25-71 = Mac:1026-1072. The check step: three headings, the "already in this print" line, sheets = ceil(total / per), the copies label, and which buttons are enabled while saving.
- **H10** WinPrint:62 = Mac:1061 = Core/Session.cs:721,730. The copies ceiling `PerSheet * 20` is written four times (and `200` once at Session.cs:730 for "no plan"); change one and the + button and the clamp disagree.
- **H11** WinPrint:117-167 = Mac:1087-1122. The "In this print" strip: when it shows, its rows, its two notes and when "Stop adding" shows.
- **H12** WinPrint:392-415,455-474 = Mac:1355-1396. The Issued IDs drawer: both notes, the row format, the signed-in / not-signed-in / waiting sentence and the refused-card sentence are identical strings built in both windows. They have already drifted: Windows lists 25 cards (WinPrint:399,403,415), the Mac 12 (Mac:1364,1368,1374).
- **H13** WinPrint:489-540 = Mac:1432-1486. Sign in, sign out and "Check the register": the same call order (`SignIn` → `LoadIndex` → `SendWaiting` → `RefreshDuplicate`) and the same six result sentences in both.
- **H14** Win:161-184 = Mac:533-557. Start-up: `LoadIndex` → `SendWaiting` → open the register drawer if anything was refused → `RefreshDuplicate`; and loading the remover.
- **H15** Win:669-704,846-877 = Mac:881-911,988-1018. Taking a photo file and a signature scan: read the size, `FileProblem`, remember `CardSerial`, decode off the window, drop the answer if the card changed. Four copies of one protocol.
- **H16** WinPrint:184-222 = Mac:1137-1169. Saving the print: write the PDF, and only then `MarkIssued`, then `RecordShared`. REVIEW.md's rule "nothing is issued until the PDF is known to be saved" is held only by the order of lines in two window methods.
- **H17** WinPrint:230-254 = Mac:1177-1206. `WritePdf` (write to `.part`, move over, two error sentences) exists twice and the copies already differ: the Mac also catches `InvalidOperationException` (Mac:1198), Windows does not (WinPrint:247).
- **H18** WinPrint:256-278 = Mac:1208-1229. "Save as pictures": the `-front` / `-back` file-name rule and both messages, twice; the catch lists differ (Windows also `NotSupportedException`).
- **H19** WinPrint:560-566,599-607 = MacTools:85-97. The batch tool's three chooser messages and the layout line ("n cards per sheet (c × r, landscape)").
- **H20** WinPrint:619-686 = MacTools:145-211. `RunBatch` is the same 60 lines twice: read CSV, load the index, `Batch.Run`, build pages, write, record, report. Inside it two Core rules are copied by hand: the back-upside-down formula (WinPrint:656, MacTools:182; the original is Core/Session.cs:751-752) and the issue loop `Record` + `NoteShared` (WinPrint:665, MacTools:192; the original is Core/Session.cs:805). Drift: a null sheet plan is `!` on Windows (WinPrint:655) and a message on the Mac (MacTools:180-181).
- **H21** WinPrint:688-703 = MacTools:104-114. How a batch row is reported (Skipped / warning / "No signature on this card." / "Ready.").
- **H22** WinPrint:750-783 = MacTools:294-335. `UseArtwork`: shape check, resize to 638 × 1011, save both pictures, derive the design's name from the file name, `AdoptArtwork`, `Save`, open the map.
- **H23** WinPrint:790-806 = MacTools:342-352. The contrast-warning sentence ("The a, b and c would be hard to read…", "those boxes" / "that box") and its detail lines.
- **H24** WinPrint:810-877 = MacTools:354-414. Start / stop adjusting, Keep, Discard, Reset: the same sequences on `TemplateStore` (`Snapshot`, `IsPristine ? Clear : Save`, `RestoreSnapshot`, delete the stored pictures) and the same five result messages.
- **H25** WinPrint:329-371 = Mac:1322-1353, WinPrint:720-731 = MacTools:276-292. Which tool may be open (`DesignAllowed`, "stop adjusting when the drawer changes"), the drawer title rule and the design tool's state.
- **H26** Win:727,742-743,760-761,710-711 = MacTools:421,440-441,431-432, Mac:370-371. When the photo may be dragged, points-to-millimetres for the drag, the wheel's zoom step and the nudge step.
- **H27** WinPrint:42-44 = Mac:1043-1045. The summary's name, upper-cased title and number; Windows uses `data.Seq!.Value`, the Mac checks for null.
- **H28** Win:491-546 vs Mac:712-744 vs Core/Session.cs:468. "The second role cannot be the first" is decided in three places: Windows hides the chip, the Mac rebuilds the second menu without it, and Core clears the pick.
- **H29** Win:786-811 = Mac:923-935 + MacTools:492-501. The signature step's button title, help sentence and tablet note; the tablet note's wording has already drifted between the two.
- **H30** XAML ↔ Mac/MacTools: 77 fixed strings in the markup are repeated word for word in the Mac window's code (for example XAML:146 = Mac:241, XAML:175 = MacTools:267, XAML:342 = Mac:349, XAML:396 = Mac:390, XAML:122-124 = MacTools:39). AGENTS.md asks that a message added to one window be added to the other; nothing shows which strings are a pair. One pair has drifted: "Batch from CSV" (XAML:55, WinPrint:306) against "Batch from a list" (Mac:204,1319).

### 1b. The card drawn twice

- **H31** `windows/IdMaker.App/CardRenderer.cs`:254-307 = `mac/IdMaker.Mac/MacCard.cs`:204-259. The front's drawing order (artwork, photo in its clip, fade when the photo stops short, artwork again inside the front outline, three texts, signature, QR) and the fade's numbers (`min(FadeMm, H/3)`) are written twice. "The printed card must not move by accident" depends on two files being edited alike.
- **H32** CardRenderer.cs:309-334 = MacCard.cs:261-284. Line placement (centre / right / left from `AnchorX`, word by word).
- **H33** CardRenderer.cs:404-446 = MacCard.cs:348-385. The QR code's snapping to print pixels, the four-module quiet zone and the run-merging loop.
- **H34** CardRenderer.cs:339-363,382-400 = MacCard.cs:289-311,328-344. Drawing ink segments and fitting a drawn or scanned signature into its box.
- **H35** CardRenderer.cs:455-481 = MacCard.cs:394-425. A sheet: page size rounding, the turned card's transform, the crop-mark line width `max(1, 0.1 × ppm)`.
- **H36** CardRenderer.cs:487-512 = MacCard.cs:443-468. `SheetPdfPages` and `CardPdfPages` are identical except for the picture type.
- **H37** CardRenderer.cs:182-198 vs MacCard.cs:428-440. Flattening a page onto white for the PDF is written with two different formulas for the same result.
- **H38** CardRenderer.cs:93-136 = `mac/IdMaker.Mac/MacText.cs`:64-128. Measuring a line word by word, the ink rounding (`Ceiling(… - 1e-6)`) and the sample strings `"H"`, `"Q,"`, `"gjpqy"` that define cap height and descent.
- **H39** CardRenderer.cs:65-67 = MacText.cs:34-36 = `mac/IdMaker.Mac/IdMaker.Mac.csproj`:41-43 (and `windows/installer/package.ps1`:42 for one of them). Which font file is the Name, Title and Number face.
- **H40** CardRenderer.cs:27 and MacCard.cs:18 both declare `record CardArt(Front, Back)`.
- **H41** CardRenderer.cs:519,537,552-555 vs MacCard.cs:100,108. What picture is too big (120 million pixels on Windows, 12,000 pixels a side on the Mac) and what the operator is told; the WebP sentence exists on Windows only.
- **H42** `windows/IdMaker.App/MapEditor.cs`:30,33-37,135-154,174-208 = `mac/IdMaker.Mac/CardViews.cs`:110,113-117,170-231,270-295. The map editor's box labels, quarter-millimetre nudge, readout sentence, drag / resize / Shift-arrow rules.
- **H43** `windows/IdMaker.App/SignaturePad.cs`:28-31,138-173 = `mac/IdMaker.Mac/SignWindow.cs`:14-17,55-92. The pad's units (1000), ink widths (2.5 and 7.8, again as bare numbers at Mac:956), jitter (1.5) and the "keep the highest pressure of a jittering point" rule. Drift: the guide line is at 0.74 of the height on Windows (SignaturePad.cs:119) and 0.72 on the Mac (SignWindow.cs:101), and only Windows rescales an old signature to the pad (`FitToPad`, SignaturePad.cs:93-100; the Mac setter SignWindow.cs:33-38 does not).

### 1c. The same value in two or more files

- **H44** The version, five times: Core/Config.cs:55, `windows/IdMaker.App/IdMaker.App.csproj`:13, `windows/IdMaker.App/app.manifest`:3, IdMaker.Mac.csproj:15 and :16. `release.yml`:74-77 checks the first two and :183 the Mac's; nothing checks the manifest or `ApplicationDisplayVersion`.
- **H45** Field lengths: Core/Config.cs:104-106 (40, 10, 60; unused, see S2) against the numbers typed into XAML:271,274,284,289,309,319. The Mac fields have no limit at all.
- **H46** "A number is 1 to 4 digits": Core/Session.cs:79 and :498, Core/Batch.cs:57, XAML:335 (`MaxLength="4"`), and the message at Session.cs:213.
- **H47** Artwork size 638 × 1011: Core/Geometry.cs:57-58, typed again in XAML:169 (the Mac reads the constants, MacTools:262).
- **H48** Zoom range and step: Core/Config.cs:177 (0.5 to 1.5, 0.05) against the slider's 50 / 150 / 5 in XAML:371 and Mac:151,361.
- **H49** XAML:235 (`Width="540" Height="856"`) is `CardWidthMm × MapEditor.UnitsPerMm` (MapEditor.cs:29), and Win:742 divides by that width. On the Mac the card's width is 400 in three places that must agree: Mac:72 (`CardPointsWide`), Mac:96 (`Ui.Picture(400, 634)`) and Mac:1249 (`800 / CardWidthMm`).
- **H50** The ink colour #1D1C1A: SignaturePad.cs:38, Win:796, Mac:951 (as 0.11, 0.11, 0.10), Mac:961, SignWindow.cs:111.
- **H51** The brand gold #D4AF37: `Colors.Light.xaml`:33, `Colors.Dark.xaml`:32, MapEditor.cs:55 (as bytes, outside what `ThemeTests` reads), `mac/IdMaker.Mac/Ui.cs`:13 and CardViews.cs:182-183 (as 0.83, 0.69, 0.22).
- **H52** The app's name: Core/Config.cs:54, XAML:5, IdMaker.App.csproj:14, IdMaker.Mac.csproj:11, `mac/IdMaker.Mac/Info.plist`:6, `windows/installer/IdMaker.iss`:31, `windows/installer/selftest.ps1`:26, `windows/installer/portable.ps1`:63, `mac/package.sh`:36. The publisher: IdMaker.App.csproj:15, IdMaker.iss:41, selftest.ps1:52.
- **H53** The program's file name `AlphaPremierIdMaker`: IdMaker.App.csproj:11, IdMaker.Mac.csproj:10, `windows/IdMaker.App/ThemeManager.cs`:56 (inside a pack URI), IdMaker.iss:32, package.ps1:42,77, portable.ps1:35, selftest.ps1:28, package.sh:44, mac.yml:70,76.
- **H54** The data folder "Alpha Premier / ID Maker": Core/Store.cs:38, Mac:190-191, selftest.ps1:30, portable.ps1:47. `last-error.txt`: `windows/IdMaker.App/App.xaml.cs`:26, selftest.ps1:81, portable.ps1:49.
- **H55** Asset names (`front-template.png`, `back.png`, `logo.png`, `modnet.onnx`): Win:127-128, Mac:493-498,551, IdMaker.App.csproj:29-31, IdMaker.Mac.csproj:38-44, package.ps1:42, `mac/IdMaker.Mac/SelfTest.cs`:114-115, `windows/IdMaker.Vision/BackgroundRemover.cs`:30. The stored-artwork name `template-{side}.png`: Win:119 and MacTools:239.
- **H56** Setting keys and their values are spelled where they are read and again where they are written: Core/Session.cs:100-102 against :741,:744,:745. The values `"short"`/`"long"` are also XAML:142-143 and WinPrint:364,547; the batch layouts `"duplex"`/`"pairs"`/`"cards"` are XAML:122-124, WinPrint:594-659 and MacTools:72-185.
- **H57** The four tool names `"register"`, `"batch"`, `"printer"`, `"design"` are bare strings in 40 places in the two windows' code, and again as tags in the markup: XAML:46,55-57, Win:169, WinPrint:304-367,816, Mac:203-206,541,1317-1348, MacTools:360.
- **H58** `"intern"` / `"employee"`: Core/Register.cs:65-66 are the constants; XAML:303-304 and Mac:305,614 type the words again.
- **H59** Accepted picture types: the rule is Core/Photo.cs:196; the Windows file dialog repeats it as a filter (Win:24); the Mac panels have no filter (Mac:1499-1507).
- **H60** The AI photo prompt: Core/Config.cs:186-192 and `photo-prompt.txt`, kept in step by a comment (Config.cs:184-185). The three steps around it are also twice: XAML:385-388 and Mac:388.
- **H61** The fields of an issued ID are listed in eleven places: Core/Register.cs:32-49 (record), :226-232 (CSV in), :538-546 (document out), :571-577 (document in), :819-825 (CSV out); Core/Session.cs:590-600 and Core/Batch.cs:199-208 (two constructors of an entry); Batch.cs:52 (template header); `windows/IdMaker.Tests/FakeFirebase.cs`:102-118 (a partial copy); `register/migrate.mjs`:97-116; `register/README.md`:49-53; and `firestore.rules` in apg-keystone. REVIEW.md's first row exists because of this.
- **H62** The `IssuedAt` format `"yyyy-MM-ddTHH:mm:ss.fffZ"`: Core/Session.cs:597 and Core/Batch.cs:205. The replacement document's name rule: Core/Register.cs:753-757 and `register/migrate.mjs`:121.
- **H63** What makes a row valid is decided twice: Core/Session.cs:76-79,192-216 for the form and Core/Batch.cs:57-58,142-168 for the CSV. They already differ: the batch does not check the title's or the suffix's characters, defaults `kind` to employee where the form refuses an empty one (Batch.cs:149, Session.cs:199), and words every message differently.
- **H64** The print file's name and title: Core/Session.cs:767-781 and Core/Batch.cs:235-240.
- **H65** Two different limits share the name `MinHeadPx`: Core/Config.cs:171 (180, a warning) and Core/Photo.cs:192 (40, a refusal).
- **H66** The pen driver is pinned twice in two formats: `windows/installer/Driver.psd1` (4.0.21.261022, URL, SHA-256) and `mac/IdMaker.Mac/PenDriver.cs`:26-29 (4.0.21_261026), with the size as prose at MacTools:477. "Is a tablet driver installed" has three definitions: `windows/IdMaker.App/PenTablet.cs`:34-35 (wintab32.dll exists), IdMaker.iss:90-122 (names in the registry) and PenDriver.cs:34-44 (names under /Applications), so the Windows app and its own installer can disagree.
- **H67** Step order: Core/Session.cs:57-65 names six steps; the windows address them by number: Win:295,398-408, XAML:437,442,447 (`Tag="0"`, `"1"`, `"2"`), Mac:110 (`new NSStackView[6]`), Mac:293-439 (`_steps[0]` to `[5]`), Mac:405-407,764-772.
- **H68** Small formatters copied instead of called: the plural `{(n == 1 ? "" : "s")}` at 24 sites and `({x.Message.TrimEnd('.')})` at 15 across Win, WinPrint, Mac, MacTools and Core.
- **H69** The card's test limits are written twice: `windows/IdMaker.App.Tests/AppTests.cs`:216-226 (zones and limits), :186 (48), :254 (90), :266 (1.5), :293-294 (324 × 514), :106 (the example card) = SelfTest.cs:141-150, :96, :170, :180, :186, :126. The font check AppTests.cs:110-139 = SelfTest.cs:54-79. The polling helper AppTests.cs:466-474 = `mac/IdMaker.Mac/Tests/WindowTest.cs`:101-109. Three window scenarios carry the same name in both (AppTests.cs:478,557,603 and WindowTest.cs:228,254,139).

### 1d. Conventions and call orders a caller just has to know

- **H70** `Session.Geometry` (Core/Session.cs:112) is a copy of `TemplateStore.Geometry()` that only the window keeps current: every template change must be followed by `ApplyGeometry()` (14 call sites, for example WinPrint:773,831,856,873 and MacTools:323,376,410). Forget one and the fit checks use the old boxes.
- **H71** `Session.Measurer` (Core/Session.cs:111) must be set by the window after the fonts load (Win:146, Mac:516). Until then `TextLayout()` is null and `JobCards()` adds no current card (Session.cs:178-179,610), silently.
- **H72** After anything that changes the register (import, sign in, sign out, batch, start-up) the window must call `_session.RefreshDuplicate()` itself: 14 call sites in the two windows.
- **H73** `Session` raises `Changed` in one place only (Core/Session.cs:312, after the shared answer arrives). Every other mutation relies on the caller calling `Render()` next: 148 `Render();` calls across the four window files.
- **H74** The "card it was for is gone" check (`var card = _session.CardSerial` before the wait, compare after) is a protocol the caller must remember: Win:681,698,854,864 and Mac:892,906,996,1005.
- **H75** The Mac project and `mac.yml` both list what the Mac is built from: IdMaker.Mac.csproj:28-56 and `.github/workflows/mac.yml`:12-19. Link a new shared file in the project and the push filter must gain a line. And because the Mac project is in no solution, `dotnet build windows/IdMaker.sln` (the check AGENTS.md names) does not compile it: a Core signature change breaks the Mac only on CI.

### 1e. The workflows (`windows.yml`, `mac.yml`, and `release.yml`, which repeats both)

- **W1** "Is .NET 8 already here" + conditional `setup-dotnet` + the downloads cache with its key: windows.yml:36-59 = windows.yml:106-129 = release.yml:42-65. Three copies.
- **W2** "Make sure Inno Setup is there": windows.yml:130-136 = release.yml:90-97.
- **W3** `package.ps1`, `selftest.ps1`, `portable.ps1` in that order: windows.yml:137-145 = release.yml:111-130.
- **W4** `MAC_XCODE: '/Applications/Xcode_26.6.app'`: mac.yml:40, mac.yml:108, release.yml:167; it must move with `mac/workloads.json`:2 (the comment at mac.yml:38-39 says so) and with the runner label.
- **W5** `dotnet-version: 10.0.4xx`: mac.yml:51, mac.yml:116, release.yml:187, and `mac/global.json`:3 (10.0.400).
- **W6** "Workload and Xcode" step: mac.yml:52-57 = mac.yml:117-120 = release.yml:188-192.
- **W7** Rosetta, then `package.sh osx-arm64 AppleSilicon` and `package.sh osx-x64 Intel`: mac.yml:121-126 = release.yml:193-203 (the release adds `--needs-register`).
- **W8** Runner labels: `windows-latest` at windows.yml:29,100 and release.yml:33; `macos-26` at mac.yml:33,103 and release.yml:161.
- **W9** The `DOTNET_NOLOGO` / `DOTNET_CLI_TELEMETRY_OPTOUT` pair, six times: windows.yml:32-33,103-104; mac.yml:36-37,106-107; release.yml:38-39,165-166.
- **W10** The trigger block and its comment: windows.yml:5-18 = mac.yml:5-21. The push filters differ without saying why: windows.yml:15 watches `.github/workflows/**`, mac.yml:19 only its own file, so a push that changes `mac.yml` alone also runs the Windows workflow but not the other way round.
- **W11** `permissions` and the `concurrency` shape: windows.yml:19-24 = mac.yml:23-28.
- **W12** The line that finds the built app (`find IdMaker.Mac/bin/Release -maxdepth 4 -path '*osx-arm64*' -name '*.app' | head -1`): mac.yml:68, mac.yml:75, `mac/package.sh`:24.
- **W13** Names shared between a workflow and test code with nothing tying them: `IDMAKER_REPORT_DIR` (windows.yml:34, mac.yml:66,73 ↔ AppTests.cs:76, SelfTest.cs:131, WindowTest.cs:48); the report files `font-metrics.txt` and `front-against-web.txt` (windows.yml:83,85 ↔ AppTests.cs:139,269); the screenshots `3-check 7-refused 8-batch 9-map` (mac.yml:82 ↔ WindowTest.cs:189,279,343,366).
- **W14** The Core tests are started from three places with three spellings: windows.yml:63, mac.yml:59, release.yml:88.
- **W15** The job names "Windows tests", "Windows package", "Mac tests", "Mac package" (windows.yml:28,99; mac.yml:32,102) are, by the comment at windows.yml:6-7, the checks `main` requires. That list lives in the repository's settings, not in the repository: renaming a job here leaves every pull request waiting.
- **W16** Action versions (`checkout@v4`, `setup-dotnet@v4`, `cache@v4`, `upload-artifact@v4`) are typed at every use: 20 uses across the three workflows.

---

## 2. Needless links (pieces tied together that need not be)

- **N1** IdMaker.Mac.csproj:41-43, SelfTest.cs:58,113 and mac.yml:16 reach into `windows/IdMaker.App/Fonts/`. The card's fonts belong to the card (like `assets/`), not to the WPF project's folder; moving or renaming that folder breaks the Mac build, its self-test and its push filter.
- **N2** `windows/IdMaker.Tests/FakeFirebase.cs` is compiled into two other projects by path (`windows/IdMaker.App.Tests/IdMaker.App.Tests.csproj`:19, IdMaker.Mac.csproj:56) and named in mac.yml:15. A shared fixture lives inside one of its three users.
- **N3** `mac/IdMaker.Mac/SelfTest.cs` is compiled into the released Mac app (`mac/IdMaker.Mac/Main.cs`:6-10; `package.sh`:49 runs it) and reads `windows/IdMaker.Tests/Fixtures/web-reference.json`, the fonts folder and `test/golden-front.png` by repository path (SelfTest.cs:58-59,113-117). The shipped program depends on the repository's layout.
- **N4** Core/Store.cs:37-38: `FileStore.DefaultDirectory` picks Windows' `LocalApplicationData`. Only the Windows app uses it (Win:66, App.xaml.cs:26,29); the Mac declares its own (Mac:190-191). A platform choice sits in Core.
- **N5** Core/Photo.cs:205: a Core message tells the operator to "open it in Windows Photos". The Mac shows the same sentence.
- **N6** Core's messages name the windows' controls: Core/Register.cs:141,143 and Win:279 = Mac:643 ("Sign in under Issued IDs"), Core/Session.cs:250 ("tick “Replacement card” below"). Renaming the rail button (XAML:46-53, Mac:203) or the tick box (XAML:340, Mac:347) means editing Core.
- **N7** Both windows read `Session.Touched`, a public mutable `HashSet<Field>` (Core/Session.cs:140), 8 times to decide what to show (see H4). The set is Session's working state.
- **N8** `Session.PreviewPage` has a public setter (Core/Session.cs:161). The windows decrement and increment it blind and clamp it later while drawing (Win:906,933-934; Mac:254-255,1270), so the valid range is known only to the drawing code.
- **N9** `Session.Queue` is a public `List<JobCard>` (Core/Session.cs:148) and `RoleChoice.Pick` / `.Custom` have public setters (Session.cs:35-36), while the rules about them live in `Session`'s methods (`AddAnother`, `RemoveFromQueue`, `PickRole1`).
- **N10** The windows build Core's `MoveResult` themselves, `new MoveResult(true, null)` at 12 sites (for example Win:430,439, WinPrint:106,114,147,285, Mac:427,441,791), only to reuse their own `AfterMove`.
- **N11** Both windows hold the `Register` as well as the `Session` and call it directly (`SignIn`, `LoadIndex`, `SendWaiting`, `Record`, `NoteShared`, `Ping`: WinPrint:496-532,633,665; Mac:1440-1478; MacTools:158,192). There are two doors to the same state, which is why H13, H16 and H72 exist.
- **N12** `Confirm(question, yes)`: the second argument is the button's label. The Mac uses it (Mac:1490-1496); Windows ignores it and shows OK / Cancel (Win:52-53), yet WinPrint:111 and :863 pass "Discard" and "Go back".
- **N13** `CardRenderer.RenderFront(…, bool showGuides = false)` (CardRenderer.cs:237,291-296) exists for one caller, the Ctrl+Shift+G maintainer overlay (Win:445-448,891). The Mac has no such overlay.
- **N14** `Batch.FileFor(cards, sheets, now)` (Core/Batch.cs:235) returns the file name and the title together, so the callers that want only the name pass a made-up `1` for the count (WinPrint:614, MacTools:135).
- **N15** `TemplateStore.Warnings(front, minRatio = 3)` (Core/Geometry.cs:279): no caller passes `minRatio`.
- **N16** `TextFit.Fit(m, spec, text, pxPerMm)` (Core/TextFit.cs:92): its only caller is the three-argument overload, always with the print resolution (TextFit.cs:89-90).
- **N17** `SignatureInk.FitInto(spec, minX, minY, inkW, inkH)` (Core/Signature.cs:167-176) takes `minX` and `minY` only to copy them into `InkFit`, where nothing reads them (S8).
- **N18** `ThemeManager.Palette` (ThemeManager.cs:55-56) writes the assembly's name into a resource address; it must change with `<AssemblyName>` in IdMaker.App.csproj:11.
- **N19** The Mac window exposes about 38 of its private controls and fields as `internal` properties "for the window test" (Mac:559-563,1521-1542; MacTools:514-524). The test is written against the window's insides, so the window cannot be rearranged without it.
- **N20** `PhotoPipeline.AnalysisMaxHeight` and `WorkingMaxHeight` are public (Core/Photo.cs:193-194) and used only inside `Analyse` (Photo.cs:535,559).

---

## 3. Surplus (code that carries no weight)

Each "never referenced" below was checked with a whole-repository grep over `.cs`, `.xaml`, `.ps1`, `.sh`, `.yml`, `.iss` and `.csproj`.

- **S1** Core/Geometry.cs:62 `TemplateStore.BoxKeys`: never referenced.
- **S2** Core/Config.cs:104-106 `MaxNameLength`, `MaxSuffixLength`, `MaxTitleLength`: never referenced (the markup types its own numbers, H45).
- **S3** Core/Geometry.cs:33-39 `CardGeometry.Text(BoxKey)`: never called.
- **S4** Core/Register.cs:77 `PersonKind.Label`: never called.
- **S5** CardRenderer.cs:150 `CardRenderer.Measurer` and MacCard.cs:149 `MacCardRenderer.Measurer`: never read.
- **S6** Core/Photo.cs:172-174 `PhotoCheck.Sharpness`, `HoleFraction`, `TorsoRatio`: set at Photo.cs:469 and :518, read nowhere, tests included. Core/Photo.cs:132 `HeadInfo.NeckY`: never read. `PhotoCheck.Head` is read only by tests.
- **S7** Core/Sheet.cs:16 `SheetPlan.PaperName`: never read.
- **S8** Core/Signature.cs:42 `InkFit.MinX`, `InkFit.MinY`: never read.
- **S9** Core/Session.cs:48 `JobCard.Title`: never read.
- **S10** Core/Session.cs:51 `MoveResult.Moved`: read only by tests; both windows look at `FocusField` and `StepIndex` instead.
- **S11** Core/Register.cs:86 `PingResult.Sheet`: read by one test (`windows/IdMaker.Tests/SessionTests.cs`:703). It holds the Firebase project's name under a word left from the Google Sheet register.
- **S12** Mac:795 `PressNext()` and Mac:1524 `TitleCustomBox`: never referenced.
- **S13** Core/Session.cs:682-695 `LoadExample` is called only by tests (SessionTests.cs:474, AppTests.cs:719). So in the apps `Example` is never true: the property (Session.cs:131), its 13 `Example = false` resets and the guard in `MarkIssued` (Session.cs:801) serve a mode nothing can enter.
- **S14** Used by tests only: Core/Register.cs:655 `IndexLoaded`; Core/Config.cs:344-348 `Naming.PdfFileNameFromWholeName`; CardRenderer.cs:522,551 `ImageFiles.LastFault`; Core/Store.cs:16-24 `MemoryStore` (a test double that lives in Core).
- **S15** Return values only tests read: `Session.SetSeq` (Core/Session.cs:494), `LeaveNamePart` (:437), `AddAnother` (:619).
- **S16** Settings for things that never vary: `MiddleNameStyle` and `CardConfig.MiddleName` with its three-way switch (Core/Config.cs:50,102,244-249); `NameCardFormat` with the comma tidying for a surname-first format nobody uses (Config.cs:101,250-258); `AutoCapitaliseNames` (Config.cs:103,313); `RegisterProject.Company = "apg"` (Core/Register.cs:93).
- **S17** Pass-throughs: `Ui.SyncText` is `Ui.SetText` under another name (Ui.cs:134-143); `MacCardRenderer.F` forwards to `MacPictures.F` (MacCard.cs:151); `Session.Role1Value` is `Role1.Value` (Core/Session.cs:169); `Session.TitleOptions()` is `_titles.All()` (:484); `OnThemeChanged()` is `Render()` (Win:191).
- **S18** mac.yml:60-61 "Build, as it is released": the next step (mac.yml:62-63) builds the same project into the same folder with the window tests in, before anything runs the first build's output; the release configuration is built again by `package.sh`:23 in the Mac package job. The step proves only that the project compiles without `Tests/`, which the package job also proves.
- **S19** `photo-prompt.txt`: no project, script or workflow reads it; its only mention is the comment at Core/Config.cs:185 (H60).
- **S20** `register/migrate.mjs` describes itself as run once (lines 1-3) and is referenced only from `register/README.md`:178,184 and GATES.md:77; it carries its own copy of the register's fields and sign-in (H61, H62). Whether it has been run for the last time is not something the repository says.

---

## 4. Special cases

### Swallowed failures (an empty catch, or a catch that only returns)

- **X1** Core/Store.cs:54-55: a file that cannot be read is reported as "no value". For `register.json` that means the list of issued IDs looks empty and the local duplicate check passes.
- **X2** Core/Store.cs:72-73: a value that cannot be written is dropped without a word ("lasts for this run only"). `Register.Record`, the outbox and the refused list all go through it (Core/Register.cs:197,700,713), against REVIEW.md's "nothing drops one".
- **X3** Core/Store.cs:93: unreadable JSON becomes the fallback; a damaged `register.json` reads as an empty list and the next `Record` writes over it.
- **X4** Core/Register.cs:282: a kept sign-in that will not parse is ignored (commented).
- **X5** `windows/IdMaker.App/WindowsKeeper.cs`:23,29,30 and `mac/IdMaker.Mac/MacKeeper.cs`:46-47: return null.
- **X6** BackgroundRemover.cs:75: a model failure returns null, and the operator is then told the photo "still has its background".
- **X7** Win:675,851 and Mac:887,993: a file whose size cannot be read is given size 0, so it passes the size limit.
- **X8** ThemeManager.cs:49-52 and :87-90; PenTablet.cs:138-142,164,186,196; CardRenderer.cs:571-575; PenDriver.cs:50-53: each returns a default.
- **X9** Empty catches with a comment: App.xaml.cs:32; WinPrint:249,295,869; Mac:1201; MacTools:406.
- **X10** WindowTest.cs:67: `catch (Exception e)` with no type (the test runner's outer catch).

### Waits, timers and retries

- **X11** Win:86-87,884: the sheet preview is drawn 120 ms after the last change, on Windows only (the Mac draws at once, Mac:1268-1287). AppTests.cs:679 then waits a fixed `Task.Delay(400)` for it, which REVIEW.md lists as a finding ("a fixed wait is a finding"); the 400 has to stay above the 120.
- **X12** AppTests.cs:466-474 and WindowTest.cs:101-109: poll 200 times, 25 ms apart. WindowTest.cs:40-46: a four-minute watchdog that exits the process.
- **X13** selftest.ps1:70-75,78,90 and portable.ps1:57-64: polling loops and fixed sleeps, with different bounds for the same thing (40 tries and 3 s in one, 80 tries and 4 s in the other).
- **X14** `windows/IdMaker.Vision/IdMaker.Vision.csproj`:22 `Retries="3"` on the model download; Core/Register.cs:511-518 one retry after a 401.

### An `if` for one value, a parameter for one caller

- **X15** The photo box is kept apart from the other five: `CustomTemplate.PhotoRect` beside `Boxes` (Core/Geometry.cs:48-49), with `key == BoxKey.Photo` branches at Geometry.cs:121-125,207, MapEditor.cs:124 and CardViews.cs:188.
- **X16** `RoleChoice.Other = "\u0001other"` (Core/Session.cs:34): "typed by hand" travels as a magic title in the same string as real titles, and both windows test for it (Win:501,507,514,542; Mac:313,323,721,733).
- **X17** Core/Session.cs:375: `NextStep` tidies the name parts only when the step is the name step. Win:418 and Mac:781: signing opens by itself only on arriving at step 4.
- **X18** WinPrint:346 and Mac:1340: the drawer title is special only for `"register"` with at least one card.
- **X19** CardRenderer.cs:540 (`decoder is JpegBitmapDecoder`) and :552 (`.webp`); Core/Photo.cs:204 (HEIC); Core/Photo.cs:564-570 (one message replaced when the background was removed); Core/Register.cs:783,795 (409 and 403 treated as one).
- **X20** Parameters with one user: `GoStep(force)` (Core/Session.cs:336; only :640 and :694), `ClearCard(keepQueue)` (:652), `LoadIndex(force)` (Core/Register.cs:595; only `Ping`), `Token(renew)` (:379), `AutoCapitalise(suffix)` (Core/Config.cs:310), `OnnxBackgroundRemover.TryCreate(modelPath)` (BackgroundRemover.cs:37; only the Mac passes it), `WritePdf(progress)` (WinPrint:230; only the batch).
- **X21** Only one of the two apps: the unhandled-error handler and `last-error.txt` (App.xaml.cs:16-37; the Mac has none); the guides overlay (N13); `--starttest` / `ExitAfterStart` (`mac/IdMaker.Mac/AppDelegate.cs`:9-21); `--needs-register` (package.sh:17, SelfTest.cs:20); `arch -x86_64` for one runtime id (package.sh:47).

### The repository's own rules

- **X22** "Every cast carries `// SAFETY:`": there are 20 such comments and about 147 lines with an unannotated cast (mostly number conversions; by file: Photo.cs 16, MacCard.cs 18, CardRenderer.cs 14, BackgroundRemover.cs 11, Signature.cs 9, Ui.cs 8). The unannotated ones that can fail at run time: `(string)item.Tag` at WinPrint:52 and :364; null-forgiving `!` at WinPrint:44,649,655,664, Core/Session.cs:592,613,623, Mac:462 and SignWindow.cs:137.
- **X23** Skipped tests: none. No `Skip =`, `[Ignore]` or conditional-out test was found.

---

## Candidate root fixes

Ordered by how many listed problems each removes. None of the WPF or macOS code can be built or run on
Linux; "CI" below means `windows.yml` (AppTests) and `mac.yml` (`--selftest`, `--windowtest`). Only
`windows/IdMaker.Tests` runs locally.

1. **A screen model in Core.** One Core type (say `Screen.From(session, register, templates, flags)`) that returns what the window shows: every text, which parts are visible or enabled, each chip's state, where focus goes after a move; plus one Core home for message strings and the plural / error-detail helpers. Each window shrinks to "copy the model onto the controls".
   Removes H1-H12, H19, H21, H23, H25, H27-H29, H67, H68, most of H30 (the strings built in code), N7-N10, X18.
   Behaviour risk: low if strings move verbatim; the 25-versus-12 list and "Batch from CSV / a list" have to be decided once.
   Covered by: new Core tests on Linux (this is the point: these rules have no test today); the visible strings are asserted by AppTests (Windows CI) and WindowTest (Mac CI).

2. **The jobs in Core, not in the windows.** `Session` (or a small `Jobs` class) owns the sequences: save-then-issue-then-share, the batch run, sign in / warm up / check, taking a photo or scan file, changing the design. The window passes in what only it has (a page renderer, a decoder, a save path) and gets back a result to show. `Session` holds the `TemplateStore` and the `Register`, raises `Changed` itself, and the windows stop touching the register.
   Removes H13-H18, H20, H22, H24, H26, H70-H74, N11, N14, X7, and makes X1-X3 fixable in one place.
   Behaviour risk: medium. The order "PDF saved, then issued" is the rule with the worst failure (STATE-MACHINES issue 5).
   Covered by: SessionTests (`MarkIssued`, `RecordShared`, outbox, refused) on Linux, and new Core tests with a temporary folder; `A_print_that_cannot_be_saved_issues_nothing` (AppTests.cs:712) and the batch and design scenarios run only on CI.

3. **One drawing plan for the card.** Core turns `CardData` + `CardGeometry` + resolution into an ordered list of plain operations (picture in a rectangle, clip polygon, fade from y1 to y2, word at x and baseline in font and size, ink segments, QR rectangles, sheet slot with its turn, crop lines, flatten onto white). Each platform keeps only "draw this operation" and "measure this word". Core also owns which file is which font, and the fonts move beside `assets/`.
   Removes H31-H40, H50, N1, N13, S5, and X15 can be folded in.
   Behaviour risk: high. This is the code AGENTS.md says must not move by accident, and a one-pixel change fails the zone limits.
   Covered by: the golden card (AppTests.cs:194, Windows CI; SelfTest `CardAgainstTheWebApp`, Mac CI). Neither runs locally; the plan's numbers can get Linux tests against `web-reference.json`.

4. **One home for every name and number, and a test that reads the files.** Version from one property (a shared `Directory.Build.props` both app projects import, with `CardConfig.AppVersion` read from the assembly and the manifest's copy dropped or generated). Limits, zoom range, tool and layout keys, setting keys, asset and data-folder names, pad constants and colours as Core constants the windows read. Where a file cannot read Core (XAML attributes, `.ps1`, `.iss`, `.plist`), a Core test scans it, the way `SecretTests` and `ThemeTests` already scan the tree.
   Removes H44-H49, H51-H60, H65, N4, N18, S2, S19.
   Behaviour risk: low (same values, one place). The version change touches the release workflow's check (release.yml:67-84,183).
   Covered by: the new scanning test and all Core tests on Linux; the installer scripts only by CI's package jobs.

5. **The register's fields and the card's rules, each once.** One field table in Core drives the document sent, the document read, CSV out and CSV in; one `RegisterEntry` factory is used by the form and the batch; one `CardRules` checks a name, title, number and kind for both. A test holds the table to `register/README.md` and makes `FakeFirebase` use it.
   Removes H46, H61-H64, and the drift inside H63.
   Behaviour risk: medium. What is sent to Firestore must stay exactly what `firestore.rules` in apg-keystone accepts (REVIEW.md, "The register"); batch rows that pass today may be refused once the form's rules apply to them.
   Covered by: SessionTests' register tests and `PropertyTests` (CSV round trip) on Linux.

6. **Workflows built from shared parts.** Two composite actions (`setup-windows`: SDK check and cache; `setup-mac`: Xcode, SDK band, workload) and the test / package jobs as reusable workflows that `release.yml` calls with the secrets. The Xcode path and SDK band live in one file beside `workloads.json`.
   Removes W1-W9, W11, W12, W14, W16, S18.
   Behaviour risk: the four required check names (W15) must come out unchanged, and a reusable workflow changes how they are displayed. Covered by nothing but running CI on a pull request.

7. **Shared test material in one place.** Move `FakeFirebase.cs`, the golden zones and limits, the example card and the polling helper into a folder all three test hosts link; compile `SelfTest.cs` only when asked (as `Tests/` already is) or have it take its fixtures from the bundle.
   Removes H69, N2, N3, part of N19, W13 if the report names move with it.
   Behaviour risk: none for the apps. `package.sh`:49 runs `--selftest` on the released build, so that check needs a new home. CI only.

8. **Delete what nothing uses.** S1, S3, S4, S6-S12, S15-S17, and S13-S14 after deciding whether the example card is coming back.
   Behaviour risk: none to low. The Linux build and Core tests catch a wrong deletion in Core and the Windows projects; the Mac compiles only on CI (H75), so S5 and S12 are confirmed there.

9. **Let the store say when it failed.** `IKeyValueStore.Get` and `Set` report an error instead of returning null or nothing, and `Register` tells the operator when the list of issued IDs could not be read or written.
   Removes X1-X3. Behaviour risk: medium (a new message where there was silence). Covered by Core tests on Linux with an unwritable folder.
