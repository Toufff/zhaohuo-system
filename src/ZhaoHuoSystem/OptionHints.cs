using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Logging;
using Mortal.Core;
using Mortal.Story;
using OBB.Framework.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ZhaoHuoSystem;

/// <summary>
/// Appends short consequence brackets to native option and dice rows. Reads script text and condition assets only:
/// never runs Switch/Condition/Execute, never touches the option source text or game state.
/// </summary>
internal sealed class OptionHints : IDisposable
{
    private readonly ManualLogSource log;
    private readonly List<KeyValuePair<string, Dictionary<string, OptionPreview>>> recent = new();
    private readonly Dictionary<string, OptionPreview?> nextScripts = new(StringComparer.OrdinalIgnoreCase);
    public OptionHints(ManualLogSource log) => this.log = log;
    /// <summary>Hidden-overlay mode: no brackets or shop lines are added to native text.</summary>
    public bool Suppressed;
    /// <summary>Titles of one-shot events that can run now behind an option's pool or next script (F43).</summary>
    public Func<string, string, List<string>>? ReadyOneShots;
    // Each option's full consequences go to the session log once per menu, so a report can be matched to what was shown.
    private readonly HashSet<string> logged = new(StringComparer.Ordinal);
    public void MenuStarted() => logged.Clear();

    /// <summary>Called with each script's original text before the mod instruments it.</summary>
    public void Load(string source, string friendlyName)
    {
        try
        {
            var map = LuaOptions.Analyze(source);
            if (map.Count <= 1) return;
            string name = friendlyName ?? "";
            name = name.Substring(name.LastIndexOfAny(new[] { '/', '\\' }) + 1);
            recent.Insert(0, new(name, map));
            if (recent.Count > 3) recent.RemoveAt(recent.Count - 1);
        }
        catch (Exception ex) { log.LogWarning("OPTION_HINT analyze " + ex.GetBaseException().Message); }
    }

    /// <summary>
    /// Option and rest rows: the bracket is an overview (conditions as 「若…」, flags counted). It stays on the title line
    /// when it fits, otherwise takes its own line and is cut to the room left of the row's own number columns.
    /// Hovering a row whose bracket is abridged or cut shows every condition and effect.
    /// </summary>
    public void Decorate(Component row, Text? source, Text[]? texts)
    {
        Forget(row);
        if (Suppressed || texts == null || texts.Length == 0) return;
        var detail = Segments(source?.text);
        if (detail == null) return;
        var segments = Inline(detail);
        var own = new HashSet<Component>(texts.Where(text => text != null)); if (source != null) own.Add(source);
        bool cut = false;
        foreach (var text in texts)
        {
            if (text == null) continue;
            if (detail.Exists(segment => segment.OneShot)) text.text = Colorize(Tone.Mark(1, Dot), text.color, text.supportRichText) + " " + text.text;
            string size = "<size=" + Mathf.Max(10, Mathf.RoundToInt(text.fontSize * .8f)) + ">";
            string inline = " " + Render(segments, text.color, text.supportRichText, size);
            try
            {
                float available = Available(row, text.rectTransform, own);
                if (Width(text, text.text + inline) <= available) { text.text += inline; continue; }
                var fit = NativeUiState.FitSegments(segments, list => Width(text, Render(list, text.color, text.supportRichText, size)), available, out bool shortened);
                cut |= shortened;
                text.text += "\n" + Render(fit, text.color, text.supportRichText, size);
            }
            catch (Exception ex) { log.LogWarning("OPTION_HINT fit " + ex.GetBaseException().Message); text.text += inline; }
        }
        Remember(row, detail, cut);
    }
    /// <summary>Dice rows keep the bracket on the option's single line.</summary>
    public void Decorate(Component row, Text? source, params TMP_Text?[] texts)
    {
        Forget(row);
        if (Suppressed) return;
        var detail = Segments(source?.text);
        if (detail == null) return;
        var segments = Inline(detail);
        var own = new HashSet<Component>(texts.Where(text => text != null)!); if (source != null) own.Add(source);
        bool cut = false;
        foreach (var text in texts)
        {
            if (text == null) continue;
            if (detail.Exists(segment => segment.OneShot)) text.text = Colorize(Tone.Mark(1, Dot), text.color, text.richText) + " " + text.text;
            try
            {
                float available = Available(row, text.rectTransform, own) - text.GetPreferredValues(text.text + " ").x;
                var fit = NativeUiState.FitSegments(segments, list => text.GetPreferredValues(Render(list, text.color, text.richText, "<size=80%>")).x, available, out bool shortened);
                cut |= shortened;
                text.text += " " + Render(fit, text.color, text.richText, "<size=80%>");
            }
            catch (Exception ex) { log.LogWarning("OPTION_HINT fit " + ex.GetBaseException().Message); text.text += " " + Render(segments, text.color, text.richText, "<size=80%>"); }
        }
        Remember(row, detail, cut);
    }
    private static List<KeyValuePair<string, int>> Inline(List<HintSegment> detail) => detail.ConvertAll(segment => new KeyValuePair<string, int>(segment.Summary, segment.State));

