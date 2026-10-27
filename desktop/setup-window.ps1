<#
  Alpha Premier ID Maker — Setup

  One window for the three things anyone ever does to this app: install or
  update it, rebuild it after a change, and remove it. It is a front for
  install.cmd, rebuild.cmd and uninstall.cmd rather than a reimplementation
  of them, so there is one copy of each job and the console window shows
  its real output.

  Launched by Setup.cmd, which supplies the execution policy.
#>

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$here    = $PSScriptRoot
$root    = Split-Path $here -Parent
$target  = Join-Path $env:LOCALAPPDATA 'AlphaPremierIDMaker'
$appName = 'Alpha Premier ID Maker'

# brand: black panel, gold accents, near-black text on gold
$gold   = [System.Drawing.Color]::FromArgb(212, 175, 55)
$ink    = [System.Drawing.Color]::FromArgb(29, 28, 26)
$paper  = [System.Drawing.Color]::FromArgb(245, 245, 242)
$muted  = [System.Drawing.Color]::FromArgb(102, 98, 90)

function Get-InstalledVersion {
  if (-not (Test-Path (Join-Path $target 'index.html'))) { return $null }
  $v = Join-Path $target 'version.txt'
  if (Test-Path $v) { return (Get-Content $v -Raw).Trim() }
  return 'unknown'
}

function Get-AvailableVersion {
  $v = Join-Path $here 'version.txt'
  if (Test-Path $v) { return (Get-Content $v -Raw).Trim() }
  $cfg = Join-Path $root 'src\config.js'
  if (Test-Path $cfg) {
    $m = Select-String -Path $cfg -Pattern "APP_VERSION\s*=\s*'([^']+)'" | Select-Object -First 1
    if ($m) { return $m.Matches[0].Groups[1].Value }
  }
  return $null
}

function Find-Edge {
  $candidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'),
    (Join-Path $env:ProgramFiles          'Microsoft\Edge\Application\msedge.exe'),
    (Join-Path $env:LOCALAPPDATA          'Microsoft\Edge\Application\msedge.exe')
  )
  foreach ($c in $candidates) { if ($c -and (Test-Path $c)) { return $c } }
  foreach ($hive in 'HKLM:', 'HKCU:') {
    $k = "$hive\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe"
    try {
      $p = (Get-ItemProperty -Path $k -ErrorAction Stop).'(default)'
      if ($p -and (Test-Path $p)) { return $p }
    } catch { }
  }
  return $null
}

function Have([string]$name) { return [bool](Get-Command $name -ErrorAction SilentlyContinue) }

# ---- the window -------------------------------------------------------

$form                 = New-Object System.Windows.Forms.Form
$form.Text            = "$appName — Setup"
$form.ClientSize      = New-Object System.Drawing.Size(520, 430)
$form.FormBorderStyle = 'FixedSingle'
$form.MaximizeBox     = $false
$form.StartPosition   = 'CenterScreen'
$form.BackColor       = $paper
$form.Font            = New-Object System.Drawing.Font('Segoe UI', 9)
$icoPath = Join-Path $here 'app.ico'
if (Test-Path $icoPath) { try { $form.Icon = New-Object System.Drawing.Icon($icoPath) } catch { } }

$header           = New-Object System.Windows.Forms.Panel
$header.Size      = New-Object System.Drawing.Size(520, 72)
$header.Location  = New-Object System.Drawing.Point(0, 0)
$header.BackColor = [System.Drawing.Color]::Black
$form.Controls.Add($header)

if (Test-Path $icoPath) {
  $logo          = New-Object System.Windows.Forms.PictureBox
  $logo.Size     = New-Object System.Drawing.Size(40, 40)
  $logo.Location = New-Object System.Drawing.Point(20, 16)
  $logo.SizeMode = 'Zoom'
  try { $logo.Image = ([System.Drawing.Icon]::new($icoPath)).ToBitmap() } catch { }
  $header.Controls.Add($logo)
}

$title           = New-Object System.Windows.Forms.Label
$title.Text      = $appName
$title.ForeColor = $gold
$title.Font      = New-Object System.Drawing.Font('Segoe UI', 13, [System.Drawing.FontStyle]::Bold)
$title.Location  = New-Object System.Drawing.Point(72, 14)
$title.Size      = New-Object System.Drawing.Size(420, 26)
$header.Controls.Add($title)

$subtitle           = New-Object System.Windows.Forms.Label
$subtitle.Text      = 'Install, rebuild or remove it on this computer'
$subtitle.ForeColor = [System.Drawing.Color]::FromArgb(224, 224, 224)
$subtitle.Location  = New-Object System.Drawing.Point(72, 40)
$subtitle.Size      = New-Object System.Drawing.Size(430, 20)
$header.Controls.Add($subtitle)

