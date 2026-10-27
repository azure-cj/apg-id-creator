@echo off
setlocal
title Install Alpha Premier ID Maker

rem ---------------------------------------------------------------
rem  Installs the ID Maker for whoever runs it. No admin rights, and
rem  nothing outside this person's own AppData folder.
rem  uninstall.cmd undoes every line of it.
rem ---------------------------------------------------------------

set "APP_NAME=Alpha Premier ID Maker"
set "APP_TARGET=%LOCALAPPDATA%\AlphaPremierIDMaker"
set "SRC=%~dp0"

echo.
echo   %APP_NAME%
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

echo   Installing to %APP_TARGET%
if not exist "%APP_TARGET%" mkdir "%APP_TARGET%" 2>nul
copy /y "%SRC%Alpha Premier ID Maker.html" "%APP_TARGET%\index.html" >nul || goto :failed
copy /y "%SRC%app.ico" "%APP_TARGET%\app.ico" >nul || goto :failed
copy /y "%SRC%uninstall.cmd" "%APP_TARGET%\uninstall.cmd" >nul 2>nul

powershell -NoProfile -ExecutionPolicy Bypass -File "%SRC%shortcut.ps1" || goto :failed

echo.
echo   Done.
echo.
echo   Open it from the Start menu, or from the shortcut on your
echo   desktop. Right-click it on the taskbar to pin it there.
echo.
echo   Once, before your first card: open "Issued IDs", then
echo   "Shared register", and paste the address your HR lead gives
echo   you. That is what makes this computer check the same list of
echo   ID numbers as everyone else's.
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
