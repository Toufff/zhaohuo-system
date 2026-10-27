using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using Mortal.Core;
using Mortal.Free;
using Mortal.Story;
using UnityEngine;
using UnityEngine.UI;

namespace ZhaoHuoSystem;

/// <summary>
/// 回想 (F101): the game's log of the story gets a line for each condition branch (the arm taken, with its condition's
/// values), and under it up to two links: 回到这段开头 (the script the branch ran in, from its start with the values it
/// started with, written into a slot and loaded as 跳转 does) and 决定它的选项 (the options in the scripts that set the flags
/// the branch reads). F106: by default each line is one small summary, its full conditions in a note under the pointer;
/// 设置/回想中的剧情分支 also offers the full lines or none.
/// From 1.0.6001 the log is the game's StoryLogPanel over StoryLogData: each line is one of its text entries (shown, never
/// saved, gone on a load), drawn by an entry view of its own; the links are found in that view's text by character and
/// drawn over by the mod, which takes their clicks. The log now outlives a scene, and its lines keep their starts with it.
/// </summary>
internal sealed class StoryLog : IDisposable
{
    /// <summary>A story script's start: what 回到这段开头 writes.</summary>
    internal sealed class Start { public string Script = "", Title = "", Music = "", Env = ""; public GameSave Save = null!; }
    /// <summary>A flag the branch reads, with its value then.</summary>
    internal sealed class Flag { public string Id = "", Name = ""; public int Value; }
    private sealed class Line
    {
        public StoryLogEntry? Entry;
        public Start? Start;
        public List<Flag> Flags = new();
        /// <summary>Each branch taken between two of the story's lines.</summary>
        public List<string> Rows = new();
        public string Text = "", Branch = "", Back = "", Source = "";
        /// <summary>Raw length of the summary at the start of the text (简略), the part the note shows under the pointer.</summary>
        public int Summary;
        // The links in the text as its view last laid it out.
        public string? LaidOut;
        public int LaidCount;
        public List<Link> Links = new();
    }
    private enum Kind { Back, Source, Summary }
    private sealed class Link { public Line Line = null!; public Kind Kind; public List<Rect> Local = new(); }
    /// <summary>设置/回想中的剧情分支 (F106): 0 简略, 1 完整, 2 不显示.</summary>
    public int Mode;

    private const int KeptStarts = 40, KeptLines = 300;
    private const string LinkColor = "#D9B077", MutedColor = "#A39580";
    // A summary's first condition, cut to this many characters.
    private const int SummaryChars = 18;
    // The lines' size against the log's own text: 简略 small, 完整 a little smaller.
    private const float SummarySize = .75f, FullSize = .85f;
    /// <summary>The entry each of the log's views last showed.</summary>
    private static readonly Dictionary<StoryLogEntryView, StoryLogEntry> shown = new();
    private static StoryLog? current;
    private readonly List<Start> starts = new();
    private readonly List<Line> lines = new();
    private readonly Dictionary<StoryLogEntry, Line> byEntry = new();
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
    public Action<string, List<Flag>, Start?>? SourceClicked;

    public StoryLog() { current = this; }

    /// <summary>From SegmentSaves, as a story script starts with its copy at hand.</summary>
    public void Kept(string script, GameSave save, string music, string env)
    {
        var node = new StoryReview.Node { Script = script, Year = save.CurrentYear, Month = save.CurrentMonth, Stage = (int)save.CurrentStage, Mission = StoryReview.MissionNow() };
        starts.Add(new Start { Script = script, Save = save, Music = music, Env = env, Title = StoryReview.Title(node) });
        if (starts.Count > KeptStarts) starts.RemoveAt(0);
    }

    /// <summary>A scene change: its starts and views go; the lines stay in the log with the starts they ran in.</summary>
    public void Clear() { starts.Clear(); shown.Clear(); Hover = default; }

    /// <summary>The scene's log panel: the story's or the free map's.</summary>
    private static StoryLogPanel? Panel()
    {
        object? owner = StoryManager.Instance is StoryManager story && story != null ? story : FreeManager.Instance is FreeManager free && free != null ? free : null;
        return R.Field(owner, "_storyLogPanel") is StoryLogPanel panel && panel != null ? panel : null;
    }

    /// <summary>The game's 回想 is open (it covers the story and its menus).</summary>
    public static bool Open => Panel()?.IsOpen == true;

    /// <summary>Before a jump from its links the log is closed the game's way (its close puts the story's state back).</summary>
    public static void CloseForJump()
    {
        if (Panel() is StoryLogPanel panel && panel.IsOpen) panel.Close();
    }

