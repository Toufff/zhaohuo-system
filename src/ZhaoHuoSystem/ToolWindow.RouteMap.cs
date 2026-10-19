using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace ZhaoHuoSystem;

// 剧情路线图 (F89) on the review page: the main line as stations on lanes along the game's calendar, the side routes
// beside it, side stories packed in rows by how they are entered, and what a station or story holds on the right.
public sealed partial class ToolWindow
{
    internal RouteMapRunner? Routes;
    private bool _routeMode, _routeOthers;
    private string _routeSelected = "", _routeSearch = "", _routeSearched = "\u0001", _routeSideKey = "";
    private Vector2 _routeScroll, _routeSideScroll;
    private RouteLayout? _routeLayout;
    private RouteMap? _routeLayoutMap;
    private float _routeLayoutScale;
    private double _routeDrawMs;
    private readonly List<SideLine> _routeSide = new();
    private readonly List<(string Owner, string Script, string Snippet)> _routeHits = new();
    private Dictionary<string, string>? _routeDialogue;
    private GUIStyle _routeLabel = null!, _routeTag = null!, _routePill = null!, _routeItem = null!, _routeItemSelected = null!;
    private GUIStyle _routeYear = null!, _routeMonth = null!, _routeGroup = null!;
    // The map's own zoom on top of 界面缩放, and the side column's share of the page once dragged (F92); this session only.
    private float _routeZoom = 1, _routeSideShare;
    private Vector2 _routeView;
    private const float RouteZoomMin = .5f, RouteZoomMax = 1.5f;
    private float Z(float value) => S(value) * _routeZoom;

    private sealed class RouteLayout
    {
        public float Width, Height, MainBottom;
        public readonly List<float> X = new();
        public int MaxSlot;
        public readonly Dictionary<string, RouteMap.Station> Stations = new(StringComparer.Ordinal);
        public readonly Dictionary<string, (float X, float Y, bool Up, string Vis)> Points = new(StringComparer.Ordinal);
        public readonly List<(RouteMap.Arc Arc, float X1, float X2, float W, float Y, string Group)> Arcs = new();
        public readonly List<(string Title, float Y)> Groups = new();
        public readonly Dictionary<string, RouteMap.Arc> ArcById = new(StringComparer.Ordinal);
    }
    private sealed class SideLine
    {
        public string Text = "", Tooltip = "", Button = "";
        public GUIStyle Style = null!;
        public float Indent;
        public Action? Click, Press;
    }

    private const float RouteSlot = 22, RouteGap = 70, RouteLane = 64, RouteAxis = 58, RouteRow = 24, RouteGroupHead = 30;
    private static readonly int[] RouteLanes = { -2, -1, 0, 1, 2, 3 };
    private static readonly Color RouteUnread = new(.91f, .70f, .29f), RouteRead = new(.42f, .47f, .45f), RouteEdge = new(.62f, .66f, .70f, .55f),
        RouteRun = new(.36f, .62f, .84f, .95f), RouteBand = new(1, 1, 1, .035f), RouteGrid = new(1, 1, 1, .06f);

    private static int RouteSlotOf(int[]? d) => d == null || d[0] <= 0 ? 0 : Math.Max(0, (d[0] - 1) * 36 + (d[1] - 4) * 3 + (d[2] - 1));
    private static Color RouteColor(int? read) => read == null ? new Color(.5f, .52f, .55f) : Color.Lerp(RouteUnread, RouteRead, read.Value / 100f);
    private static string RouteDate(int[]? d) => d == null || d[0] <= 0 ? L.Text("时间不明") : L.Format("第{0}年{1}月{2}旬", d[0], d[1], L.Text(d[2] switch { 1 => "上", 2 => "中", _ => "下" }));
    private static string RouteSpan(int[]? a, int[]? b) => a == null ? L.Text("时间不明") : b == null || RouteSlotOf(a) == RouteSlotOf(b) ? RouteDate(a) : L.Format("{0} 至 {1}", RouteDate(a), RouteDate(b));

