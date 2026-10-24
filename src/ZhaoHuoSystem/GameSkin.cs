using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ZhaoHuoSystem;

/// <summary>
/// 界面风格 (F99): the panel drawn with the game's own UI sprites and serif font, taken from what the game has loaded. Each
/// scene brings its own assets, so every part names a sprite and a fallback found on the title; a found one is kept (held
/// here, the game does not unload it). Sprites are drawn nine-sliced at the panel's scale. A part with none of its sprites
/// found is drawn the plain way, and 设置/界面风格 turns the whole skin off. Nothing of the game's is copied or shipped.
/// </summary>
internal static class GameSkin
{
    /// <summary>Button and ButtonLit: the game's own framed button (its load page's 删除) and its glow; Row and RowSelected:
    /// its option bar, for tabs and list rows.</summary>
    internal enum Part { Window, Card, Button, ButtonLit, Row, RowSelected, Input, Line }
    /// <summary>One sprite for a part: its nine-slice border (left, bottom, right, top, in sprite pixels), the size it is drawn
    /// at relative to the panel's scale, and the clear margin around what it shows (drawn outside the rect).</summary>
    private sealed class Choice
    {
        public string Name = "";
        public Vector4 Border;
        public float Scale = 1, Pad;
    }
    private static readonly Dictionary<Part, Choice[]> Choices = new()
    {
        [Part.Window] = new[] { new Choice { Name = "back_window_menu_1", Border = new Vector4(75, 60, 60, 220), Scale = .35f } },
        [Part.Card] = new[] { new Choice { Name = "back_frame_small", Border = new Vector4(25, 25, 25, 25), Scale = 1 },
            new Choice { Name = "back_window_menu_1", Border = new Vector4(75, 60, 60, 220), Scale = .2f } },
        [Part.Button] = new[] { new Choice { Name = "button_menu_3", Border = new Vector4(70, 55, 65, 55), Scale = .27f, Pad = 40 },
            new Choice { Name = "button_option_2", Border = new Vector4(50, 45, 50, 45), Scale = .32f, Pad = 18 } },
        [Part.ButtonLit] = new[] { new Choice { Name = "button_menu_3_hover", Border = new Vector4(70, 55, 65, 55), Scale = .27f, Pad = 40 },
            new Choice { Name = "button_option_1_hover", Border = new Vector4(45, 45, 45, 45), Scale = .32f, Pad = 14 } },
        [Part.Row] = new[] { new Choice { Name = "button_option_2", Border = new Vector4(50, 45, 50, 45), Scale = .32f, Pad = 18 } },
        [Part.RowSelected] = new[] { new Choice { Name = "button_option_1_hover", Border = new Vector4(45, 45, 45, 45), Scale = .32f, Pad = 14 },
            new Choice { Name = "button_menu_3_hover", Border = new Vector4(70, 55, 65, 55), Scale = .27f, Pad = 40 } },
        [Part.Input] = new[] { new Choice { Name = "back_bar_frame_1", Border = new Vector4(8, 8, 8, 8), Scale = 1 },
            new Choice { Name = "button_option_2", Border = new Vector4(50, 45, 50, 45), Scale = .32f, Pad = 18 } },
        [Part.Line] = new[] { new Choice { Name = "back_horizontal_line_log", Scale = 1 }, new Choice { Name = "icon_line_1", Scale = 1 } },
    };
    private static readonly string[] SerifNames = { "SourceHanSerif-Regular", "SourceHanSerif-Bold", "SourceHanSerifTC-Regular", "SourceHanSerifTC-Bold", "SourceHanSerifTW-Regular", "SourceHanSerifTW-Bold",
        "DFXingShuSC24-W5", "DFXingShuStd-W5" };

    private static readonly Dictionary<string, Sprite> sprites = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Font> fonts = new(StringComparer.Ordinal);
    private static int scanned = int.MinValue;
    /// <summary>设置/界面风格: the game's look (on) or the plain one.</summary>
    internal static bool Enabled = true;
    /// <summary>Bumped when a scan finds something new, so the window rebuilds its styles with it.</summary>
    internal static int Revision { get; private set; }

