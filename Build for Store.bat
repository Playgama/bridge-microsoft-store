@echo off
REM Builds the Microsoft Store package (.msixbundle, x64 + arm64) into "dist-store".
REM The Store re-signs it on upload, so it is intentionally unsigned and cannot be
REM installed locally (use "Build MSIX.bat" for local testing).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-store.ps1" %*
echo.
pause
