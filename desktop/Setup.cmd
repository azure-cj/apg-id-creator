@echo off
setlocal
title Alpha Premier ID Maker - Setup

rem ---------------------------------------------------------------
rem  Opens the setup window. PowerShell draws it; this only supplies
rem  the execution policy, so nobody has to change a machine-wide
rem  setting.
rem
rem  It runs PowerShell in the foreground on purpose. Launched with
rem  `start` and a hidden window, a script that fails to parse shows
rem  nothing at all -- the window appears to flash and vanish, with
rem  no way to find out why.
rem ---------------------------------------------------------------

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup-window.ps1"
if errorlevel 1 (
  echo.
  echo   The setup window could not open. The reason is above.
  echo.
  echo   You can still use install.cmd, rebuild.cmd and uninstall.cmd
  echo   in this folder directly - they do the same jobs.
  echo.
  pause
)
