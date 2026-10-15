# 原生纯逻辑与 Lua 等待测试

此项目直接链接仓库的 `LuaGate.cs`，使用自建合成 Lua，不读取或复制游戏剧情。词法用例覆盖字符串 / 注释、边界、多个静态调用、动态调用与重复处理；FixedEffects 回归覆盖字面量签名、分支与短路条件、mode、动态参数和成员边界。运行用例直接加载当前本机 `Fungus.dll` 内的 MoonSharp，验证主线程不等待、协程暂停 / 解除与原表达式仅执行一次。

```powershell
dotnet run --project tests/native/ZhaoHuoSystem.NativeTests.csproj -c Release
# 非默认游戏路径时，编译与运行必须指向同一个安装
dotnet run --project tests/native/ZhaoHuoSystem.NativeTests.csproj -c Release -p:GameDir='D:\SteamLibrary\steamapps\common\LegendOfMortal' -- 'D:\SteamLibrary\steamapps\common\LegendOfMortal'
```

游戏程序集通过原位引用与运行时加载使用，不复制进项目或构建输出。实际 MoonSharp 无法加载时以失败退出，不能把词法通过当成协程通过。此项目不验证 Unity 输入、真实游戏调度或真实事件消费，这些仍需游戏内验收。
