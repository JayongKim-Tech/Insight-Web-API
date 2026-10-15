@echo off
powershell -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0CopyMaps.ps1" -Part Out
echo In In-Sight: select O28, press Ctrl+V
pause
