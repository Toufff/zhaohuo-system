using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using Fungus;
using HarmonyLib;
using Mortal.Core;
using Mortal.Free;
using Mortal.Story;
using UnityEngine;
using UnityEngine.UI;

namespace ZhaoHuoSystem;

/// <summary>
/// Places where the game spends time for nothing the player sees (F95, after the duel's F94). Each patch class stands
/// alone, so one that no longer fits the game is skipped by PatchEach and the others keep working. Pacing the player can
/// see (the pause after a stat change, the free-play transitions) changes only when chosen in 设置.
/// </summary>
internal static class Speedups
{
    internal static ManualLogSource? Log;
    /// <summary>设置/数值变化停顿: 0 the game's 0.5 s, 1 a short pause, 2 none.</summary>
    internal static int StatPause;
    /// <summary>设置/自由行动过场: the wait, the wipe and the fade around a free-play action at three times the speed.</summary>
    internal static bool FastFree;
    internal const float FreeFactor = 3f;

    /// <summary>
    /// Runs a coroutine with its nested ones in place (as <see cref="CombatCheats.Flatten"/>), its fixed waits shortened and
    /// the tweens it waits on played faster, by <paramref name="factor"/>.
    /// </summary>
    internal static IEnumerator Hasten(IEnumerator root, float factor)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var top = stack.Peek();
            if (!top.MoveNext()) { stack.Pop(); continue; }
            object current = top.Current;
            if (current is WaitForSeconds wait && R.Field(wait, "m_Seconds") is float seconds) { yield return new WaitForSeconds(seconds / factor); continue; }
            if (current is CustomYieldInstruction custom)
            {
                // DOTween's WaitForCompletion keeps its tween in t.
                if (R.Field(custom, "t") is object tween && R.Field(tween, "timeScale") is float scale && Mathf.Approximately(scale, 1f)) R.Set(tween, "timeScale", factor);
                yield return custom;
                continue;
            }
            if (current is IEnumerator nested) { stack.Push(nested); continue; }
            yield return current;
        }
    }
}

// Every scene change wrote a pretty-printed JSON copy of the global save (about 1 MB, growing with the read-text record)
// before the binary file the game actually reads; nothing reads the copy. The library's manual backup still writes it.
[HarmonyPatch(typeof(SaveSystem))]
internal static class UniverseJsonPatch
{
    private static bool saving;
    [HarmonyPrefix, HarmonyPatch(nameof(SaveSystem.SaveUniverseData))] private static void Begin() => saving = true;
    [HarmonyFinalizer, HarmonyPatch(nameof(SaveSystem.SaveUniverseData))] private static void End() => saving = false;
    [HarmonyPrefix, HarmonyPatch("ExecuteSaveJsonData")] private static bool Json() => !saving;
}

// GetFlag went through some 1,065 flags by their native names on every call, and loading a save calls it once per saved
// flag. By name instead, first match in the game's own order; a name it does not know still goes to the game (its warning).
[HarmonyPatch(typeof(MissionManagerData), nameof(MissionManagerData.GetFlag))]
internal static class FlagLookupPatch
{
    private static MissionManagerData? owner;
    private static FlagCollectionData[]? built;
    private static readonly Dictionary<string, FlagData> byName = new(StringComparer.Ordinal);

    private static bool Prefix(MissionManagerData __instance, string name, ref FlagData __result)
    {
        var collections = __instance.FlagsCollection;
        if (name == null || collections == null) return true;
        if (!ReferenceEquals(owner, __instance) || !ReferenceEquals(built, collections))
        {
            byName.Clear(); owner = __instance; built = collections;
            foreach (var collection in collections)
                if (collection != null && collection.List != null)
                    foreach (var flag in collection.List)
                        if (flag != null && !byName.ContainsKey(flag.name)) byName[flag.name] = flag;
        }
        if (!byName.TryGetValue(name, out var found) || found == null) return true;
        __result = found;
        return false;
    }
}

