using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZhaoHuoSystem;

/// <summary>Passive map annotations: no controls, input consumption or game references.</summary>
internal sealed class MapIndicators : IDisposable
{
    private Texture2D? background, readyBackground;
    private Font? font;
    private GUIStyle? marker, readyMarker, tooltip;
    private long revision = -1;
    private float scale = -1;
    private sealed class Line { public string Text = ""; public Color Color = Color.white; public bool Ready; }
    public void Draw(ToolSnapshot snapshot)
    {
        if (!snapshot.Ready || snapshot.Dice.Active || Event.current.type != EventType.Repaint) return;
        var oldColor = GUI.color; var oldContent = GUI.contentColor; var oldBackground = GUI.backgroundColor; int oldDepth = GUI.depth;
        try { GUI.color = GUI.contentColor = GUI.backgroundColor = Color.white; GUI.depth = -9998; DrawCore(snapshot); }
        finally { GUI.color = oldColor; GUI.contentColor = oldContent; GUI.backgroundColor = oldBackground; GUI.depth = oldDepth; }
    }
    private void DrawCore(ToolSnapshot snapshot)
    {
        float k = UiScale.Current;
        if (revision != L.Revision || marker == null || Mathf.Abs(scale - k) > .01f)
        {
            scale = k;
            if (font != null) UnityEngine.Object.Destroy(font);
            font = Font.CreateDynamicFontFromOSFont(L.FontNames, 16);
            if (background == null) { background = new Texture2D(1, 1, TextureFormat.RGBA32, false); background.SetPixel(0, 0, new Color(0, 0, 0, .78f)); background.Apply(); }
            marker = new GUIStyle { font = font, fontSize = Mathf.RoundToInt(13 * k), alignment = TextAnchor.MiddleCenter };
            marker.normal.background = background; marker.normal.textColor = Color.white;
            // Fully satisfied events stand out on the map count and in the hover list.
            if (readyBackground == null) { readyBackground = new Texture2D(1, 1, TextureFormat.RGBA32, false); readyBackground.SetPixel(0, 0, new Color(.18f, .45f, .27f, .92f)); readyBackground.Apply(); }
            readyMarker = new GUIStyle(marker); readyMarker.normal.background = readyBackground;
            tooltip = new GUIStyle { font = font, fontSize = Mathf.RoundToInt(14 * k), alignment = TextAnchor.UpperLeft, wordWrap = true, richText = false };
            revision = L.Revision;
        }
        List<Line>? hovered = null;
        var pointer = Event.current.mousePosition;
        foreach (var location in snapshot.MapLocations)
        {
            if (!location.Visible || !location.HasScreenPosition || location.Entries.Count == 0) continue;
            var rect = new Rect(Mathf.Clamp(location.ScreenX + 20 * k, 4, Screen.width - 32 * k), Mathf.Clamp(Screen.height - location.ScreenY - 28 * k, 4, Screen.height - 28 * k), 27 * k, 23 * k);
            GUI.Label(rect, location.Entries.Count.ToString(), location.Entries.Exists(entry => entry.Ready) ? readyMarker! : marker!);
            if (!rect.Contains(pointer)) continue;
            hovered = new List<Line> { new() { Text = L.Format("{0}：{1} 个事件", location.Name, location.Entries.Count) } };
            foreach (var entry in location.Entries)
            {
                hovered.Add(new() { Text = "" });
                hovered.Add(new() { Text = entry.Ready ? L.Text("待触发") + " · " + entry.Title : entry.Title, Ready = entry.Ready });
                foreach (var condition in entry.Conditions) AppendCondition(hovered, condition, 0);
            }
        }
        if (hovered == null) return;
        float width = Mathf.Min(430 * k, Screen.width - 24), innerWidth = width - 20 * k, height = 16 * k;
        var heights = new List<float>();
        foreach (var line in hovered) { float h = Mathf.Max(20 * k, tooltip!.CalcHeight(new GUIContent(line.Text), innerWidth)); heights.Add(h); height += h; }
        height = Mathf.Min(Screen.height - 24, height);
        var box = new Rect(Mathf.Clamp(pointer.x + 14 * k, 12, Screen.width - width - 12), Mathf.Clamp(pointer.y + 18 * k, 12, Screen.height - height - 12), width, height);
        GUI.DrawTexture(box, background!);
        GUI.BeginGroup(box);
        try
        {
            float y = 8 * k;
            for (int i = 0; i < hovered.Count; i++)
            {
                if (hovered[i].Ready) GUI.DrawTexture(new Rect(4 * k, y, width - 8 * k, heights[i]), readyBackground!);
                tooltip!.normal.textColor = hovered[i].Color; GUI.Label(new Rect(10 * k, y, innerWidth, heights[i]), hovered[i].Text, tooltip); y += heights[i];
            }
        }
        finally { GUI.EndGroup(); }
    }
    private static void AppendCondition(List<Line> text, ConditionSnapshot condition, int depth)
    {
        string indent = new(' ', depth * 2);
        if (condition.Children.Count == 1) { AppendCondition(text, condition.Children[0], depth); return; }
        if (condition.Children.Count > 0)
        {
            text.Add(new() { Text = indent + L.Text(condition.Operator == "OR" || condition.Operator == "或" ? "或" : "且") + " (" });
            foreach (var child in condition.Children) AppendCondition(text, child, depth + 1);
            text.Add(new() { Text = indent + ")" }); return;
        }
        text.Add(new() { Text = indent + condition.Text, Color = Palette.State(!condition.Known ? 0 : condition.Satisfied ? 1 : -1) });
    }
    public void Dispose()
    {
        if (font != null) UnityEngine.Object.Destroy(font);
        if (background != null) UnityEngine.Object.Destroy(background);
        if (readyBackground != null) UnityEngine.Object.Destroy(readyBackground);
    }
}
