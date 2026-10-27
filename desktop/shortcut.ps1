# Creates the Start-menu and desktop shortcuts. Called by install.cmd,
# which passes everything through the environment so that neither batch
# nor PowerShell has to escape the other's quotes.
$ErrorActionPreference = 'Stop'

$name   = $env:APP_NAME
$target = $env:APP_TARGET
$edge   = $env:APP_EDGE

# let .NET build the file: URI, so a user name with a space in it
# ("C:\Users\Maria Clara\...") is encoded properly rather than cutting
# the address in half
$url  = ([System.Uri](Join-Path $target 'index.html')).AbsoluteUri
$prof = Join-Path $target 'profile'

# --app gives a window with no address bar; its own user-data-dir keeps the
# app's settings away from the person's ordinary browsing, and vice versa.
# Both paths are quoted here because a user name may contain a space.
$arguments = '--app="{0}" --user-data-dir="{1}" --no-first-run --no-default-browser-check --window-size=1280,900' -f $url, $prof

function New-AppShortcut([string]$path) {
  $sc = (New-Object -ComObject WScript.Shell).CreateShortcut($path)
  $sc.TargetPath       = $edge
  $sc.Arguments        = $arguments
  $sc.IconLocation     = (Join-Path $target 'app.ico')
  $sc.WorkingDirectory = $target
  $sc.Description      = 'Makes the Alpha Premier employee ID card'
  $sc.Save()
}

$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
New-AppShortcut (Join-Path $startMenu "$name.lnk")

try { New-AppShortcut (Join-Path ([Environment]::GetFolderPath('Desktop')) "$name.lnk") }
catch { Write-Host '   (could not put a shortcut on the desktop - the Start menu one is there)' }
