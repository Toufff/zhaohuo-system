using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Fungus;
using HarmonyLib;
using Mortal.Core;
using Mortal.Story;
using UnityEngine;
using UnityEngine.UI;

namespace ZhaoHuoSystem;

/// <summary>
/// 回想 (F101): the game's log of the story's lines gets a line for each condition branch (the arm taken, with its
/// condition's values), and under it up to two links: 回到这段开头 (the script the branch ran in, from its start with the
/// values it started with, written into a slot and loaded as 跳转 does) and 决定它的选项 (the options in the scripts that set
/// the flags the branch reads). The log is one Text in a scroll view, rebuilt from its entries; the links are found in it by
/// character and drawn over by the mod, which takes their clicks. The log lives in memory only and is cleared as each story
/// scene starts; the starts kept here go with it (the scene change).
/// F106: by default each line is one small summary (no name line), its full conditions in a note under the pointer;
/// 设置/回想中的剧情分支 also offers the full lines or none.
/// </summary>
internal sealed class StoryLog : IDisposable
{
    /// <summary>A story script's start in this scene: what 回到这段开头 writes.</summary>
    internal sealed class Start { public string Script = "", Title = "", Music = "", Env = ""; public GameSave Save = null!; }
    /// <summary>A flag the branch reads, with its value then.</summary>
    internal sealed class Flag { public string Id = "", Name = ""; public int Value; }
    private sealed class Line
    {
        public NarrativeLogEntry Entry = null!;
        public Start? Start;
        public List<Flag> Flags = new();
        /// <summary>Each branch taken between two of the story's lines.</summary>
        public List<string> Rows = new();
        public string Branch = "", Back = "", Source = "";
        /// <summary>Raw length of the summary at the start of the entry's text (简略), the part the note shows under the pointer.</summary>
        public int Summary;
    }
    private enum Kind { Back, Source, Summary }
    private sealed class Link { public Line Line = null!; public Kind Kind; public List<Rect> Local = new(); }
    /// <summary>设置/回想中的剧情分支 (F106): 0 简略, 1 完整, 2 不显示.</summary>
    public int Mode;

    private const int KeptStarts = 40, KeptLines = 300;
    private const string LinkColor = "#D9B077", MutedColor = "#A39580";
    // A summary's first condition, cut to this many characters.
    private const int SummaryChars = 18;
    private static readonly AccessTools.FieldRef<NarrativeLog, NarrativeData> History = AccessTools.FieldRefAccess<NarrativeLog, NarrativeData>("history");
    private static readonly AccessTools.FieldRef<NarrativeLogMenu, ScrollRect> View = AccessTools.FieldRefAccess<NarrativeLogMenu, ScrollRect>("narrativeLogView");
    private static readonly FieldInfo? MenuField = AccessTools.Field(typeof(NarrativeLogMenu), "instance");
    private static readonly FieldInfo? ActiveField = AccessTools.Field(typeof(NarrativeLogMenu), "narrativeLogActive");
    private static readonly FieldInfo? MaxField = AccessTools.Field(typeof(NarrativeLogMenu), "_maxLogEntry");
    private static readonly MethodInfo? Rebuild = AccessTools.Method(typeof(NarrativeLogMenu), "UpdateNarrativeLogText");
    private readonly List<Start> starts = new();
    private readonly List<Line> lines = new();
    private List<Link> links = new();
    private string? laidOut;
    private int laidCount;
    private Texture2D? glow, underline, noteFill;
    private Font? noteFont;
    private GUIStyle? noteText;
    private float noteScale = -1;
    private long noteRevision = -1;
    private int noteSkin = int.MinValue;
    /// <summary>The link under the pointer, in GUI coordinates; the input guard keeps the game's clicks out of it.</summary>
    public Rect Hover { get; private set; }
    public Action<Start>? BackClicked;
    /// <summary>The branch as it reads (no colours) and the flags it reads.</summary>
    public Action<string, List<Flag>>? SourceClicked;

