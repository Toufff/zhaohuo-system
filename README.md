# 赵活系统：《活侠传》游戏内修改器

Legend of Mortal in-game trainer · 《活俠傳》遊戲內修改器 · 《활협전》 게임 내 트레이너

<details>
<summary><b>繁體中文</b></summary>

《活俠傳》（Legend of Mortal）的遊戲內修改器，以 BepInEx 外掛的形式執行。進入遊戲後按 F8 開啟面板，可以：
- 查看這次行動會觸發哪些事件，並指定要觸發哪一個；
- 直接修改屬性和好感；
- 預先看到對話選項會帶來什麼結果；
- 指定骰子擲出的結果；
- 在戰鬥中開啟無敵、一擊必殺。

## 功能

- **事件**：每次行動前，面板會列出這次可能觸發的事件和觸發條件。條件會附上目前的數值（例如「行動次數 3>=1」），已滿足的條件顯示為綠色。
  - 選好事件後按「指定本次行動」，這次行動就會觸發它。
  - 條件還差一點時，可以按條件旁的「設為 N」，直接把數值改到滿足。
- **地圖**：地點旁會標出有幾個事件，有可以觸發的事件時，標記顯示為綠色。滑鼠移上去可以看到事件清單。
- **數值**：屬性、人物好感、門派關係、劇情標記都可以直接修改。
- **對話選項**：選項後面會用括號寫出選了會怎樣，例如屬性增減、「若…」條件分支、劇情標記、隨機事件、擲骰。
  - 綠色表示依你目前的數值會發生，紅色表示不會。
  - 滑鼠移到選項上，可以看到完整的條件和具體標記。
- **骰子**：每個結果旁都有「指定 N%」按鈕，按哪個就擲出哪個。不按的話，就是遊戲原本的隨機。
- **商店**：道具說明下方會多一行實際效果。
- **戰鬥**：無敵、一擊必殺、氣力不減、技能無冷卻，即時戰役和回合制決鬥都能用。這些開關只在戰鬥中生效，不會寫入存檔。
- **截圖**：可以一鍵隱藏模組的全部介面，也可以為它設定快捷鍵。

## 安裝

適用 Steam 版 build 25957922，載入器為 BepInEx 6.0.0-be.788（Unity.Mono，Windows x64）。

