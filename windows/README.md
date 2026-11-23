# The Windows app

Alpha Premier ID Maker as a native Windows program: C# on .NET 8, drawn
with WPF. No browser inside it and no web page: the card, the form, the
PDF and the signing screen are all Windows' own.

It replaced a web app (plain HTML and TypeScript, in a Tauri window),
which was removed on 17 Nov 2026 and is in git history before that. Two
things of it remain on purpose: `test/golden-front.png`, the card it
printed, and `IdMaker.Tests/Fixtures/web-reference.json`, the numbers it
produced. The tests hold this app to both.

The people who use the app want the **[README at the top](../README.md)**.
This page is for whoever changes it.

---

## What is where

```
windows/
  IdMaker.sln
  IdMaker.Core/        every rule about the card, with no window attached
    Config.cs            EVERY measurement: card size, text boxes, photo
                         placement, QR, signature box, number format,
                         register address, the AI photo prompt
    Session.cs           the form: what is typed, what is wrong with it,
                         which step may be open, what is waiting to print
    TextFit.cs           shrinks and wraps the name and title to fit
    Photo.cs             the photo pipeline: classify the background, key
                         out green or blue, find the head, place it
    Signature.cs         strokes (with pen pressure) and scans, as ink
    Geometry.cs          the card design in use: the map, and own artwork
    Sheet.cs             how many cards fit on a sheet, and where
    Pdf.cs               writes the PDF by hand, any number of pages
    Qr.cs, QrTables.cs   the QR code
    Register.cs          issued IDs: this computer's list, the shared register
                         (Firestore and Firebase Auth over REST), signing in
    Batch.cs             many cards from a CSV
    Store.cs             small named values kept between runs; job titles
  IdMaker.Vision/      background removal (the only outside dependency)
    BackgroundRemover.cs runs the portrait-matting model
    Models/              the model, fetched by the build; see its README
  IdMaker.App/         the program
    MainWindow.xaml      the window's layout
    MainWindow.xaml.cs   the form, the preview, photo and signature steps
    MainWindow.Print.cs  the last step and the four tools
    CardRenderer.cs      draws front, back and sheets at any resolution
    SignaturePad.cs      the pad, and the full-screen signing window
    PenTablet.cs         pen pressure straight from the tablet driver
    MapEditor.cs         the draggable boxes over the card
    Theme.xaml           type and control styles, every colour by name
    Colors.Light.xaml    the colours in light mode
    Colors.Dark.xaml     the same names, in dark mode
    ThemeManager.cs      reads Windows' light/dark setting and follows it
    WindowsKeeper.cs     wraps the register sign-in with Windows' DPAPI
    Fonts/               the three card fonts (see its README)
  IdMaker.Tests/       tests that run anywhere (Linux, macOS, Windows)
  IdMaker.App.Tests/   tests that need Windows: the renderer, the window
  installer/           the setup program's script, the pinned driver, the build scripts
```

The rule that shapes it: **`IdMaker.Core` decides, `IdMaker.App` shows.**
Anything that can be wrong about a card — a name that does not fit, a
number already issued, a step opened too early — is decided in Core and
tested there without a window. The window redraws itself from the
`Session` every time something changes (`Render()`), so no two parts of
the screen can disagree about where things stand.

Core has no dependencies at all and no image codec; the app hands it
pixels. Vision depends on ONNX Runtime and nothing else.

---

## Building, testing, packaging

You need the **.NET 8 SDK**. Nothing else.

```bash
dotnet build windows/IdMaker.sln
```

```bash
dotnet test windows/IdMaker.Tests
```

```bash
dotnet test windows/IdMaker.App.Tests
```

The first builds everywhere — on Linux or a Mac it compiles the Windows
program without being able to run it. The second runs everywhere. The
third needs Windows.

The first build downloads the background-removal model (25 MB) into
`IdMaker.Vision/Models/` and refuses it unless its checksum is the one in
the project file.

To run the app while working on it:

```bash
dotnet run --project windows/IdMaker.App
```

