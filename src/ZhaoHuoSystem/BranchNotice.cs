using System;
using System.Collections.Generic;
using System.Linq;
using Mortal.Core;
using UnityEngine;

namespace ZhaoHuoSystem;

/// <summary>
/// Floating reminder of the story's recent condition branches: every arm's condition with current values (red / green)
/// and the arm the game actually took. Fed only by the native Switch result; nothing is evaluated for the game.
/// Each new branch places it in the game window's bottom-left corner (its own bottom-left on the window's, growing
/// upwards), or where the player last dragged it (F68: kept across branches and restarts, 「重置位置」 in settings), and
/// announces itself with a short pop and a flashing title. Drag by the title, close with ×; the next branch brings it
/// back. Cleared when a choice menu opens or the scene changes.
/// </summary>
internal sealed class BranchNotice : IDisposable
{
    private sealed class Row { public string Text = ""; public int State; public bool Taken; public float Height; }
    private const int Kept = 3;
    private const float PopTime = .45f, FlashTime = 1.1f, Blink = .18f;
    // Sizes follow the mod's one scale (4K, player multiplier).
    private static float K => UiScale.Current;
    private static float Width => 380 * K;
    private static float Pad => 10 * K;
    private static float Head => 28 * K;
    private static float Gap => 9 * K;
    private static float Marker => 18 * K;
    private float styleScale = -1;
    private readonly List<List<Row>> judgments = new(); // newest first
    private bool closed = true, dragging, follow;
    private Vector2? position;
    /// <summary>The player's own place, as fractions of the screen left free by the box; null for the bottom-left corner.</summary>
    public Vector2? Anchor;
    /// <summary>After a drag ends (the new place) or a reset (null).</summary>
    public Action<Vector2?>? Moved;
    private Vector2 grab;
    private float pulse = -10;
    private Font? font;
    private Texture2D? background, line, highlight, accent;
    private GUIStyle? text, title, close;
    private long revision = -1;
    /// <summary>The box in GUI coordinates, empty while nothing is shown. The input guard keeps game clicks out of it.</summary>
    public Rect Rect { get; private set; }

    public void Record(object data, int result)
    {
        var rows = new List<Row>();
        if (R.Field(data, "_useCount") is true)
        {
            // Some branches count how often they ran instead of reading conditions.
            int count = R.Field(data, "_count") is int c ? c : 0;
            var items = R.Items(R.Field(data, "_countItems")).ToList();
            for (int i = 0; i < items.Count; i++)
            {
                var op = R.Field(items[i], "_compareOp") is StatCompareType o ? o : StatCompareType.Equal;
                int value = R.Field(items[i], "_value") is int v ? v : 0;
                rows.Add(new Row { Text = L.Format("触发次数 {0}{1}{2}", count, Conditions.Symbol(op), value), State = GameStatUtils.StatCompare(count, value, op) ? 1 : -1, Taken = result == i + 1 });
            }
        }
        else
        {
            var items = R.Items(R.Field(data, "_items")).ToList();
            for (int i = 0; i < items.Count; i++)
            {
                var condition = Conditions.Read(items[i]);
                rows.Add(new Row { Text = OptionHints.Flatten(condition), State = !condition.Known ? 0 : condition.Satisfied ? 1 : -1, Taken = result == i + 1 });
            }
        }
        bool fallback = result < 1 || result > rows.Count;
        rows.Add(new Row { Text = L.Text("否则"), State = fallback ? 1 : 0, Taken = fallback });
        judgments.Insert(0, rows);
        if (judgments.Count > Kept) judgments.RemoveRange(Kept, judgments.Count - Kept);
        // A new branch returns to the player's place (or the corner) and announces itself.
        closed = false; follow = true; dragging = false; pulse = Time.unscaledTime;
    }

    public void Clear() { judgments.Clear(); closed = true; dragging = false; Rect = default; }
    /// <summary>Back to the bottom-left corner, now and for the next branches.</summary>
    public void ResetPlace() { Anchor = null; follow = true; position = null; Moved?.Invoke(null); }

    /// <summary>Layout and input; call before other mod surfaces so the box wins its own clicks.</summary>
    public void Draw(bool hidden)
    {
        if (hidden || closed || judgments.Count == 0) { Rect = default; dragging = false; return; }
        EnsureStyles();
        var e = Event.current;
        float inner = Width - Pad * 2 - Marker, height = Head + 6 * K + Pad;
        for (int j = 0; j < judgments.Count; j++)
        {
            if (j > 0) height += Gap;
            foreach (var row in judgments[j]) { row.Height = Mathf.Max(20 * K, text!.CalcHeight(new GUIContent(row.Text), inner)); height += row.Height; }
        }
        height = Mathf.Min(height, Screen.height - 8);
        // Mid-drag it follows the pointer; otherwise the player's place, or the bottom-left corner.
        var free = new Vector2(Mathf.Max(0, Screen.width - Width), Mathf.Max(0, Screen.height - height));
        var origin = !follow && position.HasValue ? position.Value : Anchor is Vector2 anchor ? Vector2.Scale(anchor, free) : new Vector2(0, Screen.height - height);
        origin.x = Mathf.Clamp(origin.x, 0, Mathf.Max(0, Screen.width - Width));
        origin.y = Mathf.Clamp(origin.y, 0, Mathf.Max(0, Screen.height - height));
        position = origin;
        Rect = new Rect(origin.x, origin.y, Width, height);
        var titleRect = new Rect(origin.x, origin.y, Width, Head);
        var closeRect = new Rect(Rect.xMax - 32 * K, origin.y + 1, 30 * K, Head - 2);
        if (e.type == EventType.MouseDown && e.button == 0 && Rect.Contains(e.mousePosition))
        {
            if (closeRect.Contains(e.mousePosition)) { closed = true; Rect = default; e.Use(); return; }
            if (titleRect.Contains(e.mousePosition)) { dragging = true; follow = false; grab = e.mousePosition - origin; }
            e.Use();
        }
        else if (dragging && e.type == EventType.MouseDrag) { position = e.mousePosition - grab; e.Use(); }
        else if (dragging && e.rawType == EventType.MouseUp)
        {
            dragging = false; e.Use();
            Anchor = new Vector2(free.x > 0 ? Mathf.Clamp01(origin.x / free.x) : 0, free.y > 0 ? Mathf.Clamp01(origin.y / free.y) : 1);
            Moved?.Invoke(Anchor);
        }
        else if (Rect.Contains(e.mousePosition) && (e.type == EventType.MouseUp || e.type == EventType.ScrollWheel)) e.Use();
    }

