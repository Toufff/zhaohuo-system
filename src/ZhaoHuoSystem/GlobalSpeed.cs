using UnityEngine;

namespace ZhaoHuoSystem;

/// <summary>
/// 全局加速 (F102): the game's whole clock (Time.timeScale) runs at a multiple, in the story, on the map and in both kinds of
/// fight. The fights set the clock themselves (their pause, the duel's own speed): what the game set is the base and the
/// multiple goes on top, a pause staying a pause. A value written here carries a tiny offset, so a value the game writes
/// later is told apart even when it equals one written here. Turned off, the game's own value is put back.
/// </summary>
internal static class GlobalSpeed
{
    internal static readonly float[] Multiples = { 1.5f, 2f, 3f };
    internal static bool On;
    internal static int Level = 1;
    internal static float Multiple => Multiples[Mathf.Clamp(Level, 0, Multiples.Length - 1)];
    internal static string Label(int level) => Multiples[Mathf.Clamp(level, 0, Multiples.Length - 1)].ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "×";
    private const float Mark = .0003f;
    private static float baseScale = 1, written = float.NaN;

    /// <summary>Once a frame, early: takes up what the game set since, and keeps the multiple on it.</summary>
    internal static void Tick()
    {
        float now = Time.timeScale;
        bool ours = !float.IsNaN(written) && Mathf.Abs(now - written) < 1e-6f;
        if (!ours) baseScale = now;
        if (!On)
        {
            if (ours) Time.timeScale = baseScale;
            written = float.NaN;
            return;
        }
        float want = baseScale <= 0 ? 0 : baseScale * Multiple + Mark;
        if (Mathf.Abs(now - want) > 1e-6f) Time.timeScale = want;
        written = Time.timeScale;
    }

    /// <summary>The mod is going away: the game keeps its own clock.</summary>
    internal static void Release() { On = false; Tick(); }
}
