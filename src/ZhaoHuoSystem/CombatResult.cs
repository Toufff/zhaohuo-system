using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Mortal.Core;
using UnityEngine;

namespace ZhaoHuoSystem;

/// <summary>
/// 「直接胜利」「直接失败」 at the top centre of both fights (F52), to try a story's other branch quickly. Each takes two
/// clicks. They end the fight through the game's own result paths, so the story reads the same flags as after a real
/// win or loss:
/// 决斗 calls <c>CombatManager.GameOver(win)</c> (what the game's own debug result button calls): win or lose flags,
/// then the next scene, or the game-over screen for a duel whose loss is a dead end.
/// 战役 calls <c>GameLevelManager.ShowGameOver</c> (what a level's scripted forced result calls): a win sets the result
/// flag; a loss is the losing result the story goes on from where the level has one (lose on defeat or on time, or a
/// scripted enemy win), and otherwise the death panel (retry or title), the same as being killed. 战役 locks and hides the
/// mouse while it runs and frees it only in its pause menu, so there the buttons show while paused and unpause first.
/// They also show on the 战役's opening 「出征」 panel, where the mouse is free (F121): the opening waiting for 出征 is
/// stopped and the panel closed the way 出征 closes it, then the same result path runs.
/// 「直接僵持」 (F85) joins them only in a duel that can end in a draw: the level has an event that, on a set round,
/// leaves the fight without a result (its actions load the next scene). It runs that event's own actions (lines, flags,
/// the next scene), as reaching that round would. Exits on HP or 气 thresholds are not draws and do not count.
/// Resolved by name like <see cref="CombatCheats"/>; anything missing only hides the buttons.
/// </summary>
internal sealed class CombatResult : IDisposable
{
    private const int PlayerDie = 1, FriendWin = 2, EnemyWin = 3; // Mortal.Battle.GameOverType
    private const int ForcedEnemyWin = 1; // Mortal.Battle.BattleForceResult
    private const float ConfirmTime = 3f;
    private readonly ManualLogSource log;
    private readonly PropertyInfo? battleInstance, battleReady, battleOver, battlePause, battleCloseReady, combatInstance, combatHotKey;
    private readonly MethodInfo? showGameOver, pauseGame, combatGameOver, hideAction;
    private readonly Type? gameOverType;
    private int ended = -1; // the duel this mod already ended, until its scene goes
    private int pending = -1;
    private float pendingUntil;
    // Whether the duel's level has a draw (F85), worked out once per level.
    private object? drawLevel;
    private bool drawAvailable;
    private const int Win = 0, Lose = 1, Stalemate = 2;
    private float styleScale = -1;
    private long revision = -1;
    private Font? font;
    private Texture2D? normal, hover, confirm;
    private GUIStyle? button, confirming;
    private int skinKey = int.MinValue;
    /// <summary>Both buttons in GUI coordinates, empty while hidden. The input guard keeps game clicks out of it.</summary>
    public Rect Rect { get; private set; }

    public CombatResult(ManualLogSource log)
    {
        this.log = log;
        var battle = AccessTools.TypeByName("Mortal.Battle.GameLevelManager");
        var combat = AccessTools.TypeByName("Mortal.Combat.CombatManager");
        gameOverType = AccessTools.TypeByName("Mortal.Battle.GameOverType");
        if (battle != null)
        {
            battleInstance = AccessTools.Property(battle, "Instance"); battleReady = AccessTools.Property(battle, "IsReady");
            battleOver = AccessTools.Property(battle, "IsGameOver"); battlePause = AccessTools.Property(battle, "IsPause");
            battleCloseReady = AccessTools.Property(battle, "IsCloseReady");
            if (gameOverType != null) showGameOver = AccessTools.Method(battle, "ShowGameOver", new[] { gameOverType, typeof(bool) });
            pauseGame = AccessTools.Method(battle, "PauseGame", new[] { typeof(bool) });
        }
        if (combat != null)
        {
            combatInstance = AccessTools.Property(combat, "Instance"); combatHotKey = AccessTools.Property(combat, "EnableHotKey");
            combatGameOver = AccessTools.Method(combat, "GameOver", new[] { typeof(bool) });
        }
        hideAction = AccessTools.Method(AccessTools.TypeByName("Mortal.Combat.CombatUIManager"), "HideAction");
        if (battleInstance == null || battleReady == null || battleOver == null || battlePause == null || showGameOver == null || pauseGame == null) log.LogWarning("COMBAT_RESULT battle entries missing; buttons hidden in 战役");
        if (combatInstance == null || combatHotKey == null || combatGameOver == null) log.LogWarning("COMBAT_RESULT duel entries missing; buttons hidden in 决斗");
    }