    /// <summary>Once per scene: picks up the parts' sprites and the serif fonts this scene brought.</summary>
    internal static void Scan()
    {
        int scene = SceneManager.GetActiveScene().handle;
        if (scene == scanned) return;
        scanned = scene;
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var list in Choices.Values) foreach (var choice in list) if (!sprites.ContainsKey(choice.Name)) wanted.Add(choice.Name);
        bool changed = false;
        if (wanted.Count > 0)
            foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
                if (sprite != null && wanted.Contains(sprite.name) && Usable(sprite)) { sprites[sprite.name] = sprite; wanted.Remove(sprite.name); changed = true; }
        if (fonts.Count < SerifNames.Length)
            foreach (var font in Resources.FindObjectsOfTypeAll<Font>())
                if (font != null && Array.IndexOf(SerifNames, font.name) >= 0 && !fonts.ContainsKey(font.name) && Draws(font)) { fonts[font.name] = font; changed = true; }
        if (changed) Revision++;
    }
    // A packed-tight or rotated atlas sprite has no rectangle to cut from.
    private static bool Usable(Sprite sprite)
    {
        try { return sprite.texture != null && sprite.textureRect.width > 0 && (!sprite.packed || sprite.packingRotation == SpritePackingRotation.None); }
        catch { return false; }
    }
    private static bool Draws(Font font)
    {
        try { font.RequestCharactersInTexture("赵活趙"); return font.HasCharacter('赵') || font.HasCharacter('趙'); }
        catch { return false; }
    }

    /// <summary>The game's serif for this language (none for Korean, which it has no serif for): regular or bold.</summary>
    internal static Font? Serif(string language, bool bold)
    {
        // English sits beside the game's terms in its own language; Japanese needs kana the game's fonts may not have (F119).
        if (language == LocalizationCatalog.English) language = L.GameLanguageId;
        if (!Enabled || language == LocalizationCatalog.Korean || language == LocalizationCatalog.Japanese) return null;
        string[] order = language == LocalizationCatalog.Traditional
            ? (bold ? new[] { "SourceHanSerifTC-Bold", "SourceHanSerifTW-Bold", "SourceHanSerifTC-Regular" } : new[] { "SourceHanSerifTC-Regular", "SourceHanSerifTW-Regular" })
            : (bold ? new[] { "SourceHanSerif-Bold", "SourceHanSerif-Regular" } : new[] { "SourceHanSerif-Regular" });
        foreach (string name in order) if (fonts.TryGetValue(name, out var font) && font != null) return font;
        return null;
    }

    /// <summary>The game's brush script (its load page's headings) for the window's name; none for Korean.</summary>
    internal static Font? Brush(string language)
    {
        if (!Enabled || language != LocalizationCatalog.Simplified && language != LocalizationCatalog.Traditional) return null;
        return fonts.TryGetValue(language == LocalizationCatalog.Traditional ? "DFXingShuStd-W5" : "DFXingShuSC24-W5", out var font) && font != null ? font : null;
    }

    private static (Choice Choice, Sprite Sprite)? Pick(Part part)
    {
        if (!Enabled) return null;
        foreach (var choice in Choices[part]) if (sprites.TryGetValue(choice.Name, out var sprite) && sprite != null) return (choice, sprite);
        return null;
    }
    internal static bool Has(Part part) => Pick(part) != null;

    private static readonly Dictionary<string, Sprite> sliced = new(StringComparer.Ordinal);
    /// <summary>
    /// For the mod's buttons in the game's canvas: the part's sprite as a sliced one (a new sprite over the same texture, with
    /// the part's border at 100 pixels per unit), with its draw scale and clear margin; null when the part is drawn plain.
    /// </summary>
    internal static Sprite? Sliced(Part part, out float scale, out float pad)
    {
        scale = pad = 0;
        if (Pick(part) is not { } pick) return null;
        var (choice, sprite) = pick;
        scale = choice.Scale; pad = choice.Pad;
        if (!sliced.TryGetValue(choice.Name, out var made) || made == null)
            sliced[choice.Name] = made = Sprite.Create(sprite.texture, sprite.textureRect, new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, choice.Border);
        return made;
    }

    /// <summary>
    /// Draws the part over the rect, on repaint (other events only learn whether it is there): true when it is drawn the
    /// game's way, false when the caller should draw its plain look.
    /// </summary>
    internal static bool Draw(Rect r, Part part, float scale, Color tint)
    {
        if (Pick(part) is not { } pick) return false;
        if (Event.current.type != EventType.Repaint) return true;
        var (choice, sprite) = pick;
        float k = choice.Scale * scale, pad = choice.Pad * k;
        var target = new Rect(r.x - pad, r.y - pad, r.width + pad * 2, r.height + pad * 2);
        var old = GUI.color;
        GUI.color = new Color(old.r * tint.r, old.g * tint.g, old.b * tint.b, old.a * tint.a);
        try { Sliced(target, sprite, choice.Border, k); }
        finally { GUI.color = old; }
        return true;
    }

    /// <summary>Nine pieces of the sprite's rectangle in its texture, corners kept at their size (times k), edges and middle stretched.</summary>
    private static void Sliced(Rect r, Sprite sprite, Vector4 border, float k)
    {
        var texture = sprite.texture;
        var tr = sprite.textureRect;
        float l = border.x, b = border.y, rt = border.z, t = border.w;
        float dl = l * k, dr = rt * k, dt = t * k, db = b * k;
        if (dl + dr > r.width && dl + dr > 0) { float f = r.width / (dl + dr); dl *= f; dr *= f; }
        if (dt + db > r.height && dt + db > 0) { float f = r.height / (dt + db); dt *= f; db *= f; }
        float[] xs = { r.x, r.x + dl, r.xMax - dr, r.xMax }, ys = { r.y, r.y + dt, r.yMax - db, r.yMax };
        float w = texture.width, h = texture.height;
        float[] us = { tr.x / w, (tr.x + l) / w, (tr.xMax - rt) / w, tr.xMax / w };
        // Texture coordinates run from the bottom; the screen from the top.
        float[] vs = { tr.yMax / h, (tr.yMax - t) / h, (tr.y + b) / h, tr.y / h };
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                float x0 = xs[i], x1 = xs[i + 1], y0 = ys[j], y1 = ys[j + 1];
                if (x1 - x0 <= .01f || y1 - y0 <= .01f || us[i + 1] - us[i] <= 0 || vs[j] - vs[j + 1] <= 0) continue;
                GUI.DrawTextureWithTexCoords(new Rect(x0, y0, x1 - x0, y1 - y0), texture, new Rect(us[i], vs[j + 1], us[i + 1] - us[i], vs[j] - vs[j + 1]));
            }
    }
}
