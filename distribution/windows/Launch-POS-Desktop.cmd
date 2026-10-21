@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Launch-POS-Desktop.ps1"
exit /b %ERRORLEVEL%