    private static float Width(Text text, string value) =>
        text.cachedTextGeneratorForLayout.GetPreferredWidth(value, text.GetGenerationSettings(Vector2.zero)) / text.pixelsPerUnit;
    /// <summary>Room from the text's left edge to the nearest other visible label on its right (cost / count columns).</summary>
    private static float Available(Component row, RectTransform text, HashSet<Component> own)
    {
        var bounds = text.rect; float right = bounds.xMax;
        var corners = new Vector3[4];
        foreach (var graphic in row.GetComponentsInChildren<Graphic>())
        {
            if (own.Contains(graphic) || !graphic.enabled || graphic.color.a <= .01f) continue;
            string content; float preferred; var anchor = TextAnchor.MiddleLeft;
            if (graphic is Text label) { content = label.text; preferred = label.preferredWidth; anchor = label.alignment; }
            else if (graphic is TMP_Text tmp) { content = tmp.text; preferred = tmp.preferredWidth; }
            else continue;
            if (string.IsNullOrWhiteSpace(content)) continue;
            graphic.rectTransform.GetWorldCorners(corners);
            Vector3 min = text.InverseTransformPoint(corners[0]), max = text.InverseTransformPoint(corners[2]);
            if (max.y < bounds.yMin || min.y > bounds.yMax) continue;
            float local = graphic.rectTransform.rect.width, width = preferred * (local > 0 ? (max.x - min.x) / local : 1);
            float left = anchor switch
            {
                TextAnchor.UpperCenter or TextAnchor.MiddleCenter or TextAnchor.LowerCenter => (min.x + max.x - width) / 2,
                TextAnchor.UpperRight or TextAnchor.MiddleRight or TextAnchor.LowerRight => max.x - width,
                _ => min.x
            };
            if (left > bounds.xMin + 1 && left < right) right = left;
        }
        return right - bounds.xMin - 12;
    }

