using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace ZhaoHuoSystem;

// 结缘路线图 (F117) on the route map: a row of the nine heroines above the map. With one chosen, the stations and side
// stories that change her affection or set one of her flags stand out (the rest dim) with the steps they mark, and the
// side lists her way: affection and 爱人 now, each step (认识 → 告白 / 前置 → 攻略) done or not, where it is set and what
// that side story asks now (red / green), then every story touching her in the story's order.
public sealed partial class ToolWindow
{
    private int _romance = -1;
    private RomanceRoute? _romanceRoute;
    private RouteMap? _romanceMap;
    private bool _romanceFocusPending;
    private Dictionary<string, ValueSnapshot>? _liveValues;
    private List<ValueSnapshot>? _liveSource;
    private static readonly Color RomanceColor = new(.96f, .56f, .72f, 1);
    // The 爱人 stat's value field, named as the adapter names stats (stat:<GameStatType>).
    private static readonly string LoverField = "stat:" + nameof(Mortal.Core.GameStatType.愛人);
    /// <summary>A side story's missions with their trigger conditions read now (the game side).</summary>
    internal Func<string, List<(string Mission, ConditionSnapshot Condition)>>? SeriesConditions;

    private RomanceRoute? RomanceOf(RouteMap map)
    {
        if (_romance < 0 || RomanceDefs.Find(_romance) is not { } heroine) { _romanceRoute = null; return null; }
        if (_romanceRoute == null || _romanceMap != map || _romanceRoute.Heroine != heroine) { _romanceRoute = new RomanceRoute(map, heroine); _romanceMap = map; }
        return _romanceRoute;
    }
    private static string HeroineName(RomanceDefs.Heroine h) => L.GameText("PlayerInfo/Lover/" + h.Lover.ToString(CultureInfo.InvariantCulture), h.Fallback);
    private static string StepText(RomanceDefs.Heroine h, string flag) => h.Steps.FirstOrDefault(s => s.Flag == flag) is { } s ? L.Format(s.Template, HeroineName(h)) : flag;
    private static string StepShort(RomanceDefs.Heroine h, string flag) => (h.Steps.FirstOrDefault(s => s.Flag == flag)?.Template ?? "") switch
    {
        "认识{0}" => L.Text("认识"), "向{0}告白" => L.Text("告白"), "{0}结缘前置" => L.Text("前置"), "拜{0}为师" => L.Text("拜师"), "攻略{0}" => L.Text("攻略"), _ => flag
    };
    private static string Signed(int value) => value > 0 ? "+" + value.ToString(CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A value as the panel reads it now (flag:…, relation:…, stat:…); null off a game.</summary>
    private ValueSnapshot? Live(string id)
    {
        if (!_snapshot.Ready) return null;
        if (!ReferenceEquals(_liveSource, _snapshot.Values))
        {
            _liveSource = _snapshot.Values;
            _liveValues = new Dictionary<string, ValueSnapshot>(StringComparer.Ordinal);
            foreach (var value in _snapshot.Values) if (!_liveValues.ContainsKey(value.Id)) _liveValues[value.Id] = value;
        }
        return _liveValues!.TryGetValue(id, out var found) ? found : null;
    }

    private void ChooseHeroine(int lover)
    {
        _romance = lover; _romanceRoute = null;
        _routeSelected = ""; _routeSearch = ""; _routeOthers = false; _routeTargetKey = ""; _routeSideKey = ""; _routeSideScroll = Vector2.zero;
        // Pills carry her steps, so the stories are laid out again; the map then moves to her first step.
        _routeLayoutMap = null; _romanceFocusPending = lover >= 0;
    }

    /// <summary>The heroines' row under the forks; takes its height from <paramref name="r"/>.</summary>
    private void RomanceRow(ref Rect r)
    {
        float label = _small.CalcSize(new GUIContent(L.Text("结缘路线"))).x + S(10), x = r.x + label;
        Label(new Rect(r.x, r.y + S(2), label, S(26)), "结缘路线", _small, L.Text("选一位，地图上标出和她有关的剧情，右边列出结缘进度；再点一次取消"));
        float top = r.yMax;
        foreach (var heroine in RomanceDefs.All)
        {
            string name = HeroineName(heroine);
            float width = ButtonWidth(name, 52);
            // Longer names (English, F119) go on to another line.
            if (x + width > r.xMax && x > r.x + label) { x = r.x + label; r.y += S(35); }
            bool chosen = _romance == heroine.Lover;
            if (Button(new Rect(x, r.y, width, S(29)), name, "route.romance." + heroine.Lover, chosen ? _tabSelected : _button,
                chosen ? L.Text("再点一次，回到完整的路线图") : L.Format("在地图上标出和{0}有关的剧情，右边列出结缘进度", name)))
                ChooseHeroine(chosen ? -1 : heroine.Lover);
            x += width + S(6);
        }
        r.y += S(37); r.height = top - r.y;
    }

    /// <summary>After a heroine is chosen and the map laid out: ease to her first step (or her first story).</summary>
    private void RomanceFocus()
    {
        if (!_romanceFocusPending || _romanceRoute == null) return;
        _romanceFocusPending = false;
        var first = _romanceRoute.Stops.FirstOrDefault(s => !s.Category && s.Steps.Count > 0) ?? _romanceRoute.Stops.FirstOrDefault(s => !s.Category);
        if (first != null) FocusRoute(first.Owner, true);
    }

    private bool RomanceLit(string owner) => _romanceRoute == null || _romanceRoute.StopAt(owner) != null;
    /// <summary>The steps a station or story marks, e.g. 「认识·告白」; "" when none or no heroine is chosen.</summary>
    private string RomanceSteps(string owner)
        => _romanceRoute?.StopAt(owner) is { } stop && stop.Steps.Count > 0 ? string.Join("·", stop.Steps.Select(s => StepShort(_romanceRoute.Heroine, s.Flag)).Distinct()) : "";
    private string PillText(RouteMap.Arc arc)
    {
        string steps = RomanceSteps("arc:" + arc.Id);
        return steps.Length > 0 ? "【" + steps + "】" + L.DisplayText(arc.Label) : L.DisplayText(arc.Label);
    }
    private string RomanceHover(string owner)
    {
        if (_romanceRoute?.StopAt(owner) is not { } stop) return "";
        var parts = new List<string>();
        if (stop.Affection != 0) parts.Add(L.Format("好感 {0}", Signed(stop.Affection)));
        parts.AddRange(stop.Steps.Select(s => StepText(_romanceRoute.Heroine, s.Flag) + " = " + s.Value.ToString(CultureInfo.InvariantCulture)));
        return "\n" + HeroineName(_romanceRoute.Heroine) + "：" + string.Join("，", parts);
    }

    /// <summary>What the side shows depends on the values now: they go into its key.</summary>
    private string RomanceSideKey()
    {
        if (_romanceRoute == null) return "";
        var h = _romanceRoute.Heroine;
        return string.Join(",", new[] { "relation:" + h.Relation, LoverField }.Concat(h.Steps.Select(s => "flag:" + s.Flag)).Select(id => Live(id)?.Value.ToString(CultureInfo.InvariantCulture) ?? "-"));
    }

    private void SideAdd(SideKind kind, string text, string tooltip = "", float indent = 0, bool warn = false, bool good = false)
        => _routeSide.Add(new SideLine { Kind = kind, Text = text, Tooltip = tooltip, Indent = indent, Warn = warn, Good = good });
    private void SideRow(string marker, string text, string right, Action? click, string tooltip = "", float indent = 0, bool link = false)
        => _routeSide.Add(new SideLine { Kind = SideKind.Row, Marker = marker, Text = text, Right = right, Click = click, Tooltip = tooltip, Indent = indent, Link = link });

    /// <summary>A condition read now, a line per comparison: green holds, red fails; 「满足其一」 over an OR.</summary>
    private void SideConditions(ConditionSnapshot condition, float indent, int depth = 0)
    {
        if (depth > 6) return;
        if (condition.Children.Count == 1) { SideConditions(condition.Children[0], indent, depth + 1); return; }
        if (condition.Children.Count == 0)
        {
            string text = Tone.Plain(condition.Text);
            if (text.Length > 0) SideAdd(SideKind.Sub, text, condition.Detail, indent, condition.Known && !condition.Satisfied, condition.Known && condition.Satisfied);
            return;
        }
        if (condition.Operator == "OR")
        {
            SideAdd(SideKind.Sub, L.Text("满足其一："), "", indent, false, condition.Known && condition.Satisfied);
            foreach (var child in condition.Children) SideConditions(child, indent + S(14), depth + 1);
            return;
        }
        foreach (var child in condition.Children) SideConditions(child, indent, depth + 1);
    }

    /// <summary>A side story's trigger conditions now, under a line naming them.</summary>
    private void SideSeries(string series, float indent, string title)
    {
        var missions = SeriesConditions?.Invoke(series) ?? new List<(string, ConditionSnapshot)>();
        if (missions.Count == 0) return;
        SideAdd(SideKind.Sub, title, "", indent);
        foreach (var (mission, condition) in missions.Take(3))
        {
            if (missions.Count > 1) SideAdd(SideKind.Sub, mission, "", indent + S(8));
            SideConditions(condition, indent + S(14));
        }
    }

    /// <summary>On a chosen station or story: the way back to her page, and what this one does for her.</summary>
    private void RomanceHeader(RouteMap map, RouteLayout layout, string owner)
    {
        if (_romanceRoute == null) return;
        var h = _romanceRoute.Heroine;
        string name = HeroineName(h);
        SideRow("‹", L.Format("返回{0}的结缘路线", name), "", () => { _routeSelected = ""; _routeTargetKey = ""; _routeSideKey = ""; _routeSideScroll = Vector2.zero; }, "", 0, true);
        if (_romanceRoute.StopAt(owner) is not { } stop) { SideAdd(SideKind.Meta, L.Format("这一项不改{0}的好感，也不设她的结缘标记。", name)); SideAdd(SideKind.Gap, ""); return; }
        var parts = new List<string>();
        if (stop.Affection != 0) parts.Add(L.Format("好感 {0}（{1} 个剧本）", Signed(stop.Affection), stop.Scripts));
        foreach (var (flag, value) in stop.Steps) parts.Add(L.Format("设置「{0}」= {1}", StepText(h, flag), value));
        SideAdd(SideKind.Text, L.Format("和{0}有关：{1}", name, string.Join("；", parts)), "", 0, false, stop.Steps.Count > 0);
        if (owner.StartsWith("arc:", StringComparison.Ordinal)) SideSeries(owner.Substring(4), S(14), L.Text("触发条件（这一局的值）："));
        SideAdd(SideKind.Gap, "");
    }

    /// <summary>Her page: affection and 爱人 now, the steps with where each is set and what that asks now, then her stories.</summary>
    private void RomancePage(RouteMap map, RouteLayout layout)
    {
        var route = _romanceRoute!;
        var h = route.Heroine;
        string name = HeroineName(h);
        SideAdd(SideKind.Heading, L.Format("{0} · 结缘路线", name));
        var meta = new List<string>();
        if (Live("relation:" + h.Relation) is { } affection) meta.Add(L.Format("好感 {0}", affection.Value));
        if (Live(LoverField) is { } lover) meta.Add(lover.Value == h.Lover ? L.Format("这一局的爱人是{0}", name) : L.Format("这一局的爱人：{0}", L.EnumValueName("PlayerStat/lover", lover.Value)));
        if (meta.Count == 0) meta.Add(L.Text("进入存档后显示这一局的好感和进度"));
        SideAdd(SideKind.Meta, string.Join(" · ", meta));
        SideAdd(SideKind.Text, L.Format("地图上亮着的是改{0}好感或设她结缘标记的剧情，其余变暗；带【】的是关键一步。点一项看它的条件，能跳转的可以直接进入。", name));

        SideAdd(SideKind.Caption, L.Text("结缘进度"));
        foreach (var step in h.Steps)
        {
            var value = Live("flag:" + step.Flag);
            bool done = value != null && value.Value >= 1;
            var setBy = route.SetBy.TryGetValue(step.Flag, out var stops) ? stops : new List<RomanceRoute.Stop>();
            var first = setBy.FirstOrDefault(s => !s.Category) ?? setBy.FirstOrDefault();
            string text = StepText(h, step.Flag) + (value != null && value.Value > 1 ? " = " + value.Value.ToString(CultureInfo.InvariantCulture) : "");
            string where = string.Join("\n", setBy.Select(s => OwnerName(map, layout, s.Owner))) + "\n" + step.Flag;
            SideRow(value == null ? "·" : done ? "✓" : "○", text, first != null ? OwnerName(map, layout, first.Owner) : "",
                first != null && !first.Category ? () => SelectRoute(first.Owner) : null, L.Text("在哪里设置：") + "\n" + where, 0, first != null && !first.Category);
            if (setBy.Count == 0) SideAdd(SideKind.Sub, L.Text("剧本里没有直接设置它的地方：可能由任务、结局或游戏系统设置。"), "", S(28));
            foreach (var stop in setBy.Where(s => s.Owner.StartsWith("arc:", StringComparison.Ordinal)).Take(2))
                SideSeries(stop.Owner.Substring(4), S(28), L.Format("进入「{0}」的条件：", OwnerName(map, layout, stop.Owner)));
            foreach (var stop in setBy.Skip(1).Where(s => !s.Category).Take(3))
                _routeSide.Add(new SideLine { Kind = SideKind.Row, Marker = "›", Text = L.Format("也可能在：{0}", OwnerName(map, layout, stop.Owner)), Click = () => SelectRoute(stop.Owner), Indent = S(28), Link = true, Tooltip = L.Text("在地图上移过去并标出") });
            if (value != null && !done && value.Writable)
            {
                string flag = step.Flag;
                SideRow("✎", L.Text("直接设为 1"), "", () => { Execute(() => _adapter.SetValue("flag:" + flag, "1")); _routeSideKey = ""; },
                    L.Text("不经过剧情，只改这个标记；之后读它的剧情按新值判定。也可以在「数值」页改。"), S(28));
            }
        }

        var drawn = route.Stops.Where(s => !s.Category).ToList();
        SideAdd(SideKind.Caption, L.Format("相关剧情（{0}）", drawn.Count), L.Text("按时间排列；点一项在地图上移过去"));
        foreach (var stop in drawn)
        {
            var parts = new List<string>();
            if (stop.Affection != 0) parts.Add(L.Format("好感 {0}", Signed(stop.Affection)));
            if (stop.Steps.Count > 0) parts.Add(string.Join("·", stop.Steps.Select(s => StepShort(h, s.Flag)).Distinct()));
            string owner = stop.Owner;
            SideRow("›", (_routeJumpable.Contains(owner) ? "▶ " : "") + OwnerName(map, layout, owner), string.Join(" · ", parts), () => SelectRoute(owner),
                RouteDate(stop.Date) + "\n" + L.Text("在地图上移过去并标出"), 0, true);
        }
        var places = route.Stops.Where(s => s.Category && s.Affection != 0).ToList();
        if (places.Count > 0)
            SideAdd(SideKind.Text, L.Format("自由行动里也能加好感：{0}", string.Join("、", places.Select(s => OwnerName(map, layout, s.Owner) + " " + Signed(s.Affection)))),
                L.Text("地点闲聊、出行等，不在时间轴上；在「其他内容」里查看"));
    }
}