    private void DrawRouteMap(Rect r)
    {
        if (Routes == null) { Empty(r, "剧情回顾未载入"); return; }
        if (Routes.Map == null && !Routes.Building && Routes.Error.Length == 0) Routes.Start();
        var map = Routes.Map;
        // Header: where the build is, and what the map holds.
        string status = Routes.Building ? L.Format("正在生成路线图……（第 {0} 帧）", Routes.Frames)
            : Routes.Error.Length > 0 ? L.Format("生成失败：{0}", Routes.Error)
            : map == null ? "" : L.Format("主线 {0} 站 · 支线 {1} 段 · 对白已读 {2}% · 生成 {3:0.0} 秒 · 绘制 {4:0.0} 毫秒", map.Stations.Count, map.Arcs.Count,
                map.LinesAll == 0 ? 0 : Mathf.RoundToInt(100f * map.ReadAll / map.LinesAll), Routes.WallMs / 1000, _routeDrawMs);
        float rebuild = ButtonWidth("重新生成", 95), others = ButtonWidth("图外内容", 95), next = ButtonWidth("跳到下一站", 95);
        bool previous = GUI.enabled;
        GUI.enabled = previous && !Routes.Building;
        if (Button(new Rect(r.xMax - rebuild, r.y, rebuild, S(29)), "重新生成", "route.rebuild")) { Routes.Start(); _routeLayoutMap = null; _routeSideKey = ""; _routeDialogue = null; }
        GUI.enabled = previous && map != null;
        if (Button(new Rect(r.xMax - rebuild - others - S(6), r.y, others, S(29)), "图外内容", "route.others", _routeOthers ? _tabSelected : _button)) { _routeOthers = !_routeOthers; _routeSideKey = ""; }
        GUI.enabled = previous && map != null && map.NextStation.Length > 0;
        if (Button(new Rect(r.xMax - rebuild - others - next - S(12), r.y, next, S(29)), "跳到下一站", "route.next")) { _routeSelected = "stn:" + map!.NextStation; _routeOthers = false; _routeSideKey = ""; ScrollRouteTo(_routeSelected); }
        GUI.enabled = previous;
        Label(new Rect(r.x, r.y, r.width - rebuild - others - next - S(24), S(29)), status, _small, status);
        r.y += S(36); r.height -= S(36);
        if (map == null) { Empty(r, Routes.Building ? "第一次打开要读一遍全部剧本，稍等片刻。" : "路线图还没生成"); return; }
        // Second row: the stations where the ways part one click away (the prototype's top bar), the zoom on the right.
        float step = ButtonWidth("−", 36), percent = ButtonWidth("150%", 58), zoomX = r.xMax - step * 2 - percent - S(8);
        if (Button(new Rect(zoomX, r.y, step, S(29)), "−", "route.zoom.out", tooltip: L.Text("缩小"))) SetRouteZoom(_routeZoom - .1f, _routeView / 2);
        if (Button(new Rect(zoomX + step + S(4), r.y, percent, S(29)), L.Format("{0}%", Mathf.RoundToInt(_routeZoom * 100)), "route.zoom.reset", tooltip: L.Text("恢复 100%"))) SetRouteZoom(1, _routeView / 2);
        if (Button(new Rect(zoomX + step + percent + S(8), r.y, step, S(29)), "+", "route.zoom.in", tooltip: L.Text("放大"))) SetRouteZoom(_routeZoom + .1f, _routeView / 2);
        float forkX = r.x;
        var forks = RouteMapDefs.Forks.Select(f => (f.Name, Station: map.Stations.Find(s => s.Id == f.Station))).Where(f => f.Station != null)
            .Select(f => (Text: L.DisplayText(f.Name), When: f.Station!.Date != null ? L.Format("{0}年{1}月", f.Station.Date[0], f.Station.Date[1]) : "", f.Station)).ToList();
        // The date goes on the buttons when the row has room for all of them with it, as on the prototype; else on the hover.
        bool dated = forks.Sum(f => ButtonWidth(f.Text + "  " + f.When, 60) + S(6)) <= zoomX - S(10) - forkX;
        foreach (var (name, when, fork) in forks)
        {
            string id = fork.Id, text = dated ? name + "  " + when : name, key = "stn:" + id;
            float width = ButtonWidth(text, 60);
            if (forkX + width > zoomX - S(10)) break;
            if (Button(new Rect(forkX, r.y, width, S(29)), text, "route.fork." + id, _routeSelected == key ? _tabSelected : _button, when + "  " + L.DisplayText(fork.Label)))
            { _routeSelected = key; _routeOthers = false; _routeSearch = ""; _routeSideKey = ""; ScrollRouteTo(key); }
            forkX += width + S(6);
        }
        r.y += S(36); r.height -= S(36);
        if (_routeLayoutMap != map || Mathf.Abs(_routeLayoutScale - _scale) > .001f) { _routeLayout = Layout(map); _routeLayoutMap = map; _routeLayoutScale = _scale; }
        var layout = _routeLayout!;
        float sideWidth = Mathf.Clamp(_routeSideShare > 0 ? _routeSideShare * r.width : Mathf.Min(S(360), r.width * .42f), Mathf.Min(S(240), r.width * .3f), r.width * .6f);
        var canvas = new Rect(r.x, r.y, r.width - sideWidth - S(10), r.height);
        var side = new Rect(canvas.xMax + S(10), r.y, sideWidth, r.height);
        // Before the map: its control id must not move with the stations the map draws.
        RouteSplitter(new Rect(canvas.xMax, r.y, S(10), r.height), r);
        var clock = Stopwatch.StartNew();
        DrawRouteCanvas(canvas, map, layout);
        DrawRouteSide(side, map, layout);
        if (Event.current.type == EventType.Repaint) _routeDrawMs = _routeDrawMs <= 0 ? clock.Elapsed.TotalMilliseconds : _routeDrawMs * .9 + clock.Elapsed.TotalMilliseconds * .1;
    }

    /// <summary>The gap between the map and the side column: drag it to give either more room.</summary>
    private void RouteSplitter(Rect gap, Rect page)
    {
        int id = GUIUtility.GetControlID(FocusType.Passive);
        var current = Event.current;
        bool dragging = GUIUtility.hotControl == id;
        if (current.type == EventType.Repaint)
            Fill(new Rect(gap.center.x, gap.y + gap.height / 2 - S(24), 1, S(48)), dragging || gap.Contains(current.mousePosition) ? Color.white : new Color(1, 1, 1, .3f));
        Hover(gap, L.Text("拖动调整地图和说明栏的宽度"));
        switch (current.GetTypeForControl(id))
        {
            case EventType.MouseDown when current.button == 0 && gap.Contains(current.mousePosition): GUIUtility.hotControl = id; current.Use(); break;
            case EventType.MouseDrag when dragging: _routeSideShare = Mathf.Clamp01((page.xMax - current.mousePosition.x - S(5)) / page.width); current.Use(); break;
            case EventType.MouseUp when dragging: GUIUtility.hotControl = 0; current.Use(); break;
        }
    }

    /// <summary>The point under the pointer (or the middle of the map) stays where it is.</summary>
    private void SetRouteZoom(float value, Vector2 pivot)
    {
        float next = Mathf.Clamp(Mathf.Round(value * 10) / 10, RouteZoomMin, RouteZoomMax);
        if (Mathf.Abs(next - _routeZoom) < .001f) return;
        _routeScroll = (_routeScroll + pivot) * (next / _routeZoom) - pivot;
        _routeZoom = next; RouteStyles(); _routeLayoutMap = null;
    }

