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

rem --- Edge draws the card, so it has to be here ---
set "APP_EDGE="
if exist "%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe" set "APP_EDGE=%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe"
if not defined APP_EDGE if exist "%ProgramFiles%\Microsoft\Edge\Application\msedge.exe" set "APP_EDGE=%ProgramFiles%\Microsoft\Edge\Application\msedge.exe"
if not defined APP_EDGE (
  echo   Microsoft Edge was not found, and the app needs it to draw
  echo   the ID card. Install Edge from https://www.microsoft.com/edge
  echo   and run this again.
  echo.
  pause
  exit /b 1
)

if not exist "%SRC%Alpha Premier ID Maker.html" (
  echo   "Alpha Premier ID Maker.html" is missing from this folder.
  echo   Keep all the files together and run install.cmd again.
  echo.
  pause
  exit /b 1
)

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
echo   Once, before the first card: open "Issued IDs", then
echo   "Shared register", and paste the web-app address. That is
echo   what keeps a record of every ID off this laptop, which
echo   matters more than it sounds - see the note in the README.
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
