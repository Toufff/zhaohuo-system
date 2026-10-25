using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZhaoHuoSystem;

// F120: the arrow keys move a focus ring over the panel's buttons and fields, by where they sit on screen; Enter or
// Space presses the button (or starts typing in the field). The keys are the panel's only while it is open and was
// last pressed on; Enter and Space only once an arrow key has shown the ring, so they still advance the game's text.
public sealed partial class ToolWindow
{
    private sealed class NavItem
    {
        public string Key = "", Control = "";
        public Rect Screen, Local;
        public int Scroll = -1;
        public bool Shown;
    }
    private List<NavItem> _nav = new(), _navLast = new();
    private readonly Dictionary<string, int> _navSeen = new(StringComparer.Ordinal);
    private readonly Stack<(int Id, Rect Shown)> _navScrolls = new();
    private int _navScrollCount;
    private string _navFocus = "", _navPress = "";
    private Rect _navFocusScreen;
    private bool _navActive;
    private int _navRevealScroll = -1;
    private Rect _navRevealRect;
    private static readonly Color NavRing = new(.96f, .80f, .38f, .95f);

    /// <summary>The keyboard belongs to the panel: it is open and was last opened or pressed on.</summary>
    public bool NavOwned { get; set; }
    /// <summary>The game must not also act on this key (arrows while the panel holds the keyboard; Enter and Space once the ring shows).</summary>
    public bool NavTakes(bool arrow, bool press) => Visible && NavOwned && !HasTextFocus && _capturing == 0 && (arrow || press && (_navActive || _languageOpen));

    /// <summary>Start of an OnGUI pass: the keys act on the last repainted controls, then this pass registers them anew.</summary>
    private void NavBegin(Event current)
    {
        if (current.type == EventType.MouseDown) { _navActive = false; if (IsPointerOver) NavOwned = true; }
        if (current.type == EventType.KeyDown && NavOwned && !HasTextFocus && _capturing == 0) NavKey(current);
        _nav.Clear(); _navSeen.Clear(); _navScrolls.Clear(); _navScrollCount = 0;
    }

    /// <summary>End of an OnGUI pass: a repaint's controls are the ones the next key moves among.</summary>
    private void NavEnd(EventType pass)
    {
        _navPress = "";
        if (pass != EventType.Repaint) return;
        (_navLast, _nav) = (_nav, _navLast);
        if (_navFocus.Length == 0 || _navLast.Exists(item => item.Key == _navFocus)) return;
        // The focused control went (a page or a list changed): the nearest one where it was takes its place.
        var near = Nearest(_navFocusScreen.center, item => item.Shown);
        _navFocus = near?.Key ?? "";
        if (near != null) _navFocusScreen = near.Screen;
    }

    private void NavKey(Event current)
    {
        if (_languageOpen) return;
        bool press = current.keyCode is KeyCode.Return or KeyCode.KeypadEnter or KeyCode.Space;
        Vector2 direction = current.keyCode switch
        {
            KeyCode.UpArrow => Vector2.down, KeyCode.DownArrow => Vector2.up,
            KeyCode.LeftArrow => Vector2.left, KeyCode.RightArrow => Vector2.right,
            _ => Vector2.zero
        };
        if (direction == Vector2.zero && !press) return;
        var focused = _navLast.Find(item => item.Key == _navFocus);
        if (press)
        {
            // Before an arrow key has shown the ring, Enter and Space are the game's.
            if (!_navActive || focused == null) return;
            if (focused.Control.Length > 0) GUI.FocusControl(focused.Control); else _navPress = focused.Key;
            current.Use();
            return;
        }
        current.Use();
        if (!_navActive || focused == null)
        {
            // The first arrow shows the ring on the control nearest the top left of what is in view.
            _navActive = true;
            focused ??= Nearest(new Vector2(WindowRect.x, WindowRect.y), item => item.Shown);
            if (focused != null) Focus(focused);
            return;
        }
        var next = Step(focused, direction);
        if (next != null) Focus(next);
    }

    private void Focus(NavItem item)
    {
        _navFocus = item.Key; _navFocusScreen = item.Screen;
        // A control out of its list's view scrolls into it on the next pass.
        if (!item.Shown && item.Scroll >= 0) { _navRevealScroll = item.Scroll; _navRevealRect = item.Local; }
    }

    /// <summary>The control the arrow points to: ahead of this one, nearest along the way and then across it.</summary>
    private NavItem? Step(NavItem from, Vector2 direction)
    {
        NavItem? best = null; float bestScore = float.MaxValue;
        Vector2 origin = from.Screen.center;
        foreach (var item in _navLast)
        {
            if (item == from) continue;
            // Out of view only within a list, which can scroll it in; never behind the panel's edge.
            if (!item.Shown && (item.Scroll < 0 || item.Scroll != from.Scroll && !WindowRect.Overlaps(item.Screen))) continue;
            Vector2 d = item.Screen.center - origin;
            float along = Vector2.Dot(d, direction), across = Mathf.Abs(direction.x != 0 ? d.y : d.x);
            // Ahead means past this control's edge, so a wide neighbour in the same row is not "below".
            float edge = direction.x != 0 ? (from.Screen.width + item.Screen.width) / 4 : (from.Screen.height + item.Screen.height) / 4;
            if (along <= edge) continue;
            float score = along + across * (direction.x != 0 ? 3 : 2);
            if (score < bestScore) { bestScore = score; best = item; }
        }
        return best;
    }

