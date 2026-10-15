#Requires -Version 7
<#
.SYNOPSIS
  Builds, tests and packages a release on a machine with the game installed, and optionally publishes it.

.DESCRIPTION
  The plugin compiles against the installed game's assemblies, which are never stored in the repository,
  so releases are built here rather than on GitHub Actions. The script refuses to continue when:
    - the working tree has uncommitted changes;
    - the version in ZhaoHuoSystem.csproj, Plugin.cs or CHANGELOG.md differs from -Version;
    - the game's assemblies do not match the fingerprints the plugin checks at startup;
    - the build or any test fails.
  Without -Publish it only writes dist/ZhaoHuoSystem-v<Version>.zip and its .sha256 file.
  With -Publish it also tags HEAD (which must equal origin/main), pushes the tag and creates the
  GitHub release with the CHANGELOG section as notes (requires an authenticated gh CLI).

.EXAMPLE
  ./scripts/release.ps1 -Version 0.3.6
  ./scripts/release.ps1 -Version 0.3.6 -Publish
  ./scripts/release.ps1 -Version 0.3.6 -GameDir 'D:\SteamLibrary\steamapps\common\LegendOfMortal'
#>
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$GameDir = $(if ($env:ZHAOHUO_GAME_DIR) { $env:ZHAOHUO_GAME_DIR } else { 'F:\SteamLibrary\steamapps\common\LegendOfMortal' }),
    [switch]$Publish
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    function Step([string]$message) { Write-Host "==> $message" -ForegroundColor Cyan }
    function Invoke-Checked([string]$file, [string[]]$arguments) {
        & $file @arguments
        if ($LASTEXITCODE -ne 0) { throw "$file $($arguments -join ' ') failed with exit code $LASTEXITCODE." }
    }

    Step 'Checking working tree and version numbers'
    if (git status --porcelain) { throw 'The working tree has uncommitted changes; commit or stash them first.' }
    $escaped = [regex]::Escape($Version)
    $project = Get-Content -LiteralPath 'src/ZhaoHuoSystem/ZhaoHuoSystem.csproj' -Raw
    if ($project -notmatch "<Version>$escaped</Version>") { throw "ZhaoHuoSystem.csproj <Version> is not $Version." }
    $plugin = Get-Content -LiteralPath 'src/ZhaoHuoSystem/Plugin.cs' -Raw
    if ($plugin -notmatch "\[BepInPlugin\(""[^""]+"", ""[^""]+"", ""$escaped""\)\]") { throw "Plugin.cs [BepInPlugin] version is not $Version." }
    $changelog = Get-Content -LiteralPath 'CHANGELOG.md' -Raw
    $section = [regex]::Match($changelog, "(?ms)^## $escaped\b[^\r\n]*\r?\n(.*?)(?=^## |\z)")
    if (-not $section.Success) { throw "CHANGELOG.md has no '## $Version' section." }
    $notes = $section.Groups[1].Value.Trim()

    Step "Checking game assemblies in $GameDir"
    if (-not (Test-Path -LiteralPath (Join-Path $GameDir 'Mortal.exe'))) { throw "Mortal.exe not found in $GameDir; pass -GameDir or set ZHAOHUO_GAME_DIR." }
    $fingerprints = [regex]::Matches($plugin, '\["(?<file>[\w.]+\.dll)"\]\s*=\s*"(?<hash>[0-9A-F]{64})"')
    if ($fingerprints.Count -eq 0) { throw 'No assembly fingerprints found in Plugin.cs.' }
    foreach ($fingerprint in $fingerprints) {
        $file = $fingerprint.Groups['file'].Value
        $actual = (Get-FileHash -LiteralPath (Join-Path $GameDir "Mortal_Data\Managed\$file") -Algorithm SHA256).Hash
        if ($actual -ne $fingerprint.Groups['hash'].Value) { throw "$file does not match the fingerprint in Plugin.cs; this game build is not the supported one." }
    }
    $manifest = Join-Path (Split-Path -Parent (Split-Path -Parent $GameDir)) 'appmanifest_1859910.acf'
    $build = if (Test-Path -LiteralPath $manifest) { [regex]::Match((Get-Content -LiteralPath $manifest -Raw), '"buildid"\s+"(\d+)"').Groups[1].Value } else { '' }
    if (-not $build) { $build = 'unknown' }
    $loader = (Get-Item -LiteralPath (Join-Path $GameDir 'BepInEx\core\BepInEx.Core.dll')).VersionInfo.ProductVersion -replace '\+.*$', ''

    Step 'Building'
    Invoke-Checked dotnet @('build', 'src/ZhaoHuoSystem/ZhaoHuoSystem.csproj', '-c', 'Release', "-p:GameDir=$GameDir", '--nologo')
    $dll = Join-Path $repo 'src/ZhaoHuoSystem/bin/Release/netstandard2.0/ZhaoHuoSystem.dll'
    $built = [Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString()
    if ($built -ne "$Version.0") { throw "Built assembly version is $built, expected $Version.0." }

    Step 'Running tests'
    Invoke-Checked dotnet @('run', '--project', 'tests/ui-state', '-c', 'Release')
    Invoke-Checked dotnet @('run', '--project', 'tests/localization', '-c', 'Release', '--', '.')
    Invoke-Checked dotnet @('run', '--project', 'tests/native', '-c', 'Release', "-p:GameDir=$GameDir", '--', $GameDir)

    Step 'Packaging'
    $dist = Join-Path $repo 'dist'
    $stage = Join-Path $dist 'stage'
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    $pluginDir = New-Item -ItemType Directory -Force -Path (Join-Path $stage 'BepInEx/plugins/ZhaoHuoSystem')
    Copy-Item -LiteralPath $dll -Destination $pluginDir
    Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $pluginDir
    $name = "LegendOfMortal-InGameTrainer-v$Version.zip"
    $zip = Join-Path $dist $name
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip)
    Remove-Item -LiteralPath $stage -Recurse -Force
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
    Set-Content -LiteralPath "$zip.sha256" -Value "$hash  $name" -Encoding ascii
    Write-Host "$name  SHA256 $hash  (game build $build, BepInEx $loader)"

    if (-not $Publish) { Write-Host 'Packaged only. Re-run with -Publish to tag and create the GitHub release.'; return }

    Step 'Publishing'
    $tag = "v$Version"
    if (git tag --list $tag) { throw "Tag $tag already exists." }
    Invoke-Checked git @('fetch', 'origin', 'main')
    if ((git rev-parse HEAD) -ne (git rev-parse origin/main)) { throw 'HEAD is not origin/main; push main and release from it.' }
    Invoke-Checked git @('tag', '-a', $tag, '-m', "Legend of Mortal In-Game Trainer $tag")
    Invoke-Checked git @('push', 'origin', $tag)
    $body = @"
$notes

---
适配游戏 / Game: Steam build $build
加载器 / Loader: BepInEx $loader (Unity.Mono, Windows x64)
安装：把 zip 解压到游戏目录（Mortal.exe 所在文件夹）。Install: extract the zip into the game folder (where Mortal.exe is). README: [简体中文](README.md) · [English](README.en.md) · [한국어](README.ko.md)
SHA256 ($name): ``$hash``
"@
    $notesFile = Join-Path $dist "release-notes-$Version.md"
    Set-Content -LiteralPath $notesFile -Value $body -Encoding utf8
    Invoke-Checked gh @('release', 'create', $tag, $zip, "$zip.sha256", '--title', "Legend of Mortal In-Game Trainer $tag", '--notes-file', $notesFile, '--verify-tag')
    Write-Host "Released $tag."
}
finally { Pop-Location }
