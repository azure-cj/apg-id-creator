# ID Maker untangle, round 1: decision log

Repository `/home/claude/apg-id-creator`, branch `refactor/untangle` (from `main` at `16939ab`), 27 commits, 24 files changed, nothing pushed.
Problem IDs and `file:line` are those of `idmaker-problems.md` (lines as on `main`). Short names: Win = `windows/IdMaker.App/MainWindow.xaml.cs`, WinPrint = `windows/IdMaker.App/MainWindow.Print.cs`, XAML = `windows/IdMaker.App/MainWindow.xaml`, Mac = `mac/IdMaker.Mac/MainWindow.cs`, MacTools = `mac/IdMaker.Mac/MainWindow.Tools.cs`, Core/ = `windows/IdMaker.Core/`.

Nothing was built or run: NuGet is blocked here and WPF / AppKit need Windows / macOS. Every edit was checked by reading and by a whole-repository grep for the name (`.cs`, `.xaml`, `.csproj`, `.ps1`, `.sh`, `.iss`, `.yml`, `.md`). CI on Windows and macOS is the first real check. Both projects build with `TreatWarningsAsErrors`, so a wrong deletion fails loudly there.

## Fixes

### A. Surplus removed

- `b9cb9f0` Remove `TemplateStore.BoxKeys`.
  - S1, Core/Geometry.cs:62: declared, no other mention anywhere.
- `dbdc1b1` Remove `CardGeometry.Text(BoxKey)`.
  - S3, Core/Geometry.cs:33-39: no `.Text(` call exists in the repository.
- `51b51b7` Remove `PersonKind.Label`.
  - S4, Core/Register.cs:77: the only `.Label(` calls are `Ui.Label` on the Mac.
- `5b67400` Remove the `Measurer` property from both card renderers.
  - S5, windows/IdMaker.App/CardRenderer.cs:150 and mac/IdMaker.Mac/MacCard.cs:149: never read; every `.Measurer` in the code is `Session.Measurer` or the batch services'. The private `_measurer` each draws with stays.
- `ceb1897` Remove two unused test hooks from the Mac window.
  - S12, Mac:795 `PressNext()` and Mac:1524 `TitleCustomBox`: `WindowTest.cs` names neither.
- `6042c66` Remove `PhotoCheck.Sharpness`, `HoleFraction`, `TorsoRatio`.
  - S6 (part), Core/Photo.cs:172-174, set at :469 and :518: read by nothing, tests included. The locals they copied still drive the checks, so what a photo is accepted or refused for is unchanged.
- `0ce7d1b` Remove `SheetPlan.PaperName`.
  - S7, Core/Sheet.cs:16, built at :64 only: never read; no code compares two plans, so losing one member of the record's equality changes nothing.
- `c07bf9e` The Mac window calls `Ui.SetText`; `Ui.SyncText` is gone.
  - S17 (part), mac/IdMaker.Mac/Ui.cs:134-143: two helpers with the same body; eight positional calls in Mac moved over.
- `be1fbc8` `Session.Role1Value` inlined to `Role1.Value`.
  - S17 (part), Core/Session.cs:169: a pass-through read three times, all inside `Session`.
- `8f18f1b` Delete `photo-prompt.txt`.
  - S19, repository root: read by no project, script or workflow.
  - H60, Core/Config.cs:184-192: the constant and the file were kept equal by a comment; the constant is now the only copy and the comment no longer asks for a twin. (The three steps written in XAML:385-388 and Mac:388 are markup / window text and stay.)
- `1718a10` Drop the Mac workflow step "Build, as it is released".
  - S18, .github/workflows/mac.yml:60-61: the next step rebuilt the same project into the same folder before anything used the first build; "compiles without Tests/" is still proved by the Mac package job (`mac/package.sh`:23).

### B. One home for a name or number (C# sites only, same type and units)

- `71fcbf1` The stored print settings are private constants in `Session`.
  - H56 (part), Core/Session.cs:100-102 against :741,:744,:745: each key (`rotate-back`, `duplex-flip`, `print-layout`) and its values were spelled where read and again where written. Same text, so settings already on a computer read as before.
- `8bef8de` One rule for "a card number is one to four digits".
  - H46 (part), Core/Session.cs:79 and Core/Batch.cs:57: the same pattern declared twice; `Batch` now calls `Session.IsSeqText`, beside `Session.NamePrints` which it already called.
- `640d8dc` The data folder's two names live in `FileStore`.
  - H54 (part), Core/Store.cs:38 and Mac:190-191: `FileStore.CompanyFolder` / `AppFolder`; each platform still picks its own application-data root.
- `095c89f` Built-in picture names and the stored design's file name live in Core.
  - H55 (part), Win:127-128, Mac:493-498, mac/IdMaker.Mac/SelfTest.cs:114-115: `CardConfig.FrontArtFile`, `BackArtFile`, `LogoFile`.
  - H55 (part), Win:119 and MacTools:239: the same line built `template-{side}.png` in both windows; now `TemplateStore.ArtworkFileName(side)`.
- `780e437` The matting model's folder and file name live in Vision.
  - H55 (part), windows/IdMaker.Vision/BackgroundRemover.cs:30 and Mac:551: `OnnxBackgroundRemover.ModelFolder` / `ModelFile`.
- `1d2ad8a` The Mac window reads `PersonKind.Intern` / `Employee`.
  - H58 (part), Mac:305 and Mac:614 against Core/Register.cs:65-66.
- `2beaca1` The four tool names are `ToolKey` constants in Core.
  - H57 (part), Win:169, WinPrint:304-367,816, Mac:203-206,541,1317-1348, MacTools:360: 46 bare strings in the two windows' code now read `ToolKey.Register`, `Batch`, `Printer`, `Design` (same text; the `switch` in `CloseTool` uses them as constant patterns).
- `94ddd36` The three batch layouts are `Batch.Layout…` constants in Core.
  - H56 (part), WinPrint:594-659 and MacTools:72-185: eight uses in each window of `duplex` / `pairs` / `cards`.
- `e5f7d97` The pen tablet context is named from `CardConfig.AppName`.
  - H52 (part), windows/IdMaker.App/PenTablet.cs:109 against Core/Config.cs:54.
