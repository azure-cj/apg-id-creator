<#
.SYNOPSIS
    Builds the app as one .exe that runs without installing, and proves it starts.

.DESCRIPTION
    Writes windows\dist\AlphaPremierIdMaker.exe: the program, .NET, the card
    artwork, the fonts and the background-removal model packed into a single
    file. Double-clicking it runs the app; nothing is installed and nothing
    needs installing first.

    The first start on a computer takes a few seconds longer, while the file
    unpacks itself into the user's temporary folder.

    It does not set up the pen tablet driver, a Start-menu shortcut or an
    uninstaller. The installer package (package.ps1) is the one for the
    office computers; this is for trying the app or carrying it on a stick.

    After building, the file is started, checked for an open window, and
    closed again.
#>
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Here    = Split-Path -Parent $MyInvocation.MyCommand.Path
$Windows = Split-Path -Parent $Here
$Dist    = Join-Path $Windows 'dist'
$Out     = Join-Path $Dist 'portable'

if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
dotnet publish (Join-Path $Windows 'IdMaker.App\IdMaker.App.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -o $Out
if ($LASTEXITCODE -ne 0) { throw 'The app did not build.' }

$built = Join-Path $Out 'AlphaPremierIdMaker.exe'
if (-not (Test-Path $built)) { throw "No AlphaPremierIdMaker.exe in $Out." }
$others = @(Get-ChildItem $Out -Recurse -File | Where-Object { $_.FullName -ne $built })
if ($others.Count -gt 0) { throw "The single file is not single: it left $($others.Count) other file(s) beside it, the first being $($others[0].Name)." }

$exe = Join-Path $Dist 'AlphaPremierIdMaker.exe'
Copy-Item $built $exe -Force
"Built $exe ($([math]::Round((Get-Item $exe).Length / 1MB)) MB)"

# Start it from a folder of its own, the way a downloaded file is run. Nobody
# has signed in on a build machine, so the app asks the shared register for
# nothing (see selftest.ps1).
$data = Join-Path $env:LOCALAPPDATA 'Alpha Premier\ID Maker'
New-Item -ItemType Directory -Force -Path $data | Out-Null
$err = Join-Path $data 'last-error.txt'
Remove-Item $err -Force -ErrorAction SilentlyContinue

$run = Join-Path $env:TEMP "apg-id-maker-portable-$PID"
New-Item -ItemType Directory -Force -Path $run | Out-Null
Copy-Item $exe $run
$app = Start-Process -FilePath (Join-Path $run 'AlphaPremierIdMaker.exe') -PassThru
$title = ''
foreach ($i in 1..80) {
    Start-Sleep -Milliseconds 500
    $app.Refresh()
    if ($app.HasExited) { throw "The .exe closed by itself straight after starting (exit code $($app.ExitCode))." }
    if ($app.MainWindowTitle) { $title = $app.MainWindowTitle; break }
}
if ($title -ne 'Alpha Premier ID Maker') { $app | Stop-Process -Force; throw "The .exe's window did not open (title: '$title')." }
Start-Sleep -Seconds 4
$app.Refresh()
if ($app.HasExited) { throw 'The .exe closed by itself a few seconds after starting.' }
if (Test-Path $err) { Get-Content $err; $app | Stop-Process -Force; throw 'The .exe reported an error while starting.' }
$app | Stop-Process -Force
Remove-Item $data -Recurse -Force -ErrorAction SilentlyContinue
"The single .exe started and showed its window: $title"
