@echo off
setlocal
title Remove Alpha Premier ID Maker
set "APP_NAME=Alpha Premier ID Maker"
set "APP_TARGET=%LOCALAPPDATA%\AlphaPremierIDMaker"

echo.
echo   Removing %APP_NAME%...
echo.
del /q "%APPDATA%\Microsoft\Windows\Start Menu\Programs\%APP_NAME%.lnk" 2>nul
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Remove-Item (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Alpha Premier ID Maker.lnk') -ErrorAction SilentlyContinue;" ^
  "Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AlphaPremierIDMaker' -Recurse -Force -ErrorAction SilentlyContinue" 2>nul

echo   The list of IDs issued on this laptop lives with the app, so
echo   removing it removes that list. The shared Google Sheet is not
echo   touched either way - if the register is set up, nothing is
echo   really lost.
echo.
choice /c YN /n /m "   Remove the app and its local list as well? (Y/N) "
if errorlevel 2 (
  echo.
  echo   Shortcuts and the Windows entry are gone. The app is still
  echo   in %APP_TARGET%.
  echo.
  pause
  exit /b 0
)
cd /d "%LOCALAPPDATA%"
rmdir /s /q "%APP_TARGET%" 2>nul
echo.
echo   Removed.
echo.
pause
