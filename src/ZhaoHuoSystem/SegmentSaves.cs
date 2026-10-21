using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
/// A script often goes on with the music its parent put on, and a load stops all sound before replaying it: each such
/// save also notes the music and ambience heard as its script started, and the load puts them back (F78).
/// Off with the config entry 「工具/剧情中随时存档」 or with 设置/存档功能 (F105): saving then works as the game's own; the copy at
/// each script's start is still taken, since 剧情回顾 and 回想 jump from it.
/// </summary>
internal sealed class SegmentSaves(ManualLogSource log, string checkFile, string musicFile)
{
    public bool Enabled = true;
    private static readonly MethodInfo? Create = AccessTools.Method(typeof(SaveSystem), "CreateSaveData");
    private static readonly Regex Volatile = new("\"(TimeTick|Version)\":(\"[^\"]*\"|-?\\d+),?|\"FreeRandomState\":\\{[^}]*\\},?");
    private GameSave? snapshot;
    private string script = "";
    private bool nativeOpen, writing, justLoaded, roundCheck;
    // Heard as this script started; a fade to 0 is heard as none.
    private string music = "", env = "";
    private bool musicSilent, envSilent;
    public void MusicLevel(int value) => musicSilent = value <= 0;
    public void EnvLevel(int value) => envSilent = value <= 0;
    /// <summary>剧情回顾 (F84): each script's copy with the music then, as the script starts.</summary>
    public Action<string, GameSave, string, string>? Kept;
    // A kept node being written into a slot (F84), in place of this script's copy.
    private (GameSave Save, string Script, string Music, string Env)? exporting;

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
            if (StoryManager.Instance == null || SaveSystem.Instance == null || player == null || Create == null) return;
            // Only a script the story itself moved to; it is what a save starts from.
            if (!string.Equals(player.CurrentStoryScript, name, StringComparison.OrdinalIgnoreCase)) return;
            if (Create.Invoke(SaveSystem.Instance, null) is not GameSave save) return;
            save.StartStoryScript = player.CurrentStoryScript;
            snapshot = save; script = player.CurrentStoryScript;
            bool loaded = justLoaded; justLoaded = false;
            if (loaded) RestoreMusic();
            var sound = SoundManager.Instance;
            music = sound == null || musicSilent ? "" : R.Field<string>(sound, "_currentMusic", "") ?? "";
            env = sound == null || envSilent ? "" : R.Field<string>(sound, "_currentEnvSound", "") ?? "";
            if (loaded) Check(save);
            Kept?.Invoke(script, save, music, env);
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
        if (exporting != null) { writing = true; return; }
        writing = Enabled && snapshot != null && !nativeOpen && StoryManager.Instance != null && FreeManager.Instance == null;
        if (writing) snapshot!.TimeTick = DateTime.Now.Ticks;
    }

    /// <summary>In place of SaveSystem.CreateSaveData while that save is written.</summary>
    public GameSave? Substitute() => writing ? exporting?.Save ?? snapshot : null;

    /// <summary>剧情回顾 (F84): a kept node written into a slot through the game's own save, as one of these saves.</summary>
    public void Export(GameSave save, string start, string heard, string ambience, string slot)
    {
        exporting = (save, start, heard, ambience);
        try { SaveSystem.Instance.SaveGameData(slot); }
        finally { exporting = null; writing = false; }
    }

    public void AfterSave(string slot)
    {
        var (save, start, heard, ambience) = exporting ?? (snapshot!, script, music, env);
        NoteMusic(slot, writing, start, heard, ambience);
        if (!writing) return;
        writing = false;
        try
        {
            File.WriteAllText(checkFile, slot + "\n" + start + "\n" + Normalized(save), new UTF8Encoding(false));
            log.LogInfo($"SEGMENT_SAVE slot={slot} start={start} music={heard} env={ambience}");
        }
        catch (Exception ex) { log.LogWarning("SEGMENT_SAVE note " + ex.GetBaseException().Message); }
    }

    /// <summary>One line per slot written by these saves; any other save to that slot drops its line.</summary>
    private void NoteMusic(string slot, bool segment, string start, string music, string env)
    {
        try
        {
            bool note = segment && (music.Length > 0 || env.Length > 0);
            if (!note && !File.Exists(musicFile)) return;
            var lines = File.Exists(musicFile)
                ? File.ReadAllLines(musicFile, Encoding.UTF8).Where(line => !line.StartsWith(slot + "\t", StringComparison.Ordinal)).ToList()
                : new List<string> { "# 赵活系统：剧情中随时存档读档时补放的背景音乐。只有本模组读取，游戏存档不受影响。" };
            if (note) lines.Add(string.Join("\t", slot, start, music, env));
            File.WriteAllLines(musicFile, lines, new UTF8Encoding(false));
        }
        catch (Exception ex) { log.LogWarning("SEGMENT_SAVE music " + ex.GetBaseException().Message); }
    }

    /// <summary>Just loaded, before the script replays: the music its save noted, if the save is one of these.</summary>
    private void RestoreMusic()
    {
        try
        {
            if (!File.Exists(musicFile) || SoundManager.Instance is not SoundManager sound || sound == null) return;
            string slot = SaveSystem.Instance.CurrentSlot;
            var line = File.ReadAllLines(musicFile, Encoding.UTF8).Select(text => text.Split('\t'))
                .FirstOrDefault(parts => parts.Length >= 4 && parts[0] == slot && string.Equals(parts[1], script, StringComparison.OrdinalIgnoreCase));
            if (line == null) return;
            if (line[2].Length > 0) sound.PlayMusic(line[2]);
            if (line[3].Length > 0) sound.PlayEnvSound(line[3]);
            log.LogInfo($"SEGMENT_MUSIC slot={slot} start={script} music={line[2]} env={line[3]}");
        }
        catch (Exception ex) { log.LogWarning("SEGMENT_MUSIC " + ex.GetBaseException().Message); }
    }

    /// <summary>A save was loaded: the next story script is compared with what that save wrote, if it was one of these.</summary>
    public void Loaded() { justLoaded = true; roundCheck = true; }
    public void SceneChanged() { nativeOpen = false; roundCheck = false; }
    /// <summary>
    /// F108: a load runs the game's mission check (MissionManagerData.UpdateCheckMissions) before entering the save's
    /// scene, and that check starts the next 旬 whenever no action is left. A story entered from the map with the last
    /// action is saved here with none left, so loading it moved the time on and set the main mission's start (empty)
    /// as the script, and the story scene then waited on 「读取中」 for good. Right after a load into a story with no
    /// action left the check is skipped once: the story plays from its start, and when it goes back to the map the game
    /// runs the check itself, moving to the next 旬 then as it would have.
    /// </summary>
    public bool KeepRound()
    {
        if (!roundCheck) return false;
        roundCheck = false;
        var player = PlayerStatManagerData.Instance;
        if (player == null || player.CurrentScene != "Story" || player.CurrentActionCount != 0 || string.IsNullOrEmpty(player.CurrentStoryScript)) return false;
        log.LogInfo("LOAD_KEEP_ROUND start=" + player.CurrentStoryScript);
        return true;
    }

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