    private sealed class Hint { public RectTransform Row = null!; public List<HintSegment> Segments = null!; }
    private readonly List<Hint> hints = new();
    private Font? tipFont;
    private Texture2D? tipBackground;
    private GUIStyle? tipStyle;
    private long tipRevision = -1;
    private float tipScale = -1;
    private void Forget(Component row) => hints.RemoveAll(hint => hint.Row == null || hint.Row == row.transform);
    private void Remember(Component row, List<HintSegment> segments, bool cut)
    {
        // Nothing to add when the inline bracket already says everything.
        if ((cut || segments.Exists(segment => segment.Full != segment.Summary)) && row.transform is RectTransform rect)
            hints.Add(new Hint { Row = rect, Segments = segments });
    }
    /// <summary>
    /// Full consequences of the row under the pointer: each condition on its own coloured line, its effects indented
    /// below after 「→」, a small gap between segments. Draw-only, never consumes input.
    /// </summary>
    public void DrawTooltip(bool blocked)
    {
        if (Suppressed || blocked || hints.Count == 0 || Event.current.type != EventType.Repaint) return;
        hints.RemoveAll(hint => hint.Row == null);
        var pointer = Event.current.mousePosition; var screen = new Vector2(pointer.x, Screen.height - pointer.y);
        Hint? shown = null;
        foreach (var hint in hints)
        {
            if (!hint.Row.gameObject.activeInHierarchy) continue;
            var canvas = hint.Row.GetComponentInParent<Canvas>()?.rootCanvas;
            var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (RectTransformUtility.RectangleContainsScreenPoint(hint.Row, screen, camera)) { shown = hint; break; }
        }
        if (shown == null) return;
        float k = UiScale.Current;
        if (tipStyle == null || tipRevision != L.Revision || Mathf.Abs(tipScale - k) > .01f)
        {
            tipScale = k;
            if (tipFont != null) UnityEngine.Object.Destroy(tipFont);
            tipFont = Font.CreateDynamicFontFromOSFont(L.FontNames, 16);
            if (tipBackground == null) { tipBackground = new Texture2D(1, 1, TextureFormat.RGBA32, false); tipBackground.SetPixel(0, 0, new Color(0, 0, 0, .78f)); tipBackground.Apply(); }
            tipStyle = new GUIStyle { font = tipFont, fontSize = Mathf.RoundToInt(15 * k), alignment = TextAnchor.UpperLeft, wordWrap = true, richText = true };
            tipRevision = L.Revision;
        }
        float indent = 18 * k, gap = 7 * k;
        float width = Mathf.Min(440 * k, Screen.width - 24), inner = width - 20 * k, height = 16 * k;
        var lines = new List<(string Text, bool Effects, int State, bool Gap)>();
        for (int i = 0; i < shown.Segments.Count; i++)
        {
            var segment = shown.Segments[i];
            // Marks colour the condition (holds / fails) and each change (rises / falls) with the panel shades.
            lines.Add((Colorize(segment.Head, Color.white, true), false, segment.State, i > 0));
            if (segment.Body.Length > 0) lines.Add((Colorize(segment.Body, Color.white, true), true, 0, false));
        }
        var heights = lines.ConvertAll(line => Mathf.Max(20 * k, tipStyle.CalcHeight(new GUIContent(line.Text), line.Effects ? inner - indent : inner)));
        for (int i = 0; i < lines.Count; i++) height += heights[i] + (lines[i].Gap ? gap : 0);
        height = Mathf.Min(Screen.height - 24, height);
        var box = new Rect(Mathf.Clamp(pointer.x + 14 * k, 12, Screen.width - width - 12), Mathf.Clamp(pointer.y + 18 * k, 12, Screen.height - height - 12), width, height);
        var oldColor = GUI.color; int oldDepth = GUI.depth;
        try
        {
            GUI.color = Color.white; GUI.depth = -9998;
            GUI.DrawTexture(box, tipBackground!);
            GUI.BeginGroup(box);
            try
            {
                float y = 8 * k;
                for (int i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    if (line.Gap) y += gap;
                    tipStyle.normal.textColor = line.Effects ? Palette.Effects : Palette.Text;
                    float x = line.Effects ? 10 * k + indent : 10 * k;
                    GUI.Label(new Rect(x, y, inner - (x - 10 * k), heights[i]), line.Text, tipStyle); y += heights[i];
                }
            }
            finally { GUI.EndGroup(); }
        }
        finally { GUI.color = oldColor; GUI.depth = oldDepth; }
    }
    public void Dispose()
    {
        if (tipFont != null) UnityEngine.Object.Destroy(tipFont);
        if (tipBackground != null) UnityEngine.Object.Destroy(tipBackground);
    }

