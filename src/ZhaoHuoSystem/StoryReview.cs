using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using HarmonyLib;
using Mortal.Core;
using UnityEngine;

namespace ZhaoHuoSystem;

/// <summary>
/// 剧情回顾 (F84). As a branch script starts (a menu, a dice roll or a fight), the save the game would write right then,
/// F71's copy, is kept in the mod's own folder: the latest one per script, with the game date, the main mission then,
/// the script's options (requirements read right then) and when it was kept. A starred node is a copy of its own that
/// later starts of the script never replace. The review page lists them, marks options whose story the player has not
/// read yet (the game's own read-story record, across playthroughs), and writes one into an empty manual slot through
/// the game's own save as a story save (SegmentSaves): it loads from the load page like any save and plays the script
/// again from its start with the values it started with, music and all. No existing save is touched.
/// </summary>
internal sealed class StoryReview(ManualLogSource log, string folder)
{
    internal sealed class Node
    {
        /// <summary>The script for its latest start; script@ticks for a starred copy.</summary>
        public string Id = "", Script = "", Mission = "", Music = "", Env = "", Tags = "";
        public int Year, Month, Stage;
        public long Kept;
        public bool Favourite;
        /// <summary>Each menu row: its option key and its text with the requirement as kept.</summary>
        public List<KeyValuePair<string, string>> Rows = new();
    }
    private const string Header = "# 赵活系统：剧情回顾收录的节点。只有本模组读取，游戏存档不受影响；删掉 review 文件夹即全部清空。";
    // Line breaks and pairs inside a field of the one-line-per-node index.
    private const char Break = '\u001f', Pair = '\u001e';
    private static readonly MethodInfo? SaveFile = AccessTools.Method(typeof(SaveSystem), "GetSaveDataFile");
    private readonly Dictionary<string, Node> nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> reading = new(StringComparer.OrdinalIgnoreCase);
    private bool loaded;
    public Func<string, string?>? ScriptText;
    public Func<string, Dictionary<string, OptionPreview>?>? ScriptMap;
    /// <summary>Bumped on every change, so the page rebuilds its list only then.</summary>
    public int Revision { get; private set; }
    private string Index => Path.Combine(folder, "index.txt");
    private string NodeFile(string id) => Path.Combine(folder, id.ToLowerInvariant() + ".json.gz");

    /// <summary>The nodes to list: a latest start that a starred copy already holds is left out.</summary>
    public IEnumerable<Node> Nodes
    {
        get
        {
            Load();
            var starred = new HashSet<string>(nodes.Values.Where(node => node.Favourite).Select(node => node.Script + "\n" + node.Kept), StringComparer.OrdinalIgnoreCase);
            return nodes.Values.Where(node => node.Favourite || !starred.Contains(node.Script + "\n" + node.Kept)).ToList();
        }
    }

