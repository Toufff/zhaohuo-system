using System;
using BepInEx.Logging;
using HarmonyLib;
using Mortal.Core;
using Mortal.Free;
using Mortal.Story;
using UnityEngine;
using UnityEngine.UI;

namespace ZhaoHuoSystem;

/// <summary>
/// 「本旬行动点无限」 right of the date, beside 「跳过本旬」 and the same size (F59, F61), wherever the action points show
/// (the walking map and the free phase's story menus). A click fills the points up and, for the rest of this round,
/// nothing spends them; the next round (the game's own change, or 「跳过本旬」, which sets the points to 0) ends it. A
/// second click ends it at once. Never saved: it belongs to the round it was switched on in.
/// </summary>
internal sealed class RoundUnlimited(ManualLogSource log, Func<Button?> skip) : IDisposable
{
    // The game's GameTime changes in place at a round change, so the round is kept by value.
    private static (int Year, int Month, MonthStageType Stage)? round;
    /// <summary>Switched on in the round the player is in now.</summary>
    public static bool Active => round is { } on && PlayerStatManagerData.Instance is PlayerStatManagerData p && p != null && p.GameTime is GameTime now
        && on.Year == now.Year && on.Month == now.Month && on.Stage == now.Stage;

    // The dice buttons' colours; switched on takes the panel's accent.
    private static readonly Color Idle = new(.15f, .18f, .21f, 1), On = new(.22f, .37f, .51f, 1);
    // FreeManager on the map, StoryManager in the free phase's story menus: both show the date.
    private MonoBehaviour? host;
    private Text? date;
    private PlayerActionInfoPanel? panel;
    private float nextLook;
    private Button? button;
    private Text? label;
    private Font? font;
    private long revision = -1;

    public void Update(bool hidden)
    {
        MonoBehaviour? now = FreeManager.Instance != null ? FreeManager.Instance : StoryManager.Instance != null ? StoryManager.Instance : null;
        if (now == null) { host = null; return; }
        if (now != host) Attach(now);
        if (button == null || label == null || date == null) return;
        // Only where the action points show; the panel is looked for now and then, not every frame.
        if (panel == null && Time.unscaledTime >= nextLook) { nextLook = Time.unscaledTime + .5f; panel = UnityEngine.Object.FindObjectOfType<PlayerActionInfoPanel>(); }
        bool show = !hidden && date.isActiveAndEnabled && panel != null && panel.isActiveAndEnabled && PlayerStatManagerData.Instance != null;
        if (button.gameObject.activeSelf != show) button.gameObject.SetActive(show);
        if (!show) return;
        if (font == null || revision != L.Revision)
        {
            if (font != null) UnityEngine.Object.Destroy(font);
            font = Font.CreateDynamicFontFromOSFont(L.FontNames, 18); revision = L.Revision;
        }
        bool on = Active;
        // The same size as 「跳过本旬」 (F46's half size), right after it, or in its place when it is not offered.
        InlineButton.Place(button, label, date, font, L.Text(on ? "本旬行动点无限中" : "本旬行动点无限"), .4f);
        if (skip() is Button beside && beside != null && beside.gameObject.activeSelf)
        {
            var own = (RectTransform)button.transform;
            own.anchoredPosition += new Vector2(((RectTransform)beside.transform).sizeDelta.x + own.sizeDelta.y * .3f, 0);
        }
        var colors = button.colors; var want = on ? On : Idle;
        if (colors.normalColor != want) { colors.normalColor = want; button.colors = colors; }
    }

    // A new scene comes with a new manager and date; the old button went with the old scene.
    private void Attach(MonoBehaviour now)
    {
        host = now; button = null; label = null; panel = null; nextLook = 0;
        date = R.Field<Text>(R.Field<GameDateTimeText>(now, "_timeText") ?? R.Field<GameDateTimeText>(now, "_gameTime"), "_text");
        if (date == null) { log.LogWarning("ROUND_UNLIMITED no date text"); return; }
        button = InlineButton.Create("ZhaoHuo.RoundUnlimited", date.transform, Click, out var created); label = created;
    }

    private void Click()
    {
        var p = PlayerStatManagerData.Instance;
        if (p == null) return;
        if (Active) { round = null; log.LogInfo("ROUND_UNLIMITED off"); return; }
        round = (p.GameTime.Year, p.GameTime.Month, p.GameTime.Stage);
        // Full points first, so options needing more than one are open too.
        p.Stats.Set(GameStatType.行動次數, Math.Max(p.CurrentActionCount, p.GetMaxActionCount()));
        log.LogInfo($"ROUND_UNLIMITED on time={p.GameTime.Year}/{p.GameTime.Month}/{p.GameTime.Stage} actions={p.CurrentActionCount}");
    }

    /// <summary>Back at the title, nothing carries over to whatever is loaded next.</summary>
    public static void Reset() => round = null;

    public void Dispose()
    {
        if (button != null) UnityEngine.Object.Destroy(button.gameObject);
        if (font != null) UnityEngine.Object.Destroy(font);
    }
}

// While switched on, the round's action points are not spent: every spend goes through GameStat.AddValue with a negative
// value (map locations via FreeManager.UpdateActionValue, stories via statmodifymanager). Setting them (round change,
// 「跳过本旬」) is untouched.
[HarmonyPatch(typeof(GameStat), nameof(GameStat.AddValue))]
internal static class RoundUnlimitedPatch
{
    private static bool Prefix(GameStat __instance, int value) => value >= 0 || __instance.StatType != GameStatType.行動次數 || !RoundUnlimited.Active;
}
