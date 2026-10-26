#Requires -Version 7
<#
.SYNOPSIS
  Builds ZhaoHuoSystem-v<Version>.zip from a plugin DLL: the plugin, the one-click installer and the
  official BepInEx build it installs when missing. Called by release.ps1; can also be run on its own.

  Package layout (extracting it straight into the game folder also works for a manual install):
    install.bat, uninstall.bat, install guides in five languages (from installer/docs, {VERSION} filled in)
    BepInEx/plugins/ZhaoHuoSystem/ZhaoHuoSystem.dll, LICENSE
    installer/ZhaoHuoSystem-Setup.ps1, BepInEx-*.zip, THIRD-PARTY-NOTICES.txt, LGPL-2.1.txt

  -Manual builds ZhaoHuoSystem-v<Version>-manual.zip instead, for sites that quarantine scripts and nested
  archives (Nexus Mods): the BepInEx files already extracted in game-folder layout, plus
    BepInEx/plugins/ZhaoHuoSystem/ZhaoHuoSystem.dll, LICENSE, guides (from installer/docs/manual), notices

  -BepInEx5 builds ZhaoHuoSystem-v<Version>-BepInEx5.zip from the BepInEx 5 build of the plugin (F118), for games that
  already have BepInEx 5 (a translation patch, say). No BepInEx is shipped: install.bat / uninstall.bat (from
  installer/bepinex5) run the setup script with -Loader 5, which only adds or removes the plugin.
    install.bat, uninstall.bat, guides (from installer/docs/bepinex5)
    BepInEx/plugins/ZhaoHuoSystem/ZhaoHuoSystem.dll, LICENSE, installer/ZhaoHuoSystem-Setup.ps1
  With -Manual as well it builds ZhaoHuoSystem-v<Version>-BepInEx5-manual.zip, the script-free one for Nexus Mods:
    BepInEx/plugins/ZhaoHuoSystem/ZhaoHuoSystem.dll, LICENSE, guides (from installer/docs/bepinex5-manual)

.EXAMPLE
  ./scripts/package.ps1 -Version 0.3.7 -Dll src/ZhaoHuoSystem/bin/Release/netstandard2.0/ZhaoHuoSystem.dll
  ./scripts/package.ps1 -Version 0.3.7 -Dll src/ZhaoHuoSystem/bin/Release/netstandard2.0/ZhaoHuoSystem.dll -Manual
  ./scripts/package.ps1 -Version 0.3.48 -Dll src/ZhaoHuoSystem/bin/Release-BepInEx5/ZhaoHuoSystem.dll -BepInEx5
  ./scripts/package.ps1 -Version 0.3.48 -Dll src/ZhaoHuoSystem/bin/Release-BepInEx5/ZhaoHuoSystem.dll -BepInEx5 -Manual
#>
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][string]$Dll,
    [string]$OutDir,
    [switch]$Manual,
    [switch]$BepInEx5
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

