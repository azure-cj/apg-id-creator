# Alpha Premier ID Maker for Mac

The same app as the Windows one, as a native Mac program: C# on Apple's
AppKit, drawn with Core Graphics and Core Text. No web view. It shares
every rule with the Windows app through `windows/IdMaker.Core`, so a card,
a number and the register behave the same on both; the card it prints is
held to the same picture (`test/golden-front.png`).

## What is where

```
mac/
  IdMaker.Mac/
    Main.cs               start, and the flags below
    AppDelegate.cs        opens the window
    MainWindow.cs         the five steps, the preview, Issued IDs, the printer setting
    MainWindow.Tools.cs   batch from a list, card design, moving the photo, the pen driver
    CardViews.cs          what takes the mouse on the card: photo drag, the design's boxes, file drop
    SignWindow.cs         the full-screen signing pad (pen pressure from tablet events)
    MacCard.cs            the card, the sheet and the PDF pages, drawn with Core Graphics
    MacText.cs            measuring and outlining the card's text with Core Text
    MacKeeper.cs          the register sign-in, wrapped with a key in the Keychain
    PenDriver.cs          XP-Pen's Mac driver: is one installed, fetch, check, open the installer
    SelfTest.cs           checks that need a Mac and no window (--selftest)
    Tests/WindowTest.cs   the window tests (--windowtest), compiled only with -p:WindowTest=true
  package.sh              builds one disk image, opens it and starts the app inside
  make-icon.sh            the .icns from assets/id-maker-logo.png
  workloads.json          the Mac workload, pinned to the one that builds with the runner's Xcode
  global.json             the .NET SDK band that workload belongs to
```

`IdMaker.Core` decides and this shows, as on Windows. A rule about the
card or the form goes in Core with a test there.

## How it matches the Windows app

| Windows | Mac |
| --- | --- |
| WPF window, XAML | AppKit controls built in code, stack views |
| DPAPI keeps the sign-in | a key in the user's Keychain (AES-GCM) |
| WIC opens pictures | Image I/O, turned the way the phone held it |
| WPF draws the card | Core Graphics and Core Text, same geometry from Core |
| WinTab pen pressure | pressure on AppKit's tablet events |
| Setup program offers the XP-Pen driver | the app offers it on first start, and on the signature step |
| Setup program, Start menu | a disk image; drag the app to Applications |
| Job-title chips | the same titles in a menu |

## Building and checking

A Mac with Xcode and the .NET 10 SDK:

```bash
cd mac && dotnet workload install macos --from-rollback-file workloads.json
```

```bash
cd mac && dotnet build IdMaker.Mac -c Release -r osx-arm64 -p:WindowTest=true
```

```bash
app=$(find mac/IdMaker.Mac/bin/Release -maxdepth 4 -path '*osx-arm64*' -name '*.app' | head -1); "$app/Contents/MacOS/AlphaPremierIdMaker" --selftest --repo "$PWD" && "$app/Contents/MacOS/AlphaPremierIdMaker" --windowtest --repo "$PWD"
```

```bash
mac/package.sh osx-arm64 AppleSilicon
```

`.github/workflows/mac.yml` does all of this on every change, on a real
Mac. The window tests drive the real window against the pretend register
(`windows/IdMaker.Tests/FakeFirebase.cs`); they also download XP-Pen's
driver and check it is the pinned file, without opening it.

## Not signed

There is no Apple developer account, so the app is signed ad hoc only.
The first time, macOS says it cannot check the app: open System Settings,
Privacy & Security, and choose Open Anyway. A Developer ID and
notarisation would remove that step; nothing else in the app depends on it.

## The pen tablet driver

`PenDriver.cs` names one version of XP-Pen's driver, its address on
XP-Pen's server and its SHA-256. The app downloads it, refuses anything
that is not that exact file, and opens the package in Apple's installer.
It cannot be silent: the installer asks for the Mac's administrator
password, and macOS then asks the person to allow the driver under Input
Monitoring and Accessibility. No app can answer those.
