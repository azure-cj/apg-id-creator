<#
  Alpha Premier ID Maker — Setup

  One window for the three things anyone ever does to this app: install or
  update it, rebuild it after a change, and remove it. It is a front for
  install.cmd, rebuild.cmd and uninstall.cmd rather than a reimplementation
  of them, so there is one copy of each job and the console window shows
  its real output.

  Dressed to match the app: black header, the peacock mark, brand gold
  #D4AF37 on the one button that matters, and the quieter actions kept
  quiet. Launched by Setup.cmd, which supplies the execution policy.
#>

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$here    = $PSScriptRoot
$root    = Split-Path $here -Parent
$target  = Join-Path $env:LOCALAPPDATA 'AlphaPremierIDMaker'
$appName = 'Alpha Premier ID Maker'

$C = @{
  gold   = [System.Drawing.Color]::FromArgb(212, 175, 55)
  ink    = [System.Drawing.Color]::FromArgb( 29,  28, 26)
  paper  = [System.Drawing.Color]::FromArgb(245, 245, 242)
  card   = [System.Drawing.Color]::White
  line   = [System.Drawing.Color]::FromArgb(226, 223, 214)
  muted  = [System.Drawing.Color]::FromArgb(102,  98, 90)
  dim    = [System.Drawing.Color]::FromArgb(224, 224, 224)
  good   = [System.Drawing.Color]::FromArgb( 60, 122, 75)
  bad    = [System.Drawing.Color]::FromArgb(179,  38, 30)
}
# Call this parenthesised -- (Font 9) -- wherever the result is assigned:
# the right-hand side of an assignment is parsed as an expression, where a
# bare command name is a parse error, and a parse error stops the whole
# file running rather than the one line.
function Font([single]$size, [string]$style = 'Regular') {
  New-Object System.Drawing.Font('Segoe UI', $size, [System.Drawing.FontStyle]$style)
}

# ---- what is true right now -------------------------------------------

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
  foreach ($c in @(
      (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'),
      (Join-Path $env:ProgramFiles          'Microsoft\Edge\Application\msedge.exe'),
      (Join-Path $env:LOCALAPPDATA          'Microsoft\Edge\Application\msedge.exe'))) {
    if ($c -and (Test-Path $c)) { return $c }
  }
  foreach ($hive in 'HKLM:', 'HKCU:') {
    try {
      $p = (Get-ItemProperty -Path "$hive\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe" -ErrorAction Stop).'(default)'
      if ($p -and (Test-Path $p)) { return $p }
    } catch { }
  }
  return $null
}

function Have([string]$name) { return [bool](Get-Command $name -ErrorAction SilentlyContinue) }

# ---- the window -------------------------------------------------------

$form                 = New-Object System.Windows.Forms.Form
$form.Text            = "$appName — Setup"
$form.ClientSize      = New-Object System.Drawing.Size(560, 464)
$form.FormBorderStyle = 'FixedSingle'
$form.MaximizeBox     = $false
$form.StartPosition   = 'CenterScreen'
$form.BackColor       = $C.paper
$form.Font            = (Font 9)
$icoPath = Join-Path $here 'app.ico'
if (Test-Path $icoPath) { try { $form.Icon = New-Object System.Drawing.Icon($icoPath) } catch { } }

function Add-Label($parent, $text, $x, $y, $w, $h, $font, $colour, $align = 'TopLeft') {
  $l           = New-Object System.Windows.Forms.Label
  $l.Text      = $text
  $l.Location  = New-Object System.Drawing.Point($x, $y)
  $l.Size      = New-Object System.Drawing.Size($w, $h)
  $l.Font      = $font
  $l.ForeColor = $colour
  $l.TextAlign = $align
  $l.BackColor = [System.Drawing.Color]::Transparent
  $parent.Controls.Add($l)
  return $l
}

# --- header: the app's own masthead, black with the mark on it ---
$header           = New-Object System.Windows.Forms.Panel
$header.Size      = New-Object System.Drawing.Size(560, 92)
$header.Location  = New-Object System.Drawing.Point(0, 0)
$header.BackColor = [System.Drawing.Color]::Black
$form.Controls.Add($header)

