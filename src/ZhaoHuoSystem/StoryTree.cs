using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using Mortal.Core;

namespace ZhaoHuoSystem;

/// <summary>
/// 剧情树 for the panel (F87): the main missions as roots, by the date they come due, each opening into the story from its
/// start script (<see cref="StoryGraph"/>). Here only the game's side: option, condition and arm texts, read marks from
/// the game's read-story record (across playthroughs) and the 剧情回顾 node a segment can be written from. ToolWindow
/// lays it out as a folding tree; another view can use the same graph and texts.
/// </summary>
internal sealed class StoryTree(ManualLogSource log, StoryGraph graph)
{
    internal sealed class Root { public string Key = "", Title = "", Script = ""; public int Order; }
    private List<Root>? roots;
    public StoryGraph Graph => graph;

    /// <summary>The main missions that start a story, by the date they come due (fixed or earliest); undated ones last.</summary>
    public List<Root> Roots()
    {
        if (roots != null) return roots;
        var main = MissionManagerData.Instance?.Main;
        if (main == null) return new List<Root>();
        var list = new List<Root>();
        try
        {
            foreach (var check in R.Items(R.Field(main, "_missions")).OfType<MissionCheckData>())
            {
                if (check == null || string.IsNullOrEmpty(check.StartScript) || R.Field(check, "_active") is false) continue;
                var type = R.Field(check, "_timeCheckType");
                var time = type is TimeCheckType.特定 ? R.Field<GameTime>(check, "_specificTime") : type is TimeCheckType.區間 ? R.Field<GameTime>(check, "_minTime") : null;
                string desc = R.Field<string>(check, "_desc", "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
                if (desc.Length > 52) desc = desc.Substring(0, 52) + "…";
                string when = time != null && time.Year > 0 ? Conditions.TimeName(time) : L.Text("日期不定");
                list.Add(new Root { Key = check.Key, Script = check.StartScript, Order = time != null && time.Year > 0 ? time.ConvertToRounds() : int.MaxValue,
                    Title = when + "  " + (desc.Length > 0 ? L.DisplayText(desc) : check.Key) });
            }
        }
        catch (Exception ex) { log.LogWarning("STORY_TREE roots " + ex.GetBaseException().Message); }
        return roots = list.OrderBy(root => root.Order).ThenBy(root => root.Key, StringComparer.Ordinal).ToList();
    }

    public static string SegmentTitle(StoryGraph.Segment segment)
    {
        string name = segment.Scripts.Count > 1 ? L.Format("{0} … {1}（共 {2} 段）", segment.Id, segment.Last, segment.Scripts.Count) : segment.Id;
        var tags = new List<string>();
        if (!segment.Known) tags.Add(L.Text("剧本读不出来"));
        if (segment.Menu) tags.Add(L.Text("选项"));
        if (segment.Dice) tags.Add(L.Text("掷骰"));
        if (segment.Fight) tags.Add(L.Text("战斗"));
        if (segment.ToMap) tags.Add(L.Text("回到地图"));
        if (segment.MainMission.Length > 0) tags.Add(L.Format("主线推进到 {0}", segment.MainMission));
        return name + (tags.Count > 0 ? "  · " + string.Join(" · ", tags) : "");
    }

    /// <summary>What takes the story this way: the option or dice result, the condition offering it, the arm it is in.</summary>
    public static string WayLabel(StoryGraph.Way way)
    {
        string label = way.Kind switch
        {
            StoryGraph.WayKind.Option => L.Format("「{0}」", OptionText(way.Option))
                + (way.Requirement.Length > 0 ? " " + L.Format("（需 {0}）", Tone.Plain(OptionHints.RequirementText(way.Requirement))) : ""),
            StoryGraph.WayKind.Roll => L.Format("掷骰「{0}」", OptionText(way.Option)) + Threshold(way.Raw),
            StoryGraph.WayKind.Next => L.Text("接着"),
            StoryGraph.WayKind.AfterFight => L.Text("战斗后"),
            _ => ""
        };
        if (way.When.Count > 0) label = (label.Length > 0 ? label + " " : "") + string.Join(" ", way.When.Select(arm => OptionHints.ArmText(arm.Key, arm.Value)));
        return label;
    }
    /// <summary>Where the way ends when it is not followed further.</summary>
    public static string WayEnd(StoryGraph.Way way) => way.ToMap ? L.Text("回到地图") : way.MainMission.Length > 0 ? L.Format("主线推进到 {0}", way.MainMission)
        : way.Target.Length == 0 ? L.Text("（在本段内继续）") : "";
    private static string OptionText(string key) => Regex.Replace(L.GameText("Story/" + key, key), "<[^>]+>", "").Trim();
    private static string Threshold(string raw)
    {
        int bar = raw.IndexOf('|');
        return bar >= 0 && bar < raw.Length - 1 ? " " + L.Format("（{0}）", raw.Substring(bar + 1).Trim()) : "";
    }

    /// <summary>
    /// 「未读」 when none of the way's story lines (its own and the segment it leads into) has been read in any
    /// playthrough, 「部分已读」 when some have, "" when all have or it has none.
    /// </summary>
    public string Reading(StoryGraph.Way way)
    {
        try
        {
            var read = ReadStorySystem.Instance;
            if (read == null) return "";
            var keys = new HashSet<string>(way.TextKeys, StringComparer.Ordinal);
            if (way.Target.Length > 0 && !way.Ends) keys.UnionWith(graph.Get(way.Target).TextKeys);
            if (keys.Count == 0) return "";
            int seen = keys.Count(read.ExistKey);
            return seen == keys.Count ? "" : L.Text(seen == 0 ? "未读" : "部分已读");
        }
        catch (Exception ex) { log.LogWarning("STORY_TREE reading " + ex.GetBaseException().Message); return ""; }
    }
}
