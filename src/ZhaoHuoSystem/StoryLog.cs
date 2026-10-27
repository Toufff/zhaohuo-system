using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
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
/// Build 26237817 (1.0.6001) rebuilt 回想: Mortal.Core.StoryLogData keeps the entries (across scenes, and in saves), and
/// StoryLogPanel shows each in its own StoryLogEntryView, in the story and on the free map. Which log the game has decides
/// which one gets the lines; the new one is reached by reflection, so the mod still loads on builds without it. Its lines
/// are text entries, which the game never writes into a save.
/// </summary>
internal sealed class StoryLog : IDisposable
{
    /// <summary>A story script's start in this scene: what 回到这段开头 writes.</summary>
    internal sealed class Start { public string Script = "", Title = "", Music = "", Env = ""; public GameSave Save = null!; }
    /// <summary>A flag the branch reads, with its value then.</summary>
    internal sealed class Flag { public string Id = "", Name = ""; public int Value; }
    private sealed class Line
    {
        /// <summary>The game's entry: a Fungus NarrativeLogEntry, or a StoryLogEntry on the rebuilt log.</summary>
        public object Entry = null!;
        /// <summary>The entry's name line and text as composed; <see cref="Written"/> is the text as the entry holds it.</summary>
        public string Name = "", Text = "", Written = "";
        public Start? Start;
        public List<Flag> Flags = new();
        /// <summary>Each branch taken between two of the story's lines.</summary>
        public List<string> Rows = new();
        public string Branch = "", Back = "", Source = "";
        /// <summary>Raw length of the summary at the start of the entry's text (简略), the part the note shows under the pointer.</summary>
        public int Summary;
    }
    private enum Kind { Back, Source, Summary }
    private sealed class Link { public Line Line = null!; public Kind Kind; public Text Text = null!; public List<Rect> Local = new(); }
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
    // The rebuilt log (26237817): absent on older builds.
    private static readonly Type? DataType = AccessTools.TypeByName("Mortal.Core.StoryLogData");
    private static readonly FieldInfo? EntriesField = DataType == null ? null : AccessTools.Field(DataType, "_entries");
    private static readonly FieldInfo? RestartField = DataType == null ? null : AccessTools.Field(DataType, "_restartCount");
    private static readonly MethodInfo? AddText = DataType == null ? null : AccessTools.Method(DataType, "AddText");
    private static readonly FieldInfo? EntryText = AccessTools.Field(AccessTools.TypeByName("Mortal.Core.StoryLogEntry"), "Text");
    /// <summary>The game has the rebuilt 回想 (StoryLogData), not Fungus' NarrativeLog.</summary>
    internal static bool Rebuilt => EntriesField != null && RestartField != null && AddText != null && EntryText != null;
    /// <summary>The rebuilt log's panel last filled, and the text each of the mod's lines is shown in.</summary>
    private static Component? panel;
    private readonly Dictionary<Text, Line> placed = new();
    private readonly List<Start> starts = new();
    private readonly List<Line> lines = new();
    private List<Link> links = new();
    private readonly List<(Text Text, string Shown, int Count)> laid = new();
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

    /// <summary>From SegmentSaves, as a story script starts with its copy at hand.</summary>
    public void Kept(string script, GameSave save, string music, string env)
    {
        var node = new StoryReview.Node { Script = script, Year = save.CurrentYear, Month = save.CurrentMonth, Stage = (int)save.CurrentStage, Mission = StoryReview.MissionNow() };
        starts.Add(new Start { Script = script, Save = save, Music = music, Env = env, Title = StoryReview.Title(node) });
        if (starts.Count > KeptStarts) starts.RemoveAt(0);
    }

    /// <summary>A scene change. The rebuilt log outlives it, and its lines with it; their starts stay theirs.</summary>
    public void Clear() { starts.Clear(); if (!Rebuilt) lines.Clear(); placed.Clear(); links.Clear(); laid.Clear(); Hover = default; }

    /// <summary>The game's 回想 is open (it covers the story and its menus).</summary>
    public static bool Open => Rebuilt ? panel != null && R.Property(panel, "IsOpen") is true : ActiveField?.GetValue(null) is true;

