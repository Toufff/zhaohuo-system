using UnityEngine;

namespace ZhaoHuoSystem;

/// <summary>Unity colours for <see cref="Tone"/> on the mod's dark surfaces, so every panel shares one shade.</summary>
internal static class Palette
{
    public static readonly Color Positive = Parse(Tone.PositiveOnDark), Negative = Parse(Tone.NegativeOnDark);
    public static readonly Color Unknown = new(.76f, .79f, .81f), Text = new(.94f, .95f, .96f), Effects = new(.84f, .86f, .88f);
    /// <summary>1 holds, -1 fails, 0 unknown or neutral.</summary>
    public static Color State(int state) => state > 0 ? Positive : state < 0 ? Negative : Unknown;
    private static Color Parse(string hex) => ColorUtility.TryParseHtmlString(hex, out var color) ? color : Color.white;
}
