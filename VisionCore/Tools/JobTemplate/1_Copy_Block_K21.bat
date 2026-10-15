@echo off
powershell -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0CopyMaps.ps1" -Part Block
echo In In-Sight: select K21, press Ctrl+V
pause