    private static void Refresh()
    {
        if (Panel() is StoryLogPanel panel && panel.IsOpen) panel.Refresh();
    }

    /// <summary>
    /// After the game's own Switch: its line in 回想, from the same reading as the branch box. Branches with no story line
    /// between them (a script checks several at once) share one entry, one row each.
    /// </summary>
    public void Branch(object data, int result)
    {
        if (Mode == 2) return;
        // The branch runs in the latest script the story moved to; its start is what the story replays from.
        var start = starts.Count > 0 ? starts[starts.Count - 1] : null;
        var flags = Conditions.Flags(data).Select(flag => new Flag { Id = flag.name, Name = L.FlagName(flag), Value = flag.State }).ToList();
        var entries = StoryLogData.Entries;
        // Lines whose entries left the log (a load, or past its 300) go too.
        if (lines.Count > 0)
        {
            var present = new HashSet<StoryLogEntry>(entries);
            lines.RemoveAll(line => line.Entry == null || !present.Contains(line.Entry));
            byEntry.Clear();
            foreach (var kept in lines) byEntry[kept.Entry!] = kept;
        }
        var last = lines.Count > 0 ? lines[lines.Count - 1] : null;
        if (last != null && last.Start == start && entries.Count > 0 && ReferenceEquals(entries[entries.Count - 1], last.Entry))
        {
            last.Rows.Add(BranchNotice.LogBody(data, result));
            foreach (var flag in flags) if (!last.Flags.Exists(known => known.Id == flag.Id)) last.Flags.Add(flag);
            Compose(last);
            Refresh();
            return;
        }
        var line = new Line { Start = start, Flags = flags };
        line.Rows.Add(BranchNotice.LogBody(data, result));
        Compose(line);
        StoryLogData.AddText(line.Text, isSave: false);
        if (entries.Count == 0 || !ReferenceEquals(entries[entries.Count - 1].Text, line.Text)) return;
        line.Entry = entries[entries.Count - 1];
        lines.Add(line);
        byEntry[line.Entry] = line;
        if (lines.Count > KeptLines) { byEntry.Remove(lines[0].Entry!); lines.RemoveAt(0); }
        Refresh();
    }
    private void Compose(Line line)
    {
        line.Branch = line.Rows.Count == 1 ? L.Format("剧情分支：{0}", line.Rows[0]) : L.Format("剧情分支：{0}", "") + string.Concat(line.Rows.Select(row => "\n　· " + row));
        line.Back = line.Start != null ? "【" + L.Text("回到这段开头") + "】" : "";
        line.Source = line.Flags.Count > 0 ? "【" + L.Text("决定它的选项") + "】" : "";
        string links = string.Join("　", new[] { line.Back, line.Source }.Where(text => text.Length > 0));
        if (Mode == 0)
        {
            // 简略 (F106): one small muted line; the first branch only, cut short.
            string head = "<color=" + MutedColor + ">◇ " + (line.Rows.Count > 1 ? L.Format("分支×{0}：", line.Rows.Count) : L.Text("分支：")) + "</color>";
            string summary = head + Short(line.Rows[0]) + (line.Rows.Count > 1 ? "<color=" + MutedColor + ">" + L.Text(" 等") + "</color>" : "");
            line.Summary = summary.Length;
            line.Text = summary + (links.Length > 0 ? "<color=" + LinkColor + ">　" + links + "</color>" : "");
        }
        else
        {
            line.Summary = 0;
            line.Text = line.Branch + (links.Length > 0 ? "\n<color=" + LinkColor + ">" + links + "</color>" : "");
        }
        if (line.Entry != null) line.Entry.Text = line.Text;
        line.LaidOut = null;
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
        if (mode == 2)
        {
            // Hidden as the log hides a story line with no text, never taken out of its list, in which the game's save
            // keeps a place by count.
            foreach (var line in lines) if (line.Entry != null) { line.Entry.Type = StoryLogType.Story; line.Entry.Key = ""; line.Entry.Text = ""; }
            lines.Clear(); byEntry.Clear();
        }
        else foreach (var line in lines) Compose(line);
        Refresh();
    }

    /// <summary>A view of the log was set up to show an entry: one of these lines takes the log's own text colour, smaller.</summary>
    internal static void ViewSetUp(StoryLogEntryView view, StoryLogEntry entry)
    {
        shown[view] = entry;
        if (current == null || !current.byEntry.TryGetValue(entry, out var line) || R.Field(view, "_contentText") is not Text text || text == null) return;
        if (R.Field(view, "_normalTextColor") is Color normal) text.color = normal;
        text.fontSize = Mathf.Max(1, Mathf.RoundToInt(text.fontSize * (line.Summary > 0 ? SummarySize : FullSize)));
    }

