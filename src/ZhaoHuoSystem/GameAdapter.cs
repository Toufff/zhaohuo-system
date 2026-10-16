using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using HarmonyLib;
using Mortal.Core;
using Mortal.Story;
using Mortal.Free;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ZhaoHuoSystem;

public sealed class GameAdapter : IGameAdapter
{
    private readonly ManualLogSource log;
    private readonly Action<bool>? selectionChanged;
    private string poolName = "", contextIdentity = "", pendingEvent = "";
    private string statusSource = "等待读取当前行动";
    private object[] statusArguments = Array.Empty<object>();
    private string status { get => statusSource; set { statusSource = value; statusArguments = Array.Empty<object>(); } }
    private string StatusText() => statusArguments.Length == 0 ? L.Text(statusSource) : L.Format(statusSource, statusArguments);
    private void SetStatus(string template, params object[] arguments) { statusSource = template; statusArguments = arguments; }
    private long revision;
    private long pendingRevision;
    private long activeLuaToken;
    private bool selectionEnabled = true, actionWaiting, actionReleased;
    private bool diceSessionActive;
    private bool diceAwaitingFirstRoll;
    private bool diceRollRequested;
    private string actualLocation = "";
    public long BoundaryEpoch { get; private set; }
    private readonly HashSet<long> cancelledLuaTokens = new();
    public bool IsLuaCancelled(long token) => cancelledLuaTokens.Contains(token);
    private DiceMenuDialog? diceDialog;
    private string diceName = "";
    private string[] diceOptions = Array.Empty<string>();
    private bool rolling;
    private int? pendingDice;
    // The branch the player picked: the native result must land there (corrected after the roll if it does not).
    private int? pendingBranch, intendedBranch;
    private int pendingAddition;
    private string pendingDiceName = "";
    private readonly Dictionary<string, ValueBinding> values = new();
    private readonly HashSet<string> ambiguousFields = new(StringComparer.Ordinal);
    private readonly FieldIdentityIndex fieldIdentities = new();
    private readonly Dictionary<string, PositionEventData> eventObjects = new();
    private readonly Dictionary<string, string> effectSummaries = new();
    private long presentationRevision = -1;
    public ToolSnapshot Snapshot { get; private set; } = new();
    /// <summary>Diagnostics only: the game assemblies match the build this version was developed on. Nothing is gated on it.</summary>
    public bool TestedBuild { get; set; }
    /// <summary>Story script text and the flags an event script changes (the option hints' reading); set by the plugin.</summary>
    public Func<string, string?>? ScriptText { get; set; }
    public Func<string, Dictionary<string, List<OptionEffect>>>? EventFlagChanges { get; set; }
    /// <summary>Consequences of an event script by name (the option hints' analysis); set by the plugin.</summary>
    public Func<string, List<HintSegment>?>? EventConsequences { get; set; }
    public string RuntimeVersion { get; set; } = "运行版本待核验";
    public GameAdapter(ManualLogSource log, bool selectBeforeAction = true, Action<bool>? selectionChanged = null)
    { this.log = log; selectionEnabled = selectBeforeAction; this.selectionChanged = selectionChanged; }

    // The event last specified and consumed at each action pool, kept across sessions as "pool=eventId|…".
    private readonly Dictionary<string, string> lastChoices = new(StringComparer.Ordinal);
    public Action<string>? LastChoicesChanged;
    public void LoadLastChoices(string saved)
    {
        foreach (var pair in (saved ?? "").Split('|'))
        {
            int at = pair.IndexOf('=');
            if (at > 0 && at < pair.Length - 1) lastChoices[pair.Substring(0, at)] = pair.Substring(at + 1);
        }
    }