    private List<HintSegment>? Segments(string? source)
    {
        try
        {
            string key = LuaOptions.OptionKey(source ?? "");
            if (key.Length == 0) return null;
            var preview = Resolve(key);
            if (preview == null) return null;
            var segments = LuaOptions.Describe(preview, EffectText, SwitchArm, Requirement, Format, Otherwise);
            // One-shot events behind the option lead the bracket, and the row gets a green dot in front (F43).
            var own = Find(key, out _);
            var ready = own == null || ReadyOneShots == null ? null : ReadyOneShots(own.Pool, own.NextScript);
            if (ready != null && ready.Count > 0)
            {
                string all = Tone.Mark(1, L.Format("一次性事件：{0}", string.Join(L.Text("、"), ready)));
                segments.Insert(0, new HintSegment { OneShot = true, State = 1, Summary = Tone.Mark(1, L.Format("一次性事件×{0}", ready.Count)), Full = all, Head = all });
            }
            if (logged.Count > 500) logged.Clear();
            if (segments.Count > 0 && logged.Add(key)) log.LogInfo("OPTION " + key + " " + Tone.Plain(string.Join("；", segments.Select(segment => segment.Full))));
            return segments.Count == 0 ? null : segments;
        }
        catch (Exception ex) { log.LogWarning("OPTION_HINT " + ex.GetBaseException().Message); return null; }
    }

    private static string Format(string template, object[] args) => args.Length == 0 ? L.Text(template) : L.Format(template, args);

    private readonly Dictionary<string, string?> scriptTexts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, OptionPreview>?> scriptMaps = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>A story script's original text by name (cached), or null when the game has none.</summary>
    public string? ScriptText(string name)
    {
        if (scriptTexts.TryGetValue(name, out var text)) return text;
        var prefix = R.Field<string>(LuaManager.Instance, "_scriptPrefix", "Story");
        var asset = Resources.Load<TextAsset>(prefix + "/ChineseTraditional/" + name.ToLowerInvariant());
        return scriptTexts[name] = asset == null ? null : asset.text;
    }
    private Dictionary<string, OptionPreview>? ScriptMap(string name)
    {
        if (!scriptMaps.TryGetValue(name, out var map)) scriptMaps[name] = map = ScriptText(name) is string text ? LuaOptions.Analyze(text) : null;
        return map;
    }
    /// <summary>
    /// Story flag changes an event's script makes every time it runs (its top level, then its literal next script's), in
    /// order per flag. Changes inside a branch or a choice may not happen, so they are left out.
    /// </summary>
    public Dictionary<string, List<OptionEffect>> EventFlagChanges(string script)
    {
        var changes = new Dictionary<string, List<OptionEffect>>(StringComparer.Ordinal);
        void Add(Dictionary<string, OptionPreview>? map)
        {
            if (map == null || !map.TryGetValue(LuaOptions.ScriptLevel, out var level)) return;
            foreach (var effect in level.Effects)
                if (effect.Kind == "flag" || effect.Kind == "set-flag")
                { if (!changes.TryGetValue(effect.Key, out var list)) changes[effect.Key] = list = new(); list.Add(effect); }
        }
        var own = ScriptMap(script); Add(own);
        string next = own != null && own.TryGetValue(LuaOptions.ScriptLevel, out var top) ? top.NextScript : "";
        if (next.Length > 0 && !next.Equals(script, StringComparison.OrdinalIgnoreCase)) Add(ScriptMap(next));
        return changes;
    }
    /// <summary>
    /// What an action event's script does when it runs: its top-level effects, condition branches and markers (choices
    /// inside it are shown on their own option rows when they appear), plus its literal next script's top level.
    /// Branch states use current values. Null when the script is unknown or shows nothing.
    /// </summary>
    public List<HintSegment>? EventConsequences(string script)
    {
        try
        {
            var map = ScriptMap(script);
            if (map == null || !map.TryGetValue(LuaOptions.ScriptLevel, out var preview)) return null;
            bool follow = preview.NextScript.Length > 0 && !preview.NextScript.Equals(script, StringComparison.OrdinalIgnoreCase);
            var segments = LuaOptions.Describe(LuaOptions.Merge(preview, follow ? NextScript(preview.NextScript) : null), EffectText, SwitchArm, Requirement, Format, Otherwise);
            return segments.Count == 0 ? null : segments;
        }
        catch (Exception ex) { log.LogWarning("EVENT_HINT " + script + " " + ex.GetBaseException().Message); return null; }
    }

