using System;
using System.Collections.Generic;
using System.Linq;
using Fungus;
using UnityEngine;

namespace ZhaoHuoSystem;

/// <summary>
/// F107: a break menu (议题, 走访 and the like) hides a row whose condition fails, whole; the game still builds its button.
/// Under the menu's rows a small 「另有 N 个选项未满足条件」 opens a box of the mod's own that names those options and what
/// each needs (or that the game never offers it). The game's rows and choices are left as they are; 设置/显示被隐藏的选项
/// turns it off.
/// </summary>
internal sealed class HiddenOptions(OptionHints hints) : IDisposable
{
    private MenuDialog? menu;
    private bool dirty, expanded;
    private List<OptionHints.HiddenRow> rows = new();
    private Font? font;
    private GUIStyle? label, note, button, hint;
    private Texture2D? fill, buttonFill;
    private float styleScale = -1;
    private long revision = -1;
    private int skinKey = int.MinValue;
    /// <summary>The button and the open box, in GUI coordinates; the input guard keeps the game's clicks out of it.</summary>
    public Rect Rect { get; private set; }

    /// <summary>A break row was filled in (shown or hidden): its menu's hidden rows are read again before the next draw.</summary>
    public void RowUpdated(Component row)
    {
        var owner = row.GetComponentInParent<MenuDialog>();
        if (owner == null) return;
        if (owner != menu) { menu = owner; expanded = false; }
        dirty = true;
    }
    public void Clear() { menu = null; rows.Clear(); expanded = false; Rect = default; }

