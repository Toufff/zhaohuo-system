using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ZhaoHuoSystem;

/// <summary>
/// Battle-tab toggles. They only adjust values the game is about to apply inside an active fight and never write
/// saves or permanent stats. 战役 (Mortal.Battle) is real time; 决斗 (Mortal.Combat) is the turn-based duel.
/// Targets are resolved by name like the input gates, so a missing one only disables its own effect.
/// </summary>
internal static class CombatCheats
{
    internal static bool Invincible, OneHit, KeepStamina, NoCooldown;
    /// <summary>决斗 only: neither side's HP drops below 1, so a duel can be dragged out for its rounds (F63).</summary>
    internal static bool KeepAlive;
    /// <summary>The duel's own speed-up toggle runs at this multiplier instead of its fixed 2× (F53).</summary>
    internal static int DuelSpeed = 2;
    internal static readonly int[] DuelSpeeds = { 2, 3, 4 };
    internal static bool Installed { get; private set; }
    internal static readonly List<string> Missing = new();
    private const int PlayerSide = 0, FriendSide = 1, EnemySide = 2; // Mortal.Battle.EntityType
    private static ManualLogSource? log;
    private static int scripted; // inside a level script's own HP/气 change (CombatEventActionModifyStat)
    private static FieldInfo? entityField, originField, cooldownField, speedField, currentSpeedField;
    private static PropertyInfo? battleHealth, combatInstance, playerAction, enemyAction, combatStat, combatHealth, battleInstance;
    private static Type? battleCharacter;

    internal enum Mode { None, Battle, Combat }

    internal static Mode Current
    {
        get
        {
            if (battleInstance?.GetValue(null, null) is UnityEngine.Object battle && battle != null) return Mode.Battle;
            if (combatInstance?.GetValue(null, null) is UnityEngine.Object combat && combat != null) return Mode.Combat;
            return Mode.None;
        }
    }

