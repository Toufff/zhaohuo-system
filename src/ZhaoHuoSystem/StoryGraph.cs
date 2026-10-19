using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ZhaoHuoSystem;

/// <summary>
/// 剧情树 data (F87), kept apart from how it is shown so another view can use it as it is: story segments and the ways
/// between them, read from the scripts alone, each segment only when it is first asked for.
/// A segment starts at a script and takes in the scripts after it that go one way on without a choice, up to a script
/// that branches (a menu, a dice roll, a fight, conditional jumps), returns to the map, or moves the main mission on.
/// Its ways are what leaves that last script: each option and dice result (with the condition offering it and the
/// arms inside it), each conditional jump, and the plain next script or the one after a fight.
/// </summary>
public sealed class StoryGraph(Func<string, string?> text, Func<string, Dictionary<string, OptionPreview>?> map)
{
    public enum WayKind { Option, Roll, Arm, Next, AfterFight }

    public sealed class Segment
    {
        /// <summary>The first script; the segment's name.</summary>
        public string Id = "";
        /// <summary>The scripts it runs through, in order; the last is where it branches or ends.</summary>
        public List<string> Scripts = new();
        public bool Known, Menu, Dice, Fight;
        /// <summary>Its last script returns to the map / sets the main mission (outside any option): the story stops here.</summary>
        public bool ToMap;
        public string MainMission = "";
        /// <summary>Story lines shown on the way through, outside any option (for read marks).</summary>
        public List<string> TextKeys = new();
        public List<Way> Ways = new();
        public string Last => Scripts.Count > 0 ? Scripts[Scripts.Count - 1] : Id;
    }

    public sealed class Way
    {
        public WayKind Kind;
        /// <summary>Option or dice result key (its text comes from the game), and the row as written (a dice threshold).</summary>
        public string Option = "", Raw = "";
        /// <summary>The condition offering the option, and the switch arms that lead this way.</summary>
        public string Requirement = "";
        public List<KeyValuePair<string, int>> When = new();
        /// <summary>The script it goes on to; "" when it stays in this segment's script.</summary>
        public string Target = "";
        /// <summary>The option returns to the map / moves the main mission on: nothing to follow from here.</summary>
        public bool ToMap;
        public string MainMission = "";
        /// <summary>The option's own story lines (for read marks).</summary>
        public List<string> TextKeys = new();
        public bool Ends => ToMap || MainMission.Length > 0;
    }

    private static readonly Regex FightCall = new("luamanager\\s*\\.\\s*ChangeScene\\s*\\(\\s*\"(?:Combat|Battle)\"");
    private readonly Dictionary<string, Segment> segments = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The segment starting at this script (analysed now if it was not yet).</summary>
    public Segment Get(string script)
    {
        if (segments.TryGetValue(script, out var known)) return known;
        var segment = Build(script);
        segments[script] = segment;
        return segment;
    }

    private Segment Build(string script)
    {
        var segment = new Segment { Id = script };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string current = script;
        while (true)
        {
            segment.Scripts.Add(current); seen.Add(current);
            string? source = text(current);
            var options = source == null ? null : map(current);
            if (source == null || options == null || !options.TryGetValue(LuaOptions.ScriptLevel, out var top)) return segment;
            segment.Known = true;
            segment.TextKeys.AddRange(top.TextKeys);
            bool menu = top.Menus.Count > 0, dice = top.Rolls.Count > 0, fight = FightCall.IsMatch(source), arms = top.Cases.Exists(arm => arm.Story.Length > 0);
            bool stops = top.ToMap || top.SetsMain;
            string next = top.NextScript;
            // One way on and nothing to choose: the next script is part of the same segment.
            if (!menu && !dice && !fight && !arms && !stops && next.Length > 0 && !seen.Contains(next) && text(next) != null) { current = next; continue; }
            segment.Menu = menu; segment.Dice = dice; segment.Fight = fight;
            segment.ToMap = top.ToMap; segment.MainMission = top.MainMission;
            foreach (var table in top.Menus) AddRows(segment, options, table, WayKind.Option);
            foreach (var table in top.Rolls) AddRows(segment, options, table, WayKind.Roll);
            foreach (var arm in top.Cases.Where(arm => arm.Story.Length > 0))
                segment.Ways.Add(new Way { Kind = WayKind.Arm, When = arm.When, Target = arm.Story });
            if (!stops && next.Length > 0 && !segment.Ways.Exists(way => way.Kind == WayKind.Arm && way.Target.Equals(next, StringComparison.OrdinalIgnoreCase)))
                segment.Ways.Add(new Way { Kind = top.AfterScene || fight ? WayKind.AfterFight : WayKind.Next, Target = next });
            return segment;
        }
    }

    /// <summary>One way per option row, or one per arm inside it when its branch jumps by condition.</summary>
    private static void AddRows(Segment segment, Dictionary<string, OptionPreview> options, Dictionary<int, OptionSlot> table, WayKind kind)
    {
        foreach (var slot in table.OrderBy(pair => pair.Key).Select(pair => pair.Value).Where(slot => slot.Key.Length > 0))
        {
            options.TryGetValue(slot.Key, out var preview);
            Way Row() => new()
            {
                Kind = kind, Option = slot.Key, Raw = slot.Text, Requirement = slot.Requirement, ToMap = preview?.ToMap == true,
                MainMission = preview?.MainMission ?? "", TextKeys = preview?.TextKeys ?? new List<string>()
            };
            var arms = preview?.Cases.Where(arm => arm.Story.Length > 0).ToList() ?? new List<OptionCase>();
            foreach (var arm in arms) { var way = Row(); way.When = arm.When; way.Target = arm.Story; segment.Ways.Add(way); }
            string next = preview?.NextScript ?? "";
            if (arms.Count == 0 || next.Length > 0 && !arms.Exists(arm => arm.Story.Equals(next, StringComparison.OrdinalIgnoreCase)))
            { var way = Row(); way.Target = next; segment.Ways.Add(way); }
        }
    }
}
