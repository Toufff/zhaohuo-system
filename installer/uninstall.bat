@echo off
rem Use Windows PowerShell's own module path even when started from a PowerShell 7 window.
set "PSModulePath="
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer\ZhaoHuoSystem-Setup.ps1" -Action uninstall
