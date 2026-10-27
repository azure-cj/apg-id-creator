# Giving HR the app to install

Two routes. The first needs nothing installed on anyone's machine and is
what the `install.cmd` in this folder does. The second produces a real
`.exe` and needs a build on a Windows machine once.

Both end up looking the same to the person using it: an entry in the Start
menu, an icon they can pin to the taskbar, and a window with no address
bar. Neither changes a pixel of the printed card — the drawing is the same
HTML in the same Edge engine either way, so `npm test` still governs the
output.

---

## Route 1 — the installer in this folder

**Send people a zip containing these four files:**

```
Alpha Premier ID Maker.html     the app itself
install.cmd                     what they double-click
shortcut.ps1                    used by install.cmd
app.ico                         the icon
```

They unzip it anywhere and double-click **install.cmd**. It:

- copies the app to `%LOCALAPPDATA%\AlphaPremierIDMaker`,
- makes a Start-menu shortcut and a desktop shortcut that open it in
  Microsoft Edge **in app mode** — its own window, its own icon, no address
  bar and no tabs,
- gives the app its own browser profile, so its settings and issued-ID list
  are not mixed up with that person's ordinary browsing.

No administrator rights, nothing in Program Files, nothing in the registry,
and nothing for SmartScreen to warn about, because there is no `.exe` to
sign. `uninstall.cmd` removes all of it and asks before deleting that
computer's issued-ID list.

Requires Microsoft Edge, which is on every supported Windows. The installer
checks and says so if it is missing.

**Each person still pastes the shared-register address once** — Issued IDs
→ Shared register. The installer says so at the end. Without it, that
computer checks only its own list of ID numbers.

### Updating everyone

Send the new zip and have them run `install.cmd` again; it overwrites in
place and keeps their settings and their issued-ID list. This is the part
that does not scale — see "Worth knowing" below.

---

## Route 2 — the real `.exe` (`shell/`)

A small WinForms window hosting **WebView2**, Microsoft's embedding of the
Edge engine. About 110 lines. It exists for one technical reason beyond
looking the part: it serves the app from a made-up host name
(`https://idmaker.alphapremier.local/`) instead of from a file, so the page
has an ordinary origin. Browser storage is then reliable, and cross-site
requests — the shared Google Sheet register among them — behave exactly as
they do on a real website.

It also sends any link that is not the app itself to the person's normal
browser, so the window can never turn into a way to browse the web.

**Build it on a Windows machine with the [.NET 8 SDK](https://dotnet.microsoft.com/download):**

```
cd desktop\shell
dotnet publish -c Release -r win-x64
```

The result lands in `bin\Release\net8.0-windows\win-x64\publish\`. Copy
`app.ico` in beside the exe. Run `node build.mjs` in the project root first
if you have changed anything in `src/`, since the project copies the built
HTML.

**Before handing that exe to anyone, read these:**

- **The WebView2 Runtime must be present.** It ships with Windows 11 and
  with Edge on Windows 10, so usually it is. When it is not, the app says
  so and points at the download rather than showing a blank window.
- **An unsigned exe triggers "Windows protected your PC".** Staff will
  either be alarmed or learn to click through security warnings. A signing
  certificate is roughly $200–400 a year. Route 1 has no such problem.
- **You now have a build step** — the .NET SDK, and a version number in
  `AlphaPremierIDMaker.csproj` that has to be kept in step with
  `APP_VERSION` in `src/config.js`.
- Making a proper installer for it needs something like Inno Setup or WiX
  on top.

---

## Worth knowing, whichever route

**Neither one solves updates.** Both mean sending a file to every computer
and trusting that nobody keeps an old copy — and with a tool that issues
identity cards, somebody quietly running last month's version is a real
problem, not an inconvenience.

The thing that does solve it is hosting the page privately — Cloudflare
Pages behind Cloudflare Access, locked to the HR email addresses — and
having people install it from Edge with *Apps → Install this site as an
app*. Same Start-menu entry, same icon, same windowless frame, no installer
and no certificate, and everyone is on the current version the moment it is
published. The cost is that it needs the network, which the shared register
needs anyway.

Keeping `Alpha Premier ID Maker.html` on each machine as an offline standby
covers the day the internet is out.

**Do not publish the repository, only the built HTML.** `assets/back.png`
carries the CEO's signature and `test/golden-front.png` is a complete
sample card. Serving the folder would hand both to anyone who asked.