// After a stat change the story waits GetDisplayTime (always 0.5 s) whether or not anything was shown: a change of 0, one
// not to be displayed or a hidden stat waits too. It now waits only after a shown change, and for as long as 设置 says.
[HarmonyPatch]
internal static class StatPausePatch
{
    private static bool shown;
    [HarmonyPrefix, HarmonyPatch(typeof(StoryMainUI), nameof(StoryMainUI.DisplayStatModification))]
    private static void Stat(GameStat stat) { if (stat != null && stat.PropertyType != GameStatPropertyType.隱藏) shown = true; }
    [HarmonyPrefix, HarmonyPatch(typeof(StoryMainUI), nameof(StoryMainUI.DisplayRelationshipModification))]
    private static void Relation() => shown = true;
    [HarmonyPostfix, HarmonyPatch(typeof(StatModifyManager), nameof(StatModifyManager.GetDisplayTime))]
    private static void Pause(ref float __result)
    {
        __result = !shown ? 0f : Speedups.StatPause switch { 1 => .2f, 2 => 0f, _ => __result };
        shown = false;
    }
}

// The story log rebuilt its whole text (up to 250 entries, joined one by one) about twice a line, closed or not. While it is
// closed it is only marked; opening it builds it once. The history is joined in one pass, the same text as the game's.
[HarmonyPatch]
internal static class NarrativeLogPatch
{
    private static bool stale = true;
    private static readonly AccessTools.FieldRef<NarrativeLogMenu, ScrollRect> View = AccessTools.FieldRefAccess<NarrativeLogMenu, ScrollRect>("narrativeLogView");
    private static readonly AccessTools.FieldRef<NarrativeLog, NarrativeData> History = AccessTools.FieldRefAccess<NarrativeLog, NarrativeData>("history");
    private static readonly MethodInfo? Rebuild = AccessTools.Method(typeof(NarrativeLogMenu), "UpdateNarrativeLogText");

    [HarmonyPrefix, HarmonyPatch(typeof(NarrativeLogMenu), "UpdateNarrativeLogText")]
    private static bool Update(NarrativeLogMenu __instance)
    {
        var view = View(__instance);
        if (view == null || view.gameObject.activeInHierarchy) { stale = false; return true; }
        stale = true;
        return false;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(StoryManager), nameof(StoryManager.Log))]
    private static void Opened(bool enabled, NarrativeLogMenu ____narrativeLogMenu)
    {
        if (enabled && stale && ____narrativeLogMenu != null) Rebuild?.Invoke(____narrativeLogMenu, null);
    }

    [HarmonyPrefix, HarmonyPatch(typeof(NarrativeLog), nameof(NarrativeLog.GetPrettyHistory))]
    private static bool Pretty(NarrativeLog __instance, bool previousOnly, int maxEntry, ref string __result)
    {
        var entries = History(__instance)?.entries;
        if (entries == null) return true;
        var text = new StringBuilder("\n");
        int end = previousOnly ? entries.Count - 1 : entries.Count;
        for (int i = Math.Max(0, entries.Count - maxEntry); i < end; i++)
        {
            if (!string.IsNullOrEmpty(entries[i].name)) text.Append("<size=28><color=#D9B077>").Append(entries[i].name).Append("</color></size>\n");
            text.Append(entries[i].text).Append("\n\n");
        }
        __result = text.ToString();
        return false;
    }
}

// Save and load slots read their save in full to show its date, and the free map builds both pages on every visit: about
// 290 whole-file reads at 50 slots. While a slot sets itself up it gets a copy with only what it shows, kept by file and
// write time; loading a save, and every other read, still goes to the file.
[HarmonyPatch]
internal static class SlotSetupPatch
{
    internal static int Depth;
    private static IEnumerable<MethodBase> TargetMethods()
        => new[] { typeof(SaveSlotPanel), typeof(LoadSlotPanel), typeof(AutoSaveSlotPanel), typeof(RecentSaveSlotPanel) }.Select(type => (MethodBase)AccessTools.Method(type, "Setup"));
    private static void Prefix() => Depth++;
    private static void Finalizer() => Depth--;
}