    /// <summary>An option's own consequences plus its literal next script's top level.</summary>
    public OptionPreview? Resolve(string key)
    {
        var preview = Find(key, out string script);
        if (preview == null) return null;
        // A menu that reloads its own script returns to the same rows; there is nothing further to show.
        bool follow = preview.NextScript.Length > 0 && !script.Equals(preview.NextScript, StringComparison.OrdinalIgnoreCase);
        return LuaOptions.Merge(preview, follow ? NextScript(preview.NextScript) : null);
    }
    private const string Dot = "●";
    /// <summary>Native row text without this mod's bracket or leading dot, whether the bracket sits on the title line or its own line.</summary>
    public static string StripHint(string text)
    {
        int dot = text.IndexOf(Dot + (text.StartsWith("<", StringComparison.Ordinal) ? "</color> " : " "), StringComparison.Ordinal);
        if (dot >= 0 && dot < 32) text = text.Substring(text.IndexOf(' ', dot) + 1);
        foreach (string marker in new[] { " <size=", "\n<size=", " （", "\n（" })
        {
            int at = text.IndexOf(marker, StringComparison.Ordinal);
            if (at >= 0) return text.Substring(0, at);
        }
        return text;
    }
    public OptionPreview? Find(string key, out string script)
    {
        foreach (var pair in recent)
            if (pair.Value.TryGetValue(key, out var preview)) { script = pair.Key; return preview; }
        script = ""; return null;
    }

    // The game's own food menu is where misc goods are eaten or drunk; its option branches are the item's use.
    private const string FoodMenu = "food_menu_001";
    private Dictionary<string, OptionPreview>? food;

    /// <summary>One short effect line for a shop item, or "" when the game data states no fixed use.</summary>
    public string ItemEffect(object item)
    {
        if (Suppressed) return "";
        try
        {
            switch (item)
            {
                case Book book: return BookEffect(book);
                case Misc misc: return FoodEffect("misc", misc.Id);
                case SpecialItem special: return FoodEffect("special", special.Id);
                default: return "";
            }
        }
        catch (Exception ex) { log.LogWarning("SHOP_HINT " + ex.GetBaseException().Message); return ""; }
    }
    private string FoodEffect(string type, string id)
    {
        if (food == null)
        {
            var prefix = R.Field<string>(LuaManager.Instance, "_scriptPrefix", "Story");
            var asset = Resources.Load<TextAsset>(prefix + "/ChineseTraditional/" + FoodMenu);
            food = asset == null ? new Dictionary<string, OptionPreview>() : LuaOptions.Analyze(asset.text);
        }
        foreach (var option in food.Values)
        {
            if (!option.Effects.Exists(effect => effect.Kind == "item-remove" && effect.ItemType == type && effect.Key == id)) continue;
            var use = new OptionPreview { Key = option.Key, Pool = option.Pool, Dice = option.Dice, Unknown = option.Unknown };
            use.Effects.AddRange(option.Effects.FindAll(effect => !(effect.Kind == "item-remove" && effect.ItemType == type && effect.Key == id)));
            use.Cases.AddRange(option.Cases);
            var segments = LuaOptions.Describe(use, EffectText, SwitchArm, Requirement, Format, Otherwise);
            if (segments.Count > 0) return L.Format("饮食：{0}", string.Join("；", segments.Select(segment => segment.Full)));
        }
        return "";
    }
    /// <summary>The same figures as the game's own study panel: related stats at current / full level, and level-up stat directions.</summary>
    private static string BookEffect(Book book)
    {
        var lines = new List<string>();
        foreach (var convert in book.RelationStatConvert ?? Array.Empty<BookLevelStatConvertData>())
        {
            if (convert == null || convert.RelationStat == null || convert.RelationStat.IsHiddenStat()) continue;
            string format = L.GameText(convert.GetDisplayFormatKey(), "{0} [{1}/{2}]");
            lines.Add(string.Format(format, L.GameText(convert.GetNameKey(), ""), Mathf.RoundToInt(convert.GetResult()), Mathf.RoundToInt(convert.GetResult(book.MaxLevel))));
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var level in book.LevelData ?? Array.Empty<MartialLevelData>())
            foreach (var stat in level.LevelUpStat)
            {
                if (stat.Stat == null || stat.Stat.StatType != MartialLevelupStatType.角色數值 || stat.Value == 0 || stat.Stat.IsHiddenStat() || !seen.Add(stat.GetNameKey())) continue;
                lines.Add(string.Format(L.GameText("MartialStatDisplay/StatPreview", "{0}"), L.GameText(stat.GetNameKey(), "") + stat.GetSign()));
            }
        return string.Join("  ", lines);
    }

