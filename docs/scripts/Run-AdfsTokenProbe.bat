@echo off
setlocal
cd /d "%~dp0\..\.."
echo Running ADFS token probe under current Windows identity...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "docs\scripts\Invoke-AdfsTokenProbe.ps1"
echo.
echo Result file:
echo SpeechMessageProducts.ChurchReport\Logs\adfs-token-probe-latest.json
echo.
pause