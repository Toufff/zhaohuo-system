using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using HarmonyLib;
using Mortal.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ZhaoHuoSystem;

/// <summary>
/// Order of the manual save slots on the native load and save pages: the game's slot numbers, newest first or
/// oldest first; empty slots always last. Only the displayed order and pad navigation change, never a save file.
/// The button sits right of the page's 「手动存档」 (load) or 「储存游戏」 (save) heading and steps through the three.
/// Each page keeps its own order, saved on every change, so it reopens the way it was last left (F48).
/// </summary>
internal sealed class SaveSlotSort(ManualLogSource log) : IDisposable
{
    public static readonly string[] Modes = { "排序：编号", "排序：最新在前", "排序：最早在前", "排序：收藏优先", "排序：最早在前（排除收藏）" };
    /// <summary>Whether a slot number is starred (F76: 收藏优先 puts those first, each group by number).</summary>
    public Func<string, bool>? IsFavourite;
    public int LoadMode, SaveMode;
    /// <summary>(save page?, new mode) after the player steps a page's order.</summary>
    public Action<bool, int>? Changed;
    private int ModeOf(MonoBehaviour panel) => panel is SaveGamePanel ? SaveMode : LoadMode;
    private sealed class Entry { public MonoBehaviour Panel = null!; public Text Heading = null!; public Button Button = null!; public Text Label = null!; }
    private readonly List<Entry> entries = new();
    private Font? font;
    private long revision = -1;

    /// <summary>After the page refreshed its slots from disk: put them in the chosen order.</summary>
    public void Refreshed(MonoBehaviour panel)
    {
        try { Attach(panel); Sort(panel); }
        catch (Exception ex) { log.LogWarning("SAVE_SORT " + ex.GetBaseException().Message); }
    }
    private void Sort(MonoBehaviour panel)
    {
        if (R.Field(panel, "_saveSlots") is not Array slots || slots.Length < 2) return;
        var list = slots.Cast<MonoBehaviour>().Where(slot => slot != null).ToList();
        int Number(MonoBehaviour slot) => int.TryParse(R.Field<string>(slot, "_slot", ""), out int n) ? n : int.MaxValue;
        long Saved(MonoBehaviour slot) => R.Field(slot, "_currentData") is GameSave data ? data.TimeTick : long.MinValue;
        bool Starred(MonoBehaviour slot) => IsFavourite?.Invoke(R.Field<string>(slot, "_slot", "")) == true;
        var ordered = ModeOf(panel) switch
        {
            1 => list.OrderBy(slot => Saved(slot) == long.MinValue).ThenByDescending(Saved).ThenBy(Number).ToList(),
            2 => list.OrderBy(slot => Saved(slot) == long.MinValue).ThenBy(Saved).ThenBy(Number).ToList(),
            3 => list.OrderBy(slot => Saved(slot) == long.MinValue).ThenBy(slot => Starred(slot) ? 0 : 1).ThenBy(Number).ToList(),
            // F88: the oldest saves that are not starred first (the ones to write over), then the starred ones by number.
            4 => list.OrderBy(slot => Saved(slot) == long.MinValue).ThenBy(slot => Starred(slot) ? 1 : 0).ThenBy(slot => Starred(slot) ? 0 : Saved(slot)).ThenBy(Number).ToList(),
            _ => list.OrderBy(Number).ToList()
        };
        if (ordered.SequenceEqual(list)) return;
        var array = Array.CreateInstance(slots.GetType().GetElementType()!, ordered.Count);
        for (int i = 0; i < ordered.Count; i++) { array.SetValue(ordered[i], i); ordered[i].transform.SetSiblingIndex(i); }
        R.Set(panel, "_saveSlots", array);
        // The pages wire up/down for pads by array order; the load page also links its other columns to slot 0.
        AccessTools.Method(panel.GetType(), "SetButtonNavigation")?.Invoke(panel, null);
    }

    /// <summary>The save page selects its first slot before refreshing; select the first one in the new order.</summary>
    public void Opened(MonoBehaviour panel)
    {
        try
        {
            if (ModeOf(panel) == 0 || R.Field(panel, "_saveSlots") is not Array slots || slots.Length == 0 || slots.GetValue(0) is not MonoBehaviour first) return;
            AccessTools.Method(first.GetType(), "SelectButton")?.Invoke(first, null);
        }
        catch (Exception ex) { log.LogWarning("SAVE_SORT " + ex.GetBaseException().Message); }
    }

    private void Attach(MonoBehaviour panel)
    {
        if (entries.Exists(entry => entry.Panel == panel)) return;
        var heading = (panel.transform.Find("Container/Manual") ?? panel.transform.Find("Container/Header"))?.GetComponent<Text>();
        if (heading == null) { log.LogWarning("SAVE_SORT no heading on " + panel.GetType().Name); return; }
        var button = InlineButton.Create("ZhaoHuo.SaveSort", heading.transform, () => Step(panel), out var label);
        entries.Add(new Entry { Panel = panel, Heading = heading, Button = button, Label = label });
    }

    /// <summary>A star changed: pages sorted by stars (收藏优先, 排除收藏) put the slots in order again.</summary>
    public void StarsChanged()
    {
        foreach (var entry in entries)
            if (entry.Panel != null && entry.Panel.isActiveAndEnabled && ModeOf(entry.Panel) >= 3) Refreshed(entry.Panel);
    }

    private void Step(MonoBehaviour panel)
    {
        bool save = panel is SaveGamePanel;
        int mode = (ModeOf(panel) + 1) % Modes.Length;
        if (save) SaveMode = mode; else LoadMode = mode;
        Changed?.Invoke(save, mode);
        log.LogInfo($"SAVE_SORT page={(save ? "save" : "load")} mode={mode}");
        Refreshed(panel);
    }

    public void Update(bool hidden)
    {
        entries.RemoveAll(entry => entry.Panel == null || entry.Heading == null || entry.Button == null);
        if (entries.Count == 0) return;
        if (font == null || revision != L.Revision)
        {
            if (font != null) UnityEngine.Object.Destroy(font);
            font = Font.CreateDynamicFontFromOSFont(L.FontNames, 18); revision = L.Revision;
        }
        foreach (var entry in entries)
        {
            bool show = !hidden && entry.Heading.isActiveAndEnabled;
            if (entry.Button.gameObject.activeSelf != show) entry.Button.gameObject.SetActive(show);
            // Headings are 50–69 units; the button reads at about the slot dates' size.
            if (show) InlineButton.Place(entry.Button, entry.Label, entry.Heading, font, L.Text(Modes[ModeOf(entry.Panel)]), 26f / entry.Heading.fontSize);
        }
    }

    public void Dispose()
    {
        foreach (var entry in entries) if (entry.Button != null) UnityEngine.Object.Destroy(entry.Button.gameObject);
        entries.Clear();
        if (font != null) UnityEngine.Object.Destroy(font);
    }
}
