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
        var root = new GameObject("ZhaoHuo.LiftCap", typeof(RectTransform), typeof(Image), typeof(Button));
        root.transform.SetParent(count.transform, false);
        var image = root.GetComponent<Image>(); image.color = Color.white;
        var button = root.GetComponent<Button>(); button.targetGraphic = image;
        var colors = button.colors; colors.normalColor = new Color(.15f, .18f, .21f, 1); colors.highlightedColor = new Color(.25f, .32f, .39f, 1);
        colors.pressedColor = new Color(.22f, .37f, .51f, 1); colors.selectedColor = colors.highlightedColor; button.colors = colors;
        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text)); labelObject.transform.SetParent(root.transform, false);
        var label = labelObject.GetComponent<Text>(); label.alignment = TextAnchor.MiddleCenter; label.color = new Color(.94f, .95f, .96f, 1); label.raycastTarget = false;
        var labelRect = (RectTransform)labelObject.transform; labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one; labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
        button.onClick.AddListener(() => Lift(panel, count));
        entries.Add(new Entry { Panel = panel, Count = count, Button = button, Label = label });
        root.SetActive(false);
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
            if (!show) continue;
            entry.Label.font = font; entry.Label.text = L.Text("本次取消上限");
            entry.Label.fontSize = Mathf.Max(10, Mathf.RoundToInt(entry.Count.fontSize * .8f * UiScale.User));
            // The game's canvas already scales with the screen; only the player's own multiplier is added here.
            var rect = entry.Count.rectTransform.rect; float visible = entry.Count.preferredWidth;
            float right = entry.Count.alignment switch
            {
                TextAnchor.UpperRight or TextAnchor.MiddleRight or TextAnchor.LowerRight => rect.xMax,
                TextAnchor.UpperCenter or TextAnchor.MiddleCenter or TextAnchor.LowerCenter => rect.center.x + visible / 2,
                _ => rect.xMin + visible
            };
            var own = (RectTransform)entry.Button.transform;
            own.anchorMin = own.anchorMax = new Vector2(0, .5f); own.pivot = new Vector2(0, .5f);
            float height = entry.Count.fontSize * 1.35f * UiScale.User;
            own.sizeDelta = new Vector2(entry.Label.preferredWidth + height * .8f, height);
            own.anchoredPosition = new Vector2(right - rect.xMin + height * .4f, 0);
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