    /// <summary>
    /// F104: the game keeps whether 回想 is open in one static switch that each open or close flips. A scene left with 回想
    /// open (a jump from its links) leaves the switch set, and every later 回想 then fades its text out instead of in while
    /// the story pauses: it looks frozen until Esc. Before a jump the log is closed the game's own way; a story scene that
    /// starts with its log closed puts a stray switch back.
    /// </summary>
    public static void CloseForJump()
    {
        if (StoryManager.Instance is StoryManager story && story != null && R.Field(story, "_logOpen") is true && R.Field(story, "_logButton") is Toggle button && button != null) button.isOn = false;
        // The rebuilt log opens on the free map too, where there is no button: its panel closes itself.
        if (Rebuilt && Open) AccessTools.Method(panel!.GetType(), "Close")?.Invoke(panel, null);
    }
    public static bool Repair(StoryManager story)
    {
        // The rebuilt log keeps no static switch.
        if (Rebuilt || ActiveField?.GetValue(null) is not true || R.Field(story, "_logOpen") is true) return false;
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
        if (Mode == 2 || Entries() is not IList history) return;
        Prune(history);
        // The branch runs in the latest script the story moved to; its start is what the story replays from.
        var start = starts.Count > 0 ? starts[starts.Count - 1] : null;
        var flags = Conditions.Flags(data).Select(flag => new Flag { Id = flag.name, Name = L.FlagName(flag), Value = flag.State }).ToList();
        var last = lines.Count > 0 ? lines[lines.Count - 1] : null;
        if (last != null && last.Start == start && history.Count > 0 && ReferenceEquals(history[history.Count - 1], last.Entry))
        {
            last.Rows.Add(BranchNotice.LogBody(data, result));
            foreach (var flag in flags) if (!last.Flags.Exists(known => known.Id == flag.Id)) last.Flags.Add(flag);
            Compose(last);
            Refresh();
            return;
        }
        var line = new Line { Start = start, Flags = flags };
        line.Rows.Add(BranchNotice.LogBody(data, result));
        if (!Rebuilt) line.Entry = new NarrativeLogEntry();
        Compose(line);
        if (Rebuilt)
        {
            AddText!.Invoke(null, new object[] { line.Written, false });
            if (history.Count == 0 || history[history.Count - 1] is not object added || !ReferenceEquals(EntryText!.GetValue(added), line.Written)) return;
            line.Entry = added;
        }
        else if (FungusManager.Instance?.NarrativeLog is NarrativeLog log && log != null) log.AddLine((NarrativeLogEntry)line.Entry);
        else return;
        lines.Add(line);
        if (lines.Count > KeptLines) lines.RemoveAt(0);
    }

