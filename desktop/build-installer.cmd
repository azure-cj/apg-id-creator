@echo off
setlocal
title Build the Alpha Premier ID Maker installer
cd /d "%~dp0.."

rem ---------------------------------------------------------------
rem  Compiles desktop\installer\setup.iss into one file you can
rem  hand to somebody: desktop\dist\AP-ID-Maker-Setup-<version>.exe
rem
rem  Needs Inno Setup 6: https://jrsoftware.org/isdl.php
rem ---------------------------------------------------------------

echo.
echo   Alpha Premier ID Maker - installer
echo   ------------------------------------------------
echo.

if not exist "Alpha Premier ID Maker.html" (
  echo   The app has not been built yet. Run desktop\rebuild.cmd first.
  echo.
  pause
  exit /b 1
)

rem --- the version the app itself reports ---
set "APP_VERSION="
for /f "tokens=2 delims='" %%v in ('findstr /c:"const APP_VERSION" "src\config.js"') do set "APP_VERSION=%%v"
if not defined APP_VERSION (
  echo   Could not read APP_VERSION from src\config.js.
  echo.
  pause
  exit /b 1
)

rem --- find the Inno Setup compiler ---
set "ISCC="
where iscc >nul 2>nul && set "ISCC=iscc"
if not defined ISCC if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined ISCC (
  echo   Inno Setup was not found. It is free, about 5 MB, and is what
  echo   turns this into a single setup.exe:
  echo.
  echo       https://jrsoftware.org/isdl.php
  echo.
  echo   Install it, then run this again. Everything else - the app, the
  echo   zip, the installed copy on this computer - works without it.
  echo.
  pause
  exit /b 1
)

echo   Version:  %APP_VERSION%
echo   Compiler: %ISCC%
echo.

if not exist "desktop\dist" mkdir "desktop\dist"
"%ISCC%" /DAppVersion=%APP_VERSION% "desktop\installer\setup.iss" || goto :failed

echo.
echo   Done.
echo.
echo     desktop\dist\AP-ID-Maker-Setup-%APP_VERSION%.exe
echo.
echo   That one file is the whole installer. Send it to whoever needs
echo   the app; they double-click it and follow the prompts.
echo.
echo   It is not code-signed, so the first time anyone runs it Windows
echo   shows "Windows protected your PC". They click "More info", then
echo   "Run anyway". Tell them to expect that, so it does not look like
echo   something went wrong.
echo.
pause
exit /b 0

:failed
echo.
echo   The compiler reported an error above. Nothing was written.
echo.
pause
exit /b 1
