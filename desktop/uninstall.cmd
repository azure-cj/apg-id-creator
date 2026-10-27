@echo off
setlocal
title Remove Alpha Premier ID Maker
set "APP_NAME=Alpha Premier ID Maker"
set "APP_TARGET=%LOCALAPPDATA%\AlphaPremierIDMaker"

echo.
echo   Removing %APP_NAME%...
echo.
del /q "%APPDATA%\Microsoft\Windows\Start Menu\Programs\%APP_NAME%.lnk" 2>nul
powershell -NoProfile -ExecutionPolicy Bypass -Command "Remove-Item (Join-Path ([Environment]::GetFolderPath('Desktop')) ('%APP_NAME%' + '.lnk')) -ErrorAction SilentlyContinue" 2>nul

echo   The list of IDs issued on this computer lives with the app.
echo   Removing the app removes that list too. The shared Google
echo   Sheet is not touched either way.
echo.
choice /c YN /n /m "   Remove the app and its list as well? (Y/N) "
if errorlevel 2 (
  echo.
  echo   Shortcuts removed. The app is still in %APP_TARGET%.
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
