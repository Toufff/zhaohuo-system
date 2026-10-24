using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ZhaoHuoSystem;

/// <summary>What the route map reads from the game (or, in the tests, from the offline dump).</summary>
public interface IRouteSource
{
    IEnumerable<string> Scripts { get; }
    /// <summary>A script's Lua text by its lower-case name, or null.</summary>
    string? Text(string script);
    IEnumerable<RouteMission> Missions { get; }
    /// <summary>Free-play events with a name and a script of their own (the place's event tables).</summary>
    IEnumerable<RouteEvent> Events { get; }
    /// <summary>A switch or condition record's groups, in order (the k-th switch arm is the k-th group); null when there is none.</summary>
    IReadOnlyList<RouteGroup>? Groups(string name);
    /// <summary>Flags a condition record reads.</summary>
    IEnumerable<string> ConditionFlags(string name);
    /// <summary>Flags mission records change when they are checked.</summary>
    IEnumerable<string> MissionFlags { get; }
    string FlagNote(string flag);
    /// <summary>The story-record line written for this AddStory key (LegendInfo), or "".</summary>
    string Legend(string key);
    string OptionText(string key);
    bool Read(string key);
    bool Visited(string addKey);
    string CurrentMain { get; }
}

public sealed class RouteMission
{
    public string Key = "", Start = "", Desc = "";
    public bool Active;
    /// <summary>1 fixed date, 2 window, 3 periodic.</summary>
    public int TimeType;
    public int[]? Specific, Min, Max;
    public List<RouteGroup> Groups = new();
}

/// <summary>One group of a condition, already worded by the source, with what the map needs to reason about.</summary>
public sealed class RouteGroup
{
    public string Text = "";
    /// <summary>Bookkeeping conditions (temporary flags, the duel result) that say nothing to a reader.</summary>
    public bool Noise;
    /// <summary>The group is a single comparison with a constant: its opposite, worded; "" otherwise.</summary>
    public string Flip = "";
    /// <summary>"Now" compared with a fixed date: op (= != &lt; &lt;= &gt; &gt;=) and [year, month, stage].</summary>
    public List<(string Op, int[] Time)> Times = new();
    /// <summary>Flags required to be set (= k or &gt;= k with k ≥ 1), with their notes.</summary>
    public List<(string Flag, string Note)> Requires = new();
    /// <summary>"Now" compared for equality with a series' own scheduled time (SetMissionTime).</summary>
    public bool Scheduled;
}

public sealed class RouteEvent { public string Script = "", Name = "", Table = ""; public RouteGroup? Condition; }

/// <summary>
/// 剧情路线图 (F89), worked out from the scripts and the game's records: the hand-placed main-line stations
/// (<see cref="RouteMapDefs"/>) with every script that belongs to them, each station's sections (main trunk or a branch
/// and the conditions into it), the side stories placed by how they are actually entered (from a station, from another
/// side story, from a free-play event, or on a date), the three side routes, and what is left as free-play categories.
/// Plain C#: the game side only supplies <see cref="IRouteSource"/>, and the words through <c>format</c>.
/// </summary>
public sealed class RouteMap
{
    public sealed class Station
    {
        public string Id = "", Label = "", Start = "", Route = "", Kind = "station", Note = "";
        public int Lane, Branch, Endings, Lines, Variants;
        public int? Read;
        public int[] Date = new int[3];
        public bool Visited;
        public List<string> Scripts = new(), Summary = new(), DeadVariants = new(), Exits = new();
        public List<Section> Sections = new();
        public List<List<string>> Entry = new();
        public List<(string Station, List<string> Flags)> Influence = new();
    }
    public sealed class Section
    {
        public string Group = "", Label = "", Dead = "";
        public int Lines, Branch;
        public int? Read;
        public bool Trunk, Reach, Visited, ViaMission;
        public List<string> Scripts = new(), Summary = new(), Next = new();
        public List<List<string>> Conditions = new();
    }
    public sealed class Entry
    {
        public string From = "", To = "", Kind = "", Via = "", Dead = "";
        public List<List<string>> Conditions = new();
        public List<RouteGroup> Raw = new();
        public int? Months, Stages;
    }
    public sealed class Arc
    {
        public string Id = "", Kind = "", Dead = "", Anchor = "", Parent = "", Desc = "", Start = "", Label = "", Route = "";
        public bool Approx, Visited, Named, Always, Prologue;
        public int[]? Lo, Hi;
        public int? Read;
        public int Lines, Branch, Endings;
        public List<string> Calendar = new(), Exits = new(), Summary = new(), Scripts = new();
        public List<Entry> Entries = new();
        public List<(string Key, string Text)> Stages = new();
        public List<(string Station, List<string> Flags)> Influence = new();
    }
    public sealed class Category
    {
        public string Id = "";
        public int Lines;
        public int? Read;
        public bool Visited;
        public List<string> Scripts = new(), Sample = new();
        public List<CategoryEvent> Events = new();
    }
    public sealed class CategoryEvent
    {
        public string Name = "", Condition = "", Script = "";
        public int[]? Lo, Hi;
        public int Lines;
        public int? Read;
        public bool Visited;
    }

    public List<Station> Stations = new();
    public List<(string From, string To, string Kind)> Edges = new();
    /// <summary>Side stories, routes left out.</summary>
    public List<Arc> Arcs = new();
    public List<Category> Categories = new();
    /// <summary>Each script's place: stn:ID, arc:ID or cat:prefix.</summary>
    public Dictionary<string, string> Owner = new(StringComparer.Ordinal);
    /// <summary>The station of the main mission now (the next stop of this playthrough), or "".</summary>
    public string NextStation = "";
    public int ScriptsAll, LinesAll, ReadAll, BranchAll, DrawnScripts, DrawnLines;
    /// <summary>Per script, for searching: its story-record lines and its dialogue keys.</summary>
    public Dictionary<string, List<string>> Legends = new(StringComparer.Ordinal), Keys = new(StringComparer.Ordinal);
    /// <summary>结缘路线图 (F117), per script: the affection it changes by relation key (girl4 …), summed over the script.</summary>
    public Dictionary<string, Dictionary<string, int>> Affection = new(StringComparer.Ordinal);
    /// <summary>结缘路线图 (F117), per script: the flags it sets to a fixed value, in order.</summary>
    public Dictionary<string, List<(string Flag, int Value)>> FlagValues = new(StringComparer.Ordinal);
}

