@echo off
rem BepInEx 5 package: removes the plugin and its settings; BepInEx 5 itself is left as it is.
set "PSModulePath="
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer\ZhaoHuoSystem-Setup.ps1" -Action uninstall -Loader 5