    public void Draw(bool off)
    {
        Rect = default;
        if (off || menu == null || !menu.isActiveAndEnabled) { expanded = false; return; }
        // The game turns the menu's buttons off while its own menu, 回想 or another panel covers the menu: so does this button.
        var visible = menu.CachedButtons.Where(b => b != null && b.gameObject.activeInHierarchy && b.interactable && b.enabled).ToList();
        if (visible.Count == 0) { expanded = false; return; }
        if (dirty) { rows = hints.HiddenRows(menu); dirty = false; }
        if (rows.Count == 0) return;
        EnsureStyles();
        float k = UiScale.Current;
        var area = visible.Select(b => ScreenRect((RectTransform)b.transform)).Aggregate((a, b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax)));
        string caption = L.Format("另有 {0} 个选项未满足条件", rows.Count) + (expanded ? " ▾" : " ▸");
        // Values changed through the panel do not change the menu by themselves: the button's second line says it refreshes.
        string refresh = L.Text("修改条件后点击本按钮刷新选项");
        float bw = Mathf.Max(button!.CalcSize(new GUIContent(caption)).x, hint!.CalcSize(new GUIContent(refresh)).x) + 24 * k, bh = 48 * k;
        // Right of the menu's big title (「休憩」), else under its lowest row.
        Rect at;
        if ((menu.transform.Find("Container/Title/Text") ?? menu.transform.Find("Title/Text"))?.GetComponent<UnityEngine.UI.Text>() is { } title && title.isActiveAndEnabled)
        {
            var t = ScreenRect(title.rectTransform);
            float drawn = Mathf.Min(title.preferredWidth, title.rectTransform.rect.width) * (t.width / Mathf.Max(1, title.rectTransform.rect.width));
            at = new Rect(Mathf.Clamp(t.xMin + drawn + 18 * k, 8, Screen.width - bw - 8), Mathf.Clamp(t.center.y - bh / 2, 8, Screen.height - bh - 8), bw, bh);
        }
        else at = new Rect(Mathf.Clamp(area.xMin, 8, Screen.width - bw - 8), Mathf.Min(area.yMax + 6 * k, Screen.height - bh - 8), bw, bh);
        Rect box = default;
        string body = "";
        if (expanded)
        {
            body = string.Join("\n", rows.Select(Line));
            float width = Mathf.Min(Mathf.Max(area.width, 420 * k), Screen.width - 24), pad = 12 * k;
            float height = Mathf.Min(note!.CalcHeight(new GUIContent(body), width - pad * 2) + pad * 2 + 4 * k, Screen.height - 24);
            float y = at.yMax + 4 * k + height <= Screen.height - 8 ? at.yMax + 4 * k : at.y - 4 * k - height;
            box = new Rect(Mathf.Clamp(at.x, 12, Screen.width - width - 12), Mathf.Max(8, y), width, height);
        }
        Rect = expanded ? Rect.MinMaxRect(Mathf.Min(at.xMin, box.xMin), Mathf.Min(at.yMin, box.yMin), Mathf.Max(at.xMax, box.xMax), Mathf.Max(at.yMax, box.yMax)) : at;
        var e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            if (at.Contains(e.mousePosition))
            {
                e.Use();
                // Values as they are now: options whose condition holds now are shown in the menu; with none, the box opens or closes.
                int shown = hints.HiddenRows(menu).Count(hints.Reveal);
                rows = hints.HiddenRows(menu);
                if (shown == 0) expanded = !expanded;
                return;
            }
            if (expanded && box.Contains(e.mousePosition)) { e.Use(); return; }
            // A press elsewhere closes the box and still reaches the game.
            if (expanded) expanded = false;
            return;
        }
        if (e.type != EventType.Repaint) return;
        var old = GUI.color; int depth = GUI.depth;
        try
        {
            GUI.color = Color.white; GUI.depth = -9998;
            bool framed = OverlaySkin.Frame(at, k, expanded);
            if (!framed) GUI.DrawTexture(at, buttonFill!);
            GUI.Label(new Rect(at.x, at.y + 3 * k, at.width, at.height * .55f), caption, button);
            GUI.Label(new Rect(at.x, at.y + at.height * .52f, at.width, at.height * .42f), refresh, hint);
            if (expanded)
            {
                if (!OverlaySkin.Panel(box, k)) GUI.DrawTexture(box, fill!);
                float pad = 12 * k;
                GUI.Label(new Rect(box.x + pad, box.y + pad, box.width - pad * 2, box.height - pad * 2), body, note);
            }
        }
        finally { GUI.color = old; GUI.depth = depth; }
    }

    // A hidden option: its name, then what it needs (red / green as in the option hints), or that the game never offers it.
    private static string Line(OptionHints.HiddenRow row)
    {
        if (row.Unreachable) return "<color=#9A9FA5>" + row.Text + " · " + L.Text("游戏里不可达") + "</color>";
        return "<color=#E8D3A8>" + row.Text + "</color>\n　" + L.Format("条件：{0}", row.Need);
    }

    private static Rect ScreenRect(RectTransform rect)
    {
        var canvas = rect.GetComponentInParent<Canvas>()?.rootCanvas;
        Camera? camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, corners[0]), b = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Screen.height - Mathf.Max(a.y, b.y), Mathf.Max(a.x, b.x), Screen.height - Mathf.Min(a.y, b.y));
    }

    private void EnsureStyles()
    {
        float k = UiScale.Current;
        if (label != null && Mathf.Abs(styleScale - k) < .01f && revision == L.Revision && skinKey == OverlaySkin.Key) return;
        styleScale = k; skinKey = OverlaySkin.Key;
        if (font == null || revision != L.Revision)
        {
            if (font != null) UnityEngine.Object.Destroy(font);
            font = Font.CreateDynamicFontFromOSFont(L.FontNames, 16); revision = L.Revision;
        }
        fill ??= OverlaySkin.Solid(new Color(.05f, .06f, .07f, .94f));
        buttonFill ??= OverlaySkin.Solid(new Color(.15f, .18f, .21f, .92f));
        label = new GUIStyle { font = OverlaySkin.Font(font), fontSize = Mathf.RoundToInt(15 * k), richText = true };
        label.normal.textColor = Palette.Text;
        note = new GUIStyle(label) { wordWrap = true, alignment = TextAnchor.UpperLeft };
        button = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(14 * k) };
        button.normal.textColor = OverlaySkin.On ? OverlaySkin.Gold : Palette.Text;
        hint = new GUIStyle(button) { fontSize = Mathf.RoundToInt(12 * k) };
        hint.normal.textColor = Palette.Unknown;
    }

    public void Dispose()
    {
        if (font != null) UnityEngine.Object.Destroy(font);
        foreach (var texture in new[] { fill, buttonFill }) if (texture != null) UnityEngine.Object.Destroy(texture);
    }
}
