@echo off
setlocal
title Install Alpha Premier ID Maker

rem ---------------------------------------------------------------
rem  Installs the ID Maker for whoever runs it. No administrator
rem  rights, and nothing outside this person's own AppData folder
rem  and their own corner of the registry.
rem  uninstall.cmd, and Settings > Apps, undo every line of it.
rem ---------------------------------------------------------------

set "APP_NAME=Alpha Premier ID Maker"
set "APP_TARGET=%LOCALAPPDATA%\AlphaPremierIDMaker"
set "SRC=%~dp0"
set "APP_VERSION=unknown"
if exist "%SRC%version.txt" set /p APP_VERSION=<"%SRC%version.txt"

echo.
echo   %APP_NAME%  %APP_VERSION%
echo   ------------------------------------------------
echo.

rem --- the files have to be side by side: running this straight out of
rem     a zip extracts the .cmd on its own to a temp folder ---
if not exist "%SRC%Alpha Premier ID Maker.html" (
  echo   PROBLEM: "Alpha Premier ID Maker.html" is not next to this
  echo   installer.
  echo.
  echo   It is looking in:
  echo     %SRC%
  echo.
  echo   If you double-clicked install.cmd inside the zip, Windows
  echo   only unpacked that one file. Right-click the zip, choose
  echo   "Extract All", then run install.cmd from the folder that
  echo   comes out.
  echo.
  pause
  exit /b 1
)

rem --- Edge draws the card, so it has to be here. Look where it
rem     normally lives, then ask Windows where it registered it. ---
set "APP_EDGE="
if exist "%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe" set "APP_EDGE=%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe"
if not defined APP_EDGE if exist "%ProgramFiles%\Microsoft\Edge\Application\msedge.exe" set "APP_EDGE=%ProgramFiles%\Microsoft\Edge\Application\msedge.exe"
if not defined APP_EDGE if exist "%LOCALAPPDATA%\Microsoft\Edge\Application\msedge.exe" set "APP_EDGE=%LOCALAPPDATA%\Microsoft\Edge\Application\msedge.exe"
if not defined APP_EDGE for /f "tokens=2,*" %%a in ('reg query "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe" /ve 2^>nul ^| find "REG_SZ"') do set "APP_EDGE=%%b"
if not defined APP_EDGE for /f "tokens=2,*" %%a in ('reg query "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe" /ve 2^>nul ^| find "REG_SZ"') do set "APP_EDGE=%%b"
if defined APP_EDGE if not exist "%APP_EDGE%" set "APP_EDGE="

if not defined APP_EDGE (
  echo   PROBLEM: Microsoft Edge was not found, and the app needs it
  echo   to draw the ID card.
  echo.
  echo   Looked in Program Files, Program Files ^(x86^), AppData and
  echo   the registry. If Edge is installed somewhere unusual, open
  echo   it, go to edge://version, and send the "Executable path"
  echo   line.
  echo.
  pause
  exit /b 1
)
echo   Using Edge at: %APP_EDGE%

if exist "%APP_TARGET%\index.html" (
  echo   Updating the copy already installed here.
  echo   Your issued-ID list and settings are kept.
) else (
  echo   Installing to %APP_TARGET%
)
echo.

if not exist "%APP_TARGET%" mkdir "%APP_TARGET%" 2>nul
copy /y "%SRC%Alpha Premier ID Maker.html" "%APP_TARGET%\index.html" >nul || goto :failed
copy /y "%SRC%app.ico" "%APP_TARGET%\app.ico" >nul || goto :failed
copy /y "%SRC%uninstall.cmd" "%APP_TARGET%\uninstall.cmd" >nul 2>nul
if exist "%SRC%version.txt" copy /y "%SRC%version.txt" "%APP_TARGET%\version.txt" >nul 2>nul

powershell -NoProfile -ExecutionPolicy Bypass -File "%SRC%setup.ps1" || goto :failed

echo   Done.
echo.
echo   It is in the Start menu and on your desktop, and in
echo   Settings ^> Apps like any other program. Right-click it on
echo   the taskbar to pin it there.
echo.
echo   Every ID you issue is written to the shared Google Sheet
echo   automatically. Nothing to set up.
echo.
pause
exit /b 0

:failed
echo.
echo   Something went wrong. If the app is open, close it and run
echo   install.cmd again.
echo.
pause
exit /b 1
