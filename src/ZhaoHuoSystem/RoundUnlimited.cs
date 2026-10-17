using System;
using BepInEx.Logging;
using HarmonyLib;
using Mortal.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ZhaoHuoSystem;

/// <summary>
/// 「本旬无限」 under the action points in the top-left panel, wherever the game shows it (the free phase's story menus as
/// well as the walking map) (F59). A click fills the points up and, for the rest of this round,
/// nothing spends them; the next round (the game's own change, or 「跳过本旬」, which sets the points to 0) ends it. A
/// second click ends it at once. Never saved: it belongs to the round it was switched on in.
/// </summary>
internal sealed class RoundUnlimited(ManualLogSource log) : IDisposable
{
    // The game's GameTime changes in place at a round change, so the round is kept by value.
    private static (int Year, int Month, MonthStageType Stage)? round;
    /// <summary>Switched on in the round the player is in now.</summary>
    public static bool Active => round is { } on && PlayerStatManagerData.Instance is PlayerStatManagerData p && p != null && p.GameTime is GameTime now
        && on.Year == now.Year && on.Month == now.Month && on.Stage == now.Stage;

    // The dice buttons' colours; switched on takes the panel's accent.
    private static readonly Color Idle = new(.15f, .18f, .21f, 1), On = new(.22f, .37f, .51f, 1);
    private PlayerActionInfoPanel? panel;
    private float nextLook;
    private RectTransform? points;
    private Button? button;
    private Text? label;
    private Font? font;
    private long revision = -1;

    public void Update(bool hidden)
    {
        // The panel leaves with its scene; look for the next one now and then (not every frame).
        if (panel == null)
        {
            button = null; label = null; points = null;
            if (Time.unscaledTime < nextLook) return;
            nextLook = Time.unscaledTime + .5f;
            if (UnityEngine.Object.FindObjectOfType<PlayerActionInfoPanel>() is not PlayerActionInfoPanel found || found == null) return;
            Attach(found);
        }
        if (button == null || label == null || points == null) return;
        bool show = !hidden && points.gameObject.activeInHierarchy && PlayerStatManagerData.Instance != null;
        if (button.gameObject.activeSelf != show) button.gameObject.SetActive(show);
        if (!show) return;
        if (font == null || revision != L.Revision)
        {
            if (font != null) UnityEngine.Object.Destroy(font);
            font = Font.CreateDynamicFontFromOSFont(L.FontNames, 16); revision = L.Revision;
        }
        bool on = Active;
        label.font = font; label.fontSize = 15; label.text = L.Text(on ? "本旬无限中" : "本旬无限");
        var colors = button.colors; var want = on ? On : Idle;
        if (colors.normalColor != want) { colors.normalColor = want; button.colors = colors; }
        // Right under the points, inside the panel's own background.
        var rect = (RectTransform)button.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(points.anchoredPosition.x, points.anchoredPosition.y - points.rect.height - 2);
        rect.sizeDelta = new Vector2(label.preferredWidth + 14, 22);
    }

    private void Attach(PlayerActionInfoPanel found)
    {
        panel = found;
        if (R.Field<Transform>(found, "_container") is not RectTransform container || container.parent == null)
        { log.LogWarning("ROUND_UNLIMITED no action points"); return; }
        points = container;
        button = InlineButton.Create("ZhaoHuo.RoundUnlimited", container.parent, Click, out var created); label = created;
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
