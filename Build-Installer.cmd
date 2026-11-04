@echo off
rem Double-click to build the customer installer. It appears in installer\output.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer\build-installer.ps1"
pause
