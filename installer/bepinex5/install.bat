@echo off
rem BepInEx 5 package: puts the plugin into an existing BepInEx 5 (for example one installed by a translation patch).
set "PSModulePath="
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer\ZhaoHuoSystem-Setup.ps1" -Action install -Loader 5
