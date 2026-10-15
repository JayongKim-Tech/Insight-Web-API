@echo off
powershell -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0CopyMaps.ps1" -Part OriginSelect
echo In In-Sight: select L45, press Ctrl+V
pause