public static class RouteMapBuilder
{
    private static readonly Regex Jump = new("(?:SetNextScript|SetCurrentStoryScript)\\(\"([^\"]+)\"\\)");
    private static readonly Regex Line = new("GetStoryText\\(\"([^\"]+)\"\\)");
    private static readonly Regex Add = new("AddStory\\(\"([^\"]+)\"\\)");
    private static readonly Regex MainSet = new("statmodifymanager\\.Mission\\(\"Main\", *\"([^\"]+)\"\\)");
    private static readonly Regex FlagSet = new("statmodifymanager\\.(?:SetFlag|AddFlag)\\(\"(\\w+)\"");
    // Character(key, value, display): the affection change; SetFlag(name, value) with a literal value (F117).
    private static readonly Regex Affection = new("statmodifymanager\\.Character\\(\"(\\w+)\", *(-?\\d+) *,");
    private static readonly Regex FlagValue = new("statmodifymanager\\.SetFlag\\(\"(\\w+)\", *(-?\\d+) *\\)");
    private static readonly Regex Literal = new("\"([A-Za-z][A-Za-z0-9_]+)\"");
    private static readonly Regex MissionTime = new("SetMissionTime\\(\"(S\\d{4})_\\w*\", *(-?\\d+), *(-?\\d+), *(-?\\d+)\\)");
    private static readonly Regex Fight = new("ChangeScene\\(\"(?:Combat|Battle)\"");
    private static readonly Regex SwitchVar = new("^local (\\w+) = checkpointmanager\\.(?:Switch|Condition)\\(\"([^\"]+)\"\\)");
    private static readonly Regex ChoiceVar = new("^local (\\w+) = choose\\((\\w+)\\)");
    private static readonly Regex TableRow = new("^(\\w+)\\[(\\d+)\\] = [^\"]*\"([^\"]+)\"");
    private static readonly Regex ArmIf = new("^(if|elseif) (\\w+) == (\\d+) then$");
    private static readonly Regex Chapter = new("^ch\\d"), Series = new("^s\\d{4}"), Letters = new("^[a-z]+");
    private static readonly Regex VariantA = new("^(?:ch4_6_[123]$|ch4_7_|ch4_8_)");
    private static readonly Regex CodeFamily = new("^(C\\d|Talk|TC\\d|A\\d|B\\d|Shop|Pinball|Prison|Facility)");
    private static readonly Regex Generic = new("^(Talk_Option|TC\\d|A\\d|B\\d|Prison|H\\d|C\\d)");

    private sealed class Facts
    {
        public List<string> Out = new(), Keys = new(), Adds = new(), Mains = new(), Flags = new(), Literals = new();
        public bool Menu, Dice, Fight, Title;
    }

    /// <summary>Builds the whole map. <paramref name="format"/> words the map's own phrases (L.Format in the game).</summary>
    public static RouteMap Build(IRouteSource source, Func<string, object[], string> format, Action<string>? trace = null)
    {
        var map = new RouteMap();
        foreach (var _ in Steps(source, format, map, trace)) { }
        return map;
    }

