using System.Collections.Generic;
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

/// <summary>
/// One size for every mod surface: the screen height against 1080p (no upper cap, so 4K is not tiny) times the
/// player's own multiplier from the settings page.
/// </summary>
internal static class UiScale
{
    public const float MinUser = .5f, MaxUser = 2.5f;
    public static float User = 1f;
    public static float Current => Mathf.Clamp(Screen.height / 1080f, .85f, 4f) * User;
}

/// <summary>Upgrade panels (锻造 / 炼丹) whose per-visit count cap was lifted with 「本次取消上限」, until they open again.</summary>
internal static class PracticeLimits
{
    private static readonly HashSet<int> lifted = new();
    public static bool Lifted(Object panel) => panel != null && lifted.Contains(panel.GetInstanceID());
    public static void Lift(Object panel) => lifted.Add(panel.GetInstanceID());
    public static void Reset(Object panel) => lifted.Remove(panel.GetInstanceID());
}
