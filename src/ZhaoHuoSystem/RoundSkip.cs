using System;
using System.Collections;
using BepInEx.Logging;
using Mortal.Core;
using Mortal.Free;
using UnityEngine;
using UnityEngine.UI;

namespace ZhaoHuoSystem;

/// <summary>
/// 「跳过本旬」 right of the free map's date. The first click asks 「确定吗？」 for a few seconds; the second gives up the
/// round's remaining actions and runs what the game does when a story returns to the map with none left: next round,
/// shop and income, then any mission that is due. Hidden while a main story waits at a location (the game allows only
/// that), while the map is busy or a native panel is open, and with 「隐藏全部悬浮窗」.
/// </summary>
internal sealed class RoundSkip(ManualLogSource log) : IDisposable
{
    private const float ConfirmSeconds = 3f;
    private FreeManager? manager;
    private Text? date;
    private Button? button;
    private Text? label;
    private Font? font;
    private long revision = -1;
    private float confirmUntil;
    private bool running;

    public void Update(bool hidden)
    {
        var free = FreeManager.Instance;
        if (free == null) { manager = null; return; }
        if (free != manager) Attach(free);
        if (button == null || date == null || label == null) return;
        bool show = !hidden && !running && date.isActiveAndEnabled && Available(free);
        if (!show) confirmUntil = 0;
        if (button.gameObject.activeSelf != show) button.gameObject.SetActive(show);
        if (!show) return;
        if (font == null || revision != L.Revision)
        {
            if (font != null) UnityEngine.Object.Destroy(font);
            font = Font.CreateDynamicFontFromOSFont(L.FontNames, 18); revision = L.Revision;
        }
        // Half the usual inline size (F46): the date line is large, so .8 made the button too big.
        InlineButton.Place(button, label, date, font, L.Text(Time.unscaledTime < confirmUntil ? "确定吗？" : "跳过本旬"), .4f);
    }

    // A new free map scene comes with a new manager; the old button went with the old scene.
    private void Attach(FreeManager free)
    {
        manager = free; running = false; confirmUntil = 0; button = null; label = null;
        date = R.Field<Text>(R.Field<GameDateTimeText>(free, "_timeText"), "_text");
        if (date == null) { log.LogWarning("ROUND_SKIP no date text"); return; }
        button = InlineButton.Create("ZhaoHuo.SkipRound", date.transform, Click, out var created); label = created;
    }

    private static bool Available(FreeManager free)
    {
        if (free.IsProcessing || Time.timeScale == 0f) return false;
        if (R.Field(free, "_menuOpen") is true || R.Field(free, "_statOpen") is true || R.Field(free, "_saveOpen") is true) return false;
        var scenes = Mortal.Core.SceneController.Instance;
        if (scenes == null || scenes.IsPrepare || scenes.IsLoading) return false;
        var missions = MissionManagerData.Instance;
        if (missions == null || (missions.MainMissionStart && missions.MainMissionPosition != PositionType.無)) return false;
        return PlayerStatManagerData.Instance != null && PlayerStatManagerData.Instance.CurrentActionCount > 0;
    }

    private void Click()
    {
        if (manager == null) return;
        if (Time.unscaledTime >= confirmUntil) { confirmUntil = Time.unscaledTime + ConfirmSeconds; return; }
        if (!Available(manager)) return;
        confirmUntil = 0; running = true;
        manager.StartCoroutine(Skip(manager));
    }

    private IEnumerator Skip(FreeManager free)
    {
        var stats = PlayerStatManagerData.Instance;
        int left = stats.CurrentActionCount;
        log.LogInfo($"ROUND_SKIP left={left} time={stats.GameTime.Year}/{stats.GameTime.Month}/{stats.GameTime.Stage}");
        // As a location action starts: the map stops taking input, then the points drain and the screen closes.
        free.SetHotKey(active: false); free.DisableButton(); free.SetProcessing();
        yield return free.UpdateActionValue(-left);
        if (stats.CurrentActionCount != 0) stats.Stats.Set(GameStatType.行動次數, 0);
        yield return new WaitForSeconds(.5f);
        yield return free.TransitionScreen();
        // What LuaManager.ChangeScene("Free") does when a story ends: with no actions left this is the round change.
        SaveSystem.Instance.SaveUniverseData();
        var missions = MissionManagerData.Instance;
        missions.UpdateCheckMissions();
        bool story = missions.HasAnyMissionTrigger();
        log.LogInfo($"ROUND_SKIP done time={stats.GameTime.Year}/{stats.GameTime.Month}/{stats.GameTime.Stage} actions={stats.CurrentActionCount} next={(story ? "Story" : "Free")}");
        if (story) Mortal.Core.SceneController.Instance.LoadStory(); else Mortal.Core.SceneController.Instance.LoadFree();
    }

    public void Dispose()
    {
        if (button != null) UnityEngine.Object.Destroy(button.gameObject);
        if (font != null) UnityEngine.Object.Destroy(font);
    }
}
