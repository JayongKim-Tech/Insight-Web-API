@echo off
powershell -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0CopyMaps.ps1" -Part Coef
echo In In-Sight: select W18, press Ctrl+V
pause
