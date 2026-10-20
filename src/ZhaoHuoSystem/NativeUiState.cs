using System;
using System.Collections.Generic;

namespace ZhaoHuoSystem;

/// <summary>UI identity and filtering are independent of translated group labels.</summary>
public static class NativeUiState
{
    private static readonly string[] Order = { "attributes", "sect", "general", "alignment", "martial", "hidden", "other", "relationships", "factions", "talents", "books", "items", "development", "library", "flags" };
    public static List<string> Groups(List<ValueSnapshot> values)
    {
        var result = new List<string> { "all" };
        foreach (var group in Order) if (values.Exists(value => value.GroupId == group)) result.Add(group);
        foreach (var value in values)
            if (!string.IsNullOrEmpty(value.GroupId) && !result.Contains(value.GroupId)) result.Add(value.GroupId);
        return result;
    }
    public static bool MatchesValue(ValueSnapshot value, string groupId, string query)
    {
        if (groupId != "all" && value.GroupId != groupId) return false;
        if (string.IsNullOrWhiteSpace(query)) return true;
        string q = query.Trim();
        foreach (var field in new[] { value.Name, value.Id, value.SearchText, value.Detail })
            if (field != null && field.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }
    /// <summary>One comparison side: a live operand carries its current value, e.g. 「行动次数 3」; a literal is only its value.</summary>
    public static string ConditionOperand(string name, string? current, bool literal)
        => literal || current == null ? name : name + " " + (current.Length > 0 ? current : "?");
    /// <summary>Launcher anchor stored in the mod config; anything malformed falls back to the default spot.</summary>
    public static bool TryParseAnchor(string? text, out float x, out float y)
    {
        x = y = 0;
        var parts = (text ?? "").Split(',');
        return parts.Length == 2 && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x)
            && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y)
            && x >= 0 && x <= 1 && y >= 0 && y <= 1;
    }
    public static string FormatAnchor(float x, float y) => x.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + "," + y.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
    /// <summary>A hotkey must be one ordinary key: not mouse/joystick, not a lone modifier, not Esc (which cancels binding).</summary>
    public static bool IsBindableKey(string keyCode) => keyCode.Length > 0 && keyCode != "None" && keyCode != "Escape"
        && !keyCode.StartsWith("Mouse", StringComparison.Ordinal) && !keyCode.StartsWith("Joystick", StringComparison.Ordinal)
        && !keyCode.EndsWith("Shift", StringComparison.Ordinal) && !keyCode.EndsWith("Control", StringComparison.Ordinal)
        && !keyCode.EndsWith("Alt", StringComparison.Ordinal) && !keyCode.EndsWith("Command", StringComparison.Ordinal)
        && !keyCode.EndsWith("Windows", StringComparison.Ordinal) && !keyCode.EndsWith("Apple", StringComparison.Ordinal);
    /// <summary>UnityEngine.KeyCode name to the matching InputSystem Key name; most names are identical.</summary>
    public static string InputSystemKeyName(string keyCode)
    {
        if (keyCode.StartsWith("Alpha", StringComparison.Ordinal) && keyCode.Length == 6) return "Digit" + keyCode.Substring(5);
        if (keyCode.StartsWith("Keypad", StringComparison.Ordinal)) return keyCode == "KeypadEnter" ? "NumpadEnter" : "Numpad" + keyCode.Substring(6);
        return keyCode switch
        {
            "Return" => "Enter", "BackQuote" => "Backquote", "Numlock" => "NumLock", "Print" => "PrintScreen",
            _ => keyCode
        };
    }
    /// <summary>Win32 virtual key for the native focus fallback; 0 when there is no unambiguous mapping.</summary>
    public static int VirtualKey(string keyCode)
    {
        if (keyCode.Length == 1 && keyCode[0] >= 'A' && keyCode[0] <= 'Z') return keyCode[0];
        if (keyCode.StartsWith("Alpha", StringComparison.Ordinal) && keyCode.Length == 6 && char.IsDigit(keyCode[5])) return '0' + (keyCode[5] - '0');
        if (keyCode.StartsWith("Keypad", StringComparison.Ordinal) && keyCode.Length == 7 && char.IsDigit(keyCode[6])) return 0x60 + (keyCode[6] - '0');
        if (keyCode.Length >= 2 && keyCode[0] == 'F' && int.TryParse(keyCode.Substring(1), out int f) && f >= 1 && f <= 24) return 0x6F + f;
        return keyCode switch
        {
            "Space" => 0x20, "Tab" => 0x09, "Return" => 0x0D, "Insert" => 0x2D, "Delete" => 0x2E, "Home" => 0x24, "End" => 0x23,
            "PageUp" => 0x21, "PageDown" => 0x22, "Pause" => 0x13, "BackQuote" => 0xC0, _ => 0
        };
    }
    /// <summary>Branch chance for a dice button: whole percent, with tiny non-zero shares never shown as 0%.</summary>
    public static string ChanceLabel(double? chance)
    {
        if (!chance.HasValue) return "?";
        double percent = chance.Value * 100;
        if (percent > 0 && percent < 1) return "<1%";
        if (percent < 100 && percent > 99) return ">99%";
        return Math.Round(percent, MidpointRounding.AwayFromZero).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
    }
    public static List<KeyValuePair<int, string>> EnumRows(IReadOnlyDictionary<int, string> meanings)
    {
        var result = new List<KeyValuePair<int, string>>();
        foreach (var pair in meanings) result.Add(pair);
        result.Sort((left, right) => left.Key.CompareTo(right.Key));
        return result;
    }

    public sealed class EnumTooltipGrid
    {
        public int Columns, Rows, VisibleCount;
        public float Width, Height, ColumnWidth, RowHeight, HeaderHeight;
        public bool Truncated;
    }

    // Measures the actual translated strings. A short list uses one column;
    // longer lists prefer at most sixteen rows, then adapt to the screen bounds.
    public static EnumTooltipGrid EnumTooltipLayout(IReadOnlyList<string> lines, float maxWidth, float maxHeight,
        float preferredColumnWidth, float minimumColumnWidth, float gap, float padding, float minimumRowHeight,
        float footerHeight, Func<string, float, float> measureLine, Func<float, float> measureHeader)
    {
        if (lines.Count == 0) return new EnumTooltipGrid();
        maxWidth = Math.Max(1, maxWidth); maxHeight = Math.Max(1, maxHeight);
        int maxColumns = Math.Max(1, Math.Min(lines.Count, (int)((maxWidth - padding * 2 + gap) / (minimumColumnWidth + gap))));
        int firstColumns = Math.Min(maxColumns, Math.Max(1, (lines.Count + 15) / 16));
        EnumTooltipGrid? fallback = null;
        for (int columns = firstColumns; columns <= maxColumns; columns++)
        {
            float columnWidth = Math.Max(1, Math.Min(preferredColumnWidth, (maxWidth - padding * 2 - gap * (columns - 1)) / columns));
            float width = Math.Min(maxWidth, columnWidth * columns + gap * (columns - 1) + padding * 2);
            float headerHeight = measureHeader(Math.Max(1, width - padding * 2));
            float rowHeight = minimumRowHeight;
            foreach (var line in lines) rowHeight = Math.Max(rowHeight, measureLine(line, columnWidth));
            int rows = (lines.Count + columns - 1) / columns;
            float height = padding * 2 + headerHeight + rows * rowHeight;
            var candidate = new EnumTooltipGrid { Columns = columns, Rows = rows, VisibleCount = lines.Count, Width = width,
                Height = Math.Min(maxHeight, height), ColumnWidth = columnWidth, RowHeight = rowHeight, HeaderHeight = headerHeight };
            if (height <= maxHeight) return candidate;
            int visibleRows = Math.Max(0, (int)((maxHeight - padding * 2 - headerHeight - footerHeight) / rowHeight));
            candidate.Rows = visibleRows;
            candidate.VisibleCount = Math.Min(lines.Count, visibleRows * columns);
            candidate.Truncated = true;
            if (fallback == null || candidate.VisibleCount > fallback.VisibleCount) fallback = candidate;
        }
        return fallback!;
    }

    /// <summary>
    /// The leading consequence segments that fit one line, closed by "…" when any are cut; a single overlong first
    /// segment is shortened by characters. <paramref name="width"/> measures the rendered bracket for a candidate list.
    /// </summary>
    public static List<KeyValuePair<string, int>> FitSegments(List<KeyValuePair<string, int>> segments,
        Func<List<KeyValuePair<string, int>>, float> width, float available, out bool truncated)
    {
        truncated = false;
        if (segments.Count == 0 || width(segments) <= available) return segments;
        truncated = true;
        var more = new KeyValuePair<string, int>("…", 0);
        for (int keep = segments.Count - 1; keep >= 1; keep--)
        {
            var candidate = segments.GetRange(0, keep); candidate.Add(more);
            if (width(candidate) <= available) return candidate;
        }
        string first = segments[0].Key; int low = 0, high = first.Length - 1;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (width(new() { Cut(segments[0], mid) }) <= available) low = mid; else high = mid - 1;
        }
        return new() { low > 0 ? Cut(segments[0], low) : more };
    }
    /// <summary>
    /// Raw dice value per branch. Natural rolls (uniform over min..max) give each branch its first raw value and its
    /// hit count. A wanted branch the natural range never reaches gets the raw value worked back from the total,
    /// nearest to the range first and never above <paramref name="upper"/> (the wheel shows two digits); branches still
    /// missing can only be forced. Null hits when the range is too wide to count.
    /// </summary>
    public static Dictionary<int, int> DiceRaws(int min, int max, int upper, IEnumerable<int> wanted, Func<int, int> branchOf, out Dictionary<int, int>? hits)
    {
        var raws = new Dictionary<int, int>();
        hits = null;
        if ((long)max - min > 10000) return raws;
        hits = new Dictionary<int, int>();
        for (long raw = min; raw <= max; raw++)
        {
            int branch = branchOf((int)raw);
            if (!raws.ContainsKey(branch)) raws[branch] = (int)raw;
            hits[branch] = hits.TryGetValue(branch, out var count) ? count + 1 : 1;
        }
        var missing = new HashSet<int>(wanted); missing.ExceptWith(raws.Keys);
        for (long step = 1; step <= 10000 && missing.Count > 0; step++)
            foreach (long raw in new[] { (long)min - step, (long)max + step })
            {
                if (raw > upper || raw < int.MinValue) continue;
                int branch = branchOf((int)raw);
                if (missing.Remove(branch)) raws[branch] = (int)raw;
            }
        return raws;
    }
    /// <summary><see cref="Tone"/> marks as rich-text colours, or removed when a colour is null. Always balanced, even after a cut.</summary>
    /// <summary>
    /// 回想 (F101): where each entry's text starts in the log's one text, as Fungus' GetPrettyHistory writes it: a line break,
    /// then for each of the last <paramref name="max"/> entries its name line (in its own size and colour) if it has a name,
    /// its text and a blank line. -1 for an entry left out.
    /// </summary>
    public static int[] LogTextStarts(IReadOnlyList<KeyValuePair<string, string>> entries, int max)
    {
        var starts = new int[entries.Count];
        int first = Math.Max(0, entries.Count - Math.Max(0, max)), at = 1;
        for (int i = 0; i < entries.Count; i++)
        {
            if (i < first) { starts[i] = -1; continue; }
            if (!string.IsNullOrEmpty(entries[i].Key)) at += LogNameOpen.Length + entries[i].Key.Length + LogNameClose.Length;
            starts[i] = at;
            at += (entries[i].Value ?? "").Length + 2;
        }
        return starts;
    }
    private const string LogNameOpen = "<size=28><color=#D9B077>", LogNameClose = "</color></size>\n";

    /// <summary>
    /// Index among the characters a Unity Text lays out of the character at <paramref name="raw"/> in its rich text: tags it
    /// understands take no place. Anything else in angle brackets is drawn as written.
    /// </summary>
    public static int DrawnIndex(string rich, int raw)
    {
        int drawn = 0;
        for (int i = 0; i < raw && i < rich.Length;)
        {
            int tag = rich[i] == '<' ? RichTagLength(rich, i) : 0;
            if (tag > 0 && i + tag <= raw) { i += tag; continue; }
            drawn++; i++;
        }
        return drawn;
    }
    private static readonly string[] RichTags = { "b", "i", "size", "color", "material", "quad" };
    private static int RichTagLength(string text, int at)
    {
        int close = text.IndexOf('>', at);
        if (close < 0) return 0;
        string inner = text.Substring(at + 1, close - at - 1);
        if (inner.StartsWith("/", StringComparison.Ordinal)) inner = inner.Substring(1);
        int equals = inner.IndexOf('=');
        string name = (equals >= 0 ? inner.Substring(0, equals) : inner).Trim();
        return Array.IndexOf(RichTags, name) >= 0 ? close - at + 1 : 0;
    }

    public static string ToneColors(string text, string? positive, string? negative)
    {
        if (text.IndexOf(Tone.Positive) < 0 && text.IndexOf(Tone.Negative) < 0 && text.IndexOf(Tone.End) < 0) return text;
        var result = new System.Text.StringBuilder(text.Length + 32); bool open = false;
        foreach (char c in text)
        {
            if (c == Tone.Positive || c == Tone.Negative)
            {
                if (open) { result.Append("</color>"); open = false; }
                string? color = c == Tone.Positive ? positive : negative;
                if (color != null) { result.Append("<color=").Append(color).Append('>'); open = true; }
            }
            else if (c == Tone.End) { if (open) { result.Append("</color>"); open = false; } }
            else result.Append(c);
        }
        if (open) result.Append("</color>");
        return result.ToString();
    }
    private static KeyValuePair<string, int> Cut(KeyValuePair<string, int> segment, int length)
    {
        // Never split a surrogate pair.
        if (length > 0 && char.IsHighSurrogate(segment.Key[length - 1])) length--;
        return new(segment.Key.Substring(0, length) + "…", segment.Value);
    }
}
