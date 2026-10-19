using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ZhaoHuoSystem;

internal static partial class Program
{
    private static void RouteMapTests()
    {
        Test("路线图 places scripts by station, chapter jumps and prefix; a fork story hangs off its station; sections split trunk from branch (F89)", () =>
        {
            var source = new FakeRouteSource();
            source.Script("ch1_1", "say(luamanager.GetStoryText(\"L1\"))\nlocal t = {}\nt[1] = \"O_GO\"\nlocal c = choose(t)\nif c == 1 then\nluamanager.SetNextScript(\"s0003_01\")\nend\nluamanager.SetNextScript(\"ch1_1_2\")");
            source.Script("ch1_1_2", "say(luamanager.GetStoryText(\"L2\"))");
            source.Script("ch1_1_9", "say(luamanager.GetStoryText(\"L9\"))");
            source.Script("s0003_01", "say(luamanager.GetStoryText(\"S1\"))");
            source.Script("ch3_7_1", "say(luamanager.GetStoryText(\"A\"))\nlocal t = {}\nt[1] = \"O_8\"\nt[2] = \"O_9\"\nlocal c = choose(t)\nif c == 1 then\nluamanager.SetNextScript(\"ch3_8_1\")\nelseif c == 2 then\nluamanager.SetNextScript(\"ch3_9_1\")\nend");
            source.Script("ch3_8_1", "say(luamanager.GetStoryText(\"B\"))\nluamanager.SetNextScript(\"ch3_10_1\")");
            source.Script("ch3_9_1", "say(luamanager.GetStoryText(\"C\"))\nluamanager.SetNextScript(\"ch3_10_1\")");
            source.Script("ch3_10_1", "say(luamanager.GetStoryText(\"D\"))\nluamanager.SetNextScript(\"ch4_1\")");
            source.Script("ch4_1", "say(luamanager.GetStoryText(\"E\"))");
            source.Script("door_02_503", "say(luamanager.GetStoryText(\"F\"))");
            source.Mission("M0001", "ch1_1", 1, 1, 1);
            source.Mission("M2008_01", "ch3_7_1", 2, 10, 1);
            source.Mission("M2008_02", "ch4_1", 2, 11, 1);
            var map = RouteMapBuilder.Build(source, (text, args) => args.Length == 0 ? text : string.Format(text, args));
            Require(map.Owner["ch1_1_2"] == "stn:M0001", "the story leads into ch1_1_2");
            Require(map.Owner["ch1_1_9"] == "stn:M0001", "ch1_1_9 by its prefix");
            Require(map.Owner["door_02_503"] == "cat:door", "free play stays a category");
            var arc = map.Arcs.Single(a => a.Id == "S0003");
            Require(arc.Kind == "fork" && arc.Anchor == "M0001" && arc.Entries.Single().Conditions.Single().SequenceEqual(new[] { "选「O_GO」" }), "fork with its choice");
            var station = map.Stations.Single(s => s.Id == "M2008_01");
            Require(station.Sections.Select(s => s.Group).SequenceEqual(new[] { "ch3_7", "ch3_8", "ch3_9", "ch3_10" }), "sections in story order: " + string.Join(",", station.Sections.Select(s => s.Group)));
            Require(station.Sections.Single(s => s.Group == "ch3_7").Trunk && station.Sections.Single(s => s.Group == "ch3_10").Trunk, "trunk");
            Require(!station.Sections.Single(s => s.Group == "ch3_8").Trunk && station.Sections.Single(s => s.Group == "ch3_8").Conditions.Single().SequenceEqual(new[] { "选「O_8」" }), "branch with its choice");
        });

        // Optional comparison with the offline prototype (local, untracked dump and the user's own read record).
        string? research = Environment.GetEnvironmentVariable("ZHAOHUO_ROUTE_AUDIT");
        if (research == null || !Directory.Exists(research)) return;
        Test("路线图 built from the offline dump, compared with the prototype (report only)", () =>
        {
            var started = DateTime.Now;
            var source = new DumpRouteSource(research, Environment.GetEnvironmentVariable("ZHAOHUO_ROUTE_UNIVERSE"));
            var loaded = DateTime.Now;
            var report = new StringBuilder();
            var map = RouteMapBuilder.Build(source, (text, args) => args.Length == 0 ? text : string.Format(text, args), phase => report.AppendLine("  " + phase));
            var built = DateTime.Now;
            report.AppendLine($"load {(loaded - started).TotalSeconds:0.0}s build {(built - loaded).TotalSeconds:0.0}s");
            report.AppendLine($"scripts {map.ScriptsAll} lines {map.LinesAll} read {map.ReadAll} branch {map.BranchAll} drawn {map.DrawnScripts} drawnLines {map.DrawnLines}");
            report.AppendLine($"stations {map.Stations.Count} edges {map.Edges.Count} arcs {map.Arcs.Count} cats {map.Categories.Count}");
            report.AppendLine("kinds " + string.Join(" ", map.Arcs.GroupBy(a => a.Kind).OrderBy(g => g.Key).Select(g => g.Key + "=" + g.Count())));
            foreach (var st in map.Stations.Where(s => s.Id is "M2008_01" or "M2007_02" or "R_XY" or "R_YCS" or "R_SNOW" or "P0"))
            {
                report.AppendLine($"== {st.Id} {st.Label} {st.Date[0]}/{st.Date[1]}/{st.Date[2]} scripts {st.Scripts.Count} lines {st.Lines} entry {string.Join(" | ", st.Entry.Select(e => string.Join("，", e)))}");
                foreach (var x in st.Sections) report.AppendLine($"     {(x.Trunk ? "主" : x.Reach ? "岔" : "断")} {x.Group} {x.Lines} {x.Label} cond={string.Join(" | ", x.Conditions.Select(c => string.Join("，", c)))}");
            }
            foreach (var a in map.Arcs.Where(a => a.Id is "S2700" or "S2914" or "S2901" or "S2900")) report.AppendLine($"arc {a.Id} {a.Kind} {Date(a.Lo)}~{Date(a.Hi)} anchor={a.Anchor} parent={a.Parent} {a.Label}");
            var hits = map.Keys.Where(p => p.Value.Any(k => source.StoryText(k).Contains("小梅村"))).Select(p => p.Key).OrderBy(x => x).ToList();
            report.AppendLine("小梅村 " + string.Join(" ", hits));
            IEnumerable<string> Deads(RouteMap m) => m.Arcs.Where(x => x.Dead.Length > 0).Select(x => $"arc {x.Id}: {x.Dead}")
                .Concat(m.Arcs.SelectMany(x => x.Entries.Where(e => e.Dead.Length > 0).Select(e => $"entry {x.Id}<-{e.From}: {e.Dead}")))
                .Concat(m.Stations.SelectMany(st => st.Sections.Where(x => x.Dead.Length > 0).Select(x => $"section {st.Id}/{x.Group}: {x.Dead}")))
                .Concat(m.Stations.SelectMany(st => st.DeadVariants.Select(d => $"variant {st.Id}: {d}")));
            var withCodeFlags = Deads(map).ToList();
            var kept = RouteMapDefs.CodeFlags.ToList();
            RouteMapDefs.CodeFlags.Clear();
            var bare = Deads(RouteMapBuilder.Build(source, (text, args) => args.Length == 0 ? text : string.Format(text, args))).ToList();
            RouteMapDefs.CodeFlags.UnionWith(kept);
            report.AppendLine("dead with code flags: " + string.Join(" || ", withCodeFlags));
            report.AppendLine("dead only without code flags: " + string.Join(" || ", bare.Except(withCodeFlags)));
            string? proto = Environment.GetEnvironmentVariable("ZHAOHUO_ROUTE_PROTO");
            if (proto != null && File.Exists(proto))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(proto));
                var root = doc.RootElement;
                var protoArcs = root.GetProperty("arcs").EnumerateArray().ToDictionary(a => a.GetProperty("id").GetString()!, a => a.GetProperty("kind").GetString()!);
                var mine = map.Arcs.ToDictionary(a => a.Id, a => a.Kind);
                report.AppendLine("arcs only in prototype: " + string.Join(" ", protoArcs.Keys.Except(mine.Keys).OrderBy(x => x)));
                report.AppendLine("arcs only here: " + string.Join(" ", mine.Keys.Except(protoArcs.Keys).OrderBy(x => x)));
                report.AppendLine("kind differs: " + string.Join(" ", mine.Where(p => protoArcs.TryGetValue(p.Key, out var k) && k != p.Value).Select(p => $"{p.Key}:{protoArcs[p.Key]}->{p.Value}")));
                var protoCats = root.GetProperty("cats").EnumerateArray().Select(c => c.GetProperty("id").GetString()!).ToList();
                report.AppendLine("cats only in prototype: " + string.Join(" ", protoCats.Except(map.Categories.Select(c => c.Id))) + " / only here: " + string.Join(" ", map.Categories.Select(c => c.Id).Except(protoCats)));
                var protoOwner = root.GetProperty("index").EnumerateArray().ToDictionary(r => r[0].GetString()!, r => r[1].GetString()!);
                var moved = map.Owner.Where(p => protoOwner.TryGetValue(p.Key, out var o) && o != p.Value).Select(p => $"{p.Key}:{protoOwner[p.Key]}->{p.Value}").ToList();
                report.AppendLine($"owner differs for {moved.Count}: " + string.Join(" ", moved.Take(40)));
                foreach (var ps in root.GetProperty("stations").EnumerateArray())
                {
                    string id = ps.GetProperty("id").GetString()!;
                    var st = map.Stations.FirstOrDefault(s => s.Id == id);
                    int protoSections = ps.GetProperty("sections").GetArrayLength(), protoScripts = ps.GetProperty("scripts").GetInt32();
                    if (st == null || st.Sections.Count != protoSections || st.Scripts.Count != protoScripts)
                        report.AppendLine($"station {id}: sections {protoSections}->{st?.Sections.Count} scripts {protoScripts}->{st?.Scripts.Count}");
                }
            }
            string output = Environment.GetEnvironmentVariable("ZHAOHUO_ROUTE_REPORT") ?? Path.Combine(Path.GetTempPath(), "route-audit.txt");
            File.WriteAllText(output, report.ToString(), new UTF8Encoding(false));
            Console.WriteLine("route audit written to " + output);
        });
    }
    private static string Date(int[]? t) => t == null ? "-" : $"{t[0]}/{t[1]}/{t[2]}";

    private sealed class FakeRouteSource : IRouteSource
    {
        private readonly Dictionary<string, string> scripts = new();
        private readonly List<RouteMission> missions = new();
        public void Script(string name, string text) => scripts[name] = text;
        public void Mission(string key, string start, int y, int m, int s) => missions.Add(new RouteMission { Key = key, Start = start, Active = true, TimeType = 1, Specific = new[] { y, m, s } });
        public IEnumerable<string> Scripts => scripts.Keys;
        public string? Text(string script) => scripts.TryGetValue(script, out var t) ? t : null;
        public IEnumerable<RouteMission> Missions => missions;
        public IEnumerable<RouteEvent> Events => Enumerable.Empty<RouteEvent>();
        public IReadOnlyList<RouteGroup>? Groups(string name) => null;
        public IEnumerable<string> ConditionFlags(string name) => Enumerable.Empty<string>();
        public IEnumerable<string> MissionFlags => Enumerable.Empty<string>();
        public string FlagNote(string flag) => "";
        public string Legend(string key) => "";
        public string OptionText(string key) => key;
        public bool Read(string key) => false;
        public bool Visited(string addKey) => false;
        public string CurrentMain => "";
    }

    /// <summary>The offline dump read the way the prototype read it (lom.py, st8.py), to compare the two.</summary>
    private sealed class DumpRouteSource : IRouteSource
    {
        private static readonly string[] Ops = { "=", "!=", "<", "<=", ">", ">=" };
        private static readonly Dictionary<string, int> Numbers = new() { ["一"] = 1, ["二"] = 2, ["三"] = 3, ["四"] = 4, ["五"] = 5, ["六"] = 6, ["七"] = 7, ["八"] = 8, ["九"] = 9, ["十"] = 10, ["十一"] = 11, ["十二"] = 12 };
        private static readonly Dictionary<string, int> Stages = new() { ["上"] = 1, ["中"] = 2, ["下"] = 3 };
        private readonly string lua;
        private readonly Dictionary<(string, long), JsonElement> records = new();
        private readonly Dictionary<string, List<(string File, JsonElement Data)>> byName = new();
        private readonly Dictionary<string, string[]> ext = new();
        private readonly Dictionary<string, string> zhs = new(), zht = new();
        private readonly HashSet<string> read = new();
        private readonly List<JsonDocument> docs = new();

        public DumpRouteSource(string research, string? universe)
        {
            lua = Path.Combine(research, "lua-25987745");
            foreach (string line in File.ReadLines(Path.Combine(research, "f11", "mb.jsonl")))
            {
                JsonDocument doc;
                try { doc = JsonDocument.Parse(line); } catch { continue; }
                docs.Add(doc);
                var r = doc.RootElement;
                if (!r.TryGetProperty("data", out var d) || d.ValueKind != JsonValueKind.Object) continue;
                string file = r.GetProperty("file").GetString()!;
                if (!ext.ContainsKey(file) && r.TryGetProperty("ext", out var e)) ext[file] = e.EnumerateArray().Select(x => x.GetString()!).ToArray();
                records[(file, r.GetProperty("path").GetInt64())] = d;
                if (d.TryGetProperty("m_Name", out var n) && n.GetString() is { Length: > 0 } name) { if (!byName.TryGetValue(name, out var l)) byName[name] = l = new(); l.Add((file, d)); }
                if (d.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
                {
                    string lang = d.TryGetProperty("Language", out var lg) ? lg.GetString() ?? "" : "";
                    foreach (var en in entries.EnumerateArray())
                    {
                        if (en.ValueKind != JsonValueKind.Object || !en.TryGetProperty("Name", out var nm) || !en.TryGetProperty("Text", out var tx)) continue;
                        string key = nm.GetString() ?? "", text = tx.GetString() ?? "";
                        if (key.Length == 0 || text.Contains('�')) continue;
                        if (lang == "ChineseSimplified") zhs.TryAdd(key, text);
                        else if (lang == "ChineseTraditional" || Regex.IsMatch(text, "[一-鿿]") && !Regex.IsMatch(text, "[가-힯]")) zht.TryAdd(key, text);
                    }
                }
            }
            if (universe != null && File.Exists(universe))
            {
                using var u = JsonDocument.Parse(File.ReadAllText(universe));
                foreach (var k in u.RootElement.GetProperty("ReadStoryData").EnumerateArray()) read.Add(k.GetString()!);
            }
        }

        private (string File, JsonElement Data)? Ref(string file, JsonElement r)
        {
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("m_PathID", out var p) || p.GetInt64() == 0) return null;
            int fid = r.GetProperty("m_FileID").GetInt32();
            string target = fid == 0 ? file : ext.TryGetValue(file, out var e) && fid - 1 < e.Length ? e[fid - 1] : "globalgamemanagers.assets";
            return records.TryGetValue((target, p.GetInt64()), out var d) ? (target, d) : null;
        }
        private static string Str(JsonElement d, string name) => d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        private static int Int(JsonElement d, string name) => d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

        public IEnumerable<string> Scripts => Directory.EnumerateFiles(lua, "*.txt").Select(f => Path.GetFileNameWithoutExtension(f).ToLowerInvariant());
        public string? Text(string script) { string path = Path.Combine(lua, script + ".txt"); return File.Exists(path) ? File.ReadAllText(path) : null; }
        public string StoryText(string key) => zhs.TryGetValue("Story/" + key, out var t) ? t : zht.TryGetValue("Story/" + key, out t) ? t : "";

        // A value record as lom.val renders it: its name, and for a flag the flag's name and note.
        private (string Name, string Flag, string Note, bool IsConstant, int Constant) Value(string file, JsonElement r)
        {
            if (Ref(file, r) is not var (vf, d)) return ("?", "", "", false, 0);
            string name = Str(d, "m_Name");
            string flag = "", note = "";
            if (d.TryGetProperty("_value", out var vals) && vals.ValueKind == JsonValueKind.Array && vals.GetArrayLength() > 0)
            {
                var v0 = vals[0];
                if (Int(v0, "_checkType") == 3 && v0.TryGetProperty("_flag", out var fr) && Ref(vf, fr) is var (_, fd)) { flag = Str(fd, "m_Name"); note = Str(fd, "_devNote").Replace("\r\n", " ").Replace("\n", " "); }
                if (Int(v0, "_checkType") == 0 && vals.GetArrayLength() == 1) return (name, "", "", true, Int(v0, "_constant"));
            }
            return (flag.Length > 0 ? $"{name}[{flag}({note})]" : name, flag, note, false, 0);
        }

        private RouteGroup Group(string file, JsonElement group)
        {
            var result = new RouteGroup();
            var parts = new List<string>();
            var items = group.TryGetProperty("_items", out var its) && its.ValueKind == JsonValueKind.Array ? its.EnumerateArray().ToList() : new List<JsonElement>();
            foreach (var it in items)
            {
                var a = Value(file, it.GetProperty("_value1")); var b = Value(file, it.GetProperty("_value2"));
                string op = Ops[Math.Min(5, Math.Max(0, Int(it, "_compareOp")))];
                parts.Add($"{a.Name} {op} {b.Name}");
                if (a.Flag.Length > 0 && b.IsConstant && b.Constant >= 1 && op is "=" or ">=") result.Requires.Add((a.Flag, a.Note));
                if (a.Name == "時間_目前時間")
                {
                    var m = Regex.Match(b.Name, "^時間_第(\\S)年(\\S{1,2})月(\\S)旬");
                    if (m.Success) result.Times.Add((op, new[] { Numbers.GetValueOrDefault(m.Groups[1].Value), Numbers.GetValueOrDefault(m.Groups[2].Value), Stages.GetValueOrDefault(m.Groups[3].Value, 1) }));
                    if (op == "=" && b.Name.StartsWith("時間_S")) result.Scheduled = true;
                }
                if (items.Count == 1 && b.Name.StartsWith("常數_") && !Regex.IsMatch(a.Name, "\\s"))
                    result.Flip = Readable($"({a.Name} {Flip(op)} {b.Name})");
            }
            string raw = "(" + string.Join(Int(group, "_logicOp") == 1 ? " OR " : " AND ", parts) + ")";
            result.Noise = Regex.IsMatch(raw, "旗標暫存|決鬥泛用|通用決鬥成敗");
            result.Text = raw == "()" ? "" : Readable(raw);
            return result;
        }
        private static string Flip(string op) => op switch { ">=" => "<", "<=" => ">", ">" => "<=", "<" => ">=", "=" => "!=", _ => "=" };
        // st8.readable, without the traditional-to-simplified step.
        private static string Readable(string c)
        {
            c = Regex.Replace(c, "旗標_\\w+\\[(\\w+)\\(([^)]*)\\)\\]\\s*=\\s*常數_0", m => "未" + (m.Groups[2].Value.Trim().Length > 0 ? m.Groups[2].Value.Trim() : m.Groups[1].Value));
            c = Regex.Replace(c, "旗標_\\w+\\[(\\w+)\\(([^)]*)\\)\\]\\s*(>=|=)\\s*常數_(\\d+)", m => (m.Groups[2].Value.Trim().Length > 0 ? m.Groups[2].Value.Trim() : m.Groups[1].Value) + (m.Groups[3].Value == "=" && m.Groups[4].Value == "1" ? "" : m.Groups[3].Value + m.Groups[4].Value));
            c = Regex.Replace(c, "(?:好感度)_(\\S+) (>=|<=|>|<|=|!=) 常數_(-?\\d+)", "$1好感$2$3");
            c = Regex.Replace(c, "(?:數值|技能|貴重品)_(\\S+) (>=|<=|>|<|=|!=) 常數_(-?\\d+)", "$1$2$3");
            c = Regex.Replace(c, "時間_目前時間 (>=|<=|=|>|<) 時間_(第\\S+)", m => (m.Groups[1].Value switch { ">=" => "时间不早于", "<=" => "时间不晚于", "=" => "时间为", ">" => "时间晚于", _ => "时间早于" }) + m.Groups[2].Value);
            c = Regex.Replace(c, "隨機值_\\S+ (<=|>=) 常數_\\d+", "随机");
            c = Regex.Replace(c, "\\s*AND\\s*", "，"); c = Regex.Replace(c, "\\s*OR\\s*", " 或 ");
            return c.Trim('(', ')');
        }

        public IEnumerable<RouteMission> Missions
        {
            get
            {
                foreach (var (file, d) in records.Select(p => (p.Key.Item1, p.Value)))
                {
                    if (!d.TryGetProperty("_startScript", out _)) continue;
                    int[]? Time(string name) => d.TryGetProperty(name, out var t) && t.ValueKind == JsonValueKind.Object && Int(t, "Year") > 0 ? new[] { Int(t, "Year"), Int(t, "Month"), Int(t, "Stage") } : null;
                    var m = new RouteMission { Key = Str(d, "m_Name"), Start = Str(d, "_startScript"), Desc = Str(d, "_desc"), Active = Int(d, "_active") == 1, TimeType = Int(d, "_timeCheckType"),
                        Specific = Time("_specificTime"), Min = Time("_minTime"), Max = Time("_maxTime") };
                    if (d.TryGetProperty("_conditions", out var cs) && cs.ValueKind == JsonValueKind.Array) foreach (var g in cs.EnumerateArray()) m.Groups.Add(Group(file, g));
                    yield return m;
                }
            }
        }
        public IEnumerable<RouteEvent> Events
        {
            get
            {
                foreach (var (file, d) in records.Select(p => (p.Key.Item1, p.Value)))
                {
                    if (!d.TryGetProperty("_scriptPrefix", out var prefix) || !d.TryGetProperty("_list", out var list) || list.ValueKind != JsonValueKind.Array) continue;
                    foreach (var r in list.EnumerateArray())
                    {
                        if (Ref(file, r) is not var (ef, e)) continue;
                        if (!e.TryGetProperty("_config", out var cr) || Ref(ef, cr) is not var (_, cfg) || Str(cfg, "Value") is not { Length: > 0 } value) continue;
                        yield return new RouteEvent { Script = (prefix.GetString() + "_" + value).ToLowerInvariant(), Name = Str(e, "_devNote").Trim(), Table = Str(d, "m_Name"),
                            Condition = e.TryGetProperty("_activeCondition", out var ac) && ac.ValueKind == JsonValueKind.Object ? Group(ef, ac) : null };
                    }
                }
            }
        }
        private IEnumerable<(string File, JsonElement Group)> GroupsOf(string name)
        {
            if (!byName.TryGetValue(name, out var list)) yield break;
            foreach (var (file, d) in list)
                foreach (string k in new[] { "_items", "_conditions" })
                    if (d.TryGetProperty(k, out var items) && items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0 && items[0].ValueKind == JsonValueKind.Object && items[0].TryGetProperty("_items", out _))
                        foreach (var g in items.EnumerateArray()) yield return (file, g);
        }
        public IReadOnlyList<RouteGroup>? Groups(string name)
        {
            if (!byName.TryGetValue(name, out var list)) return null;
            foreach (var (file, d) in list)
                if (d.TryGetProperty("_items", out var items) && items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0 && items[0].ValueKind == JsonValueKind.Object && items[0].TryGetProperty("_items", out _))
                    return items.EnumerateArray().Select(g => Group(file, g)).ToList();
            return null;
        }
        public IEnumerable<string> ConditionFlags(string name)
        {
            var flags = new HashSet<string>();
            foreach (var (file, g) in GroupsOf(name))
                foreach (var it in g.GetProperty("_items").EnumerateArray())
                    foreach (string side in new[] { "_value1", "_value2" })
                        if (Value(file, it.GetProperty(side)).Flag is { Length: > 0 } f) flags.Add(f);
            return flags;
        }
        public IEnumerable<string> MissionFlags
        {
            get
            {
                var set = new HashSet<string>();
                void Walk(string file, JsonElement e)
                {
                    if (e.ValueKind == JsonValueKind.Object)
                    {
                        if (e.TryGetProperty("m_PathID", out var p) && p.GetInt64() != 0 && Ref(file, e) is var (_, fd) && fd.TryGetProperty("_devNote", out _) && fd.TryGetProperty("State", out _)) set.Add(Str(fd, "m_Name"));
                        foreach (var prop in e.EnumerateObject()) Walk(file, prop.Value);
                    }
                    else if (e.ValueKind == JsonValueKind.Array) foreach (var x in e.EnumerateArray()) Walk(file, x);
                }
                foreach (var ((file, _), d) in records)
                    foreach (string k in new[] { "_modifyFlagsWhenTrue", "_modifyFlagsWhenFalse" })
                        if (d.TryGetProperty(k, out var v)) Walk(file, v);
                return set;
            }
        }
        public string FlagNote(string flag) => byName.TryGetValue(flag, out var l) ? l.Select(x => Str(x.Data, "_devNote")).FirstOrDefault(n => n.Length > 0)?.Replace("\r\n", " ").Replace("\n", " ") ?? "" : "";
        public string Legend(string key) => zhs.TryGetValue("LegendInfo/" + key, out var t) ? t : zht.TryGetValue("LegendInfo/" + key, out t) ? t : "";
        public string OptionText(string key) => zhs.TryGetValue("Story/" + key, out var t) ? t : key;
        public bool Read(string key) => read.Contains(key);
        public bool Visited(string addKey) => false;
        public string CurrentMain => "";
    }
}