    /// <summary>
    /// The fight that can be ended now, or null: started and not already ending; a 战役 in its pause menu or on its opening
    /// 「出征」 panel (Opening), a 决斗 running.
    /// </summary>
    private (MonoBehaviour Manager, bool Battle, bool Opening)? Active()
    {
        if (SceneController.Instance != null && SceneController.Instance.IsLoading) return null;
        if (battleInstance?.GetValue(null, null) is MonoBehaviour battle && battle != null)
        {
            if (battleOver!.GetValue(battle, null) is not false || showGameOver == null || pauseGame == null) return null;
            if (battleReady!.GetValue(battle, null) is true) return battlePause!.GetValue(battle, null) is true ? (battle, true, false) : null;
            return OpeningPanel(battle) != null ? (battle, true, true) : null;
        }
        if (combatInstance?.GetValue(null, null) is MonoBehaviour combat && combat != null)
            return Time.timeScale > 0 && combatHotKey!.GetValue(combat, null) is true && R.Field(combat, "_combatLevel") != null && combat.GetInstanceID() != ended && combatGameOver != null
                ? (combat, false, false) : null;
        return null;
    }

    /// <summary>The 「出征」 panel while it waits for its button, fully shown; null otherwise.</summary>
    private MonoBehaviour? OpeningPanel(MonoBehaviour battle)
        => battleCloseReady?.GetValue(battle, null) is false && R.Field(battle, "_readyPanel") is MonoBehaviour panel && panel != null && panel.isActiveAndEnabled
            && R.Field(panel, "_container") is CanvasGroup container && container != null && container.alpha > .99f ? panel : null;

    /// <summary>Layout, input and paint; call before the panel so the buttons win their own clicks.</summary>
    public void Draw(bool hidden)
    {
        var fight = hidden ? null : Active();
        if (fight == null) { Rect = default; pending = -1; return; }
        EnsureStyles();
        int[] choices = !fight.Value.Battle && HasDraw(fight.Value.Manager) ? new[] { Win, Stalemate, Lose } : new[] { Win, Lose };
        float k = UiScale.Current, width = 96 * k, height = 28 * k, gap = 8 * k;
        float x = (Screen.width - width * choices.Length - gap * (choices.Length - 1)) / 2, y = 6 * k;
        Rect = new Rect(x, y, width * choices.Length + gap * (choices.Length - 1), height);
        if (pending >= 0 && Time.unscaledTime > pendingUntil) pending = -1;
        for (int i = 0; i < choices.Length; i++)
        {
            int choice = choices[i];
            bool asking = pending == choice;
            string label = asking ? L.Text("再点一次确认") : L.Text(choice == Win ? "直接胜利" : choice == Stalemate ? "直接僵持" : "直接失败");
            var place = new Rect(x + i * (width + gap), y, width, height);
            var style = asking ? confirming! : button!;
            // 界面风格 (F103): the game's framed button, lit while it asks to be pressed again.
            if (!GUI.Button(place, label, OverlaySkin.Frame(place, k, asking) ? OverlaySkin.Bare(style) : style)) continue;
            if (!asking) { pending = choice; pendingUntil = Time.unscaledTime + ConfirmTime; continue; }
            pending = -1;
            try
            {
                if (choice == Stalemate) FinishDraw(fight.Value.Manager);
                else Finish(fight.Value.Manager, fight.Value.Battle, choice == Win, fight.Value.Opening);
            }
            catch (Exception ex) { log.LogWarning("COMBAT_RESULT " + ex.GetBaseException().Message); }
        }
        var e = Event.current;
        if (Rect.Contains(e.mousePosition) && (e.isMouse || e.type == EventType.ScrollWheel)) e.Use();
    }

    private void Finish(MonoBehaviour manager, bool battle, bool win, bool opening = false)
    {
        if (battle)
        {
            int type = win ? FriendWin : StoryLoss(R.Field(manager, "_levelData")) ? EnemyWin : PlayerDie;
            if (opening && OpeningPanel(manager) is MonoBehaviour panel)
            {
                // The opening waits for 出征 and would then start the fight; it stops here, and the panel closes as 出征 closes it.
                manager.StopAllCoroutines();
                AccessTools.Method(panel.GetType(), "StartButtonClick")?.Invoke(panel, null);
            }
            // Out of the pause menu first (time runs again); the result panel brings the mouse back.
            else pauseGame!.Invoke(manager, new object[] { false });
            showGameOver!.Invoke(manager, new[] { Enum.ToObject(gameOverType!, type), type != PlayerDie });
            log.LogInfo($"COMBAT_RESULT battle win={win} type={Enum.ToObject(gameOverType!, type)} opening={opening}");
            return;
        }
        // The duel's own round loop stops first, so a pending action or a natural end cannot run alongside.
        manager.StopAllCoroutines();
        if (R.Field(manager, "_uiManager") is object ui) hideAction?.Invoke(ui, null);
        ended = manager.GetInstanceID();
        manager.StartCoroutine((IEnumerator)combatGameOver!.Invoke(manager, new object[] { win }));
        log.LogInfo($"COMBAT_RESULT duel win={win}");
    }

    private bool HasDraw(MonoBehaviour manager)
    {
        var level = R.Field(manager, "_combatLevel");
        if (!ReferenceEquals(level, drawLevel)) { drawLevel = level; drawAvailable = Draws(manager).Any(); }
        return drawAvailable;
    }

