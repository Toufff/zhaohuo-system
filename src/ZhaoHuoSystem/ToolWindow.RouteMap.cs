using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ZhaoHuoSystem;

// 剧情路线图 (F89) on the review page: the main line as stations on lanes along the game's calendar, the side routes
// beside it, side stories packed in rows by how they are entered, and what a station or story holds on the right.
// F98: the right side is laid out as a header, an action card for 跳转 / 直接跳转 and sections of rows that say by their
// look whether they choose (○ ●), move the map (›) or open (▸); a move eases the map there and rings the item it lands on.
public sealed partial class ToolWindow
{
    internal RouteMapRunner? Routes;
    private bool _routeMode, _routeOthers, _routeSummaryOpen;
    private string _routeSelected = "", _routeSearch = "", _routeSearched = "\u0001", _routeSideKey = "", _routeTargetKey = "", _routeOpenCategory = "";
    private Vector2 _routeScroll, _routeSideScroll;
    private RouteLayout? _routeLayout;
    private RouteMap? _routeLayoutMap;
    private float _routeLayoutScale;
    private readonly List<SideLine> _routeSide = new();
    private readonly List<(string Owner, string Script, string Snippet)> _routeHits = new();
    private Dictionary<string, string>? _routeDialogue;
    private GUIStyle _routeLabel = null!, _routeTag = null!, _routePill = null!, _routeItem = null!, _routeItemSelected = null!;
    private GUIStyle _routeYear = null!, _routeMonth = null!, _routeGroup = null!, _routeMark = null!, _routeLink = null!, _smallRight = null!;
    // The map's own zoom on top of 界面缩放, and the side column's share of the page once dragged (F92); this session only.
    private float _routeZoom = 1, _routeSideShare;
    private Vector2 _routeView;
    private const float RouteZoomMin = .5f, RouteZoomMax = 1.5f;
    private float Z(float value) => S(value) * _routeZoom;
    // A move to an item (F98): the scroll eases there, then the item rings for a moment.
    private string _routeFocus = "";
    private float _routeFocusAt = -10;
    private Vector2 _routeScrollFrom, _routeScrollTo;
    private bool _routeEasing;
    // Kept starts by script (剧情回顾) and the stations and stories holding one (▶), refreshed as the review changes.
    private Dictionary<string, StoryReview.Node> _routeKept = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _routeJumpable = new(StringComparer.Ordinal);
    private int _routeKeptRevision = -1;
    private RouteMap? _routeKeptMap;
    private JumpTarget? _routeTarget;

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

    private enum SideKind { Heading, Meta, Caption, Text, Row, Sub, Gap }
    /// <summary>One line of the right side. A row with Click is a button: a link (›, blue) moves the map, ○ ● choose the jump start, ▸ ▾ open.</summary>
    private sealed class SideLine
    {
        public SideKind Kind;
        public string Text = "", Right = "", Tooltip = "", Marker = "";
        public float Indent, Progress = -1;
        public bool Selected, Link, Warn, Good;
        public Action? Click;
    }

    private const float RouteSlot = 22, RouteGap = 70, RouteLane = 64, RouteAxis = 58, RouteRow = 24, RouteGroupHead = 30;
    private static readonly int[] RouteLanes = { -2, -1, 0, 1, 2, 3 };
    private static readonly Color RouteUnread = new(.91f, .70f, .29f), RouteRead = new(.42f, .47f, .45f), RouteEdge = new(.62f, .66f, .70f, .55f),
        RouteRun = new(.36f, .62f, .84f, .95f), RouteBand = new(1, 1, 1, .035f), RouteGrid = new(1, 1, 1, .06f), RouteLinkColor = new(.58f, .76f, .95f, 1);

    private static int RouteSlotOf(int[]? d) => d == null || d[0] <= 0 ? 0 : Math.Max(0, (d[0] - 1) * 36 + (d[1] - 4) * 3 + (d[2] - 1));
    private static Color RouteColor(int? read) => read == null ? new Color(.5f, .52f, .55f) : Color.Lerp(RouteUnread, RouteRead, read.Value / 100f);
    private static string RouteDate(int[]? d) => d == null || d[0] <= 0 ? L.Text("时间不明") : L.Format("第{0}年{1}月{2}旬", d[0], d[1], L.Text(d[2] switch { 1 => "上", 2 => "中", _ => "下" }));
    private static string RouteSpan(int[]? a, int[]? b) => a == null ? L.Text("时间不明") : b == null || RouteSlotOf(a) == RouteSlotOf(b) ? RouteDate(a) : L.Format("{0} 至 {1}", RouteDate(a), RouteDate(b));
    private static string Reading(int? read, int lines) => read == null ? L.Format("{0} 句", lines) : L.Format("{0} 句 · 读过 {1}%", lines, read.Value);

    /// <summary>The route map's buttons on the review page's own row, right to left before 说明.</summary>
    private void RouteToolbar(float y, float left, float right)
    {
        if (Routes == null) return;
        var map = Routes.Map;
        bool previous = GUI.enabled;
        float refresh = ButtonWidth("刷新", 70), others = ButtonWidth("其他内容", 95), next = ButtonWidth("显示下一个节点", 120);
        float x = right - refresh;
        GUI.enabled = previous && !Routes.Building;
        if (x > left && Button(new Rect(x, y, refresh, S(29)), "刷新", "route.rebuild", tooltip: L.Text("重新读取游戏数据，生成路线图")))
        { Routes.Start(); _routeLayoutMap = null; _routeSideKey = ""; _routeDialogue = null; _routeKeptMap = null; }
        x -= others + S(6);
        GUI.enabled = previous && map != null;
        if (x > left && Button(new Rect(x, y, others, S(29)), "其他内容", "route.others", _routeOthers ? _tabSelected : _button, L.Text("没画在时间轴上的闲聊、地点和会议事件")))
        { _routeOthers = !_routeOthers; _routeSearch = ""; _routeTargetKey = ""; _routeSideKey = ""; _routeSideScroll = Vector2.zero; }
        x -= next + S(6);
        GUI.enabled = previous && map != null && map.NextStation.Length > 0;
        if (x > left && Button(new Rect(x, y, next, S(29)), "显示下一个节点", "route.next", tooltip: L.Text("移到这一局的下一站并标出它")))
            SelectRoute("stn:" + map!.NextStation);
        GUI.enabled = previous;
    }