    private static string Render(List<KeyValuePair<string, int>> segments, Color baseColor, bool rich, string size)
    {
        // Only the marked parts take colour: the condition lead and each change; names keep the row's own colour.
        string body = "（" + string.Join("；", segments.Select(segment => Colorize(segment.Key, baseColor, rich))) + "）";
        return rich ? size + body + "</size>" : body;
    }
    private static bool DarkText(Color color) => color.r * .299f + color.g * .587f + color.b * .114f < .5f;
    /// <summary>
    /// <see cref="Tone"/> marks in the mod's one meaning (green holds / rises / gains, red fails / falls / loses).
    /// Deep shades on the game's cream rows (dark text), light shades on dark rows and panels.
    /// </summary>
    public static string Colorize(string text, Color baseColor, bool rich)
    {
        if (!rich) return NativeUiState.ToneColors(text, null, null);
        bool darkText = DarkText(baseColor);
        return NativeUiState.ToneColors(text, darkText ? Tone.PositiveOnLight : Tone.PositiveOnDark, darkText ? Tone.NegativeOnLight : Tone.NegativeOnDark);
    }

    private OptionPreview? NextScript(string name)
    {
        if (nextScripts.TryGetValue(name, out var cached)) return cached;
        OptionPreview? result = null;
        try
        {
            var prefix = R.Field<string>(LuaManager.Instance, "_scriptPrefix", "Story");
            var asset = Resources.Load<TextAsset>(prefix + "/ChineseTraditional/" + name.ToLowerInvariant());
            // One level only: the next script's own next script is not followed.
            if (asset != null) { result = LuaOptions.Analyze(asset.text)[LuaOptions.ScriptLevel]; result.NextScript = ""; }
        }
        catch (Exception ex) { log.LogWarning("OPTION_HINT next " + name + " " + ex.GetBaseException().Message); }
        return nextScripts[name] = result;
    }

    private static string EffectText(OptionEffect effect)
    {
        string signed = effect.Value.ToString("+0;-0;0", CultureInfo.InvariantCulture), value = effect.Value.ToString(CultureInfo.InvariantCulture);
        switch (effect.Kind)
        {
            // Only the change itself is marked, so a conditional segment keeps its own red / green around it.
            case "stat": return StatName(effect.Key) + Tone.Mark(effect.Value, (effect.Approximate ? "≈" : "") + signed);
            case "set-stat": return StatName(effect.Key) + "=" + value;
            case "relation": return RelationName(effect.Key) + Tone.Mark(effect.Value, signed);
            case "set-relation": return RelationName(effect.Key) + "=" + value;
            case "flag":
            case "set-flag":
                // Same name as the 数值 page: official term, verified name, the game's own short note, else the raw ID.
                return FlagName(effect.Key) + (effect.Kind == "flag" ? signed : "=" + value);
            case "item-add": return Tone.Mark(1, L.Format("获得{0}×{1}", ItemName(effect), effect.Value));
            case "item-remove": return Tone.Mark(-1, L.Format("失去{0}", ItemName(effect)));
            case "talent": return Tone.Mark(1, L.Format("天赋{0} Lv{1}", L.GameText("PlayerTalent/Name/" + effect.Key, effect.Key), effect.Value));
            default: return "";
        }
    }
    private static string StatName(string key)
    {
        var player = PlayerStatManagerData.Instance;
        if (player != null && EnumUtils.TryParseByStringValue<GameStatType>(key, out var type))
        {
            var stat = player.Stats.List.FirstOrDefault(item => item != null && item.StatType == type);
            if (stat != null) return L.StatName(stat);
        }
        return key;
    }
    private static Dictionary<string, FlagData>? flagIndex;
    private static MissionManagerData? flagOwner;
    private static string FlagName(string id)
    {
        var missions = MissionManagerData.Instance;
        if (missions != null && (flagIndex == null || flagIndex.Count == 0 || !ReferenceEquals(flagOwner, missions)))
        {
            flagOwner = missions; flagIndex = new Dictionary<string, FlagData>(StringComparer.Ordinal);
            foreach (var flag in missions.FlagsCollection.Where(x => x != null).SelectMany(collection => collection.List).Where(x => x != null))
                if (!flagIndex.ContainsKey(flag.name)) flagIndex[flag.name] = flag;
        }
        return flagIndex != null && flagIndex.TryGetValue(id, out var data) ? L.FlagName(data) : id;
    }
    private static string RelationName(string key) => EnumUtils.TryParseByStringValue<RelationshipStatType>(key, out var type) ? L.RelationName(type) : key;
    private static string ItemName(OptionEffect effect)
    {
        string prefix = effect.ItemType == "book" ? "Book/Name/" : effect.ItemType == "special" ? "Special/Name/" : "Misc/Name/";
        return L.GameText(prefix + effect.Key, effect.Key);
    }