    /// <summary>Paint after the other surfaces so the box stays on top.</summary>
    public void Paint()
    {
        if (Rect.width <= 0 || Event.current.type != EventType.Repaint) return;
        var old = GUI.color; var oldMatrix = GUI.matrix;
        try
        {
            // Arrival: grow a little and lift, then settle back; the title flashes a few times.
            float t = Time.unscaledTime - pulse, k = t >= 0 && t < PopTime ? Mathf.Sin(Mathf.PI * t / PopTime) : 0;
            bool flash = t >= 0 && t < FlashTime && (int)(t / Blink) % 2 == 0;
            var box = new Rect(Rect.x, Rect.y - 10 * K * k, Rect.width, Rect.height);
            if (k > 0) GUIUtility.ScaleAroundPivot(new Vector2(1 + .07f * k, 1 + .07f * k), box.center);
            GUI.color = Color.white;
            GUI.DrawTexture(box, background!);
            if (flash) GUI.DrawTexture(new Rect(box.x, box.y, Width, Head), accent!);
            title!.normal.textColor = flash ? Color.white : Palette.Text;
            title.fontSize = Mathf.RoundToInt((flash ? 16 : 15) * K);
            GUI.Label(new Rect(box.x + Pad, box.y + 4 * K, Width - Pad * 2 - 32 * K, Head - 4 * K), L.Text("剧情分支"), title);
            var closeRect = new Rect(box.xMax - 32 * K, box.y + 1, 30 * K, Head - 2);
            close!.normal.textColor = closeRect.Contains(Event.current.mousePosition) ? Color.white : new Color(.72f, .75f, .78f);
            GUI.Label(closeRect, "×", close);
            GUI.DrawTexture(new Rect(box.x, box.y + Head, Width, 1), line!);
            float y = box.y + Head + 6 * K, inner = Width - Pad * 2 - Marker;
            GUI.BeginGroup(new Rect(0, 0, Screen.width, box.yMax - 2));
            try
            {
                for (int j = 0; j < judgments.Count; j++)
                {
                    if (j > 0) { GUI.color = Color.white; GUI.DrawTexture(new Rect(box.x + Pad, y + Gap / 2, Width - Pad * 2, 1), line!); y += Gap; }
                    // The latest branch reads at full strength; earlier ones are kept for context.
                    GUI.color = j == 0 ? Color.white : new Color(1, 1, 1, .62f);
                    foreach (var row in judgments[j])
                    {
                        if (row.Taken)
                        {
                            GUI.DrawTexture(new Rect(box.x + 4 * K, y, Width - 8 * K, row.Height), highlight!);
                            text!.normal.textColor = Color.white;
                            GUI.Label(new Rect(box.x + Pad, y, Marker, row.Height), "▶", text);
                        }
                        text!.normal.textColor = Palette.State(row.State);
                        GUI.Label(new Rect(box.x + Pad + Marker, y, inner, row.Height), row.Text, text);
                        y += row.Height;
                    }
                }
            }
            finally { GUI.EndGroup(); }
        }
        finally { GUI.color = old; GUI.matrix = oldMatrix; }
    }

    private void EnsureStyles()
    {
        if (text != null && revision == L.Revision && Mathf.Abs(styleScale - K) < .01f) return;
        styleScale = K;
        if (font != null) UnityEngine.Object.Destroy(font);
        font = Font.CreateDynamicFontFromOSFont(L.FontNames, 16);
        background ??= Solid(new Color(0, 0, 0, .78f));
        line ??= Solid(new Color(1, 1, 1, .22f));
        highlight ??= Solid(new Color(1, 1, 1, .09f));
        // The panel's own accent, so the flash reads as "look here" rather than as a red / green meaning.
        accent ??= Solid(new Color(.29f, .46f, .61f, .95f));
        text = new GUIStyle { font = font, fontSize = Mathf.RoundToInt(15 * K), alignment = TextAnchor.UpperLeft, wordWrap = true, richText = false };
        title = new GUIStyle(text) { fontStyle = FontStyle.Bold, wordWrap = false }; title.normal.textColor = Palette.Text;
        close = new GUIStyle(text) { fontSize = Mathf.RoundToInt(20 * K), alignment = TextAnchor.MiddleCenter, wordWrap = false };
        revision = L.Revision;
    }
    private static Texture2D Solid(Color color)
    {
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false); texture.SetPixel(0, 0, color); texture.Apply(); return texture;
    }
    public void Dispose()
    {
        if (font != null) UnityEngine.Object.Destroy(font);
        foreach (var texture in new[] { background, line, highlight, accent }) if (texture != null) UnityEngine.Object.Destroy(texture);
    }
}