if ($BepInEx5) {
    # No BepInEx in this package, so nothing to fetch or check.
    $stage = Join-Path $OutDir "stage-$Version-bepinex5$(if ($Manual) { '-manual' })"
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $pluginDir = New-Item -ItemType Directory -Force -Path (Join-Path $stage 'BepInEx/plugins/ZhaoHuoSystem')
    Copy-Item -LiteralPath (Resolve-Path -LiteralPath $Dll).Path -Destination (Join-Path $pluginDir 'ZhaoHuoSystem.dll')
    Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $pluginDir
    # The manual one has no scripts at all (Nexus quarantines them); its guides sit in the plugin folder.
    if ($Manual) { $guideDir = $pluginDir; $guideSource = 'docs/bepinex5-manual' }
    else {
        foreach ($file in 'install.bat', 'uninstall.bat') { Copy-Item -LiteralPath (Join-Path $installer "bepinex5/$file") -Destination $stage }
        $setupDir = New-Item -ItemType Directory -Force -Path (Join-Path $stage 'installer')
        Copy-Item -LiteralPath $setup -Destination $setupDir
        $guideDir = $stage; $guideSource = 'docs/bepinex5'
    }
    $guides = [ordered]@{ 'zh-Hans.txt' = '安装说明.txt'; 'zh-Hant.txt' = '安裝說明.txt'; 'en.txt' = 'INSTALL.txt'; 'ko.txt' = '설치 안내.txt'; 'ja.txt' = 'インストール説明.txt' }
    foreach ($guide in $guides.GetEnumerator()) {
        $text = (Get-Content -LiteralPath (Join-Path $installer "$guideSource/$($guide.Key)") -Raw -Encoding utf8).Replace('{VERSION}', $Version)
        [IO.File]::WriteAllText((Join-Path $guideDir $guide.Value), ($text -replace "`r?`n", "`r`n"), (New-Object System.Text.UTF8Encoding $true))
    }
    $name = if ($Manual) { "ZhaoHuoSystem-v$Version-BepInEx5-manual.zip" } else { "ZhaoHuoSystem-v$Version-BepInEx5.zip" }
    $zip = Join-Path $OutDir $name
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip)
    Remove-Item -LiteralPath $stage -Recurse -Force
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
    Set-Content -LiteralPath "$zip.sha256" -Value "$hash  $name" -Encoding ascii
    return [pscustomobject]@{ Zip = $zip; Name = $name; Sha256 = $hash }
}

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
Add-Type -AssemblyName System.IO.Compression.FileSystem
# The manual package carries BepInEx already extracted; it is unpacked before the plugin goes in.
if ($Manual) { [IO.Compression.ZipFile]::ExtractToDirectory($bepinex, $stage) }
$pluginDir = New-Item -ItemType Directory -Force -Path (Join-Path $stage 'BepInEx/plugins/ZhaoHuoSystem')
Copy-Item -LiteralPath (Resolve-Path -LiteralPath $Dll).Path -Destination (Join-Path $pluginDir 'ZhaoHuoSystem.dll')
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $pluginDir
# Guides and notices sit next to install.bat, or inside the plugin folder so extracting the manual package
# into the game folder adds nothing outside BepInEx's own files.
if ($Manual) { $guideDir = $pluginDir; $guideSource = 'docs/manual'; $noticeDir = $pluginDir }
else {
    foreach ($file in 'install.bat', 'uninstall.bat') { Copy-Item -LiteralPath (Join-Path $installer $file) -Destination $stage }
    $guideDir = $stage; $guideSource = 'docs'
    $noticeDir = New-Item -ItemType Directory -Force -Path (Join-Path $stage 'installer')
    Copy-Item -LiteralPath $setup -Destination $noticeDir
    Copy-Item -LiteralPath $bepinex -Destination $noticeDir
}
# Install guides, UTF-8 with BOM and CRLF so Notepad opens them correctly.
$guides = [ordered]@{ 'zh-Hans.txt' = '安装说明.txt'; 'zh-Hant.txt' = '安裝說明.txt'; 'en.txt' = 'INSTALL.txt'; 'ko.txt' = '설치 안내.txt'; 'ja.txt' = 'インストール説明.txt' }
foreach ($guide in $guides.GetEnumerator()) {
    $text = (Get-Content -LiteralPath (Join-Path $installer "$guideSource/$($guide.Key)") -Raw -Encoding utf8).Replace('{VERSION}', $Version)
    $text = ($text -replace "`r?`n", "`r`n")
    [IO.File]::WriteAllText((Join-Path $guideDir $guide.Value), $text, (New-Object System.Text.UTF8Encoding $true))
}
foreach ($file in 'THIRD-PARTY-NOTICES.txt', 'LGPL-2.1.txt') { Copy-Item -LiteralPath (Join-Path $installer $file) -Destination $noticeDir }

$name = if ($Manual) { "ZhaoHuoSystem-v$Version-manual.zip" } else { "ZhaoHuoSystem-v$Version.zip" }
$zip = Join-Path $OutDir $name
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip)
Remove-Item -LiteralPath $stage -Recurse -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
Set-Content -LiteralPath "$zip.sha256" -Value "$hash  $name" -Encoding ascii
[pscustomobject]@{ Zip = $zip; Name = $name; Sha256 = $hash }
