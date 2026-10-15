@echo off
powershell -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0CopyMaps.ps1" -Part OriginTable
echo In In-Sight: select J21, press Ctrl+V
pause