    /// <summary>The level's draw events: on a set round (a flag may pick which), leaving the fight by loading the next scene.</summary>
    private static IEnumerable<object> Draws(MonoBehaviour manager)
    {
        if (R.Field(manager, "_combatLevel") is not object level) yield break;
        foreach (var data in R.Items(R.Field(level, "Events")))
            foreach (var item in R.Items(R.Field(data, "_conditionAction")))
            {
                if (item == null) continue;
                var conditions = R.Items(R.Field(item, "_conditions")).Where(condition => condition != null).ToList();
                if (!conditions.Exists(condition => condition.GetType().Name == "CombatEventConditionRound") || conditions.Exists(condition => condition.GetType().Name == "CombatEventConditionStat")) continue;
                if (Exit(item).Exists(action => action.GetType().Name == "CombatEventActionLoadScene")) yield return item;
            }
    }

    /// <summary>The event's actions in the order the duel runs them (round start, after the action, after the result), up to leaving.</summary>
    private static List<object> Exit(object item)
    {
        var actions = new List<object>();
        foreach (string list in new[] { "_actions", "_afterActions", "_afterResult" })
            foreach (var action in R.Items(R.Field(item, list)))
            {
                if (action == null) continue;
                actions.Add(action);
                if (action.GetType().Name == "CombatEventActionLoadScene") return actions;
            }
        return actions;
    }

    /// <summary>The draw whose other conditions hold now (the round is the one this skips to), run as the duel would run it.</summary>
    private void FinishDraw(MonoBehaviour manager)
    {
        bool Holds(object condition) => AccessTools.Method(condition.GetType(), "Check")?.Invoke(condition, new object[] { manager }) is true;
        var draw = Draws(manager).FirstOrDefault(item => R.Field(item, "_logic") is LogicOperatorType.OR
            || R.Items(R.Field(item, "_conditions")).Where(condition => condition != null && condition.GetType().Name != "CombatEventConditionRound").All(Holds));
        if (draw == null) { log.LogWarning("COMBAT_RESULT duel draw: none applies now"); return; }
        manager.StopAllCoroutines();
        if (R.Field(manager, "_uiManager") is object ui) hideAction?.Invoke(ui, null);
        ended = manager.GetInstanceID();
        manager.StartCoroutine(Run(manager, Exit(draw)));
        log.LogInfo("COMBAT_RESULT duel draw " + R.Name(draw));
    }
    private static IEnumerator Run(MonoBehaviour manager, List<object> actions)
    {
        foreach (var action in actions)
            if (AccessTools.Method(action.GetType(), "Action")?.Invoke(action, new object[] { manager }) is IEnumerator step) yield return step;
    }

    /// <summary>Whether the level has a loss the story goes on from: lose on defeat or on time, or a scripted enemy win.</summary>
    private static bool StoryLoss(object? level)
    {
        if (level == null) return false;
        if (R.Field(level, "_victoryDefeat") is true || R.Field(level, "_victoryTimeout") is true) return true;
        foreach (var data in R.Items(R.Field(level, "_events")))
            foreach (var action in R.Items(R.Field(data, "_actions")))
                if (action.GetType().Name == "BattleEventActionFlashGameOver" && R.Field(action, "_result") is Enum result && Convert.ToInt32(result) == ForcedEnemyWin) return true;
        return false;
    }

    private void EnsureStyles()
    {
        float k = UiScale.Current;
        if (button != null && Mathf.Abs(styleScale - k) < .01f && revision == L.Revision && skinKey == OverlaySkin.Key) return;
        styleScale = k; skinKey = OverlaySkin.Key;
        if (font == null || revision != L.Revision)
        {
            if (font != null) UnityEngine.Object.Destroy(font);
            font = Font.CreateDynamicFontFromOSFont(L.FontNames, 16); revision = L.Revision;
        }
        // The dice buttons' colours; the confirming one takes the panel's accent.
        normal ??= Solid(new Color(.15f, .18f, .21f, .92f)); hover ??= Solid(new Color(.25f, .32f, .39f, .95f)); confirm ??= Solid(new Color(.29f, .46f, .61f, .95f));
        GUIStyle Style(Texture2D background, Texture2D over)
        {
            var style = new GUIStyle { font = OverlaySkin.Font(font!), fontSize = Mathf.RoundToInt(14 * k), alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
            style.normal.background = background; style.hover.background = style.active.background = over;
            style.normal.textColor = style.hover.textColor = style.active.textColor = Palette.Text;
            return style;
        }
        button = Style(normal, hover); confirming = Style(confirm, confirm);
    }

    private static Texture2D Solid(Color color)
    {
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave }; texture.SetPixel(0, 0, color); texture.Apply(); return texture;
    }

    public void Dispose()
    {
        if (font != null) UnityEngine.Object.Destroy(font);
        foreach (var texture in new[] { normal, hover, confirm }) if (texture != null) UnityEngine.Object.Destroy(texture);
    }
}
