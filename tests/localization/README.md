# 本地化纯逻辑检查

在仓库根目录运行：

```powershell
dotnet run --project tests/localization/ZhaoHuoSystem.LocalizationTests.csproj -- .
```

使用真实 `Localization.cs`、`LocalizationCatalog.cs` 和独立 stub；不加载游戏程序集，不启动游戏，不发送游戏指令，不读写存档。最后一项按传入仓库路径扫描当前自有 UI / `L.Text` / `L.Format` 文案是否有三语资源，并检查格式参数一致性。

实际游戏显示与语言切换仍须在游戏内确认。
