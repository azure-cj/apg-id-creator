@echo off
setlocal
title Rebuild Alpha Premier ID Maker
cd /d "%~dp0.."

rem ---------------------------------------------------------------
rem  Rebuilds everything after a change to src\:
rem    1. the single HTML file
rem    2. the installer zip
rem    3. the desktop exe, if the .NET SDK is here
rem  Double-click it, or run it from a terminal.
rem ---------------------------------------------------------------

echo.
echo   Rebuilding Alpha Premier ID Maker
echo   ------------------------------------------------
echo.

where node >nul 2>nul || (
  echo   Node is not on the PATH, and the app is built with it.
  echo   Install it from https://nodejs.org and run this again.
  echo.
  pause
  exit /b 1
)

echo   [1/3] the app...
call node build.mjs || goto :failed

echo   [2/3] the installer zip...
call node desktop\pack.mjs || goto :failed

rem --- the exe is optional: the installer works without it ---
set "DOTNET="
where dotnet >nul 2>nul && set "DOTNET=dotnet"
if not defined DOTNET if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"

if not defined DOTNET (
  echo   [3/3] the desktop exe... skipped, no .NET SDK on this computer.
  echo.
  echo   Done. The app and the installer zip are rebuilt.
  echo   To build the exe as well, install the .NET SDK from
  echo   https://dotnet.microsoft.com/download and run this again.
  echo.
  pause
  exit /b 0
)

rem --- Windows will not let a build replace a running program, and the
rem     failure arrives as a stack trace about a denied path ---
tasklist /nh 2>nul | find /i "Alpha Premier ID" >nul && (
  echo   [3/3] the desktop exe...
  echo.
  echo   The app is open, and Windows will not let the build replace
  echo   a running program. Close the Alpha Premier ID Maker window
  echo   and run this again.
  echo.
  echo   The app and the installer zip above were rebuilt and are
  echo   up to date; only the exe was left alone.
  echo.
  pause
  exit /b 1
)

echo   [3/3] the desktop exe...
pushd desktop\shell
"%DOTNET%" publish -c Release -r win-x64 --nologo -v minimal || (popd & goto :failed)
popd

echo.
echo   Done. All three are rebuilt:
echo.
echo     Alpha Premier ID Maker.html
echo     desktop\dist\Alpha Premier ID Maker - install.zip
echo     desktop\shell\bin\Release\net10.0-windows\win-x64\publish\
echo.
echo   The exe does not replace what is already installed. Run
echo   desktop\install.cmd from the new zip to update that.
echo.
pause
exit /b 0

:failed
echo.
echo   Stopped: the step above failed. Nothing further was built.
echo.
pause
exit /b 1
