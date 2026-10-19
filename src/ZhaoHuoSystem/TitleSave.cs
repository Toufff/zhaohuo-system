using System;
using System.Globalization;
using BepInEx.Logging;
using Mortal.Core;
using UnityEngine.SceneManagement;

namespace ZhaoHuoSystem;

/// <summary>
/// On the title (F96) the story tree and the route map read the most recent save, the load page's 最近存档: once the panel
/// is open there, that save is put into the game's player, mission and item data the way the game's own load does, without
/// its scene. Starting a new game or loading a save resets that data first, as before; the prologue's restore data is left alone.
/// </summary>
internal static class TitleSave
{
    private static int scene = -1;
    internal static string Source { get; private set; } = "";
    /// <summary>The same save as the base a 直接跳转 starts from: 最近存档（第 N 格 · date）.</summary>
    internal static string Base { get; private set; } = "";

    /// <summary>Reads it once per visit to the title; true when it has just been read (views built before should be built again).</summary>
    internal static bool Ensure(ManualLogSource log)
    {
        var active = SceneManager.GetActiveScene();
        if (active.name != "Title") { Source = Base = ""; return false; }
        if (scene == active.handle) return false;
        scene = active.handle; Source = Base = "";
        try
        {
            var system = SaveSystem.Instance;
            string slot = system != null ? system.CurrentSlot : "";
            if (system == null || string.IsNullOrEmpty(slot) || system.GetSaveData(slot) is not GameSave save) { log.LogInfo("TITLE_SAVE none"); return false; }
            PlayerStatManagerData.Instance?.Load(save);
            MissionManagerData.Instance?.Load(save);
            ItemDatabase.Instance?.Load(save);
            string number = int.TryParse(slot, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n.ToString(CultureInfo.InvariantCulture) : slot;
            string date = Conditions.TimeName(new GameTime(save.CurrentYear, save.CurrentMonth, save.CurrentStage));
            Source = L.Format("按最近存档（第 {0} 格 · {1}）", number, date);
            Base = L.Format("最近存档（第 {0} 格 · {1}）", number, date);
            log.LogInfo($"TITLE_SAVE slot={slot} time={save.CurrentYear}/{save.CurrentMonth}/{save.CurrentStage}");
            return true;
        }
        catch (Exception ex) { log.LogWarning("TITLE_SAVE " + ex); return false; }
    }
}