    private void DrawRouteMap(Rect r)
    {
        if (Routes == null) { Empty(r, "剧情回顾未载入"); return; }
        if (Routes.Map == null && !Routes.Building && Routes.Error.Length == 0) Routes.Start();
        var map = Routes.Map;
        // The map's facts go to the window's bottom bar, out of the way (F98).
        string source = _snapshot.Title && TitleSource.Length > 0 ? TitleSource + "  ·  " : "";
        _reviewStatus = Routes.Building ? L.Format("正在生成路线图……（第 {0} 帧）", Routes.Frames)
            : Routes.Error.Length > 0 ? L.Format("生成失败：{0}", Routes.Error)
            : map == null ? "" : source + L.Format("主线 {0} 站 · 支线 {1} 段 · 对白已读 {2}%", map.Stations.Count, map.Arcs.Count, map.LinesAll == 0 ? 0 : Mathf.RoundToInt(100f * map.ReadAll / map.LinesAll))
                + (Routes.Partial ? "  ·  " + L.Text("剧情条件未载入，进一次剧情后自动补全") : "");
        if (map == null) { Empty(r, Routes.Building ? "第一次打开要读一遍全部剧本，稍等片刻。" : Routes.Error.Length > 0 ? _reviewStatus : "路线图还没生成"); return; }
        RefreshKept(map);
        // Forks on the left (the prototype's top bar), the zoom on the right.
        float step = ButtonWidth("−", 36), percent = ButtonWidth("150%", 58), zoomX = r.xMax - step * 2 - percent - S(8);
        if (Button(new Rect(zoomX, r.y, step, S(29)), "−", "route.zoom.out", tooltip: L.Text("缩小"))) SetRouteZoom(_routeZoom - .1f, _routeView / 2);
        if (Button(new Rect(zoomX + step + S(4), r.y, percent, S(29)), L.Format("{0}%", Mathf.RoundToInt(_routeZoom * 100)), "route.zoom.reset", tooltip: L.Text("恢复 100%"))) SetRouteZoom(1, _routeView / 2);
        if (Button(new Rect(zoomX + step + percent + S(8), r.y, step, S(29)), "+", "route.zoom.in", tooltip: L.Text("放大"))) SetRouteZoom(_routeZoom + .1f, _routeView / 2);
        float forkLabel = _small.CalcSize(new GUIContent(L.Text("分岔点"))).x + S(10), forkX = r.x + forkLabel;
        Label(new Rect(r.x, r.y + S(2), forkLabel, S(26)), "分岔点", _small);
        var forks = RouteMapDefs.Forks.Select(f => (f.Name, Station: map.Stations.Find(s => s.Id == f.Station))).Where(f => f.Station != null)
            .Select(f => (Text: L.DisplayText(f.Name), When: f.Station!.Date != null ? L.Format("{0}年{1}月", f.Station.Date[0], f.Station.Date[1]) : "", f.Station)).ToList();
        // The date goes on the buttons when the row has room for all of them with it, as on the prototype; else on the hover.
        bool dated = forks.Sum(f => ButtonWidth(f.Text + "  " + f.When, 60) + S(6)) <= zoomX - S(10) - forkX;
        // Names longer than the row (English, F119) go on to another line under it.
        float forkRight = zoomX - S(10), forkY = r.y, bottom = r.yMax;
        foreach (var (name, when, fork) in forks)
        {
            string text = dated ? name + "  " + when : name, key = "stn:" + fork.Id;
            float width = ButtonWidth(text, 60);
            if (forkX + width > forkRight && forkX > r.x + forkLabel) { forkX = r.x + forkLabel; forkY += S(35); forkRight = r.xMax; }
            if (Button(new Rect(forkX, forkY, width, S(29)), text, "route.fork." + fork.Id, _routeSelected == key ? _tabSelected : _button, when + "  " + L.DisplayText(fork.Label))) SelectRoute(key);
            forkX += width + S(6);
        }
        r.y = forkY + S(37); r.height = bottom - r.y;
        RomanceRow(ref r);
        RomanceOf(map);
        if (_routeLayoutMap != map || Mathf.Abs(_routeLayoutScale - _scale) > .001f) { _routeLayout = Layout(map); _routeLayoutMap = map; _routeLayoutScale = _scale; }
        var layout = _routeLayout!;
        RomanceFocus();
        float sideWidth = Mathf.Clamp(_routeSideShare > 0 ? _routeSideShare * r.width : Mathf.Min(S(380), r.width * .42f), Mathf.Min(S(260), r.width * .3f), r.width * .6f);
        var canvas = new Rect(r.x, r.y, r.width - sideWidth - S(10), r.height);
        var side = new Rect(canvas.xMax + S(10), r.y, sideWidth, r.height);
        // Before the map: its control id must not move with the stations the map draws.
        RouteSplitter(new Rect(canvas.xMax, r.y, S(10), r.height), r);
        DrawRouteCanvas(canvas, map, layout);
        DrawRouteSide(side, map, layout);
    }

    /// <summary>Picks an item as the right side's subject; a move from the side, a fork or a search eases the map to it and rings it.</summary>
    private void SelectRoute(string key, bool scroll = true)
    {
        _routeSelected = key; _routeOthers = false; _routeSearch = ""; _routeSideKey = ""; _routeTargetKey = ""; _routeSummaryOpen = false; _routeSideScroll = Vector2.zero;
        FocusRoute(key, scroll);
    }

    private void FocusRoute(string key, bool scroll)
    {
        _routeFocus = key; _routeFocusAt = Time.unscaledTime; _routeEasing = false;
        if (!scroll || _routeLayout == null) return;
        Vector2? to = null;
        if (key.StartsWith("stn:", StringComparison.Ordinal) && _routeLayout.Points.TryGetValue(key.Substring(4), out var p)) to = new Vector2(p.X - _routeView.x / 2, p.Y - _routeView.y / 2);
        else if (key.StartsWith("arc:", StringComparison.Ordinal))
            foreach (var item in _routeLayout.Arcs) if ("arc:" + item.Arc.Id == key) { to = new Vector2(item.X1 - _routeView.x / 3, item.Y - _routeView.y / 2); break; }
        if (to is not { } target) return;
        _routeScrollFrom = _routeScroll; _routeScrollTo = new Vector2(Mathf.Max(0, target.x), Mathf.Max(0, target.y)); _routeEasing = true;
    }

