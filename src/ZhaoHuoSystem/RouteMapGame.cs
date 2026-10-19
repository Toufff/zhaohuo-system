using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using Mortal.Core;
using Mortal.Story;
using UnityEngine;

namespace ZhaoHuoSystem;

/// <summary>
/// 剧情路线图 (F89), the game's side: the story scripts, missions, conditions and free-play event tables the game has
/// loaded, its texts (story record, options, dialogue), the read-story record and this playthrough's story record.
/// Everything is read on the main thread; a script's text is let go as soon as the builder has read it.
/// </summary>
internal sealed class RouteMapGame : IRouteSource
{
    private readonly Dictionary<string, TextAsset> assets = new(StringComparer.Ordinal);
    private readonly List<MissionCheckData> checks = new();
    private readonly Dictionary<string, object> records = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> notes = new(StringComparer.Ordinal);
    private readonly HashSet<string> visited = new(StringComparer.Ordinal);
    // By name, read once: the game's own lookups go through every record and its native name each time.
    private readonly Dictionary<string, object> switchData = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MissionCheckData> checkByKey = new(StringComparer.Ordinal);
    public string CurrentMain { get; }

    public RouteMapGame()
    {
        string prefix = LuaManager.Instance != null ? R.Field<string>(LuaManager.Instance, "_scriptPrefix", "Story") ?? "Story" : "Story";
        foreach (var asset in Resources.LoadAll<TextAsset>(prefix + "/ChineseTraditional"))
            if (asset != null) assets[asset.name.ToLowerInvariant()] = asset;
        var missions = MissionManagerData.Instance;
        if (missions != null)
        {
            void Add(object? data) { foreach (var check in R.Items(R.Field(data, "_missions")).OfType<MissionCheckData>()) if (check != null) checks.Add(check); }
            Add(missions.Main);
            foreach (var data in R.Items(R.Field(R.Field(missions, "_subMissions"), "_list"))) Add(data);
            foreach (var flag in missions.FlagsCollection.Where(x => x != null).SelectMany(x => x.List).Where(x => x != null))
                if (!notes.ContainsKey(flag.name)) notes[flag.name] = (flag.DevNote ?? "").Replace("\r\n", " ").Replace("\n", " ");
            CurrentMain = missions.Main?.CurrentKey ?? "";
        }
        else CurrentMain = "";
        var checkpoints = CheckPointManager.Instance;
        foreach (var condition in R.Items(R.Field(R.Field(checkpoints, "_condition"), "_list")))
            if (condition is UnityEngine.Object asset && asset != null && !records.ContainsKey(asset.name)) records[asset.name] = condition;
        foreach (var config in R.Field<SwitchResultConfig[]>(checkpoints, "_switchList") ?? new SwitchResultConfig[0])
            foreach (var data in R.Items(R.Field(config, "_list")))
                if (data is UnityEngine.Object asset && asset != null && !switchData.ContainsKey(asset.name)) switchData[asset.name] = data;
        foreach (var check in checks) if (!checkByKey.ContainsKey(check.Key)) checkByKey[check.Key] = check;
        if (PlayerStatManagerData.Instance is PlayerStatManagerData player && player != null)
            foreach (string key in player.StoryKeyList) visited.Add(key);
    }

    public IEnumerable<string> Scripts => assets.Keys.ToList();

    public string? Text(string script)
    {
        if (!assets.TryGetValue(script, out var asset)) return null;
        assets.Remove(script);
        if (asset == null) return null;
        string text = asset.text;
        Resources.UnloadAsset(asset);
        return text;
    }

    /// <summary>Lets go of the texts the builder did not ask for.</summary>
    public void Release()
    {
        foreach (var asset in assets.Values) if (asset != null) Resources.UnloadAsset(asset);
        assets.Clear();
    }

    public IEnumerable<RouteMission> Missions => checks.Select(check =>
    {
        int[]? Time(string field) => R.Field<GameTime>(check, field) is GameTime t && t.Year > 0 ? new[] { t.Year, t.Month, (int)t.Stage } : null;
        return new RouteMission
        {
            Key = check.Key, Start = check.StartScript ?? "", Desc = R.Field<string>(check, "_desc", "") ?? "", Active = R.Field(check, "_active") is true,
            TimeType = R.Field(check, "_timeCheckType") is TimeCheckType type ? (int)type : 0, Specific = Time("_specificTime"), Min = Time("_minTime"), Max = Time("_maxTime"),
            Groups = R.Items(R.Field(check, "_conditions")).Select(Conditions.RouteGroupOf).ToList()
        };
    }).ToList();

    /// <summary>Read as they are asked for, so the builder can let a frame go between them.</summary>
    public IEnumerable<RouteEvent> Events
    {
        get
        {
            foreach (var table in R.Items(R.Field(R.Field(CheckPointManager.Instance, "_position"), "_list")).ToList())
            {
                string prefix = R.Field<string>(table, "_scriptPrefix", "") ?? "";
                if (prefix.Length == 0) continue;
                foreach (var item in R.Items(R.Field(table, "_list")))
                {
                    if (item == null || R.Property(item, "ResultType") is not string value || value.Length == 0) continue;
                    yield return new RouteEvent
                    {
                        Script = (prefix + "_" + value).ToLowerInvariant(), Name = (R.Field<string>(item, "_devNote", "") ?? "").Trim(), Table = R.Name(table),
                        Condition = R.Field(item, "_activeCondition") is object condition ? Conditions.RouteGroupOf(condition) : null
                    };
                }
            }
        }
    }

