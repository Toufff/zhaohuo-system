using System.Collections.Generic;
using UnityEngine;

namespace ZhaoHuoSystem;

/// <summary>
/// 界面风格 for the mod's floating surfaces outside the panel (F103): the branch box, the fights' result buttons, the hover
/// notes, the map counts and the save-note editor. Each keeps its own layout and styles; this draws the game's frames under
/// them and names the serif to use. With 界面风格 off, or before the game's sprites are found, nothing is drawn and the
/// surfaces keep their plain look.
/// </summary>
internal static class OverlaySkin
{
    /// <summary>Changes whenever the look does: styles built from it are built again.</summary>
    internal static int Key => GameSkin.Enabled ? GameSkin.Revision : -1;
    internal static bool On => GameSkin.Has(GameSkin.Part.Card);
    internal static readonly Color Gold = new(.93f, .80f, .56f, 1);
    private static Texture2D? fill;
    private static readonly Dictionary<GUIStyle, GUIStyle> bare = new();
    private static int bareKey = int.MinValue;

    /// <summary>A box in the game's small frame over a dark fill (the frame alone is see-through), on repaint; false when plain.</summary>
    internal static bool Panel(Rect r, float scale)
    {
        if (!On) return false;
        if (Event.current.type == EventType.Repaint) GUI.DrawTexture(r, fill ??= Solid(new Color(.04f, .05f, .07f, .92f)));
        GameSkin.Draw(r, GameSkin.Part.Card, scale, Color.white);
        return true;
    }

    /// <summary>The game's framed button behind a button's text, lit when asked or under the pointer; false when plain.</summary>
    internal static bool Frame(Rect r, float scale, bool lit)
    {
        if (!GameSkin.Has(GameSkin.Part.Button)) return false;
        lit |= r.Contains(Event.current.mousePosition);
        GameSkin.Draw(r, lit && GameSkin.Has(GameSkin.Part.ButtonLit) ? GameSkin.Part.ButtonLit : GameSkin.Part.Button, scale, Color.white);
        return true;
    }

    /// <summary>The game's input bar behind a text field; false when plain.</summary>
    internal static bool Field(Rect r, float scale) => GameSkin.Draw(r, GameSkin.Part.Input, scale, Color.white);

    /// <summary>The game's serif for the current language, or the OS font given (Korean has no serif of the game's).</summary>
    internal static Font Font(Font fallback, bool bold = false) => GameSkin.Serif(L.LanguageId, bold) ?? fallback;

    /// <summary>The style without its backgrounds, for text over one of the frames (kept per style).</summary>
    internal static GUIStyle Bare(GUIStyle style)
    {
        if (bareKey != Key || bare.Count > 64) { bare.Clear(); bareKey = Key; }
        if (bare.TryGetValue(style, out var plain)) return plain;
        plain = new GUIStyle(style);
        foreach (var state in new[] { plain.normal, plain.hover, plain.active, plain.focused, plain.onNormal, plain.onHover, plain.onActive, plain.onFocused }) state.background = null;
        return bare[style] = plain;
    }

    internal static Texture2D Solid(Color color)
    {
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        texture.SetPixel(0, 0, color); texture.Apply(); return texture;
    }
}