1. 在 Steam 收藏庫中對《活俠傳》按右鍵 → 管理 → 瀏覽本機檔案，開啟遊戲資料夾（有 `Mortal.exe` 的資料夾）。
2. 下載 [BepInEx-Unity.Mono-win-x64-6.0.0-be.788](https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.Mono-win-x64-6.0.0-be.788%2B5b766a3.zip)，把壓縮檔裡的全部內容解壓縮到遊戲資料夾。
3. 啟動一次遊戲，到標題畫面後退出。
4. 從 [Releases](https://github.com/Toufff/zhaohuo-system/releases) 下載最新的 `LegendOfMortal-InGameTrainer-v*.zip`，同樣解壓縮到遊戲資料夾。
5. 進入遊戲並讀取存檔，按 F8 開啟面板。

修改數值和指定事件都會寫入存檔，第一次使用前請先備份存檔。

- **Steam Deck / Linux**：在遊戲內容的啟動選項中填入 `WINEDLLOVERRIDES="winhttp=n,b" %command%`。
- **更新**：關閉遊戲，用新的 zip 覆蓋解壓縮即可。
- **解除安裝**：刪除 `BepInEx/plugins/ZhaoHuoSystem` 資料夾。如果要連 BepInEx 一起移除，再刪除遊戲資料夾裡的 `BepInEx`、`winhttp.dll`、`doorstop_config.ini`、`.doorstop_version` 和 `changelog.txt`。

## 使用

- **開啟面板**：按 F8，或點擊畫面左側的「趙活系統」小按鈕。快捷鍵可以在「設定」頁修改，小按鈕可以拖曳。
- **語言**：介面語言跟隨遊戲，支援繁體中文、簡體中文和韓文。
- **設定檔**：設定儲存在 `BepInEx/config/local.zhaohuo.system.cfg`。

## 遊戲更新之後

外掛只在適配過的遊戲版本上運作。Steam 更新遊戲後，外掛會自動停用，不會改動遊戲；等這裡發布新版本後再更新即可。

想確認是不是這個原因，可以開啟 `BepInEx/LogOutput.log`：如果裡面出現 `compatible=False`，表示目前的遊戲版本還沒有適配。

## 回報問題

請在 [Issues](https://github.com/Toufff/zhaohuo-system/issues) 中說明遊戲語言、在哪個場景出的問題，並附上 `BepInEx/LogOutput.log`。

## 從原始碼建置

需要準備：
- Windows；
- [.NET SDK 9](https://dotnet.microsoft.com/download)；
- 一份已經安裝好 BepInEx 的遊戲。

外掛在編譯時會直接參照遊戲資料夾裡的組件，不會複製這些檔案。

```powershell
# 遊戲不在預設路徑時，用 -p:GameDir 指定遊戲資料夾
dotnet build src/ZhaoHuoSystem/ZhaoHuoSystem.csproj -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\LegendOfMortal"

# 不需要遊戲的測試，CI 也會執行這兩項
dotnet run --project tests/ui-state -c Release
dotnet run --project tests/localization -c Release -- .

# 需要本機遊戲的測試
dotnet run --project tests/native -c Release -p:GameDir="D:\...\LegendOfMortal" -- "D:\...\LegendOfMortal"
```

建置結果是 `src/ZhaoHuoSystem/bin/Release/netstandard2.0/ZhaoHuoSystem.dll`，把它放進 `BepInEx/plugins/ZhaoHuoSystem/` 即可。

正式版本由維護者在裝有遊戲的電腦上用 `scripts/release.ps1` 發布。指令碼會依序核對遊戲版本、建置、執行全部測試、打包並上傳到 Releases。

## 授權

[MIT](LICENSE) © Toufff。可以自由使用、修改、再散布，也可以商用，只需保留著作權聲明。

這是玩家自製的模組，與《活俠傳》的開發商和發行商無關，不包含任何遊戲檔案。

</details>

<details>
<summary><b>English</b></summary>

An in-game trainer and cheat panel for **Legend of Mortal** (活侠传), shipped as a BepInEx plugin. Press F8 in game to:
- see which events your next action can trigger, and pick the one you want;
- edit stats and relationships;
- preview what a dialogue choice will do;
- choose dice results;
- turn on god mode and one-hit kills in combat.

The in-game panel follows the game's language and is available in Simplified Chinese, Traditional Chinese and Korean. Other game languages show Simplified Chinese.

## Features

- **Events**: before each action, the panel lists the events it can trigger and their conditions. Conditions show current values (for example, `Actions 3>=1`), and the ones you already meet are green.
  - Pick an event and press 指定本次行动 (choose for this action); this action will trigger it.
  - If you are just short of a condition, press 设为 N (set to N) next to it to change the value so the condition holds.
- **Map**: locations show how many events they hold, and the badge turns green when one of them can trigger now. Hover a badge to see the list.
- **Values**: edit attributes, character affection, sect relations and story flags directly.
- **Dialogue choices**: each option gets a short note in brackets saying what picking it does: stat changes, conditional branches, story flags, random events and dice rolls.
  - Green means it happens with your current values; red means it does not.
  - Hover the option to see the full conditions and the flags involved.
- **Dice**: each outcome gets a 指定 N% button, where N is its chance. Click one to roll that outcome. If you click nothing, the roll stays random as usual.
- **Shop**: item descriptions get one extra line with the item's actual effect.
- **Combat**: god mode, one-hit kill, no stamina cost and no skill cooldown. They work in both real-time battles and turn-based duels, apply only during a fight, and are not written to your save.
- **Screenshots**: hide everything the mod draws with one switch or a hotkey of your choice.

## Install

Supports the Steam release, build 25957922, with BepInEx 6.0.0-be.788 (Unity.Mono, Windows x64).

1. In your Steam library, right-click Legend of Mortal → Manage → Browse local files. This opens the game folder (the one with `Mortal.exe`).
2. Download [BepInEx-Unity.Mono-win-x64-6.0.0-be.788](https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.Mono-win-x64-6.0.0-be.788%2B5b766a3.zip) and extract everything into the game folder.
3. Start the game once, wait for the title screen, then quit.
4. Download the latest `LegendOfMortal-InGameTrainer-v*.zip` from [Releases](https://github.com/Toufff/zhaohuo-system/releases) and extract it into the game folder as well.
5. Start the game, load a save and press F8.

Edited values and chosen events end up in your save, so back up your saves before the first use.

- **Steam Deck / Linux**: set the launch options to `WINEDLLOVERRIDES="winhttp=n,b" %command%`.
- **Update**: close the game and extract the new zip over the old files.
- **Uninstall**: delete `BepInEx/plugins/ZhaoHuoSystem`. To remove BepInEx as well, also delete `BepInEx`, `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` and `changelog.txt` from the game folder.

## Usage

- **Open the panel**: press F8, or click the small 赵活系统 button on the left edge of the screen. You can change the hotkey on the 设置 (Settings) tab, and you can drag the button.
- **Settings file**: settings are saved to `BepInEx/config/local.zhaohuo.system.cfg`.

## After a game update

The plugin only runs on game versions it has been checked against. When Steam updates the game, the plugin switches itself off and leaves the game alone until a new release supports that version.

To confirm this is what happened, open `BepInEx/LogOutput.log`. If it contains `compatible=False`, the current game version is not supported yet.

## Reporting problems

Open an [issue](https://github.com/Toufff/zhaohuo-system/issues) with your game language, what you were doing when it happened, and your `BepInEx/LogOutput.log`.

## Building from source

You need:
- Windows;
- the [.NET 9 SDK](https://dotnet.microsoft.com/download);
- a copy of the game with BepInEx installed.

The plugin compiles against the game's own assemblies in place, and nothing from the game is copied into the repository or the build.

```powershell
# Use -p:GameDir when the game is not in the default location
dotnet build src/ZhaoHuoSystem/ZhaoHuoSystem.csproj -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\LegendOfMortal"

# Tests that need no game files (the same two run in CI)
dotnet run --project tests/ui-state -c Release
dotnet run --project tests/localization -c Release -- .

# Tests that use your local game installation
dotnet run --project tests/native -c Release -p:GameDir="D:\...\LegendOfMortal" -- "D:\...\LegendOfMortal"
```

The build output is `src/ZhaoHuoSystem/bin/Release/netstandard2.0/ZhaoHuoSystem.dll`. Copy it to `BepInEx/plugins/ZhaoHuoSystem/` to use it.

Releases are published by the maintainer with `scripts/release.ps1` on a machine that has the game installed. The script checks the game version, builds, runs every test, packages the plugin and uploads it to Releases.

## License

[MIT](LICENSE) © Toufff. You can use, modify, redistribute and sell it, as long as you keep the copyright notice.

This is a fan-made mod. It is not affiliated with the developer or publisher of Legend of Mortal, and it contains no game files.

</details>

<details>
<summary><b>한국어</b></summary>

《활협전》(Legend of Mortal, 活侠传)용 게임 내 트레이너(치트 패널)이며, BepInEx 플러그인으로 동작합니다. 게임에서 F8을 누르면 패널이 열리고 다음 기능을 쓸 수 있습니다.

- 이번 행동에서 발생할 수 있는 이벤트를 확인하고, 원하는 이벤트를 지정합니다.
- 능력치와 호감도를 직접 수정합니다.
- 대화 선택지의 결과를 미리 확인합니다.
- 주사위 결과를 지정합니다.
- 전투에서 무적, 일격필살을 켭니다.

게임 내 패널은 게임 언어를 따르며 한국어, 중국어 간체, 중국어 번체를 지원합니다.

## 기능

- **이벤트**: 행동하기 전에, 이번 행동에서 발생할 수 있는 이벤트와 발생 조건을 패널에 보여 줍니다. 조건에는 현재 값과 목표 값이 함께 표시되며, 이미 충족한 조건은 초록색으로 나타납니다.
  - 이벤트를 고른 뒤 「이번 행동 지정」을 누르면 이번 행동에서 그 이벤트가 발생합니다.
  - 조건이 조금 모자랄 때는 조건 옆의 「N(으)로 설정」을 누르면 값을 바로 맞출 수 있습니다.
- **지도**: 장소 옆에 이벤트 수가 표시되고, 지금 발생할 수 있는 이벤트가 있으면 초록색으로 바뀝니다. 마우스를 올리면 이벤트 목록을 볼 수 있습니다.
- **수치**: 능력치, 인물 호감도, 문파 관계, 스토리 플래그를 직접 수정합니다.
- **대화 선택지**: 선택지 뒤 괄호 안에 그 선택지를 고르면 일어나는 일을 짧게 적어 줍니다. 능력치 증감, 조건 분기, 스토리 플래그, 랜덤 이벤트, 주사위 판정 등이 표시됩니다.
  - 초록색은 지금 수치로 일어나는 결과, 빨간색은 일어나지 않는 결과입니다.
  - 선택지에 마우스를 올리면 전체 조건과 관련 플래그를 볼 수 있습니다.
- **주사위**: 각 결과 옆에 「지정 N%」 버튼이 있어서, 누른 결과가 그대로 나옵니다. 누르지 않으면 원래대로 무작위로 굴립니다.
- **상점**: 아이템 설명 아래에 실제 효과가 한 줄 추가됩니다.
- **전투**: 무적, 일격필살, 기력 유지, 스킬 쿨타임 없음 기능을 제공합니다. 실시간 전역과 턴제 결투 모두에서 쓸 수 있으며, 전투 중에만 적용되고 세이브에는 기록되지 않습니다.
- **스크린샷**: 모드가 표시하는 화면을 한 번에 숨길 수 있고, 단축키도 지정할 수 있습니다.

## 설치

Steam 버전 build 25957922와 BepInEx 6.0.0-be.788(Unity.Mono, Windows x64) 기준으로 만들어졌습니다.

1. Steam 라이브러리에서 《활협전》을 우클릭 → 관리 → 로컬 파일 보기를 눌러, `Mortal.exe`가 있는 게임 폴더를 엽니다.
2. [BepInEx-Unity.Mono-win-x64-6.0.0-be.788](https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.Mono-win-x64-6.0.0-be.788%2B5b766a3.zip)을 내려받아, 압축 파일 안의 내용을 모두 게임 폴더에 풉니다.
3. 게임을 한 번 실행하고, 타이틀 화면이 나오면 종료합니다.
4. [Releases](https://github.com/Toufff/zhaohuo-system/releases)에서 최신 `LegendOfMortal-InGameTrainer-v*.zip`을 내려받아 마찬가지로 게임 폴더에 풉니다.
5. 게임을 실행해 세이브를 불러온 뒤 F8을 누릅니다.

수정한 수치와 지정한 이벤트는 세이브에 반영됩니다. 처음 사용하기 전에 세이브를 백업해 두세요.

- **Steam Deck / Linux**: 게임 속성의 시작 옵션에 `WINEDLLOVERRIDES="winhttp=n,b" %command%`를 입력합니다.
- **업데이트**: 게임을 종료한 뒤 새 zip을 덮어써서 풉니다.
- **삭제**: `BepInEx/plugins/ZhaoHuoSystem` 폴더를 지웁니다. BepInEx까지 지우려면 게임 폴더의 `BepInEx`, `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, `changelog.txt`도 함께 지웁니다.

## 사용법

- **패널 열기**: F8을 누르거나, 화면 왼쪽의 작은 「조활 시스템」 버튼을 누릅니다. 단축키는 「설정」 탭에서 바꿀 수 있고, 버튼은 끌어서 옮길 수 있습니다.
- **설정 파일**: 설정은 `BepInEx/config/local.zhaohuo.system.cfg`에 저장됩니다.

## 게임이 업데이트되면

이 플러그인은 지원을 확인한 게임 버전에서만 동작합니다. Steam에서 게임이 업데이트되면 플러그인이 스스로 꺼지며, 게임에는 아무 영향도 주지 않습니다. 새 버전이 올라오면 그때 업데이트하세요.

이 경우인지 확인하려면 `BepInEx/LogOutput.log`를 열어 보세요. `compatible=False`가 있으면 현재 게임 버전은 아직 지원되지 않는 것입니다.

## 문제 신고

[Issues](https://github.com/Toufff/zhaohuo-system/issues)에 게임 언어와 문제가 생긴 상황을 적고, `BepInEx/LogOutput.log`를 첨부해 주세요.

## 소스에서 빌드하기

준비할 것:

- Windows
- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- BepInEx가 설치된 게임

플러그인은 게임 폴더에 있는 어셈블리를 그대로 참조해서 컴파일합니다. 게임 파일은 저장소나 빌드 결과물에 복사되지 않습니다.

```powershell
# 게임이 기본 경로에 없으면 -p:GameDir로 게임 폴더를 지정합니다
dotnet build src/ZhaoHuoSystem/ZhaoHuoSystem.csproj -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\LegendOfMortal"

# 게임 파일이 필요 없는 테스트 (CI에서도 이 두 개를 실행합니다)
dotnet run --project tests/ui-state -c Release
dotnet run --project tests/localization -c Release -- .

# 로컬 게임 설치가 필요한 테스트
dotnet run --project tests/native -c Release -p:GameDir="D:\...\LegendOfMortal" -- "D:\...\LegendOfMortal"
```

빌드 결과물은 `src/ZhaoHuoSystem/bin/Release/netstandard2.0/ZhaoHuoSystem.dll`이며, `BepInEx/plugins/ZhaoHuoSystem/`에 넣으면 됩니다.

정식 릴리스는 메인테이너가 게임이 설치된 PC에서 `scripts/release.ps1`로 배포합니다. 이 스크립트는 게임 버전 확인, 빌드, 전체 테스트, 패키징, Releases 업로드를 순서대로 진행합니다.

## 라이선스

[MIT](LICENSE) © Toufff. 상업적 이용을 포함해 자유롭게 사용, 수정, 재배포할 수 있습니다. 저작권 고지만 남겨 주세요.

플레이어가 만든 비공식 모드이며, 《활협전》의 개발사 및 배급사와는 관계가 없습니다. 게임 파일은 포함되어 있지 않습니다.

</details>

《活侠传》（Legend of Mortal）的游戏内修改器，以 BepInEx 插件的形式运行。进游戏后按 F8 打开面板，可以：
- 查看这次行动会触发哪些事件，并指定触发哪一个；
- 直接修改属性和好感；
- 提前看到对话选项会带来什么结果；
- 指定骰子投出的结果；
- 在战斗里开启无敌、一击必杀。

## 能做什么

- **事件**：每次行动前，面板列出这次可能触发的事件和触发条件。条件里带着当前值（例如「行动次数 3>=1」），满足的显示为绿色。
  - 选中一个事件后点「指定本次行动」，这次行动就会触发它。
  - 条件还差一点时，可以点条件旁的「设为 N」，直接把数值改到满足。
- **地图**：地点旁会标出有几个事件，有可以触发的事件时标记显示为绿色。鼠标移上去能看到事件列表。
- **数值**：属性、人物好感、门派关系、剧情标记都可以直接修改。
- **对话选项**：选项后面用括号写出选了会怎样，比如属性加减、「若…」条件分支、剧情标记、随机事件、掷骰。
  - 绿色表示按你现在的数值会发生，红色表示不会。
  - 鼠标移到选项上，可以看到完整的条件和具体标记。
- **骰子**：每个结果旁有「指定 N%」按钮，点哪个就投出哪个。不点的话，就是游戏原本的随机。
- **商店**：道具说明下面多一行实际效果。
- **战斗**：无敌、一击必杀、气力不减、技能无冷却，即时战役和回合制决斗都能用。这些开关只在战斗中生效，不写进存档。
- **截图**：可以一键隐藏模组的全部界面，也可以给它绑定快捷键。

## 安装

适配 Steam 版 build 25957922，加载器为 BepInEx 6.0.0-be.788（Unity.Mono，Windows x64）。

1. 在 Steam 库里右键《活侠传》→ 管理 → 浏览本地文件，打开游戏目录（有 `Mortal.exe` 的文件夹）。
2. 下载 [BepInEx-Unity.Mono-win-x64-6.0.0-be.788](https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.Mono-win-x64-6.0.0-be.788%2B5b766a3.zip)，把压缩包里的全部内容解压到游戏目录。
3. 启动一次游戏，到标题画面后退出。
4. 从 [Releases](https://github.com/Toufff/zhaohuo-system/releases) 下载最新的 `LegendOfMortal-InGameTrainer-v*.zip`，也解压到游戏目录。
5. 进入游戏并读档，按 F8 打开面板。

修改数值和指定事件都会写进存档，第一次使用前请先备份存档。

- **Steam Deck / Linux**：在游戏的启动选项里填 `WINEDLLOVERRIDES="winhttp=n,b" %command%`。
- **更新**：关掉游戏，用新的 zip 覆盖解压即可。
- **卸载**：删除 `BepInEx/plugins/ZhaoHuoSystem` 文件夹。如果要把 BepInEx 也删掉，再删除游戏目录里的 `BepInEx`、`winhttp.dll`、`doorstop_config.ini`、`.doorstop_version` 和 `changelog.txt`。

## 使用

- **打开面板**：按 F8，或点击屏幕左侧的「赵活系统」小按钮。快捷键可以在「设置」页修改，小按钮可以拖动。
- **语言**：界面语言跟随游戏，支持简体中文、繁体中文和韩语。
- **配置文件**：设置保存在 `BepInEx/config/local.zhaohuo.system.cfg`。

## 游戏更新以后

插件只在适配过的游戏版本上工作。Steam 更新游戏后，插件会自动停用，不会改动游戏；等这里发布新版本后再更新即可。

想确认是不是这个原因，可以打开 `BepInEx/LogOutput.log`：如果里面出现 `compatible=False`，说明当前游戏版本还没有适配。

## 反馈问题

请在 [Issues](https://github.com/Toufff/zhaohuo-system/issues) 里说明游戏语言、在哪个场景出的问题，并附上 `BepInEx/LogOutput.log`。

## 从源码构建

需要准备：
- Windows；
- [.NET SDK 9](https://dotnet.microsoft.com/download)；
- 一份已经装好 BepInEx 的游戏。

插件编译时直接引用游戏目录里的程序集，不会复制这些文件。

```powershell
# 游戏不在默认路径时，用 -p:GameDir 指定游戏目录
dotnet build src/ZhaoHuoSystem/ZhaoHuoSystem.csproj -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\LegendOfMortal"

# 不需要游戏的测试，CI 也跑这两项
dotnet run --project tests/ui-state -c Release
dotnet run --project tests/localization -c Release -- .

# 需要本机游戏的测试
dotnet run --project tests/native -c Release -p:GameDir="D:\...\LegendOfMortal" -- "D:\...\LegendOfMortal"
```

构建结果是 `src/ZhaoHuoSystem/bin/Release/netstandard2.0/ZhaoHuoSystem.dll`，把它放进 `BepInEx/plugins/ZhaoHuoSystem/` 即可。

正式版本由维护者在装有游戏的电脑上用 `scripts/release.ps1` 发布。脚本会依次核对游戏版本、构建、运行全部测试、打包并上传到 Releases。

## 许可证

[MIT](LICENSE) © Toufff。可以自由使用、修改、再发布，也可以商用，只需保留版权声明。

这是玩家自制的模组，与《活侠传》的开发商和发行商无关，不包含任何游戏文件。
