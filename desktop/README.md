# Installing the ID Maker on the HR laptop

`install.cmd` here makes the ID Maker a proper installed Windows
application: an entry in the Start menu, an icon you can pin to the
taskbar, a window with no address bar and no tabs, and a line in
**Settings → Apps → Installed apps** with a working Uninstall button.

It needs no administrator rights. Everything it writes lives in the
person's own `%LOCALAPPDATA%` and their own `HKCU` registry branch, which
is why there is no "Windows protected your PC" warning: there is no `.exe`
to sign, so SmartScreen has nothing to object to.

The card itself is drawn by the same HTML in the same Edge engine as
before, so `npm test` still governs what comes out of the printer. Nothing
about the output changes by installing it.

## Making the package

```
node build.mjs            # only if anything in src/ changed
node desktop/pack.mjs
```

That writes `desktop/dist/Alpha Premier ID Maker - install.zip`, about
1 MB, with the version read out of `src/config.js` so what Windows reports
is the version actually installed.

## Installing it

Unzip anywhere and double-click **install.cmd**. It copies the app to
`%LOCALAPPDATA%\AlphaPremierIDMaker`, makes the shortcuts, and registers
the Windows entry.

The app gets **its own Edge profile**, so its settings and its issued-ID
list are not mixed up with anyone's ordinary browsing — and a cleared
browser history cannot wipe the ID register.

The shared Google Sheet is built in, so there is nothing to set up. Read
the next section for why that matters here.

## Updating

Re-run `install.cmd` from a newer zip. It overwrites in place, keeps the
issued-ID list and the settings, and says it is updating rather than
installing.

## The thing to get right on a single laptop

With one machine doing the issuing, **that machine is the only copy of who
has which ID number** — unless the shared Google Sheet is set up. The local
list lives in the app's Edge profile. A reimaged laptop, a wiped profile or
a dead disk takes it with it, and the duplicate check silently starts
again from nothing, which is exactly the failure this app was built to
prevent.

So on a single-laptop setup the shared register is not the optional extra
it is described as in `register/README.md` — it is the backup. Set it up,
and once in a while use **Download register (CSV)** and keep the file
somewhere else as well.

## Removing it

`uninstall.cmd`, or the Uninstall button in Settings → Apps. It takes the
shortcuts and the Windows entry, then asks before deleting the app and its
local list. The Google Sheet is never touched.

---

## If you later want a real `.exe` (`shell/`)

A small WinForms window hosting **WebView2**, Microsoft's embedding of the
Edge engine — about 110 lines in `shell/Program.cs`.

Its reason to exist is not that it looks more official. It serves the app
from a host name (`https://idmaker.alphapremier.local/`) instead of from a
file, so the page has an ordinary web origin: browser storage is reliable
and cross-site requests — the shared Google Sheet among them — behave
exactly as they do on a real website. It also sends any link that is not
the app itself to the normal browser, so the window cannot become a way to
browse the web.

Build it on a Windows machine with the [.NET 8 SDK](https://dotnet.microsoft.com/download):

```
node build.mjs
cd desktop\shell
dotnet publish -c Release -r win-x64
```

The result is in `bin\Release\net8.0-windows\win-x64\publish\` —
`Alpha Premier ID Maker.exe` with `web\index.html` and `app.ico` beside
it. All three belong together; the exe alone will not start.

It is built against the installed .NET 8 runtime rather than carrying its
own, which keeps it small but means the machine running it needs .NET 8.
That is fine on the laptop that built it. To hand it to a machine without
.NET, add `--self-contained true` and accept roughly 150 MB.

Before using it in earnest:

- **The WebView2 Runtime must be present.** It ships with Windows 11 and
  with Edge on Windows 10, so usually it is. When it is not, the app says
  so and points at the download instead of showing a blank window.
- **An unsigned exe triggers "Windows protected your PC"** on first run. A
  signing certificate is roughly $200–400 a year. `install.cmd` has no such
  problem, which is why it is the route described above.
- The version in `AlphaPremierIDMaker.csproj` has to be kept in step with
  `APP_VERSION` in `src/config.js` by hand.

## Do not publish the repository

Only ever hand out the built HTML. `assets/back.png` carries the CEO's
signature and `test/golden-front.png` is a complete sample card; serving
the folder would give both to anyone who asked.