    private NavItem? Nearest(Vector2 point, Predicate<NavItem> allowed)
    {
        NavItem? best = null; float bestDistance = float.MaxValue;
        foreach (var item in _navLast)
        {
            if (!allowed(item)) continue;
            float distance = (item.Screen.center - point).sqrMagnitude;
            if (distance < bestDistance) { bestDistance = distance; best = item; }
        }
        return best;
    }

    /// <summary>Registers a control drawn at r; true when Enter or Space pressed it this pass. Draws the ring on the focused one.</summary>
    private bool NavControl(Rect r, string label, string control = "")
    {
        if (!GUI.enabled) return false;
        string name = control.Length > 0 ? control : label;
        _navSeen.TryGetValue(name, out int seen); _navSeen[name] = seen + 1;
        Vector2 min = GUIUtility.GUIToScreenPoint(r.position);
        var screen = new Rect(min, r.size);
        var item = new NavItem { Key = name + "#" + seen, Control = control, Screen = screen, Local = r };
        if (_navScrolls.Count > 0) { var (id, shown) = _navScrolls.Peek(); item.Scroll = id; item.Shown = shown.Contains(screen.center); }
        else item.Shown = WindowRect.Contains(screen.center);
        _nav.Add(item);
        bool focused = _navActive && item.Key == _navFocus;
        if (focused && Event.current.type == EventType.Repaint)
        {
            float w = Mathf.Max(1, S(2));
            GUI.DrawTexture(new Rect(r.x - w, r.y - w, r.width + w * 2, w), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, NavRing, 0, 0);
            GUI.DrawTexture(new Rect(r.x - w, r.yMax, r.width + w * 2, w), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, NavRing, 0, 0);
            GUI.DrawTexture(new Rect(r.x - w, r.y, w, r.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, NavRing, 0, 0);
            GUI.DrawTexture(new Rect(r.xMax, r.y, w, r.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, NavRing, 0, 0);
        }
        return _navPress.Length > 0 && item.Key == _navPress;
    }

    /// <summary>Before a scroll view begins: its id for the controls in it, and the scroll that brings a focused one into view.</summary>
    private Vector2 NavScroll(Rect position, Vector2 scroll)
    {
        int id = _navScrollCount++;
        var shown = new Rect(GUIUtility.GUIToScreenPoint(position.position), position.size);
        if (_navScrolls.Count > 0) { var outer = _navScrolls.Peek().Shown; shown = Rect.MinMaxRect(Mathf.Max(shown.xMin, outer.xMin), Mathf.Max(shown.yMin, outer.yMin), Mathf.Min(shown.xMax, outer.xMax), Mathf.Min(shown.yMax, outer.yMax)); }
        _navScrolls.Push((id, shown));
        if (id != _navRevealScroll) return scroll;
        _navRevealScroll = -1;
        float pad = S(6);
        if (_navRevealRect.yMin < scroll.y) scroll.y = Mathf.Max(0, _navRevealRect.yMin - pad);
        else if (_navRevealRect.yMax > scroll.y + position.height) scroll.y = _navRevealRect.yMax - position.height + pad;
        if (_navRevealRect.xMin < scroll.x) scroll.x = Mathf.Max(0, _navRevealRect.xMin - pad);
        else if (_navRevealRect.xMax > scroll.x + position.width) scroll.x = _navRevealRect.xMax - position.width + pad;
        return scroll;
    }
    private void NavScrollEnd() { if (_navScrolls.Count > 0) _navScrolls.Pop(); }

    /// <summary>The open language list: up and down choose, Enter takes it, Esc closes it.</summary>
    private void LanguageKeys(Event current, string[] items)
    {
        if (current.type != EventType.KeyDown) return;
        int count = items.Length;
        if (current.keyCode == KeyCode.UpArrow) { _languageHighlight = (_languageHighlight + count - 1) % count; current.Use(); }
        else if (current.keyCode == KeyCode.DownArrow) { _languageHighlight = (_languageHighlight + 1) % count; current.Use(); }
        else if (current.keyCode is KeyCode.Return or KeyCode.KeypadEnter or KeyCode.Space)
        {
            string id = _languageHighlight == 0 ? "" : LocalizationCatalog.Languages[_languageHighlight - 1].Id;
            _languageOpen = false;
            if (id != L.Override) LanguageChosen?.Invoke(id);
            current.Use();
        }
    }
}