    /// <summary>The map's texts follow its zoom; the side column keeps the panel's sizes.</summary>
    private void RouteStyles()
    {
        Color text = new(.94f, .95f, .96f, 1), muted = new(.73f, .76f, .79f, 1);
        GUIStyle Canvas(int size, Color color, TextAnchor alignment)
        {
            var style = TextStyle(size, color);
            style.fontSize = Mathf.Max(6, Mathf.RoundToInt(S(size) * _routeZoom)); style.alignment = alignment;
            return style;
        }
        _routeLabel = Canvas(13, text, TextAnchor.MiddleCenter);
        _routeTag = Canvas(11, new Color(.95f, .80f, .36f, 1), TextAnchor.MiddleCenter);
        _routePill = Canvas(12, new Color(.07f, .08f, .09f, 1), TextAnchor.MiddleCenter);
        _routeYear = Canvas(16, text, TextAnchor.MiddleLeft); _routeMonth = Canvas(13, muted, TextAnchor.MiddleLeft); _routeGroup = Canvas(15, muted, TextAnchor.MiddleLeft);
    }

    private RouteLayout Layout(RouteMap map)
    {
        var layout = new RouteLayout();
        foreach (var s in map.Stations) layout.Stations[s.Id] = s;
        foreach (var a in map.Arcs) layout.ArcById[a.Id] = a;
        var slot = map.Stations.ToDictionary(s => s.Id, s => RouteSlotOf(s.Date), StringComparer.Ordinal);
        var rank = map.Stations.ToDictionary(s => s.Id, _ => 0, StringComparer.Ordinal);
        var preds = map.Stations.ToDictionary(s => s.Id, _ => new List<string>(), StringComparer.Ordinal);
        var succs = map.Stations.ToDictionary(s => s.Id, _ => new List<string>(), StringComparer.Ordinal);
        var edges = map.Edges.Where(e => slot.ContainsKey(e.From) && slot.ContainsKey(e.To)).ToList();
        foreach (var e in edges) { succs[e.From].Add(e.To); preds[e.To].Add(e.From); }
        // Stations on the same 旬 stand side by side in the order the story reaches them.
        for (int pass = 0; pass < 8; pass++) foreach (var e in edges) if (slot[e.From] == slot[e.To] && rank[e.To] < rank[e.From] + 1) rank[e.To] = rank[e.From] + 1;
        int maxSlot = Math.Max(map.Stations.Max(s => slot[s.Id]), map.Arcs.Where(a => a.Hi != null).Select(a => RouteSlotOf(a.Hi)).DefaultIfEmpty(0).Max()) + 3;
        var occupied = new Dictionary<int, int>();
        foreach (var s in map.Stations) occupied[slot[s.Id]] = Math.Max(occupied.TryGetValue(slot[s.Id], out int o) ? o : -1, rank[s.Id]);
        layout.X.Add(Z(24));
        for (int i = 0; i < maxSlot; i++) layout.X.Add(layout.X[i] + (occupied.TryGetValue(i, out int o) ? (o + 1) * Z(RouteGap) : Z(RouteSlot)));
        layout.MaxSlot = maxSlot;
        // This playthrough: stations with a story record, and those between two such with only one way through.
        var vis = map.Stations.ToDictionary(s => s.Id, s => s.Visited ? "yes" : "no", StringComparer.Ordinal);
        for (bool changed = true; changed;)
        {
            changed = false;
            foreach (var s in map.Stations)
            {
                if (vis[s.Id] != "no" || s.Kind == "route") continue;
                bool On(string id) => vis[id] != "no";
                bool viaPred = preds[s.Id].Any(p => On(p) && succs[p].Count == 1), viaSucc = succs[s.Id].Any(n => On(n) && preds[n].Count == 1);
                bool predOk = preds[s.Id].Count == 0 || preds[s.Id].Any(On), succOk = succs[s.Id].Any(On);
                if (viaPred && succOk || viaSucc && predOk) { vis[s.Id] = "inferred"; changed = true; }
            }
        }
        foreach (int lane in RouteLanes)
        {
            var row = map.Stations.Where(s => s.Lane == lane).Select(s => (s, x: layout.X[slot[s.Id]] + rank[s.Id] * Z(RouteGap) + Z(RouteGap) / 2)).OrderBy(p => p.x).ToList();
            for (int i = 0; i < row.Count; i++)
                layout.Points[row[i].s.Id] = (row[i].x, Z(RouteAxis) + (Array.IndexOf(RouteLanes, lane) + .5f) * Z(RouteLane), row[i].s.Kind == "route" ? lane < 0 : i % 2 == 0, vis[row[i].s.Id]);
        }
        layout.MainBottom = Z(RouteAxis) + RouteLanes.Length * Z(RouteLane);
        // Side stories: by how they are entered; a chained story goes with the story it follows.
        RouteMap.Arc Root(RouteMap.Arc a) { var x = a; for (int n = 0; x.Kind == "chain" && layout.ArcById.TryGetValue(x.Parent, out var p) && n < 8; n++) x = p; return x; }
        float y = layout.MainBottom + Z(50);
        foreach (var (group, title) in new[] { ("fork", "从主线某一站岔出，走完回到原处（含只在序章里出现的事件）"), ("free", "自由行动里触发（地点菜单、随机事件）"), ("date", "按日期和条件自动触发") })
        {
            var items = map.Arcs.Where(a => a.Lo != null && (a.Anchor.Length > 0 ? "fork" : Root(a).Kind == "date" ? "date" : "free") == group).Select(a =>
            {
                float x1 = a.Anchor.Length > 0 && layout.Points.TryGetValue(a.Anchor, out var p) ? p.X - Z(8) : layout.X[Math.Min(RouteSlotOf(a.Lo), maxSlot)] + Z(2);
                float w = _routePill.CalcSize(new GUIContent(L.DisplayText(a.Label))).x + Z(16);
                float x2 = a.Hi != null ? Mathf.Max(layout.X[Math.Min(RouteSlotOf(a.Hi) + 1, maxSlot)] - Z(2), x1) : x1;
                return (a, x1, x2, w);
            }).OrderBy(t => t.x1).ThenByDescending(t => t.a.Lines).ToList();
            if (items.Count == 0) continue;
            layout.Groups.Add((L.Text(title), y));
            y += Z(RouteGroupHead);
            var rows = new List<float>();
            foreach (var (a, x1, x2, w) in items)
            {
                float need = Mathf.Max(x2, x1 + w) + Z(10);
                int row = rows.FindIndex(end => end <= x1);
                if (row < 0) { row = rows.Count; rows.Add(0); }
                rows[row] = need;
                layout.Arcs.Add((a, x1, x2, w, y + row * Z(RouteRow) + Z(RouteRow) / 2, group));
            }
            y += rows.Count * Z(RouteRow) + Z(10);
        }
        layout.Width = layout.X[maxSlot] + Z(80);
        layout.Height = y + Z(20);
        return layout;
    }

