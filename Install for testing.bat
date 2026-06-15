@echo off
REM Double-click to install the latest built MSIX on this PC for testing.
REM This needs administrator rights (to trust the test certificate) - click "Yes" on the prompt.
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File','%~dp0install.ps1'"