    internal static void Install(Harmony harmony, ManualLogSource logger)
    {
        log = logger;
        battleCharacter = AccessTools.TypeByName("Mortal.Battle.Character");
        var entity = AccessTools.TypeByName("Mortal.Battle.Entity");
        var combatManager = AccessTools.TypeByName("Mortal.Combat.CombatManager");
        var controller = AccessTools.TypeByName("Mortal.Combat.CombatActionController");
        entityField = battleCharacter == null ? null : AccessTools.Field(battleCharacter, "_entity");
        originField = entity == null ? null : AccessTools.Field(entity, "_originType");
        battleHealth = battleCharacter == null ? null : AccessTools.Property(battleCharacter, "CurrentHealth");
        battleInstance = AccessTools.Property(AccessTools.TypeByName("Mortal.Battle.GameLevelManager"), "Instance");
        combatInstance = combatManager == null ? null : AccessTools.Property(combatManager, "Instance");
        playerAction = combatManager == null ? null : AccessTools.Property(combatManager, "PlayerAction");
        enemyAction = combatManager == null ? null : AccessTools.Property(combatManager, "EnemyAction");
        combatStat = controller == null ? null : AccessTools.Property(controller, "Stat");
        combatHealth = AccessTools.Property(AccessTools.TypeByName("Mortal.Combat.CombatStatController"), "CurrentHealth");
        var skillButton = AccessTools.TypeByName("Mortal.Battle.SkillButton");
        cooldownField = skillButton == null ? null : AccessTools.Field(skillButton, "_currentCooldownTime");
        if (entityField == null || originField == null || battleHealth == null) Missing.Add("Mortal.Battle side/health");
        if (combatInstance == null || playerAction == null || enemyAction == null || combatStat == null || combatHealth == null) Missing.Add("Mortal.Combat controllers");
        speedField = combatManager == null ? null : AccessTools.Field(combatManager, "_speedTime");
        currentSpeedField = combatManager == null ? null : AccessTools.Field(combatManager, "_currentSpeedTime");
        if (speedField == null || currentSpeedField == null) Missing.Add("Mortal.Combat speed");

        Patch(harmony, "Mortal.Battle.Character:TakeDamage", prefix: nameof(BattleDamage));
        Patch(harmony, "Mortal.Battle.CharacterMana:Modify", prefix: nameof(BattleMana));
        Patch(harmony, "Mortal.Battle.SkillButton:CoolDownSkill", postfix: nameof(BattleCooldown));
        Patch(harmony, "Mortal.Combat.CombatManager:GetTotalDamage", postfix: nameof(CombatDamage));
        Patch(harmony, "Mortal.Combat.CombatActionController:ModifyHealth", prefix: nameof(CombatHealth));
        Patch(harmony, "Mortal.Combat.CombatActionController:ModifyStamina", prefix: nameof(CombatStamina));
        Patch(harmony, "Mortal.Combat.CombatManager:EnableSpeedTime", prefix: nameof(CombatSpeed));
        // F94: every buff's effect units go through each of these steps every round; a step a unit leaves to the base class
        // only waits a frame there (yield return null). Frames are not shortened by the speed-up (timeScale), so with many
        // buffs the pause between rounds grew to seconds at any speed. Those steps now end at once; a unit's own steps run as before.
        foreach (string step in UnitSteps) Patch(harmony, "Mortal.Combat.CombatStateEffectUnitScriptable:" + step, prefix: nameof(NoIdleFrame));
        // The duel's timing log showed the bigger part: each state, its units and each level event run as coroutines yielded
        // inside one another, and Unity resumes a coroutine a frame after a nested one that ends at once. One state taken off
        // told all 46 states on both sides, about 63 frames, and every round passed thousands of such frames. The round's
        // coroutines (from the start of the duel and from each chosen action) now run nested ones in place; they still give the
        // frame back for every real wait.
        Patch(harmony, "Mortal.Combat.CombatManager:InitCombatRound", postfix: nameof(Flat));
        Patch(harmony, "Mortal.Combat.CombatManager:StartAction", postfix: nameof(Flat));
        // Debuff icons have no cast animation: their Cast/CastFinish are the base class's frame waits too.
        Patch(harmony, "Mortal.Combat.CombatStateEffectUI:Cast", prefix: nameof(NoIdleFrame));
        Patch(harmony, "Mortal.Combat.CombatStateEffectUI:CastFinish", prefix: nameof(NoIdleFrame));
        // About fifty effects send their buff icon back after each step that may have cast it, moved or not: a full-length
        // tween from where it already is. Only an icon that moved goes back now.
        Patch(harmony, "Mortal.Combat.CombatBuffStateUI:CastFinish", prefix: nameof(BuffIconBack));
        // Script-driven HP/气 changes must still land so HP-threshold story exits keep working.
        var scriptAction = AccessTools.Method(AccessTools.TypeByName("Mortal.Combat.CombatEventActionModifyStat"), "Action");
        var moveNext = scriptAction == null ? null : AccessTools.EnumeratorMoveNext(scriptAction);
        if (moveNext == null) Missing.Add("Mortal.Combat.CombatEventActionModifyStat:Action");
        else
        {
            try { harmony.Patch(moveNext, prefix: new HarmonyMethod(typeof(CombatCheats), nameof(ScriptBegin)), finalizer: new HarmonyMethod(typeof(CombatCheats), nameof(ScriptEnd))); }
            catch (Exception ex) { Missing.Add("Mortal.Combat.CombatEventActionModifyStat:Action " + ex.GetBaseException().Message); }
        }
        foreach (var missing in Missing) logger.LogWarning("COMBAT missing " + missing);
        Installed = true;
        logger.LogInfo($"COMBAT ready missing={Missing.Count} invincible={Invincible} oneHit={OneHit} keepStamina={KeepStamina} noCooldown={NoCooldown} keepAlive={KeepAlive} duelSpeed={DuelSpeed}");
    }

