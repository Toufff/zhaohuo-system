<#
  One-click install / uninstall for ZhaoHuoSystem, started by install.bat or uninstall.bat.
  Runs on the Windows PowerShell 5.1 that ships with Windows. It only writes inside a folder
  that contains Mortal.exe, and never touches saves (they live under AppData, not the game folder).

  -Action detect  only print the game folder that would be used; changes nothing
  -GameDir  use this game folder instead of finding it
  -Yes      uninstall: also remove BepInEx without asking
  -NoPause  do not wait for a key at the end (for scripted runs)
#>
param(
    [ValidateSet('install', 'uninstall', 'detect')][string]$Action = 'install',
    [string]$GameDir = '',
    [switch]$Yes,
    [switch]$NoPause
)
$ErrorActionPreference = 'Stop'
$AppId = '1859910'
$BepInExZip = 'BepInEx-Unity.Mono-win-x64-6.0.0-be.788+5b766a3.zip'
$BepInExSha256 = 'D7455185E8A0A01EB122947C59229F3FFCCEED853FEBD4707C08219743D8F477'
$BepInExFiles = @('BepInEx', 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version', 'changelog.txt')
$PackageRoot = Split-Path -Parent $PSScriptRoot

$culture = (Get-UICulture).Name
$lang = if ($culture -match '^zh-(TW|HK|MO|Hant)') { 'zht' } elseif ($culture -like 'zh*') { 'zhs' } elseif ($culture -like 'ko*') { 'ko' } else { 'en' }
$Text = @{
    zhs = @{
        Searching = '正在查找《活侠传》游戏目录…'; Found = '游戏目录：{0}'
        Pick = '没有自动找到游戏。请在弹出的窗口里选择《活侠传》的游戏文件夹（有 Mortal.exe 的那个）。'
        PickTitle = '选择《活侠传》游戏文件夹（有 Mortal.exe 的文件夹）'; NotGame = '这个文件夹里没有 Mortal.exe，请重新选择。'
        Cancelled = '已取消，没有做任何修改。'; Running = '游戏正在运行。请先关闭《活侠传》，然后按回车继续（输入 q 退出）'
        BepInExOk = 'BepInEx 已安装，保留现有版本。'; BepInExInstall = '正在安装 BepInEx 6.0.0-be.788…'
        BepInExOther = '游戏目录里已有其他版本的 BepInEx（BepInEx 5 或 IL2CPP 版），本插件需要 BepInEx 6 Unity.Mono。请先移除旧版本，或按 README 手动安装。'
        BadZip = '安装包里的 BepInEx 文件不完整，请重新下载安装包。'; PluginInstall = '正在安装赵活系统插件…'
        Installed = '安装完成。启动游戏并读档后，按 F8 打开面板。'
        Removed = '已删除赵活系统插件和它的设置。'; NotInstalled = '没有找到已安装的赵活系统插件。'
        AskBepInEx = '是否同时删除 BepInEx？其他 BepInEx 模组也会一起失效。输入 y 删除，直接回车保留'
        OtherPlugins = 'BepInEx/plugins 里还有其他模组：{0}'
        BepInExRemoved = '已删除 BepInEx。'; BepInExKept = '已保留 BepInEx。'; Saves = '存档不受影响。'
        Failed = '出错了：{0}'; PressKey = '按回车键关闭窗口'
    }
    zht = @{
        Searching = '正在尋找《活俠傳》遊戲資料夾…'; Found = '遊戲資料夾：{0}'
        Pick = '沒有自動找到遊戲。請在跳出的視窗中選擇《活俠傳》的遊戲資料夾（有 Mortal.exe 的那個）。'
        PickTitle = '選擇《活俠傳》遊戲資料夾（有 Mortal.exe 的資料夾）'; NotGame = '這個資料夾裡沒有 Mortal.exe，請重新選擇。'
        Cancelled = '已取消，沒有做任何修改。'; Running = '遊戲正在執行。請先關閉《活俠傳》，然後按 Enter 繼續（輸入 q 結束）'
        BepInExOk = 'BepInEx 已安裝，保留現有版本。'; BepInExInstall = '正在安裝 BepInEx 6.0.0-be.788…'
        BepInExOther = '遊戲資料夾裡已有其他版本的 BepInEx（BepInEx 5 或 IL2CPP 版），本外掛需要 BepInEx 6 Unity.Mono。請先移除舊版本，或依 README 手動安裝。'
        BadZip = '安裝包裡的 BepInEx 檔案不完整，請重新下載安裝包。'; PluginInstall = '正在安裝趙活系統外掛…'
        Installed = '安裝完成。啟動遊戲並讀取存檔後，按 F8 開啟面板。'
        Removed = '已刪除趙活系統外掛和它的設定。'; NotInstalled = '沒有找到已安裝的趙活系統外掛。'
        AskBepInEx = '是否一併刪除 BepInEx？其他 BepInEx 模組也會一起失效。輸入 y 刪除，直接按 Enter 保留'
        OtherPlugins = 'BepInEx/plugins 裡還有其他模組：{0}'
        BepInExRemoved = '已刪除 BepInEx。'; BepInExKept = '已保留 BepInEx。'; Saves = '存檔不受影響。'
        Failed = '發生錯誤：{0}'; PressKey = '按 Enter 鍵關閉視窗'
    }
    ko = @{
        Searching = '《활협전》 게임 폴더를 찾는 중…'; Found = '게임 폴더: {0}'
        Pick = '게임을 자동으로 찾지 못했습니다. 열리는 창에서 《활협전》 게임 폴더(Mortal.exe가 있는 폴더)를 선택하세요.'
        PickTitle = '《활협전》 게임 폴더 선택 (Mortal.exe가 있는 폴더)'; NotGame = '이 폴더에는 Mortal.exe가 없습니다. 다시 선택하세요.'
        Cancelled = '취소했습니다. 아무것도 바꾸지 않았습니다.'; Running = '게임이 실행 중입니다. 《활협전》을 종료한 뒤 Enter를 누르세요 (q 입력 시 종료)'
        BepInExOk = 'BepInEx가 이미 설치되어 있어 그대로 사용합니다.'; BepInExInstall = 'BepInEx 6.0.0-be.788 설치 중…'
        BepInExOther = '게임 폴더에 다른 버전의 BepInEx(BepInEx 5 또는 IL2CPP)가 있습니다. 이 플러그인은 BepInEx 6 Unity.Mono가 필요합니다. 기존 버전을 지우거나 README를 따라 수동으로 설치하세요.'
        BadZip = '설치 파일 안의 BepInEx가 손상되었습니다. 설치 파일을 다시 내려받으세요.'; PluginInstall = '조활 시스템 플러그인 설치 중…'
        Installed = '설치를 마쳤습니다. 게임을 실행해 세이브를 불러온 뒤 F8을 누르세요.'
        Removed = '조활 시스템 플러그인과 설정을 삭제했습니다.'; NotInstalled = '설치된 조활 시스템 플러그인을 찾지 못했습니다.'
        AskBepInEx = 'BepInEx도 함께 삭제할까요? 다른 BepInEx 모드도 함께 동작하지 않게 됩니다. 삭제하려면 y, 유지하려면 Enter'
        OtherPlugins = 'BepInEx/plugins에 다른 모드가 있습니다: {0}'
        BepInExRemoved = 'BepInEx를 삭제했습니다.'; BepInExKept = 'BepInEx를 유지했습니다.'; Saves = '세이브 파일에는 영향이 없습니다.'
        Failed = '오류가 발생했습니다: {0}'; PressKey = 'Enter를 누르면 창이 닫힙니다'
    }
    en = @{
        Searching = 'Looking for the Legend of Mortal game folder...'; Found = 'Game folder: {0}'
        Pick = 'The game was not found automatically. Choose the Legend of Mortal game folder (the one with Mortal.exe) in the window that opens.'
        PickTitle = 'Choose the Legend of Mortal game folder (the one with Mortal.exe)'; NotGame = 'There is no Mortal.exe in that folder. Please choose again.'
        Cancelled = 'Cancelled. Nothing was changed.'; Running = 'The game is running. Close Legend of Mortal, then press Enter to continue (type q to quit)'
        BepInExOk = 'BepInEx is already installed and is kept as it is.'; BepInExInstall = 'Installing BepInEx 6.0.0-be.788...'
        BepInExOther = 'The game folder has a different BepInEx (BepInEx 5 or the IL2CPP build). This plugin needs BepInEx 6 Unity.Mono. Remove the old one first, or install by hand as described in the README.'
        BadZip = 'The bundled BepInEx file is damaged. Please download the package again.'; PluginInstall = 'Installing the ZhaoHuo System plugin...'
        Installed = 'Done. Start the game, load a save and press F8.'
        Removed = 'Removed the ZhaoHuo System plugin and its settings.'; NotInstalled = 'No installed ZhaoHuo System plugin was found.'
        AskBepInEx = 'Also remove BepInEx? Other BepInEx mods will stop working too. Type y to remove it, or press Enter to keep it'
        OtherPlugins = 'Other mods in BepInEx/plugins: {0}'
        BepInExRemoved = 'Removed BepInEx.'; BepInExKept = 'Kept BepInEx.'; Saves = 'Your saves are not affected.'
        Failed = 'Something went wrong: {0}'; PressKey = 'Press Enter to close this window'
    }
}[$lang]
function Say([string]$key, [object[]]$values = @()) { Write-Host ($Text[$key] -f $values) }

# .NET instead of Get-FileHash, which fails to load when PowerShell 7's module path leaks into Windows PowerShell.
function Get-Sha256([string]$path) {
    $sha = [Security.Cryptography.SHA256]::Create(); $stream = [IO.File]::OpenRead($path)
    try { -join ($sha.ComputeHash($stream) | ForEach-Object { $_.ToString('X2') }) } finally { $stream.Dispose(); $sha.Dispose() }
}

function Test-Game([string]$dir) { $dir -and (Test-Path -LiteralPath (Join-Path $dir 'Mortal.exe') -PathType Leaf) }

function Find-SteamGame {
    $roots = @()
    foreach ($key in @('HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam')) {
        $item = Get-ItemProperty -Path $key -ErrorAction SilentlyContinue
        foreach ($name in @('SteamPath', 'InstallPath')) { if ($item -and $item.$name) { $roots += ($item.$name -replace '/', '\') } }
    }
    $libraries = @()
    foreach ($root in ($roots | Select-Object -Unique)) {
        $libraries += $root
        $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $vdf) {
            foreach ($match in [regex]::Matches((Get-Content -LiteralPath $vdf -Raw), '"path"\s+"([^"]+)"')) { $libraries += ($match.Groups[1].Value -replace '\\\\', '\') }
        }
    }
    foreach ($library in ($libraries | Select-Object -Unique)) {
        $manifest = Join-Path $library "steamapps\appmanifest_$AppId.acf"
        if (-not (Test-Path -LiteralPath $manifest)) { continue }
        $installDir = [regex]::Match((Get-Content -LiteralPath $manifest -Raw), '"installdir"\s+"([^"]+)"').Groups[1].Value
        if ($installDir) {
            $dir = Join-Path $library "steamapps\common\$installDir"
            if (Test-Game $dir) { return $dir }
        }
    }
    return $null
}

function Select-GameFolder {
    Add-Type -AssemblyName System.Windows.Forms
    Say 'Pick'
    while ($true) {
        $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
        $dialog.Description = $Text.PickTitle
        $dialog.ShowNewFolderButton = $false
        if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { return $null }
        if (Test-Game $dialog.SelectedPath) { return $dialog.SelectedPath }
        Say 'NotGame'
    }
}

function Find-Game {
    if ($GameDir) { if (Test-Game $GameDir) { return (Resolve-Path -LiteralPath $GameDir).Path } else { throw ($Text.NotGame) } }
    # The package may have been extracted straight into the game folder.
    foreach ($dir in @($PackageRoot, (Split-Path -Parent $PackageRoot))) { if (Test-Game $dir) { return $dir } }
    Say 'Searching'
    $found = Find-SteamGame
    if ($found) { return $found }
    return Select-GameFolder
}

# A Mortal process counts when it runs from this game folder (or its path cannot be read).
function Test-GameRunning([string]$game) {
    [bool](Get-Process -Name 'Mortal' -ErrorAction SilentlyContinue | Where-Object { -not $_.Path -or $_.Path.StartsWith($game + '\', [StringComparison]::OrdinalIgnoreCase) })
}
function Wait-GameClosed([string]$game) {
    while (Test-GameRunning $game) {
        $answer = Read-Host $Text.Running
        if ($answer -eq 'q') { return $false }
    }
    return $true
}

# Only ever removes paths inside the confirmed game folder.
function Remove-InGame([string]$game, [string]$relative) {
    $target = [IO.Path]::GetFullPath((Join-Path $game $relative))
    if (-not $target.StartsWith($game.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Refusing to remove $target" }
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force; return $true }
    return $false
}

function Install-Mod([string]$game) {
    $core = Join-Path $game 'BepInEx\core'
    if ((Test-Path -LiteralPath (Join-Path $core 'BepInEx.dll')) -or (Test-Path -LiteralPath (Join-Path $core 'BepInEx.Unity.IL2CPP.dll'))) { throw ($Text.BepInExOther) }
    if ((Test-Path -LiteralPath (Join-Path $core 'BepInEx.Unity.Mono.dll')) -and (Test-Path -LiteralPath (Join-Path $game 'winhttp.dll'))) { Say 'BepInExOk' }
    else {
        Say 'BepInExInstall'
        $zip = Join-Path $PSScriptRoot $BepInExZip
        if (-not (Test-Path -LiteralPath $zip) -or (Get-Sha256 $zip) -ne $BepInExSha256) { throw ($Text.BadZip) }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [IO.Compression.ZipFile]::OpenRead($zip)
        try {
            foreach ($entry in $archive.Entries) {
                if (-not $entry.Name) { continue }
                $target = [IO.Path]::GetFullPath((Join-Path $game $entry.FullName))
                if (-not $target.StartsWith($game.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw ($Text.BadZip) }
                New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
            }
        }
        finally { $archive.Dispose() }
    }
    Say 'PluginInstall'
    $source = Join-Path $PackageRoot 'BepInEx\plugins\ZhaoHuoSystem'
    $destination = Join-Path $game 'BepInEx\plugins\ZhaoHuoSystem'
    if ([IO.Path]::GetFullPath($source).TrimEnd('\') -ne [IO.Path]::GetFullPath($destination).TrimEnd('\')) {
        New-Item -ItemType Directory -Force -Path $destination | Out-Null
        Copy-Item -Path (Join-Path $source '*') -Destination $destination -Force
    }
    if (-not (Test-Path -LiteralPath (Join-Path $destination 'ZhaoHuoSystem.dll'))) { throw 'ZhaoHuoSystem.dll was not copied.' }
    Say 'Installed'
}

function Uninstall-Mod([string]$game) {
    $removed = Remove-InGame $game 'BepInEx\plugins\ZhaoHuoSystem'
    [void](Remove-InGame $game 'BepInEx\config\local.zhaohuo.system.cfg')
    [void](Remove-InGame $game 'BepInEx\ZhaoHuoSystem')
    if ($removed) { Say 'Removed' } else { Say 'NotInstalled' }
    if (Test-Path -LiteralPath (Join-Path $game 'BepInEx')) {
        $plugins = Join-Path $game 'BepInEx\plugins'
        $others = @(if (Test-Path -LiteralPath $plugins) { Get-ChildItem -LiteralPath $plugins | ForEach-Object Name })
        if ($others.Count -gt 0) { Say 'OtherPlugins' @($others -join ', ') }
        $remove = $Yes -or ((Read-Host $Text.AskBepInEx) -match '^\s*y')
        if ($remove) {
            foreach ($name in $BepInExFiles) {
                # changelog.txt is removed only when it is BepInEx's own.
                if ($name -eq 'changelog.txt') {
                    $file = Join-Path $game $name
                    if (-not (Test-Path -LiteralPath $file) -or (Get-Content -LiteralPath $file -TotalCount 1) -notmatch 'changes since v\d') { continue }
                }
                [void](Remove-InGame $game $name)
            }
            Say 'BepInExRemoved'
        }
        else { Say 'BepInExKept' }
    }
    Say 'Saves'
}

$exitCode = 0
try {
    $game = Find-Game
    if (-not $game) { Say 'Cancelled' }
    else {
        $game = [IO.Path]::GetFullPath($game).TrimEnd('\')
        Say 'Found' @($game)
        if ($Action -eq 'detect') { }
        elseif (-not (Wait-GameClosed $game)) { Say 'Cancelled' }
        elseif ($Action -eq 'install') { Install-Mod $game }
        else { Uninstall-Mod $game }
    }
}
catch { Say 'Failed' @($_.Exception.Message); $exitCode = 1 }
if (-not $NoPause) { [void](Read-Host $Text.PressKey) }
exit $exitCode