$status            = New-Object System.Windows.Forms.Label
$status.Location   = New-Object System.Drawing.Point(20, 88)
$status.Size       = New-Object System.Drawing.Size(480, 96)
$status.ForeColor  = $ink
$form.Controls.Add($status)

$hint              = New-Object System.Windows.Forms.Label
$hint.Location     = New-Object System.Drawing.Point(20, 188)
$hint.Size         = New-Object System.Drawing.Size(480, 40)
$hint.ForeColor    = $muted
$form.Controls.Add($hint)

function New-Button([string]$text, [int]$top, [bool]$primary) {
  $b           = New-Object System.Windows.Forms.Button
  $b.Text      = $text
  $b.Location  = New-Object System.Drawing.Point(20, $top)
  $b.Size      = New-Object System.Drawing.Size(480, 40)
  $b.FlatStyle = 'Flat'
  $b.Font      = New-Object System.Drawing.Font('Segoe UI', 10, [System.Drawing.FontStyle]::Bold)
  if ($primary) {
    $b.BackColor = $gold
    $b.ForeColor = $ink
    $b.FlatAppearance.BorderSize = 0
  } else {
    $b.BackColor = [System.Drawing.Color]::White
    $b.ForeColor = $ink
    $b.FlatAppearance.BorderColor = [System.Drawing.Color]::FromArgb(226, 223, 214)
  }
  $form.Controls.Add($b)
  return $b
}

$btnInstall   = New-Button 'Install'                 236 $true
$btnOpen      = New-Button 'Open the app'            284 $false
$btnRebuild   = New-Button 'Rebuild from source'     332 $false
$btnUninstall = New-Button 'Remove it'               380 $false

# ---- running the real scripts ----------------------------------------

function Invoke-Step([string]$script, [string]$busy) {
  $path = Join-Path $here $script
  if (-not (Test-Path $path)) {
    [System.Windows.Forms.MessageBox]::Show("$script is missing from:`n$here", $appName) | Out-Null
    return
  }
  foreach ($b in @($btnInstall, $btnOpen, $btnRebuild, $btnUninstall)) { $b.Enabled = $false }
  $hint.Text = "$busy  A console window is doing the work; it will close when it is finished."
  [System.Windows.Forms.Application]::DoEvents()
  try {
    Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', "`"$path`"" -WorkingDirectory $here -Wait
  } catch {
    [System.Windows.Forms.MessageBox]::Show("Could not run $script`n`n$($_.Exception.Message)", $appName) | Out-Null
  }
  Refresh-Status
}

function Refresh-Status {
  $installed = Get-InstalledVersion
  $available = Get-AvailableVersion
  $edge      = Find-Edge
  $hasNode   = Have 'node'
  $hasDotnet = (Have 'dotnet') -or (Test-Path (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'))

  $lines = @()
  if ($installed) { $lines += "Installed on this computer:  $installed" }
  else            { $lines += 'Installed on this computer:  not yet' }
  if ($available) { $lines += "Available in this folder:    $available" }
  else            { $lines += 'Available in this folder:    no app found' }
  if ($edge)      { $lines += 'Microsoft Edge:              found' }
  else            { $lines += 'Microsoft Edge:              NOT FOUND - the app needs it' }
  $lines += ("Build tools:                 node " + $(if ($hasNode) { 'yes' } else { 'no' }) +
             ",  .NET SDK " + $(if ($hasDotnet) { 'yes' } else { 'no' }))
  $status.Text = ($lines -join "`r`n")

  $btnInstall.Enabled   = [bool]$available -and [bool]$edge
  $btnOpen.Enabled      = [bool]$installed
  $btnUninstall.Enabled = [bool]$installed
  $btnRebuild.Enabled   = $hasNode -and (Test-Path (Join-Path $root 'build.mjs'))

  if (-not $installed) {
    $btnInstall.Text = 'Install'
    $hint.Text = 'Nothing is installed yet. Install puts it in the Start menu and in Settings > Apps.'
  } elseif ($available -and $available -ne $installed) {
    $btnInstall.Text = "Update to $available"
    $hint.Text = "Version $available is ready to replace $installed. Your issued-ID list and settings are kept."
  } else {
    $btnInstall.Text = 'Reinstall'
    $hint.Text = 'Up to date. Rebuild from source after changing anything in src\, then install again.'
  }
  if (-not $hasNode) { $btnRebuild.Text = 'Rebuild from source (needs Node)' }
}

$btnInstall.Add_Click({   Invoke-Step 'install.cmd'   'Installing...' })
$btnRebuild.Add_Click({   Invoke-Step 'rebuild.cmd'   'Rebuilding...' })
$btnUninstall.Add_Click({ Invoke-Step 'uninstall.cmd' 'Removing...' })
$btnOpen.Add_Click({
  $lnk = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\$appName.lnk"
  if (Test-Path $lnk) { Start-Process $lnk } else { Start-Process (Join-Path $target 'index.html') }
})

Refresh-Status
[void]$form.ShowDialog()
