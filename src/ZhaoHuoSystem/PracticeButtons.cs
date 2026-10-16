using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using Mortal.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ZhaoHuoSystem;

/// <summary>
/// 「本次取消上限」 right of the native 「最大消费 x/y」 line in the martial (修炼) and upgrade (锻造 / 炼丹) panels.
/// A click lifts the per-visit count cap for this opening only; points and money are spent as usual, and the next
/// opening has the game's own cap again. The button lives in the game's canvas and is the only thing it adds there.
/// </summary>
internal sealed class PracticeButtons(ManualLogSource log) : IDisposable
{
    private sealed class Entry { public MonoBehaviour Panel = null!; public Text Count = null!; public Button Button = null!; public Text Label = null!; }
    private readonly List<Entry> entries = new();
    private Font? font;
    private long revision = -1;

    /// <summary>Called when a panel opens. The upgrade panel's lift is forgotten here, so every opening starts capped.</summary>
    public void Opened(MonoBehaviour panel)
    {
        if (panel is UpgradePanel) PracticeLimits.Reset(panel);
        if (entries.Exists(entry => entry.Panel == panel)) return;
        var count = R.Field<Text>(panel, "_maxCountText");
        if (count == null) return;
        var button = InlineButton.Create("ZhaoHuo.LiftCap", count.transform, () => Lift(panel, count), out var label);
        entries.Add(new Entry { Panel = panel, Count = count, Button = button, Label = label });
    }

    /// <summary>Each frame: shown while the panel still has its cap, next to the line's visible text, at the mod's scale.</summary>
    public void Update(bool hidden)
    {
        entries.RemoveAll(entry => entry.Panel == null || entry.Count == null || entry.Button == null);
        if (entries.Count == 0) return;
        if (font == null || revision != L.Revision)
        {
            if (font != null) UnityEngine.Object.Destroy(font);
            font = Font.CreateDynamicFontFromOSFont(L.FontNames, 18); revision = L.Revision;
        }
        foreach (var entry in entries)
        {
            bool show = !hidden && entry.Count.isActiveAndEnabled && !Unlimited(entry.Panel);
            if (entry.Button.gameObject.activeSelf != show) entry.Button.gameObject.SetActive(show);
            if (show) InlineButton.Place(entry.Button, entry.Label, entry.Count, font, L.Text("本次取消上限"));
        }
    }

    private static bool Unlimited(MonoBehaviour panel) =>
        panel is MartialPanel ? R.Field(panel, "_mode") is int mode && mode != 0 : PracticeLimits.Lifted(panel);

    private void Lift(MonoBehaviour panel, Text count)
    {
        try
        {
            if (panel is MartialPanel)
            {
                // The martial panel's own unlimited mode, the one the game uses for a few story trainings.
                R.Set(panel, "_mode", 1);
                Refresh(panel, "UpdateMartialPointInfo"); Refresh(panel, "UpdateCurrentStatus");
            }
            else
            {
                PracticeLimits.Lift(panel);
                UpgradeUnlimitedPatch.Fill(panel);
                Refresh(panel, "UpdateStatus"); Refresh(panel, "UpdateButton");
            }
            count.text = L.GameText("System/Infinity", "∞");
            log.LogInfo("PRACTICE_CAP lifted " + panel.GetType().Name);
        }
        catch (Exception ex) { log.LogWarning("PRACTICE_CAP " + ex.GetBaseException().Message); }
    }
    // Native refreshers need a selected item; without one the next native refresh shows the change.
    private static void Refresh(object panel, string method)
    {
        try { AccessTools.Method(panel.GetType(), method)?.Invoke(panel, null); }
        catch (Exception) { }
    }

    public void Dispose()
    {
        foreach (var entry in entries) if (entry.Button != null) UnityEngine.Object.Destroy(entry.Button.gameObject);
        entries.Clear();
        if (font != null) UnityEngine.Object.Destroy(font);
    }
}