    internal static readonly string[] UnitSteps = { "Init", "ApplyBeforeAction", "ApplyInitAction", "ApplyAfterAction", "ApplyBeforeResult", "ApplyAfterResult",
        "TriggerAfterAttacked", "TriggerDuringAttack", "Remove", "OnStateEffectRemoved" };
    private static bool NoIdleFrame(ref IEnumerator __result) { __result = Nothing(); return false; }
    private static IEnumerator Nothing() { yield break; }
    private static void Flat(ref IEnumerator __result) { if (__result != null) __result = Flatten(__result); }
    /// <summary>
    /// Runs a coroutine and every coroutine it yields in one go, giving the frame back only for a real wait: a frame, seconds,
    /// a coroutine started on its own, or a polled condition such as a tween's end or the player's input.
    /// </summary>
    internal static IEnumerator Flatten(IEnumerator root)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var top = stack.Peek();
            if (!top.MoveNext()) { stack.Pop(); continue; }
            object current = top.Current;
            if (current is IEnumerator nested && current is not CustomYieldInstruction) { stack.Push(nested); continue; }
            yield return current;
        }
    }
    private static bool BuffIconBack(object __instance, ref IEnumerator __result)
    {
        try
        {
            if (R.Field(__instance, "_movePanel") is not Component panel || panel == null || R.Field(panel, "_rectTransform") is not RectTransform rect || rect == null
                || R.Field(panel, "_originAnchoredPosition") is not Vector3 origin || ((Vector2)origin - rect.anchoredPosition).sqrMagnitude > .01f) return true;
            __result = Nothing(); return false;
        }
        catch (Exception ex) { Fail("BuffIconBack", ex); return true; }
    }

    // The toggle (and its remembered state at the duel's start) goes through EnableSpeedTime, which copies _speedTime.
    private static void CombatSpeed(object __instance)
    {
        try { speedField?.SetValue(__instance, (float)DuelSpeed); }
        catch (Exception ex) { Fail("CombatSpeed", ex); }
    }

    /// <summary>A multiplier picked mid-duel applies at once while the speed-up is on (not while a game panel holds time at 0).</summary>
    internal static void ApplyDuelSpeed()
    {
        if (combatInstance?.GetValue(null, null) is not UnityEngine.Object manager || manager == null || speedField == null || currentSpeedField == null) return;
        speedField.SetValue(manager, (float)DuelSpeed);
        if (currentSpeedField.GetValue(manager) is not float current || current <= 1f) return;
        currentSpeedField.SetValue(manager, (float)DuelSpeed);
        if (Time.timeScale > 0) Time.timeScale = DuelSpeed;
    }

    private static void Patch(Harmony harmony, string target, string? prefix = null, string? postfix = null)
    {
        // A game update may change a target; only that effect is lost.
        try
        {
            int colon = target.IndexOf(':');
            var method = AccessTools.Method(AccessTools.TypeByName(target.Substring(0, colon)), target.Substring(colon + 1));
            if (method == null) { Missing.Add(target); return; }
            harmony.Patch(method, prefix: prefix == null ? null : new HarmonyMethod(typeof(CombatCheats), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(CombatCheats), postfix));
        }
        catch (Exception ex) { Missing.Add(target + " " + ex.GetBaseException().Message); }
    }

    private static int Side(object? character)
    {
        if (character is not UnityEngine.Object alive || alive == null) return -1;
        var entity = entityField?.GetValue(character);
        return entity == null || originField == null ? -1 : Convert.ToInt32(originField.GetValue(entity));
    }

    /// <summary>战役: our side skips the hit exactly like the game's own Ignore; enemies take at least their remaining HP.</summary>
    private static bool BattleDamage(object __instance, ref int damage, object attacker, ref bool __result)
    {
        try
        {
            if (!Invincible && !OneHit) return true;
            int side = Side(__instance);
            if (Invincible && (side == PlayerSide || side == FriendSide)) { __result = true; return false; }
            if (OneHit && side == EnemySide && damage > 0 && Side(attacker) != EnemySide && battleHealth?.GetValue(__instance, null) is int health)
                damage = Math.Max(damage, health);
        }
        catch (Exception ex) { Fail("TakeDamage", ex); }
        return true;
    }

    private static bool BattleMana(object __instance, int amount)
    {
        try
        {
            if (!KeepStamina || amount >= 0 || __instance is not Component mana || battleCharacter == null) return true;
            return Side(mana.GetComponent(battleCharacter)) != PlayerSide;
        }
        catch (Exception ex) { Fail("CharacterMana", ex); return true; }
    }

    private static void BattleCooldown(object __instance)
    {
        // The button's own Update ends a zero-length cooldown on the next frame and restores its icon.
        try { if (NoCooldown) cooldownField?.SetValue(__instance, 0f); }
        catch (Exception ex) { Fail("SkillButton", ex); }
    }

    /// <summary>决斗 damage preview and popups agree with the clamp applied in ModifyHealth.</summary>
    private static void CombatDamage(object attacker, object attacked, ref int __result)
    {
        try
        {
            if (!Invincible && !OneHit) return;
            var manager = combatInstance?.GetValue(null, null);
            if (manager == null) return;
            if (Invincible && ReferenceEquals(attacked, playerAction!.GetValue(manager, null))) __result = 0;
            else if (OneHit && __result > 0 && ReferenceEquals(attacked, enemyAction!.GetValue(manager, null)))
                __result = Math.Max(__result, CombatHealthOf(attacked));
        }
        catch (Exception ex) { Fail("GetTotalDamage", ex); }
    }

    private static void CombatHealth(object __instance, ref int value)
    {
        try
        {
            if (value >= 0 || !Invincible && !OneHit && !KeepAlive) return;
            var manager = combatInstance?.GetValue(null, null);
            if (manager == null) return;
            if (Invincible && ReferenceEquals(__instance, playerAction!.GetValue(manager, null)))
                // Scripted HP loss still applies but never kills; everything else is ignored.
                value = scripted > 0 ? Math.Min(0, Math.Max(value, 1 - CombatHealthOf(__instance))) : 0;
            else if (OneHit && scripted == 0 && ReferenceEquals(__instance, enemyAction!.GetValue(manager, null)))
                value = Math.Min(value, -CombatHealthOf(__instance)); // native minimum-HP floors still clamp
            // Last, over 一击必杀 too: down to 1 HP and no further, for either side, scripted or not.
            if (KeepAlive && (ReferenceEquals(__instance, playerAction!.GetValue(manager, null)) || ReferenceEquals(__instance, enemyAction!.GetValue(manager, null))))
                value = Math.Min(0, Math.Max(value, 1 - CombatHealthOf(__instance)));
        }
        catch (Exception ex) { Fail("ModifyHealth", ex); }
    }

    private static void CombatStamina(object __instance, ref int value)
    {
        try
        {
            if (!KeepStamina || value >= 0 || scripted > 0) return;
            var manager = combatInstance?.GetValue(null, null);
            if (manager != null && ReferenceEquals(__instance, playerAction!.GetValue(manager, null))) value = 0;
        }
        catch (Exception ex) { Fail("ModifyStamina", ex); }
    }

    private static int CombatHealthOf(object controller) => combatHealth?.GetValue(combatStat?.GetValue(controller, null), null) is int health ? health : 0;
    private static void ScriptBegin() => scripted++;
    private static void ScriptEnd() => scripted = Math.Max(0, scripted - 1);
    private static readonly HashSet<string> reported = new();
    private static void Fail(string where, Exception ex)
    {
        if (reported.Add(where)) log?.LogWarning("COMBAT " + where + " " + ex.GetBaseException().Message + "; native value kept");
    }
}