if (Test-Path $icoPath) {
  $logo          = New-Object System.Windows.Forms.PictureBox
  $logo.Size     = New-Object System.Drawing.Size(44, 44)
  $logo.Location = New-Object System.Drawing.Point(24, 24)
  $logo.SizeMode = 'Zoom'
  $logo.BackColor = [System.Drawing.Color]::Transparent
  try { $logo.Image = (New-Object System.Drawing.Icon($icoPath, 64, 64)).ToBitmap() } catch { }
  $header.Controls.Add($logo)
}
Add-Label $header $appName            84 22 340 28 (Font 14 'Bold') $C.gold | Out-Null
Add-Label $header 'Setup for this computer' 84 52 340 20 (Font 9) $C.dim  | Out-Null
$lblVersion = Add-Label $header '' 400 30 136 34 (Font 9) $C.dim 'MiddleRight'

# a gold rule, as on the card itself
$rule           = New-Object System.Windows.Forms.Panel
$rule.Size      = New-Object System.Drawing.Size(560, 3)
$rule.Location  = New-Object System.Drawing.Point(0, 92)
$rule.BackColor = $C.gold
$form.Controls.Add($rule)

# --- what it found ---
$card           = New-Object System.Windows.Forms.Panel
$card.Size      = New-Object System.Drawing.Size(512, 126)
$card.Location  = New-Object System.Drawing.Point(24, 120)
$card.BackColor = $C.card
$card.Add_Paint({
  param($sender, $e)
  $pen = New-Object System.Drawing.Pen($script:C.line)
  $e.Graphics.DrawRectangle($pen, 0, 0, $sender.Width - 1, $sender.Height - 1)
  $pen.Dispose()
})
$form.Controls.Add($card)

$rows = @{}
$names = @('installed', 'available', 'edge', 'tools')
$labels = @('Installed here', 'Available in this folder', 'Microsoft Edge', 'Build tools')
for ($i = 0; $i -lt 4; $i++) {
  $y = 16 + $i * 24
  $rows[$names[$i]] = @{
    mark  = (Add-Label $card '' 16 $y 18 20 (Font 10 'Bold') $C.muted)
    value = (Add-Label $card '' 210 $y 286 20 (Font 9 'Bold') $C.ink)
  }
  Add-Label $card $labels[$i] 38 $y 170 20 (Font 9) $C.muted | Out-Null
}

$hint = Add-Label $form '' 24 256 512 40 (Font 9) $C.muted

# --- the one that matters, then the quiet ones ---
function New-Button($text, $x, $y, $w, $h, $primary) {
  $b                            = New-Object System.Windows.Forms.Button
  $b.Text                       = $text
  $b.Location                   = New-Object System.Drawing.Point($x, $y)
  $b.Size                       = New-Object System.Drawing.Size($w, $h)
  $b.FlatStyle                  = 'Flat'
  $b.Cursor                     = [System.Windows.Forms.Cursors]::Hand
  $b.UseVisualStyleBackColor    = $false
  if ($primary) {
    $b.Font      = (Font 11 'Bold')
    $b.BackColor = $C.gold
    $b.ForeColor = $C.ink
    $b.FlatAppearance.BorderSize = 0
    $b.FlatAppearance.MouseOverBackColor = [System.Drawing.Color]::FromArgb(224, 190, 78)
    $b.FlatAppearance.MouseDownBackColor = [System.Drawing.Color]::FromArgb(190, 154, 42)
  } else {
    $b.Font      = (Font 9)
    $b.BackColor = $C.card
    $b.ForeColor = $C.ink
    $b.FlatAppearance.BorderColor = $C.line
    $b.FlatAppearance.MouseOverBackColor = [System.Drawing.Color]::FromArgb(246, 240, 224)
  }
  $form.Controls.Add($b)
  return $b
}

$btnInstall   = New-Button 'Install'            24 302 512 52 $true
$btnOpen      = New-Button 'Open the app'       24 366 164 40 $false
$btnRebuild   = New-Button 'Rebuild'           198 366 164 40 $false
$btnUninstall = New-Button 'Remove'            372 366 164 40 $false

