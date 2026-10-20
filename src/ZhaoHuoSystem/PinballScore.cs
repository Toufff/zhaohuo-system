using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Mortal.Pinball;
using UnityEngine;
using UnityEngine.UI;

namespace ZhaoHuoSystem;

/// <summary>
/// Pinball: a row under the native 「总分」 line, 「总分改为： 100 60 30 0」. The story after the table reads only the
/// total, in four tiers (≥100, ≥60, ≥30, below), so each button lands on one tier; the game's own 「离开」 then ends the
/// table. The total moves through the game's AddScore, so both native score lines update. Hidden once the machine is
/// broken (its own story) and with 「隐藏全部悬浮窗」.
/// </summary>
internal sealed class PinballScore(ManualLogSource log) : IDisposable
{
    private static readonly int[] Targets = { 100, 60, 30, 0 };
    private PinballManager? manager;
    private Text? total;
    private RectTransform? row;
    private Text? caption;
    private readonly List<(Button Button, Text Label, int Target)> buttons = new();
    private Font? font;
    private long revision = -1;

    public void Update(bool hidden)
    {
        var table = PinballManager.Instance;
        if (table == null) { manager = null; return; }
        if (table != manager) Attach(table);
        if (row == null || total == null || caption == null) return;
        bool show = !hidden && total.isActiveAndEnabled && R.Field(table, "_isBroken") is not true;
        if (row.gameObject.activeSelf != show) row.gameObject.SetActive(show);
        if (!show) return;
        if (font == null || revision != L.Revision)
        {
            if (font != null) UnityEngine.Object.Destroy(font);
            font = Font.CreateDynamicFontFromOSFont(L.FontNames, 18); revision = L.Revision;
        }
        // Smaller than the 50-unit score lines; the game's canvas already scales with the screen.
        int size = Mathf.Max(10, Mathf.RoundToInt(total.fontSize * .5f * UiScale.User));
        float height = size * 1.5f, gap = size * .4f;
        // Just under the visible total line, starting where its text starts.
        row.anchorMin = row.anchorMax = new Vector2(0, .5f); row.pivot = new Vector2(0, 1);
        row.anchoredPosition = new Vector2(0, -total.fontSize * .7f);
        caption.font = font; caption.fontSize = size; caption.color = total.color; caption.text = L.Text("总分改为：");
        var own = caption.rectTransform; own.anchorMin = own.anchorMax = own.pivot = new Vector2(0, 1);
        own.sizeDelta = new Vector2(caption.preferredWidth, height); own.anchoredPosition = Vector2.zero;
        float x = caption.preferredWidth + gap;
        foreach (var (button, label, target) in buttons)
        {
            label.font = font; label.fontSize = size; label.text = target.ToString();
            var place = (RectTransform)button.transform;
            place.anchorMin = place.anchorMax = place.pivot = new Vector2(0, 1);
            place.sizeDelta = new Vector2(Mathf.Max(label.preferredWidth + height * .8f, height * 1.4f), height);
            place.anchoredPosition = new Vector2(x, 0);
            InlineButton.Skin(button, height);
            x += place.sizeDelta.x + gap;
        }
        row.sizeDelta = new Vector2(x, height);
    }

    // A new table scene comes with a new manager; the old row went with the old scene.
    private void Attach(PinballManager table)
    {
        manager = table; row = null; caption = null; buttons.Clear();
        total = R.Field<Text>(table, "_totalText");
        if (total == null) { log.LogWarning("PINBALL_SCORE no total text"); return; }
        var root = new GameObject("ZhaoHuo.PinballScore", typeof(RectTransform));
        root.transform.SetParent(total.transform, false);
        row = (RectTransform)root.transform;
        var captionObject = new GameObject("Caption", typeof(RectTransform), typeof(Text)); captionObject.transform.SetParent(row, false);
        caption = captionObject.GetComponent<Text>(); caption.alignment = TextAnchor.MiddleLeft; caption.raycastTarget = false;
        caption.horizontalOverflow = HorizontalWrapMode.Overflow;
        foreach (int target in Targets)
        {
            var button = InlineButton.Create("Score." + target, row, () => SetTotal(target), out var label);
            button.gameObject.SetActive(true);
            buttons.Add((button, label, target));
        }
        root.SetActive(false);
    }

    private void SetTotal(int target)
    {
        if (manager == null || R.Field(manager, "_totalScore") is not int before) return;
        manager.AddScore(target - before);
        log.LogInfo($"PINBALL_SCORE total {before} -> {target}");
    }

    public void Dispose()
    {
        if (row != null) UnityEngine.Object.Destroy(row.gameObject);
        if (font != null) UnityEngine.Object.Destroy(font);
    }
}
