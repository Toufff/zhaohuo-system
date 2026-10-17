using System;
using System.Collections;
using BepInEx.Logging;
using Mortal.Core;
using Mortal.Free;
using Mortal.Story;
using UnityEngine;
using UnityEngine.UI;

namespace ZhaoHuoSystem;

/// <summary>
/// 「跳过本旬」 right of the date, on the free map and in the free phase's story menus (F60). The first click asks
/// 「确定吗？」 for a few seconds; the second gives up the round's remaining actions and runs what the game does when a
/// story returns to the map with none left: next round, shop and income, then any mission that is due. In the story
/// scene it shows only while a menu whose options spend action points waits (choosing where to go), never mid-story.
/// Hidden while a main story waits at a location (the game allows only that), while the map is busy or a native panel
/// is open, and with 「隐藏全部悬浮窗」.
/// </summary>
internal sealed class RoundSkip(ManualLogSource log, Func<bool> actionMenu) : IDisposable
{
    private const float ConfirmSeconds = 3f;
    // FreeManager on the map, StoryManager in the free phase's story menus.
    private MonoBehaviour? host;
    private Text? date;
    private Button? button;
    private Text? label;
    /// <summary>「本旬行动点无限」 lines up right after it while it shows.</summary>
    public Button? Button => button;
    private Font? font;
    private long revision = -1;
    private float confirmUntil;
    private bool running;

    public void Update(bool hidden)
    {
        MonoBehaviour? now = FreeManager.Instance != null ? FreeManager.Instance : StoryManager.Instance != null ? StoryManager.Instance : null;
        if (now == null) { host = null; return; }
        if (now != host) Attach(now);
        if (button == null || date == null || label == null) return;
        bool show = !hidden && !running && date.isActiveAndEnabled && Available(now);
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

    // A new scene comes with a new manager; the old button went with the old scene.
    private void Attach(MonoBehaviour now)
    {
        host = now; running = false; confirmUntil = 0; button = null; label = null;
        date = R.Field<Text>(R.Field<GameDateTimeText>(now, "_timeText") ?? R.Field<GameDateTimeText>(now, "_gameTime"), "_text");
        if (date == null) { log.LogWarning("ROUND_SKIP no date text"); return; }
        button = InlineButton.Create("ZhaoHuo.SkipRound", date.transform, Click, out var created); label = created;
    }

    private bool Available(MonoBehaviour now)
    {
        if (Time.timeScale == 0f) return false;
        if (now is FreeManager free)
        {
            if (free.IsProcessing || R.Field(free, "_menuOpen") is true || R.Field(free, "_statOpen") is true || R.Field(free, "_saveOpen") is true) return false;
        }
        else if (!actionMenu()) return false;
        var scenes = Mortal.Core.SceneController.Instance;
        if (scenes == null || scenes.IsPrepare || scenes.IsLoading) return false;
        var missions = MissionManagerData.Instance;
        if (missions == null || (missions.MainMissionStart && missions.MainMissionPosition != PositionType.無)) return false;
        return PlayerStatManagerData.Instance != null && PlayerStatManagerData.Instance.CurrentActionCount > 0;
    }

    private void Click()
    {
        if (host == null) return;
        if (Time.unscaledTime >= confirmUntil) { confirmUntil = Time.unscaledTime + ConfirmSeconds; return; }
        if (!Available(host)) return;
        confirmUntil = 0; running = true;
        if (host is FreeManager free) free.StartCoroutine(Skip(free));
        else SkipStory();
    }

    /// <summary>
    /// The free phase's story menu: no points left, the waiting choice put away so it cannot run during the change, then
    /// what a story does when it ends (LuaManager.ChangeScene("Free")), which with no points left is the round change.
    /// </summary>
    private void SkipStory()
    {
        var stats = PlayerStatManagerData.Instance;
        log.LogInfo($"ROUND_SKIP story left={stats.CurrentActionCount} time={stats.GameTime.Year}/{stats.GameTime.Month}/{stats.GameTime.Stage}");
        stats.Stats.Set(GameStatType.行動次數, 0);
        var menu = Fungus.MenuDialog.ActiveMenuDialog;
        if (menu != null) menu.Clear();
        LuaManager.Instance.ChangeScene("Free", "", "");
        log.LogInfo($"ROUND_SKIP story done time={stats.GameTime.Year}/{stats.GameTime.Month}/{stats.GameTime.Stage} actions={stats.CurrentActionCount}");
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
