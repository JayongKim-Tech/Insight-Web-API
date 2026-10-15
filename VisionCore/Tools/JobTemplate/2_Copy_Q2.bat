@echo off
powershell -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0CopyMaps.ps1" -Part Q2
echo In In-Sight: select Q2, press Ctrl+V
pause
