@echo off
rem Opens the setup window. PowerShell draws it; this only supplies the
rem execution policy, so nobody has to change a machine-wide setting.
start "" powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0setup-window.ps1"
