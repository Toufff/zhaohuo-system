# ZhaoHuo System: in-game trainer for Legend of Mortal

[简体中文](README.md) | English | [한국어](README.ko.md)

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
4. Download the latest `LegendOfMortal-InGameTrainer-v*.zip` from [Releases](../../releases) and extract it into the game folder as well.
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

Open an [issue](../../issues) with your game language, what you were doing when it happened, and your `BepInEx/LogOutput.log`.

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
