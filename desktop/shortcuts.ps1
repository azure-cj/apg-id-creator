# Shortcuts, and the entry that makes Windows consider this an installed
# application. Called by install.cmd (not by the setup window, which only
# runs install.cmd itself), which passes everything through the
# environment so neither batch nor PowerShell has to escape the other's
# quotes.
$ErrorActionPreference = 'Stop'

$name    = $env:APP_NAME
$target  = $env:APP_TARGET
$edge    = $env:APP_EDGE
$version = $env:APP_VERSION

# let .NET build the file: URI, so a user name with a space in it
# ("C:\Users\Maria Clara\...") is encoded properly rather than cutting
# the address in half
$url  = ([System.Uri](Join-Path $target 'index.html')).AbsoluteUri
$prof = Join-Path $target 'profile'
$icon = Join-Path $target 'app.ico'

# --app gives a window with no address bar; its own user-data-dir keeps the
# app's settings away from the person's ordinary browsing, and vice versa.
# Both paths are quoted because a user name may contain a space.
$arguments = '--app="{0}" --user-data-dir="{1}" --no-first-run --no-default-browser-check --window-size=1280,900' -f $url, $prof

function New-AppShortcut([string]$path) {
  $sc = (New-Object -ComObject WScript.Shell).CreateShortcut($path)
  $sc.TargetPath       = $edge
  $sc.Arguments        = $arguments
  $sc.IconLocation     = $icon
  $sc.WorkingDirectory = $target
  $sc.Description      = 'Makes the Alpha Premier employee ID card'
  $sc.Save()
}

$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
New-AppShortcut (Join-Path $startMenu "$name.lnk")

try { New-AppShortcut (Join-Path ([Environment]::GetFolderPath('Desktop')) "$name.lnk") }
catch { Write-Host '   (no desktop shortcut - the Start menu one is there)' }

# Settings -> Apps -> Installed apps. Under HKCU, so this needs no
# administrator rights; it is what turns a folder of files into something
# Windows will list and offer to uninstall, like any other program.
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AlphaPremierIDMaker'
New-Item -Path $key -Force | Out-Null
$size = [int]((Get-ChildItem $target -Recurse -File -ErrorAction SilentlyContinue |
                Measure-Object Length -Sum).Sum / 1KB)
@{
  DisplayName     = $name
  DisplayVersion  = $version
  Publisher       = 'Alpha Premier Group of Companies'
  DisplayIcon     = $icon
  InstallLocation = $target
  UninstallString = ('"{0}"' -f (Join-Path $target 'uninstall.cmd'))
  NoModify        = 1
  NoRepair        = 1
  EstimatedSize   = $size
}.GetEnumerator() | ForEach-Object {
  $type = if ($_.Value -is [int]) { 'DWord' } else { 'String' }
  New-ItemProperty -Path $key -Name $_.Key -Value $_.Value -PropertyType $type -Force | Out-Null
}