    /// <summary>Arm k is taken when item k is the first satisfied item; arm 0 or past the end is "none matched".</summary>
    private static KeyValuePair<string, int> SwitchArm(string name, int arm)
    {
        var data = FindSwitch(name);
        if (data == null || R.Field(data, "_useCount") is true) return new(L.Text("条件未核验"), 0);
        var items = R.Items(R.Field(data, "_items")).Select(item => Conditions.Read(item)).ToList();
        bool known = true, earlierMatched = false;
        int last = arm >= 1 && arm <= items.Count ? arm - 1 : items.Count;
        for (int i = 0; i < last; i++) { known &= items[i].Known; earlierMatched |= items[i].Known && items[i].Satisfied; }
        if (last == items.Count) return new("", !known ? 0 : earlierMatched ? -1 : 1);
        var own = items[last];
        int state = !known || !own.Known ? 0 : !earlierMatched && own.Satisfied ? 1 : -1;
        return new(Flatten(own), state);
    }
    /// <summary>A default arm shown without its earlier arms names what it stands for: none of the switch's conditions hold.</summary>
    private static string Otherwise(string name)
    {
        var data = FindSwitch(name);
        if (data == null || R.Field(data, "_useCount") is true) return "";
        var items = R.Items(R.Field(data, "_items")).Select(item => Flatten(Conditions.Read(item))).Where(text => text.Length > 0).ToList();
        return items.Count == 0 ? "" : items.Count == 1 ? L.Format("未满足 {0}", items[0]) : L.Format("{0} 都不满足", string.Join("、", items));
    }
    private static KeyValuePair<string, int> Requirement(string name)
    {
        var config = R.Field(CheckPointManager.Instance, "_condition");
        var data = R.Items(R.Field(config, "_list")).FirstOrDefault(item => item is UnityEngine.Object asset && asset != null && asset.name == name);
        if (data == null) return new(L.Text("条件未核验"), 0);
        var condition = Conditions.Read(data);
        return new(Flatten(condition), condition.Known ? condition.Satisfied ? 1 : -1 : 0);
    }
    internal static object? FindSwitch(string name)
    {
        foreach (var config in R.Items(R.Field(CheckPointManager.Instance, "_switchList")))
            if (config is SwitchResultConfig switches && switches.GetData(name) is SwitchResultData data) return data;
        return null;
    }
    internal static string Flatten(ConditionSnapshot condition)
    {
        if (condition.Children.Count == 1) return Flatten(condition.Children[0]);
        if (condition.Children.Count == 0) return condition.Text;
        string op = " " + L.Text(condition.Operator == "OR" ? "或" : "且") + " ";
        return string.Join(op, condition.Children.Select(child => child.Children.Count > 1 ? "(" + Flatten(child) + ")" : Flatten(child)));
    }
}
