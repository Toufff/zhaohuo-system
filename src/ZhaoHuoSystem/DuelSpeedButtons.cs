using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ZhaoHuoSystem;

/// <summary>
/// 2× / 3× / 4× right above the duel's own speed-up button (F53): how fast its speed-up runs; the original is 2×. The
/// game's toggle still switches between normal speed and sped up. The row lives in that button's own container, so it
/// shows, hides and leaves with it.
/// </summary>
internal sealed class DuelSpeedButtons(ManualLogSource log) : IDisposable
{
    // The dice buttons' colours; the chosen speed takes the panel's accent.
    private static readonly Color Idle = new(.15f, .18f, .21f, 1), Chosen = new(.22f, .37f, .51f, 1);
    private const float Width = 40, Height = 30, Gap = 4;
    private static readonly PropertyInfo? Instance = AccessTools.Property(AccessTools.TypeByName("Mortal.Combat.CombatManager"), "Instance");
    private readonly List<(int Speed, Button Button, Text Label)> buttons = new();
    private MonoBehaviour? attached;
    private GameObject? row;
    private Font? font;
    private long revision = -1;
    public Action? Changed;

    public void Update(bool hidden)
    {
        if (Instance?.GetValue(null, null) is not MonoBehaviour manager || manager == null) return;
        if (attached != manager || row == null) Attach(manager);
        if (row == null) return;
        if (row.activeSelf == hidden) row.SetActive(!hidden);
        if (hidden) return;
        if (font == null || revision != L.Revision)
        {
            if (font != null) UnityEngine.Object.Destroy(font);
            font = Font.CreateDynamicFontFromOSFont(L.FontNames, 20); revision = L.Revision;
        }
        foreach (var (speed, button, label) in buttons)
        {
            label.font = font; label.fontSize = 20; label.text = speed + "×";
            var colors = button.colors; var want = CombatCheats.DuelSpeed == speed ? Chosen : Idle;
            if (colors.normalColor != want) { colors.normalColor = want; button.colors = colors; }
            // 界面风格 (F103): the game's framed button, the chosen speed lit.
            InlineButton.Skin(button, Height, CombatCheats.DuelSpeed == speed);
        }
    }

    private void Attach(MonoBehaviour manager)
    {
        attached = manager; buttons.Clear(); row = null;
        var ui = R.Field(manager, "_uiManager");
        if (R.Field<Toggle>(ui, "_speedButtonToggle") is not Toggle toggle || toggle == null || toggle.transform.parent is not RectTransform container)
        { log.LogWarning("DUEL_SPEED no speed button"); return; }
        row = new GameObject("ZhaoHuo.DuelSpeed", typeof(RectTransform));
        var rect = (RectTransform)row.transform; rect.SetParent(container, false);
        // On top of the speed button, right edges aligned; narrow enough to stay clear of the action buttons on its left.
        rect.anchorMin = rect.anchorMax = new Vector2(1, 1); rect.pivot = new Vector2(1, 0);
        rect.anchoredPosition = new Vector2(0, Gap); rect.sizeDelta = new Vector2(Width * CombatCheats.DuelSpeeds.Length + Gap * (CombatCheats.DuelSpeeds.Length - 1), Height);
        for (int i = 0; i < CombatCheats.DuelSpeeds.Length; i++)
        {
            int speed = CombatCheats.DuelSpeeds[i];
            var button = InlineButton.Create("Speed" + speed, rect, () => Pick(speed), out var label);
            var own = (RectTransform)button.transform;
            own.anchorMin = own.anchorMax = own.pivot = new Vector2(0, .5f);
            own.anchoredPosition = new Vector2(i * (Width + Gap), 0); own.sizeDelta = new Vector2(Width, Height);
            button.gameObject.SetActive(true);
            buttons.Add((speed, button, label));
        }
    }

    private void Pick(int speed)
    {
        CombatCheats.DuelSpeed = speed; CombatCheats.ApplyDuelSpeed(); Changed?.Invoke();
        log.LogInfo("DUEL_SPEED " + speed);
    }

    public void Dispose()
    {
        if (row != null) UnityEngine.Object.Destroy(row);
        if (font != null) UnityEngine.Object.Destroy(font);
    }
}