Add-Label $form 'Alpha Premier Group of Companies' 24 424 330 20 (Font 8) $C.muted | Out-Null
$lblWhere = Add-Label $form '' 354 424 182 20 (Font 8) $C.muted 'TopRight'

# ---- doing the work ---------------------------------------------------

function Set-Row($key, $text, $state) {
  $r = $rows[$key]
  $r.value.Text = $text
  switch ($state) {
    'good' { $r.mark.Text = [char]0x2713; $r.mark.ForeColor = $C.good }   # check
    'bad'  { $r.mark.Text = [char]0x2715; $r.mark.ForeColor = $C.bad  }   # cross
    default { $r.mark.Text = [char]0x2022; $r.mark.ForeColor = $C.muted } # bullet
  }
}

function Refresh-Status {
  $installed = Get-InstalledVersion
  $available = Get-AvailableVersion
  $edge      = Find-Edge
  $hasNode   = Have 'node'
  $hasDotnet = (Have 'dotnet') -or (Test-Path (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'))

  if ($installed) { Set-Row 'installed' $installed 'good' } else { Set-Row 'installed' 'not yet' 'none' }
  if ($available) { Set-Row 'available' $available 'good' } else { Set-Row 'available' 'no app found here' 'bad' }
  if ($edge)      { Set-Row 'edge' 'found' 'good' }        else { Set-Row 'edge' 'not found' 'bad' }
  $toolText = ('node ' + $(if ($hasNode) { 'yes' } else { 'no' }) + '   .NET SDK ' + $(if ($hasDotnet) { 'yes' } else { 'no' }))
  Set-Row 'tools' $toolText $(if ($hasNode) { 'good' } else { 'none' })

  $lblVersion.Text = $(if ($available) { "v$available" } else { '' })
  $lblWhere.Text   = $(if ($installed) { 'Installed for ' + $env:USERNAME } else { 'Not installed' })

  $btnInstall.Enabled   = [bool]$available -and [bool]$edge
  $btnOpen.Enabled      = [bool]$installed
  $btnUninstall.Enabled = [bool]$installed
  $btnRebuild.Enabled   = $hasNode -and (Test-Path (Join-Path $root 'build.mjs'))
  foreach ($b in @($btnOpen, $btnRebuild, $btnUninstall)) {
    $b.ForeColor = $(if ($b.Enabled) { $C.ink } else { $C.muted })
  }

  if (-not $edge) {
    $btnInstall.Text = 'Install'
    $hint.Text = "Microsoft Edge was not found, and the app uses it to draw the card.`r`nInstall Edge, then reopen this window."
  } elseif (-not $installed) {
    $btnInstall.Text = 'Install'
    $hint.Text = "Nothing is installed yet. Install puts it in the Start menu, on the desktop,`r`nand in Settings > Apps with its own Uninstall button."
  } elseif ($available -and $available -ne $installed) {
    $btnInstall.Text = "Update to $available"
    $hint.Text = "Version $available is ready to replace $installed.`r`nYour issued-ID list and your settings are kept."
  } else {
    $btnInstall.Text = 'Reinstall'
    $hint.Text = "Up to date. After changing anything in src\, press Rebuild and then install again."
  }
}

function Invoke-Step($script, $busy) {
  $path = Join-Path $here $script
  if (-not (Test-Path $path)) {
    [System.Windows.Forms.MessageBox]::Show("$script is missing from:`n$here", $appName) | Out-Null
    return
  }
  foreach ($b in @($btnInstall, $btnOpen, $btnRebuild, $btnUninstall)) { $b.Enabled = $false }
  $hint.Text = "$busy`r`nA console window is doing the work. It closes when you press a key in it."
  $form.Cursor = [System.Windows.Forms.Cursors]::WaitCursor
  [System.Windows.Forms.Application]::DoEvents()
  try {
    Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', "`"$path`"" -WorkingDirectory $here -Wait
  } catch {
    [System.Windows.Forms.MessageBox]::Show("Could not run $script`n`n$($_.Exception.Message)", $appName) | Out-Null
  }
  $form.Cursor = [System.Windows.Forms.Cursors]::Default
  Refresh-Status
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