    /// <summary>
    /// The same work in small steps, into <paramref name="map"/>: the game runs a few each frame so the screen keeps
    /// moving, and every call into the game stays on its main thread. A script's text is read once and let go.
    /// </summary>
    public static IEnumerable<object?> Steps(IRouteSource source, Func<string, object[], string> format, RouteMap map, Action<string>? trace = null)
    {
        string T(string text, params object[] args) => format(text, args);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        void Phase(string name) { trace?.Invoke($"{name} {clock.ElapsedMilliseconds}ms"); clock.Restart(); }
        // ---- script facts ----
        var facts = new Dictionary<string, Facts>(StringComparer.Ordinal);
        // Each jump with the conditions around it, worked out while the text is at hand.
        var jumps = new Dictionary<string, List<(string Target, List<RouteGroup> Raw, List<string> Words)>>(StringComparer.Ordinal);
        int counted = 0;
        var missionTimes = new Dictionary<string, List<(string Script, int Months, int Stages)>>(StringComparer.Ordinal);
        foreach (string name in source.Scripts.Select(name => name.ToLowerInvariant()).Distinct().OrderBy(name => name, StringComparer.Ordinal))
        {
            string? text = source.Text(name);
            if (text == null) continue;
            var f = new Facts
            {
                Out = Jump.Matches(text).Cast<Match>().Select(m => m.Groups[1].Value.ToLowerInvariant()).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList(),
                Keys = Line.Matches(text).Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList(),
                Adds = Add.Matches(text).Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList(),
                Mains = MainSet.Matches(text).Cast<Match>().Select(m => m.Groups[1].Value).ToList(),
                Flags = FlagSet.Matches(text).Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList(),
                Literals = Literal.Matches(text).Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList(),
                Menu = text.Contains("choose("), Dice = text.Contains("ExecuteRoll"), Fight = Fight.IsMatch(text), Title = text.Contains("ChangeScene(\"Title\""),
            };
            facts[name] = f;
            Dictionary<string, int>? affection = null;
            foreach (Match m in Affection.Matches(text))
            {
                affection ??= new Dictionary<string, int>(StringComparer.Ordinal);
                affection[m.Groups[1].Value] = (affection.TryGetValue(m.Groups[1].Value, out int sum) ? sum : 0) + int.Parse(m.Groups[2].Value);
            }
            if (affection != null) map.Affection[name] = affection;
            var values = FlagValue.Matches(text).Cast<Match>().Select(m => (m.Groups[1].Value, int.Parse(m.Groups[2].Value))).ToList();
            if (values.Count > 0) map.FlagValues[name] = values;
            foreach (Match m in MissionTime.Matches(text))
                Get(missionTimes, m.Groups[1].Value).Add((name, int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value)));
            if (f.Out.Count > 0) jumps[name] = ParseJumps(text);
            yield return null;
        }
        Phase("facts");
        var keys = facts.Keys.ToList();
        var keySet = new HashSet<string>(keys, StringComparer.Ordinal);
        var incoming = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var pair in facts) foreach (string t in pair.Value.Out) Get(incoming, t).Add(pair.Key);
        IEnumerable<string> Out(string x) => facts.TryGetValue(x, out var f) ? f.Out : Enumerable.Empty<string>();
        IEnumerable<string> In(string x) => incoming.TryGetValue(x, out var s) ? s : Enumerable.Empty<string>();
        bool Branchy(string x) => facts.TryGetValue(x, out var f) && (f.Menu || f.Dice || f.Fight);
        bool Ending(string x) => facts.TryGetValue(x, out var f) && f.Title;
        bool IsChapter(string x) => Chapter.IsMatch(x);
        bool IsSeries(string x) => Series.IsMatch(x);
        string Group(string x) => IsChapter(x) ? string.Join("_", x.Split('_').Take(2)) : x;
        int[] NumericKey(string g) => g.Split('_').Skip(1).Select(v => int.TryParse(v, out int n) ? n : 0).ToArray();

        // ---- missions ----
        var missions = new Dictionary<string, RouteMission>(StringComparer.Ordinal);
        foreach (var m in source.Missions) if (m.Key.Length > 0 && !missions.ContainsKey(m.Key)) missions[m.Key] = m;
        var startOf = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var m in missions.Values) if (m.Start.Length > 0) Get(startOf, m.Start.ToLowerInvariant()).Add(m.Key);
        bool Starts(string x) => startOf.TryGetValue(x, out var l) && l.Count > 0;
        int[]? DateOf(string key) => missions.TryGetValue(key, out var m) ? (m.TimeType == 1 ? m.Specific : m.Min) : null;

        // Each mission's story run: from its start script along the jumps, up to another mission's start or a free-play entry.
        var runs = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var m in missions.Values)
        {
            string start = m.Start.ToLowerInvariant();
            if (start.Length == 0) continue;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var stack = new Stack<string>(); stack.Push(start);
            while (stack.Count > 0)
            {
                string n = stack.Pop();
                if (seen.Contains(n) || !keySet.Contains(n)) continue;
                if (n != start && (Starts(n) || RouteMapDefs.FreeEntries.Any(p => n.StartsWith(p, StringComparison.Ordinal)))) continue;
                seen.Add(n);
                foreach (string t in Out(n)) stack.Push(t);
            }
            runs[m.Key] = seen.OrderBy(x => x, StringComparer.Ordinal).ToList();
        }
        // Hand-drawn variants: 4-6-A runs in ch4_6_1..3 / ch4_7 / ch4_8 outside 4-6-B; 4-5 keeps its own ch4_5; 响应武林盟 is one script.
        var b = new HashSet<string>(Run(runs, "M3004_01").Concat(Run(runs, "M3004_02")), StringComparer.Ordinal);
        if (runs.ContainsKey("M3003_02")) runs["M3003_02"] = runs["M3003_02"].Where(s => s.StartsWith("ch4_5", StringComparison.Ordinal)).ToList();
        runs["V_A"] = keys.Where(s => VariantA.IsMatch(s) && !b.Contains(s)).OrderBy(s => s, StringComparer.Ordinal).ToList();
        runs["V_XY"] = new List<string> { "ch7_4_3_4" };
        List<string> Members(IEnumerable<string> ids)
        {
            var set = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string id in ids)
                if (id.EndsWith("*")) { foreach (var pair in runs) if (pair.Key.StartsWith(id.TrimEnd('*'), StringComparison.Ordinal)) set.UnionWith(pair.Value); }
                else set.UnionWith(Run(runs, id));
            return set.ToList();
        }

        Phase("missions");
        // ---- free-play event tables ----
        var events = new Dictionary<string, RouteEvent>(StringComparer.Ordinal);
        counted = 0;
        foreach (var e in source.Events)
        {
            string s = e.Script.ToLowerInvariant();
            if (keySet.Contains(s) && !events.ContainsKey(s)) events[s] = e;
            if (++counted % 2 == 0) yield return null;
        }

        // ---- who sets and who reads each flag ----
        var setters = new HashSet<string>(facts.Values.SelectMany(f => f.Flags), StringComparer.Ordinal);
        setters.UnionWith(source.MissionFlags);
        setters.UnionWith(RouteMapDefs.CodeFlags);
        var setBy = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var pair in facts) foreach (string flag in pair.Value.Flags) Get(setBy, flag).Add(pair.Key);
        var conditionFlags = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var readers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        counted = 0;
        foreach (var pair in facts)
        {
            foreach (string literal in pair.Value.Literals)
            {
                if (!conditionFlags.TryGetValue(literal, out var read)) conditionFlags[literal] = read = source.ConditionFlags(literal).ToList();
                foreach (string flag in read) Get(readers, flag).Add(pair.Key);
            }
            yield return null;
        }

        Phase("events and flags");
        // ---- reading helpers ----
        (int? Read, int Lines) Reading(IEnumerable<string> scripts)
        {
            var set = new HashSet<string>(scripts.SelectMany(s => facts.TryGetValue(s, out var f) ? f.Keys : new List<string>()), StringComparer.Ordinal);
            if (set.Count == 0) return (null, 0);
            return ((int)Math.Round(100.0 * set.Count(source.Read) / set.Count, MidpointRounding.ToEven), set.Count);
        }
        List<string> Summary(IEnumerable<string> scripts, string start, int count = 3)
        {
            var list = scripts.ToList();
            var order = list.Contains(start) ? new[] { start }.Concat(list.Where(s => s != start)) : list;
            var lines = new List<string>();
            foreach (string s in order)
            {
                if (facts.TryGetValue(s, out var f))
                    foreach (string key in f.Adds)
                    {
                        string text = source.Legend(key).Trim();
                        if (text.Length > 0 && !lines.Contains(text)) lines.Add(text);
                    }
                if (lines.Count >= count) break;
            }
            return lines.Take(count).ToList();
        }
        bool Visited(IEnumerable<string> scripts) => scripts.Any(s => facts.TryGetValue(s, out var f) && f.Adds.Any(source.Visited));
        string Dead(IEnumerable<RouteGroup> raw)
        {
            foreach (var group in raw)
                foreach (var (flag, note) in group.Requires)
                    if (!setters.Contains(flag) && !CodeFamily.IsMatch(flag))
                        return note.Contains("試玩") || note.Contains("试玩") ? T("仅试玩版") : T("需要「{0}」，但数据里没有地方会设置它", note.Trim().Length > 0 ? note.Trim() : flag);
            return "";
        }
        (int[]? Lo, int[]? Hi) TimesIn(IEnumerable<RouteGroup> groups)
        {
            int[]? lo = null, hi = null;
            foreach (var group in groups)
                foreach (var (op, time) in group.Times)
                {
                    if (op is ">=" or ">" or "=") lo = time;
                    if (op is "<=" or "<" or "=") hi = time;
                }
            return (lo, hi);
        }

        // The conditions around each jump from one script to another: the if/elseif/else arms it sits in.
        List<(List<RouteGroup> Raw, List<string> Text)> EntryConditions(string from, string to)
            => jumps.TryGetValue(from, out var list) ? list.Where(j => j.Target == to).Select(j => (j.Raw, j.Words)).ToList() : new List<(List<RouteGroup>, List<string>)>();
        List<(string Target, List<RouteGroup> Raw, List<string> Words)> ParseJumps(string text)
        {
            var found = new List<(string, List<RouteGroup>, List<string>)>();
            var switches = new Dictionary<string, string>(StringComparer.Ordinal);
            var tables = new Dictionary<string, Dictionary<int, string>>(StringComparer.Ordinal);
            var choiceOf = new Dictionary<string, string>(StringComparer.Ordinal);
            var stack = new List<List<(string Var, int Arm)?>>();
            foreach (string raw in text.Split('\n'))
            {
                string s = raw.Trim();
                // Most lines are dialogue: only these shapes matter, so each regex runs only where it can match.
                if (s.StartsWith("local ", StringComparison.Ordinal))
                {
                    var m = SwitchVar.Match(s);
                    if (m.Success) switches[m.Groups[1].Value] = m.Groups[2].Value;
                    m = ChoiceVar.Match(s);
                    if (m.Success) choiceOf[m.Groups[1].Value] = m.Groups[2].Value;
                }
                else if (s.IndexOf("] = ", StringComparison.Ordinal) > 0)
                {
                    var m = TableRow.Match(s);
                    if (m.Success && LuaOptions.OptionKey(m.Groups[3].Value) is { Length: > 0 } key) Get(tables, m.Groups[1].Value)[int.Parse(m.Groups[2].Value)] = key;
                }
                else if (s.StartsWith("if ", StringComparison.Ordinal) || s.StartsWith("elseif ", StringComparison.Ordinal))
                {
                    var m = ArmIf.Match(s);
                    if (m.Success)
                    {
                        (string, int)? item = (m.Groups[2].Value, int.Parse(m.Groups[3].Value));
                        if (m.Groups[1].Value == "if") stack.Add(new List<(string, int)?> { item });
                        else if (stack.Count > 0) stack[stack.Count - 1].Add(item);
                        continue;
                    }
                    if (s.StartsWith("if ", StringComparison.Ordinal) && s.EndsWith(" then", StringComparison.Ordinal)) { stack.Add(new List<(string, int)?> { null }); continue; }
                }
                if (s == "else" && stack.Count > 0) { stack[stack.Count - 1].Add(("else", 0)); continue; }
                if (s == "end" && stack.Count > 0) { stack.RemoveAt(stack.Count - 1); continue; }
                if (s.IndexOf("Script(\"", StringComparison.Ordinal) < 0) continue;
                var targets = Jump.Matches(s);
                if (targets.Count == 0) continue;
                var groups = new List<RouteGroup>();
                var words = new List<string>();
                foreach (var level in stack)
                {
                    if (level[level.Count - 1] is not (string var, int arm)) continue;
                    if (var == "else")
                    {
                        var previous = level.Count > 1 ? level[level.Count - 2] : null;
                        if (previous is (string pv, _) && switches.ContainsKey(pv)) words.Add(T("（其余情况）"));
                        continue;
                    }
                    if (choiceOf.TryGetValue(var, out var table))
                    {
                        if (tables.TryGetValue(table, out var rows) && rows.TryGetValue(arm, out var option)) words.Add(T("选「{0}」", source.OptionText(option)));
                        continue;
                    }
                    if (!switches.TryGetValue(var, out var record) || source.Groups(record) is not { } list) continue;
                    if (arm <= list.Count)
                    {
                        groups.Add(list[arm - 1]);
                        if (!list[arm - 1].Noise && list[arm - 1].Text.Length > 0) words.Add(list[arm - 1].Text);
                    }
                    else
                    {
                        var rest = list.Where(g => !g.Noise).ToList();
                        if (rest.Count == 1 && rest[0].Flip.Length > 0) words.Add(rest[0].Flip);
                        else words.Add(rest.Count > 0 ? T("不满足「{0}」", string.Join("」「", rest.Select(g => g.Text))) : T("（其余情况）"));
                    }
                }
                foreach (Match target in targets) found.Add((target.Groups[1].Value.ToLowerInvariant(), groups, words.Where(w => w.Length > 0).ToList()));
            }
            return found;
        }

        // ---- stations: their members, then scripts the story leads into, then by chapter prefix ----
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);
        var ownerOrder = new List<string>();
        void Own(string x, string o) { if (!owner.ContainsKey(x)) { owner[x] = o; ownerOrder.Add(x); } }
        var members = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var def in RouteMapDefs.Stations)
        {
            members[def.Id] = new SortedSet<string>(Members(def.Members).Where(x => !IsSeries(x)), StringComparer.Ordinal);
            foreach (string x in members[def.Id]) Own(x, "stn:" + def.Id);
        }
        members["M0001"].Add("ch1_1"); if (!owner.ContainsKey("ch1_1")) Own("ch1_1", "stn:M0001");   // a new game starts at ch1_1
        foreach (string x in keys.Where(x => Regex.IsMatch(x, "^ch0_[12](_|$)")))
        {
            // The prologue opens in ch0_1/ch0_2 (snapshot, clock to year 3) before its own missions start.
            foreach (var set in members.Values) set.Remove(x);
            members["P0"].Add(x);
            if (owner.ContainsKey(x)) owner[x] = "stn:P0"; else Own(x, "stn:P0");
        }
        int Propagate()
        {
            int n = 0; bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (string x in ownerOrder.ToList())
                {
                    string o = owner[x];
                    if (!o.StartsWith("stn:", StringComparison.Ordinal)) continue;
                    foreach (string t in Out(x))
                        if (keySet.Contains(t) && !owner.ContainsKey(t) && IsChapter(t) && !Starts(t)) { Own(t, o); members[o.Substring(4)].Add(t); n++; changed = true; }
                }
            }
            return n;
        }
        int PrefixAttach()
        {
            int n = 0;
            foreach (string x in keys)
            {
                if (owner.ContainsKey(x) || !IsChapter(x)) continue;
                var parts = x.Split('_');
                for (int len = parts.Length - 1; len > 1; len--)
                {
                    string pre = string.Join("_", parts.Take(len));
                    var counts = new Dictionary<string, int>(StringComparer.Ordinal); var first = new List<string>();
                    foreach (string y in ownerOrder)
                    {
                        string o = owner[y];
                        if (!o.StartsWith("stn:", StringComparison.Ordinal) || !(y == pre || y.StartsWith(pre + "_", StringComparison.Ordinal))) continue;
                        if (!counts.ContainsKey(o)) { counts[o] = 0; first.Add(o); }
                        counts[o]++;
                    }
                    if (counts.Count == 0) continue;
                    string best = first.OrderByDescending(o => counts[o]).First();   // stable: ties keep the first seen
                    Own(x, best); members[best.Substring(4)].Add(x); n++;
                    break;
                }
            }
            return n;
        }
        for (int pass = 0; pass < 4; pass++)
        {
            int attached = Propagate();
            yield return null;
            attached += PrefixAttach();
            yield return null;
            if (attached == 0) break;
        }
        Phase("ownership");
        // Side series (sNNNN by number), chapter fragments nobody leads to, and the rest by kind of place.
        var series = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (string x in keys) if (IsSeries(x) && !owner.ContainsKey(x)) Get(series, "S" + x.Substring(1, 4)).Add(x);
        foreach (string x in keys)
        {
            if (owner.ContainsKey(x)) continue;
            if (IsSeries(x)) Own(x, "arc:S" + x.Substring(1, 4));
            else if (IsChapter(x)) { Own(x, "arc:" + Group(x)); Get(series, Group(x)).Add(x); }
            else Own(x, "cat:" + (Letters.Match(x) is { Success: true } lm ? lm.Value : x));
        }
        var stationOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in owner) if (pair.Value.StartsWith("stn:", StringComparison.Ordinal)) stationOf[pair.Key] = pair.Value.Substring(4);
        var stationDate = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var def in RouteMapDefs.Stations) stationDate[def.Id] = DateOf(def.DateFrom) ?? new[] { 0, 0, 0 };
        var missionStation = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var def in RouteMapDefs.Stations)
            foreach (string pattern in def.Members)
                foreach (string k in missions.Keys.OrderBy(k => k, StringComparer.Ordinal))
                    if ((k == pattern || pattern.EndsWith("*") && k.StartsWith(pattern.TrimEnd('*'), StringComparison.Ordinal)) && !missionStation.ContainsKey(k)) missionStation[k] = def.Id;

        List<(string, List<string>)> Influence(IEnumerable<string> scripts)
        {
            var flags = new SortedSet<string>(scripts.SelectMany(s => facts.TryGetValue(s, out var f) ? f.Flags : new List<string>()), StringComparer.Ordinal);
            var result = new List<(string Station, List<string> Flags)>();
            foreach (string flag in flags)
            {
                if (Generic.IsMatch(flag)) continue;
                var stations = (readers.TryGetValue(flag, out var r) ? r : new HashSet<string>()).Where(stationOf.ContainsKey).Select(x => stationOf[x]).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
                if (stations.Count == 0 || stations.Count > 6) continue;
                string note = source.FlagNote(flag).Trim();
                foreach (string st in stations)
                {
                    int i = result.FindIndex(x => x.Station == st);
                    if (i < 0) result.Add((st, new List<string> { note.Length > 0 ? note : flag }));
                    else result[i].Flags.Add(note.Length > 0 ? note : flag);
                }
            }
            return result.Select(x => (x.Station, x.Flags.Take(3).ToList())).ToList();
        }

        // ---- side stories, placed by how they are entered ----
        var seriesMissions = new Dictionary<string, List<RouteMission>>(StringComparer.Ordinal);
        foreach (var m in missions.Values) if (m.Key.StartsWith("S", StringComparison.Ordinal) && m.Start.Length > 0 && m.Key.Length >= 5) Get(seriesMissions, m.Key.Substring(0, 5)).Add(m);
        var arcs = new Dictionary<string, RouteMap.Arc>(StringComparer.Ordinal);
        foreach (var pair in series)
        {
            string id = pair.Key; var sc = pair.Value;
            var entries = new List<RouteMap.Entry>();
            foreach (string s in sc)
                foreach (string a in In(s))
                {
                    if (sc.Contains(a)) continue;
                    string o = owner.TryGetValue(a, out var oo) ? oo : "cat:?";
                    string kind = o.StartsWith("stn:", StringComparison.Ordinal) ? "fork" : o.StartsWith("arc:", StringComparison.Ordinal) ? "chain" : "free";
                    var conditions = EntryConditions(a, s);
                    if (events.TryGetValue(a, out var ev))
                    {
                        var words = new List<string> { ev.Name.Length > 0 ? T("自由行动事件「{0}」", ev.Name) : T("自由行动随机事件") };
                        if (ev.Condition != null && ev.Condition.Text.Length > 0) words.Add(ev.Condition.Text);
                        conditions.Insert(0, (ev.Condition != null ? new List<RouteGroup> { ev.Condition } : new List<RouteGroup>(), words));
                    }
                    var deads = conditions.Count > 0 ? conditions.Select(c => Dead(c.Raw)).ToList() : new List<string> { "" };
                    entries.Add(new RouteMap.Entry
                    {
                        From = a, To = s, Kind = kind, Via = o, Conditions = conditions.Select(c => c.Text).Take(3).ToList(),
                        Raw = conditions.SelectMany(c => c.Raw).ToList(), Dead = deads.All(d => d.Length > 0) ? deads.First(d => d.Length > 0) : ""
                    });
                }
            foreach (var (script, months, stages) in missionTimes.TryGetValue(id, out var mt) ? mt : new List<(string, int, int)>())
            {
                string o = owner.TryGetValue(script, out var oo) ? oo : "";
                if (o.Length == 0 || o == "arc:" + id) continue;
                entries.Add(new RouteMap.Entry
                {
                    From = script, Kind = o.StartsWith("arc:", StringComparison.Ordinal) ? "chain" : o.StartsWith("stn:", StringComparison.Ordinal) ? "fork" : "free", Via = o,
                    Conditions = new List<List<string>> { new() { T("前一段结束后约 {0} 旬", months * 3 + stages) } }, Months = months, Stages = stages
                });
            }
            var mis = seriesMissions.TryGetValue(id, out var ml) ? ml : new List<RouteMission>();
            var calendar = new List<(List<int[]> Times, string Text)>();
            foreach (var m in mis)
            {
                if (!m.Active || m.TimeType == 1 && m.Groups.Any(g => g.Scheduled)) continue;
                var ts = (m.TimeType == 1 ? new[] { m.Specific, m.Specific } : new[] { m.Min, m.Max }).Where(t => t != null && t[0] > 0).Select(t => t!).ToList();
                if (ts.Count == 0) continue;
                var (lo, hi) = TimesIn(m.Groups);
                if (lo != null) ts = new List<int[]> { lo, hi ?? ts.OrderBy(Rounds).Last() };
                else if (hi != null) ts = new List<int[]> { ts.OrderBy(Rounds).First(), hi };
                calendar.Add((ts, string.Join(" ｜ ", m.Groups.Where(g => g.Text.Length > 0).Select(g => g.Text))));
            }
            var calendarDead = mis.Where(m => m.Active).Select(m => Dead(m.Groups)).ToList();
            var kinds = entries.Select(e => e.Kind).ToList();
            string arcKind = kinds.Contains("fork") ? "fork" : kinds.Contains("chain") ? "chain" : kinds.Contains("free") ? "free" : calendar.Count > 0 ? "date" : "orphan";
            var arc = new RouteMap.Arc { Id = id, Kind = arcKind };
            if (arcKind == "fork")
            {
                arc.Anchor = entries.Where(e => e.Kind == "fork").GroupBy(e => e.Via.Substring(4)).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).First().Key;
                arc.Lo = arc.Hi = stationDate.TryGetValue(arc.Anchor, out var d) ? d : null;
            }
            else if (arcKind == "chain") arc.Parent = entries.First(e => e.Kind == "chain").Via.Substring(4);
            else if (arcKind is "free" or "date")
            {
                var los = new List<int[]>(); var his = new List<int[]>();
                foreach (var e in entries)
                    foreach (var group in e.Raw)
                    {
                        var (lo, hi) = TimesIn(new[] { group });
                        if (lo != null) los.Add(lo);
                        if (hi != null) his.Add(hi);
                    }
                var records = calendar.Count > 0 ? calendar.Select(c => c.Times).ToList()
                    : mis.Select(m => (m.TimeType == 1 ? new[] { m.Specific } : new[] { m.Min, m.Max }).Where(t => t != null && t[0] > 0).Select(t => t!).ToList()).Where(l => l.Count > 0).ToList();
                if (los.Count == 0 && records.Count > 0) { los = records.Select(ts => ts.OrderBy(Rounds).First()).ToList(); if (calendar.Count == 0) arc.Approx = true; }
                if (his.Count == 0 && records.Count > 0) his = records.Select(ts => ts.OrderBy(Rounds).Last()).ToList();
                if (los.Count > 0) arc.Lo = los.OrderBy(Rounds).First();
                if (his.Count > 0) arc.Hi = his.OrderBy(Rounds).Last();
                arc.Lo ??= arc.Hi; arc.Hi ??= arc.Lo;
            }
            var (read, lines) = Reading(sc);
            if (lines == 0) continue;
            var firstMission = mis.OrderBy(m => m.Key, StringComparer.Ordinal).FirstOrDefault();
            arc.Start = firstMission != null && firstMission.Start.Length > 0 ? firstMission.Start.ToLowerInvariant() : sc.First();
            foreach (string s in sc)
            {
                var sm = Regex.Match(s, "^(s\\d+_\\d+)");
                string k = sm.Success ? sm.Groups[1].Value : Group(s);
                if (arc.Stages.Exists(x => x.Key == k) || !facts.TryGetValue(s, out var f)) continue;
                foreach (string key in f.Adds) { string t = source.Legend(key).Trim(); if (t.Length > 0) { arc.Stages.Add((k, t)); break; } }
            }
            arc.Desc = firstMission == null ? "" : (firstMission.Desc.Split('\n').FirstOrDefault() ?? "").Trim();
            var exits = new Dictionary<string, int>(StringComparer.Ordinal); var exitOrder = new List<string>();
            void Exit(string k, int w) { if (!exits.ContainsKey(k)) { exits[k] = 0; exitOrder.Add(k); } exits[k] += w; }
            foreach (string s in sc)
            {
                foreach (string main in facts[s].Mains) if (missionStation.TryGetValue(main, out var st)) Exit("stn:" + st, 5);
                foreach (string t in Out(s))
                    if (!sc.Contains(t)) { string o = owner.TryGetValue(t, out var oo) ? oo : ""; Exit(o.StartsWith("stn:", StringComparison.Ordinal) || o.StartsWith("arc:", StringComparison.Ordinal) ? o : "free", 1); }
                if (Ending(s)) Exit("ending", 1);
            }
            arc.Exits = exitOrder.OrderByDescending(k => exits[k]).Take(4).ToList();
            if (entries.Count > 0 && entries.All(e => e.Dead.Length > 0) && !(calendar.Count > 0 && !calendarDead.All(d => d.Length > 0))) arc.Dead = entries[0].Dead;
            else if (entries.Count == 0 && calendar.Count > 0 && calendarDead.Count > 0 && calendarDead.All(d => d.Length > 0)) arc.Dead = calendarDead[0];
            arc.Calendar = calendar.Where(c => c.Text.Length > 0).Select(c => c.Text).Take(2).ToList();
            arc.Entries = entries; arc.Read = read; arc.Lines = lines; arc.Branch = sc.Count(Branchy); arc.Endings = sc.Count(Ending);
            arc.Visited = Visited(sc); arc.Summary = Summary(sc, arc.Start); arc.Influence = Influence(sc); arc.Scripts = sc.ToList();
            if (arc.Stages.Count > 12) arc.Stages = arc.Stages.Take(12).ToList();
            arcs[id] = arc;
            yield return null;
        }
        Phase("arcs");
        // Free-play stories with no date: not before the station that sets the flag their entry needs.
        foreach (var arc in arcs.Values)
        {
            if (arc.Lo != null || arc.Kind != "free") continue;
            var dates = new List<int[]>();
            foreach (var e in arc.Entries)
                foreach (var group in e.Raw)
                    foreach (var (flag, _) in group.Requires)
                        foreach (string s in setBy.TryGetValue(flag, out var by) ? by : new List<string>())
                            if (stationOf.TryGetValue(s, out var st) && stationDate.TryGetValue(st, out var d)) dates.Add(d);
            if (dates.Count > 0) { arc.Lo = arc.Hi = dates.OrderBy(Rounds).First(); arc.Approx = true; }
        }
        // Events whose missions need the prologue flag happen only inside the prologue.
        foreach (var arc in arcs.Values)
            if (arc.Id == "ch0_meeting" || (seriesMissions.TryGetValue(arc.Id, out var pm) && pm.Any(m => m.Groups.Any(g => g.Requires.Any(r => r.Flag == "Prologue")))))
            { arc.Anchor = "P0"; arc.Lo = arc.Hi = stationDate["P0"]; arc.Prologue = true; }
        // Chained stories take their time from the story before, and hang off the same station.
        for (int pass = 0; pass < 5; pass++)
            foreach (var arc in arcs.Values)
            {
                if (arc.Kind != "chain" || arc.Lo != null) continue;
                if (arcs.TryGetValue(arc.Parent, out var parent) && parent.Lo != null)
                {
                    var e = arc.Entries.FirstOrDefault(x => x.Months != null && x.Via == "arc:" + arc.Parent);
                    arc.Lo = e != null ? Add3(parent.Lo, e.Months!.Value, e.Stages!.Value) : parent.Lo;
                    arc.Hi = e != null ? arc.Lo : parent.Hi;
                }
                else if (stationDate.TryGetValue(arc.Parent, out var d)) arc.Lo = arc.Hi = d;
            }
        for (int pass = 0; pass < 5; pass++)
            foreach (var arc in arcs.Values)
                if (arc.Kind == "chain" && arc.Anchor.Length == 0 && arcs.TryGetValue(arc.Parent, out var parent) && parent.Anchor.Length > 0)
                { arc.Anchor = parent.Anchor; arc.Lo = arc.Hi = stationDate[arc.Anchor]; }

        // ---- stations with their sections ----
        foreach (var def in RouteMapDefs.Stations)
        {
            var sc = members[def.Id].ToList();
            string start = def.Id == "P0" ? "ch0_1" : missions.TryGetValue(def.Members[0], out var fm) ? fm.Start.ToLowerInvariant() : sc.FirstOrDefault() ?? "";
            if (!sc.Contains(start) && sc.Count > 0) start = sc[0];
            var groups = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            foreach (string x in sc) Get(groups, Group(x)).Add(x);
            string head = sc.Count > 0 ? Group(start) : "";
            var gedges = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            var exits = new HashSet<string>(StringComparer.Ordinal);
            foreach (string x in sc)
                foreach (string t in Out(x))
                {
                    if (members[def.Id].Contains(t)) { if (Group(t) != Group(x)) Get(gedges, Group(x)).Add(Group(t)); }
                    else if (owner.TryGetValue(t, out var o) && o.StartsWith("stn:", StringComparison.Ordinal)) exits.Add(Group(x));
                }
            IEnumerable<string> Next(string g) => gedges.TryGetValue(g, out var n) ? n : Enumerable.Empty<string>();
            var reach = new HashSet<string>(StringComparer.Ordinal);
            var todo = new Stack<string>(); if (head.Length > 0) todo.Push(head);
            while (todo.Count > 0) { string n = todo.Pop(); if (reach.Contains(n) || !groups.ContainsKey(n)) continue; reach.Add(n); foreach (string t in Next(n)) todo.Push(t); }
            // Dominators of a virtual exit: the sections every way through the station passes are its trunk.
            const string Sink = "$";
            var nodes = reach.OrderBy(x => x, StringComparer.Ordinal).Concat(new[] { Sink }).ToList();
            var preds = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var pair in gedges) if (reach.Contains(pair.Key)) foreach (string t in pair.Value) Get(preds, t).Add(pair.Key);
            exits.IntersectWith(reach);
            if (exits.Count == 0 && reach.Count > 0) exits.Add(reach.OrderBy(z => NumericKey(z), new IntArrayOrder()).Last());
            foreach (string x in exits) Get(preds, Sink).Add(x);
            var dom = nodes.ToDictionary(n => n, n => new HashSet<string>(nodes, StringComparer.Ordinal), StringComparer.Ordinal);
            if (head.Length > 0) dom[head] = new HashSet<string>(StringComparer.Ordinal) { head };
            for (int pass = 0; pass < nodes.Count + 2; pass++)
                foreach (string n in nodes)
                {
                    if (n == head) continue;
                    var ps = preds.TryGetValue(n, out var p) ? p.Where(dom.ContainsKey).Select(x => dom[x]).ToList() : new List<HashSet<string>>();
                    var own = new HashSet<string>(StringComparer.Ordinal) { n };
                    if (ps.Count > 0) { var common = new HashSet<string>(ps[0], StringComparer.Ordinal); foreach (var q in ps.Skip(1)) common.IntersectWith(q); own.UnionWith(common); }
                    dom[n] = own;
                }
            var trunk = head.Length > 0 ? new HashSet<string>(dom[Sink].Where(x => x != Sink), StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
            var order = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>(); if (head.Length > 0) queue.Enqueue(head);
            while (queue.Count > 0)
            {
                string n = queue.Dequeue();
                if (seen.Contains(n) || !groups.ContainsKey(n)) continue;
                seen.Add(n); order.Add(n);
                foreach (string t in Next(n).OrderBy(z => NumericKey(z), new IntArrayOrder())) queue.Enqueue(t);
            }
            order.AddRange(groups.Keys.Where(g => !seen.Contains(g)).OrderBy(g => g, StringComparer.Ordinal));
            var station = new RouteMap.Station { Id = def.Id, Lane = def.Lane, Label = def.Label, Date = stationDate[def.Id], Start = start, Route = def.Route, Scripts = sc };
            foreach (string g in order)
            {
                var gs = groups[g].ToList();
                var (gr, gl) = Reading(gs);
                var conditions = new List<List<string>>(); var deads = new List<string>();
                var ins = gs.SelectMany(t => In(t).Where(a => members[def.Id].Contains(a) && Group(a) != g).Select(a => (a, t))).Take(6);
                foreach (var (a, t) in ins)
                    foreach (var (raw, words) in EntryConditions(a, t).Take(2))
                    {
                        deads.Add(Dead(raw));
                        if (!trunk.Contains(g) && words.Count > 0 && !conditions.Exists(c => c.SequenceEqual(words))) conditions.Add(words);
                    }
                var sm = Summary(gs, gs.OrderBy(x => x, StringComparer.Ordinal).First(), 1);
                station.Sections.Add(new RouteMap.Section
                {
                    Group = g, Lines = gl, Read = gr, Trunk = trunk.Contains(g), Reach = seen.Contains(g), Visited = Visited(gs),
                    ViaMission = gs.Any(z => z != start && Starts(z)), Dead = deads.Count > 0 && deads.All(d => d.Length > 0) ? deads[0] : "",
                    Label = RouteMapDefs.Names.TryGetValue(def.Id + "/" + g, out var named) ? named : FirstClause(sm.FirstOrDefault() ?? g),
                    Conditions = conditions.Take(2).ToList(), Summary = sm, Scripts = gs, Branch = gs.Count(Branchy),
                    Next = Next(g).Where(groups.ContainsKey).OrderBy(x => x, StringComparer.Ordinal).ToList()
                });
            }
            var (read, lines) = Reading(sc);
            station.Read = read; station.Lines = lines; station.Branch = sc.Count(Branchy); station.Endings = sc.Count(Ending); station.Visited = Visited(sc);
            station.Variants = def.Variants.Length == 0 ? 0 : missions.Keys.Count(k => def.Variants.Any(v => k.StartsWith(v, StringComparison.Ordinal)) && k.Count(c => c == '_') >= 2);
            station.DeadVariants = missionStation.Where(p => p.Value == def.Id && missions.ContainsKey(p.Key)).Select(p => Dead(missions[p.Key].Groups)).Where(d => d.Length > 0).Distinct().OrderBy(d => d, StringComparer.Ordinal).ToList();
            station.Summary = Summary(sc, start);
            map.Stations.Add(station);
            yield return null;
        }
        map.Edges.AddRange(RouteMapDefs.Edges);
        Phase("stations");

        // ---- side routes that replace a stretch of the main line ----
        // Their scripts move to them only after all of them are laid out (what they influence is read as before).
        var moved = new List<(string Script, string Route)>();
        foreach (var route in RouteMapDefs.Routes)
        {
            var sc = route.Arcs.Where(arcs.ContainsKey).SelectMany(a => series[a]).OrderBy(x => x, StringComparer.Ordinal).ToList();
            foreach (string a in route.Arcs) if (arcs.TryGetValue(a, out var ra)) ra.Route = route.Id;
            foreach (string x in sc) moved.Add((x, route.Id));
            var first = arcs.TryGetValue(route.Arcs[0], out var fa) ? fa : null;
            var station = new RouteMap.Station
            {
                Id = route.Id, Lane = route.Lane, Label = route.Label, Kind = "route", Note = route.Note, Date = first?.Lo ?? stationDate[route.From],
                Start = first?.Start ?? "", Scripts = sc, Route = route.Label.Split('（')[0].Split(' ')[0], Exits = new List<string> { route.To }
            };
            foreach (string a in route.Arcs)
            {
                if (!arcs.TryGetValue(a, out var arc)) continue;
                foreach (var (k, v) in arc.Stages)
                {
                    var gs = series[a].Where(x => x == k || x.StartsWith(k + "_", StringComparison.Ordinal)).ToList();
                    var (gr, gl) = Reading(gs);
                    station.Sections.Add(new RouteMap.Section { Group = k, Lines = gl, Read = gr, Trunk = true, Reach = true, Visited = Visited(gs), Label = FirstClause(v), Summary = new List<string> { v }, Scripts = gs, Branch = gs.Count(Branchy) });
                }
            }
            var (read, lines) = Reading(sc);
            station.Read = read; station.Lines = lines; station.Branch = sc.Count(Branchy); station.Endings = sc.Count(Ending); station.Visited = Visited(sc);
            station.Summary = Summary(sc, station.Start); station.Influence = Influence(sc);
            station.Entry = first == null ? new List<List<string>>() : first.Entries.Select(e => e.Conditions.FirstOrDefault() ?? new List<string>()).Take(2).ToList();
            if (station.Entry.Count == 0 && first != null) station.Entry = first.Calendar.Take(2).Select(c => new List<string> { c }).ToList();
            map.Stations.Add(station);
            map.Edges.Add((route.From, route.Id, route.Kind == "main" ? "main" : "free"));
            map.Edges.Add((route.Id, route.To, "free"));
        }
        foreach (var (x, id) in moved) { owner[x] = "stn:" + id; stationOf[x] = id; }

        Phase("routes");
        // ---- what is left: free-play categories, with their named events ----
        var categories = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (string x in keys) if (owner[x].StartsWith("cat:", StringComparison.Ordinal)) Get(categories, owner[x].Substring(4)).Add(x);
        var lineCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pair in categories) { lineCounts[pair.Key] = Reading(pair.Value).Lines; yield return null; }
        counted = 0;
        foreach (var pair in categories.OrderByDescending(p => lineCounts[p.Key]).ThenBy(p => p.Key, StringComparer.Ordinal))
        {
            var sc = pair.Value;
            var (read, lines) = Reading(sc);
            string first = sc.FirstOrDefault(s => facts[s].Adds.Count > 0) ?? sc[0];
            var category = new RouteMap.Category { Id = pair.Key, Lines = lines, Read = read, Visited = Visited(sc), Scripts = sc, Sample = Summary(new[] { first }, first, 1) };
            foreach (string x in sc)
            {
                if (!events.TryGetValue(x, out var ev) || ev.Name.Length == 0) continue;
                var gs = sc.Where(y => y == x || y.StartsWith(x + "_", StringComparison.Ordinal)).ToList();
                var (er, el) = Reading(gs);
                var (lo, hi) = ev.Condition != null ? TimesIn(new[] { ev.Condition }) : (null, null);
                category.Events.Add(new RouteMap.CategoryEvent { Name = ev.Name, Condition = ev.Condition?.Text ?? "", Script = x, Lo = lo, Hi = hi, Lines = el, Read = er, Visited = Visited(gs) });
                yield return null;
            }
            map.Categories.Add(category);
            yield return null;
        }

        // ---- the rest of the map ----
        foreach (var arc in arcs.Values.Where(a => a.Route.Length == 0).OrderBy(a => a.Id, StringComparer.Ordinal))
        {
            arc.Named = RouteMapDefs.Names.TryGetValue(arc.Id, out var named);
            arc.Label = named ?? (arc.Desc.Length > 0 && arc.Desc.Length <= 12 ? arc.Desc : "");
            if (arc.Label.Length == 0) arc.Label = arc.Summary.Count > 0 ? FirstClause(arc.Summary[0]) : "";
            if (arc.Label.Length == 0) arc.Label = T("未命名 {0}", arc.Id);
            arc.Always = arc.Lo != null && arc.Hi != null && Rounds(arc.Hi) - Rounds(arc.Lo) > 24 * 3;
            map.Arcs.Add(arc);
        }
        foreach (string x in ownerOrder) map.Owner[x] = owner[x];
        map.NextStation = missionStation.TryGetValue(source.CurrentMain, out var next) ? next : "";
        var allKeys = new HashSet<string>(facts.Values.SelectMany(f => f.Keys), StringComparer.Ordinal);
        var drawn = keys.Where(x => !owner[x].StartsWith("cat:", StringComparison.Ordinal)).ToList();
        map.ScriptsAll = keys.Count; map.LinesAll = allKeys.Count; map.ReadAll = allKeys.Count(source.Read); map.BranchAll = keys.Count(Branchy);
        map.DrawnScripts = drawn.Count; map.DrawnLines = new HashSet<string>(drawn.SelectMany(x => facts[x].Keys), StringComparer.Ordinal).Count;
        counted = 0;
        foreach (string x in keys)
        {
            if (++counted % 200 == 0) yield return null;
            map.Keys[x] = facts[x].Keys;
            var legends = new List<string>();
            foreach (string key in facts[x].Adds) { string t = source.Legend(key).Trim(); if (t.Length > 0 && !legends.Contains(t)) legends.Add(t); }
            map.Legends[x] = legends;
        }
        Phase("categories and index");
        yield break;
    }

    private static List<string> Run(Dictionary<string, List<string>> runs, string id) => runs.TryGetValue(id, out var l) ? l : new List<string>();
    private static TV Get<TK, TV>(Dictionary<TK, TV> d, TK k) where TV : new() where TK : notnull { if (!d.TryGetValue(k, out var v)) d[k] = v = new TV(); return v; }
    /// <summary>A date as a count of 旬 from the first one.</summary>
    public static int Rounds(int[] t) => (t[0] - 1) * 36 + (t[1] - 1) * 3 + (t[2] - 1);
    private static int[]? Add3(int[]? t, int months, int stages)
    {
        if (t == null) return null;
        int v = Rounds(t) + months * 3 + stages;
        return new[] { v / 36 + 1, v % 36 / 3 + 1, v % 3 + 1 };
    }
    /// <summary>A short name from a line of story record: its first clause, at most 16 characters.</summary>
    public static string FirstClause(string text, int length = 16)
    {
        string first = (text ?? "").Trim().Split('\n')[0];
        string clause = Regex.Split(first, "[，。！？：；、]")[0].Trim();
        return clause.Length <= length ? clause : clause.Substring(0, length) + "…";
    }
    private sealed class IntArrayOrder : IComparer<int[]>
    {
        public int Compare(int[]? x, int[]? y)
        {
            x ??= new int[0]; y ??= new int[0];
            for (int i = 0; i < Math.Min(x.Length, y.Length); i++) if (x[i] != y[i]) return x[i].CompareTo(y[i]);
            return x.Length.CompareTo(y.Length);
        }
    }
}