internal static class SlotHeaders
{
    private sealed class Header { public DateTime Written; public long Length; public object Save = null!; }
    private static readonly Dictionary<string, Header> headers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly MethodInfo? FileOf = AccessTools.Method(typeof(SaveSystem), "GetSaveDataFile");

    /// <summary>The kept copy while a slot sets itself up and its file has not changed; otherwise the file to read and keep.</summary>
    internal static bool TryKept(SaveSystem system, string slot, out object? kept, out (string Path, DateTime Written, long Length)? read)
    {
        kept = null; read = null;
        if (SlotSetupPatch.Depth <= 0 || FileOf?.Invoke(system, new object[] { slot }) is not string path) return false;
        var info = new FileInfo(path);
        if (!info.Exists) return false;
        if (headers.TryGetValue(path, out var known) && known.Written == info.LastWriteTimeUtc && known.Length == info.Length) { kept = known.Save; return true; }
        read = (path, info.LastWriteTimeUtc, info.Length);
        return false;
    }

    internal static T Keep<T>((string Path, DateTime Written, long Length) read, T copy) where T : class
    {
        headers[read.Path] = new Header { Written = read.Written, Length = read.Length, Save = copy };
        return copy;
    }

    // What a slot shows: the game date, the save time, the demo and new-game-plus marks.
    internal static GameSave Copy(GameSave save) => new()
    {
        CurrentYear = save.CurrentYear, CurrentMonth = save.CurrentMonth, CurrentStage = save.CurrentStage,
        TimeTick = save.TimeTick, IsDemo = save.IsDemo, NewGamePlus = save.NewGamePlus
    };
}

[HarmonyPatch]
internal static class SaveHeaderPatch
{
    // The public slot overload, not the private generic reader of the same name.
    private static MethodBase TargetMethod() => typeof(SaveSystem).GetMethods(BindingFlags.Instance | BindingFlags.Public)
        .First(m => m.Name == nameof(SaveSystem.GetSaveData) && !m.IsGenericMethodDefinition && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
    private static bool Prefix(SaveSystem __instance, string slot, ref GameSave __result, out (string Path, DateTime Written, long Length)? __state)
    {
        if (!SlotHeaders.TryKept(__instance, slot, out var kept, out __state)) return true;
        __result = (GameSave)kept!;
        return false;
    }
    private static void Postfix(ref GameSave __result, (string Path, DateTime Written, long Length)? __state)
    { if (__state is { } read && __result != null) __result = SlotHeaders.Keep(read, SlotHeaders.Copy(__result)); }
}

[HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.GetAutoSaveData))]
internal static class AutoHeaderPatch
{
    private static bool Prefix(SaveSystem __instance, string slot, ref AutoGameSave __result, out (string Path, DateTime Written, long Length)? __state)
    {
        if (!SlotHeaders.TryKept(__instance, slot, out var kept, out __state)) return true;
        __result = (AutoGameSave)kept!;
        return false;
    }
    private static void Postfix(ref AutoGameSave __result, (string Path, DateTime Written, long Length)? __state)
    {
        if (__state is { } read && __result != null)
            __result = SlotHeaders.Keep(read, new AutoGameSave { Slot = __result.Slot, GameSave = __result.GameSave == null ? null! : SlotHeaders.Copy(__result.GameSave) });
    }
}

// Auto mode pressed "next" every 8 seconds whatever was on screen: a press during a wait or an animation made the next line
// appear whole and go at the following press. It now waits until a line is on screen in full, gives it time to be read by
// its length (2.5–8 s, never more than the game's 8), then turns the page. Skipping is the game's own, unchanged.
[HarmonyPatch(typeof(StoryManager), "StartAutoDialogue")]
internal static class AutoDialoguePatch
{
    private static readonly AccessTools.FieldRef<StoryManager, bool> Skip = AccessTools.FieldRefAccess<StoryManager, bool>("_skipDialog");
    private static readonly AccessTools.FieldRef<StoryManager, bool> Auto = AccessTools.FieldRefAccess<StoryManager, bool>("_autoDialog");
    private static readonly MethodInfo? Next = AccessTools.Method(typeof(StoryManager), "SetDialogNextLineFlag");