- `1a66e77` The Windows window takes the on-screen ink colour from `SignaturePad.InkColor`.
  - H50 (part), windows/IdMaker.App/SignaturePad.cs:38 and Win:796: #1D1C1A written twice as a WPF colour. On screen only; the printed signature uses the card map's colour, untouched.
- `43d73dc` The Mac preview's width is `CardPointsWide` everywhere.
  - H49 (part), Mac:72, Mac:96, Mac:1249: 400 and 800 typed beside the constant. `2 * 400 / 54` is the same double as `800 / 54`. Screen preview only.

### C. Workflows

- `57c2ef5` `DOTNET_NOLOGO` / `DOTNET_CLI_TELEMETRY_OPTOUT` once per workflow.
  - W9, windows.yml:32-33,103-104; mac.yml:36-37,106-107; release.yml:38-39,165-166: six copies become three top-level `env` blocks.
- `3740a36` `MAC_XCODE` once in mac.yml.
  - W4 (part), mac.yml:40 and :108: one top-level value with the comment tying it to `mac/workloads.json`.
- `c119a62` The Mac SDK band once in mac.yml.
  - W5 (part), mac.yml:51 and :116: `MAC_DOTNET_SDK`, read by both `setup-dotnet` steps through `${{ env.… }}`.
- `86ed95f` `IDMAKER_REPORT_DIR` once for the Mac tests job.
  - W13 (part), mac.yml:66 and :73: set on the job, as windows.yml:34 already does.
- (S18, `1718a10`, is listed under A.)

Checked after each: the three files still parse; job ids and the names "Windows tests", "Windows package", "Mac tests", "Mac package" are unchanged; windows.yml and mac.yml still trigger on a bare `pull_request`; no reusable workflow or composite action was added.

### D. Comments

- `4e25673` Remove three summaries that only repeat the member's name.
  - Core/Session.cs `ChipPressed` ("A step chip was pressed."), Core/Register.cs `SignedIn` ("Is somebody signed in on this computer?"), mac/IdMaker.Mac/PenDriver.cs `Installed` ("Is a pen tablet driver installed on this Mac?").
  - Every other comment read in `windows/`, `mac/` and the workflows states a reason, a limit, or a fact from outside the code, and stays. All `// SAFETY:` and `// shortcut:` comments are untouched. The one-word section labels in the two `Render` methods (`// name`, `// number`, `// step 4`) were left: they are how a 100-line method is navigated, not a restatement of one line.

## Not done this round

Out of scope by instruction (need to be built and seen):

