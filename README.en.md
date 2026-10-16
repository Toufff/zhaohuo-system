# ZhaoHuo System: in-game trainer for Legend of Mortal

[简体中文](README.md) | [繁體中文](README.zh-Hant.md) | English | [한국어](README.ko.md)

An in-game trainer and cheat panel for **Legend of Mortal** (活侠传), shipped as a BepInEx plugin. Press F8 in game to:
- see which events your next action can trigger, and pick the one you want;
- edit stats and relationships;
- preview what a dialogue choice will do;
- choose dice results;
- turn on god mode and one-hit kills in combat.

The in-game panel follows the game's language and is available in Simplified Chinese, Traditional Chinese and Korean.

![Seeing which events this action can trigger and choosing one in the panel](media/event-choose.webp)

## Features

- **Events**: before each action, the panel lists the events it can trigger and their conditions. Conditions show current values (for example, `Actions 3>=1`), and the ones you already meet are green.
  - Pick an event and press 指定本次行动 (choose for this action); this action will trigger it.
  - If you are just short of a condition, press 设为 N (set to N) next to it to change the value so the condition holds.
- **Map**: locations show how many events they hold, and the badge turns green when one of them can trigger now. Hover a badge to see the list.

  <img src="media/map-hover.webp" width="420" alt="Event count badges on the map and the hover list">

- **Values**: edit attributes, character affection, sect relations and story flags directly.

  <img src="media/values-tab.webp" width="560" alt="The Values tab">

- **Dialogue choices**: each option gets a short note in brackets saying what picking it does: stat changes, conditional branches, story flags, random events and dice rolls.
  - Colours mean the same thing everywhere in the mod: green is a condition that holds, a value that goes up or an item gained; red is a condition that fails, a value that goes down or an item lost.
  - Hover the option to see the full conditions and the flags involved.

  <img src="media/options.webp" width="560" alt="Consequence notes after options and the hover details">

- **Story branches**: when the story branches on a flag or condition, a small box appears at a fixed spot on the left of the dialogue box. It lists each branch's condition with current values and marks the branch taken, and it pops and flashes its title whenever it appears or updates. Drag it anywhere or close it; at the next branch it comes back to that spot.

  <img src="media/branch.webp" width="620" alt="The story branch box">

- **Dice**: each outcome gets a 指定 N% button, where N is its natural chance. Click one to roll that outcome. Outcomes your bonuses make impossible (shown as 0%) can be picked too: the dice face is worked back from the total, so the shown sum still adds up. If you click nothing, the roll stays random as usual.

  <img src="media/dice.webp" width="520" alt="Clicking 指定 50% makes the wheel stop on that outcome">

- **Shop**: item descriptions get one extra line with the item's actual effect.

  <img src="media/shop.webp" width="560" alt="The effect line under an item description">

- **Combat**: god mode, one-hit kill, no stamina cost and no skill cooldown. They work in both real-time battles and turn-based duels, apply only during a fight, and are not written to your save.

  <img src="media/battle-tab.webp" width="560" alt="Combat switches">

- **Screenshots**: hide everything the mod draws with one switch or a hotkey of your choice.

## Install

Supports the Steam release, build 25957922.

1. Download the latest `ZhaoHuoSystem-v*.zip` from [Releases](https://github.com/Toufff/zhaohuo-system/releases) and extract it to any folder.
2. Close the game and double-click `install.bat`.
   - It finds Legend of Mortal in your Steam library by itself. If it can't, a window opens so you can choose the game folder (the one with `Mortal.exe`).
   - If the game doesn't have BepInEx yet, it also installs the official BepInEx 6.0.0-be.788 (Unity.Mono, Windows x64) that comes in the zip.
3. Start the game, load a save and press F8.

Edited values and chosen events end up in your save, so back up your saves before the first use.

- **Update**: download the new version and double-click `install.bat` again.
- **Uninstall**: double-click `uninstall.bat`. It removes the plugin and its settings, then asks whether to remove BepInEx too. Other BepInEx mods need it, so pressing Enter keeps it. Your saves are not affected.
- If Windows shows a security prompt, confirm that you want to run it.

### Manual install

1. In your Steam library, right-click Legend of Mortal → Manage → Browse local files. This opens the game folder (the one with `Mortal.exe`).
2. Download [BepInEx-Unity.Mono-win-x64-6.0.0-be.788](https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.Mono-win-x64-6.0.0-be.788%2B5b766a3.zip) and extract everything into the game folder.
3. Extract `ZhaoHuoSystem-v*.zip` into the game folder as well. The plugin ends up in `BepInEx/plugins/ZhaoHuoSystem/`.
4. Start the game, load a save and press F8.

- **Steam Deck / Linux**: install by hand, and set the game's launch options to `WINEDLLOVERRIDES="winhttp=n,b" %command%`.
- **Manual uninstall**: delete `BepInEx/plugins/ZhaoHuoSystem`. To remove BepInEx as well, also delete `BepInEx`, `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` and `changelog.txt` from the game folder.

## Usage

- **Open the panel**: press F8, or click the small 赵活系统 button on the left edge of the screen. You can change the hotkey on the 设置 (Settings) tab, and you can drag the button.
- **Settings file**: settings are saved to `BepInEx/config/local.zhaohuo.system.cfg`.

## After a game update

The plugin only runs on game versions it has been checked against. When Steam updates the game, the plugin switches itself off and leaves the game alone until a new release supports that version.

To confirm this is what happened, open `BepInEx/LogOutput.log`. If it contains `compatible=False`, the current game version is not supported yet.

## Reporting problems

Open an [issue](https://github.com/Toufff/zhaohuo-system/issues) with your game language, what you were doing and roughly when it happened. Then press 导出诊断 (export diagnostics) on the panel's 设置 (settings) tab, zip the folder it creates and attach it; its path is copied to your clipboard.

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

## License

[MIT](LICENSE) © Toufff. You can use, modify, redistribute and sell it, as long as you keep the copyright notice.

This is a fan-made mod. It is not affiliated with the developer or publisher of Legend of Mortal, and it contains no game files.
