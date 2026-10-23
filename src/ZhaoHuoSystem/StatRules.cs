using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Mortal.Core;
using Mortal.Story;
using UnityEngine;

namespace ZhaoHuoSystem;

/// <summary>
/// The player's stats as the panel sets the rules for them. 锁定 (F111): a locked stat keeps its value against the game
/// (story, items, fight results); the panel's own writes, loading a save and starting a game still go through. A lock lasts
/// this run, or always (kept in the config). 六维上限 (F112): 体力、内力、轻功、刀剑、暗器、拳掌 may rise to 200 or 999. The
/// game's own maximum stays 100 for every gauge and level, so its bars and radar read a raised value as full.
/// </summary>
internal static class StatRules
{
    internal enum Lock { None, Session, Always }
    private static readonly Dictionary<GameStatType, Lock> locks = new();
    private static int allowed;
    /// <summary>The 「一直锁定」 stats changed: the config keeps their names.</summary>
    public static Action<string>? AlwaysChanged;

    public static readonly int[] Caps = { 100, 200, 999 };
    public static int Cap = 100;
    // The radar's six (RadarChartPanel on the status page).
    private static readonly HashSet<GameStatType> six = new() { GameStatType.體力, GameStatType.內力, GameStatType.輕功, GameStatType.武功刀劍, GameStatType.武功暗器, GameStatType.武功拳掌 };
    private static readonly AccessTools.FieldRef<GameStat, int> MaxRef = AccessTools.FieldRefAccess<GameStat, int>("_max");
    private static readonly AccessTools.FieldRef<GameStat, int> ValueRef = AccessTools.FieldRefAccess<GameStat, int>("_value");
    private static readonly AccessTools.FieldRef<GameStat, int> LimitRef = AccessTools.FieldRefAccess<GameStat, int>("_limitAddition");

