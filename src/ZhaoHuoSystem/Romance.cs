using System;
using System.Collections.Generic;
using System.Linq;

namespace ZhaoHuoSystem;

/// <summary>
/// 结缘路线图 (F117): the nine the protagonist can win, each with the flags that mark the way (认识 → 告白 / 前置 → 攻略)
/// and the 爱人 value the game gives her. Facts read from the game's own flags and scripts (G1000x 已经认识…, G1100x
/// 攻略…, G0000x 告白, M50024 叶云裳结缘前置, S0014_02 拜夏侯兰为师; PlayerInfo/Lover/n); no game text is kept here.
/// </summary>
public static class RomanceDefs
{
    public sealed class Step
    {
        public string Flag = "", Template = "";
        public bool Conquer;
    }
    public sealed class Heroine
    {
        /// <summary>The 爱人 value, also her order and her key.</summary>
        public int Lover;
        /// <summary>RelationshipStatType name (the value's field is relation:Name) and its script key (Character("girl4", …)).</summary>
        public string Relation = "", Key = "", Fallback = "";
        public Step[] Steps = new Step[0];
        public string Conquer => Steps.Last().Flag;
    }

    private static Step Met(string flag) => new() { Flag = flag, Template = "认识{0}" };
    private static Step Confess(string flag) => new() { Flag = flag, Template = "向{0}告白" };
    private static Step Win(string flag) => new() { Flag = flag, Template = "攻略{0}", Conquer = true };
    private static Heroine H(int lover, string relation, string key, string fallback, params Step[] steps)
        => new() { Lover = lover, Relation = relation, Key = key, Fallback = fallback, Steps = steps };

    public static readonly Heroine[] All =
    {
        H(0, "小師妹", "sister1", "小师妹", Win("G11000")),
        H(1, "瑞杏", "girl1", "瑞杏", Win("G11008")),
        H(2, "葉雲裳", "girl2", "叶云裳", Met("G10002"), new Step { Flag = "M50024", Template = "{0}结缘前置" }, Win("G11002")),
        H(3, "虞小梅", "girl5", "虞小梅", Met("G10004"), Confess("G00005"), Win("G11004")),
        H(4, "上官螢", "girl9", "上官萤", Met("G10001"), Win("G11001")),
        H(5, "夏侯蘭", "girl6", "夏侯兰", Met("G10005"), new Step { Flag = "S0014_02", Template = "拜{0}为师" }, Win("G11005")),
        H(6, "郁竹", "girl7", "郁竹", Met("G10006"), Confess("G00006"), Win("G11006")),
        H(7, "魏菊", "girl8", "魏菊", Met("G10007"), Win("G11007")),
        H(8, "龍湘", "girl4", "龙湘", Met("G10003"), Confess("G00004"), Win("G11003")),
    };
    public static Heroine? Find(int lover) => All.FirstOrDefault(h => h.Lover == lover);
}

/// <summary>
/// One heroine's way on the route map (F117): every station, side story or free-play category holding a script that
/// changes her affection or sets one of her flags, with the sum it adds and the steps it marks, in the story's order.
/// Plain C#: built from <see cref="RouteMap"/> alone.
/// </summary>
public sealed class RomanceRoute
{
    public sealed class Stop
    {
        /// <summary>stn:ID, arc:ID or cat:prefix, as <see cref="RouteMap.Owner"/> names it.</summary>
        public string Owner = "";
        public int Affection, Scripts;
        public int[]? Date;
        /// <summary>The steps it sets: flag with the value set, in the order of <see cref="RomanceDefs.Heroine.Steps"/>.</summary>
        public List<(string Flag, int Value)> Steps = new();
        public bool Category => Owner.StartsWith("cat:", StringComparison.Ordinal);
    }

    public RomanceDefs.Heroine Heroine { get; }
    /// <summary>Drawn stops first, by date (undated after), then the free-play categories by the affection they give.</summary>
    public List<Stop> Stops { get; } = new();
    /// <summary>Where each step's flag is set to a value of 1 or more: the stops, in the story's order.</summary>
    public Dictionary<string, List<Stop>> SetBy { get; } = new(StringComparer.Ordinal);

    public RomanceRoute(RouteMap map, RomanceDefs.Heroine heroine)
    {
        Heroine = heroine;
        var flags = new HashSet<string>(heroine.Steps.Select(s => s.Flag), StringComparer.Ordinal);
        var stops = new Dictionary<string, Stop>(StringComparer.Ordinal);
        Stop Get(string owner) => stops.TryGetValue(owner, out var s) ? s : stops[owner] = new Stop { Owner = owner, Date = DateOf(map, owner) };
        foreach (var pair in map.Owner)
        {
            string script = pair.Key, owner = pair.Value;
            bool touched = false;
            Stop? stop = null;
            if (map.Affection.TryGetValue(script, out var affection) && affection.TryGetValue(heroine.Key, out int delta) && delta != 0)
            {
                stop = Get(owner); stop.Affection += delta; touched = true;
            }
            if (map.FlagValues.TryGetValue(script, out var values))
                foreach (var (flag, value) in values)
                {
                    if (!flags.Contains(flag)) continue;
                    stop ??= Get(owner); touched = true;
                    if (!stop.Steps.Contains((flag, value))) stop.Steps.Add((flag, value));
                }
            if (touched) stop!.Scripts++;
        }
        var order = heroine.Steps.Select(s => s.Flag).ToList();
        foreach (var stop in stops.Values) stop.Steps.Sort((a, b) => order.IndexOf(a.Flag) != order.IndexOf(b.Flag) ? order.IndexOf(a.Flag).CompareTo(order.IndexOf(b.Flag)) : a.Value.CompareTo(b.Value));
        Stops.AddRange(stops.Values.Where(s => !s.Category).OrderBy(s => s.Date == null ? int.MaxValue : RouteMapBuilder.Rounds(s.Date)).ThenBy(s => s.Owner, StringComparer.Ordinal));
        Stops.AddRange(stops.Values.Where(s => s.Category).OrderByDescending(s => s.Affection).ThenBy(s => s.Owner, StringComparer.Ordinal));
        foreach (var step in heroine.Steps)
            SetBy[step.Flag] = Stops.Where(s => s.Steps.Any(x => x.Flag == step.Flag && x.Value >= 1)).ToList();
    }

    public Stop? StopAt(string owner) => Stops.FirstOrDefault(s => s.Owner == owner);

    private static int[]? DateOf(RouteMap map, string owner)
    {
        if (owner.StartsWith("stn:", StringComparison.Ordinal)) return map.Stations.FirstOrDefault(s => s.Id == owner.Substring(4)) is { } s && s.Date != null && s.Date.Length == 3 && s.Date[0] > 0 ? s.Date : null;
        if (owner.StartsWith("arc:", StringComparison.Ordinal)) return map.Arcs.FirstOrDefault(a => a.Id == owner.Substring(4))?.Lo;
        return null;
    }
}
