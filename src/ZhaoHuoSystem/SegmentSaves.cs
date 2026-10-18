using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using HarmonyLib;
using Mortal.Core;
using Mortal.Free;
using Mortal.Story;
using UnityEngine;
using UnityEngine.UI;

namespace ZhaoHuoSystem;

/// <summary>
/// Saving anywhere in a story (F71). A save keeps only the script to start from and replays it from its first line, so
/// a save made mid-script would replay changes already made. Instead, as each story script starts, the save the game
/// would write right then is kept in memory (its start script set to this script); the story menu's 存档 stays on, and
/// a save made later in the same script writes that copy. Loading it starts this script again with exactly the values
/// it started with: only the part of this script already seen is played again. The game's own save points (where a
/// script turned saving on itself) keep the game's own save. Battles and the map are untouched.
/// To check this in normal play, each such save also notes what it wrote (in the mod's own folder, never in a save);
/// the first script after loading that save is compared with it and the result logged (SAVE_CHECK).
/// Off with the config entry 「工具/剧情中随时存档」.
/// </summary>
internal sealed class SegmentSaves(ManualLogSource log, string checkFile)
{
    public bool Enabled = true;
    private static readonly MethodInfo? Create = AccessTools.Method(typeof(SaveSystem), "CreateSaveData");
    private static readonly Regex Volatile = new("\"(TimeTick|Version)\":(\"[^\"]*\"|-?\\d+),?|\"FreeRandomState\":\\{[^}]*\\},?");
    private GameSave? snapshot;
    private string script = "";
    private bool nativeOpen, writing, justLoaded;

    /// <summary>Each story script as the game loads it, before any of it runs.</summary>
    public void ScriptLoaded(string? friendlyName)
    {
        try
        {
            string name = friendlyName ?? "";
            name = name.Substring(name.LastIndexOfAny(new[] { '/', '\\' }) + 1);
            if (!name.EndsWith(".LuaScript", StringComparison.OrdinalIgnoreCase)) return;
            name = name.Substring(0, name.Length - ".LuaScript".Length);
            if (name.Length == 0 || name.StartsWith("tmp", StringComparison.OrdinalIgnoreCase)) return;
            snapshot = null; script = "";
            var player = PlayerStatManagerData.Instance;
            if (!Enabled || StoryManager.Instance == null || SaveSystem.Instance == null || player == null || Create == null) return;
            // Only a script the story itself moved to; it is what a save starts from.
            if (!string.Equals(player.CurrentStoryScript, name, StringComparison.OrdinalIgnoreCase)) return;
            if (Create.Invoke(SaveSystem.Instance, null) is not GameSave save) return;
            save.StartStoryScript = player.CurrentStoryScript;
            snapshot = save; script = player.CurrentStoryScript;
            if (justLoaded) { justLoaded = false; Check(save); }
        }
        catch (Exception ex) { snapshot = null; log.LogWarning("SEGMENT_SAVE " + ex.GetBaseException().Message); }
    }

    /// <summary>The game's own save point turned saving on (1) or off (0) in this story.</summary>
    public void NativeToggle(bool active) => nativeOpen = active;

    /// <summary>Keeps the story menu's 存档 on while a copy of this script's start is at hand.</summary>
    public void Update()
    {
        if (!Enabled || snapshot == null || nativeOpen || StoryManager.Instance is not StoryManager story || story == null) return;
        if (R.Field<Button>(R.Field(story, "_menuPanelController"), "_saveButton") is Button save && save != null && !save.interactable) story.ToggleSaveButton(true);
    }

    /// <summary>Before SaveSystem.SaveGameData(slot): in a story, away from the game's own save points, write the copy.</summary>
    public void BeforeSave()
    {
        writing = Enabled && snapshot != null && !nativeOpen && StoryManager.Instance != null && FreeManager.Instance == null;
        if (writing) snapshot!.TimeTick = DateTime.Now.Ticks;
    }

    /// <summary>In place of SaveSystem.CreateSaveData while that save is written.</summary>
    public GameSave? Substitute() => writing ? snapshot : null;

    public void AfterSave(string slot)
    {
        if (!writing) return;
        writing = false;
        try
        {
            File.WriteAllText(checkFile, slot + "\n" + script + "\n" + Normalized(snapshot!), new UTF8Encoding(false));
            log.LogInfo($"SEGMENT_SAVE slot={slot} start={script}");
        }
        catch (Exception ex) { log.LogWarning("SEGMENT_SAVE note " + ex.GetBaseException().Message); }
    }

    /// <summary>A save was loaded: the next story script is compared with what that save wrote, if it was one of these.</summary>
    public void Loaded() => justLoaded = true;
    public void SceneChanged() { nativeOpen = false; }

    private void Check(GameSave now)
    {
        try
        {
            if (!File.Exists(checkFile)) return;
            var lines = File.ReadAllText(checkFile, Encoding.UTF8).Split(new[] { '\n' }, 3);
            if (lines.Length < 3 || lines[0] != SaveSystem.Instance.CurrentSlot || !string.Equals(lines[1], script, StringComparison.OrdinalIgnoreCase)) return;
            string expected = lines[2], actual = Normalized(now);
            if (expected == actual) { log.LogInfo($"SAVE_CHECK ok slot={lines[0]} start={script} size={actual.Length}"); return; }
            int at = 0; while (at < expected.Length && at < actual.Length && expected[at] == actual[at]) at++;
            int from = Math.Max(0, at - 60);
            log.LogWarning($"SAVE_CHECK mismatch slot={lines[0]} start={script} at={at} saved=…{Cut(expected, from)}… loaded=…{Cut(actual, from)}…");
        }
        catch (Exception ex) { log.LogWarning("SAVE_CHECK " + ex.GetBaseException().Message); }
    }
    private static string Cut(string text, int from) => from >= text.Length ? "" : text.Substring(from, Math.Min(160, text.Length - from));
    /// <summary>The save as JSON without what differs on every write (time written, version, random state).</summary>
    private static string Normalized(GameSave save) => Volatile.Replace(JsonUtility.ToJson(save), "");
}
