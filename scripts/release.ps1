#Requires -Version 7
<#
.SYNOPSIS
  Builds, tests and packages a release on a machine with the game installed, and optionally publishes it.

.DESCRIPTION
  The plugin compiles against the installed game's assemblies, which are never stored in the repository,
  so releases are built here rather than on GitHub Actions. The script refuses to continue when:
    - the working tree has uncommitted changes;
    - the version in ZhaoHuoSystem.csproj, Plugin.cs or CHANGELOG.md differs from -Version;
    - the build or any test fails.
  It only warns when the installed game's assemblies differ from the fingerprints recorded in Plugin.cs
  (the build the plugin was developed on); the plugin itself keeps running on other builds.
  Without -Publish it only writes dist/ZhaoHuoSystem-v<Version>.zip (built by package.ps1: plugin, one-click
  installer and the bundled official BepInEx) and its .sha256 file, plus dist/ZhaoHuoSystem-v<Version>-manual.zip,
  the script-free package for Nexus Mods (uploaded there by hand, not attached to the GitHub release).
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
    foreach ($fingerprint in $fingerprints) {
        $file = $fingerprint.Groups['file'].Value
        $actual = (Get-FileHash -LiteralPath (Join-Path $GameDir "Mortal_Data\Managed\$file") -Algorithm SHA256).Hash
        if ($actual -ne $fingerprint.Groups['hash'].Value) { Write-Warning "$file differs from the build recorded in Plugin.cs; building against the installed game anyway." }
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
    $package = & (Join-Path $PSScriptRoot 'package.ps1') -Version $Version -Dll $dll -OutDir $dist
    $name = $package.Name; $zip = $package.Zip; $hash = $package.Sha256
    Write-Host "$name  SHA256 $hash  (game build $build, BepInEx $loader)"
    $manual = & (Join-Path $PSScriptRoot 'package.ps1') -Version $Version -Dll $dll -OutDir $dist -Manual
    Write-Host "$($manual.Name)  SHA256 $($manual.Sha256)  (for Nexus Mods)"

    if (-not $Publish) { Write-Host 'Packaged only. Re-run with -Publish to tag and create the GitHub release.'; return }

    Step 'Publishing'
    $tag = "v$Version"
    if (git tag --list $tag) { throw "Tag $tag already exists." }
    Invoke-Checked git @('fetch', 'origin', 'main')
    if ((git rev-parse HEAD) -ne (git rev-parse origin/main)) { throw 'HEAD is not origin/main; push main and release from it.' }
    Invoke-Checked git @('tag', '-a', $tag, '-m', "ZhaoHuoSystem $tag")
    Invoke-Checked git @('push', 'origin', $tag)
    $readme = (gh repo view --json url --jq .url) + '#readme'
    $body = @"
$notes

---
开发测试 / Built on: Steam build $build
加载器 / Loader: BepInEx $loader (Unity.Mono, Windows x64)
安装：解压后双击 install.bat，卸载双击 uninstall.bat。Install: extract the zip and double-click install.bat; uninstall.bat removes it. README（简体中文 · 繁體中文 · English · 한국어）: $readme
SHA256 ($name): ``$hash``
"@
    $notesFile = Join-Path $dist "release-notes-$Version.md"
    Set-Content -LiteralPath $notesFile -Value $body -Encoding utf8
    Invoke-Checked gh @('release', 'create', $tag, $zip, "$zip.sha256", '--title', "ZhaoHuoSystem $tag", '--notes-file', $notesFile, '--verify-tag')
    Write-Host "Released $tag."
}
finally { Pop-Location }
