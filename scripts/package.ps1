#Requires -Version 7
<#
.SYNOPSIS
  Builds ZhaoHuoSystem-v<Version>.zip from a plugin DLL: the plugin, the one-click installer and the
  official BepInEx build it installs when missing. Called by release.ps1; can also be run on its own.

  Package layout (extracting it straight into the game folder also works for a manual install):
    install.bat, uninstall.bat
    BepInEx/plugins/ZhaoHuoSystem/ZhaoHuoSystem.dll, LICENSE
    installer/ZhaoHuoSystem-Setup.ps1, BepInEx-*.zip, THIRD-PARTY-NOTICES.txt, LGPL-2.1.txt

.EXAMPLE
  ./scripts/package.ps1 -Version 0.3.7 -Dll src/ZhaoHuoSystem/bin/Release/netstandard2.0/ZhaoHuoSystem.dll
#>
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][string]$Dll,
    [string]$OutDir
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $repo 'dist' }
$installer = Join-Path $repo 'installer'
$setup = Join-Path $installer 'ZhaoHuoSystem-Setup.ps1'
$setupText = Get-Content -LiteralPath $setup -Raw
$bepinexName = [regex]::Match($setupText, "BepInExZip = '([^']+)'").Groups[1].Value
$pinned = [regex]::Match($setupText, "BepInExSha256 = '([0-9A-F]{64})'").Groups[1].Value
if (-not $bepinexName -or -not $pinned) { throw 'BepInEx name or SHA256 not found in ZhaoHuoSystem-Setup.ps1.' }

# The official build, cached under dist/cache and checked against the hash pinned in the setup script.
$cache = New-Item -ItemType Directory -Force -Path (Join-Path $OutDir 'cache')
$bepinex = Join-Path $cache $bepinexName
if (-not (Test-Path -LiteralPath $bepinex)) {
    $build = [regex]::Match($bepinexName, 'be\.(\d+)').Groups[1].Value
    Invoke-WebRequest -Uri ("https://builds.bepinex.dev/projects/bepinex_be/$build/" + [uri]::EscapeDataString($bepinexName)) -OutFile $bepinex
}
if ((Get-FileHash -LiteralPath $bepinex -Algorithm SHA256).Hash -ne $pinned) {
    Remove-Item -LiteralPath $bepinex
    throw "$bepinexName does not match the SHA256 pinned in ZhaoHuoSystem-Setup.ps1."
}

$stage = Join-Path $OutDir "stage-$Version"
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
$pluginDir = New-Item -ItemType Directory -Force -Path (Join-Path $stage 'BepInEx/plugins/ZhaoHuoSystem')
$installerDir = New-Item -ItemType Directory -Force -Path (Join-Path $stage 'installer')
Copy-Item -LiteralPath (Resolve-Path -LiteralPath $Dll).Path -Destination (Join-Path $pluginDir 'ZhaoHuoSystem.dll')
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $pluginDir
foreach ($file in 'install.bat', 'uninstall.bat') { Copy-Item -LiteralPath (Join-Path $installer $file) -Destination $stage }
foreach ($file in 'ZhaoHuoSystem-Setup.ps1', 'THIRD-PARTY-NOTICES.txt', 'LGPL-2.1.txt') { Copy-Item -LiteralPath (Join-Path $installer $file) -Destination $installerDir }
Copy-Item -LiteralPath $bepinex -Destination $installerDir

$name = "ZhaoHuoSystem-v$Version.zip"
$zip = Join-Path $OutDir $name
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip)
Remove-Item -LiteralPath $stage -Recurse -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
Set-Content -LiteralPath "$zip.sha256" -Value "$hash  $name" -Encoding ascii
[pscustomobject]@{ Zip = $zip; Name = $name; Sha256 = $hash }