- Root fix 1 (screen model): H1-H12, H19, H21, H23, H25, H27-H30, H67, H68, N7-N10, X18.
- Root fix 2 (jobs in Core): H13-H18, H20, H22, H24, H26, H70-H74, N11, N14, X7.
- Root fix 3 (drawing plan): H31-H40, N1, N13, X15. No value that feeds the card's drawing was touched.
- Root fix 5 (register fields, card rules): H61-H64 and the rest of H46 / H63.
- The Core test that scans XAML / csproj / scripts (root fix 4's second half).
- Root fixes 6, 7, 9 (shared workflow parts, shared test material, the store reporting failure).

Surplus left, with the reason:

- S2 `MaxNameLength` / `MaxSuffixLength` / `MaxTitleLength` (Core/Config.cs:104-106): unreferenced, but they are the Core home for the limits typed in XAML:271-319 (H45) and equal them today (40, 10, 60). Deleting them would remove the home root fix 4's scanning test needs; kept until that test exists or the decision is to drop them.
- S6 `HeadInfo.NeckY` (Core/Photo.cs:132): a test builds `HeadInfo` by position (windows/IdMaker.Tests/Support.cs:152).
- S8 `InkFit.MinX` / `MinY` (Core/Signature.cs:42) with N17: removing them alone leaves `FitInto` taking two parameters that do nothing, and a test calls `FitInto` with all five (PhotoTests.cs:278).
- S9 `JobCard.Title`: a test constructs `JobCard` by position (SessionTests.cs:357).
- S10 `MoveResult.Moved`, S11 `PingResult.Sheet`, S13 `LoadExample` / `Example`, S14 (`IndexLoaded`, `PdfFileNameFromWholeName`, `ImageFiles.LastFault`, `MemoryStore`), S15 return values: all read by tests.
- S16 (`MiddleNameStyle`, `NameCardFormat`, `AutoCapitaliseNames`, `RegisterProject.Company`): removing them deletes branches of `Naming.ComposeName` and `AutoCapitalise`, which is a change to how a name is composed for the card, not a deletion of a dead symbol; `Company` is read by `FakeFirebase.cs`:161,168.
- S17 remainder: `MacCardRenderer.F` (16 call sites inside the card-drawing file, left alone on purpose); `Session.TitleOptions()` (the windows' only way to the private title list, not a surplus pass-through); `OnThemeChanged()` (carries the comment explaining why a redraw is all that is needed).
- S20 `register/migrate.mjs`: named in `register/README.md` and GATES.md; whether it has run for the last time is a person's call.

Duplicated values left, with the reason:

- H44 version: csproj, `app.manifest`, Mac csproj. Not C#; `release.yml` checks two of them.
- H45 field lengths, H47 artwork size, H48 (Windows slider), H49 (Windows 540 x 856), H56 / H57 / H58 (the `Tag` attributes): typed in MainWindow.xaml. The C# side now reads constants with the same text; the markup is for the scanning test.
- H46 remainder: `MaxLength="4"` in XAML:335, `Take(4)` in Core/Session.cs:498 and the two messages' wording.
- H48 on the Mac (Mac:151, :361): the slider works in per cent (50 / 150 / 5), the constants in fractions (0.5 / 1.5 / 0.05). Different units, so not a plain read.
- H50 on the Mac (Mac:951, Mac:961, SignWindow.cs:111): two are written as rounded fractions (0.11, 0.11, 0.10), which is not the same number as #1D1C1A; unifying them would change a screen colour.
- H51 gold: WPF resource, bytes in MapEditor.cs:55 and rounded fractions on the Mac; three types and not the same number.
- H52 remainder: XAML title, both csproj files, Info.plist, IdMaker.iss, selftest.ps1, portable.ps1, package.sh. `MacKeeper.cs`:16 (the Keychain service name) is left as its own text on purpose: it is the key the kept sign-in is stored under and must not follow a rename of the app.
- H53 `AlphaPremierIdMaker`: csproj, scripts and workflows; `ThemeManager.cs`:56 (N18) would need the assembly's name at run time, which is not a read-only-provable swap.
- H54 remainder: selftest.ps1:30, portable.ps1:47; `last-error.txt` has one C# site and two script sites.
- H55 remainder: both csproj files, package.ps1, and the tests (AppTests.cs:96, WindowTest.cs:379-381), which keep their own spelling so they do not follow a wrong constant.
- H56 note: `Session`'s stored value words `duplex` / `pairs` and `Batch.LayoutDuplex` / `LayoutPairs` are the same text for two different things (a saved setting, a choice in the batch tool); they were not merged.
- H59 picture types: the Windows dialog filter is a different form of the rule; the Mac panels have no filter (a behaviour difference).
- H65 two limits named `MinHeadPx`: outside H44-H60 and a rename, left.
- H10 copies ceiling (`PerSheet * 20`, four sites): outside H44-H60 for this round; a one-constant fix for the next.

Workflows left:

- W1, W2, W3, W6, W7: repeated step blocks; removing them needs a composite action or reusable workflow, excluded this round.
- W4 / W5 in release.yml: each value is used once there; W4's tie to `mac/workloads.json` and W5's to `mac/global.json` remain by comment.
- W8 runner labels: `runs-on` cannot read `env`.
- W10 the two push filters differ (windows.yml watches every workflow file, mac.yml only its own): a change in when CI runs, not a consolidation.
- W11 `permissions` / `concurrency`: per-file by nature without shared parts.
- W12 the `find … *.app` line (mac.yml twice, package.sh once): sharing it between steps means passing state through `$GITHUB_ENV`, more than a plain consolidation.
- W13 remainder: report file and screenshot names shared with test code by spelling.
- W14 three spellings of "run the Core tests", W16 action versions at every use.
- W15: nothing to change; the four job names were kept.

## E. Tests that cannot fail, or only repeat the implementation (listed, not deleted)

None of the 121 test methods in `windows/IdMaker.Tests` and `windows/IdMaker.App.Tests` is empty or assertion-free, and none is skipped. The ones below are the weak spots found by reading.

Repeat the implementation's own formula or constant:

- `windows/IdMaker.Tests/SessionTests.cs`:483 `One_copy_each_unless_more_are_asked_for_and_never_more_than_twenty_sheets`, lines 500 and 502: asserts `plan.PerSheet * 20`, the expression Core/Session.cs:721 and :730 use (H10). The earlier assertions in the same test ("1 page of A4", "2 pages of A4", 5 entries) are real.
- `windows/IdMaker.Tests/CoreTests.cs`:384 `A_box_cannot_be_dragged_off_the_card_or_shrunk_past_grabbing`: `Assert.Equal(2, b.W)` restates the private `MinBoxMm = 2`; the clamps to 0 and to the card's height are real.

Check the test fixtures, not the app:

- `windows/IdMaker.Tests/SecretTests.cs`:71 `Tests_use_a_project_that_does_not_exist`: asserts on `Make.Project` and `Make.LocalRegister()`, both test code. It can only fail when `Support.cs` / `FakeFirebase.cs` is edited. It is the guard for the AGENTS.md rule "tests never call the real project", so it is a candidate to keep, named for what it is.
- `windows/IdMaker.Tests/CoreTests.cs`:323 `A_value_that_no_longer_parses_reads_as_the_fallback`: runs against `MemoryStore`, the test double that lives in Core (S14). It does exercise the shared `Read` helper, and what it pins is the behaviour X3 lists as a problem (damaged JSON silently becomes the fallback).

Exercise members only tests reach (they pass or fail without telling anything about the apps):

- `windows/IdMaker.Tests/SessionTests.cs`:469 `The_example_card_is_never_written_to_the_register`: `LoadExample` is called by no app (S13), so the guard it proves protects a mode nothing can enter.
- `windows/IdMaker.Tests/SessionTests.cs`:703 `Assert.Equal("demo-idmaker", ping.Sheet)`: the only reader of `PingResult.Sheet` (S11).
- `windows/IdMaker.Tests/CoreTests.cs`:46 `File_names_carry_the_number_and_a_plain_surname`: three of its four cases call `Naming.PdfFileNameFromWholeName`, which only tests call (S14). It is a differential test against `web-reference.json`, so it holds the function to the web app, but the function is not in use.
- `.Moved` assertions at `SessionTests.cs`:15, 19, 38, 41, 90, 110, 183: `MoveResult.Moved` is read by no window (S10); the neighbouring `StepIndex` / `FocusField` assertions are the ones that describe what an operator sees.
- `IndexLoaded` assertions at `SessionTests.cs`:346, 967, 992 (S14).

Weak assertions inside otherwise real tests:

- `windows/IdMaker.App.Tests/AppTests.cs`:612, 614, 658, 684 and `windows/IdMaker.Tests/VisionTests.cs`:40: `Assert.NotNull(…)` on a picture source or a matte; each is followed by stronger checks in the same test except AppTests.cs:684 (the sheet preview after a fixed `Task.Delay(400)`, X11).

Written twice, once per platform (not a repeat of the implementation, but one changes without the other):

- H69: the golden-card zones and limits, the font check and three window scenarios exist in `AppTests.cs` and again in `mac/IdMaker.Mac/SelfTest.cs` / `Tests/WindowTest.cs`.

---

# ID Maker untangle, round 2: decision log

Branch `refactor/untangle-2` (from `main` at `96b6b60`, which holds round 1). Nothing pushed. The owner was not there to ask; every call made for him is in the list at the end of this file.

What could be checked here, and what could not:

- `windows/IdMaker.Core` has no package, so it builds on this machine against an empty package source (`dotnet build --source <empty folder>`), with warnings as errors as on CI.
- The Core tests need xunit, which cannot be fetched here. They were compiled and run against a small local stand-in for xunit (attributes, `Assert`, a runner) kept outside the repository. It runs every test in `windows/IdMaker.Tests` except `VisionTests.cs` (which needs the ONNX Runtime package): 124 passed before round 2 began. A stand-in is not xunit: its analyzers do not run here, so CI is still the check for the tests' own style.
- `windows/IdMaker.App`, `windows/IdMaker.App.Tests` and `mac/IdMaker.Mac` (WPF and AppKit) cannot be built here at all. Every edit there was checked by reading: each call against the signature it calls, each name by a whole-repository grep.

Each commit's `Verified:` line says which of these it had.

## Round 2 fixes

### 1. The screen model (root fix 1)

- `17065c7` Core: `Screen.From(session, input)` in `windows/IdMaker.Core/Screen.cs`, with `ScreenTests` (29 tests, no window).
  - H1 the line under the number; H2 "Still needed"; H3 the step chips; H4 "an error shows once its field was touched" and its three exceptions; H5 the photo step's ladder; H6 the stage captions and the sheet caption (`Session.SheetPreview`); H7 Back / Next; H8 where the cursor goes (`Screen.AfterMove`, `FocusFor`, `LandingFor`); H9 the check step; H10 the copies ceiling (`Session.MostCopies`, one constant, was written four times); H11 the "In this print" strip; H12 the Issued IDs drawer; H19 the batch chooser lines and layout line; H21 a batch row's report; H23 the contrast sentence; H25 which tool is open and its title; H27 the summary (one null check, not `!` in one window and a check in the other); H28 `Session.SecondRoleHides`; H29 the signature step; H68 `Words.S` / `Words.Why` for new Core code.
  - N8: the windows no longer step `PreviewPage` blind; `TurnPreviewPage` and `SheetPreview` own the range. N10: `AfterMove(from)` takes no made-up `MoveResult`.
- `bc47598` The platform flag is named `HostPlatform` (Apple's bindings have had a `Platform` of their own).
- `2f8127f` The Windows window reads the model. `a703ea5` The Mac window reads the model.
  - What each window still holds: which control is which (`ControlFor`), how a tone or a chip is painted, pictures, and the state Core cannot know (`ScreenState()`).

Left from root fix 1: H30 (strings fixed in XAML and typed again in the Mac window's constructor: 77 pairs) and H67 (steps addressed by number in XAML tags and the Mac's `_steps[n]`). Both are markup-against-code and need the scanning test or a shared string table; neither is a rule. `Session.Touched` is still public (N7): tests read it. `PreviewPage`'s setter is still public: tests may set it.

### 2. The register's fields and the card's rules (root fix 5)

- `033b121` `WireFormatTests`: the document sent to Firestore, to the letter (names, types, order), written and passing before the code below changed.
- `5b4eb96` `RegisterFields.All` in `Core/Register.cs` is the one list of an issued ID's fields. The document sent (`Document`), the document read (`ParseEntry`), the CSV saved (`ToCsv`) and the CSV brought in (`ImportCsv`) are made from it (H61, five of its eleven places). `RegisterEntry.Issue` is the one constructor of an entry for the form and the batch, and `RegisterEntry.Stamp` the one spelling of the issue time (H61, H62). `CardRules` (`Core/Rules.cs`) holds which characters a name, a suffix and a title may have and what a number is (H46, H63); `Session.Problems` and `Batch.PrepareRow` both call it.
  - What is sent did not change: `WireFormatTests` passes unchanged, and a test holds the list to the table in `register/README.md`.
  - H61's other places are outside this app's code and unchanged: `FakeFirebase.cs` (kept as an independent stand-in on purpose: a fake that serialised with the app's own table could not catch the table being wrong), `register/migrate.mjs`, and `firestore.rules` in apg-keystone.
  - H62's second half (the replacement document's name in `migrate.mjs`) and H64 (the print file's name and title, which differ by design between a print and a batch) are left.
  - H65 (two limits called `MinHeadPx`) is left: a rename inside the photo pipeline, not a duplicated rule.

### 3. The jobs (root fix 2)

- `731bf1d` Core: `Core/Jobs.cs` with `JobTests` (18 tests, stand-ins only).
  - `Session.SavePrint` writes the PDF (`PrintFiles.Write`: to `.part`, moved over when whole) and only then calls `MarkIssued`; the shared register is told after that. H16, H17. `An_id_is_issued_only_after_the_pdf_is_saved` watches the register's own list being written and finds the PDF whole on disk at that moment, and no card issued while the page was being drawn; `A_print_that_cannot_be_saved_issues_nothing` and `A_page_that_cannot_be_drawn…` hold the other side.
  - `Session.WarmRegister`, `SignIn`, `SignOut`, `CheckRegister`, `ImportRegister`, `RegisterCsv`, `ClearRefused`: H13, H14, H72 (the window no longer has to remember `RefreshDuplicate`), N11.
  - `Session.ReadSharedRegister`, `BatchServices`, `IssueBatch`: the batch's issue loop and its copy of the back-upside-down formula (H20).
  - `Session.TakePhoto`, `TakeSignatureScan`, `CheckingPhoto`: H15, H74 (the "card it was for is gone" check is Core's), X7 (one place, with the reason it stays).
  - `DesignDesk`: begin / end / keep / discard / reset boxes / adopt artwork / back to built-in, each followed by the form's map being brought up to date. H22 (part), H24, H70. `PrintFiles.BackPictureFor` and its two messages: H18.
- `cc7c44a` The Windows window calls them; its `_register` field and its `WritePdf` are gone. `53ef927` The Mac window calls them; it keeps the `Register` it made only for its window test to read.

Left in the windows, with the reason:

- The batch's orchestration (read the list, check rows off the window's thread, draw pages, save, report) is still ~45 lines in each window (H20's larger half). Its steps are interleaved with status lines the two windows word and time differently ("Building the PDF: page 3 of 12…" on Windows, "Building the PDF…" on the Mac) and with each platform's renderer; moving it needs a host interface with seven members, which could not be compiled here for either window. The two Core rules it copied are gone from both.
- `UseArtwork`'s loading, resizing and saving of the two pictures (H22's other half) is each platform's picture code.
- H71 (`Session.Measurer` set by the window after the fonts load) and H73 (148 `Render()` calls; `Session.Changed` is raised by `TakePhoto` and the late register answer only) are as they were.
- The state of a save (`_saving`, `_saved`, `_savedCount`, `_savedPath`) is still the window's, passed to Core through `ScreenInput`.

### 4. One home, and a test that reads the files (root fix 4, remainder)

- `553337a` `HomeTests` (9 tests) reads `MainWindow.xaml`, both `.csproj` files, `app.manifest`, `Info.plist`, `IdMaker.iss`, `selftest.ps1`, `portable.ps1`, `package.ps1`, `package.sh`, the two palettes, and three `.cs` files that spell a value as bytes or per cent, and holds each value to its Core home: the version (H44: five places, the manifest and `ApplicationDisplayVersion` included, which the release workflow does not check); the app's name, publisher and program name (H52, H53, N18); the data folder and `last-error.txt` (H54); the built-in pictures (H55); field lengths (H45, so S2's three constants now have a reader) and the number box (H46); the artwork size (H47); the zoom slider on both platforms (H48); the card surface (H49); the tool, layout, kind and step tags (H56, H57, H58, H67's three Edit tags); gold and screen ink (H50, H51).
  - New Core homes for values that had none: `CardConfig.Publisher`, `ProgramName`, `LastErrorFile`, `BrandGold`, `ScreenInk`. No value changed.
  - Not covered: the Mac's colours written as rounded fractions (0.83, 0.69, 0.22 and 0.11, 0.11, 0.10: not the same numbers as the hex, so a test would have to allow a tolerance or the colour would have to change); the printer-flip tags `long` / `short` (their Core home is two private constants of `Session`); H59 (the Windows file-dialog filter); H66 (the pen driver pinned twice).

### 5. One drawing plan for the front of the card (root fix 3, in part)

A series of three, last on the branch, so it can be dropped: revert `3/3`, then `2/3`; `1/3` draws nothing and can stay or go.

- `e51fe03` 1/3 Core: `CardDrawing.Front` (`Core/Drawing.cs`) returns the front as ordered operations: `ArtworkOp`, `PhotoOp` (clip, fade), `ArtworkOp` inside the front outline, a `WordOp` per word, `InkOp` or `ScanOp`, `QrOp`. `DrawingTests` (6) hold the order and the placement without a renderer.
- `f574158` 2/3 The Windows renderer draws from the list. `80494d1` 3/3 The Mac renderer draws from the list.
  - H31 (the front's order and the fade's numbers), H32 (line placement), H33 (the QR code's snapping, quiet zone and run merging), H34's second half (fitting a signature into its box), H38's `Words`: each now once, in Core.
  - How the card is kept from moving: every expression that produces a coordinate was moved, not rewritten. Where a renderer multiplied millimetres by the resolution itself (clip polygons, the QR code's rectangles, which the two renderers even compute differently: corner to corner on Windows, corner and size on the Mac) the operation carries millimetres and the renderer still does its own multiplication. Where the position was already in pixels (text, ink, the photo rectangle) the operation carries the pixels. Word offsets come from `ITextMeasurer.Width`, which for a single word and for a single space returns exactly what each platform's private `WordWidth` did (0 spaces times the space width, plus the word).
  - So the brief's "positions in card units" holds for clips and the QR code and not for text, ink or pictures; converting those to millimetres and back would have been a second rounding on the way to the printer.

Left from root fix 3: H35 (a sheet: page rounding, the turned card, crop marks), H36 (`SheetPdfPages` / `CardPdfPages`), H37 (flattening onto white), H39 (which file is which font), H40 (`CardArt` declared twice), H41 (what picture is too big), H42 (the map editor), H43 (the signing pad), N1 (the fonts' folder), N13 (the guides overlay is still a parameter for one caller) and X15 (the photo box kept apart). The back of the card is one picture and was left as it is. `WpfTextMeasurer.Words` and `MacTextMeasurer.Words` are now unused and were left in place: deleting a public member of a file that cannot be compiled here was not worth a failed build.

### 6. Special cases (X1 to X23)

Made unnecessary by the fixes above and gone:

- X7: four copies of "a file whose size cannot be read is given size 0" are one (`Session.SizeOf`), with the reason it stays.
- X9: four of the six commented empty catches are two (`PrintFiles.Write` for the half-written file, `DesignDesk.BackToBuiltIn` for an orphaned picture).
- X17 (second half): "signing opens by itself on arriving at the signature step" is once, in `Screen.AfterMove`. X18: the register drawer's special title is once, in `Screen`.
- X20: `WritePdf(progress)` is gone with the windows' `WritePdf`; `PrintFiles.Write` keeps the parameter for the Windows batch's page counter.

Still needed, now saying what outside fact needs them (`a9511ff`, `05f015f`):

- X1, X2, X3 (`FileStore.Get`, `Set`, `StoreJson.Read`): another program holding the file, a full or read-only disk, a file from an older version. What each costs is written beside it. Making the store able to say "could not" is root fix 9, not done.
- X11: the 120 ms wait before the sheet preview is drawn on Windows.
- X22: SAFETY comments on the unchecked marks this round touched (two in `Session`, the paper and flip casts and three marks in the Windows `RunBatch`, the cast in the new `ScanOp` branch).

Not touched: X4, X5, X6, X8 (each returns a default with a comment already there, or in a file this round did not change), X10, X12, X13 (tests and scripts waiting on a process or a network), X14 (one retry after a 401, commented), X15, X16, X19, X21, X23.

### 7. Comments and tests

- Comments: the files this round wrote (`Screen.cs`, `Jobs.cs`, `Rules.cs`, `Drawing.cs`, the new tests) say why, or what outside fact, not what the next line does. In the four window files the comments that described a rule were moved to Core with the rule; the ones left say which control or which platform fact. No pass was made over untouched files.
- `b0e90a4` The copies ceiling is asserted as 80, not as `plan.PerSheet * 20`.
- `3e4f831` `PingResult.Sheet` and the one assertion that read it are gone (S11).
- `fbf2fd0` `MoveResult.Moved` and the seven assertions on it are gone (S10); the tests now assert the step the form is on.
- Kept, though round 1 listed them: `A_box_cannot_be_dragged…` (`Assert.Equal(2, b.W)` pins a private constant from outside and does fail if it changes); `Tests_use_a_project_that_does_not_exist` (the guard for an AGENTS.md rule); `A_value_that_no_longer_parses…` (exercises `StoreJson.Read`); the `IndexLoaded` assertions (they test the register's state through a test-only window, and can fail); `The_example_card_is_never_written…` and the `PdfFileNameFromWholeName` cases (whether the example card and that function stay is S13 / S14, the owner's call); the weak `Assert.NotNull` lines in `AppTests.cs` (cannot be compiled here).

## Differences preserved, for the owner to unify

Each is kept exactly as it was on `main`; where Core now makes the choice it is by `HostPlatform`.

1. Issued IDs listed in the drawer: Windows 25, Mac 12 (`Screen.IssuedListed`).
2. The batch tool's name: "Batch from CSV" on Windows (button and drawer title), "Batch from a list" on the Mac (`Screen.ToolTitle`).
3. The note when no tablet driver is found: Windows "…sign with the mouse or use a scan. The installer can add the XP-Pen driver."; Mac "…sign with the mouse or trackpad, or use a scan. The app can install the XP-Pen driver." (`Screen.From`, `SignView.TabletNote`). The second half is true of each platform, so only the first half is a candidate to unify.
4. A row of the Issued IDs list: Windows puts the date at the right of the number; the Mac writes "number · date" in one label. Core gives the pieces (`IssuedRow.Head`, `.When`, `.Detail`).
5. Removing a remembered job title: Windows by right-clicking its chip; the Mac by a button under the list for the title that is picked (`TitleView.ForgetShown`, read by the Mac only).
6. Where the cursor lands on the job-title step: Windows on the chip that is ticked (or the first); the Mac on the list. Core says `TitlePicked` / `TitleChoice`; the Mac maps both to its one list.
7. After a move to another step Windows scrolls the form to the top; the Mac's form does not scroll.
8. "Saved … as pictures. The card is not issued until you save the PDF.": on Windows it is replaced at the next redraw; on the Mac it stays until "Start a new ID".
9. The stage: Windows has a card view and a sheet view; the Mac shows both in one picture, and draws a grey hairline round the sheet (`PaperEdge`). The sheet is drawn 120 ms after the last change on Windows, at once on the Mac (X11).
10. Messages that are empty: the Mac hides the label (photo message, caption, register message); Windows leaves an empty line of the same height.
11. The photo button and the Sign button are drawn "strong" until there is a photo / signature on Windows only (the Mac has one button style).
12. The mouse cursor over the card (four arrows when the photo can be dragged) is Windows only, and its rule does not look at the step as `CanDragPhoto` does; the card is hidden on the last step, so the two never disagree on screen. Left as written.
13. `Screen.LandingFor` keeps the branch "saved → Start a new ID button" although no move can reach it (a move to another step always forgets the save first). Both windows had it; kept, not pruned.

The form and the batch (H63). They now ask the same `CardRules`, but not all the same questions; a list HR has used before must not start failing overnight, so what a batch accepts was not widened or narrowed:

14. The batch checks the whole printed name against the name rule; the form checks each part, and the suffix against the looser suffix rule. So "3rd" as a suffix passes the form and fails a batch.
15. The batch does not check a job title's characters; the form does for a title typed under "Other…".
16. An empty `kind` is "employee" in a batch (a list from before the column existed); the form refuses to guess.
17. Every message is worded differently in the two ("Enter the last name." / "No last name.").

## Calls made without the owner

1. The two documents were copied in as they were written (line numbers as on `main` before round 1), not rewritten to the tree as it is now.
2. Commit messages say what was really checked. The instruction's fixed line ("not built or run locally") was written on the understanding that nothing builds here; Core does, so commits that touch Core say so, and commits that touch only window code carry the fixed line.
3. Root fix 1: every place the two windows differ was kept as it was and listed, none unified.
4. Root fix 5: the batch keeps accepting exactly what it accepted (differences 14 to 17), rather than taking on the form's stricter rules. One input changed: an employee number written in digits other than 0 to 9 (for example Arabic-Indic) used to pass the "1 to 4 digits" pattern and then stop the whole batch with an unhandled error when it was turned into a number; it is now refused like any other bad number, row by row. The form never let such digits in.
5. Root fix 5: `FakeFirebase` was not changed to use the app's field list. The audit suggested it; a fake built from the code under test cannot disagree with it.
6. Root fix 2: `PrintFiles.Write` reports a page that could not be drawn (`InvalidOperationException`) as "The file couldn’t be saved there (…)", on both platforms. The Mac did; Windows did not catch it, so there a save could end with no message and the button left saying "Saving the PDF…". The Mac's behaviour was taken for both.
7. Root fix 2: on signing in, the password box is emptied as soon as the password has been handed to Core, rather than after the first answer comes back. It is never held longer than before.
8. Root fix 2: after a batch whose cards could not all reach the shared register, the number on the form is checked again inside `IssueBatch` (as both windows did); after an ordinary save it is not (as neither window did). Kept as found.
9. Root fix 2: the batch's orchestration and the state of a save were left in the windows (see "Left in the windows" under fix 3) rather than moved without a compiler.
10. Order of work: the drawing plan (step 5) was done after the special cases and the tests (steps 6 and 7), so that its three commits are the last on the branch and can be dropped without touching anything else.
11. Drawing plan: operations carry pixels for text, ink and pictures and millimetres for clips and the QR code, rather than card units throughout, so that no coordinate is computed differently from before. Only the front was planned; sheets, the PDF pages and the back were left.
12. Tests: of the tests round 1 listed, three were changed or removed (each its own commit) and the rest kept with the reason given under fix 7. None of them was strictly unable to fail.
13. `HomeTests` needed five values to have a Core home that had none (`Publisher`, `ProgramName`, `LastErrorFile`, `BrandGold`, `ScreenInk`); they were added to `CardConfig` with the values the other files already had.

---

# ID Maker untangle, round 3: decision log

Branch `refactor/untangle-3` (from `main` at `109988a`, which holds rounds 1 and 2). Seven commits, nothing pushed. It answers the independent review of round 2 (PR #16): one issuing hole older than the untangle, and the ten things the review's Part 2 called safe to do without the owner. Review item numbers below are that review's (P1, M1, m1 to m7, and Part 2 items 1 to 10).

**Not released.** Rounds 1 and 2 are on `main`; round 3 is on this branch and will be on `main` once it is merged. None of the three is in a release: the version was not bumped in any round. It is still 2.0.4 (`CardConfig.AppVersion`, both `.csproj` files, `app.manifest`), and `v2.0.4` was tagged on 17 Nov 2026, before round 1's first commit. The app HR runs is the one from before the untangle, the issuing hole in fix 1 included. A version bump publishes a release to HR and is the owner's call.

What could be checked here, and what could not (the same as round 2):

- `windows/IdMaker.Core` builds on this machine from a copy in `/tmp`, against an empty package source, warnings as errors: 0 warnings, 0 errors after every commit.
- Console programs against that build, kept outside the repository: the review's `save-while-editing` and `register-old-vs-new`, and two written for this round (six ways a PDF write can end; 54 screens drawn before and after). Each was run on the commit before a change and on the commit after it.
- The Core tests, compiled with warnings as errors and run against a small local stand-in for xunit (every test file except `VisionTests.cs`): 223 passed on `main`, 255 at the end of the round (each row of a theory counted). A stand-in is not xunit, and its analyzers do not run here.
- `windows/IdMaker.App`, `windows/IdMaker.App.Tests` and `mac/IdMaker.Mac` cannot be built here. One line changed in each window (fix 4), checked by reading. CI on Windows and macOS is the check.

## Round 3 fixes

### 1. A saved print issues the cards that were printed (review P1, Part 2 item 1)

- `3dfd145` `Session.SavePrint` wrote the PDF and then called `MarkIssued()`, which read the form again. The form stays live during the write (only Save, the pictures and "Add another ID" are off), so:
  - a queued card removed meanwhile was in the PDF and never in the register, its number free to be given to somebody else;
  - a name typed again meanwhile was registered as typed, for a card printed with the earlier name.
  - Both windows did the same before the untangle; round 2 carried the order into Core unchanged.
- Now `Session.Print()` fixes what the print issues in a `PrintIssue` carried by the `PrintJob` (the job's cards, whether it is the example, the job titles typed under "Other…"), lays the pages out from that same list of cards, and `SavePrint` records exactly that. A queued card that was in the print leaves the queue by reference; `MarkIssued()` with no argument still means "the form as it stands", for its tests.
- No window edit: `Print()` and `SavePrint(path, job, pages)` have the signatures they had.
- Tests (`JobTests`): `A_queued_card_removed_while_the_file_is_written_is_still_issued_as_printed`, `A_name_typed_again_while_the_file_is_written_does_not_change_what_is_issued`, `A_print_job_carries_the_cards_it_will_issue`.
- Harness (`save-while-editing`), two cards in the print:

  | While the file is written | Register before | Register after |
  | --- | --- | --- |
  | a queued card is removed | APG-2026-125 Ana Cruz | APG-2026-124 Ana Reyes, APG-2026-125 Ana Cruz |
  | the name is typed again | APG-2026-124 Ana Reyes, APG-2026-125 Ana Santos | APG-2026-124 Ana Reyes, APG-2026-125 Ana Cruz |
  | nothing | APG-2026-124 Ana Reyes, APG-2026-125 Ana Cruz | the same |

- `docs/STATE-MACHINES.md` section 6 gains the invariant (I5) and `REVIEW.md` a row for it, under AGENTS.md's "a miss becomes a rule". That is a change to the list changes are judged by, made in the same branch as the code it describes; the pull request should say so at its top.

### 2. A batch year in other digits (review m5, Part 2 item 2)

- `72b6ca4` The `year` column was matched with `\d{4}`, which takes Arabic-Indic and full-width digits, and then parsed with the invariant culture, which does not: one such row stopped the whole batch with `FormatException`. Round 2 fixed the same fault in `seq` and left this one. `CardRules.YearNumber` takes four of 0 to 9; anything else is no year and the row gets this year, as a year in words always did.
- Tests (`RulesTests`): `A_year_is_four_of_the_digits_the_card_prints` (10 rows), `A_year_in_other_digits_falls_back_to_this_year_and_the_batch_goes_on`.
- Harness (`register-old-vs-new`, `main` against this commit): 172 lines before, 178 after, and the only difference is the `batch-year-arabic` case (THROWS `FormatException` before; `APG-2026-012` made after).

### 3. A page that could not be drawn says so (review m1, Part 2 item 3)

- `9fafb30` `PrintFiles.Write` caught every `InvalidOperationException` and answered "The file couldn’t be saved there (…). If it is open in another program, close it, or choose a different folder." That type is what a window's page function throws when it cannot draw, and also what a slip in the drawing code throws, for a print and for a batch.
- Now each page function is wrapped: an `InvalidOperationException` out of one becomes Core's own `PageNotDrawnException` (private to `PrintFiles`), the only one of the kind `Write` catches. Its message: "A page of this print couldn’t be drawn (…). Nothing was saved. Try again; if it happens again, close the app and open it again." A file that cannot be written reads as before. Nothing is issued either way.
- Tests (`JobTests`): `A_page_that_cannot_be_drawn_fails_the_save_and_leaves_no_half_file` (now holds the whole message), `A_slip_in_page_drawing_is_not_reported_as_a_file_open_in_another_program`, `A_failure_of_another_kind_in_page_drawing_is_not_caught_as_a_save_that_failed`.
- Harness (six ways a write can end, before and after): the three `InvalidOperationException` cases changed message; a folder that is not there, a good save and an uncaught `FormatException` are unchanged.

### 4. One count of the cards per redraw (review m6, Part 2 item 4)

- `274659f` `Screen.From` built the check step at every step, and `PrintFileLine` counted the print's cards a second time (making a second register entry for the card on screen, to throw away). `Screen.Check` is now null off the last step, and `PrintFileLine(cards)` takes the count `Screen.From` already has.
- The two windows: `RenderCheckStep(m.Check);` became `if (m.Check is { } check) RenderCheckStep(check);`, the same line in `MainWindow.xaml.cs` and in the Mac's `MainWindow.cs`. Both already returned from `RenderCheckStep` before reading the view unless the last step was open, so what is shown is the same. Not compiled here.
- Tests (`ScreenTests`): `The_check_step_is_made_only_where_it_is_shown`, `A_redraw_counts_the_cards_in_the_print_once`.
- Harness (54 screens: 18 states of the form, three inputs, both platforms; before and after): every part but the check step identical at every step, the check step identical on the last step, 109 lines with no difference. Clock reads in one redraw of a finished card off the last step: 2 before, 1 after.

### 5. Four names (review Part 2 items 5, 7, 8, 9)

- `2d2be67`
  - Item 5: `RegisterField.Sent(entry)` names what `Register.Document` spelled as `field.TextOf(field.With(new RegisterEntry(), field.TextOf(e)))`. Test: `What_is_sent_for_a_kind_is_what_would_be_read_back`. Harness (`register-old-vs-new`): 178 lines identical to the commit before; against `main` the only difference is still fix 2's. The Firestore document is field for field what it was.
  - Item 7 (H65): the private `MinHeadPx` in `Photo.cs` (40, a refusal) is `UnusableHeadPx`; `PhotoSpec.MinHeadPx` (180, a warning) keeps its name. Neither number moved. Test: `A_head_too_small_to_show_a_face_is_refused_and_a_merely_small_one_is_warned_about`, which nothing pinned before.
  - Item 8 (H68): `Words.S` in `Batch.FileFor`, `Session.PrintFileLine` and the two "waiting to be written" lines of the Issued IDs drawer, with a new `Words.IsAre` for the verb in the last two. The pinning tests (`The_issued_ids_drawer_counts_the_waiting_cards_in_words`, one more line in the batch file-name test) were written first and passed on the old code.
  - Item 9 (difference 13 of round 2): `Screen.LandingFor` has no `saved` parameter and no branch for it. `AfterMove` was its only caller and passed false whenever it called. `FocusTarget.NewIdButton` stays: each window sends the cursor there itself after a save.

### 6. Two things held by tests (review Part 2 items 6 and 10)

- `e20f9ff` No code changed.
  - Item 6: `The_migration_script_sends_the_fields_of_the_list_under_the_same_names` reads `register/migrate.mjs`: the fields `documentOf` writes are the always-sent fields of `RegisterFields.All`, in order and of the same kind, then `by` and `byEmail`; every sheet column it reads is a field of the list. With the table in `register/README.md` (round 2), two of H61's three places outside the app's code are now held to the list; `firestore.rules` in apg-keystone is still held by nothing here.
  - Item 10: `DrawingTests` for left, centred and right text in all three boxes; a name on two lines; a scan in a signature box that was moved, wide and tall. They assert on `CardDrawing.Front`'s operations, with the expected numbers written from the box rather than by calling the code that places them. Each was seen to fail against a copy with the fault it is for.
  - These hold Core's arithmetic. They do not show that either renderer draws what the list says: that is still only the golden card.

### 7. This section

- This commit: the section above, invariant I5 in `docs/STATE-MACHINES.md` section 6, one row in `REVIEW.md`.

## Found by the review and not changed

Recorded so they are not found again as news.

- **M1 (coverage gap, no defect seen).** No Mac test draws a scanned signature or a photo that fades: `MacCard.cs`'s `ScanOp` branch and the fading half of its `PhotoOp` branch were rewritten in round 2 (`80494d1`) and have never produced a pixel anybody compared. The numbers handed to them are the old ones (the review's replay: 960 cases, 218,672 drawing steps, 0 differ), and round 3's drawing tests hold those numbers in Core. A Mac is needed to hold the pixels.
- **m2 (ordering, undeclared in round 2, harmless).** The shared register is told before the window shows "saved" (`SavePrint` and `IssueBatch` start the request themselves); the windows used to start it after redrawing. Only the start of the request moved.
- **m3 (ordering of clocks, undeclared, harmless in the app).** A form print's PDF takes its creation date from the Session's clock; a batch's PDF still takes `DateTime.Now` from the window while its entries are stamped by the Session's clock. The same clock in the shipped app, two under a test clock.
- **m4 (ordering, undeclared, invisible).** Windows writes "Reading the signature…" before a file that is refused outright, and the Mac redraws when a second photo is offered while one is being checked. Both are overwritten in the same turn.
- **m7 (declared in round 2).** `WpfTextMeasurer.Words` and `MacTextMeasurer.Words` are dead copies of `CardDrawing.Words`.

## What is left

Needs a compiler for the windows, a Mac, or the owner:

- A Mac test that draws a scanned signature and a fading photo (M1).
- One clock for a batch (m3): the window passes `DateTime.Now` to `PrintFiles.Write` and `Batch.FileFor`; it should be the Session's.
- Deleting the two dead `Words` methods (m7): public members of window files.
- The store reporting a failed write (X2, root fix 9): `FileStore.Set` still drops a write that fails, an issued card included.
- The drawing plan for the back, the sheets and the PDF pages (H35, H36, H37): only the front is a list of operations.
- The batch's orchestration, still about 45 lines in each window (H20's larger half), and the state of a save (`_saving`, `_saved`, `_savedCount`, `_savedPath`).

Seen while doing this round, and left:

- An exception from page drawing that is neither an `InvalidOperationException` nor one of the file errors is not caught by `PrintFiles.Write` (it never was): the `.part` file stays on disk and the window is left saying "Saving the PDF…" until the app's own error report takes over. Catching more would hide slips; removing the `.part` file in a `finally` would be safe.
- After a save, the number on the form is marked as taken by the last card issued (`DupLocal`), as before. If the operator changed the number on the form during the write, that mark is for the printed card's number, not the one now typed, until the next keystroke checks again.
- `Session.Print()` still asks for the print's cards twice (once for what it issues and its pages, once more inside `PrintFile` for the title). Same answer both times, in the same turn.

## Calls made without the owner, round 3

1. Fix 1 records the titles typed under "Other…" as they were on the card when the print was made, not when the write ended, so that everything a save remembers comes from the print.
2. Fix 1 removes from the queue the cards that were printed, rather than emptying it. Today those are the same cards (nothing can be added during a write); if that ever changes, a card that was not printed stays queued and unissued.
3. Fix 3 wraps only `InvalidOperationException` from a page function, as the review asked, not every exception: the rule is that an exception is caught by type where something can be done about it.
4. Fix 3's message tells the operator to try again and then to restart the app. There is nothing more exact to say from Core about a page the window could not draw.
5. Fix 4 made `Screen.Check` nullable, which needs one line in each window, rather than handing the windows a made-up check step off the last step that nothing reads. A null cannot be mistaken for a real one.
6. Fix 5 added `Words.IsAre` beside `Words.S` so "card is / cards are" is not a third spelling.
7. `REVIEW.md` and `docs/STATE-MACHINES.md` were changed in this branch (see fix 1).
