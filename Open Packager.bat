@echo off
REM Opens the visual Packager: pick the game folder, fill in the info, build the package.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-packager.ps1"
pause