    private static bool Prefix(StoryManager __instance, ref IEnumerator __result)
    {
        if (Next == null) return true;
        __result = Run(__instance);
        return false;
    }

    internal static float ReadingTime(string text) => Mathf.Clamp(1.5f + Regex.Replace(text ?? "", "<[^>]+>", "").Trim().Length * .06f, 2.5f, 8f);

    private static IEnumerator Run(StoryManager manager)
    {
        while (true)
        {
            if (Skip(manager)) { Next!.Invoke(manager, null); yield return null; continue; }
            var say = SayDialog.ActiveSayDialog;
            var writer = say != null ? say.GetComponent<Writer>() : null;
            if (!Auto(manager) || writer == null || !writer.IsWaitingForInput) { yield return null; continue; }
            string text = say!.StoryText ?? "";
            bool Same() => Auto(manager) && !Skip(manager) && writer != null && writer.IsWaitingForInput && say != null && say.StoryText == text;
            float until = Time.time + ReadingTime(text);
            while (Time.time < until && Same()) yield return null;
            if (!Same()) continue;
            Next!.Invoke(manager, null);
            // Until the page has turned (a press the dialog did not take is made again after a while, as the game would).
            float release = Time.time + 2f;
            while (Same() && Time.time < release) yield return null;
        }
    }
}

// 设置/自由行动过场: the second after a free-play action, the wipe to the story and the fade back to the map, at three times the speed.
[HarmonyPatch]
internal static class FreeTransitionPatch
{
    private static IEnumerable<MethodBase> TargetMethods() => new[]
    {
        AccessTools.Method(typeof(PositionController), "ExecuteMainStoryAction"), AccessTools.Method(typeof(PositionController), "ExecuteSubStoryAction"),
        AccessTools.Method(typeof(PositionController), "ExecuteActivityAction"), AccessTools.Method(typeof(FreeManager), "Start")
    };
    private static void Postfix(ref IEnumerator __result) { if (Speedups.FastFree && __result != null) __result = Speedups.Hasten(__result, Speedups.FreeFactor); }
}

// While skipping, each line still cost several frames in Fungus's plumbing: the task starts a frame late, waiting on a
// coroutine that has already ended costs a frame, and the script looks again once a frame; loading a portrait that is
// already loaded costs as much. While skipping, a command that can finish at once now does, and the script goes straight
// on; one that really waits is handed over as before. Normal play is unchanged.
[HarmonyPatch(typeof(LuaEnvironment), nameof(LuaEnvironment.RunUnityCoroutine))]
internal static class SkipCommandPatch
{
    private static readonly AccessTools.FieldRef<StoryManager, bool> Skip = AccessTools.FieldRefAccess<StoryManager, bool>("_skipDialog");

    private static bool Prefix(IEnumerator coroutine, ref Fungus.Task __result)
    {
        if (coroutine == null || StoryManager.Instance == null || !Skip(StoryManager.Instance)) return true;
        var flat = CombatCheats.Flatten(coroutine);
        bool more;
        try { more = flat.MoveNext(); }
        catch (Exception ex) { Speedups.Log?.LogWarning("SKIP_COMMAND " + ex.GetBaseException().Message); more = false; }
        // A null task is what the script gets for nothing to run: it goes on at once.
        __result = more ? new Fungus.Task(Rest(flat)) : null!;
        return false;
    }

    private static IEnumerator Rest(IEnumerator flat)
    {
        while (true)
        {
            yield return flat.Current;
            bool more;
            try { more = flat.MoveNext(); }
            catch (Exception ex) { Speedups.Log?.LogWarning("SKIP_COMMAND " + ex.GetBaseException().Message); yield break; }
            if (!more) yield break;
        }
    }
}