    /// <summary>The kept starts as the review has them now, and which stations and stories hold one.</summary>
    private void RefreshKept(RouteMap map)
    {
        int revision = Review?.Revision ?? 0;
        if (revision == _routeKeptRevision && ReferenceEquals(map, _routeKeptMap)) return;
        _routeKeptRevision = revision; _routeKeptMap = map;
        _routeKept = Review?.Latest() ?? new Dictionary<string, StoryReview.Node>(StringComparer.OrdinalIgnoreCase);
        _routeJumpable.Clear();
        foreach (var s in map.Stations)
            if (_routeKept.ContainsKey(s.Start) || s.Sections.Any(section => section.Scripts.Any(_routeKept.ContainsKey))) _routeJumpable.Add("stn:" + s.Id);
        foreach (var a in map.Arcs)
            if (_routeKept.ContainsKey(a.Start) || a.Scripts.Any(_routeKept.ContainsKey)) _routeJumpable.Add("arc:" + a.Id);
        // A story's pill makes room for its ▶.
        _routeLayoutMap = null; _routeSideKey = "";
    }
    private StoryReview.Node? KeptIn(IEnumerable<string> scripts) => scripts.Select(s => _routeKept.TryGetValue(s, out var n) ? n : null).FirstOrDefault(n => n != null);

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
            case EventType.MouseDown when GUI.enabled && current.button == 0 && gap.Contains(current.mousePosition): GUIUtility.hotControl = id; current.Use(); break;
            case EventType.MouseDrag when dragging: _routeSideShare = Mathf.Clamp01((page.xMax - current.mousePosition.x - S(5)) / page.width); current.Use(); break;
            case EventType.MouseUp when dragging: GUIUtility.hotControl = 0; current.Use(); break;
        }
    }

    /// <summary>The point under the pointer (or the middle of the map) stays where it is.</summary>
    private void SetRouteZoom(float value, Vector2 pivot)
    {
        float next = Mathf.Clamp(Mathf.Round(value * 10) / 10, RouteZoomMin, RouteZoomMax);
        if (Mathf.Abs(next - _routeZoom) < .001f) return;
        _routeEasing = false;
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
        _routeMark = Canvas(11, RouteLinkColor, TextAnchor.MiddleCenter);
        _routeYear = Canvas(16, text, TextAnchor.MiddleLeft); _routeMonth = Canvas(13, muted, TextAnchor.MiddleLeft); _routeGroup = Canvas(15, muted, TextAnchor.MiddleLeft);
        _routeLink = TextStyle(16, RouteLinkColor); _routeLink.wordWrap = true;
        _smallRight = TextStyle(13, muted); _smallRight.alignment = TextAnchor.MiddleRight;
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
                float w = _routePill.CalcSize(new GUIContent(PillText(a))).x + Z(16) + (_routeJumpable.Contains("arc:" + a.Id) ? Z(12) : 0);
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
        if (GUI.enabled && current.type == EventType.ScrollWheel && canvas.Contains(current.mousePosition) && (current.shift || current.control))
        {
            float wheel = current.delta.y != 0 ? current.delta.y : current.delta.x;
            _routeEasing = false;
            if (current.control) SetRouteZoom(_routeZoom - Mathf.Sign(wheel) * .1f, current.mousePosition - canvas.position);
            else _routeScroll.x += wheel * S(20);
            current.Use();
        }
        if (_routeEasing)
        {
            float t = Mathf.Clamp01((Time.unscaledTime - _routeFocusAt) / .35f);
            _routeScroll = Vector2.Lerp(_routeScrollFrom, _routeScrollTo, 1 - (1 - t) * (1 - t) * (1 - t));
            if (t >= 1) _routeEasing = false;
        }
        _routeScroll = BeginScroll(canvas, _routeScroll, new Rect(0, 0, layout.Width, layout.Height), horizontal: true);
        // Taken before the stations and stories, whose number on screen changes as the map moves.
        int pan = GUIUtility.GetControlID(FocusType.Passive);
        try
        {
            var view = new Rect(_routeScroll.x, _routeScroll.y, canvas.width, canvas.height);
            bool repaint = Event.current.type == EventType.Repaint;
            // The ring starts as the scroll settles; the item grows a little and comes back (F98).
            float age = Time.unscaledTime - _routeFocusAt - (_routeEasing ? 1f : .25f);
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
                    float month = i + 3 < layout.X.Count ? layout.X[i + 3] - layout.X[i] - Z(6) : Z(46);
                    GUI.Label(new Rect(layout.X[i] + Z(4), Z(30), Mathf.Max(Z(46), month), Z(18)), L.Format("{0}月", (i / 3 + 3) % 12 + 1), _routeMonth);
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
                    var edge = run ? RouteRun : RouteEdge;
                    // 结缘 (F117): the main line stays as a guide, faint.
                    if (_romanceRoute != null) edge.a *= .35f;
                    DrawRouteEdge(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y), e.Kind, edge);
                }
            }
            foreach (var s in map.Stations)
            {
                if (!layout.Points.TryGetValue(s.Id, out var p)) continue;
                var hit = new Rect(p.X - Z(40), p.Up ? p.Y - Z(28) : p.Y - Z(12), Z(80), Z(44));
                if (!hit.Overlaps(view)) continue;
                string key = "stn:" + s.Id;
                bool lit = RomanceLit(key);
                var tint = GUI.color;
                if (!lit) GUI.color = new Color(tint.r, tint.g, tint.b, tint.a * .22f);
                if (repaint)
                {
                    bool selected = _routeSelected == key, next = map.NextStation == s.Id, focus = _routeFocus == key;
                    var centre = new Vector2(p.X, p.Y);
                    // 结缘 (F117): a station on her way gets her colour around it and the steps it marks.
                    if (_romanceRoute != null && lit)
                    {
                        Ring(centre, Z(19), Z(2.5f), RomanceColor);
                        string steps = RomanceSteps(key);
                        if (steps.Length > 0) GUI.Label(new Rect(p.X - Z(50), p.Up ? p.Y - Z(48) - (next ? Z(16) : 0) : p.Y + Z(29) + (next ? Z(16) : 0), Z(100), Z(18)), "【" + steps + "】", _routeTag);
                    }
                    if (next) Ring(centre, Z(17), Z(3), new Color(.95f, .80f, .36f));
                    if (p.Vis != "no" || selected) Ring(centre, Z(13), Z(2.5f), selected ? Color.white : p.Vis == "yes" ? RouteRun : new Color(RouteRun.r, RouteRun.g, RouteRun.b, .45f));
                    float size = (s.Kind == "route" ? Z(16) : Z(15)) * (focus && age >= 0 && age < .6f ? 1 + .55f * Mathf.Sin(age / .6f * Mathf.PI) : 1);
                    GUI.DrawTexture(new Rect(p.X - size / 2, p.Y - size / 2, size, size), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, Tinted(RouteColor(s.Read)), 0, s.Kind == "route" ? Z(4) : size / 2);
                    if (focus) for (int k = 0; k < 3; k++)
                    {
                        float phase = (age - k * .28f) / .8f;
                        if (phase is >= 0 and <= 1) Ring(centre, Z(13) + Z(30) * phase, Mathf.Max(1, Z(3) * (1 - phase)), new Color(1, 1, 1, .9f * (1 - phase)));
                    }
                    string label = L.DisplayText(s.Label);
                    var labelRect = new Rect(p.X - Z(70), p.Up ? p.Y - Z(30) : p.Y + Z(11), Z(140), Z(20));
                    GUI.Label(labelRect, label, _routeLabel);
                    if (next) GUI.Label(new Rect(p.X - Z(40), p.Up ? p.Y - Z(48) : p.Y + Z(29), Z(80), Z(18)), L.Text("下一站"), _routeTag);
                    if (s.Endings > 0) GUI.Label(new Rect(p.X + Z(9), p.Up ? p.Y + Z(4) : p.Y - Z(18), Z(20), Z(16)), L.Text("终"), _routeTag);
                    if (s.Variants > 0) GUI.Label(new Rect(p.X - Z(46), p.Up ? p.Y + Z(4) : p.Y - Z(18), Z(36), Z(16)), (s.DeadVariants.Count > 0 ? "⚠ " : "") + "+" + s.Variants, _routeTag);
                    // ▶: a kept start here, so 跳转 can go to it; on the side away from 终.
                    if (_routeJumpable.Contains(key)) GUI.Label(new Rect(p.X + Z(7), p.Up ? p.Y - Z(21) : p.Y + Z(5), Z(16), Z(16)), "▶", _routeMark);
                }
                GUI.color = tint;
                if (GUI.Button(hit, new GUIContent("", L.DisplayText(s.Label)), GUIStyle.none)) SelectRoute(key, scroll: false);
                Hover(hit, L.DisplayText(s.Label) + "\n" + RouteDate(s.Date) + (_routeJumpable.Contains(key) ? "\n" + L.Text("▶ 有收录进度，可以跳转") : "") + RomanceHover(key));
            }
            foreach (var (arc, x1, x2, w, y, _) in layout.Arcs)
            {
                var pill = new Rect(x1, y - Z(9), w, Z(18));
                var span = new Rect(x1, y - Z(1), Mathf.Max(0, x2 - x1), Z(2));
                if (!pill.Overlaps(view) && !span.Overlaps(view)) continue;
                string key = "arc:" + arc.Id;
                bool jumpable = _routeJumpable.Contains(key), lit = RomanceLit(key);
                var tint = GUI.color;
                if (!lit) GUI.color = new Color(tint.r, tint.g, tint.b, tint.a * .22f);
                if (repaint)
                {
                    if (x2 > x1 + w) Fill(new Rect(x1 + w, y - Z(1), x2 - x1 - w, Z(2)), new Color(RouteColor(arc.Read).r, RouteColor(arc.Read).g, RouteColor(arc.Read).b, .5f));
                    var fill = RouteColor(arc.Read); fill.a = .9f;
                    GUI.DrawTexture(pill, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, Tinted(fill), 0, Z(9));
                    if (arc.Visited || _routeSelected == key) GUI.DrawTexture(pill, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, Tinted(_routeSelected == key ? Color.white : RouteRun), Z(2), Z(9));
                    // 结缘 (F117): a story on her way is outlined in her colour (white still marks the chosen one).
                    else if (_romanceRoute != null && lit) GUI.DrawTexture(pill, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, RomanceColor, Z(2), Z(9));
                    if (_routeFocus == key) for (int k = 0; k < 3; k++)
                    {
                        float phase = (age - k * .28f) / .8f, grow = Z(16) * phase;
                        if (phase is >= 0 and <= 1)
                            GUI.DrawTexture(new Rect(pill.x - grow, pill.y - grow, pill.width + grow * 2, pill.height + grow * 2), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                                new Color(1, 1, 1, .9f * (1 - phase)), Mathf.Max(1, Z(3) * (1 - phase)), Z(9) + grow);
                    }
                    if (jumpable) GUI.Label(new Rect(pill.x + Z(3), pill.y, Z(14), pill.height), "▶", _routePill);
                    GUI.Label(jumpable ? new Rect(pill.x + Z(12), pill.y, pill.width - Z(12), pill.height) : pill, PillText(arc), _routePill);
                }
                GUI.color = tint;
                if (GUI.Button(pill, GUIContent.none, GUIStyle.none)) SelectRoute(key, scroll: false);
                Hover(pill, L.DisplayText(arc.Label) + "\n" + RouteSpan(arc.Lo, arc.Hi) + (jumpable ? "\n" + L.Text("▶ 有收录进度，可以跳转") : "") + RomanceHover(key));
            }
            // Dragging the map moves it; a station or story under the press takes its own click first.
            switch (current.GetTypeForControl(pan))
            {
                case EventType.MouseDown when GUI.enabled && current.button == 0 && view.Contains(current.mousePosition): GUIUtility.hotControl = pan; _routeEasing = false; current.Use(); break;
                case EventType.MouseDrag when GUIUtility.hotControl == pan: _routeScroll -= current.delta; current.Use(); break;
                case EventType.MouseUp when GUIUtility.hotControl == pan: GUIUtility.hotControl = 0; current.Use(); break;
            }
        }
        finally { EndScroll(); }
    }

    // A colour given to DrawTexture ignores GUI.color: it is faded here with GUI.color's alpha (结缘 dims the rest, F117).
    private static Color Tinted(Color color) { color.a *= GUI.color.a; return color; }
    private static void Fill(Rect rect, Color color) => GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, Tinted(color), 0, 0);
    private static void Ring(Vector2 centre, float radius, float width, Color color)
        => GUI.DrawTexture(new Rect(centre.x - radius, centre.y - radius, radius * 2, radius * 2), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, Tinted(color), width, radius);

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

    // ---- the right-hand side: search, the action card, then the subject's sections ----
    private void DrawRouteSide(Rect side, RouteMap map, RouteLayout layout)
    {
        _routeSearch = Search(new Rect(side.x, side.y, side.width, S(32)), _routeSearch, "搜人物、地点、事件或台词", "zh.review.route");
        string key = string.Join("|", _routeSelected, _routeSearch, _routeOthers, _routeTargetKey, _routeOpenCategory, _routeSummaryOpen, map.GetHashCode(), L.Revision, _scale, _routeKeptRevision, _romance, RomanceSideKey());
        if (key != _routeSideKey) { BuildRouteSide(map, layout); _routeSideKey = key; }
        float y = side.y + S(42);
        if (_routeTarget != null)
        {
            float card = CardHeight(_routeTarget, side.width);
            DrawActionCard(new Rect(side.x, y, side.width, card), _routeTarget);
            y += card + S(10);
        }
        var body = new Rect(side.x, y, side.width, side.yMax - y);
        float width = body.width - S(18), total = _routeSide.Sum(line => LineHeight(line, width));
        _routeSideScroll = BeginScroll(body, _routeSideScroll, new Rect(0, 0, width, Mathf.Max(body.height, total)));
        try
        {
            float top = 0;
            foreach (var line in _routeSide)
            {
                float h = LineHeight(line, width);
                if (top + h >= _routeSideScroll.y && top <= _routeSideScroll.y + body.height && DrawSideLine(line, new Rect(0, top, width, h))) break;
                top += h;
            }
        }
        finally { EndScroll(); }
    }

    private static string CardHint(JumpTarget target) => target.Node is { } node
        ? L.Format("有收录进度（收录于 {0}），从这一段的开头进入。", RouteDate(new[] { node.Year, node.Month, node.Stage }))
        : L.Text("没有收录进度：装着模组玩到这里时才会收录。只能直接跳转，可能出错。");
    // The hint wraps with the column's width; the card grows to hold it and the button row below it.
    private float CardHeight(JumpTarget target, float width)
        => S(58) + TextHeight(target.Node != null ? _small : _unmetSmall, CardHint(target), width - S(26)) + S(50);

    /// <summary>The start 跳转 goes to, whether a kept start is there, and the two buttons (直接跳转 only without one).</summary>
    private void DrawActionCard(Rect r, JumpTarget target)
    {
        // Darker than the buttons on it, so they read as buttons; the game's framed panel with 界面风格.
        GUI.DrawTexture(r, _cardFill);
        if (!GameSkin.Draw(r, GameSkin.Part.Card, _scale, Color.white)) GUI.DrawTexture(new Rect(r.x, r.y, S(3), r.height), _jumpDialogAccent);
        float x = r.x + S(14), width = r.width - S(26);
        Label(new Rect(x, r.y + S(8), width, S(20)), "跳转起点", _small);
        Label(new Rect(x, r.y + S(28), width, S(26)), target.Title, _label, target.Title);
        string hint = CardHint(target);
        var hintStyle = target.Node != null ? _small : _unmetSmall;
        Label(new Rect(x, r.y + S(58), width, TextHeight(hintStyle, hint, width)), hint, hintStyle, hint);
        float jump = ButtonWidth("跳转", 84), direct = ButtonWidth("直接跳转", 96), by = r.yMax - S(42);
        bool previous = GUI.enabled;
        if (target.Node != null)
        {
            if (Button(new Rect(r.xMax - S(12) - jump, by, jump, S(30)), "跳转", "route.jump", _primary, L.Text("选一个存档格，写好后直接进入这一段"))) OpenJump(target, false);
        }
        else
        {
            GUI.enabled = false;
            Button(new Rect(r.xMax - S(12) - direct - S(8) - jump, by, jump, S(30)), "跳转", "route.jump", _button);
            GUI.enabled = previous && target.Script.Length > 0;
            if (Button(new Rect(r.xMax - S(12) - direct, by, direct, S(30)), "直接跳转", "route.direct", _button, L.Text("没有收录进度时用：会先说明可能出现的问题"))) OpenJump(target, true);
            GUI.enabled = previous;
        }
    }

    private float LineHeight(SideLine line, float width)
    {
        switch (line.Kind)
        {
            case SideKind.Gap: return S(8);
            case SideKind.Caption: return S(40);
            case SideKind.Heading: return TextHeight(_heading, line.Text, width - line.Indent) + S(2);
            case SideKind.Row:
                float text = width - line.Indent - S(30) - RightWidth(line);
                return Mathf.Max(S(32), TextHeight(line.Link ? _routeLink : _label, line.Text, text) + S(8));
            default: return TextHeight(line.Warn ? _unmetSmall : line.Good ? _metSmall : _small, line.Text, width - line.Indent);
        }
    }
    private float RightWidth(SideLine line) => line.Right.Length == 0 ? 0 : _smallRight.CalcSize(new GUIContent(line.Right)).x + S(10) + (line.Progress >= 0 ? S(46) : 0);

    /// <summary>True when a click changed the side (the caller stops drawing the old lines).</summary>
    private bool DrawSideLine(SideLine line, Rect r)
    {
        var rect = new Rect(r.x + line.Indent, r.y, r.width - line.Indent, r.height);
        switch (line.Kind)
        {
            case SideKind.Gap: return false;
            case SideKind.Heading: Label(rect, line.Text, _heading, line.Tooltip); return false;
            case SideKind.Caption:
                GUI.DrawTexture(new Rect(rect.x, rect.y + S(8), rect.width, 1), _line);
                Label(new Rect(rect.x, rect.y + S(14), rect.width, S(24)), line.Text, _caption, line.Tooltip);
                return false;
            case SideKind.Row:
                bool clicked = line.Click != null && SkinnedButton(new Rect(rect.x, rect.y + S(1), rect.width, rect.height - S(2)), GUIContent.none, line.Selected ? _routeItemSelected : _routeItem);
                float right = RightWidth(line);
                if (line.Marker.Length > 0) Label(new Rect(rect.x + S(6), rect.y + S(4), S(20), S(24)), line.Marker, line.Link ? _routeLink : _label);
                Label(new Rect(rect.x + S(28), rect.y + S(2), rect.width - S(30) - right, rect.height - S(4)), line.Text, line.Link ? _routeLink : _label);
                if (line.Right.Length > 0)
                {
                    float rx = rect.xMax - right;
                    if (line.Progress >= 0)
                    {
                        Fill(new Rect(rx + S(4), rect.y + rect.height / 2 - S(3), S(36), S(6)), new Color(1, 1, 1, .12f));
                        Fill(new Rect(rx + S(4), rect.y + rect.height / 2 - S(3), S(36) * Mathf.Clamp01(line.Progress), S(6)), new Color(1, 1, 1, .55f));
                        rx += S(46);
                    }
                    Label(new Rect(rx, rect.y + S(4), rect.xMax - rx - S(6), S(24)), line.Right, _smallRight);
                }
                Hover(rect, line.Tooltip);
                if (clicked) { line.Click!(); return true; }
                return false;
            default:
                Label(rect, line.Text, line.Warn ? _unmetSmall : line.Good ? _metSmall : _small, line.Tooltip);
                return false;
        }
    }

    private string OwnerName(RouteMap map, RouteLayout layout, string owner)
    {
        if (owner.StartsWith("stn:", StringComparison.Ordinal)) return layout.Stations.TryGetValue(owner.Substring(4), out var s) ? L.DisplayText(s.Label) : owner.Substring(4);
        if (owner.StartsWith("arc:", StringComparison.Ordinal)) return layout.ArcById.TryGetValue(owner.Substring(4), out var a) ? L.DisplayText(a.Label) : owner.Substring(4);
        if (owner.StartsWith("cat:", StringComparison.Ordinal)) return L.DisplayText(RouteMapDefs.CategoryNames.TryGetValue(owner.Substring(4), out var c) ? c : owner.Substring(4));
        return owner == "free" ? L.Text("自由行动") : owner == "ending" ? L.Text("结局") : owner;
    }
    /// <summary>A main-line station's mission key (M…), the main mission a 直接跳转 there sets; none for the prologue loop and the side routes.</summary>
    private static string MainKey(RouteMap.Station station) => station.Id.Length > 1 && station.Id[0] == 'M' && char.IsDigit(station.Id[1]) ? station.Id : "";

    private void BuildRouteSide(RouteMap map, RouteLayout layout)
    {
        _routeSide.Clear();
        _routeTarget = null;
        void Line(SideKind kind, string text, string tooltip = "", float indent = 0, bool warn = false) => _routeSide.Add(new SideLine { Kind = kind, Text = text, Tooltip = tooltip, Indent = indent, Warn = warn });
        void Row(string marker, string text, string right, Action? click, string tooltip = "", float indent = 0, bool selected = false, bool link = false, float progress = -1)
            => _routeSide.Add(new SideLine { Kind = SideKind.Row, Marker = marker, Text = text, Right = right, Click = click, Tooltip = tooltip, Indent = indent, Selected = selected, Link = link, Progress = progress });
        void Link(string text, string owner, string right = "", float indent = 0) => Row("›", text, right, () => SelectRoute(owner), L.Text("在地图上移过去并标出"), indent, link: true);
        void Summary(IEnumerable<string> lines)
        {
            var all = lines.Select(L.DisplayText).Where(t => t.Trim().Length > 0).ToList();
            if (all.Count == 0) return;
            string text = string.Join("\n", all);
            if (_routeSummaryOpen || text.Length <= 80) Line(SideKind.Text, text);
            else Line(SideKind.Text, Short(text, 80), text);
            if (text.Length > 80) Row(_routeSummaryOpen ? "▾" : "▸", _routeSummaryOpen ? L.Text("收起") : L.Text("展开剧情概要"), "", () => { _routeSummaryOpen = !_routeSummaryOpen; _routeSideKey = ""; });
        }
        float progress(int? read) => read == null ? -1 : read.Value / 100f;

        if (_routeSearch.Trim().Length > 0)
        {
            RouteSearch(map, layout, _routeSearch.Trim());
            Line(SideKind.Meta, _routeHits.Count == 0 ? L.Text("没有找到") : L.Format("找到 {0} 处（最多列 60 处），点名字在地图上移过去", _routeHits.Count));
            foreach (var group in _routeHits.GroupBy(h => h.Owner))
            {
                string owner = group.Key;
                if (owner.StartsWith("stn:", StringComparison.Ordinal) || owner.StartsWith("arc:", StringComparison.Ordinal)) Link(OwnerName(map, layout, owner), owner, L.Format("{0} 处", group.Count()));
                else Row("", OwnerName(map, layout, owner), L.Format("{0} 处", group.Count()), () => { _routeSearch = ""; _routeOthers = true; _routeOpenCategory = owner.Substring(owner.IndexOf(':') + 1); _routeSideKey = ""; }, L.Text("在「其他内容」里打开"));
                foreach (var (_, script, snippet) in group.Take(4)) Line(SideKind.Sub, snippet + "  " + script, snippet, S(28));
            }
            return;
        }
        if (_routeOthers)
        {
            int lines = map.Categories.Sum(c => c.Lines);
            Line(SideKind.Heading, L.Text("其他内容"));
            Line(SideKind.Meta, L.Format("自由行动里的地点、闲聊、出行和会议等，不按日历推进，没画在时间轴上：共 {0} 句对白，约占全部的 {1}%。点一类展开，看里面有名字的事件。", lines, map.LinesAll == 0 ? 0 : Mathf.RoundToInt(100f * lines / map.LinesAll)));
            Line(SideKind.Gap, "");
            foreach (var c in map.Categories.Where(c => c.Lines > 0))
            {
                bool open = _routeOpenCategory == c.Id;
                Row(open ? "▾" : "▸", OwnerName(map, layout, "cat:" + c.Id), Reading(c.Read, c.Lines) + (c.Visited ? " · " + L.Text("走过") : ""),
                    () => { _routeOpenCategory = open ? "" : c.Id; _routeTargetKey = ""; _routeSideKey = ""; }, string.Join("\n", c.Sample.Select(L.DisplayText)), 0, open, false, progress(c.Read));
                if (!open) continue;
                if (c.Events.Count == 0) Line(SideKind.Sub, L.Text("没有带名字的事件"), "", S(28));
                for (int i = 0; i < c.Events.Count; i++)
                {
                    var ev = c.Events[i];
                    string target = "evt:" + c.Id + ":" + i;
                    bool kept = _routeKept.ContainsKey(ev.Script), chosen = _routeTargetKey == target;
                    if (chosen) _routeTarget = new JumpTarget { Title = L.DisplayText(ev.Name), Node = KeptIn(new[] { ev.Script }), Script = ev.Script, Date = ev.Lo };
                    Row(chosen ? "●" : "○", (kept ? "▶ " : "") + L.DisplayText(ev.Name), (ev.Lo != null ? RouteSpan(ev.Lo, ev.Hi) + " · " : "") + Reading(ev.Read, ev.Lines),
                        () => { _routeTargetKey = chosen ? "" : target; _routeSideKey = ""; }, L.Text("设为跳转起点") + "\n" + ev.Script, S(18), chosen);
                    if (ev.Condition.Length > 0) Line(SideKind.Sub, L.Format("条件：{0}", ev.Condition), "", S(46));
                }
            }
            return;
        }
        if (_routeSelected.StartsWith("stn:", StringComparison.Ordinal) && layout.Stations.TryGetValue(_routeSelected.Substring(4), out var station))
        {
            RomanceHeader(map, layout, _routeSelected);
            var p = layout.Points.TryGetValue(station.Id, out var pp) ? pp : default;
            Line(SideKind.Heading, L.DisplayText(station.Label));
            var meta = new List<string> { RouteDate(station.Date) };
            if (station.Kind == "route") meta.Add(L.Text("改走主线的支线"));
            else if (station.Route.Length > 0) meta.Add(L.DisplayText(station.Route));
            if (map.NextStation == station.Id) meta.Add(L.Text("下一站"));
            meta.Add(p.Vis == "yes" ? L.Text("这一局走过") : p.Vis == "inferred" ? L.Text("这一局应该走过") : L.Text("这一局没走过"));
            meta.Add(Reading(station.Read, station.Lines));
            if (station.Branch > 0) meta.Add(L.Format("{0} 处分支", station.Branch));
            if (station.Endings > 0) meta.Add(L.Format("{0} 个结局", station.Endings));
            if (station.Variants > 0) meta.Add(L.Format("{0} 个变体", station.Variants));
            Line(SideKind.Meta, string.Join(" · ", meta), p.Vis == "inferred" ? L.Text("前后都走过且只有一条路") : "");
            foreach (string dead in station.DeadVariants) Line(SideKind.Text, "⚠ " + dead, "", 0, true);
            Summary(station.Summary.Take(2));
            // Sections: one is the jump start (the first by default); ▶ marks those with a kept start.
            if (station.Sections.Count > 0)
            {
                int chosenIndex = _routeTargetKey.StartsWith("sec:", StringComparison.Ordinal) && int.TryParse(_routeTargetKey.Substring(4), out int c) && c < station.Sections.Count ? c : 0;
                Line(SideKind.Caption, L.Format("小节（{0}）", station.Sections.Count), L.Text("点一节，设为跳转起点"));
                for (int i = 0; i < station.Sections.Count; i++)
                {
                    var section = station.Sections[i];
                    bool chosen = i == chosenIndex, kept = section.Scripts.Any(_routeKept.ContainsKey);
                    string mark = section.Trunk ? L.Text("主干") : section.Reach ? L.Text("岔路") : section.ViaMission ? L.Text("按日期") : L.Text("接续不明");
                    if (chosen)
                    {
                        string start = section.Scripts.Contains(station.Start) ? station.Start : section.Scripts.FirstOrDefault() ?? station.Start;
                        _routeTarget = new JumpTarget { Title = L.DisplayText(station.Label) + " · " + L.DisplayText(section.Label), Node = KeptIn(section.Scripts), Script = start, Date = station.Date, MainMission = MainKey(station) };
                    }
                    int index = i;
                    Row(chosen ? "●" : "○", (kept ? "▶ " : "") + mark + "  " + L.DisplayText(section.Label), Reading(section.Read, section.Lines) + (section.Visited ? " · " + L.Text("走过") : ""),
                        () => { _routeTargetKey = "sec:" + index; _routeSideKey = ""; }, string.Join("\n", section.Summary.Select(L.DisplayText).Concat(new[] { string.Join(" ", section.Scripts.Take(12)) })), 0, chosen);
                    foreach (var condition in section.Conditions) Line(SideKind.Sub, L.Format("条件：{0}", string.Join("，", condition)), "", S(28));
                    if (section.Dead.Length > 0) Line(SideKind.Sub, "⚠ " + section.Dead, "", S(28), true);
                }
            }
            if (station.Kind == "route")
            {
                Line(SideKind.Caption, L.Text("这条支线"));
                if (station.Note.Length > 0) Line(SideKind.Text, L.DisplayText(station.Note));
                foreach (var entry in station.Entry.Where(e => e.Count > 0)) Line(SideKind.Sub, L.Format("进入条件：{0}", string.Join("，", entry)));
                foreach (string exit in station.Exits) Link(L.Format("走完接到：{0}", OwnerName(map, layout, "stn:" + exit)), "stn:" + exit);
                foreach (var (st, flags) in station.Influence) Link(L.Format("会影响 {0}", OwnerName(map, layout, "stn:" + st)), "stn:" + st, string.Join("、", flags.Select(L.DisplayText)));
            }
            var forks = map.Arcs.Where(a => a.Anchor == station.Id).ToList();
            var routes = map.Edges.Where(e => e.From == station.Id && layout.Stations.TryGetValue(e.To, out var rs) && rs.Kind == "route").Select(e => layout.Stations[e.To]).ToList();
            if (forks.Count + routes.Count > 0) Line(SideKind.Caption, (station.Id == "P0" ? L.Text("只在序章里出现的事件") : L.Text("从这里岔出去的")) + L.Format("（{0}）", forks.Count + routes.Count));
            foreach (var rs in routes)
            {
                Link((_routeJumpable.Contains("stn:" + rs.Id) ? "▶ " : "") + L.DisplayText(rs.Label), "stn:" + rs.Id, Reading(rs.Read, rs.Lines));
                foreach (var entry in rs.Entry.Where(e => e.Count > 0)) Line(SideKind.Sub, L.Format("条件：{0}", string.Join("，", entry)), "", S(28));
            }
            foreach (var arc in forks)
            {
                Link((_routeJumpable.Contains("arc:" + arc.Id) ? "▶ " : "") + L.DisplayText(arc.Label), "arc:" + arc.Id, Reading(arc.Read, arc.Lines) + (arc.Visited ? " · " + L.Text("走过") : ""));
                foreach (var condition in arc.Entries.Where(e => e.Via == "stn:" + station.Id).SelectMany(e => e.Conditions).Where(c => c.Count > 0).Take(2))
                    Line(SideKind.Sub, L.Format("条件：{0}", string.Join("，", condition)), "", S(28));
            }
            return;
        }
        if (_routeSelected.StartsWith("arc:", StringComparison.Ordinal) && layout.ArcById.TryGetValue(_routeSelected.Substring(4), out var story))
        {
            string group = layout.Arcs.FirstOrDefault(x => x.Arc == story).Group ?? "";
            RomanceHeader(map, layout, _routeSelected);
            Line(SideKind.Heading, L.DisplayText(story.Label));
            var meta = new List<string> { RouteSpan(story.Lo, story.Hi) + (story.Approx ? L.Text("（约）") : ""),
                L.Text(group == "fork" ? "从主线某一站岔出" : group == "date" ? "按日期和条件触发" : "自由行动里触发"),
                story.Visited ? L.Text("这一局走过") : L.Text("这一局没走过"), Reading(story.Read, story.Lines) };
            if (story.Branch > 0) meta.Add(L.Format("{0} 处分支", story.Branch));
            if (story.Endings > 0) meta.Add(L.Format("{0} 个结局", story.Endings));
            Line(SideKind.Meta, string.Join(" · ", meta));
            if (story.Dead.Length > 0) Line(SideKind.Text, "⚠ " + story.Dead, "", 0, true);
            if (story.Desc.Length > 0 && L.DisplayText(story.Desc) != L.DisplayText(story.Label)) Line(SideKind.Text, L.DisplayText(story.Desc));
            // Jump start: the story's beginning, or one of its stages.
            var stages = story.Stages.Select(s => (s.Key, s.Text, Scripts: story.Scripts.Where(x => x == s.Key || x.StartsWith(s.Key + "_", StringComparison.Ordinal)).ToList())).ToList();
            int chosenStage = _routeTargetKey.StartsWith("stage:", StringComparison.Ordinal) && int.TryParse(_routeTargetKey.Substring(6), out int cs) && cs < stages.Count ? cs : -1;
            Line(SideKind.Caption, L.Text("跳转起点"), L.Text("点一项，设为跳转起点"));
            var beginning = new[] { story.Start }.Concat(story.Scripts).ToList();
            bool beginKept = KeptIn(beginning) != null;
            if (chosenStage < 0) _routeTarget = new JumpTarget { Title = L.DisplayText(story.Label), Node = KeptIn(beginning), Script = story.Start, Date = story.Lo };
            Row(chosenStage < 0 ? "●" : "○", (beginKept ? "▶ " : "") + L.Text("从开头"), story.Start, () => { _routeTargetKey = ""; _routeSideKey = ""; }, "", 0, chosenStage < 0);
            for (int i = 0; i < stages.Count; i++)
            {
                var (stageKey, text, scripts) = stages[i];
                bool chosen = i == chosenStage, kept = scripts.Any(_routeKept.ContainsKey);
                string title = L.DisplayText(RouteMapBuilder.FirstClause(text, 40));
                if (chosen) _routeTarget = new JumpTarget { Title = L.DisplayText(story.Label) + " · " + title, Node = KeptIn(scripts), Script = scripts.FirstOrDefault() ?? stageKey, Date = story.Lo };
                int index = i;
                Row(chosen ? "●" : "○", (kept ? "▶ " : "") + title, "", () => { _routeTargetKey = "stage:" + index; _routeSideKey = ""; }, L.DisplayText(text), 0, chosen);
            }
            if (story.Anchor.Length > 0 || story.Parent.Length > 0 || story.Entries.Count > 0) Line(SideKind.Caption, L.Text("怎么进入"));
            if (story.Anchor.Length > 0) Link(L.Format("挂在：{0}", OwnerName(map, layout, "stn:" + story.Anchor)), "stn:" + story.Anchor);
            if (story.Parent.Length > 0) Link(L.Format("接在：{0}", OwnerName(map, layout, "arc:" + story.Parent)), "arc:" + story.Parent);
            foreach (var entry in story.Entries.Take(6))
            {
                string via = entry.Via, text = L.Format("从「{0}」（{1}）", OwnerName(map, layout, via), entry.From);
                if (via.StartsWith("stn:", StringComparison.Ordinal) || via.StartsWith("arc:", StringComparison.Ordinal)) Link(text, via);
                else Row("", text, "", null);
                foreach (var condition in entry.Conditions.Where(c => c.Count > 0)) Line(SideKind.Sub, L.Format("条件：{0}", string.Join("，", condition)), "", S(28));
                if (entry.Dead.Length > 0) Line(SideKind.Sub, "⚠ " + entry.Dead, "", S(28), true);
            }
            foreach (string calendar in story.Calendar) Line(SideKind.Sub, L.Format("按日期：{0}", calendar));
            if (story.Exits.Count > 0)
            {
                Line(SideKind.Caption, L.Text("走完去哪"));
                foreach (string exit in story.Exits)
                    if (exit.StartsWith("stn:", StringComparison.Ordinal) || exit.StartsWith("arc:", StringComparison.Ordinal)) Link(OwnerName(map, layout, exit), exit);
                    else Row("", OwnerName(map, layout, exit), "", null);
            }
            if (story.Influence.Count > 0)
            {
                Line(SideKind.Caption, L.Text("会影响后面的站"));
                foreach (var (st, flags) in story.Influence) Link(OwnerName(map, layout, "stn:" + st), "stn:" + st, string.Join("、", flags.Select(L.DisplayText)));
            }
            return;
        }
        // 结缘 (F117): with a heroine chosen and nothing else, her page.
        if (_romanceRoute != null) { RomancePage(map, layout); return; }
        // Nothing chosen: what to do here, and the way to the next station.
        Line(SideKind.Heading, L.Text("剧情路线图"));
        Line(SideKind.Text, L.Text("点地图上的站点或支线，右边会列出它的小节和岔路。带 ▶ 的有收录进度，可以「跳转」直接进入；没有的只能「直接跳转」，可能出错。详细规则见上方「说明」。"));
        if (map.NextStation.Length > 0 && layout.Stations.TryGetValue(map.NextStation, out var next))
        {
            Line(SideKind.Caption, L.Text("这一局"));
            Link(L.Format("下一个节点：{0}", L.DisplayText(next.Label)), "stn:" + next.Id, RouteDate(next.Date));
        }
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
