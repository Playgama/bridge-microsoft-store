@echo off
REM Double-click this file to build the installable MSIX.
REM It does everything automatically and leaves the result in the "dist" folder.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
echo.
pause