    /// <summary>From SegmentSaves, as a story script starts with its copy at hand.</summary>
    public void Kept(string script, GameSave save, string music, string env)
    {
        var node = new StoryReview.Node { Script = script, Year = save.CurrentYear, Month = save.CurrentMonth, Stage = (int)save.CurrentStage, Mission = StoryReview.MissionNow() };
        starts.Add(new Start { Script = script, Save = save, Music = music, Env = env, Title = StoryReview.Title(node) });
        if (starts.Count > KeptStarts) starts.RemoveAt(0);
    }

    public void Clear() { starts.Clear(); lines.Clear(); links.Clear(); laidOut = null; Hover = default; }

    /// <summary>The game's 回想 is open (it covers the story and its menus).</summary>
    public static bool Open => ActiveField?.GetValue(null) is true;

    /// <summary>
    /// F104: the game keeps whether 回想 is open in one static switch that each open or close flips. A scene left with 回想
    /// open (a jump from its links) leaves the switch set, and every later 回想 then fades its text out instead of in while
    /// the story pauses: it looks frozen until Esc. Before a jump the log is closed the game's own way; a story scene that
    /// starts with its log closed puts a stray switch back.
    /// </summary>
    public static void CloseForJump()
    {
        if (StoryManager.Instance is not StoryManager story || story == null || R.Field(story, "_logOpen") is not true) return;
        if (R.Field(story, "_logButton") is Toggle button && button != null) button.isOn = false;
    }
    public static bool Repair(StoryManager story)
    {
        if (ActiveField?.GetValue(null) is not true || R.Field(story, "_logOpen") is true) return false;
        ActiveField.SetValue(null, false);
        if (R.Field(story, "_narrativeLogMenu") is NarrativeLogMenu menu && menu != null && R.Field(menu, "narrativeLogMenuGroup") is CanvasGroup group && group != null) group.alpha = 0;
        return true;
    }

    /// <summary>
    /// After the game's own Switch: its line in 回想, from the same reading as the branch box. Branches with no story line
    /// between them (a script checks several at once) share one entry, one row each.
    /// </summary>
    public void Branch(object data, int result)
    {
        if (Mode == 2 || FungusManager.Instance?.NarrativeLog is not NarrativeLog log || log == null) return;
        // The branch runs in the latest script the story moved to; its start is what the story replays from.
        var start = starts.Count > 0 ? starts[starts.Count - 1] : null;
        var flags = Conditions.Flags(data).Select(flag => new Flag { Id = flag.name, Name = L.FlagName(flag), Value = flag.State }).ToList();
        var last = lines.Count > 0 ? lines[lines.Count - 1] : null;
        var history = History(log)?.entries;
        if (last != null && last.Start == start && history is { Count: > 0 } && ReferenceEquals(history[history.Count - 1], last.Entry))
        {
            last.Rows.Add(BranchNotice.LogBody(data, result));
            foreach (var flag in flags) if (!last.Flags.Exists(known => known.Id == flag.Id)) last.Flags.Add(flag);
            Compose(last);
            if (MenuField?.GetValue(null) is NarrativeLogMenu menu && menu != null) Rebuild?.Invoke(menu, null);
            return;
        }
        var line = new Line { Start = start, Flags = flags, Entry = new NarrativeLogEntry { name = L.Text("赵活系统") } };
        line.Rows.Add(BranchNotice.LogBody(data, result));
        Compose(line);
        lines.Add(line);
        if (lines.Count > KeptLines) lines.RemoveAt(0);
        log.AddLine(line.Entry);
    }
    private void Compose(Line line)
    {
        line.Branch = "<size=24>" + (line.Rows.Count == 1 ? L.Format("剧情分支：{0}", line.Rows[0]) : L.Format("剧情分支：{0}", "") + string.Concat(line.Rows.Select(row => "\n　· " + row))) + "</size>";
        line.Back = line.Start != null ? "【" + L.Text("回到这段开头") + "】" : "";
        line.Source = line.Flags.Count > 0 ? "【" + L.Text("决定它的选项") + "】" : "";
        string shown = string.Join("　", new[] { line.Back, line.Source }.Where(text => text.Length > 0));
        if (Mode == 0)
        {
            // 简略 (F106): one small muted line, no name line; the first branch only, cut short.
            line.Entry.name = "";
            string head = "<color=" + MutedColor + ">◇ " + (line.Rows.Count > 1 ? L.Format("分支×{0}：", line.Rows.Count) : L.Text("分支：")) + "</color>";
            string summary = "<size=21>" + head + Short(line.Rows[0]) + (line.Rows.Count > 1 ? "<color=" + MutedColor + ">" + L.Text(" 等") + "</color>" : "") + "</size>";
            line.Summary = summary.Length;
            line.Entry.text = summary + (shown.Length > 0 ? "<size=21><color=" + LinkColor + ">　" + shown + "</color></size>" : "");
            return;
        }
        line.Entry.name = L.Text("赵活系统"); line.Summary = 0;
        line.Entry.text = line.Branch + (shown.Length > 0 ? "\n<size=24><color=" + LinkColor + ">" + shown + "</color></size>" : "");
    }
    // A branch row cut to its first characters, in its first colour.
    private static string Short(string row)
    {
        string plain = Regex.Replace(row, "</?(size|color)[^>]*>", "");
        if (plain.Length <= SummaryChars) return row;
        var color = Regex.Match(row, "<color=([^>]+)>");
        string cut = plain.Substring(0, char.IsHighSurrogate(plain[SummaryChars - 1]) ? SummaryChars - 1 : SummaryChars) + "…";
        return color.Success ? "<color=" + color.Groups[1].Value + ">" + cut + "</color>" : cut;
    }

