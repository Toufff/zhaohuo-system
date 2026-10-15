# 赵活系统

《活侠传》（Legend of Mortal）的游戏内辅助面板，以 BepInEx 插件形式运行。能看到当前行动可能触发的事件及其条件、指定本次结果、查看和修改数值、提前看到对话选项的后果，并提供战斗开关。

> 非官方模组，与《活侠传》的开发商、发行商无关。仓库和发布包不包含任何游戏文件。修改数值、指定事件会改变你的存档进程，使用前请自行备份存档。

## 功能

- **事件**：列出当前地点、当前行动会进入的事件池，以及每个事件的触发条件。条件写出当前值并用红绿标示，例如「行动次数 3>=1」；条件全部满足的事件绿色突出。
  - 「指定本次行动」：让这次行动触发你选的事件。
  - 「行动前选择结果」（默认开启）：行动到达事件判定前先停下，等你选择；收起面板或点「继续本次行动」就按游戏原本的随机结果继续。
  - 「当前地图」：查看本地图各地点的事件。地图上的地点旁会显示事件数，鼠标悬停可以看到列表。
- **数值**：查看并修改属性、人物关系、门派关系、标记等，写入后会读回确认。
- **对话与休憩选项**：选项后面用括号概述选择的后果，比如属性变化、「若…」条件分支、标记数量、随机事件、掷骰、后续选择等，红绿表示此刻是否会发生。鼠标悬停可以看到每个条件的全文，以及它带来的影响和具体标记。悬停会进入随机事件的选项时，面板会预览可能出现的事件。
- **骰子**：骰子界面左侧有各档位的「指定 N%」按钮，点一下就直接投出该档；不点则完全按游戏原本的方式投掷。
- **商店**：道具描述后面附一行实际效果。
- **战斗**：无敌、一击必杀、气力不减、技能无冷却四个开关，分别作用于即时的战役和回合制的决斗。这些开关只改战斗中的伤害和消耗，不写入存档；剧情安排的扣血和保底血量照常生效。
- **设置**：可以修改开关面板的快捷键；「隐藏全部悬浮窗」开启后不显示任何模组内容，方便截图，也可以给它绑定一个快捷键。
- 界面语言跟随游戏，支持简体中文、繁体中文和韩语。

## 适配版本

| 项目 | 版本 |
| --- | --- |
| 游戏 | Steam 版《活侠传》，build 25957922 |
| 加载器 | BepInEx 6.0.0-be.788（Unity.Mono，Windows x64） |

插件启动时会核对游戏程序集的指纹。游戏更新后，指纹对不上，插件会自动停用，不会改动游戏，等发布新版本后更新即可。实际状态可以在 `BepInEx/LogOutput.log` 里查看 `compatible=True/False`。

## 安装

1. 找到游戏目录（`Mortal.exe` 所在的文件夹）：在 Steam 库中右键《活侠传》→ 管理 → 浏览本地文件。
2. 下载 [BepInEx-Unity.Mono-win-x64-6.0.0-be.788](https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.Mono-win-x64-6.0.0-be.788%2B5b766a3.zip)，把压缩包里的全部内容解压到游戏目录。解压后目录里会多出 `BepInEx` 文件夹和 `winhttp.dll`。
3. 先启动一次游戏，等进入标题画面后退出，让 BepInEx 完成初始化。
4. 从 [Releases](../../releases) 下载最新的 `ZhaoHuoSystem-v*.zip`，同样解压到游戏目录。插件位置应为 `BepInEx/plugins/ZhaoHuoSystem/ZhaoHuoSystem.dll`。
5. 启动游戏，载入存档后按 **F8**，或点击屏幕左侧的「赵活系统」小按钮打开面板。

更新插件时，先关闭游戏，再用新的 zip 覆盖解压即可。

Steam Deck 或 Linux（Proton）：在游戏属性的启动选项里填 `WINEDLLOVERRIDES="winhttp=n,b" %command%`。

## 使用

- **F8**：打开 / 收起面板（可在「设置」中修改或清除）。左侧的「赵活系统」小按钮可以拖动。
- 面板有四个页签：事件、数值、战斗、设置。
- 设置保存在 `BepInEx/config/local.zhaohuo.system.cfg`。

## 卸载

- 只删插件：删除 `BepInEx/plugins/ZhaoHuoSystem` 文件夹。
- 连 BepInEx 一起删：删除游戏目录里的 `BepInEx` 文件夹、`winhttp.dll`、`doorstop_config.ini`、`.doorstop_version` 和 `changelog.txt`。

## 问题反馈

请在 [Issues](../../issues) 中写明游戏语言、出问题的场景，并附上 `BepInEx/LogOutput.log`。

## 从源码构建

需要 Windows、[.NET SDK 9](https://dotnet.microsoft.com/download)，以及一份装好 BepInEx 的游戏。插件编译时会直接引用游戏目录里的程序集，不会复制它们。

```powershell
# 游戏不在默认路径时，用 -p:GameDir 指定游戏目录
dotnet build src/ZhaoHuoSystem/ZhaoHuoSystem.csproj -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\LegendOfMortal"

# 不依赖游戏的测试（CI 也跑这两项）
dotnet run --project tests/ui-state -c Release
dotnet run --project tests/localization -c Release -- .

# 需要本机游戏的测试：用游戏自带程序集核对补丁目标，并运行真实 MoonSharp
dotnet run --project tests/native -c Release -p:GameDir="D:\...\LegendOfMortal" -- "D:\...\LegendOfMortal"
```

构建产物为 `src/ZhaoHuoSystem/bin/Release/netstandard2.0/ZhaoHuoSystem.dll`，复制到 `BepInEx/plugins/ZhaoHuoSystem/` 即可。

发布由维护者在装有游戏的电脑上执行 `scripts/release.ps1`（详见脚本开头的说明）：脚本会核对游戏指纹，然后构建、运行全部测试、打包 zip，并创建 GitHub Release。

## 许可证

[MIT](LICENSE) © Toufff。可以自由使用、修改、再发布和商用，但需在副本中保留版权声明和许可文本。

《活侠传》及相关名称、内容的权利归其权利人所有。