    private object? Record(string name) => records.TryGetValue(name, out var condition) ? condition : switchData.TryGetValue(name, out var data) ? data : null;
    public IReadOnlyList<RouteGroup>? Groups(string name) => Record(name) is object record ? R.Items(R.Field(record, "_items")).Select(Conditions.RouteGroupOf).ToList() : null;

    private readonly Dictionary<string, List<string>> flagsRead = new(StringComparer.Ordinal);
    public IEnumerable<string> ConditionFlags(string name)
    {
        if (flagsRead.TryGetValue(name, out var known)) return known;
        var flags = new HashSet<string>(StringComparer.Ordinal);
        if (Record(name) is object record) flags.UnionWith(Conditions.FlagsRead(record));
        // A script naming a mission (Mission("Main", "M…")) reads what that mission's conditions read.
        if (checkByKey.TryGetValue(name, out var check)) foreach (var group in R.Items(R.Field(check, "_conditions"))) flags.UnionWith(Conditions.FlagsRead(group));
        return flagsRead[name] = flags.ToList();
    }

    public IEnumerable<string> MissionFlags => checks.SelectMany(check => new[] { "_modifyFlagsWhenTrue", "_modifyFlagsWhenFalse" }
        .SelectMany(field => R.Items(R.Field(check, field)))).Select(modify => R.Field<FlagData>(modify, "_data")).Where(flag => flag != null).Select(flag => flag!.name).Distinct().ToList();

    public string FlagNote(string flag) => notes.TryGetValue(flag, out var note) ? note : "";
    public string Legend(string key) => L.GameText("LegendInfo/" + key, "");
    public string OptionText(string key) => Regex.Replace(L.GameText("Story/" + key, key), "<[^>]+>", "").Trim();
    public static string StoryText(string key) => L.GameText("Story/" + key, "");
    public bool Read(string key) => ReadStorySystem.Instance != null && ReadStorySystem.Instance.ExistKey(key);
    public bool Visited(string addKey) => visited.Contains(addKey);
}

/// <summary>
/// Builds the route map a few milliseconds a frame (F89) and keeps it for the session; 重新生成 builds it again (the read
/// and story records move on as the player plays). Records how long it took and the cost per frame for the report.
/// </summary>
internal sealed class RouteMapRunner(ManualLogSource log)
{
    private const double Budget = 6; // ms of work per frame
    private IEnumerator<object?>? steps;
    // The phase the slowest single step came after, for the log.
    private string lastPhase = "start", slowestAfter = "";
    private double slowestStep;
    private RouteMapGame? source;
    private RouteMap? building;
    private readonly Stopwatch total = new();
    public RouteMap? Map { get; private set; }
    public bool Building => steps != null;
    public int Frames { get; private set; }
    public double LoadMs { get; private set; }
    public double WorkMs { get; private set; }
    public double WallMs { get; private set; }
    public string Error { get; private set; } = "";
    /// <summary>The longest single frame's work, to tell how much a frame lost while building.</summary>
    public double LongestFrameMs { get; private set; }

    public void Start()
    {
        if (steps != null) return;
        Error = ""; Frames = 0; WorkMs = 0; LongestFrameMs = 0; lastPhase = "start"; slowestAfter = ""; slowestStep = 0;
        total.Restart();
        var load = Stopwatch.StartNew();
        try { source = new RouteMapGame(); }
        catch (Exception ex) { Error = ex.GetBaseException().Message; log.LogWarning("ROUTE_MAP source " + Error); return; }
        LoadMs = load.Elapsed.TotalMilliseconds;
        building = new RouteMap();
        steps = RouteMapBuilder.Steps(source, (text, args) => args.Length == 0 ? L.Text(text) : L.Format(text, args), building, phase => { log.LogInfo("ROUTE_MAP " + phase); lastPhase = phase.Split(' ')[0]; }).GetEnumerator();
        log.LogInfo($"ROUTE_MAP load {LoadMs:0}ms");
    }

    /// <summary>Each frame (from the plugin's Update): a few steps, within the budget.</summary>
    public void Update()
    {
        if (steps == null) return;
        var frame = Stopwatch.StartNew();
        try
        {
            while (frame.Elapsed.TotalMilliseconds < Budget)
            {
                double before = frame.Elapsed.TotalMilliseconds;
                string phase = lastPhase;
                bool more = steps.MoveNext();
                double step = frame.Elapsed.TotalMilliseconds - before;
                if (step > slowestStep) { slowestStep = step; slowestAfter = phase; }
                if (more) continue;
                Map = building; Finish();
                log.LogInfo($"ROUTE_MAP built stations={Map!.Stations.Count} arcs={Map.Arcs.Count} categories={Map.Categories.Count} scripts={Map.ScriptsAll} load={LoadMs:0}ms work={WorkMs + frame.Elapsed.TotalMilliseconds:0}ms frames={Frames + 1} wall={total.Elapsed.TotalMilliseconds:0}ms longest={Math.Max(LongestFrameMs, frame.Elapsed.TotalMilliseconds):0}ms next={Map.NextStation} slowestStep={slowestStep:0}ms after {slowestAfter}");
                break;
            }
        }
        catch (Exception ex) { Error = ex.GetBaseException().Message; log.LogWarning("ROUTE_MAP " + ex); Finish(); }
        double spent = frame.Elapsed.TotalMilliseconds;
        WorkMs += spent; Frames++; LongestFrameMs = Math.Max(LongestFrameMs, spent);
        if (steps == null) WallMs = total.Elapsed.TotalMilliseconds;
    }

    private void Finish()
    {
        steps = null; building = null;
        source?.Release(); source = null;
    }
}
