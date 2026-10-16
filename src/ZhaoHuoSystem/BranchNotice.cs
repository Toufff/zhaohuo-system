using System;
using System.Collections.Generic;
using System.Linq;
using Mortal.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ZhaoHuoSystem;

/// <summary>
/// Floating reminder of the story's recent condition branches: every arm's condition with current values (red / green)
/// and the arm the game actually took. Fed only by the native Switch result; nothing is evaluated for the game.
/// Each new branch places it at one fixed spot in the dialogue area, taken once from the game's standard character
/// dialogue box (left of the story text, below the name) and kept as a screen fraction, so dialogue boxes without a
/// name or with another layout never move it. It announces itself with a short pop and a flashing title. Drag by the
/// title (until the next branch), close with ×; the next branch brings it back. Cleared when a choice menu opens or the
/// scene changes.
/// </summary>
internal sealed class BranchNotice : IDisposable
{
    private sealed class Row { public string Text = ""; public int State; public bool Taken; public float Height; }
    private const int Kept = 3;
    private const float Width = 380, Pad = 10, Head = 28, Gap = 9, Marker = 18, PopTime = .45f, FlashTime = 1.1f, Blink = .18f;
    private readonly List<List<Row>> judgments = new(); // newest first
    private bool closed = true, dragging, follow;
    private Vector2? position;
    private Vector2 grab;
    private float pulse = -10;
    private Font? font;
    private Texture2D? background, line, highlight, accent;
    private GUIStyle? text, title, close;
    private long revision = -1;
    /// <summary>The fixed spot as screen fractions of the box's top-left; learned from the character dialogue box.</summary>
    public Vector2? Reference { get; set; }
    public Action<Vector2>? ReferenceLearned;
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
        // A new branch always returns to the dialogue box, even after a drag, and announces itself.
        closed = false; follow = true; dragging = false; pulse = Time.unscaledTime;
    }

    public void Clear() { judgments.Clear(); closed = true; dragging = false; Rect = default; }

    /// <summary>Layout and input; call before other mod surfaces so the box wins its own clicks.</summary>
    public void Draw(bool hidden)
    {
        if (hidden || closed || judgments.Count == 0) { Rect = default; dragging = false; return; }
        EnsureStyles();
        var e = Event.current;
        float inner = Width - Pad * 2 - Marker, height = Head + 6 + Pad;
        for (int j = 0; j < judgments.Count; j++)
        {
            if (j > 0) height += Gap;
            foreach (var row in judgments[j]) { row.Height = Mathf.Max(20, text!.CalcHeight(new GUIContent(row.Text), inner)); height += row.Height; }
        }
        height = Mathf.Min(height, Screen.height - 8);
        // Until the player drags it, the box sits at the fixed spot; before that spot is known, beside the story text.
        if (follow) Learn();
        var origin = !follow ? position ?? Fallback()
            : Reference.HasValue ? new Vector2(Reference.Value.x * Screen.width, Reference.Value.y * Screen.height) : Fallback();
        origin.x = Mathf.Clamp(origin.x, 0, Mathf.Max(0, Screen.width - Width));
        origin.y = Mathf.Clamp(origin.y, 0, Mathf.Max(0, Screen.height - height));
        position = origin;
        Rect = new Rect(origin.x, origin.y, Width, height);
        var titleRect = new Rect(origin.x, origin.y, Width, Head);
        var closeRect = new Rect(Rect.xMax - 32, origin.y + 1, 30, Head - 2);
        if (e.type == EventType.MouseDown && e.button == 0 && Rect.Contains(e.mousePosition))
        {
            if (closeRect.Contains(e.mousePosition)) { closed = true; Rect = default; e.Use(); return; }
            if (titleRect.Contains(e.mousePosition)) { dragging = true; follow = false; grab = e.mousePosition - origin; }
            e.Use();
        }
        else if (dragging && e.type == EventType.MouseDrag) { position = e.mousePosition - grab; e.Use(); }
        else if (dragging && e.rawType == EventType.MouseUp) { dragging = false; e.Use(); }
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
            var box = new Rect(Rect.x, Rect.y - 10 * k, Rect.width, Rect.height);
            if (k > 0) GUIUtility.ScaleAroundPivot(new Vector2(1 + .07f * k, 1 + .07f * k), box.center);
            GUI.color = Color.white;
            GUI.DrawTexture(box, background!);
            if (flash) GUI.DrawTexture(new Rect(box.x, box.y, Width, Head), accent!);
            title!.normal.textColor = flash ? Color.white : Palette.Text;
            title.fontSize = flash ? 16 : 15;
            GUI.Label(new Rect(box.x + Pad, box.y + 4, Width - Pad * 2 - 32, Head - 4), L.Text("剧情分支"), title);
            var closeRect = new Rect(box.xMax - 32, box.y + 1, 30, Head - 2);
            close!.normal.textColor = closeRect.Contains(Event.current.mousePosition) ? Color.white : new Color(.72f, .75f, .78f);
            GUI.Label(closeRect, "×", close);
            GUI.DrawTexture(new Rect(box.x, box.y + Head, Width, 1), line!);
            float y = box.y + Head + 6, inner = Width - Pad * 2 - Marker;
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
                            GUI.DrawTexture(new Rect(box.x + 4, y, Width - 8, row.Height), highlight!);
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

    /// <summary>
    /// Takes the fixed spot from the game's standard character dialogue box while it shows a name: left of the story
    /// text, just below the name. Other dialogue layouts only use the spot, never change it.
    /// </summary>
    private void Learn()
    {
        var dialog = Fungus.SayDialog.ActiveSayDialog;
        if (dialog is not CharacterSayDialog || !dialog.gameObject.activeInHierarchy || Screen.width <= 0 || Screen.height <= 0) return;
        var story = dialog.StoryTextRectTrans;
        var name = R.Field<Text>(dialog, "nameText")?.rectTransform ?? R.Field<GameObject>(dialog, "nameTextGO")?.transform as RectTransform;
        if (story == null || name == null || !story.gameObject.activeInHierarchy || !name.gameObject.activeInHierarchy) return;
        var camera = CanvasCamera(story);
        var spot = new Vector2((ScreenRect(story, camera).xMin - Width - 12) / Screen.width, (ScreenRect(name, camera).yMax + 8) / Screen.height);
        spot = new Vector2(Mathf.Clamp01(spot.x), Mathf.Clamp01(spot.y));
        if (Reference.HasValue && (Reference.Value - spot).sqrMagnitude < .003f * .003f) return;
        Reference = spot; ReferenceLearned?.Invoke(spot);
    }
    /// <summary>Before the spot is known: beside the active dialogue's story text, else the top-right corner.</summary>
    private static Vector2 Fallback()
    {
        var story = Fungus.SayDialog.ActiveSayDialog is { } dialog && dialog.gameObject.activeInHierarchy ? dialog.StoryTextRectTrans : null;
        if (story == null || !story.gameObject.activeInHierarchy) return new Vector2(Screen.width - Width - 24, 72);
        var rect = ScreenRect(story, CanvasCamera(story));
        return new Vector2(rect.xMin - Width - 12, rect.yMin);
    }
    private static Camera? CanvasCamera(RectTransform rect)
    {
        var canvas = rect.GetComponentInParent<Canvas>()?.rootCanvas;
        return canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
    }
    /// <summary>A RectTransform in IMGUI coordinates (origin top-left).</summary>
    private static Rect ScreenRect(RectTransform rect, Camera? camera)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
        foreach (var corner in corners)
        {
            var point = RectTransformUtility.WorldToScreenPoint(camera, corner);
            min = Vector2.Min(min, point); max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, Screen.height - max.y, max.x, Screen.height - min.y);
    }

    private void EnsureStyles()
    {
        if (text != null && revision == L.Revision) return;
        if (font != null) UnityEngine.Object.Destroy(font);
        font = Font.CreateDynamicFontFromOSFont(L.FontNames, 16);
        background ??= Solid(new Color(0, 0, 0, .78f));
        line ??= Solid(new Color(1, 1, 1, .22f));
        highlight ??= Solid(new Color(1, 1, 1, .09f));
        // The panel's own accent, so the flash reads as "look here" rather than as a red / green meaning.
        accent ??= Solid(new Color(.29f, .46f, .61f, .95f));
        text = new GUIStyle { font = font, fontSize = 15, alignment = TextAnchor.UpperLeft, wordWrap = true, richText = false };
        title = new GUIStyle(text) { fontStyle = FontStyle.Bold, wordWrap = false }; title.normal.textColor = Palette.Text;
        close = new GUIStyle(text) { fontSize = 20, alignment = TextAnchor.MiddleCenter, wordWrap = false };
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