    /// <summary>The game's entries: the rebuilt log's, or the Fungus log's history.</summary>
    private static IList? Entries()
    {
        if (Rebuilt) return EntriesField!.GetValue(null) as IList;
        return FungusManager.Instance?.NarrativeLog is NarrativeLog log && log != null ? History(log)?.entries : null;
    }
    /// <summary>Lines whose entry the game dropped (a save loaded, the oldest past its limit) are forgotten.</summary>
    private void Prune(IList entries)
    {
        if (!Rebuilt || lines.Count == 0) return;
        var alive = new HashSet<object>(entries.Cast<object>());
        lines.RemoveAll(line => !alive.Contains(line.Entry));
    }
    /// <summary>The open log shows its entries anew; a closed one is filled as it opens.</summary>
    private static void Refresh()
    {
        if (Rebuilt) { if (Open) AccessTools.Method(panel!.GetType(), "Refresh")?.Invoke(panel, null); }
        else if (MenuField?.GetValue(null) is NarrativeLogMenu menu && menu != null) Rebuild?.Invoke(menu, null);
    }
    /// <summary>Into the game's entry. The rebuilt log's text entries have no name line, so it goes into the text.</summary>
    private static void Write(Line line)
    {
        if (Rebuilt)
        {
            line.Written = line.Name.Length > 0 ? "<color=" + LinkColor + ">" + line.Name + "</color>\n" + line.Text : line.Text;
            if (line.Entry != null) EntryText!.SetValue(line.Entry, line.Written);
            return;
        }
        line.Written = line.Text;
        if (line.Entry is NarrativeLogEntry entry) { entry.name = line.Name; entry.text = line.Text; }
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
            line.Name = "";
            string head = "<color=" + MutedColor + ">◇ " + (line.Rows.Count > 1 ? L.Format("分支×{0}：", line.Rows.Count) : L.Text("分支：")) + "</color>";
            string summary = "<size=21>" + head + Short(line.Rows[0]) + (line.Rows.Count > 1 ? "<color=" + MutedColor + ">" + L.Text(" 等") + "</color>" : "") + "</size>";
            line.Summary = summary.Length;
            line.Text = summary + (shown.Length > 0 ? "<size=21><color=" + LinkColor + ">　" + shown + "</color></size>" : "");
            Write(line);
            return;
        }
        line.Name = L.Text("赵活系统"); line.Summary = 0;
        line.Text = line.Branch + (shown.Length > 0 ? "\n<size=24><color=" + LinkColor + ">" + shown + "</color></size>" : "");
        Write(line);
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
        if (Entries() is not IList entries) { lines.Clear(); return; }
        Prune(entries);
        if (mode == 2) { foreach (var line in lines) Remove(entries, line.Entry); lines.Clear(); }
        else foreach (var line in lines) Compose(line);
        laid.Clear();
        Refresh();
    }
    // The rebuilt log saves its entries up to a restart point counted from the start; one removed before it moves it back.
    private static void Remove(IList entries, object entry)
    {
        int at = entries.IndexOf(entry);
        if (at < 0) return;
        entries.RemoveAt(at);
        if (Rebuilt && RestartField!.GetValue(null) is int restart && at < restart) RestartField.SetValue(null, restart - 1);
    }

    /// <summary>The rebuilt log's panel filled itself: which of its texts shows which of the mod's lines.</summary>
    public void Refreshed(Component shownPanel) { panel = shownPanel; laid.Clear(); }
    /// <summary>One of its entry views set up: its text now shows this entry.</summary>
    public void EntryShown(Component view, object entry)
    {
        if (R.Field(view, "_contentText") is not Text text || text == null) return;
        var line = lines.Find(known => ReferenceEquals(known.Entry, entry));
        if (line != null) placed[text] = line; else placed.Remove(text);
    }

    /// <summary>While 回想 is open: the links under the pointer light up and take the click.</summary>
    public void Draw(bool hidden)
    {
        Hover = default;
        if (hidden || lines.Count == 0 || Shown() is not { } open) return;
        var (view, texts) = open;
        LayOut(texts);
        if (links.Count == 0) return;
        var canvas = texts[0].canvas != null ? texts[0].canvas.rootCanvas : null;
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
                var r = ToGui(link.Text.rectTransform, local, camera);
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

    /// <summary>While 回想 is open: its scroll view and the texts that show the mod's lines.</summary>
    private (ScrollRect View, List<Text> Texts)? Shown()
    {
        if (Rebuilt)
        {
            if (!Open || R.Field(panel, "_scrollRect") is not ScrollRect scroll || scroll == null) return null;
            var texts = placed.Keys.Where(shown => shown != null && shown.isActiveAndEnabled).ToList();
            return texts.Count > 0 ? (scroll, texts) : null;
        }
        if (ActiveField?.GetValue(null) is not true || MenuField?.GetValue(null) is not NarrativeLogMenu menu || menu == null) return null;
        var view = View(menu);
        if (view == null || !view.gameObject.activeInHierarchy || view.content == null) return null;
        var text = view.content.GetComponent<Text>();
        return text != null && text.isActiveAndEnabled ? (view, new List<Text> { text }) : null;
    }

    /// <summary>The links' rectangles in each text's own space, worked out again only when a text is laid out anew.</summary>
    private void LayOut(List<Text> texts)
    {
        bool same = laid.Count == texts.Count;
        for (int i = 0; same && i < texts.Count; i++)
            same = laid[i].Text == texts[i] && ReferenceEquals(laid[i].Shown, texts[i].text) && laid[i].Count == texts[i].cachedTextGenerator.characterCount;
        if (same) return;
        links = new List<Link>(); laid.Clear();
        foreach (var text in texts)
        {
            string shown = text.text ?? "";
            var generator = text.cachedTextGenerator;
            laid.Add((text, shown, generator.characterCount));
            if (generator.characterCount > 0) foreach (var (line, start) in Placed(text)) LayOut(text, shown, line, start);
        }
    }
    /// <summary>The mod's lines in a text, each with where its entry's text starts in it.</summary>
    private IEnumerable<(Line Line, int Start)> Placed(Text text)
    {
        if (Rebuilt)
        {
            // An entry view's text is its entry's text.
            if (placed.TryGetValue(text, out var line)) yield return (line, 0);
            yield break;
        }
        if (FungusManager.Instance?.NarrativeLog is not NarrativeLog log || log == null || History(log)?.entries is not { } entries) yield break;
        int max = MenuField?.GetValue(null) is NarrativeLogMenu menu && menu != null && MaxField?.GetValue(menu) is int limit ? limit : 1000;
        var starts = NativeUiState.LogTextStarts(entries.Select(entry => new KeyValuePair<string, string>(entry?.name ?? "", entry?.text ?? "")).ToList(), max);
        var index = new Dictionary<NarrativeLogEntry, int>();
        for (int i = 0; i < entries.Count; i++) if (entries[i] != null) index[entries[i]] = i;
        foreach (var line in lines)
            if (line.Entry is NarrativeLogEntry entry && index.TryGetValue(entry, out int i) && starts[i] >= 0) yield return (line, starts[i]);
    }
    private void LayOut(Text text, string shown, Line line, int start)
    {
        var generator = text.cachedTextGenerator;
        // Older Unity laid out a character for every tag; this one lays out only what is drawn.
        bool tagsLaidOut = generator.characterCount >= shown.Length;
        var chars = generator.GetCharactersArray();
        var rows = generator.GetLinesArray();
        float ppu = text.pixelsPerUnit > 0 ? text.pixelsPerUnit : 1;
        var spans = new List<(int From, int To, Kind Kind)>();
        foreach (var (label, kind) in new[] { (line.Back, Kind.Back), (line.Source, Kind.Source) })
        {
            int at = label.Length == 0 ? -1 : line.Written.IndexOf(label, StringComparison.Ordinal);
            if (at < 0) continue;
            int raw = start + at;
            if (raw + label.Length > shown.Length || string.CompareOrdinal(shown, raw, label, 0, label.Length) != 0) continue;
            spans.Add((raw, raw + label.Length, kind));
        }
        // 简略's summary: the note under the pointer (F106).
        if (line.Summary > 0 && start + line.Summary <= shown.Length) spans.Add((start, start + line.Summary, Kind.Summary));
        foreach (var (from, to, kind) in spans)
        {
            int first = tagsLaidOut ? from : NativeUiState.DrawnIndex(shown, from), end = tagsLaidOut ? to : NativeUiState.DrawnIndex(shown, to);
            var link = new Link { Line = line, Kind = kind, Text = text };
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

// The rebuilt 回想 (26237817): which panel is open and which of its views shows which entry. Absent on older builds.
[HarmonyPatch]
internal static class StoryLogPanelPatch
{
    private static bool Prepare() => StoryLog.Rebuilt;
    private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("Mortal.Core.StoryLogPanel"), "Refresh");
    private static void Postfix(Component __instance) => Plugin.Instance?.StoryLogShown(__instance);
}
[HarmonyPatch]
internal static class StoryLogEntryViewPatch
{
    private static bool Prepare() => StoryLog.Rebuilt;
    private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("Mortal.Core.StoryLogEntryView"), "Setup");
    private static void Postfix(Component __instance, object entry) => Plugin.Instance?.StoryLogShown(__instance, entry);
}
