using System;
using System.Collections.Generic;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Unity.Mono;
using HarmonyLib;
using Fungus;
using Mortal.Core;
using Mortal.Free;
using Mortal.Story;
using MoonSharp.Interpreter;
using TMPro;
using UnityEngine.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ZhaoHuoSystem;

[DefaultExecutionOrder(-32000)]
[BepInPlugin("local.zhaohuo.system", "赵活系统", "0.3.37")]
public sealed class Plugin : BaseUnityPlugin
{
    internal static Plugin? Instance;
    internal GameAdapter Adapter = null!;
    internal OptionHints OptionHints = null!;
    internal PracticeButtons Practice = null!;
    private RoundSkip roundSkip = null!;
    private PinballScore pinball = null!;
    internal SaveSlotSort SaveSort = null!;
    internal SaveNotes SaveNotes = null!;
    internal int SaveSlots = 20;
    internal BranchNotice Branches = null!;
    private CombatResult combatResult = null!;
    private DuelSpeedButtons speedButtons = null!;
    private RoundUnlimited unlimited = null!;
    internal SegmentSaves Segments = null!;
    private StoryReview Review = null!;
    private RouteMapRunner routeMap = null!;
    internal readonly Dictionary<Closure, long> LuaTokens = new();
    private ToolWindow window = null!;
    private NativeDicePresenter dicePresenter = null!;
    private readonly MapIndicators mapIndicators = new();
    private NativeInputGuard inputGuard = null!;
    private Harmony harmony = null!;
    private float nextRefresh;
    private bool wasVisible;
    private int lastToggleFrame = -1;
    private float lastToggleTime = -10;
    private readonly Dictionary<int, bool> nativeKeyDown = new();
    private bool legacyInputAvailable = true;
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    private readonly int processId = System.Diagnostics.Process.GetCurrentProcess().Id;
    private bool NativeFocused()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var foregroundId);
        return foregroundId == (uint)processId;
    }
    private string diagnostics = "", fingerprints = "";
    private readonly List<string> skippedPatches = new();
    private Diagnostics? sessionLog;
    private void Awake()
    {
        Instance = this;
        var selectBeforeAction = Config.Bind("工具", "行动前选择结果", true, "到达真实地点事件判定前等待选择；收起面板或继续会放行游戏流程。");
        Adapter = new GameAdapter(Logger, selectBeforeAction.Value, enabled => selectBeforeAction.Value = enabled);
        Adapter.RuntimeVersion = ReadRuntimeVersion();
        var lastChoices = Config.Bind("工具", "上次选择", "", "每个行动上次指定并实际触发的事件（行动=事件），供「重复上次选择」使用。");
        Adapter.LoadLastChoices(lastChoices.Value);
        Adapter.LastChoicesChanged = saved => lastChoices.Value = saved;
        var managed = Path.Combine(Paths.GameRootPath, "Mortal_Data", "Managed");
        var expected = new Dictionary<string, string>
        {
            ["Mortal.Core.dll"] = "0C20EE36782666F3AB70EC8889068BDF230ACC2D630446EA1DB551836866E8FA",
            ["Mortal.Story.dll"] = "EE9139A5E4631DC275B80CC3FF6A81A4959F3C908A3FE751E778A42350372249",
            ["Mortal.Free.dll"] = "0ABC19100F216515BCA88975E52051AFCE11955A8439FFACF4FF02118FD27920"
            ,["Fungus.dll"] = "BCB6D47EB31DEDC5D23D0DA281E0835AF0F939F30B64A39AAE7D5F6DAB96CAD2"
        };
        // Diagnostics only. Game updates are mostly small fixes that leave the mod working, so nothing is switched off here;
        // a patch that no longer fits is skipped on its own (PatchEach) and the rest keep working.
        Adapter.TestedBuild = expected.All(entry => Hash(Path.Combine(managed, entry.Key)) == entry.Value);
        // The mod's own session log starts before anything else is logged, so BOOT and fingerprints are in it.
        diagnostics = Path.Combine(Paths.BepInExRootPath, "ZhaoHuoSystem"); Directory.CreateDirectory(diagnostics);
        Diagnostics.Scene = SceneManager.GetActiveScene().name;
        sessionLog = new Diagnostics(diagnostics, Logger.SourceName, $"ZhaoHuoSystem {typeof(Plugin).Assembly.GetName().Version} | game {Adapter.RuntimeVersion} | Unity {Application.unityVersion} | testedBuild={Adapter.TestedBuild} | started {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        BepInEx.Logging.Logger.Listeners.Add(sessionLog);
        Logger.LogInfo($"BOOT plugin={typeof(Plugin).Assembly.GetName().Version} game={Adapter.RuntimeVersion} unity={Application.unityVersion} x64={IntPtr.Size == 8} testedBuild={Adapter.TestedBuild}");
        foreach (var entry in expected) Logger.LogInfo("FINGERPRINT " + entry.Key + " " + Hash(Path.Combine(managed, entry.Key)));
        if (!Adapter.TestedBuild) Logger.LogWarning("BUILD_CHANGED game assemblies differ from the build this version was developed on; all features stay on, any patch that no longer fits is skipped and logged");
        fingerprints = string.Join("\n", expected.Select(entry => "fingerprint " + entry.Key + " " + Hash(Path.Combine(managed, entry.Key)) + (Hash(Path.Combine(managed, entry.Key)) == entry.Value ? "" : " (expected " + entry.Value + ")")));
        L.RefreshLanguage();
        window = new ToolWindow(Adapter);
        var launcherAnchor = Config.Bind("界面", "入口位置", "", "收起入口位置，屏幕可用区域比例 x,y；留空或无效时用默认位置。");
        if (NativeUiState.TryParseAnchor(launcherAnchor.Value, out var anchorX, out var anchorY)) window.LauncherAnchor = new Vector2(anchorX, anchorY);
        window.LauncherMoved = anchor => launcherAnchor.Value = NativeUiState.FormatAnchor(anchor.x, anchor.y);
        dicePresenter = new NativeDicePresenter(Adapter, Logger);
        OptionHints = new OptionHints(Logger);
        Practice = new PracticeButtons(Logger);
        LibraryCollections.Log = Logger;
        OptionHints.ReadyOneShots = Adapter.ReadyOneShots;
        roundSkip = new RoundSkip(Logger, () => OptionHints.ActionMenuOpen(), name => OptionHints.ScriptText(name));
        unlimited = new RoundUnlimited(Logger, () => roundSkip.Button);
        // Saving anywhere in stories (F71); on by default, with no settings row, only this entry to switch it off.
        var storySaves = Config.Bind("工具", "剧情中随时存档", true, "剧情中任何时候都能用游戏自带的存档；存下的是当前这段剧情开头的状态，读档从这段开头重播。");
        Segments = new SegmentSaves(Logger, Path.Combine(diagnostics, "segment-save-check.txt"), Path.Combine(diagnostics, "segment-save-music.txt")) { Enabled = storySaves.Value };
        // 剧情回顾 (F84): branch scripts' copies kept as they start, written into an empty slot from the panel.
        Review = new StoryReview(Logger, Path.Combine(diagnostics, "review")) { ScriptText = name => OptionHints.ScriptText(name), ScriptMap = name => OptionHints.Map(name) };
        Segments.Kept = Review.Keep;
        window.Review = Review;
        window.ReviewFirstSlot = () => Review.FirstEmptySlot(SaveSlots);
        window.ReviewExport = (node, slot) => Review.Export(node, slot, SaveSlots, Segments);
        window.ReviewStar = node => Review.Star(node);
        // 剧情树 (F87): the main story's segments, read from the scripts as they are opened.
        window.Tree = new StoryTree(Logger, new StoryGraph(name => OptionHints.ScriptText(name), name => OptionHints.Map(name)));
        // 剧情路线图 (F89): built from the game's data the first time its view opens, a few milliseconds a frame.
        routeMap = new RouteMapRunner(Logger);
        window.Routes = routeMap;
        pinball = new PinballScore(Logger);
        SaveSort = new SaveSlotSort(Logger);
        // Until 0.3.22 one order served both pages; the load page starts from that shared choice.
        var saveSort = Config.Bind("界面", "存档排序", 0, "存档页手动存档的显示顺序：0 编号，1 最新在前，2 最早在前，3 收藏优先，4 最早在前（排除收藏）；记住上次的选择。");
        var loadSort = Config.Bind("界面", "读档排序", saveSort.Value, "读档页手动存档的显示顺序：0 编号，1 最新在前，2 最早在前，3 收藏优先，4 最早在前（排除收藏）；记住上次的选择。");
        SaveSort.SaveMode = Mathf.Clamp(saveSort.Value, 0, SaveSlotSort.Modes.Length - 1);
        SaveSort.LoadMode = Mathf.Clamp(loadSort.Value, 0, SaveSlotSort.Modes.Length - 1);
        SaveSort.Changed = (save, mode) => { if (save) saveSort.Value = mode; else loadSort.Value = mode; };
        var saveSlots = Config.Bind("界面", "手动存档数", 50, "读档、存档页的手动存档格数，20–99（游戏原为 20），面板「设置」里也能改。超过 20 号的存档在卸载模组后不在游戏的存档列表里显示，文件仍在，「最近存档」照常可读。改后下次打开读档、存档页时生效。");
        SaveSlots = Mathf.Clamp(saveSlots.Value, ToolWindow.MinSaveSlots, ToolWindow.MaxSaveSlots);
        // Stars and notes live beside the logs, never in a save (F50). A file that cannot be read is left alone, not overwritten.
        SaveNoteStore notes;
        try { notes = new SaveNoteStore(Path.Combine(diagnostics, "save-notes.txt")); }
        catch (Exception ex) { notes = new SaveNoteStore(null); Logger.LogWarning("SAVE_NOTE not loaded, changes this session are not kept: " + ex.GetBaseException().Message); }
        SaveNotes = new SaveNotes(Logger, notes);
        SaveSort.IsFavourite = slot => SaveNotes.IsFavourite(slot);
        SaveNotes.StarChanged = () => SaveSort.StarsChanged();
        // After an overwrite the player allowed, an open save page reads its slots again.
        SaveNotes.Overwritten = () =>
        {
            foreach (var page in Resources.FindObjectsOfTypeAll<SaveGamePanel>())
                if (page != null && page.isActiveAndEnabled) AccessTools.Method(typeof(SaveGamePanel), "UpdateSlot")?.Invoke(page, null);
        };
        Adapter.EventConsequences = OptionHints.EventConsequences;
        OptionHints.SetValue = Adapter.SetValue;
        OptionHints.CanWrite = id => Adapter.Snapshot.Values.Exists(value => value.Id == id && value.Writable);
        Adapter.ScriptText = OptionHints.ScriptText; Adapter.EventFlagChanges = OptionHints.EventFlagChanges;
        var panelKey = Config.Bind("界面", "面板快捷键", "F8", "打开/收起面板的按键（UnityEngine.KeyCode 名称，None 为不绑定）。");
        var hideKey = Config.Bind("界面", "隐藏快捷键", "None", "切换「隐藏全部悬浮窗」的按键；默认不绑定。");
        var hidden = Config.Bind("界面", "隐藏全部悬浮窗", false, "隐藏本模组在游戏内的全部悬浮显示，便于截图；用面板或隐藏快捷键恢复。");
        var uiScale = Config.Bind("界面", "界面缩放", 1f, "在按屏幕高度自动缩放的基础上再乘的倍数（0.5–2.5）。");
        UiScale.User = Mathf.Clamp(uiScale.Value, UiScale.MinUser, UiScale.MaxUser);
        var showBranches = Config.Bind("界面", "显示剧情分支小框", true, "剧情按条件分支时弹出的小框；关闭后不再弹出，其他悬浮显示不受影响。");
        var branchPlace = Config.Bind("界面", "剧情分支小框位置", "", "剧情分支小框拖到的位置，屏幕可用区域比例 x,y；留空为左下角。");
        var showMapCounts = Config.Bind("界面", "显示地图事件数字", true, "地图上地点旁的事件数字和悬停说明；关闭后不再显示，其他悬浮显示不受影响。");
        var showRoundButtons = Config.Bind("界面", "显示本旬按钮", true, "日期右边的「跳过本旬」和「本旬行动点无限」；关闭后两个都不显示，其他悬浮显示不受影响。");
        var directDice = Config.Bind("工具", "指定后直接出结果", true, "骰子界面点「指定」后不播转盘、不等「继续」，直接给出结果；结果仍由游戏自己结算。");
        Adapter.DirectDiceResult = directDice.Value;
        window.LoadSettings(ParseKey(panelKey.Value, KeyCode.F8), ParseKey(hideKey.Value, KeyCode.None), hidden.Value);
        window.ShowBranches = showBranches.Value; window.ShowMapCounts = showMapCounts.Value; window.ShowRoundButtons = showRoundButtons.Value; window.SaveSlots = SaveSlots;
        OptionHints.Suppressed = window.Hidden; PrimeKeys();
        window.SettingsChanged = () =>
        {
            panelKey.Value = window.PanelKey.ToString(); hideKey.Value = window.HideKey.ToString(); hidden.Value = window.Hidden;
            uiScale.Value = UiScale.User; showBranches.Value = window.ShowBranches; showMapCounts.Value = window.ShowMapCounts; showRoundButtons.Value = window.ShowRoundButtons; directDice.Value = Adapter.DirectDiceResult;
            SaveSlots = window.SaveSlots; saveSlots.Value = SaveSlots;
            if (!window.ShowBranches) Branches?.Clear();
            OptionHints.Suppressed = window.Hidden; PrimeKeys();
            Logger.LogInfo($"SETTINGS panelKey={window.PanelKey} hideKey={window.HideKey} hidden={window.Hidden} scale={UiScale.User} branches={window.ShowBranches} mapCounts={window.ShowMapCounts} roundButtons={window.ShowRoundButtons} directDice={Adapter.DirectDiceResult} saveSlots={SaveSlots}");
        };
        var invincible = Config.Bind("战斗", "无敌", false, "战役中主角与友军、决斗中主角不受伤害；决斗剧情扣血仍生效但不致死。");
        var oneHit = Config.Bind("战斗", "一击必杀", false, "敌人受到伤害即倒下；剧情保底血量仍生效。");
        var keepStamina = Config.Bind("战斗", "气力不减", false, "战役与决斗中主角的气不被消耗；决斗剧情改动仍生效。");
        var noCooldown = Config.Bind("战斗", "技能无冷却", false, "战役技能用后立刻可再用。");
        var keepAlive = Config.Bind("战斗", "双方最低一血", false, "仅决斗：双方的血都不会低于 1，谁都不会被打死。");
        CombatCheats.Invincible = invincible.Value; CombatCheats.OneHit = oneHit.Value;
        CombatCheats.KeepStamina = keepStamina.Value; CombatCheats.NoCooldown = noCooldown.Value; CombatCheats.KeepAlive = keepAlive.Value;
        var duelSpeed = Config.Bind("战斗", "决斗加速倍数", 2, "决斗（回合制）里游戏自带的加速开关打开时的倍数：2（原版）、3、4。");
        CombatCheats.DuelSpeed = Array.IndexOf(CombatCheats.DuelSpeeds, duelSpeed.Value) >= 0 ? duelSpeed.Value : 2;
        window.CombatChanged = () =>
        {
            invincible.Value = CombatCheats.Invincible; oneHit.Value = CombatCheats.OneHit;
            keepStamina.Value = CombatCheats.KeepStamina; noCooldown.Value = CombatCheats.NoCooldown; keepAlive.Value = CombatCheats.KeepAlive; duelSpeed.Value = CombatCheats.DuelSpeed;
            Logger.LogInfo($"COMBAT invincible={CombatCheats.Invincible} oneHit={CombatCheats.OneHit} keepStamina={CombatCheats.KeepStamina} noCooldown={CombatCheats.NoCooldown} keepAlive={CombatCheats.KeepAlive} duelSpeed={CombatCheats.DuelSpeed}");
        };
        Branches = new BranchNotice();
        // The branch box keeps the place the player dragged it to (F68).
        if (NativeUiState.TryParseAnchor(branchPlace.Value, out var branchX, out var branchY)) Branches.Anchor = new Vector2(branchX, branchY);
        Branches.Moved = anchor => { branchPlace.Value = anchor is Vector2 a ? NativeUiState.FormatAnchor(a.x, a.y) : ""; Logger.LogInfo("BRANCH_PLACE " + (branchPlace.Value.Length > 0 ? branchPlace.Value : "corner")); };
        window.BranchPlaceReset = () => Branches.ResetPlace();
        // The note editor is modal: the whole screen's pointer and the keyboard are its own while it is open.
        combatResult = new CombatResult(Logger);
        speedButtons = new DuelSpeedButtons(Logger) { Changed = () => window.CombatChanged?.Invoke() };
        inputGuard = new NativeInputGuard(window, Logger, () => SaveNotes.Editing,
            () => SaveNotes.Editing ? new Rect(0, 0, Screen.width, Screen.height) : Branches.Rect, () => combatResult.Rect);
        LocalizationManager.OnLanguageChanged += LanguageChanged;
        window.ExportDiagnostics = ExportDiagnostics;
        harmony = new Harmony("local.zhaohuo.system");
        PatchEach();
        try { CombatCheats.Install(harmony, Logger); }
        catch (Exception ex) { Logger.LogError("PATCH_SKIPPED CombatCheats " + ex.GetBaseException().Message); }
        SceneManager.activeSceneChanged += SceneChanged;
        MenuSignals.OnMenuEnd += MenuEnded;
        MenuSignals.OnMenuStart += MenuStarted;
        Logger.LogInfo($"READY panelKey={window.PanelKey} hideKey={window.HideKey} hidden={window.Hidden}; diagnostics=" + diagnostics);
        Logger.LogInfo($"INPUT keyboard={(Keyboard.current == null ? "none" : Keyboard.current.name)} focused={Application.isFocused}");
    }
    /// <summary>Each patch class on its own: one that no longer fits the game is skipped and logged, the others still apply.</summary>
    private void PatchEach()
    {
        foreach (var type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
        {
            try { harmony.CreateClassProcessor(type).Patch(); }
            catch (Exception ex) { skippedPatches.Add(type.Name); Logger.LogError("PATCH_SKIPPED " + type.Name + " " + ex.GetBaseException().Message); }
        }
        Logger.LogInfo("PATCHES skipped=" + skippedPatches.Count + (skippedPatches.Count > 0 ? " " + string.Join(",", skippedPatches) : ""));
    }
    private static string Hash(string path)
    {
        using var algorithm = SHA256.Create(); using var stream = File.OpenRead(path);
        return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "");
    }
    private string ReadRuntimeVersion()
    {
        try
        {
            var steamapps = Directory.GetParent(Paths.GameRootPath)?.Parent?.FullName;
            var manifest = steamapps == null ? "" : Path.Combine(steamapps, "appmanifest_1859910.acf");
            var build = File.Exists(manifest) ? Regex.Match(File.ReadAllText(manifest), "\"buildid\"\\s+\"(\\d+)\"").Groups[1].Value : "";
            return build.Length > 0 ? "build " + build : (string.IsNullOrWhiteSpace(Application.version) ? "本机游戏" : Application.version);
        }
        catch { return "本机游戏"; }
    }
    private void SceneChanged(Scene before, Scene after)
    {
        Diagnostics.Scene = after.name; Logger.LogInfo("SCENE " + before.name + " -> " + after.name);
        inputGuard?.Reset(); dicePresenter?.Dispose(); Branches?.Clear(); SaveNotes?.Close(); Segments?.SceneChanged();
        if (after.name == "Title") RoundUnlimited.Reset();
        Adapter.Invalidate("场景已变化，旧指定已清除", true, after.name == "Free" || after.name == "Title");
    }
    private void Update()
    {
        if (Perf.Frame() is string slow) Logger.LogWarning(slow);
        using var timing = Perf.Time("update");
        try
        {
            routeMap.Update();
            // Typing in a mod field or binding a new key never fires a hotkey.
            bool typing = window.CapturingKey || window.HasTextFocus || SaveNotes.Editing;
            if (Pressed(window.PanelKey) && !typing) TogglePanel();
            if (Pressed(window.HideKey) && !typing) ToggleHidden();
            if (wasVisible && !window.Visible) Adapter.ContinueAction();
            using (Perf.Time("dice-ui")) dicePresenter.Update(window.Hidden);
            try { Practice.Update(window.Hidden); } catch (Exception ex) { Logger.LogWarning("PRACTICE_CAP " + ex.GetBaseException().Message); }
            try { roundSkip.Update(window.Hidden || !window.ShowRoundButtons); } catch (Exception ex) { Logger.LogWarning("ROUND_SKIP " + ex.GetBaseException().Message); }
            try { unlimited.Update(window.Hidden || !window.ShowRoundButtons); } catch (Exception ex) { Logger.LogWarning("ROUND_UNLIMITED " + ex.GetBaseException().Message); }
            try { pinball.Update(window.Hidden); } catch (Exception ex) { Logger.LogWarning("PINBALL_SCORE " + ex.GetBaseException().Message); }
            try { SaveSort.Update(window.Hidden); } catch (Exception ex) { Logger.LogWarning("SAVE_SORT " + ex.GetBaseException().Message); }
            try { SaveNotes.Update(window.Hidden); } catch (Exception ex) { Logger.LogWarning("SAVE_NOTE " + ex.GetBaseException().Message); }
            try { Segments.Update(); } catch (Exception ex) { Logger.LogWarning("SEGMENT_SAVE " + ex.GetBaseException().Message); }
            try { speedButtons.Update(window.Hidden); } catch (Exception ex) { Logger.LogWarning("DUEL_SPEED " + ex.GetBaseException().Message); }
            wasVisible = window.Visible; inputGuard.Tick();
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + .4f;
                if (L.RefreshLanguage()) RefreshPresentation();
                using (Perf.Time("refresh")) Adapter.Refresh();
                // The full state snapshot is written only when a diagnostic command asks for it, or on export.
                if (ReadCommand()) File.WriteAllText(Path.Combine(diagnostics, "status.txt"), StatusText(), Encoding.UTF8);
            }
        }
        catch (Exception ex) { Logger.LogError(ex); }
    }
    internal bool SuppressGameInput(InputAction.CallbackContext context) => inputGuard?.Suppress(context) == true;
    internal bool SuppressGameInput(InputAction action) => inputGuard?.Suppress(action) == true;
    internal bool SuppressPointer => inputGuard?.BlocksPointer == true;
    internal bool SuppressKeyboard => inputGuard?.BlocksKeyboard == true;
    private void LanguageChanged() { if (L.RefreshLanguage()) RefreshPresentation(); }
    private void RefreshPresentation() { window?.RefreshPresentation(); dicePresenter?.RefreshPresentation(); }
    private void OnApplicationFocus(bool focused) { if (!focused) inputGuard?.Reset(); }
    /// <summary>The key just pressed to bind a hotkey must not also fire it on the next frame's native poll.</summary>
    private void PrimeKeys()
    {
        foreach (var key in new[] { window.PanelKey, window.HideKey })
        {
            int vk = NativeUiState.VirtualKey(key.ToString());
            if (vk != 0) nativeKeyDown[vk] = (GetAsyncKeyState(vk) & 0x8000) != 0;
        }
    }
    private static KeyCode ParseKey(string text, KeyCode fallback) =>
        Enum.TryParse<KeyCode>(text, out var key) && (key == KeyCode.None || NativeUiState.IsBindableKey(key.ToString())) ? key : fallback;
    /// <summary>Legacy input, InputSystem and the native focus fallback; TogglePanel/ToggleHidden debounce duplicates.</summary>
    private bool Pressed(KeyCode key)
    {
        if (key == KeyCode.None) return false;
        bool pressed = false;
        if (legacyInputAvailable)
        {
            try { pressed |= Input.GetKeyDown(key); }
            catch (InvalidOperationException) { legacyInputAvailable = false; Logger.LogInfo("INPUT legacy unavailable"); }
        }
        if (Keyboard.current != null && Enum.TryParse<Key>(NativeUiState.InputSystemKeyName(key.ToString()), out var inputKey) && inputKey != Key.None)
            pressed |= Keyboard.current[inputKey].wasPressedThisFrame;
        int vk = NativeUiState.VirtualKey(key.ToString());
        if (vk != 0)
        {
            var state = GetAsyncKeyState(vk); bool down = (state & 0x8000) != 0;
            nativeKeyDown.TryGetValue(vk, out bool wasDown);
            if (((state & 1) != 0 || down && !wasDown) && NativeFocused()) pressed = true;
            nativeKeyDown[vk] = down;
        }
        return pressed;
    }
    private void OnGUI()
    {
        if (window == null) return;
        using var timing = Perf.Time("gui");
        var key = Event.current;
        if (key.type == EventType.KeyDown && key.keyCode != KeyCode.None && !window.CapturingKey && !window.HasTextFocus && !SaveNotes.Editing)
        {
            if (key.keyCode == window.PanelKey) { TogglePanel(); key.Use(); }
            else if (key.keyCode == window.HideKey) { ToggleHidden(); key.Use(); }
        }
        try
        {
            inputGuard.ObserveGui(Event.current);
            bool mouseDown = Event.current.type == EventType.MouseDown;
            // The note editor is drawn alone, so no other surface can take its clicks or keys.
            if (SaveNotes.Editing)
            {
                try { SaveNotes.Draw(); } catch (Exception ex) { OverlayFailed("note", ex); SaveNotes.Close(); }
                if (mouseDown) inputGuard.FocusResolved(); else inputGuard.Tick();
                return;
            }
            // Passive overlays are guarded one by one: a failure in any of them must never keep the panel from drawing,
            // because a paused action waits for the panel. The branch notice handles its clicks first, paints last.
            try { Branches.Draw(window.Hidden); } catch (Exception ex) { OverlayFailed("branch", ex); Branches.Clear(); }
            try { combatResult.Draw(window.Hidden); } catch (Exception ex) { OverlayFailed("combat-result", ex); }
            if (!window.Visible)
            {
                try { if (!window.Hidden && window.ShowMapCounts) mapIndicators.Draw(Adapter.Snapshot); } catch (Exception ex) { OverlayFailed("map", ex); }
                if (window.DrawLauncher())
                {
                    lastToggleFrame = Time.frameCount; lastToggleTime = Time.realtimeSinceStartup;
                    Logger.LogInfo($"PANEL frame={Time.frameCount} visible=True source=mouse");
                }
            }
            else
            {
                window.Draw();
            }
            try { Branches.Paint(); } catch (Exception ex) { OverlayFailed("branch", ex); Branches.Clear(); }
            try { if (!window.Hidden) OptionHints.DrawTooltip(window.Visible && window.IsPointerOver); } catch (Exception ex) { OverlayFailed("tooltip", ex); }
            if (mouseDown) inputGuard.FocusResolved(); else inputGuard.Tick();
        }
        catch (Exception ex)
        {
            inputGuard?.Reset(); Logger.LogError(ex);
            // Never leave the game waiting on a panel that cannot draw: the action goes on with the game's own choice.
            if (Adapter.Snapshot.ActionWaiting) { Adapter.ContinueAction(); Logger.LogWarning("PANEL_FAILED paused action released"); }
        }
    }
    /// <summary>Before a load or save page builds its slots: never fewer than the game's own.</summary>
    internal void ExpandSaveSlots(MonoBehaviour panel)
    {
        if (R.Field(panel, "_slotCount") is not int count || count >= SaveSlots) return;
        R.Set(panel, "_slotCount", SaveSlots);
        Logger.LogInfo($"SAVE_SLOTS page={(panel is SaveGamePanel ? "save" : "load")} {count}->{SaveSlots}");
    }
    /// <summary>
    /// Before a page opens (F90): one built with another count than 设置 now has builds its slots again, with the game's
    /// own CreateSlot, as at its Awake. The pages are built once a session, so this is what lets a change apply without a restart.
    /// </summary>
    internal void FitSaveSlots(MonoBehaviour panel)
    {
        if (R.Field(panel, "_slotCount") is not int count || count == SaveSlots) return;
        try
        {
            R.Set(panel, "_slotCount", SaveSlots);
            AccessTools.Method(panel.GetType(), "CreateSlot")?.Invoke(panel, null);
            AccessTools.Method(panel.GetType(), "SetButtonNavigation")?.Invoke(panel, null);
            Logger.LogInfo($"SAVE_SLOTS page={(panel is SaveGamePanel ? "save" : "load")} rebuilt {count}->{SaveSlots}");
        }
        catch (Exception ex) { Logger.LogWarning("SAVE_SLOTS " + ex.GetBaseException().Message); }
    }
    private readonly HashSet<string> overlayFailures = new(StringComparer.Ordinal);
    private void OverlayFailed(string overlay, Exception ex)
    {
        if (overlayFailures.Add(overlay + ex.GetType().Name)) Logger.LogError("OVERLAY_FAILED " + overlay + " " + ex);
    }
    private void OnDestroy()
    {
        Adapter?.ContinueAction();
        dicePresenter?.Dispose(); mapIndicators.Dispose(); OptionHints?.Dispose(); Branches?.Dispose(); Practice?.Dispose(); roundSkip?.Dispose(); unlimited?.Dispose(); pinball?.Dispose(); SaveSort?.Dispose(); SaveNotes?.Dispose(); combatResult?.Dispose(); speedButtons?.Dispose();
        inputGuard?.Dispose(); SceneManager.activeSceneChanged -= SceneChanged; MenuSignals.OnMenuEnd -= MenuEnded; MenuSignals.OnMenuStart -= MenuStarted;
        LocalizationManager.OnLanguageChanged -= LanguageChanged;
        window?.Dispose();
        harmony?.UnpatchSelf(); Instance = null;
        if (sessionLog != null) { BepInEx.Logging.Logger.Listeners.Remove(sessionLog); sessionLog.Dispose(); }
    }
    private bool Debounced()
    {
        if (lastToggleFrame == Time.frameCount || Time.realtimeSinceStartup - lastToggleTime < .18f) return false;
        lastToggleTime = Time.realtimeSinceStartup; lastToggleFrame = Time.frameCount; return true;
    }
    private void TogglePanel()
    {
        if (!Debounced()) return;
        // The panel key also brings a hidden overlay back, so hiding can never strand the player.
        if (window.Hidden) { window.SetHidden(false); window.Visible = true; }
        else window.Visible = !window.Visible;
        if (!window.Visible) Adapter.ContinueAction();
        Logger.LogInfo($"PANEL frame={Time.frameCount} visible={window.Visible}");
    }
    private void ToggleHidden()
    {
        if (!Debounced()) return;
        window.SetHidden(!window.Hidden);
        if (window.Hidden) Adapter.ContinueAction();
        Logger.LogInfo($"OVERLAY hidden={window.Hidden}");
    }
    internal void ShowForAction() { if (window.Hidden) return; window.ShowEvents(true); inputGuard.Tick(); }
    private void MenuEnded(MenuDialog menu) => Adapter?.ClearPreview();
    // A choice menu takes over from the branch that led to it.
    private void MenuStarted(MenuDialog menu) { Branches?.Clear(); OptionHints?.MenuStarted(); Logger.LogInfo("MENU start"); }
    /// <summary>Called after the game's own Switch ran; reads the same data, evaluates nothing for the game.</summary>
    internal void BranchTaken(string name, int result)
    {
        using var timing = Perf.Time("branch");
        Logger.LogInfo($"BRANCH name={name} result={result}");
        // Switched off in settings: the branch is still logged, the box is never filled.
        if (!window.ShowBranches) return;
        try { if (OptionHints.FindSwitch(name) is object data) Branches.Record(data, result); }
        catch (Exception ex) { Logger.LogWarning("BRANCH " + name + " " + ex.GetBaseException().Message); }
    }
    /// <summary>Appends a short effect line under the merchant's description; the item and shop state are untouched.</summary>
    internal void ShopItemHovered(ShopPanel panel, IGameItem item)
    {
        var description = R.Field<Text>(panel, "_dialogDesc");
        string effect = description == null || item == null ? "" : OptionHints.ItemEffect(item);
        if (effect.Length > 0) description!.text += "\n" + OptionHints.Colorize(effect, description!.color, description.supportRichText);
    }
    /// <summary>A hovered menu row that leads into a random pool previews that pool in the panel, read-only.</summary>
    internal void OptionHovered(Component row)
    {
        try
        {
            Text[]? titles = null; string? source = null;
            if (row.GetComponent<BreakOptionButton>() is BreakOptionButton rest) { source = R.Field<Text>(rest, "_source")?.text; titles = R.Field<Text[]>(rest, "_titleText"); }
            else if (row.GetComponent<NormalOptionButton>() is NormalOptionButton normal) { source = R.Field<Text>(normal, "_source")?.text; titles = R.Field<Text[]>(normal, "_text"); }
            if (source == null) return;
            var preview = OptionHints.Resolve(LuaOptions.OptionKey(source));
            if (preview == null || preview.Pool.Length == 0) { Adapter.ClearPreview(); return; }
            string title = titles != null && titles.Length > 0 && titles[0] != null ? OptionHints.StripHint(titles[0].text) : "";
            Adapter.PreviewPool(preview.Pool, title);
        }
        catch (Exception ex) { Logger.LogWarning("OPTION_PREVIEW " + ex.GetBaseException().Message); }
    }
    internal void BeginDicePresentation(DiceMenuDialog dialog) => dicePresenter.Begin(dialog);
    internal void EndDicePresentation(DiceMenuDialog dialog) => dicePresenter.End(dialog);
    internal bool GatePosition(string name, long token)
    {
        // A hidden overlay cannot offer a choice, so the game is never held for one.
        try { return !window.Hidden && Adapter.WaitForPosition(name, token); }
        catch (Exception ex) { Logger.LogError(ex); Adapter.ContinueAction(); return false; }
    }
    private bool ReadCommand()
    {
        var path = Path.Combine(diagnostics, "command.txt");
        if (!File.Exists(path)) return false;
        var parts = File.ReadAllText(path, Encoding.UTF8).Trim().Split(new[] { '\t' }, StringSplitOptions.None); File.Delete(path);
        if (parts.Length == 0) return false;
        OperationResult result;
        switch (parts[0])
        {
            case "set" when parts.Length >= 3: result = Adapter.SetValue(parts[1], parts[2]); break;
            case "event" when parts.Length >= 2: result = Adapter.SpecifyEvent(parts[1]); break;
            case "cancel-event": result = Adapter.CancelEvent(); break;
            case "continue": result = Adapter.ContinueAction(); break;
            case "selection" when parts.Length >= 2: result = Adapter.SetActionSelection(parts[1] == "true"); break;
            case "dice" when parts.Length >= 2 && int.TryParse(parts[1], out var value): result = Adapter.SpecifyDice(value); break;
            case "branch" when parts.Length >= 2: result = Adapter.SpecifyDiceBranch(parts[1]); break;
            case "cancel-dice": result = Adapter.CancelDice(); break;
            case "show": window.Visible = true; result = OperationResult.Ok("面板已打开"); break;
            case "hide": window.Visible = false; result = OperationResult.Ok("面板已收起"); break;
            default: result = OperationResult.Fail("未知诊断操作"); break;
        }
        File.WriteAllText(Path.Combine(diagnostics, "command-result.txt"), DateTime.Now.ToString("O") + "\t" + result.Success + "\t" + result.Message, Encoding.UTF8);
        return true;
    }
    /// <summary>One folder with the kept session logs, current state, settings, performance and environment; its path is copied.</summary>
    private string ExportDiagnostics()
    {
        string folder = Path.Combine(diagnostics, "reports", "report-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        Logger.LogInfo("EXPORT " + folder);
        sessionLog?.CopyLogs(folder);
        File.WriteAllText(Path.Combine(folder, "status.txt"), StatusText(), Encoding.UTF8);
        File.WriteAllText(Path.Combine(folder, "performance.txt"), Perf.Report() + "\n\nrepeated warnings:\n" + (sessionLog?.RepeatSummary() ?? ""), Encoding.UTF8);
        File.WriteAllText(Path.Combine(folder, "environment.txt"), EnvironmentText(), Encoding.UTF8);
        if (File.Exists(Config.ConfigFilePath))
        {
            using var input = new FileStream(Config.ConfigFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var output = File.Create(Path.Combine(folder, Path.GetFileName(Config.ConfigFilePath)));
            input.CopyTo(output);
        }
        GUIUtility.systemCopyBuffer = folder;
        return folder;
    }
    private string EnvironmentText() => string.Join("\n", new[]
    {
        "plugin=" + typeof(Plugin).Assembly.GetName().Version, "pluginSha256=" + Hash(typeof(Plugin).Assembly.Location), "game=" + Adapter.RuntimeVersion,
        "testedBuild=" + Adapter.TestedBuild, fingerprints,
        "skippedPatches=" + string.Join(",", skippedPatches), "combatMissing=" + string.Join(",", CombatCheats.Missing), "unity=" + Application.unityVersion, "bepinex=" + typeof(Paths).Assembly.GetName().Version,
        "language=" + L.LanguageId, "screen=" + Screen.width + "x" + Screen.height + " " + Screen.fullScreenMode, "os=" + SystemInfo.operatingSystem,
        "gpu=" + SystemInfo.graphicsDeviceName, "scene=" + SceneManager.GetActiveScene().name, "panelKey=" + window.PanelKey + " hideKey=" + window.HideKey + " hidden=" + window.Hidden,
        "combat invincible=" + CombatCheats.Invincible + " oneHit=" + CombatCheats.OneHit + " keepStamina=" + CombatCheats.KeepStamina + " noCooldown=" + CombatCheats.NoCooldown,
        "exported=" + DateTime.Now.ToString("O")
    });
    private string StatusText()
    {
        var s = Adapter.Snapshot; var text = new StringBuilder();
        text.AppendLine($"time={DateTime.Now:O}\nversion={s.Version}\nready={s.Ready}\nrevision={s.Revision}\nscene={SceneManager.GetActiveScene().name}\nlocation={s.Location}\naction={s.Action}\nstatus={s.Status}\npanel={window.Visible}\nselectionEnabled={s.ActionSelectionEnabled}\nactionWaiting={s.ActionWaiting}\npointerBlocked={inputGuard.BlocksPointer}\nkeyboardBlocked={inputGuard.BlocksKeyboard}\npendingEvent={s.PendingEventId}");
        foreach (var e in s.Events) text.AppendLine($"EVENT\t{e.Id}\t{e.Title}\teligible={e.Eligible}\tweight={e.Weight}");
        foreach (var v in s.Values) text.AppendLine($"VALUE\t{v.Id}\t{v.Name}\t{v.Value}\t{v.Min}\t{v.Max}\twritable={v.Writable}");
        text.AppendLine($"DICE\tactive={s.Dice.Active}\tmanual={s.Dice.ManualStart}\tname={s.Dice.Name}\trange={s.Dice.Min}..{s.Dice.Max}\taddition={s.Dice.Addition}\traw={s.Dice.RawValue}\tpending={s.Dice.PendingRawValue}");
        foreach (var b in s.Dice.Branches) text.AppendLine($"BRANCH\t{b.Id}\t{b.Title}\treachable={b.Reachable}\traw={b.RawValue}");
        if (s.Ready && SceneManager.GetActiveScene().name == "Free")
        {
            var camera = Camera.main;
            foreach (var position in Resources.FindObjectsOfTypeAll<PositionController>())
            {
                if (!position.gameObject.activeInHierarchy || position.gameObject.scene.name != "Free") continue;
                var point = camera != null ? camera.WorldToScreenPoint(position.transform.position) : Vector3.zero;
                text.AppendLine($"POSITION\t{R.Field(position, "_position")}\t{position.gameObject.name}\tclient=({point.x:0},{Screen.height - point.y:0})\tz={point.z:0.0}");
            }
        }
        return text.ToString();
    }
}

[HarmonyPatch(typeof(LuaEnvironment), nameof(LuaEnvironment.LoadLuaFunction))]
internal static class LuaContextPatch
{
    private static void Prefix(ref string luaString, string friendlyName, out long __state)
    {
        __state = 0;
        using var timing = Perf.Time("lua-load");
        var plugin = Plugin.Instance;
        if (plugin == null) return;
        // Option consequences are read from the original text, before instrumentation.
        plugin.OptionHints.Load(luaString, friendlyName);
        plugin.Segments?.ScriptLoaded(friendlyName);
        var token = plugin.Adapter.LuaLoaded(luaString, friendlyName);
        var instrumented = LuaGate.Instrument(luaString, token);
        if (instrumented != luaString) __state = token;
        luaString = instrumented;
    }
    private static void Postfix(LuaEnvironment __instance, Closure __result, long __state)
    {
        if (__state != 0 && __result != null && Plugin.Instance != null) Plugin.Instance.LuaTokens[__result] = __state;
        __instance.Interpreter.Globals["__zhaohuo_wait_position"] = DynValue.NewCallback((context, args) =>
        {
            if (context.GetCallingCoroutine().State == CoroutineState.Main || args.Count < 2) return DynValue.False;
            return DynValue.NewBoolean(Plugin.Instance?.GatePosition(args[0].String, (long)args[1].Number) == true);
        });
    }
}
[HarmonyPatch(typeof(LuaEnvironment), "RunLuaCoroutine")]
internal static class LuaBoundaryPatch
{
    private static void Postfix(Closure closure, ref IEnumerator __result)
    {
        var plugin = Plugin.Instance;
        if (plugin != null && plugin.LuaTokens.TryGetValue(closure, out var token))
        {
            plugin.LuaTokens.Remove(closure);
            __result = Guard(__result, plugin.Adapter, plugin.Adapter.BoundaryEpoch, token);
        }
    }
    private static IEnumerator Guard(IEnumerator original, GameAdapter adapter, long epoch, long token)
    {
        while (epoch == adapter.BoundaryEpoch && !adapter.IsLuaCancelled(token) && original.MoveNext()) yield return original.Current;
        (original as IDisposable)?.Dispose();
    }
}
[HarmonyPatch(typeof(PlayerStatManagerData), nameof(PlayerStatManagerData.Load))]
internal static class LoadPatch { private static void Prefix() => Plugin.Instance?.Adapter.Invalidate("载入存档，旧指定已清除", true, true); }
[HarmonyPatch(typeof(PositionController), nameof(PositionController.OnPositionClick))]
internal static class MapActionPatch
{
    private static void Prefix(PositionController __instance, out bool __state)
    {
        __state = R.Field<FreeManager>(__instance, "_freeManager")?.IsProcessing ?? true;
        Plugin.Instance?.Adapter.Invalidate("等待读取本次行动", true, true);
    }
    private static void Postfix(PositionController __instance, bool __state)
    {
        if (!__state && R.Field<FreeManager>(__instance, "_freeManager")?.IsProcessing == true)
            Plugin.Instance?.Adapter.EnterLocation(__instance);
    }
}
[HarmonyPatch(typeof(LuaManager), nameof(LuaManager.ChangeScene))]
internal static class ScriptLocationPatch { private static void Prefix() => Plugin.Instance?.Adapter.ClearLocation(); }
[HarmonyPatch(typeof(CheckPointManager), nameof(CheckPointManager.Position))]
internal static class PositionTracePatch
{
    private static void Prefix(string name) => Plugin.Instance?.Adapter.ObservePosition(name);
    private static void Postfix(string name, string __result) => Plugin.Instance?.Adapter.CompletePosition(name, __result);
}
[HarmonyPatch(typeof(PositionResultData), nameof(PositionResultData.GetResult))]
internal static class EventPatch
{
    private static bool Prefix(PositionResultData __instance, ref string __result)
    {
        if (Plugin.Instance?.Adapter.TryEvent(__instance, out var result) == true) { __result = result; return false; }
        return true;
    }
}
[HarmonyPatch(typeof(DiceMenuDialog), nameof(DiceMenuDialog.ExecuteRoll))]
internal static class DiceOpenPatch
{
    private static void Prefix(DiceMenuDialog __instance, string[] options, string checkName)
    {
        var plugin = Plugin.Instance;
        if (plugin == null) return;
        plugin.Adapter.BeginDice(__instance, checkName, options);
        plugin.BeginDicePresentation(__instance);
    }
}
// 「指定后直接出结果」 (F67): the specified roll shows its result without the wheel's spin and goes on without 继续 (or
// the fate-point 重掷 prompt). The result is still CheckPointManager.Dice's, which Roll called before the spin.
[HarmonyPatch(typeof(DiceMenuDialog), "StartSpin")]
internal static class DiceFastSpinPatch
{
    private static bool Prefix(DiceMenuDialog __instance, ref IEnumerator __result)
    {
        if (Plugin.Instance?.Adapter is not GameAdapter adapter || !adapter.FastRoll(__instance)) return true;
        __result = adapter.FastSpin(__instance); return false;
    }
}
[HarmonyPatch(typeof(DiceMenuDialog), "GetNextInput")]
internal static class DiceFastContinuePatch
{
    private static void Postfix(DiceMenuDialog __instance, ref bool __result) { if (!__result && Plugin.Instance?.Adapter.FastRolled(__instance) == true) __result = true; }
}
[HarmonyPatch(typeof(DiceMenuDialog), "Roll")]
internal static class DiceRollPatch { private static void Prefix(DiceMenuDialog __instance, string checkName) => Plugin.Instance?.Adapter.BeginRoll(__instance, checkName); }
[HarmonyPatch(typeof(DiceMenuDialog), "OnDisable")]
internal static class DiceClosePatch
{
    private static void Prefix(DiceMenuDialog __instance)
    { Plugin.Instance?.EndDicePresentation(__instance); Plugin.Instance?.Adapter.EndDice(__instance); }
}
[HarmonyPatch(typeof(ShopPanel), nameof(ShopPanel.OnItemHover))]
internal static class ShopHoverPatch
{
    private static void Postfix(ShopPanel __instance, IGameItem item) => Plugin.Instance?.ShopItemHovered(__instance, item);
}
[HarmonyPatch(typeof(MenuToggleButton))]
internal static class MenuHoverPatch
{
    [HarmonyPostfix, HarmonyPatch(nameof(MenuToggleButton.OnPointerEnter))]
    private static void Enter(MenuToggleButton __instance) => Plugin.Instance?.OptionHovered(__instance);
    [HarmonyPostfix, HarmonyPatch(nameof(MenuToggleButton.OnSelect))]
    private static void Select(MenuToggleButton __instance) => Plugin.Instance?.OptionHovered(__instance);
}
// Consequence brackets after the native option text. UpdateContent rewrites the text from its source key each
// time, so appending once per call never stacks; the source key itself is left untouched.
[HarmonyPatch(typeof(NormalOptionButton), nameof(NormalOptionButton.UpdateContent))]
internal static class NormalOptionHintPatch
{
    private static void Postfix(NormalOptionButton __instance)
    { using (Perf.Time("option-hint")) Plugin.Instance?.OptionHints.Decorate(__instance, R.Field<Text>(__instance, "_source"), R.Field<Text[]>(__instance, "_text")); }
}
[HarmonyPatch(typeof(BreakOptionButton), nameof(BreakOptionButton.UpdateContent))]
internal static class BreakOptionHintPatch
{
    private static void Postfix(BreakOptionButton __instance)
    { if (__instance.gameObject.activeSelf) using (Perf.Time("option-hint")) Plugin.Instance?.OptionHints.Decorate(__instance, R.Field<Text>(__instance, "_source"), R.Field<Text[]>(__instance, "_titleText")); }
}
[HarmonyPatch(typeof(DiceOption), nameof(DiceOption.UpdateContent))]
internal static class DiceOptionHintPatch
{
    private static void Postfix(DiceOption __instance)
    { using (Perf.Time("option-hint")) Plugin.Instance?.OptionHints.Decorate(__instance, R.Field<Text>(__instance, "_sourceText"), R.Field<TMP_Text>(__instance, "_normalText"), R.Field<TMP_Text>(__instance, "_highText")); }
}
// Manual save slots in the chosen order each time the load or save page refreshes them from disk (F44), with their
// stars and notes (F50).
[HarmonyPatch(typeof(LoadGamePanel), "UpdateSlot")]
internal static class LoadSlotSortPatch
{
    private static void Postfix(LoadGamePanel __instance)
    { Plugin.Instance?.SaveSort.Refreshed(__instance); Plugin.Instance?.SaveNotes.Refreshed(__instance); }
}
[HarmonyPatch(typeof(SaveGamePanel))]
internal static class SaveSlotSortPatch
{
    [HarmonyPostfix, HarmonyPatch("UpdateSlot")]
    private static void Refreshed(SaveGamePanel __instance)
    { Plugin.Instance?.SaveSort.Refreshed(__instance); Plugin.Instance?.SaveNotes.Refreshed(__instance); }
    [HarmonyPostfix, HarmonyPatch(nameof(SaveGamePanel.OnPanelOpen))]
    private static void Opened(SaveGamePanel __instance)
    => Plugin.Instance?.SaveSort.Opened(__instance);
}
// More manual save slots (F51): both pages build theirs in Awake from _slotCount. A slot is just its number in the save
// file's name (Save_021.dat), read and written by the game's own SaveSystem like the first twenty.
[HarmonyPatch]
internal static class SaveSlotCountPatch
{
    [HarmonyPrefix, HarmonyPatch(typeof(LoadGamePanel), "Awake")] private static void Load(LoadGamePanel __instance) => Plugin.Instance?.ExpandSaveSlots(__instance);
    [HarmonyPrefix, HarmonyPatch(typeof(SaveGamePanel), "Awake")] private static void Save(SaveGamePanel __instance) => Plugin.Instance?.ExpandSaveSlots(__instance);
    // A count changed in 设置 since the page was built (F90).
    [HarmonyPrefix, HarmonyPatch(typeof(LoadGamePanel), nameof(LoadGamePanel.OnPanelOpen))] private static void LoadOpen(LoadGamePanel __instance) => Plugin.Instance?.FitSaveSlots(__instance);
    [HarmonyPrefix, HarmonyPatch(typeof(SaveGamePanel), nameof(SaveGamePanel.OnPanelOpen))] private static void SaveOpen(SaveGamePanel __instance) => Plugin.Instance?.FitSaveSlots(__instance);
}
// 剧情中随时存档 (F71): a story save made away from the game's own save points writes the copy kept at this script's start.
[HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.SaveGameData), new[] { typeof(string) })]
internal static class SegmentSaveWritePatch
{
    // Held for the favourite question (F73): nothing is written now, so nothing is noted either.
    [ThreadStatic] private static bool held;
    private static bool Prefix(string slot)
    {
        held = Plugin.Instance?.SaveNotes?.HoldOverwrite(slot) == true;
        if (held) return false;
        Plugin.Instance?.Segments?.BeforeSave(); return true;
    }
    private static void Postfix(string slot)
    {
        if (held) { held = false; return; }
        Plugin.Instance?.Segments?.AfterSave(slot);
        Plugin.Instance?.SaveNotes?.Written(slot);
    }
}
[HarmonyPatch(typeof(SaveSystem), "CreateSaveData")]
internal static class SegmentSaveCreatePatch
{
    private static bool Prefix(ref GameSave __result)
    {
        if (Plugin.Instance?.Segments?.Substitute() is not GameSave copy) return true;
        __result = copy; return false;
    }
}
[HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.LoadGameData))]
internal static class SegmentSaveLoadPatch { private static void Postfix() => Plugin.Instance?.Segments?.Loaded(); }
[HarmonyPatch(typeof(LuaManager), nameof(LuaManager.ToggleSaveButton))]
internal static class SegmentSaveNativePatch { private static void Postfix(int active) => Plugin.Instance?.Segments?.NativeToggle(active == 1); }
// Whether the music and ambience can be heard, for the note a story save keeps of them (F78).
[HarmonyPatch(typeof(SoundManager))]
internal static class SegmentSaveMusicPatch
{
    [HarmonyPostfix, HarmonyPatch(nameof(SoundManager.FadeOutMusic))] private static void MusicOut(int value) => Plugin.Instance?.Segments?.MusicLevel(value);
    [HarmonyPostfix, HarmonyPatch(nameof(SoundManager.FadeInMusic))] private static void MusicIn(int value) => Plugin.Instance?.Segments?.MusicLevel(value);
    [HarmonyPostfix, HarmonyPatch(nameof(SoundManager.FadeOutEnvSound))] private static void EnvOut(int value) => Plugin.Instance?.Segments?.EnvLevel(value);
    [HarmonyPostfix, HarmonyPatch(nameof(SoundManager.FadeInEnvSound))] private static void EnvIn(int value) => Plugin.Instance?.Segments?.EnvLevel(value);
}
// A save deleted in game takes its star and note with it.
[HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.DeleteSaveData))]
internal static class SaveNoteDeletePatch { private static void Postfix(string slot) => Plugin.Instance?.SaveNotes.Deleted(slot); }
// 「本次取消上限」 is offered whenever a martial (修炼) or upgrade (锻造 / 炼丹) panel opens.
[HarmonyPatch(typeof(MartialPanel), nameof(MartialPanel.Open))]
internal static class MartialOpenPatch { private static void Prefix(MartialPanel __instance) => Plugin.Instance?.Practice.Opened(__instance); }
// The upgrade panel has no unlimited mode of its own: once lifted for this opening, its count is kept topped up and shown as ∞.
[HarmonyPatch(typeof(UpgradePanel))]
internal static class UpgradeUnlimitedPatch
{
    internal static void Fill(MonoBehaviour panel) { if (PracticeLimits.Lifted(panel) && R.Field(panel, "_currentCount") is int count && count < 100000) R.Set(panel, "_currentCount", 1000000); }
    [HarmonyPrefix, HarmonyPatch(nameof(UpgradePanel.Open))] private static void Open(UpgradePanel __instance) => Plugin.Instance?.Practice.Opened(__instance);
    [HarmonyPrefix, HarmonyPatch("UpdateButton")] private static void Button(UpgradePanel __instance) => Fill(__instance);
    [HarmonyPrefix, HarmonyPatch("UpgradeButtonClick")] private static void Upgrade(UpgradePanel __instance) => Fill(__instance);
    [HarmonyPrefix, HarmonyPatch("NextLevelButtonClick")] private static void Next(UpgradePanel __instance) => Fill(__instance);
    [HarmonyPrefix, HarmonyPatch("UpdateStatus")] private static void Before(UpgradePanel __instance) => Fill(__instance);
    [HarmonyPostfix, HarmonyPatch("UpdateStatus")]
    private static void After(UpgradePanel __instance)
    { if (PracticeLimits.Lifted(__instance) && R.Field<Text>(__instance, "_maxCountText") is Text text && text != null) text.text = L.GameText("System/Infinity", "∞"); }
}
[HarmonyPatch(typeof(CheckPointManager), nameof(CheckPointManager.Switch))]
internal static class BranchPatch { private static void Postfix(string name, int __result) => Plugin.Instance?.BranchTaken(name, __result); }
[HarmonyPatch(typeof(CheckPointManager), nameof(CheckPointManager.Dice))]
internal static class DicePatch
{
    private static void Prefix(string name, ref int random) => Plugin.Instance?.Adapter.BeforeDice(name, ref random);
    private static void Postfix(string name, int random, DiceCheckResult __result) => Plugin.Instance?.Adapter.AfterDice(name, random, __result);
}
