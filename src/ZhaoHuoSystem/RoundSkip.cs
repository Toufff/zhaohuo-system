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
/// 「确定吗？」 for a few seconds; the second gives up the round's remaining actions one at a time, checking missions
/// after each exactly as the game does when a location's story returns to the map (F72): a story due at that point
/// (main missions such as 4-3 唐门内战 need a set round with 2 or 1 actions left) plays there and the rest of the
/// round is kept; with none due, the last one is the round change (next round, shop and income). In the story
/// scene it shows only while a menu whose options spend action points waits (choosing where to go), never mid-story;
/// a free phase that runs inside the story scene (破庙 on 云裳线, …) comes back to its menu after the round change
/// as the game's own does, instead of going to the map (F80).
/// Hidden while a main story waits at a location (the game allows only that), while the map is busy or a native panel
/// is open, and with 「隐藏全部悬浮窗」.
/// </summary>
internal sealed class RoundSkip(ManualLogSource log, Func<bool> actionMenu, Func<string, string?> scriptText) : IDisposable
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
    /// The free phase's story menu: the waiting choice put away so it cannot run during the change, then the points
    /// given up one at a time. A free phase on the map side goes on as a story ending with LuaManager.ChangeScene("Free"):
    /// the map unless a story is due. One that runs inside the story scene goes on as its own actions do, with
    /// ChangeScene("Story"): the story scene, which plays the story due or else this menu again.
    /// </summary>
    private void SkipStory()
    {
        var stats = PlayerStatManagerData.Instance;
        string menu = stats.CurrentStoryScript ?? "";
        bool loop = LuaOptions.StoryLoop(stats.CurrentTravelScript ?? "", menu, scriptText);
        var dialog = Fungus.MenuDialog.ActiveMenuDialog;
        if (dialog != null) dialog.Clear();
        // ChangeScene runs the mission check itself; here each step has run it once already, and a second run would
        // advance a main mission twice. So its other steps, then the scene the steps decided.
        SaveSystem.Instance.SaveUniverseData();
        bool story = SpendStepwise(stats, loop ? "story-loop" : "story", loop ? menu : null);
        StoryMainUI.Instance.Hide();
        StoryManager.Instance.SetHotKey(active: false);
        if (story || loop) Mortal.Core.SceneController.Instance.LoadStory(); else Mortal.Core.SceneController.Instance.LoadFree();
    }

    /// <summary>
    /// One action point at a time, each followed by what LuaManager.ChangeScene checks after an action's story:
    /// UpdateCheckMissions (at 0 it is the round change), then HasAnyMissionTrigger. Each check runs once per step: it
    /// moves a main mission on when it starts. Stops at the first story due; true when one is. Inside a story-scene
    /// loop (<paramref name="menu"/>), also when the check set another story script: the story scene plays it wherever
    /// the player is.
    /// </summary>
    private bool SpendStepwise(PlayerStatManagerData stats, string where, string? menu = null)
    {
        int left = stats.CurrentActionCount;
        string start = $"{stats.GameTime.Year}/{stats.GameTime.Month}/{stats.GameTime.Stage}";
        var missions = MissionManagerData.Instance;
        for (int value = left - 1; value >= 0; value--)
        {
            // Set, not spent: 「本旬行动点无限」 holds back spending, and the points still have to go down here.
            stats.Stats.Set(GameStatType.行動次數, value);
            missions.UpdateCheckMissions();
            bool due = missions.HasAnyMissionTrigger();
            if (!due && (menu == null || string.Equals(stats.CurrentStoryScript, menu, StringComparison.OrdinalIgnoreCase))) continue;
            log.LogInfo($"ROUND_SKIP {where} left={left} time={start} stopped={value} story={stats.CurrentStoryScript}");
            return true;
        }
        log.LogInfo($"ROUND_SKIP {where} left={left} time={start} done={stats.GameTime.Year}/{stats.GameTime.Month}/{stats.GameTime.Stage} actions={stats.CurrentActionCount} next={(menu == null ? "Free" : menu)}");
        return false;
    }

    private IEnumerator Skip(FreeManager free)
    {
        var stats = PlayerStatManagerData.Instance;
        // As a location action starts: the map stops taking input, then the screen closes.
        free.SetHotKey(active: false); free.DisableButton(); free.SetProcessing();
        yield return new WaitForSeconds(.3f);
        yield return free.TransitionScreen();
        // What LuaManager.ChangeScene("Free") does when a story ends, once per action point given up.
        SaveSystem.Instance.SaveUniverseData();
        bool story = SpendStepwise(stats, "map");
        if (story) Mortal.Core.SceneController.Instance.LoadStory(); else Mortal.Core.SceneController.Instance.LoadFree();
    }

    public void Dispose()
    {
        if (button != null) UnityEngine.Object.Destroy(button.gameObject);
        if (font != null) UnityEngine.Object.Destroy(font);
    }
}