    /// <summary>行动次数 has 「本旬行动点无限」 instead.</summary>
    public static bool Lockable(GameStatType type) => type != GameStatType.行動次數;
    public static Lock LockOf(GameStatType type) => locks.TryGetValue(type, out var mode) ? mode : Lock.None;
    /// <summary>未锁 → 本次锁定 → 一直锁定 → 未锁.</summary>
    public static Lock Cycle(GameStatType type)
    {
        var next = LockOf(type) switch { Lock.None => Lock.Session, Lock.Session => Lock.Always, _ => Lock.None };
        if (next == Lock.None) locks.Remove(type); else locks[type] = next;
        AlwaysChanged?.Invoke(SavedLocks());
        return next;
    }
    public static string SavedLocks() => string.Join(",", locks.Where(pair => pair.Value == Lock.Always).Select(pair => pair.Key.ToString()));
    public static void LoadLocks(string saved)
    {
        foreach (string name in (saved ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            if (Enum.TryParse(name.Trim(), out GameStatType type) && Lockable(type)) locks[type] = Lock.Always;
    }

    /// <summary>Writes that go through a lock: the panel's own, loading a save, starting a game.</summary>
    public static IDisposable Allow() { allowed++; return new Release(); }
    private sealed class Release : IDisposable { private bool done; public void Dispose() { if (!done) { done = true; allowed--; } } }

    // Only the player's own stat objects: fights give enemies stats of the same types.
    private static bool Player(GameStat stat, out GameStatType type)
    {
        type = stat.StatType;
        var stats = PlayerStatManagerData.Instance?.Stats;
        return stats != null && ReferenceEquals(stats.Get(type), stat);
    }
    public static bool Blocks(GameStat stat) => allowed == 0 && locks.Count > 0 && Player(stat, out var type) && LockOf(type) != Lock.None;
    public static bool Raised(GameStat stat) => Cap > 100 && six.Contains(stat.StatType) && Player(stat, out _);
    /// <summary>The highest base value the stat may take now.</summary>
    public static int BaseMax(GameStat stat) => Raised(stat) ? Cap : stat.Max;
    public static int FinalMax(GameStat stat) => BaseMax(stat) + LimitRef(stat);

    /// <summary>Runs a native write with the raised maximum in place, so its own clamp keeps the value.</summary>
    internal static int Lift(GameStat stat)
    {
        int max = MaxRef(stat);
        if (Raised(stat)) MaxRef(stat) = Cap;
        return max;
    }
    internal static void Drop(GameStat stat, int max) => MaxRef(stat) = max;
    internal static int RaisedFinal(GameStat stat) => Mathf.Clamp(ValueRef(stat) + stat.AdditionValue, stat.Min, Cap + LimitRef(stat));
}

[HarmonyPatch(typeof(GameStat))]
internal static class StatRulesWritePatch
{
    [HarmonyPrefix, HarmonyPatch(nameof(GameStat.SetValue))]
    private static bool Set(GameStat __instance, out int __state) => Guard(__instance, out __state);
    [HarmonyFinalizer, HarmonyPatch(nameof(GameStat.SetValue))]
    private static void SetDone(GameStat __instance, int __state) { if (__state >= 0) StatRules.Drop(__instance, __state); }
    [HarmonyPrefix, HarmonyPatch(nameof(GameStat.AddValue))]
    private static bool Add(GameStat __instance, out int __state) => Guard(__instance, out __state);
    [HarmonyFinalizer, HarmonyPatch(nameof(GameStat.AddValue))]
    private static void AddDone(GameStat __instance, int __state) { if (__state >= 0) StatRules.Drop(__instance, __state); }
    private static bool Guard(GameStat stat, out int max)
    {
        max = -1;
        if (StatRules.Blocks(stat)) return false;
        if (StatRules.Raised(stat)) max = StatRules.Lift(stat);
        return true;
    }
    [HarmonyPostfix, HarmonyPatch(nameof(GameStat.FinalValue), MethodType.Getter)]
    private static void Final(GameStat __instance, ref int __result) { if (StatRules.Cap > 100 && StatRules.Raised(__instance)) __result = StatRules.RaisedFinal(__instance); }
}
// The story shows a change from its own numbers, not from the stat: a locked stat that did not change shows nothing.
[HarmonyPatch(typeof(StoryMainUI), nameof(StoryMainUI.DisplayStatModification))]
internal static class StatRulesNoticePatch
{
    private static bool Prefix(GameStat stat) => stat == null || !StatRules.Blocks(stat);
}
// A raised value would pick a level past the last one: the gauges' level text keeps the last.
[HarmonyPatch(typeof(GameStatUtils), nameof(GameStatUtils.GetGameStatLevel))]
internal static class StatRulesLevelPatch
{
    private static void Postfix(int levelLength, ref int __result) { if (levelLength > 0) __result = Mathf.Clamp(__result, 0, levelLength - 1); }
}
// The status page's radar draws a raised value at the chart's edge, not past it.
[HarmonyPatch(typeof(RadarChartPanel), nameof(RadarChartPanel.UpdateStatus))]
internal static class StatRulesRadarPatch
{
    private static void Postfix(RadarChartPanel __instance)
    {
        if (StatRules.Cap <= 100 || R.Field(__instance, "_uiPolygon") is not object polygon || R.Field(polygon, "VerticesDistances") is not float[] distances) return;
        for (int i = 0; i < distances.Length; i++) distances[i] = Mathf.Min(distances[i], 1f);
    }
}
// Loading a save and starting a game set the stats as they are: locks do not hold against them.
[HarmonyPatch]
internal static class StatRulesAllowPatch
{
    [HarmonyPrefix, HarmonyPatch(typeof(PlayerStatManagerData), nameof(PlayerStatManagerData.Load))]
    private static void Load(out IDisposable __state) => __state = StatRules.Allow();
    [HarmonyFinalizer, HarmonyPatch(typeof(PlayerStatManagerData), nameof(PlayerStatManagerData.Load))]
    private static void Loaded(IDisposable __state) => __state?.Dispose();
    [HarmonyPrefix, HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.NewGameData))]
    private static void New(out IDisposable __state) => __state = StatRules.Allow();
    [HarmonyFinalizer, HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.NewGameData))]
    private static void Started(IDisposable __state) => __state?.Dispose();
    [HarmonyPrefix, HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.NewActGameData))]
    private static void NewAct(out IDisposable __state) => __state = StatRules.Allow();
    [HarmonyFinalizer, HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.NewActGameData))]
    private static void ActStarted(IDisposable __state) => __state?.Dispose();
}
