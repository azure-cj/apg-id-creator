<#
.SYNOPSIS
    Builds the setup program HR is given: AlphaPremierIdMaker-Setup.exe.

.DESCRIPTION
    Publishes the app with .NET built in (so the office computers need
    nothing installed first), fetches XP-Pen's driver and checks it is the
    exact file named in Driver.psd1, and compiles IdMaker.iss with Inno
    Setup into windows\dist\AlphaPremierIdMaker-Setup.exe.

    The driver is XP-Pen's software, carried unchanged inside the setup
    program; it is not kept in this repository.

    Needs Inno Setup 6 (https://jrsoftware.org/isinfo.php). It is a build
    tool only: nothing of it has to be on the computers the app goes to.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Here    = Split-Path -Parent $MyInvocation.MyCommand.Path
$Windows = Split-Path -Parent $Here
$Dist    = Join-Path $Windows 'dist'
$App     = Join-Path $Dist 'app'
$Driver  = Import-PowerShellDataFile (Join-Path $Here 'Driver.psd1')

$iscc = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 was not found. Install it from https://jrsoftware.org/isinfo.php and run this again.' }

if (Test-Path $Dist) { Remove-Item $Dist -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Dist | Out-Null

dotnet publish (Join-Path $Windows 'IdMaker.App\IdMaker.App.csproj') -c Release -r win-x64 --self-contained true -p:DebugType=none -o $App
if ($LASTEXITCODE -ne 0) { throw 'The app did not build.' }

foreach ($need in 'AlphaPremierIdMaker.exe', 'Assets\front-template.png', 'Assets\back.png', 'Fonts\Poppins-Bold.ttf', 'Models\modnet.onnx', 'onnxruntime.dll') {
    if (-not (Test-Path (Join-Path $App $need))) { throw "The published app is missing $need." }
}

[xml]$project = Get-Content (Join-Path $Windows 'IdMaker.App\IdMaker.App.csproj')
$version = ($project.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { throw 'No <Version> in IdMaker.App.csproj.' }

$driverDir = Join-Path $Dist 'driver'
New-Item -ItemType Directory -Force -Path $driverDir | Out-Null
# The driver is downloaded once and kept beside this script (CI keeps the
# folder between runs). A kept copy is used only if it is still, byte for
# byte, the file Driver.psd1 names; anything else is fetched again.
$kept = Join-Path $Here '.cache'
New-Item -ItemType Directory -Force -Path $kept | Out-Null
$zip = Join-Path $kept $Driver.Zip
$hash = if (Test-Path $zip) { (Get-FileHash -Algorithm SHA256 -Path $zip).Hash.ToLowerInvariant() } else { '' }
if ($hash -eq $Driver.Sha256) {
    Write-Host "Using the kept copy of the $($Driver.Name) $($Driver.Version)."
} else {
    Write-Host "Fetching the $($Driver.Name) $($Driver.Version)..."
    Invoke-WebRequest -Uri $Driver.Url -OutFile $zip -UseBasicParsing
    $hash = (Get-FileHash -Algorithm SHA256 -Path $zip).Hash.ToLowerInvariant()
    if ($hash -ne $Driver.Sha256) {
        Remove-Item $zip -Force
        throw "The driver that was downloaded is not the file Driver.psd1 names (checksum $hash). Check XP-Pen's download page and update Driver.psd1."
    }
}
Expand-Archive -Path $zip -DestinationPath $driverDir -Force
$driverExe = Join-Path $driverDir $Driver.Exe
if (-not (Test-Path $driverExe)) { throw "The driver package did not contain $($Driver.Exe)." }

& $iscc "/DAppVersion=$version" "/DSourceDir=$App" "/DDriverExe=$driverExe" "/DOutputDir=$Dist" /Qp (Join-Path $Here 'IdMaker.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup could not build the setup program.' }

$out = Join-Path $Dist 'AlphaPremierIdMaker-Setup.exe'
if (-not (Test-Path $out)) { throw "Inno Setup finished but $out is not there." }
Write-Host "Built $out ($([math]::Round((Get-Item $out).Length / 1MB)) MB), version $version"