To make the setup program HR is given (on Windows, in PowerShell, with
[Inno Setup 6](https://jrsoftware.org/isinfo.php) installed):

```powershell
./windows/installer/package.ps1
```

That writes `windows/dist/AlphaPremierIdMaker-Setup.exe`. It installs the
way any Windows app does: a wizard, a Start-menu shortcut, an optional
desktop shortcut, and an entry under Settings → Apps → Installed apps
with an Uninstall button. Run over an older copy, it upgrades it. It
installs for the person signed in and needs no administrator.

```powershell
./windows/installer/selftest.ps1
```

proves all of that on the machine it runs on: it installs silently,
checks the shortcut and the Installed apps entry, installs again over the
top, starts the app, then uninstalls and checks nothing is left but the
user's own data. CI runs both on every push and keeps the setup program
as an artifact named `AlphaPremierIdMaker-Setup`.

For deploying with nobody at the keyboard (Action1 and the like):

```powershell
AlphaPremierIdMaker-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
```

Add `/TASKS=""` to leave the tablet driver out. With it in, run the
command as an administrator, or the driver step stops at a Windows prompt
nobody is there to answer.

Inno Setup is the one build tool beyond the .NET SDK. Writing a Windows
installer by hand — the uninstaller, the Installed apps entry, upgrades,
files in use — is the kind of thing it exists so that nobody has to.

To make one `.exe` that runs without installing:

```powershell
./windows/installer/portable.ps1
```

That writes `windows/dist/AlphaPremierIdMaker.exe` and starts it once to
prove it opens. It carries everything except the pen tablet driver.

### Releases

Releases build themselves. When a change to the app reaches `main`,
`.github/workflows/release.yml` runs every test, builds the package,
proves it installs and starts, and publishes it as a GitHub release
named after the version (`v2.0.3`): the setup program, the single
portable `.exe`, and a `SHA256SUMS.txt` for both.

The version is `Version` in `IdMaker.App.csproj`. A version that is
already released is not released again, so a change HR should install
needs the version bumped in three places that must agree:
`IdMaker.App.csproj`, `app.manifest` and `CardConfig.AppVersion`. The
release stops with an error if the first and last differ.

### What the tests hold the app to

`IdMaker.Tests/Fixtures/web-reference.json` holds values captured from
the web app (version 1.20.0) with its example card loaded. The native app
is held to them:

| | Held to |
| --- | --- |
| QR code | the same modules, one for one, as the library the web app used |
| Sheets | the same plan for every paper and layout |
| Names, titles, file names | the same answers from the same inputs |
| Example photo | placed within half a millimetre of where the web app put it |
| Card fonts (Windows) | the same widths and heights as Chromium measured |
| Line breaks (Windows) | the same lines at the same point size |
| The example card (Windows) | `test/golden-front.png`, zone by zone |

The last is not pixel-for-pixel and cannot be: the golden card was drawn
by Chromium and this one by Windows, which smooth edges and resample
photos differently. Each zone — photo, name, title, number, signature,
QR — has a limit on how far it may differ, tight enough that a line of
text one pixel out of place fails. CI prints the numbers and keeps the
rendered card (`front-native.png`) and a picture of the differences
(`front-difference.png`) in the `render-report` artifact, for a person
to look at.

The window tests drive the real window: an empty form refuses to move
on and says why; a card goes from typing to a saved PDF; a print that
cannot be saved issues nothing; every tool opens; the map can be moved,
kept, discarded and reset.

[`docs/STATE-MACHINES.md`](../docs/STATE-MACHINES.md) lists every part of
the app that has states, and the faults that mapping them found.

---

## The pen tablet

Signing opens `SignWindow`: the pad alone, filling the screen, with the
pointer kept inside that screen until signing is done. A small tablet
maps onto the whole display, so a pad tucked into a corner of the form
uses a corner of the tablet and the pen runs off the edge. Filling the
screen gives the hand the whole tablet.

Pressure reaches the pad one of two ways, and the pad does not care
which:

- **Windows Ink.** When *Windows Ink* is ticked in the XP-Pen settings,
  the pen arrives as a pen, pressure included
  (`SignaturePad.OnStylusDown` and friends). This also covers a finger
  on a touch screen, which has no pressure.
- **WinTab.** When it is not ticked, the pen arrives as a mouse, which
  has no pressure. The driver still knows it, and `PenTablet.cs` asks it
  directly through `wintab32.dll` — the standard interface every tablet
  driver installs, the one drawing programs use.

With pressure, the line is thicker where the pen pressed harder. Without
it — a mouse, a finger, no driver — the line's width follows speed
instead, as it did in the web app. Nothing is required: with no tablet,
signing still works.

**The driver.** `installer/Driver.psd1` names one exact file: XP-Pen's
Deco Fun driver 4.0.21, its address on XP-Pen's server and its SHA-256.
`package.ps1` downloads it, refuses it unless the checksum matches, and
packs it inside the setup program. Setup offers it only when the computer
has no tablet driver, and runs XP-Pen's own installer (itself built with
Inno Setup, run with its switches for no questions). Installing a driver
needs an administrator, so Windows asks once; the app itself never does.

To move to a newer driver, change `Driver.psd1` and try it on one
computer first.

---

## The background remover

An ordinary photo is cut out on the computer by MODNet, a portrait-matting
model, run through ONNX Runtime on the processor (no graphics card
needed, about a third of a second). `IdMaker.Vision/Models/README.md`
says what it is, where it comes from and what it is good and bad at.

It replaces the step where HR was told to paste the photo into Gemini or
ChatGPT: an employee's face no longer has to leave the computer to make
their ID. A green-background photo or a cut-out PNG still works exactly
as before, and never touches the model.

If the model file is missing or will not load, the app says so on the
photo step and carries on accepting green-background photos and cut-outs.

---

## Light and dark

The window is drawn in whichever mode Windows is set to for apps
(Settings → Personalisation → Colours → *Choose your app mode*), and
changes with it while it is open — the title bar too — without being
restarted.

- `Colors.Light.xaml` and `Colors.Dark.xaml` define the same `Color.*`
  names. `ThemeManager` swaps one for the other: at start-up from the
  registry value Windows keeps (`AppsUseLightTheme`), and later when
  Windows announces a change (`WM_SETTINGCHANGE "ImmersiveColorSet"`).
- Everything refers to those names with `DynamicResource`, and code with
  `Theme.Paint`, so the swap reaches every control. A `StaticResource`
  would keep the first colour it found; `ThemeTests` fails on one, and on
  any colour name that is not in both files.
- Every control the window uses is drawn by `Theme.xaml`, including lists,
  tick boxes, fold-out sections, the slider, scroll bars, menus and
  tooltips. Left to WPF they would stay light grey in dark mode.
- `ThemeTests` also measures contrast in both palettes: 4.5:1 for text,
  3:1 for control edges, scroll bars and the focus ring.

What does not change, on purpose: the printed card (its colours are the
artwork's), the signing screen (black, like the card), and the signature
pad (white, like paper).

### How the window is meant to look

The rules `Theme.xaml` and `MainWindow.xaml` are held to, so the next
change keeps them:

- **Gold means one thing.** It is spent on the button that moves the job
  forward (`Button.Primary`: Next, Save the PDF) and on the rule over the
  step you are on. Anything else that is chosen, ticked, hovered, open or
  focused uses the text colour (`Color.Strong`, `Color.Focus`) or a tone
  step of the page (`Color.Hover`, `Color.Active`). `ThemeTests` fails if
  gold appears anywhere else.
- **One gold button on screen.** A step's own main action (Choose a
  photo, Sign now) and a tool's (Make the batch, Keep this map) are
  `Button.Strong`, the text colour as a fill, and drop to `Button.Ghost`
  once done.
- **The card is the biggest thing.** No title bar or footer of our own:
  the product's name is at the top of the rail, the version at its foot.
  The card sits on `Color.Stage`, one step from the page, with a hairline
  round it so its edge shows in either mode.
- **Flat.** A 1-pixel line or a tone step separates surfaces. No shadows,
  no gradients, no animation; a press shows as a dip in opacity.
- **One radius family:** panels 8, controls 6, small marks 4.
- **Four type sizes:** 13, 14, 16, 20. Fields are 40 high and 20 apart.
- **In a narrow window** (under 1296 wide) an open tool takes the form's
  place until it is closed (`FitColumns`); the card never gives way.

**Not done:** Windows' *contrast themes* (high contrast). The app keeps
its own colours there rather than taking the contrast theme's.

## Where things are kept

Per user, in `%LOCALAPPDATA%\Alpha Premier\ID Maker\`:

| File | What |
| --- | --- |
| `register.json` | cards issued on this computer |
| `register-outbox.json` | issued cards not yet written to the shared register |
| `register-refused.json` | cards the shared register refused: the number was already somebody else's |
| `register-signin.json` | this computer's sign-in to the shared register, wrapped by Windows (DPAPI); never the password |
| `titles.json` | job titles typed under "Other…" |
| `template.json`, `template-*.png` | a moved map, and own artwork |
| `rotate-back.json`, `duplex-flip.json`, `print-layout.json` | the printer settings |
| `last-error.txt` | the last unexpected error, to send to whoever maintains the app |

**No photograph is ever kept**, and none leaves the computer.

Moving from the web app: its list of issued IDs lived in the browser and
cannot be read from here. Save it there (*Issued IDs → Download register
(CSV)*) and bring it in here with *Issued IDs → Bring in a saved list*.
The shared register is the real record and needs nothing done.

---

## Changing something

Most changes are numbers in `IdMaker.Core/Config.cs`.

| Want to… | Change |
| --- | --- |
| Move or resize the name, title or number | `NameText`, `TitleText`, `EmployeeNoText` in `Config.cs` |
| Add or remove the job titles everyone sees | `CardConfig.Titles` |
| Change where the photo sits or how big the head is | `CardConfig.Photo` |
| Change the QR link | `CardConfig.Qr` |
| Different number format | `EmployeePrefix`, `EmployeeDigits`, `EmployeeLabel` |
| Paper sizes, margins, crop marks | `Papers`, `SheetMarginMm`, `SheetGapMm`, `CropMark*` |
| New card artwork, built in | replace `assets/front-template.png` and `assets/back.png` at the top of the repository (638 × 1004 or 638 × 1011), re-trace `FrontOutline` and `PhotoArea` if the band or the photo window moved |
| Change the app's icon | `IdMaker.App/AppIcon.ico`. The logo it is made from is `assets/id-maker-logo.png`, and the editable original is in Canva ("Logo - Employee IDs", page 4) |
| Restyle the window after a rebrand | `IdMaker.App/Colors.Light.xaml` and `Colors.Dark.xaml`, which must keep the same names. None of them touch the printed card |
| Point at a different register | the `FIREBASE_PROJECT_ID` and `FIREBASE_API_KEY` repository secrets, used by the release build; neither is in the source. See `register/README.md` |
| The version | `CardConfig.AppVersion`, and `Version` in `IdMaker.App.csproj` and `app.manifest` |

After any change that could move something on the card, run all three
commands above; the Windows tests are the ones that notice.

---

## The shared register

Issued IDs are written to a list in the company's Firebase project, the
one APG Keystone uses, and HR signs in to it once on each computer with
their Keystone account. `register/README.md` has all of it: what is kept,
why a number cannot be issued twice, where the rules live, and how the
old Sheet's rows were copied in.

In the code:

- **`Register.cs` talks to it over REST** with `HttpClient`: Firebase
  Auth to sign in and to renew the hour-long pass, Firestore to read the
  list and create documents. No Firebase package: the app reads one list
  and creates documents, and needs neither live updates nor the SDK's
  offline cache.
- **Core does not know Windows.** It asks an `ISecretKeeper` to wrap the
  sign-in; the app's `WindowsKeeper` does it with DPAPI. Without a keeper
  the sign-in lasts for the run and is never written.
- **Which project it is comes from the release build**, as assembly
  metadata read by `CardConfig.Register`. A build without the two
  secrets (a developer's, a pull request's) has no shared register and
  keeps its own list.
- **Nothing is sent until somebody signs in**, so a build machine or a
  test run that starts the app asks the register for nothing.
- **Tests use `FakeFirebase`** (`IdMaker.Tests/Support.cs`): sign-in,
  renewal and the register's list in memory, answering as the real
  services do and holding to the register's rules.

---

## What is still to do

These need a person, a Windows computer and the tablet. None could be
done from the machine this was written on, which is not Windows.

1. **Sign with the Deco Fun.** Check the line thickens with pressure,
   with *Windows Ink* both ticked and unticked in the XP-Pen settings.
2. **Run the setup on an office computer** and let it install the
   driver. CI installs and uninstalls the app itself on every push, but
   always with the driver step left out: the driver's own install has not
   been run end to end.
3. **Print one card at 100%** and measure it against an existing card.
4. **Try real staff photos** through the background remover.
Not carried over from the web app, on purpose:

- **Typing coming back after a reload.** A program does not reload.
- **Reading a colour-coded map picture.** Nothing in the web app used it
  any more either; the map is dragged on the card.

---

## Credits

- **Fonts on the card:** Poppins and Montserrat (SIL Open Font License
  1.1), Arimo (Apache License 2.0). See `IdMaker.App/Fonts/README.md`.
- **QR code:** `IdMaker.Core/Qr.cs` follows qrcode-generator by Kazuhiko
  Arase (MIT License) step for step, and its tables are that library's.
  "QR Code" is a registered trademark of DENSO WAVE INCORPORATED.
- **Background removal:** MODNet by Zhanghan Ke and others (Apache
  License 2.0), run with ONNX Runtime (MIT License).
- **Pen tablet driver:** XP-Pen's own, redistributed unchanged inside the
  installer package. It is not part of this repository.