    /// <summary>Each script's latest kept start (剧情树 offers 存入 on the segments that have one, F87).</summary>
    public Dictionary<string, Node> Latest()
    {
        Load();
        return nodes.Values.Where(node => !node.Favourite).GroupBy(node => node.Script, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>From SegmentSaves, as a script starts with its copy at hand.</summary>
    public void Keep(string script, GameSave save, string music, string env)
    {
        try
        {
            if (ScriptText?.Invoke(script) is not string text || !LuaOptions.IsBranch(text)) return;
            Load();
            Directory.CreateDirectory(folder);
            var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(save));
            using (var file = File.Create(NodeFile(script)))
            using (var zip = new GZipStream(file, CompressionMode.Compress))
                zip.Write(bytes, 0, bytes.Length);
            var main = MissionManagerData.Instance?.MainCheckData;
            var tags = new List<string>();
            if (LuaOptions.DiceCall.IsMatch(text)) tags.Add(L.Text("掷骰"));
            if (LuaOptions.FightCall.IsMatch(text)) tags.Add(L.Text("战斗"));
            nodes[script] = new Node
            {
                Id = script, Script = script, Year = save.CurrentYear, Month = save.CurrentMonth, Stage = (int)save.CurrentStage,
                Mission = main == null ? "" : FirstLine(R.Field<string>(main, "_desc", "")), Music = music, Env = env, Kept = DateTime.Now.Ticks,
                Rows = Rows(script), Tags = string.Join(" · ", tags)
            };
            reading.Remove(script);
            Save();
        }
        catch (Exception ex) { log.LogWarning("REVIEW keep " + script + " " + ex.GetBaseException().Message); }
    }

    /// <summary>
    /// ☆ keeps a copy of this node that later starts of the script never replace; ★ removes that copy.
    /// </summary>
    public OperationResult Star(Node node)
    {
        try
        {
            if (node.Favourite)
            {
                nodes.Remove(node.Id);
                if (File.Exists(NodeFile(node.Id))) File.Delete(NodeFile(node.Id));
                Save();
                return OperationResult.Ok(L.Text("已取消收藏"));
            }
            string id = node.Script + "@" + node.Kept.ToString(CultureInfo.InvariantCulture);
            File.Copy(NodeFile(node.Id), NodeFile(id), true);
            nodes[id] = new Node
            {
                Id = id, Script = node.Script, Mission = node.Mission, Music = node.Music, Env = node.Env, Tags = node.Tags,
                Year = node.Year, Month = node.Month, Stage = node.Stage, Kept = node.Kept, Favourite = true, Rows = node.Rows
            };
            Save();
            return OperationResult.Ok(L.Text("已收藏，这一份不会被新收录覆盖"));
        }
        catch (Exception ex) { log.LogWarning("REVIEW star " + node.Id + " " + ex.GetBaseException().Message); return OperationResult.Fail(L.Text("写入失败，详见日志")); }
    }

    private void Save()
    {
        string Line(Node node) => string.Join("\t", node.Id, node.Script, node.Year, node.Month, node.Stage, node.Kept, node.Favourite ? 1 : 0,
            node.Music, node.Env, Field(node.Mission), Field(node.Tags), string.Join(Break.ToString(), node.Rows.Select(row => Field(row.Key) + Pair + Field(row.Value))));
        File.WriteAllLines(Index, new[] { Header }.Concat(nodes.Values.Select(Line)), new UTF8Encoding(false));
        Revision++;
    }
    private static string Field(string text) => text.Replace('\t', ' ').Replace("\r", "").Replace('\n', ' ').Replace(Break, ' ').Replace(Pair, ' ');

    private void Load()
    {
        if (loaded) return;
        loaded = true;
        try
        {
            if (!File.Exists(Index)) return;
            foreach (string line in File.ReadAllLines(Index, Encoding.UTF8))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                var parts = line.Split('\t');
                if (parts.Length < 12 || !File.Exists(NodeFile(parts[0]))) continue;
                nodes[parts[0]] = new Node
                {
                    Id = parts[0], Script = parts[1], Year = Number(parts[2]), Month = Number(parts[3]), Stage = Number(parts[4]),
                    Kept = long.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out long kept) ? kept : 0, Favourite = parts[6] == "1",
                    Music = parts[7], Env = parts[8], Mission = parts[9], Tags = parts[10],
                    Rows = parts[11].Split(new[] { Break }, StringSplitOptions.RemoveEmptyEntries).Select(row => row.Split(Pair)).Where(pair => pair.Length == 2)
                        .Select(pair => new KeyValuePair<string, string>(pair[0], pair[1])).ToList()
                };
            }
        }
        catch (Exception ex) { log.LogWarning("REVIEW index " + ex.GetBaseException().Message); }
    }
    private static int Number(string text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
    private static string FirstLine(string desc)
    {
        string first = desc.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
        return first.Length > 52 ? first.Substring(0, 52) + "…" : first;
    }

    /// <summary>The script's menu rows with their requirements as they read now (the start of the script).</summary>
    private List<KeyValuePair<string, string>> Rows(string script)
    {
        var rows = new List<KeyValuePair<string, string>>();
        try
        {
            if (ScriptMap?.Invoke(script) is { } map && map.TryGetValue(LuaOptions.ScriptLevel, out var top))
                foreach (var menu in top.Menus)
                    foreach (var slot in menu.OrderBy(pair => pair.Key).Select(pair => pair.Value).Where(slot => slot.Key.Length > 0))
                        rows.Add(new(slot.Key, OptionText(slot.Key) + (slot.Requirement.Length > 0 ? " " + L.Format("（需 {0}）", Tone.Plain(OptionHints.RequirementText(slot.Requirement))) : "")));
        }
        catch (Exception ex) { log.LogWarning("REVIEW options " + script + " " + ex.GetBaseException().Message); }
        return rows;
    }
    private static string OptionText(string key) => Regex.Replace(L.GameText("Story/" + key, key), "<[^>]+>", "").Trim();

    /// <summary>Read marks are taken again the next time they are asked for (each time the page opens).</summary>
    public void ForgetReading() => reading.Clear();

    /// <summary>
    /// For each option of the node's script: 「未读」 when none of its story lines (its own and the start of the script it
    /// leads to) is in the game's read-story record, 「部分已读」 when some are, nothing when all are or it has none.
    /// </summary>
    private Dictionary<string, string> Reading(string script)
    {
        if (reading.TryGetValue(script, out var marks)) return marks;
        reading[script] = marks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var read = ReadStorySystem.Instance;
            if (read == null || ScriptMap?.Invoke(script) is not { } map) return marks;
            foreach (var pair in map)
            {
                if (pair.Key == LuaOptions.ScriptLevel) continue;
                var keys = new HashSet<string>(pair.Value.TextKeys, StringComparer.Ordinal);
                if (pair.Value.NextScript.Length > 0 && ScriptMap(pair.Value.NextScript) is { } next && next.TryGetValue(LuaOptions.ScriptLevel, out var top))
                    keys.UnionWith(top.TextKeys);
                if (keys.Count == 0) continue;
                int seen = keys.Count(read.ExistKey);
                if (seen < keys.Count) marks[pair.Key] = L.Text(seen == 0 ? "未读" : "部分已读");
            }
        }
        catch (Exception ex) { log.LogWarning("REVIEW reading " + script + " " + ex.GetBaseException().Message); }
        return marks;
    }

    /// <summary>The row's first line: the game date and the main mission then.</summary>
    public static string Title(Node node)
    {
        string when = node.Stage is >= 1 and <= 3 ? Conditions.TimeName(new GameTime(node.Year, node.Month, (MonthStageType)node.Stage)) : "";
        return (when + "  " + (node.Mission.Length > 0 ? L.DisplayText(node.Mission) : node.Script)).Trim();
    }
    /// <summary>What the search looks through.</summary>
    public static string SearchText(Node node) => node.Script + " " + node.Tags + " " + string.Join(" ", node.Rows.Select(row => row.Value));

    /// <summary>The row's second line (each option with its read mark) and the full list for hovering.</summary>
    public (string Summary, string Detail) Describe(Node node)
    {
        var marks = Reading(node.Script);
        var rows = node.Rows.Select(row => row.Value + (marks.TryGetValue(row.Key, out var mark) ? "〔" + mark + "〕" : "")).ToList();
        string summary = string.Join(" ／ ", rows);
        if (node.Tags.Length > 0) summary = summary.Length > 0 ? summary + " · " + node.Tags : node.Tags;
        string kept = node.Kept > 0 ? new DateTime(node.Kept).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "";
        string detail = string.Join("\n", rows.Select(row => "· " + row)) + (node.Tags.Length > 0 ? "\n" + node.Tags : "") + "\n\n" + node.Script
            + (kept.Length > 0 ? "\n" + L.Format("收录于 {0}", kept) : "") + (node.Favourite ? "\n" + L.Text("已收藏，这一份不会被新收录覆盖") : "")
            + (node.Rows.Count > 0 ? "\n" + L.Text("条件里的数值是收录时的；未读、部分已读按全部周目读过的对白算") : "");
        return (summary, detail.Trim());
    }

    /// <summary>The first manual slot with no save in it, or "" when every one holds a save.</summary>
    public string FirstEmptySlot(int count)
    {
        for (int i = 1; i <= count; i++)
        {
            string slot = i.ToString("000", CultureInfo.InvariantCulture);
            if (!Exists(slot)) return slot;
        }
        return "";
    }
    // Without the game's own file name a slot counts as taken: nothing is ever written over.
    private static bool Exists(string slot)
    {
        try { return SaveSystem.Instance == null || SaveFile?.Invoke(SaveSystem.Instance, new object[] { slot }) is not string path || File.Exists(path); }
        catch { return true; }
    }

    /// <summary>Writes the node into this manual slot, which must be empty, as a story save.</summary>
    public OperationResult Export(Node node, string slot, int count, SegmentSaves segments)
    {
        if (SaveSystem.Instance == null || SaveFile == null) return OperationResult.Fail(L.Text("存档系统未就绪"));
        if (!int.TryParse(slot, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) || number < 1 || number > count)
            return OperationResult.Fail(L.Format("请填 1–{0} 的格子编号", count));
        slot = number.ToString("000", CultureInfo.InvariantCulture);
        if (Exists(slot)) return OperationResult.Fail(L.Format("第 {0} 格已有存档，请选空格", number));
        GameSave? save;
        try
        {
            using var file = File.OpenRead(NodeFile(node.Id));
            using var zip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new StreamReader(zip, Encoding.UTF8);
            save = JsonUtility.FromJson<GameSave>(reader.ReadToEnd());
        }
        catch (Exception ex) { log.LogWarning("REVIEW read " + node.Id + " " + ex.GetBaseException().Message); save = null; }
        if (save == null) return OperationResult.Fail(L.Text("节点文件读不出来"));
        save.TimeTick = DateTime.Now.Ticks;
        segments.Export(save, node.Script, node.Music, node.Env, slot);
        if (!Exists(slot)) return OperationResult.Fail(L.Text("写入失败，详见日志"));
        log.LogInfo($"REVIEW export {node.Id} slot={slot}");
        return OperationResult.Ok(L.Format("已存到第 {0} 格，去读档页读取", number));
    }
}
