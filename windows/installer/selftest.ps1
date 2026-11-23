<#
.SYNOPSIS
    Proves the setup program installs the app the way Windows expects, and
    that it uninstalls cleanly. Run after package.ps1; CI runs it on every change.

.DESCRIPTION
    Runs AlphaPremierIdMaker-Setup.exe without questions and without the
    driver step (a build machine has no tablet and nobody to answer the
    Windows prompt), then checks what a person would see afterwards:

      - the program is where Windows puts per-user apps
      - a Start-menu shortcut that points at it
      - an entry under Settings -> Apps -> Installed apps, with the right
        name, version, publisher and an uninstaller
      - the app starts and shows its window

    Then it runs that uninstaller and checks all of it is gone again.
#>
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Here  = Split-Path -Parent $MyInvocation.MyCommand.Path
$Setup = Join-Path (Split-Path -Parent $Here) 'dist\AlphaPremierIdMaker-Setup.exe'
if (-not (Test-Path $Setup)) { throw "No setup program at $Setup. Run package.ps1 first." }

$Name     = 'Alpha Premier ID Maker'
$Folder   = Join-Path $env:LOCALAPPDATA "Programs\$Name"
$Exe      = Join-Path $Folder 'AlphaPremierIdMaker.exe'
$Shortcut = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\$Name.lnk"
$Data     = Join-Path $env:LOCALAPPDATA 'Alpha Premier\ID Maker'

function Get-AppsEntry {
    Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
        Where-Object { $_.PSObject.Properties['DisplayName'] -and $_.DisplayName -eq $Name }
}

$log = Join-Path $env:TEMP "apg-id-maker-setup-$PID.log"
$install = Start-Process -FilePath $Setup -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/TASKS=""', "/LOG=`"$log`"" -Wait -PassThru
if ($install.ExitCode -ne 0) { if (Test-Path $log) { Get-Content $log -Tail 40 }; throw "Setup stopped with code $($install.ExitCode)." }

if (-not (Test-Path $Exe)) { throw "Setup did not put the app at $Exe." }
if (-not (Test-Path $Shortcut)) { throw 'No Start-menu shortcut was made.' }
$target = (New-Object -ComObject WScript.Shell).CreateShortcut($Shortcut).TargetPath
if ($target -ne $Exe) { throw "The Start-menu shortcut points at '$target', not the installed app." }

$entry = @(Get-AppsEntry)
if ($entry.Count -ne 1) { throw "Expected one '$Name' entry under Installed apps, found $($entry.Count)." }
$entry = $entry[0]
[xml]$project = Get-Content (Join-Path (Split-Path -Parent $Here) 'IdMaker.App\IdMaker.App.csproj')
$version = ($project.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if ($entry.DisplayVersion -ne $version) { throw "Installed apps shows version '$($entry.DisplayVersion)', expected '$version'." }
if ($entry.Publisher -ne 'Alpha Premier Group of Companies') { throw "Installed apps shows publisher '$($entry.Publisher)'." }
if (-not $entry.UninstallString) { throw 'The Installed apps entry has no uninstaller.' }
"Installed apps: $($entry.DisplayName) $($entry.DisplayVersion), $($entry.Publisher)"

# Installing again over the top is an upgrade, not a second copy.
$again = Start-Process -FilePath $Setup -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/TASKS=""' -Wait -PassThru
if ($again.ExitCode -ne 0) { throw "Installing over the top stopped with code $($again.ExitCode)." }
if (@(Get-AppsEntry).Count -ne 1) { throw 'Installing over the top left two entries under Installed apps.' }

# The app asks the shared register for nothing until somebody has signed in
# on the computer, and nobody signs in on a build machine, so starting it
# here sends nothing anywhere. One file of the person's own is put down, to
# check further on that uninstalling leaves it alone.
New-Item -ItemType Directory -Force -Path $Data | Out-Null
Set-Content -Path (Join-Path $Data 'titles.json') -Value '["Tester"]' -NoNewline -Encoding utf8

$app = Start-Process -FilePath $Exe -PassThru
$title = ''
foreach ($i in 1..40) {
    Start-Sleep -Milliseconds 500
    $app.Refresh()
    if ($app.HasExited) { throw "The app closed by itself straight after starting (exit code $($app.ExitCode))." }
    if ($app.MainWindowTitle) { $title = $app.MainWindowTitle; break }
}
if ($title -ne $Name) { $app | Stop-Process -Force; throw "The app's window did not open (title: '$title')." }
"The installed app started and showed its window: $title"
Start-Sleep -Seconds 3
$app.Refresh()
if ($app.HasExited) { throw 'The app closed by itself a few seconds after starting.' }
$err = Join-Path $Data 'last-error.txt'
if (Test-Path $err) { Get-Content $err; $app | Stop-Process -Force; throw 'The app reported an error while starting.' }
$app | Stop-Process -Force
$app.WaitForExit(5000) | Out-Null

# Uninstall the way Settings -> Apps does: with the entry's own uninstaller.
$uninstaller = $entry.UninstallString.Trim('"')
$remove = Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait -PassThru
if ($remove.ExitCode -ne 0) { throw "The uninstaller stopped with code $($remove.ExitCode)." }
foreach ($i in 1..20) { if (-not (Test-Path $Exe)) { break }; Start-Sleep -Milliseconds 500 }   # it finishes from a copy of itself
if (Test-Path $Exe) { throw 'The uninstaller left the app behind.' }
if (Test-Path $Shortcut) { throw 'The uninstaller left the Start-menu shortcut behind.' }
if (@(Get-AppsEntry).Count -ne 0) { throw 'The uninstaller left the Installed apps entry behind.' }
# what the person made with the app is theirs: uninstalling does not take it
if (-not (Test-Path (Join-Path $Data 'titles.json'))) { throw "The uninstaller removed the user's own data, which it must leave." }
Remove-Item $Data -Recurse -Force
'Installed, upgraded in place, started, and uninstalled cleanly.'