    /// <summary>设置/回想中的剧情分支 changed: the lines already in 回想 take the new look, or leave it.</summary>
    public void SetMode(int mode)
    {
        if (mode == Mode) return;
        Mode = mode;
        if (FungusManager.Instance?.NarrativeLog is not NarrativeLog log || log == null) { lines.Clear(); return; }
        if (mode == 2) { if (History(log)?.entries is { } entries) foreach (var line in lines) entries.Remove(line.Entry); lines.Clear(); }
        else foreach (var line in lines) Compose(line);
        laidOut = null;
        if (MenuField?.GetValue(null) is NarrativeLogMenu menu && menu != null) Rebuild?.Invoke(menu, null);
    }

    /// <summary>While 回想 is open: the links under the pointer light up and take the click.</summary>
    public void Draw(bool hidden)
    {
        Hover = default;
        if (hidden || lines.Count == 0 || Shown() is not { } open) return;
        var (text, view, menu) = open;
        LayOut(text, menu);
        if (links.Count == 0) return;
        var canvas = text.canvas != null ? text.canvas.rootCanvas : null;
        Camera? camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var port = view.viewport != null ? view.viewport : (RectTransform)view.transform;
        var clip = ToGui(port, port.rect, camera);
        var e = Event.current;
        Link? over = null;
        var lit = new List<Rect>();
        foreach (var link in links)
        {
            var rects = new List<Rect>();
            foreach (var local in link.Local)
            {
                var r = ToGui(text.rectTransform, local, camera);
                r = Rect.MinMaxRect(Mathf.Max(r.xMin, clip.xMin), Mathf.Max(r.yMin, clip.yMin), Mathf.Min(r.xMax, clip.xMax), Mathf.Min(r.yMax, clip.yMax));
                if (r.width > 1 && r.height > 1) rects.Add(r);
            }
            if (over == null && rects.Exists(r => r.Contains(e.mousePosition))) { over = link; lit = rects; }
        }
        if (over == null) return;
        Hover = lit[0];
        if (over.Kind == Kind.Summary) { if (e.type == EventType.Repaint) DrawNote(over.Line, e.mousePosition); return; }
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            e.Use();
            if (over.Kind == Kind.Back) { if (over.Line.Start != null) BackClicked?.Invoke(over.Line.Start); }
            else SourceClicked?.Invoke(Regex.Replace(over.Line.Branch, "</?(size|color)[^>]*>", ""), over.Line.Flags);
            return;
        }
        if (e.type != EventType.Repaint) return;
        glow ??= Solid(new Color(.85f, .69f, .47f, .16f));
        underline ??= Solid(new Color(.85f, .69f, .47f, .9f));
        foreach (var r in lit) { GUI.DrawTexture(r, glow); GUI.DrawTexture(new Rect(r.x, r.yMax - 2, r.width, 2), underline); }
    }

    /// <summary>简略's note under the pointer (F106): every branch of the line with its values, red and green as in 完整.</summary>
    private void DrawNote(Line line, Vector2 pointer)
    {
        float k = UiScale.Current;
        if (noteText == null || noteRevision != L.Revision || Mathf.Abs(noteScale - k) > .01f || noteSkin != OverlaySkin.Key)
        {
            noteScale = k; noteRevision = L.Revision; noteSkin = OverlaySkin.Key;
            if (noteFont != null) UnityEngine.Object.Destroy(noteFont);
            noteFont = Font.CreateDynamicFontFromOSFont(L.FontNames, 16);
            noteText = new GUIStyle { font = OverlaySkin.Font(noteFont), fontSize = Mathf.RoundToInt(15 * k), wordWrap = true, richText = true, alignment = TextAnchor.UpperLeft };
            noteText.normal.textColor = Palette.Text;
        }
        string body = L.Text("剧情分支") + string.Concat(line.Rows.Select(row => "\n· " + Regex.Replace(row, "</?size[^>]*>", "")));
        float width = Mathf.Min(440 * k, Screen.width - 24), pad = 10 * k;
        float height = noteText.CalcHeight(new GUIContent(body), width - pad * 2) + pad * 2 + 4 * k;
        var box = new Rect(Mathf.Clamp(pointer.x + 14 * k, 12, Screen.width - width - 12), Mathf.Clamp(pointer.y + 18 * k, 12, Screen.height - height - 12), width, height);
        var old = GUI.color; int depth = GUI.depth;
        try
        {
            GUI.color = Color.white; GUI.depth = -9998;
            if (!OverlaySkin.Panel(box, k)) GUI.DrawTexture(box, noteFill ??= Solid(new Color(0, 0, 0, .82f)));
            GUI.Label(new Rect(box.x + pad, box.y + pad, width - pad * 2, height - pad * 2), body, noteText);
        }
        finally { GUI.color = old; GUI.depth = depth; }
    }

    private (Text Text, ScrollRect View, NarrativeLogMenu Menu)? Shown()
    {
        if (ActiveField?.GetValue(null) is not true || MenuField?.GetValue(null) is not NarrativeLogMenu menu || menu == null) return null;
        var view = View(menu);
        if (view == null || !view.gameObject.activeInHierarchy || view.content == null) return null;
        var text = view.content.GetComponent<Text>();
        return text != null && text.isActiveAndEnabled ? (text, view, menu) : null;
    }

    /// <summary>The links' rectangles in the log text's own space, worked out again only when its text is laid out anew.</summary>
    private void LayOut(Text text, NarrativeLogMenu menu)
    {
        string shown = text.text ?? "";
        var generator = text.cachedTextGenerator;
        if (ReferenceEquals(shown, laidOut) && generator.characterCount == laidCount) return;
        links = new List<Link>(); laidOut = null;
        if (generator.characterCount == 0 || FungusManager.Instance?.NarrativeLog is not NarrativeLog log || log == null || History(log)?.entries is not { } entries) return;
        int max = MaxField?.GetValue(menu) is int limit ? limit : 1000;
        var starts = NativeUiState.LogTextStarts(entries.Select(entry => new KeyValuePair<string, string>(entry?.name ?? "", entry?.text ?? "")).ToList(), max);
        var index = new Dictionary<NarrativeLogEntry, int>();
        for (int i = 0; i < entries.Count; i++) if (entries[i] != null) index[entries[i]] = i;
        // Older Unity laid out a character for every tag; this one lays out only what is drawn.
        bool tagsLaidOut = generator.characterCount >= shown.Length;
        var chars = generator.GetCharactersArray();
        var rows = generator.GetLinesArray();
        float ppu = text.pixelsPerUnit > 0 ? text.pixelsPerUnit : 1;
        foreach (var line in lines)
        {
            if (!index.TryGetValue(line.Entry, out int i) || starts[i] < 0) continue;
            var spans = new List<(int From, int To, Kind Kind)>();
            foreach (var (label, kind) in new[] { (line.Back, Kind.Back), (line.Source, Kind.Source) })
            {
                int at = label.Length == 0 ? -1 : line.Entry.text.IndexOf(label, StringComparison.Ordinal);
                if (at < 0) continue;
                int raw = starts[i] + at;
                if (raw + label.Length > shown.Length || string.CompareOrdinal(shown, raw, label, 0, label.Length) != 0) continue;
                spans.Add((raw, raw + label.Length, kind));
            }
            // 简略's summary: the note under the pointer (F106).
            if (line.Summary > 0 && starts[i] + line.Summary <= shown.Length) spans.Add((starts[i], starts[i] + line.Summary, Kind.Summary));
            foreach (var (from, to, kind) in spans)
            {
                int first = tagsLaidOut ? from : NativeUiState.DrawnIndex(shown, from), end = tagsLaidOut ? to : NativeUiState.DrawnIndex(shown, to);
                var link = new Link { Line = line, Kind = kind };
                // One rectangle for each laid-out row the span takes.
                int row = -1; float left = 0, right = 0;
                for (int c = first; c < end && c < chars.Length; c++)
                {
                    int r = RowOf(rows, c);
                    if (r != row) { if (row >= 0) link.Local.Add(RowRect(rows[row], left, right, ppu)); row = r; left = chars[c].cursorPos.x; }
                    right = chars[c].cursorPos.x + chars[c].charWidth;
                }
                if (row >= 0) link.Local.Add(RowRect(rows[row], left, right, ppu));
                if (link.Local.Count > 0) links.Add(link);
            }
        }
        laidOut = shown; laidCount = generator.characterCount;
    }
    private static int RowOf(UILineInfo[] rows, int character)
    {
        int found = 0;
        for (int i = 0; i < rows.Length && rows[i].startCharIdx <= character; i++) found = i;
        return found;
    }
    // The generator works in pixels from the text's pivot, y up.
    private static Rect RowRect(UILineInfo row, float left, float right, float ppu) => new(left / ppu, (row.topY - row.height) / ppu, (right - left) / ppu, row.height / ppu);
    private static Rect ToGui(RectTransform transform, Rect local, Camera? camera)
    {
        Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, transform.TransformPoint(new Vector3(local.xMin, local.yMin)));
        Vector2 b = RectTransformUtility.WorldToScreenPoint(camera, transform.TransformPoint(new Vector3(local.xMax, local.yMax)));
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Screen.height - Mathf.Max(a.y, b.y), Mathf.Max(a.x, b.x), Screen.height - Mathf.Min(a.y, b.y));
    }
    private static Texture2D Solid(Color color)
    {
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false); texture.SetPixel(0, 0, color); texture.Apply(); return texture;
    }
    public void Dispose()
    {
        foreach (var texture in new[] { glow, underline, noteFill }) if (texture != null) UnityEngine.Object.Destroy(texture);
        if (noteFont != null) UnityEngine.Object.Destroy(noteFont);
    }
}