    private void DrawRouteCanvas(Rect canvas, RouteMap map, RouteLayout layout)
    {
        GUI.DrawTexture(new Rect(canvas.x, canvas.y - S(4), canvas.width, 1), _line);
        var current = Event.current;
        _routeView = canvas.size;
        // The plain wheel scrolls up and down; Shift + wheel goes along the calendar, Ctrl + wheel zooms about the pointer.
        if (current.type == EventType.ScrollWheel && canvas.Contains(current.mousePosition) && (current.shift || current.control))
        {
            float wheel = current.delta.y != 0 ? current.delta.y : current.delta.x;
            if (current.control) SetRouteZoom(_routeZoom - Mathf.Sign(wheel) * .1f, current.mousePosition - canvas.position);
            else _routeScroll.x += wheel * S(20);
            current.Use();
        }
        _routeScroll = BeginScroll(canvas, _routeScroll, new Rect(0, 0, layout.Width, layout.Height), horizontal: true);
        // Taken before the stations and stories, whose number on screen changes as the map moves.
        int pan = GUIUtility.GetControlID(FocusType.Passive);
        try
        {
            var view = new Rect(_routeScroll.x, _routeScroll.y, canvas.width, canvas.height);
            bool repaint = Event.current.type == EventType.Repaint;
            if (repaint)
            {
                // Calendar: every other year shaded, a line per month.
                for (int year = 1; year <= 4; year++)
                {
                    int s0 = Math.Max(0, RouteSlotOf(new[] { year, 1, 1 })), s1 = Math.Min(layout.MaxSlot, RouteSlotOf(new[] { year + 1, 1, 1 }));
                    if (year == 1) s0 = 0;
                    if (s1 <= 0 || s0 >= layout.MaxSlot) continue;
                    if (year % 2 == 0) Fill(new Rect(layout.X[s0], 0, layout.X[s1] - layout.X[s0], layout.Height), RouteBand);
                    GUI.Label(new Rect(layout.X[s0] + Z(8), Z(6), Z(90), Z(22)), L.Format("第{0}年", year), _routeYear);
                }
                for (int i = 0; i < layout.MaxSlot; i += 3)
                {
                    Fill(new Rect(layout.X[i], Z(30), 1, layout.Height - Z(30)), RouteGrid);
                    GUI.Label(new Rect(layout.X[i] + Z(4), Z(30), Z(46), Z(18)), L.Format("{0}月", (i / 3 + 3) % 12 + 1), _routeMonth);
                }
                foreach (var (title, top) in layout.Groups) GUI.Label(new Rect(view.x + Z(8), top, Z(900), Z(24)), title, _routeGroup);
                // Lines: this playthrough's way in colour, a stretch through free play dashed, a jump in time arched over.
                foreach (var e in map.Edges)
                {
                    if (!layout.Points.TryGetValue(e.From, out var a) || !layout.Points.TryGetValue(e.To, out var b)) continue;
                    // Not this playthrough's way when it went round by a side route that bridges these two stations.
                    bool bridged = map.Stations.Any(rs => rs.Kind == "route" && layout.Points.TryGetValue(rs.Id, out var rp) && rp.Vis != "no"
                        && map.Edges.Any(x => x.From == e.From && x.To == rs.Id) && map.Edges.Any(x => x.From == rs.Id && x.To == e.To));
                    bool run = a.Vis != "no" && b.Vis != "no" && e.Kind != "skip" && !bridged;
                    DrawRouteEdge(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y), e.Kind, run ? RouteRun : RouteEdge);
                }
            }
            foreach (var s in map.Stations)
            {
                if (!layout.Points.TryGetValue(s.Id, out var p)) continue;
                var hit = new Rect(p.X - Z(40), p.Up ? p.Y - Z(28) : p.Y - Z(12), Z(80), Z(44));
                if (!hit.Overlaps(view)) continue;
                string key = "stn:" + s.Id;
                if (repaint)
                {
                    bool selected = _routeSelected == key, next = map.NextStation == s.Id;
                    if (next) Ring(new Vector2(p.X, p.Y), Z(17), Z(3), new Color(.95f, .80f, .36f));
                    if (p.Vis != "no" || selected) Ring(new Vector2(p.X, p.Y), Z(13), Z(2.5f), selected ? Color.white : p.Vis == "yes" ? RouteRun : new Color(RouteRun.r, RouteRun.g, RouteRun.b, .45f));
                    float size = s.Kind == "route" ? Z(16) : Z(15);
                    GUI.DrawTexture(new Rect(p.X - size / 2, p.Y - size / 2, size, size), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, RouteColor(s.Read), 0, s.Kind == "route" ? Z(4) : size / 2);
                    string label = L.DisplayText(s.Label);
                    var labelRect = new Rect(p.X - Z(70), p.Up ? p.Y - Z(30) : p.Y + Z(11), Z(140), Z(20));
                    GUI.Label(labelRect, label, _routeLabel);
                    if (next) GUI.Label(new Rect(p.X - Z(40), p.Up ? p.Y - Z(48) : p.Y + Z(29), Z(80), Z(18)), L.Text("下一站"), _routeTag);
                    if (s.Endings > 0) GUI.Label(new Rect(p.X + Z(9), p.Up ? p.Y + Z(4) : p.Y - Z(18), Z(20), Z(16)), L.Text("终"), _routeTag);
                    if (s.Variants > 0) GUI.Label(new Rect(p.X - Z(46), p.Up ? p.Y + Z(4) : p.Y - Z(18), Z(36), Z(16)), (s.DeadVariants.Count > 0 ? "⚠ " : "") + "+" + s.Variants, _routeTag);
                }
                if (GUI.Button(hit, new GUIContent("", L.DisplayText(s.Label)), GUIStyle.none)) { _routeSelected = key; _routeOthers = false; _routeSearch = ""; _routeSideKey = ""; }
                Hover(hit, L.DisplayText(s.Label) + "\n" + RouteDate(s.Date));
            }
            foreach (var (arc, x1, x2, w, y, _) in layout.Arcs)
            {
                var pill = new Rect(x1, y - Z(9), w, Z(18));
                var span = new Rect(x1, y - Z(1), Mathf.Max(0, x2 - x1), Z(2));
                if (!pill.Overlaps(view) && !span.Overlaps(view)) continue;
                string key = "arc:" + arc.Id;
                if (repaint)
                {
                    if (x2 > x1 + w) Fill(new Rect(x1 + w, y - Z(1), x2 - x1 - w, Z(2)), new Color(RouteColor(arc.Read).r, RouteColor(arc.Read).g, RouteColor(arc.Read).b, .5f));
                    var fill = RouteColor(arc.Read); fill.a = .9f;
                    GUI.DrawTexture(pill, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, fill, 0, Z(9));
                    if (arc.Visited || _routeSelected == key) GUI.DrawTexture(pill, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, _routeSelected == key ? Color.white : RouteRun, Z(2), Z(9));
                    GUI.Label(pill, L.DisplayText(arc.Label), _routePill);
                }
                if (GUI.Button(pill, GUIContent.none, GUIStyle.none)) { _routeSelected = key; _routeOthers = false; _routeSearch = ""; _routeSideKey = ""; }
                Hover(pill, L.DisplayText(arc.Label) + "\n" + RouteSpan(arc.Lo, arc.Hi));
            }
            // Dragging the map moves it; a station or story under the press takes its own click first.
            switch (current.GetTypeForControl(pan))
            {
                case EventType.MouseDown when current.button == 0 && view.Contains(current.mousePosition): GUIUtility.hotControl = pan; current.Use(); break;
                case EventType.MouseDrag when GUIUtility.hotControl == pan: _routeScroll -= current.delta; current.Use(); break;
                case EventType.MouseUp when GUIUtility.hotControl == pan: GUIUtility.hotControl = 0; current.Use(); break;
            }
        }
        finally { EndScroll(); }
    }

    private static void Fill(Rect rect, Color color) => GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color, 0, 0);
    private static void Ring(Vector2 centre, float radius, float width, Color color)
        => GUI.DrawTexture(new Rect(centre.x - radius, centre.y - radius, radius * 2, radius * 2), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color, width, radius);

    /// <summary>Square-cornered lines (no rotated textures): along, across, along; dashed through free play; a jump in time arched over the lane.</summary>
    private void DrawRouteEdge(Vector2 a, Vector2 b, string kind, Color color)
    {
        float w = Z(2.5f);
        void Segment(Vector2 p, Vector2 q)
        {
            var rect = new Rect(Mathf.Min(p.x, q.x) - w / 2, Mathf.Min(p.y, q.y) - w / 2, Mathf.Abs(q.x - p.x) + w, Mathf.Abs(q.y - p.y) + w);
            if (kind == "main") { Fill(rect, color); return; }
            float dash = Z(6), gap = Z(5);
            bool across = Mathf.Abs(q.x - p.x) >= Mathf.Abs(q.y - p.y);
            float length = across ? rect.width : rect.height;
            for (float t = 0; t < length; t += dash + gap)
                Fill(across ? new Rect(rect.x + t, rect.y, Mathf.Min(dash, length - t), rect.height) : new Rect(rect.x, rect.y + t, rect.width, Mathf.Min(dash, length - t)), color);
        }
        if (kind == "skip")
        {
            float top = Mathf.Min(a.y, b.y) - Z(RouteLane) * .7f;
            Segment(a, new Vector2(a.x, top)); Segment(new Vector2(a.x, top), new Vector2(b.x, top)); Segment(new Vector2(b.x, top), b);
            return;
        }
        if (Mathf.Abs(a.y - b.y) < .5f) { Segment(a, b); return; }
        float mid = a.x + Mathf.Max(Z(16), Mathf.Min(Z(48), (b.x - a.x) / 2));
        Segment(a, new Vector2(mid, a.y)); Segment(new Vector2(mid, a.y), new Vector2(mid, b.y)); Segment(new Vector2(mid, b.y), b);
    }

    private void ScrollRouteTo(string key)
    {
        if (_routeLayout == null) return;
        // Into the middle of the map, as the prototype does.
        if (key.StartsWith("stn:", StringComparison.Ordinal) && _routeLayout.Points.TryGetValue(key.Substring(4), out var p))
            _routeScroll = new Vector2(Mathf.Max(0, p.X - _routeView.x / 2), Mathf.Max(0, p.Y - _routeView.y / 2));
        else if (key.StartsWith("arc:", StringComparison.Ordinal))
            foreach (var item in _routeLayout.Arcs) if ("arc:" + item.Arc.Id == key) { _routeScroll = new Vector2(Mathf.Max(0, item.X1 - _routeView.x / 3), Mathf.Max(0, item.Y - _routeView.y / 2)); break; }
    }

    // ---- the right-hand side: search, the selected station or story, or what lies outside the timeline ----
    private void DrawRouteSide(Rect side, RouteMap map, RouteLayout layout)
    {
        _routeSearch = Search(new Rect(side.x, side.y, side.width, S(32)), _routeSearch, "搜人物、地点、事件或台词", "zh.review.route");
        var body = new Rect(side.x, side.y + S(40), side.width, side.height - S(40));
        string key = string.Join("|", _routeSelected, _routeSearch, _routeOthers, map.GetHashCode(), L.Revision, _scale);
        if (key != _routeSideKey) { BuildRouteSide(map, layout, body.width - S(18)); _routeSideKey = key; }
        float total = _routeSide.Sum(line => LineHeight(line, body.width - S(18)));
        _routeSideScroll = BeginScroll(body, _routeSideScroll, new Rect(0, 0, body.width - S(18), Mathf.Max(body.height, total)));
        try
        {
            float y = 0, width = body.width - S(18);
            foreach (var line in _routeSide)
            {
                float h = LineHeight(line, width);
                if (y + h >= _routeSideScroll.y && y <= _routeSideScroll.y + body.height)
                {
                    float right = line.Button.Length > 0 ? ButtonWidth(line.Button, 70) + S(6) : 0;
                    var rect = new Rect(line.Indent, y, width - line.Indent - right, h);
                    if (line.Click != null) { if (GUI.Button(rect, line.Text, line.Style)) { line.Click(); break; } Hover(rect, line.Tooltip); }
                    else Label(rect, line.Text, line.Style, line.Tooltip);
                    if (line.Button.Length > 0 && line.Press != null && Button(new Rect(width - right + S(6), y + S(2), right - S(6), Mathf.Min(h - S(2), S(28))), line.Button, "route.side." + y.ToString(CultureInfo.InvariantCulture)))
                    { line.Press(); break; }
                }
                y += h;
            }
        }
        finally { EndScroll(); }
    }
    private float LineHeight(SideLine line, float width)
    {
        float right = line.Button.Length > 0 ? ButtonWidth(line.Button, 70) + S(6) : 0;
        return Mathf.Max(S(line.Button.Length > 0 ? 30 : 22), line.Style.CalcHeight(new GUIContent(line.Text), Mathf.Max(S(40), width - line.Indent - right)) + S(4));
    }

    private string OwnerName(RouteMap map, RouteLayout layout, string owner)
    {
        if (owner.StartsWith("stn:", StringComparison.Ordinal)) return layout.Stations.TryGetValue(owner.Substring(4), out var s) ? L.DisplayText(s.Label) : owner.Substring(4);
        if (owner.StartsWith("arc:", StringComparison.Ordinal)) return layout.ArcById.TryGetValue(owner.Substring(4), out var a) ? L.DisplayText(a.Label) : owner.Substring(4);
        if (owner.StartsWith("cat:", StringComparison.Ordinal)) return L.DisplayText(RouteMapDefs.CategoryNames.TryGetValue(owner.Substring(4), out var c) ? c : owner.Substring(4));
        return owner == "free" ? L.Text("自由行动") : owner == "ending" ? L.Text("结局") : owner;
    }

    private void BuildRouteSide(RouteMap map, RouteLayout layout, float width)
    {
        _routeSide.Clear();
        void Add(string text, GUIStyle style, float indent = 0, string tooltip = "", Action? click = null, string button = "", Action? press = null)
            => _routeSide.Add(new SideLine { Text = text, Style = style, Indent = indent, Tooltip = tooltip, Click = click, Button = button, Press = press });
        void Select(string owner) { _routeSelected = owner; _routeOthers = false; _routeSearch = ""; _routeSideKey = ""; ScrollRouteTo(owner); }
        string Reading(int? read, int lines) => read == null ? L.Format("对白 {0} 句", lines) : L.Format("对白 {0} 句，读过 {1}%", lines, read.Value);
        var kept = Review?.Latest() ?? new Dictionary<string, StoryReview.Node>();
        void Keep(IEnumerable<string> scripts, string text, GUIStyle style, float indent, string tooltip = "")
        {
            var node = scripts.Select(s => kept.TryGetValue(s, out var n) ? n : null).FirstOrDefault(n => n != null);
            if (node == null) Add(text, style, indent, tooltip);
            else Add(text, style, indent, tooltip, button: "存入", press: () => { if (Execute(() => ReviewExport?.Invoke(node, _reviewSlot) ?? OperationResult.Fail(L.Text("存档系统未就绪"))).Success) _reviewSlotChecked = false; });
        }

        if (_routeSearch.Trim().Length > 0)
        {
            RouteSearch(map, layout, _routeSearch.Trim());
            Add(_routeHits.Count == 0 ? L.Text("没有找到") : L.Format("找到 {0} 处（最多列 60 处）", _routeHits.Count), _muted);
            foreach (var group in _routeHits.GroupBy(h => h.Owner))
            {
                string owner = group.Key;
                Add(OwnerName(map, layout, owner), _routeItem, 0, owner, click: () => Select(owner));
                foreach (var (_, script, snippet) in group.Take(4)) Add(script + "  " + snippet, _small, S(14), snippet);
            }
            return;
        }
        if (_routeOthers)
        {
            int lines = map.Categories.Sum(c => c.Lines);
            Add(L.Text("没画在时间轴上的内容"), _heading);
            Add(L.Format("自由行动里的地点、闲聊、出行和会议等，不按日历推进，没法放进路线图。共 {0} 句对白，约占全部的 {1}%。点开能看到这一类里有名字的随机事件和触发条件。", lines, map.LinesAll == 0 ? 0 : Mathf.RoundToInt(100f * lines / map.LinesAll)), _muted);
            foreach (var c in map.Categories.Where(c => c.Lines > 0))
            {
                string key = "cat:" + c.Id;
                Add(OwnerName(map, layout, key) + "  ·  " + Reading(c.Read, c.Lines) + (c.Visited ? "  ✓" : ""), _routeSelected == key ? _routeItemSelected : _routeItem, 0, string.Join("\n", c.Sample.Select(L.DisplayText)),
                    click: () => { _routeSelected = _routeSelected == key ? "" : key; _routeSideKey = ""; });
                if (_routeSelected != key) continue;
                if (c.Events.Count == 0) Add(L.Text("没有带名字的事件"), _small, S(14));
                foreach (var ev in c.Events)
                    Keep(new[] { ev.Script }, L.DisplayText(ev.Name) + (ev.Lo != null ? "  " + RouteSpan(ev.Lo, ev.Hi) : "") + (ev.Condition.Length > 0 ? "\n" + ev.Condition : "") + "  " + Reading(ev.Read, ev.Lines) + (ev.Visited ? "  ✓" : ""), _small, S(14), ev.Script);
            }
            return;
        }
        if (_routeSelected.StartsWith("stn:", StringComparison.Ordinal) && layout.Stations.TryGetValue(_routeSelected.Substring(4), out var station))
        {
            var p = layout.Points.TryGetValue(station.Id, out var pp) ? pp : default;
            Add(L.DisplayText(station.Label), _heading);
            Add(RouteDate(station.Date) + (station.Route.Length > 0 && station.Kind != "route" ? "  ·  " + L.DisplayText(station.Route) : "") + (map.NextStation == station.Id ? "  ·  " + L.Text("下一站") : ""), _muted);
            Add((p.Vis == "yes" ? L.Text("这一局走过") : p.Vis == "inferred" ? L.Text("这一局应该走过（前后都走过且只有一条路）") : L.Text("这一局没走过")) + "  ·  " + Reading(station.Read, station.Lines)
                + (station.Branch > 0 ? "  ·  " + L.Format("{0} 处分支", station.Branch) : "") + (station.Endings > 0 ? "  ·  " + L.Format("{0} 个结局", station.Endings) : "")
                + (station.Variants > 0 ? "  ·  " + L.Format("{0} 个变体", station.Variants) : ""), _small);
            foreach (string dead in station.DeadVariants) Add("⚠ " + dead, _unmet, 0);
            if (station.Kind == "route")
            {
                Add(L.DisplayText(station.Note), _label);
                foreach (var entry in station.Entry.Where(e => e.Count > 0)) Add(L.Format("进入条件：{0}", string.Join("，", entry)), _small);
                foreach (string exit in station.Exits) Add(L.Format("走完接到：{0}", OwnerName(map, layout, "stn:" + exit)), _routeItem, 0, "", click: () => Select("stn:" + exit));
                foreach (var (st, flags) in station.Influence) Add(L.Format("会影响 {0}：{1}", OwnerName(map, layout, "stn:" + st), string.Join("、", flags.Select(L.DisplayText))), _small);
            }
            foreach (string line in station.Summary.Take(2)) Add(Short(L.DisplayText(line)), _small, 0, L.DisplayText(line));
            Add(L.Text("小节"), _title);
            foreach (var section in station.Sections)
            {
                string mark = section.Trunk ? L.Text("主干") : section.Reach ? L.Text("岔路") : section.ViaMission ? L.Text("按日期") : L.Text("接续不明");
                Keep(section.Scripts, mark + "  " + L.DisplayText(section.Label) + "  ·  " + Reading(section.Read, section.Lines) + (section.Visited ? "  ✓" : ""),
                    section.Trunk ? _label : _muted, 0, string.Join("\n", section.Summary.Select(L.DisplayText).Concat(new[] { string.Join(" ", section.Scripts.Take(12)) })));
                foreach (var condition in section.Conditions) Add(string.Join("，", condition), _small, S(18));
                if (section.Dead.Length > 0) Add("⚠ " + section.Dead, _unmet, S(18));
            }
            var forks = map.Arcs.Where(a => a.Anchor == station.Id).ToList();
            var routes = map.Edges.Where(e => e.From == station.Id && layout.Stations.TryGetValue(e.To, out var rs) && rs.Kind == "route").Select(e => layout.Stations[e.To]).ToList();
            if (forks.Count + routes.Count > 0) Add(station.Id == "P0" ? L.Text("只在序章里出现的事件") : L.Text("从这里岔出去的"), _title);
            foreach (var rs in routes)
            {
                Add(L.DisplayText(rs.Label), _routeItem, 0, L.DisplayText(rs.Note), click: () => Select("stn:" + rs.Id));
                foreach (var entry in rs.Entry.Where(e => e.Count > 0)) Add(L.Format("条件：{0}", string.Join("，", entry)), _small, S(18));
            }
            foreach (var arc in forks)
            {
                Add(L.DisplayText(arc.Label) + "  ·  " + Reading(arc.Read, arc.Lines) + (arc.Visited ? "  ✓" : ""), _routeItem, 0, "", click: () => Select("arc:" + arc.Id));
                foreach (var condition in arc.Entries.Where(e => e.Via == "stn:" + station.Id).SelectMany(e => e.Conditions).Where(c => c.Count > 0).Take(2)) Add(L.Format("条件：{0}", string.Join("，", condition)), _small, S(18));
            }
            return;
        }
        if (_routeSelected.StartsWith("arc:", StringComparison.Ordinal) && layout.ArcById.TryGetValue(_routeSelected.Substring(4), out var story))
        {
            string group = layout.Arcs.FirstOrDefault(x => x.Arc == story).Group ?? "";
            Add(L.DisplayText(story.Label), _heading);
            Add(RouteSpan(story.Lo, story.Hi) + (story.Approx ? L.Text("（约）") : "") + "  ·  " + L.Text(group == "fork" ? "从主线某一站岔出" : group == "date" ? "按日期和条件触发" : "自由行动里触发"), _muted);
            Add((story.Visited ? L.Text("这一局走过") : L.Text("这一局没走过")) + "  ·  " + Reading(story.Read, story.Lines) + (story.Branch > 0 ? "  ·  " + L.Format("{0} 处分支", story.Branch) : "")
                + (story.Endings > 0 ? "  ·  " + L.Format("{0} 个结局", story.Endings) : ""), _small);
            if (story.Dead.Length > 0) Add("⚠ " + story.Dead, _unmet);
            if (story.Desc.Length > 0 && L.DisplayText(story.Desc) != L.DisplayText(story.Label)) Add(L.DisplayText(story.Desc), _small);
            Keep(new[] { story.Start }.Concat(story.Scripts), L.Text("从开头重玩"), _title, 0, story.Start);
            if (story.Anchor.Length > 0) Add(L.Format("挂在：{0}", OwnerName(map, layout, "stn:" + story.Anchor)), _routeItem, 0, "", click: () => Select("stn:" + story.Anchor));
            if (story.Parent.Length > 0) Add(L.Format("接在：{0}", OwnerName(map, layout, "arc:" + story.Parent)), _routeItem, 0, "", click: () => Select("arc:" + story.Parent));
            if (story.Entries.Count > 0) Add(L.Text("怎么进入"), _title);
            foreach (var entry in story.Entries.Take(6))
            {
                string via = entry.Via;
                Add(L.Format("从「{0}」（{1}）", OwnerName(map, layout, via), entry.From), via.StartsWith("cat:", StringComparison.Ordinal) ? _small : _routeItem, 0, "",
                    click: via.StartsWith("cat:", StringComparison.Ordinal) ? null : () => Select(via));
                foreach (var condition in entry.Conditions.Where(c => c.Count > 0)) Add(string.Join("，", condition), _small, S(18));
                if (entry.Dead.Length > 0) Add("⚠ " + entry.Dead, _unmet, S(18));
            }
            foreach (string calendar in story.Calendar) Add(L.Format("按日期：{0}", calendar), _small);
            if (story.Exits.Count > 0)
            {
                Add(L.Text("走完去哪"), _title);
                foreach (string exit in story.Exits)
                    Add(OwnerName(map, layout, exit), exit.StartsWith("stn:", StringComparison.Ordinal) || exit.StartsWith("arc:", StringComparison.Ordinal) ? _routeItem : _small, 0, "",
                        click: exit.StartsWith("stn:", StringComparison.Ordinal) || exit.StartsWith("arc:", StringComparison.Ordinal) ? () => Select(exit) : null);
            }
            if (story.Influence.Count > 0) Add(L.Text("会影响后面的站"), _title);
            foreach (var (st, flags) in story.Influence) Add(L.Format("{0}：{1}", OwnerName(map, layout, "stn:" + st), string.Join("、", flags.Select(L.DisplayText))), _small, 0, "", click: () => Select("stn:" + st));
            if (story.Stages.Count > 0) Add(L.Text("分段"), _title);
            foreach (var (stage, text) in story.Stages) Keep(story.Scripts.Where(s => s == stage || s.StartsWith(stage + "_", StringComparison.Ordinal)), L.DisplayText(RouteMapBuilder.FirstClause(text, 40)), _small, 0, L.DisplayText(text));
            return;
        }
        // Nothing chosen: how to read the map.
        Add(L.Text("剧情路线图"), _heading);
        Add(L.Text("横轴是游戏里的日历，每条车道是一条路线；圆点是主线的一站，方块是会改走主线的支线（襄阳行、山洞九日、雪山行）。点开一站看它的小节、进入条件和从这里岔出去的支线；下方的胶囊是支线，按怎么进入分三组。"), _muted);
        Add(L.Text("颜色：越偏黄越少读过（全部周目），越偏灰读得越多。蓝圈是这一局走过的站，淡蓝圈是推断走过的，金圈是下一站。虚线经过自由行动，上方拱起的线是跳过时间。"), _muted);
        Add(L.Text("有收录节点的小节和支线带「存入」，写进上方填的空格后从读档页读取，就从那里重玩。"), _muted);
        Add(L.Text("按住左键拖动地图可以移动；滚轮上下移动，Shift + 滚轮左右移动，Ctrl + 滚轮或上方的 −、+ 缩放。地图和说明栏之间的竖线可以拖动，调整两边宽度。"), _muted);
    }

    private static string Short(string text, int length = 90)
    {
        string line = text.Replace("\r", "").Replace('\n', ' ');
        return line.Length <= length ? line : line.Substring(0, length) + "…";
    }

    /// <summary>Stations, sections, side stories and categories by name, story record or dialogue (the game's own texts).</summary>
    private void RouteSearch(RouteMap map, RouteLayout layout, string query)
    {
        if (query == _routeSearched) return;
        _routeSearched = query;
        _routeHits.Clear();
        if (_routeDialogue == null)
        {
            _routeDialogue = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in map.Keys) _routeDialogue[pair.Key] = string.Join("\n", pair.Value.Select(RouteMapGame.StoryText));
        }
        string Snippet(string text, int at) { int from = Math.Max(0, at - 12); return text.Substring(from, Math.Min(40, text.Length - from)).Replace('\n', ' '); }
        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in map.Owner)
        {
            string script = pair.Key, owner = pair.Value, found = "";
            if (script.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) found = script;
            else if (OwnerName(map, layout, owner).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // A station or story found by its name is listed once, not once per script in it.
                if (!named.Add(owner)) continue;
                found = OwnerName(map, layout, owner);
            }
            else if (map.Legends.TryGetValue(script, out var legends) && legends.FirstOrDefault(t => L.DisplayText(t).Contains(query)) is string legend) found = Snippet(L.DisplayText(legend), L.DisplayText(legend).IndexOf(query, StringComparison.Ordinal));
            else if (_routeDialogue.TryGetValue(script, out var dialogue) && dialogue.IndexOf(query, StringComparison.Ordinal) is int at && at >= 0) found = Snippet(dialogue, at);
            if (found.Length == 0) continue;
            _routeHits.Add((owner, script, found));
            if (_routeHits.Count >= 60) break;
        }
    }
}