    /// <summary>While 回想 is open: the links under the pointer light up and take the click.</summary>
    public void Draw(bool hidden)
    {
        Hover = default;
        if (hidden || lines.Count == 0 || Panel() is not StoryLogPanel panel || !panel.IsOpen) return;
        if (R.Field(panel, "_scrollRect") is not ScrollRect view || view == null || R.Field(panel, "_entryViews") is not List<StoryLogEntryView> views) return;
        var port = view.viewport != null ? view.viewport : (RectTransform)view.transform;
        var canvas = port.GetComponentInParent<Canvas>();
        if (canvas != null) canvas = canvas.rootCanvas;
        Camera? camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var clip = ToGui(port, port.rect, camera);
        var e = Event.current;
        Link? over = null;
        var lit = new List<Rect>();
        foreach (var entryView in views)
        {
            if (entryView == null || !entryView.gameObject.activeInHierarchy || !shown.TryGetValue(entryView, out var entry) || !byEntry.TryGetValue(entry, out var line)) continue;
            if (R.Field(entryView, "_contentText") is not Text text || text == null || !text.isActiveAndEnabled) continue;
            // Out of the view's sight it is neither laid out nor hit.
            if (!ToGui(text.rectTransform, text.rectTransform.rect, camera).Overlaps(clip)) continue;
            LayOut(line, text);
            foreach (var link in line.Links)
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
        }
        if (over == null) return;
        Hover = lit[0];
        if (over.Kind == Kind.Summary) { if (e.type == EventType.Repaint) DrawNote(over.Line, e.mousePosition); return; }
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            e.Use();
            if (over.Kind == Kind.Back) { if (over.Line.Start != null) BackClicked?.Invoke(over.Line.Start); }
            else SourceClicked?.Invoke(Regex.Replace(over.Line.Branch, "</?(size|color)[^>]*>", ""), over.Line.Flags, over.Line.Start);
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

    /// <summary>The line's links in its text's own space, worked out again only when the text is laid out anew.</summary>
    private static void LayOut(Line line, Text text)
    {
        string laid = text.text ?? "";
        var generator = text.cachedTextGenerator;
        if (ReferenceEquals(laid, line.LaidOut) && generator.characterCount == line.LaidCount) return;
        line.Links = new List<Link>(); line.LaidOut = null;
        if (generator.characterCount == 0 || laid != line.Text) return;
        var spans = new List<(int From, int To, Kind Kind)>();
        foreach (var (label, kind) in new[] { (line.Back, Kind.Back), (line.Source, Kind.Source) })
        {
            int at = label.Length == 0 ? -1 : laid.IndexOf(label, StringComparison.Ordinal);
            if (at >= 0) spans.Add((at, at + label.Length, kind));
        }
        // 简略's summary: the note under the pointer (F106).
        if (line.Summary > 0 && line.Summary <= laid.Length) spans.Add((0, line.Summary, Kind.Summary));
        // Older Unity laid out a character for every tag; this one lays out only what is drawn.
        bool tagsLaidOut = generator.characterCount >= laid.Length;
        var chars = generator.GetCharactersArray();
        var rows = generator.GetLinesArray();
        float ppu = text.pixelsPerUnit > 0 ? text.pixelsPerUnit : 1;
        foreach (var (from, to, kind) in spans)
        {
            int first = tagsLaidOut ? from : NativeUiState.DrawnIndex(laid, from), end = tagsLaidOut ? to : NativeUiState.DrawnIndex(laid, to);
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
            if (link.Local.Count > 0) line.Links.Add(link);
        }
        line.LaidOut = laid; line.LaidCount = generator.characterCount;
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
        if (current == this) current = null;
        shown.Clear();
        foreach (var texture in new[] { glow, underline, noteFill }) if (texture != null) UnityEngine.Object.Destroy(texture);
        if (noteFont != null) UnityEngine.Object.Destroy(noteFont);
    }
}

// Which entry each view of the log shows, and the look of these lines in it.
[HarmonyPatch(typeof(StoryLogEntryView), nameof(StoryLogEntryView.Setup))]
internal static class StoryLogViewPatch { private static void Postfix(StoryLogEntryView __instance, StoryLogEntry entry) => StoryLog.ViewSetUp(__instance, entry); }