    public static string StatName(GameStat stat) => L.StatName(stat);
    private static string Identity()
    {
        var p = PlayerStatManagerData.Instance;
        return SceneManager.GetActiveScene().name + "|" + (p == null ? "" : p.CurrentStoryScript + "|" + p.CurrentScene + "|" + p.CurrentSceneKey);
    }
    public void Invalidate(string reason, bool boundary = false, bool clearLocation = false)
    {
        if (actionWaiting || actionReleased) cancelledLuaTokens.Add(activeLuaToken);
        if (boundary) BoundaryEpoch++;
        if (clearLocation) actualLocation = "";
        revision++; poolName = ""; pendingEvent = ""; pendingDice = null; pendingBranch = intendedBranch = null; rolling = false; previewPool = previewTitle = "";
        actionWaiting = actionReleased = false;
        diceSessionActive = false;
        diceAwaitingFirstRoll = false;
        diceRollRequested = false;
        contextIdentity = Identity(); status = reason;
        log.LogInfo($"CONTEXT_INVALIDATED rev={revision} reason={reason} identity={contextIdentity}");
    }
    public long LuaLoaded(string code, string friendlyName)
    {
        // Loading records a node token only. Actual checkpoint execution establishes the context.
        Invalidate("当前没有待选择的事件");
        var matches = Regex.Matches(code ?? "", "checkpointmanager\\s*[.:]\\s*Position\\s*\\(\\s*[\"']([^\"']+)[\"']\\s*\\)").Cast<Match>().Select(x => x.Groups[1].Value).Distinct().ToList();
        activeLuaToken = revision;
        log.LogInfo($"LUA_NODE frame={Time.frameCount} rev={revision} name={friendlyName} candidates={matches.Count} scene={Identity()}");
        Refresh();
        return activeLuaToken;
    }
    public void EnterLocation(PositionController controller)
    {
        var position = R.Field(controller, "_position");
        actualLocation = position?.ToString() ?? "";
        log.LogInfo("LOCATION_ENTER " + actualLocation);
    }
    public void ClearLocation() { actualLocation = ""; }
    public bool WaitForPosition(string name, long token)
    {
        try
        {
            if (!selectionEnabled || token != activeLuaToken) return false;
            if (poolName == name && actionWaiting) return true;
            if (poolName == name && actionReleased) { actionReleased = false; return false; }
            revision++; poolName = name; pendingEvent = ""; actionReleased = false;
            contextIdentity = Identity();
            var pool = Pool();
            if (pool == null || pool.List.Count <= 1) { poolName = ""; return false; }
            actionWaiting = true; status = "等待继续本次行动"; Refresh();
            if (!Snapshot.Ready) { actionWaiting = false; return false; }
            Plugin.Instance?.ShowForAction();
            log.LogInfo($"EVENT_WAIT frame={Time.frameCount} rev={revision} node={token} pool={name} count={Snapshot.Events.Count}");
            return true;
        }
        catch (Exception ex) { actionWaiting = false; pendingEvent = ""; log.LogError(ex); return false; }
    }
    public OperationResult SetActionSelection(bool enabled)
    {
        selectionEnabled = enabled;
        selectionChanged?.Invoke(enabled);
        if (!enabled) ContinueAction();
        return Feedback(true, enabled ? "行动前选择结果已开启" : "行动前选择结果已关闭");
    }
    public OperationResult ContinueAction()
    {
        if (actionWaiting) { actionWaiting = false; actionReleased = true; status = "继续本次行动"; log.LogInfo($"EVENT_RELEASE frame={Time.frameCount} pool={poolName} pending={pendingEvent}"); }
        Refresh(); return OperationResult.Ok(StatusText());
    }
    private PositionResultData? Pool() => poolName.Length == 0 ? null : FindPool(poolName);
    private static PositionResultData? FindPool(string name) => Pools()?.List.FirstOrDefault(x => x.name == name);
    // The pool table belongs to the story scene; kept from there so the free map (F32 gate reminders) can still read it.
    // While this reference lives, Unity keeps the asset loaded. Right after loading a save into the map it is not known
    // until the first story scene.
    private static PositionResultConfig? pools;
    private static PositionResultConfig? Pools()
    {
        if (CheckPointManager.Instance != null && R.Field<PositionResultConfig>(CheckPointManager.Instance, "_position") is PositionResultConfig live && live != null) pools = live;
        return pools != null ? pools : null;
    }
    private string previewPool = "", previewTitle = "";
    /// <summary>Read-only preview of the pool a hovered menu row would reach. Never specifiable, never consumed.</summary>
    public void PreviewPool(string pool, string title)
    {
        if (previewPool == pool && previewTitle == title) return;
        previewPool = pool; previewTitle = title; Refresh();
    }
    public void ClearPreview() { if (previewPool.Length == 0) return; previewPool = previewTitle = ""; Refresh(); }
    public void Refresh()
    {
        try
        {
            L.RefreshLanguage();
            if (presentationRevision != L.Revision) { presentationRevision = L.Revision; effectSummaries.Clear(); }
            var identity = Identity();
            if (contextIdentity != identity) Invalidate("场景已变化，旧指定已清除");
            var p = PlayerStatManagerData.Instance;
            var scene = SceneManager.GetActiveScene().name;
            var ready = p != null && p.Stats != null && (scene.IndexOf("story", StringComparison.OrdinalIgnoreCase) >= 0 || scene.IndexOf("free", StringComparison.OrdinalIgnoreCase) >= 0);
            var next = new ToolSnapshot { Ready = ready, Revision = revision, Version = RuntimeVersion, Location = ready ? (actualLocation.Length > 0 ? L.DisplayText(actualLocation) : L.Text(scene.IndexOf("free", StringComparison.OrdinalIgnoreCase) >= 0 ? "当前地图" : "当前剧情")) : "", Action = poolName.Length > 0 ? L.Text("活动事件") : "", ContextDetail = identity + "\n" + L.Text("检查点") + " " + poolName, Status = StatusText(), PendingEventId = pendingEvent, ActionWaiting = actionWaiting, ActionSelectionEnabled = selectionEnabled };
            values.Clear(); ambiguousFields.Clear(); fieldIdentities.Clear(); eventObjects.Clear();
            if (ready && p != null)
            {
                foreach (var stat in p.Stats!.List.OrderBy(stat => stat == null ? int.MaxValue : ValueSemantics.SafeCatalogOrder(stat.GetLocaleKey)))
                {
                    if (stat == null) continue;
                    try
                    {
                        var id = "stat:" + stat.StatType;
                        int addition = stat.AdditionValue;
                        long cap = (long)stat.Max + R.RequiredField<int>(stat, "_limitAddition");
                        int min = stat.FinalValue, max = stat.FinalValue;
                        bool writable = cap >= int.MinValue && cap <= int.MaxValue && ValueSemantics.TryRange(stat.Min, stat.Max, addition, (int)cap, out min, out max);
                        var view = ValueCatalog.View(id, StatName(stat), ValueSemantics.GroupId(stat.GetLocaleKey(), (int)stat.PropertyType), stat.FinalValue, min, max, writable);
                        view.BaseValue = stat.Value; view.AdditionValue = addition;
                        view.EnumValues = L.EnumValues(stat.GetLocaleKey());
                        view.Reason = writable ? "" : L.Text("加成或范围无法安全反推，暂只读");
                        view.Detail += "\n" + stat.GetLocaleKey() + "\n" + L.Format("基础 {0}；加成 {1}；最终 {2}", stat.Value, addition, stat.FinalValue) + "\n" + L.Text("原生分组") + " " + stat.PropertyType;
                        if (ValueSemantics.IsRetainedMartial(stat.GetLocaleKey()))
                        {
                            view.Detail += "\n" + L.Text("保留字段；尚未确认当前玩法用途。") + "\n" + L.Text("静态检索：当前核心配置、脚本文本及直接枚举调用未发现引用。");
                            if (stat.GetLocaleKey() == "PlayerStat/m-internal") view.Detail += "\n" + L.Text("独立于内力、阴阳内功。");
                        }
                        AddValue(next, new(stat, view, () => stat.FinalValue, value =>
                        {
                            int currentAddition = stat.AdditionValue;
                            long currentCap = (long)stat.Max + R.RequiredField<int>(stat, "_limitAddition");
                            if (currentCap < int.MinValue || currentCap > int.MaxValue || !ValueSemantics.TryBase(value, stat.Value, stat.Min, stat.Max, currentAddition, (int)currentCap, out int targetBase)) throw new InvalidOperationException(L.Text("目标最终值不可达"));
                            stat.SetValue(targetBase);
                        }));
                    }
                    catch (Exception ex) { log.LogWarning("VALUE_READ stat=" + stat.StatType + " " + ex.GetBaseException().Message); }
                }
                foreach (var relation in p.Relationships.List)
                {
                    if (relation == null) continue;
                    var id = "relation:" + relation.Type;
                    var view = ValueCatalog.View(id, L.RelationName(relation.Type), "relationships", relation.Value, 0, R.RequiredField<int>(relation, "_max"), true);
                    AddValue(next, new(relation, view, () => relation.Value, relation.SetValue));
                }
                var missions = MissionManagerData.Instance;
                var facilityFlags = FacilityFlags(p);
                if (missions != null) foreach (var group in missions.FlagsCollection.Where(x => x != null).SelectMany(collection => collection.List).Where(x => x != null).GroupBy(flag => flag.name, StringComparer.Ordinal))
                {
                    foreach (var flag in group)
                    {
                        var id = "flag:" + flag.name; var info = FlagDictionary.GetInfo(id);
                        var view = ValueCatalog.View(id, L.FlagName(flag), info.GroupId, flag.State, int.MinValue, int.MaxValue, true);
                        view.Reason = info.Verified ? "" : L.Text("原始整数；语义和状态范围尚未核验");
                        view.Detail += "\n" + flag.GetNameKey() + "\n" + L.Text(info.Meaning) + (string.IsNullOrWhiteSpace(flag.DevNote) ? "" : "\n" + L.Text("开发备注，名称未核验") + "\n" + L.DisplayText(flag.DevNote));
                        if (facilityFlags.TryGetValue(flag, out var facility))
                        {
                            view.Name = facility.Item1 + " · " + L.Text("等级"); view.GroupId = "development"; view.Group = L.GroupName(view.GroupId);
                            view.Min = 0; view.Max = facility.Item2; view.Reason = L.Text("设施等级范围来自原生配置");
                        }
                        view.SearchText = id + " " + view.Name + " " + flag.DevNote + " " + info.Name;
                        AddValue(next, new(flag, view, () => flag.State, value => flag.State = value));
                    }
                }
                try { foreach (var binding in ValueCatalog.Read(p)) AddValue(next, binding); }
                catch (Exception ex) { log.LogWarning("VALUE_CATALOG " + ex.GetBaseException().Message); }
                try { next.MapLocations = MapOverview.Read(p, missions); }
                catch (Exception ex) { log.LogWarning("MAP_READ " + ex.GetBaseException().Message); }
                try { using (Perf.Time("one-shot")) AddOneShots(next.MapLocations, p); }
                catch (Exception ex) { log.LogWarning("ONE_SHOT " + ex.GetBaseException().Message); }
                var pool = Pool();
                if (pool != null)
                {
                    next.Events = ReadPoolEvents(pool, p, true);
                    if (actionWaiting && lastChoices.TryGetValue(pool.name, out var last) && next.Events.Any(x => x.Id == last)) next.RepeatEventId = last;
                }
                // Hovering a menu row previews its pool; real checkpoints always take precedence.
                else if (previewPool.Length > 0 && FindPool(previewPool) is PositionResultData preview)
                { next.PreviewEvents = ReadPoolEvents(preview, p, false); next.PreviewTitle = previewTitle; }
            }
            if (pendingEvent.Length > 0 && (!next.Events.Any(x => x.Id == pendingEvent && x.Eligible) || pendingRevision != revision))
            { pendingEvent = ""; next.PendingEventId = ""; status = "条件或场景变化，指定已清除"; next.Status = StatusText(); }
            next.Dice = ReadDice();
            Snapshot = next;
        }
        catch (Exception ex) { SetStatus("读取失败：{0}", ex.GetBaseException().Message); log.LogError(ex); Snapshot.Status = StatusText(); Snapshot.Ready = false; Snapshot.Events.Clear(); Snapshot.MapLocations.Clear(); Snapshot.Values.Clear(); }
    }
    // One-shot events reachable from a location: found once per entry script, then checked against current values.
    private sealed class OneShot { public string Pool = "", Item = ""; public List<string> Path = new(); }
    private readonly Dictionary<string, List<OneShot>> oneShots = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// A one-shot event is gone after it runs once: the flag changes its script always makes leave its own trigger
    /// condition unmet (F40: a counter that allows several runs, like 行商 大失败 under "< 2", is not one). Those whose
    /// conditions hold now are added, green, to the location that reaches them (F32 the gate, F41 every location).
    /// </summary>
    private void AddOneShots(List<MapLocationSnapshot> locations, PlayerStatManagerData p)
    {
        foreach (var location in locations)
        {
            if (location.EntryScript.Length == 0 || ScriptText == null || EventFlagChanges == null) continue;
            foreach (var shot in OneShotsFrom(location.EntryScript))
            {
                if (FindPool(shot.Pool) is not PositionResultData pool || pool.List.FirstOrDefault(x => x.name == shot.Item) is not PositionEventData item) continue;
                var ev = ReadPoolEvent(pool, item, p, false);
                if (!ev.Eligible) continue;
                string place = string.Join(" · ", shot.Path.Skip(Math.Max(0, shot.Path.Count - 2)).Select(OptionTitle).Where(x => x.Length > 0));
                location.Entries.Add(new MapEventSnapshot
                {
                    Id = "oneshot:" + ev.Id, Title = place.Length > 0 ? L.Format("{0} · {1}", place, ev.Title) : ev.Title, Ready = true, Known = true, Conditions = ev.Conditions,
                    Detail = L.Text("一次性事件：触发后条件不再满足") + "\n" + (place.Length > 0 ? place + "\n" : "") + ev.Detail
                });
            }
        }
    }
    /// <summary>
    /// One-shot events that can run now behind a menu option (F43): its own pool and whatever its next script reaches.
    /// Titles in the game's language; empty when nothing qualifies or the pool table is not known yet.
    /// </summary>
    public List<string> ReadyOneShots(string pool, string nextScript)
    {
        var titles = new List<string>();
        try
        {
            if (ScriptText == null || EventFlagChanges == null || PlayerStatManagerData.Instance is not PlayerStatManagerData p || p == null) return titles;
            var shots = new List<OneShot>();
            if (pool.Length > 0) shots.AddRange(OneShotsFrom(pool, true));
            if (nextScript.Length > 0) shots.AddRange(OneShotsFrom(nextScript));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var shot in shots)
            {
                if (!seen.Add(shot.Pool + "/" + shot.Item)) continue;
                if (FindPool(shot.Pool) is not PositionResultData data || data.List.FirstOrDefault(x => x.name == shot.Item) is not PositionEventData item) continue;
                var ev = ReadPoolEvent(data, item, p, false);
                if (ev.Eligible) titles.Add(ev.Title);
            }
        }
        catch (Exception ex) { log.LogWarning("ONE_SHOT option " + ex.GetBaseException().Message); }
        return titles;
    }
    private List<OneShot> OneShotsFrom(string entry, bool fromPool = false)
    {
        string key = (fromPool ? "pool:" : "") + entry;
        if (oneShots.TryGetValue(key, out var found)) return found;
        found = new List<OneShot>();
        // Without the pool table nothing past the entry script can be followed; try again on a later refresh.
        if (Pools() == null) return found;
        var reached = LuaOptions.Reach(entry, name => ScriptText!(name),
            name => FindPool(name) is PositionResultData data ? data.List.Select(item => R.Field<string>(data, "_scriptPrefix", "") + "_" + item.ResultType).ToList() : new List<string>(), entryIsPool: fromPool);
        foreach (var target in reached)
        {
            if (FindPool(target.Pool) is not PositionResultData pool) continue;
            foreach (var item in pool.List)
            {
                if (R.Field(item, "_toggleCondition") is not true) continue;
                var changes = EventFlagChanges!(R.Field<string>(pool, "_scriptPrefix", "") + "_" + item.ResultType);
                if (!Conditions.EndsAfterOnce(R.Field(item, "_activeCondition"), changes)) continue;
                found.Add(new OneShot { Pool = pool.name, Item = item.name, Path = target.Path });
            }
        }
        log.LogInfo($"ONE_SHOT entry={key} pools={reached.Count} candidates={found.Count}");
        return oneShots[key] = found;
    }
    private static string OptionTitle(string key) => Regex.Replace(L.GameText("Story/" + key, ""), "<[^>]+>", "").Trim();

    /// <summary>Events of one position pool. Only the actual pool registers objects that can be specified.</summary>
    private List<EventSnapshot> ReadPoolEvents(PositionResultData pool, PlayerStatManagerData p, bool actual)
    {
        var events = new List<EventSnapshot>();
        foreach (var item in pool.List) events.Add(ReadPoolEvent(pool, item, p, actual));
        return events;
    }
    private EventSnapshot ReadPoolEvent(PositionResultData pool, PositionEventData item, PlayerStatManagerData p, bool actual)
    {
        {
            var id = pool.name + "/" + item.name + "/" + item.ResultType;
            if (actual) eventObjects[id] = item;
            var condition = R.Field(item, "_toggleCondition") is bool toggleCondition ? (toggleCondition ? Conditions.Read(R.Field(item, "_activeCondition")) : new ConditionSnapshot { Text = L.Text("无条件"), Satisfied = true }) : new ConditionSnapshot { Text = L.Text("条件未核验"), Known = false, Detail = L.Text("启用条件字段不可读") };
            var weight = R.RequiredField<int>(item, "_defaultRate");
            var conditions = new List<ConditionSnapshot> { condition };
            var weightDetails = new List<string>();
            var known = condition.Known;
            foreach (var rateItem in R.Items(R.RequiredField<System.Collections.IEnumerable>(item, "_eventRateItem")))
            {
                var rateCondition = Conditions.Read(R.Property(rateItem, "Condition"));
                var rateValue = R.Property(rateItem, "Rate");
                if (rateValue is not int rate) throw new InvalidOperationException(L.Text("权重调整字段不可读"));
                known &= rateCondition.Known;
                if (rateCondition.Satisfied) weight += rate;
                rateCondition.Detail += "\n" + L.Format("权重调整 {0}", rate.ToString("+0;-0;0"));
                weightDetails.Add(L.Format("权重 {0}：{1}（{2}）", rate.ToString("+0;-0;0"), Conditions.Expression(rateCondition), L.Text(!rateCondition.Known ? "未核验" : rateCondition.Satisfied ? "满足" : "未满足")));
                // Weight modifiers are detail, not mandatory activation requirements.
            }
            weight = condition.Satisfied && known ? Math.Max(0, weight) : 0;
            var note = R.Field<string>(item, "_devNote", "");
            var mapping = R.Field(item, "_config"); var mappingName = R.Name(mapping); var mappingKey = R.Field<string>(mapping, "Key", "");
            var script = R.Field<string>(pool, "_scriptPrefix", "") + "_" + item.ResultType;
            var effects = EffectSummary(script, p);
            var label = !string.IsNullOrWhiteSpace(note) ? note.Trim() : !string.IsNullOrWhiteSpace(mappingKey) ? mappingKey : mappingName;
            if (string.IsNullOrWhiteSpace(label) || Regex.IsMatch(label, @"^[A-Za-z0-9_]+$")) label = effects.Length > 0 ? Tone.Plain(effects) : L.Text("未命名结果") + " · " + item.ResultType;
            return new() { Id = id, Title = L.DisplayText(label), Detail = item.name + "\n" + L.Text("映射") + " " + mappingName + " / " + mappingKey + "\n" + L.Text("结果") + " " + script + (effects.Length > 0 ? "\n" + L.Text("固定数值调用") + " " + effects : "") + "\n" + L.Format("基础权重 {0}；最终权重 {1}", R.Field<int>(item, "_defaultRate"), known ? weight.ToString() : L.Text("未核验")) + (weightDetails.Count > 0 ? "\n" + string.Join("\n", weightDetails) : ""), Eligible = weight > 0, Weight = weight, Conditions = conditions, Consequences = EventConsequences?.Invoke(script) ?? new List<HintSegment>() };
        }
    }
    private string EffectSummary(string script, PlayerStatManagerData player)
    {
        if (effectSummaries.TryGetValue(script, out var cached)) return cached;
        var prefix = R.Field<string>(LuaManager.Instance, "_scriptPrefix", "Story");
        var asset = Resources.Load<TextAsset>(prefix + "/ChineseTraditional/" + script.ToLowerInvariant());
        if (asset == null) return effectSummaries[script] = "";
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var stat in player.Stats.List)
        {
            var localeKey = stat.GetLocaleKey(); labels[localeKey.Substring(localeKey.LastIndexOf('/') + 1)] = StatName(stat);
        }
        // Rises and falls carry the mod's one colour meaning wherever the detail is shown.
        var changes = LuaGate.FixedEffects(asset.text).Where(pair => labels.ContainsKey(pair.Key)).Select(pair => labels[pair.Key] + " " + Tone.Mark(pair.Value, pair.Value.ToString("+0;-0;0"))).ToList();
        return effectSummaries[script] = string.Join(" · ", changes);
    }
    private OperationResult Feedback(bool success, string message, params object[] arguments)
    {
        SetStatus(message, arguments); string display = StatusText(); log.LogInfo($"ACTION success={success} {display}"); Refresh();
        return success ? OperationResult.Ok(display) : OperationResult.Fail(display);
    }
    private void AddValue(ToolSnapshot snapshot, ValueBinding binding)
    {
        var view = binding.View;
        var identity = fieldIdentities.Register(view.Id, binding.Source);
        if (identity != FieldIdentity.New)
        {
            // Unity's Object equality is not used for identity: one reference may appear in many collections.
            if (identity == FieldIdentity.SameReference) return;
            var previous = values[view.Id];
            ambiguousFields.Add(view.Id);
            previous.View.Writable = false; previous.View.Reason = L.Text("同名字段，编辑已停用");
            previous.View.Detail += "\n" + L.Text("不同对象共用标识") + " " + binding.Read();
            return;
        }
        values.Add(view.Id, binding); snapshot.Values.Add(view);
    }
    private static Dictionary<FlagData, Tuple<string, int>> FacilityFlags(PlayerStatManagerData player)
    {
        var result = new Dictionary<FlagData, Tuple<string, int>>();
        var config = R.Field<FacilityConfigData>(player, "_facility");
        if (config == null || config.Data == null) return result;
        foreach (var facility in config.Data)
        {
            if (facility == null) continue;
            var flag = R.Field<FlagData>(facility, "_level");
            var levels = R.Field<Array>(facility, "_levelData");
            if (flag != null && levels != null) result[flag] = Tuple.Create(L.GameText(facility.GetNameKey(), facility.name), levels.Length);
        }
        return result;
    }
    public OperationResult SetValue(string fieldId, string input)
    {
        Refresh();
        if (!Snapshot.Ready) return Feedback(false, "当前场景不可编辑");
        if (string.IsNullOrWhiteSpace(input) || !int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var target)) return Feedback(false, "请输入整数");
        var definition = Snapshot.Values.FirstOrDefault(x => x.Id == fieldId);
        if (definition == null) return Feedback(false, "字段不存在");
        if (ambiguousFields.Contains(fieldId)) return Feedback(false, "同名字段，无法精确写入");
        if (!definition.Writable || !values.TryGetValue(fieldId, out var binding) || binding.Write == null) return Feedback(false, "字段不可写");
        if (target < definition.Min || target > definition.Max) return Feedback(false, "范围 {0}–{1}", definition.Min, definition.Max);
        var before = definition.Value;
        try
        {
            bool confirmed = ValueSemantics.WriteAndVerify(target, binding.Write, binding.Read, out var actual);
            log.LogInfo($"VALUE_WRITE field={fieldId} before={before} target={target} readback={actual}");
            // Converters may change after native events. Never claim success from the requested base alone.
            return confirmed ? Feedback(true, "已更新：{0}", actual) : Feedback(false, "写后读回不一致：目标 {0}，实际 {1}", target, actual);
        }
        catch (Exception ex) { log.LogWarning("VALUE_WRITE_FAILED " + fieldId + " " + ex.GetBaseException().Message); return Feedback(false, "写入失败：{0}", ex.GetBaseException().Message); }
    }
    public OperationResult SpecifyEvent(string eventId)
    {
        Refresh();
        if (!Snapshot.Ready || !actionWaiting || Pool() == null || !eventObjects.ContainsKey(eventId)) return Feedback(false, "事件不在当前行动");
        var item = Snapshot.Events.FirstOrDefault(x => x.Id == eventId);
        if (item == null || !item.Eligible) return Feedback(false, "条件未满足或权重为零");
        pendingEvent = eventId; pendingRevision = revision;
        return Feedback(true, "已指定下一次当前行动");
    }
    public OperationResult CancelEvent() { pendingEvent = ""; return Feedback(true, "事件恢复随机"); }
    public OperationResult RepeatLastChoice()
    {
        Refresh();
        var id = Snapshot.RepeatEventId;
        if (id.Length == 0) return Feedback(false, "这个行动还没有上次的选择");
        var specified = SpecifyEvent(id);
        if (!specified.Success) return specified;
        string title = Snapshot.Events.FirstOrDefault(x => x.Id == id)?.Title ?? id;
        ContinueAction();
        return Feedback(true, "已重复上次选择：{0}", title);
    }
    public bool TryEvent(PositionResultData pool, out string result)
    {
        result = ""; Refresh();
        if (pendingEvent.Length == 0) return false;
        var id = pendingEvent; pendingEvent = ""; // A real request consumes or rejects this token once.
        if (!Snapshot.Ready || pendingRevision != revision || !ReferenceEquals(Pool(), pool) || !eventObjects.TryGetValue(id, out var item) || item.GetRate() <= 0)
        { Feedback(false, "本次指定失效，使用游戏随机"); return false; }
        result = R.Field<string>(pool, "_scriptPrefix", "") + "_" + item.ResultType;
        if (!lastChoices.TryGetValue(pool.name, out var previous) || previous != id)
        {
            lastChoices[pool.name] = id;
            LastChoicesChanged?.Invoke(string.Join("|", lastChoices.Select(pair => pair.Key + "=" + pair.Value)));
        }
        log.LogInfo($"EVENT_CONSUMED rev={revision} id={id} result={result}"); SetStatus("已执行指定：{0}", item.ResultType); Refresh(); return true;
    }
    public void ObservePosition(string name) { log.LogInfo($"POSITION_EXECUTE name={name} context={poolName} rev={revision} pending={pendingEvent}"); }
    public void CompletePosition(string name, string result)
    {
        log.LogInfo($"POSITION_RESULT frame={Time.frameCount} name={name} result={result}");
        poolName = ""; pendingEvent = ""; actionWaiting = actionReleased = false; revision++; Refresh();
    }
    public void BeginDice(DiceMenuDialog dialog, string name, string[] options)
    {
        pendingDice = null; pendingBranch = intendedBranch = null;
        diceDialog = dialog; diceName = name; diceOptions = options; rolling = false;
        diceSessionActive = true;
        diceAwaitingFirstRoll = true;
        diceRollRequested = false;
        log.LogInfo($"DICE_ACTIVE name={name} options={options.Length}"); Refresh();
    }
    public void BeginRoll(DiceMenuDialog dialog, string name) { diceDialog = dialog; diceName = name; diceAwaitingFirstRoll = false; diceRollRequested = false; rolling = true; log.LogInfo("DICE_REAL_ROLL " + name); }
    public void BeforeDice(string name, ref int random)
    {
        if (!rolling || name != diceName) { log.LogInfo($"DICE_READ name={name} raw={random} consume=false"); return; }
        rolling = false; intendedBranch = null;
        var dice = ReadDice();
        var target = pendingDice; var branch = pendingBranch; pendingDice = null; pendingBranch = null;
        if (!target.HasValue && !branch.HasValue) return;
        if (pendingDiceName != name || pendingAddition != dice.Addition) { status = "骰子条件变化，指定失效"; log.LogWarning("DICE_REJECTED " + name); return; }
        if (target.HasValue)
        {
            // The game adds its own bonuses to this raw value and picks the branch itself; a raw value outside the
            // native range (e.g. −21 with +100) keeps the shown arithmetic true. The wheel shows 零零 below zero.
            random = target.Value;
            if (diceDialog != null) R.Set(diceDialog, "_currentRandomValue", target.Value);
            SetStatus("已执行骰子指定：{0}", target.Value);
        }
        intendedBranch = branch;
        log.LogInfo($"DICE_CONSUMED name={name} raw={target?.ToString() ?? "native"} branch={branch} addition={dice.Addition}");
    }
    public void AfterDice(string name, int raw, DiceCheckResult result)
    {
        if (intendedBranch.HasValue && name == diceName)
        {
            // The picked branch wins even if the game's bonuses differ from this mod's reading of them.
            if (result.Result != intendedBranch.Value) { log.LogWarning($"DICE_FORCED name={name} native={result.Result} picked={intendedBranch}"); result.Result = intendedBranch.Value; }
            intendedBranch = null;
        }
        log.LogInfo($"DICE_RESULT name={name} raw={raw} final={result.ResultCount} branch={result.Result}");
    }
    private DiceSnapshot ReadDice()
    {
        var active = diceSessionActive && diceDialog != null && diceDialog.gameObject.activeInHierarchy && diceName.Length > 0;
        var headerKey = "DiceHeader/" + diceName;
        var result = new DiceSnapshot { Active = active, ManualStart = SystemSettings.ManualDestiny, Name = active ? L.GameText(headerKey, "当前骰子") : "", Detail = active ? diceName + "\n" + L.Text("只消费下一次真实掷骰；读取不消费") : L.Text("等待游戏掷骰界面"), PendingRawValue = pendingDice };
        if (!active || diceDialog == null) { pendingDice = null; result.PendingRawValue = null; return result; }
        var restart = R.Field<Component>(diceDialog, "_restartButton");
        // A requested roll owns this session until the native Roll begins; a second click cannot re-specify.
        result.CanSpecify = !diceRollRequested && (diceAwaitingFirstRoll || (restart != null && restart.gameObject.activeInHierarchy));
        result.Min = R.Field<int>(diceDialog, "_diceMin"); result.Max = R.Field<int>(diceDialog, "_diceMax", 100); result.RawValue = R.Field<int>(diceDialog, "_currentRandomValue");
        var config = R.Field<DiceResultConfig>(CheckPointManager.Instance, "_dice");
        var data = config?.List.FirstOrDefault(x => x.name == diceName);
        if (data == null) { result.Detail = L.Text("骰子配置不可读"); return result; }
        foreach (var item in R.Items(R.Field(data, "_propertyItems")))
        {
            var type = R.Property(item, "Type")?.ToString();
            float value = 0;
            switch (type)
            {
                case "數值":
                    var stat = R.Property(item, "Stat") as GameStat;
                    if (stat != null) value = stat.LevelLength == 0 ? stat.FinalValue : (GameStatUtils.GetGameStatLevel(stat.FinalValue, stat.Max, stat.LevelLength) - stat.LevelLength / 2) * 10;
                    break;
                case "好感度": value = (R.Property(item, "Relationship") as RelationshipStat)?.Value ?? 0; break;
                case "技能": value = R.IntProperty(R.Property(item, "Talent"), "Level"); break;
                case "旗標": value = (R.Property(item, "Flag") as FlagData)?.State ?? 0; break;
            }
            var rate = R.Property(item, "Rate") is float f ? f : 1f;
            result.Addition += Mathf.RoundToInt(value * (R.Property(item, "Negative") is true ? -1 : 1) * rate);
        }
        if ((pendingDice.HasValue || pendingBranch.HasValue) && (pendingAddition != result.Addition || pendingDiceName != diceName)) { pendingDice = null; pendingBranch = null; result.PendingRawValue = null; status = "骰子加成变化，指定已清除"; }
        var items = R.Items(R.Field(data, "_resultItems")).Cast<DiceResultItem>().ToList();
        // Native Roll draws Random.Range(min, max + 1) and adds the bonuses; the first matching item is the branch.
        // Every offered branch stays selectable: a raw value outside the native range is worked back if needed.
        int addition = result.Addition;
        var reachable = NativeUiState.DiceRaws(result.Min, result.Max, Math.Max(99, result.Max), Enumerable.Range(1, diceOptions.Length),
            raw => { int found = items.FindIndex(x => x.Check(raw + addition)); return found < 0 ? items.Count + 1 : found + 1; }, out var hits);
        double span = (long)result.Max - result.Min + 1;
        for (var i = 1; i <= Math.Max(items.Count + 1, diceOptions.Length); i++)
        {
            var option = i <= diceOptions.Length ? diceOptions[i - 1] : "";
            var separator = option.IndexOf('|');
            var key = separator >= 0 ? option.Substring(0, separator) : option;
            var text = key.Length > 0 ? L.GameText("Story/" + key, "") : "";
            var hasOption = i <= diceOptions.Length;
            result.Branches.Add(new() { Id = i.ToString(), Title = text.Length > 0 && text != key ? Regex.Replace(text, "<[^>]+>", "") : L.Format("结果 {0}", i), Reachable = hasOption, RawValue = hasOption && reachable.TryGetValue(i, out var raw) ? raw : (int?)null, Chance = hits != null ? (hits.TryGetValue(i, out var hit) ? hit : 0) / span : (double?)null, Detail = L.Format("分支 {0}", i) + "\n" + option + (!hasOption ? "\n" + L.Text("游戏未提供该选项") : "") });
        }
        return result;
    }
    public OperationResult SpecifyDice(int rawValue)
    {
        Refresh(); var dice = Snapshot.Dice;
        if (!Snapshot.Ready || !dice.Active) return Feedback(false, "当前没有可指定的骰子");
        if (!dice.CanSpecify) return Feedback(false, "本次骰子已计算，等待游戏继续");
        if (rawValue < dice.Min || rawValue > dice.Max) return Feedback(false, "范围 {0}–{1}", dice.Min, dice.Max);
        pendingDice = rawValue; pendingAddition = dice.Addition; pendingDiceName = diceName;
        return Feedback(true, "下一次骰子原始值 = {0}", rawValue);
    }
    /// <summary>Any offered branch can be picked: natural raw value, a worked-back one, or (beyond the wheel) a forced result.</summary>
    public OperationResult SpecifyDiceBranch(string branchId)
    {
        Refresh(); var dice = Snapshot.Dice; var branch = dice.Branches.FirstOrDefault(x => x.Id == branchId);
        if (branch == null || !branch.Reachable || !int.TryParse(branch.Id, out int index)) return Feedback(false, "该结果不可达");
        if (!Snapshot.Ready || !dice.Active) return Feedback(false, "当前没有可指定的骰子");
        if (!dice.CanSpecify) return Feedback(false, "本次骰子已计算，等待游戏继续");
        pendingDice = branch.RawValue; pendingBranch = index; pendingAddition = dice.Addition; pendingDiceName = diceName;
        return branch.RawValue is int raw ? Feedback(true, "下一次骰子原始值 = {0}", raw) : Feedback(true, "下一次骰子结果强制为：{0}", branch.Title);
    }
    public OperationResult RollDiceBranch(string branchId)
    {
        var specified = SpecifyDiceBranch(branchId);
        if (!specified.Success || diceDialog == null || !pendingBranch.HasValue) return specified;
        int? raw = pendingDice;
        bool restart = !diceAwaitingFirstRoll;
        try
        {
            // Only the game's own start paths: the wheel, result display and consumption stay native.
            if (restart)
            {
                // Same as the native 重掷 button: its fate-point rule, then the restart flag.
                var useFatePoint = AccessTools.Method(typeof(DiceMenuDialog), "UseFatePoint") ?? throw new MissingMethodException("DiceMenuDialog.UseFatePoint");
                useFatePoint.Invoke(diceDialog, null);
                diceDialog.PressRestartButton();
            }
            // Auto mode rolls by itself; only the manual wheel waits for its start button.
            else if (SystemSettings.ManualDestiny) diceDialog.PressStartButton();
        }
        catch (Exception ex)
        {
            // Never leave a specification armed for a roll that did not start.
            pendingDice = null; pendingBranch = null; log.LogWarning("DICE_ROLL_FAILED " + diceName + " " + ex.GetBaseException().Message);
            return Feedback(false, "无法开始掷骰：{0}", ex.GetBaseException().Message);
        }
        diceRollRequested = true;
        log.LogInfo($"DICE_ROLL_REQUEST name={diceName} raw={raw?.ToString() ?? "forced"} manual={SystemSettings.ManualDestiny} restart={restart}");
        return raw.HasValue ? Feedback(true, "已指定 {0}，开始掷骰", raw.Value) : Feedback(true, "已指定结果，开始掷骰");
    }
    public OperationResult CancelDice() { pendingDice = null; pendingBranch = null; return Feedback(true, "骰子恢复随机"); }
    public void EndDice(DiceMenuDialog dialog)
    {
        if (ReferenceEquals(dialog, diceDialog)) { diceSessionActive = false; diceAwaitingFirstRoll = false; diceRollRequested = false; pendingDice = null; pendingBranch = intendedBranch = null; rolling = false; }
    }
}
