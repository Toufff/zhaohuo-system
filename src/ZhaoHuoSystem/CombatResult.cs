using System;
using System.Collections;
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
/// Resolved by name like <see cref="CombatCheats"/>; anything missing only hides the buttons.
/// </summary>
internal sealed class CombatResult : IDisposable
{
    private const int PlayerDie = 1, FriendWin = 2, EnemyWin = 3; // Mortal.Battle.GameOverType
    private const int ForcedEnemyWin = 1; // Mortal.Battle.BattleForceResult
    private const float ConfirmTime = 3f;
    private readonly ManualLogSource log;
    private readonly PropertyInfo? battleInstance, battleReady, battleOver, battlePause, combatInstance, combatHotKey;
    private readonly MethodInfo? showGameOver, pauseGame, combatGameOver, hideAction;
    private readonly Type? gameOverType;
    private int ended = -1; // the duel this mod already ended, until its scene goes
    private int pending = -1;
    private float pendingUntil;
    private float styleScale = -1;
    private long revision = -1;
    private Font? font;
    private Texture2D? normal, hover, confirm;
    private GUIStyle? button, confirming;
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

    /// <summary>The fight that can be ended now, or null: started and not already ending; a 战役 in its pause menu, a 决斗 running.</summary>
    private (MonoBehaviour Manager, bool Battle)? Active()
    {
        if (SceneController.Instance != null && SceneController.Instance.IsLoading) return null;
        if (battleInstance?.GetValue(null, null) is MonoBehaviour battle && battle != null)
            return battleReady!.GetValue(battle, null) is true && battleOver!.GetValue(battle, null) is false && battlePause!.GetValue(battle, null) is true && showGameOver != null && pauseGame != null
                ? (battle, true) : null;
        if (combatInstance?.GetValue(null, null) is MonoBehaviour combat && combat != null)
            return Time.timeScale > 0 && combatHotKey!.GetValue(combat, null) is true && R.Field(combat, "_combatLevel") != null && combat.GetInstanceID() != ended && combatGameOver != null
                ? (combat, false) : null;
        return null;
    }

    /// <summary>Layout, input and paint; call before the panel so the buttons win their own clicks.</summary>
    public void Draw(bool hidden)
    {
        var fight = hidden ? null : Active();
        if (fight == null) { Rect = default; pending = -1; return; }
        EnsureStyles();
        float k = UiScale.Current, width = 96 * k, height = 28 * k, gap = 8 * k;
        float x = (Screen.width - width * 2 - gap) / 2, y = 6 * k;
        Rect = new Rect(x, y, width * 2 + gap, height);
        if (pending >= 0 && Time.unscaledTime > pendingUntil) pending = -1;
        for (int i = 0; i < 2; i++)
        {
            bool asking = pending == i;
            string label = asking ? L.Text("再点一次确认") : L.Text(i == 0 ? "直接胜利" : "直接失败");
            if (!GUI.Button(new Rect(x + i * (width + gap), y, width, height), label, asking ? confirming : button)) continue;
            if (!asking) { pending = i; pendingUntil = Time.unscaledTime + ConfirmTime; continue; }
            pending = -1;
            try { Finish(fight.Value.Manager, fight.Value.Battle, i == 0); }
            catch (Exception ex) { log.LogWarning("COMBAT_RESULT " + ex.GetBaseException().Message); }
        }
        var e = Event.current;
        if (Rect.Contains(e.mousePosition) && (e.isMouse || e.type == EventType.ScrollWheel)) e.Use();
    }

    private void Finish(MonoBehaviour manager, bool battle, bool win)
    {
        if (battle)
        {
            int type = win ? FriendWin : StoryLoss(R.Field(manager, "_levelData")) ? EnemyWin : PlayerDie;
            // Out of the pause menu first (time runs again); the result panel brings the mouse back.
            pauseGame!.Invoke(manager, new object[] { false });
            showGameOver!.Invoke(manager, new[] { Enum.ToObject(gameOverType!, type), type != PlayerDie });
            log.LogInfo($"COMBAT_RESULT battle win={win} type={Enum.ToObject(gameOverType!, type)}");
            return;
        }
        // The duel's own round loop stops first, so a pending action or a natural end cannot run alongside.
        manager.StopAllCoroutines();
        if (R.Field(manager, "_uiManager") is object ui) hideAction?.Invoke(ui, null);
        ended = manager.GetInstanceID();
        manager.StartCoroutine((IEnumerator)combatGameOver!.Invoke(manager, new object[] { win }));
        log.LogInfo($"COMBAT_RESULT duel win={win}");
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
        if (button != null && Mathf.Abs(styleScale - k) < .01f && revision == L.Revision) return;
        styleScale = k;
        if (font == null || revision != L.Revision)
        {
            if (font != null) UnityEngine.Object.Destroy(font);
            font = Font.CreateDynamicFontFromOSFont(L.FontNames, 16); revision = L.Revision;
        }
        // The dice buttons' colours; the confirming one takes the panel's accent.
        normal ??= Solid(new Color(.15f, .18f, .21f, .92f)); hover ??= Solid(new Color(.25f, .32f, .39f, .95f)); confirm ??= Solid(new Color(.29f, .46f, .61f, .95f));
        GUIStyle Style(Texture2D background, Texture2D over)
        {
            var style = new GUIStyle { font = font, fontSize = Mathf.RoundToInt(14 * k), alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
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
