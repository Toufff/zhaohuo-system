using System;
using System.Globalization;
using BepInEx.Logging;
using Mortal.Core;
using UnityEngine.SceneManagement;

namespace ZhaoHuoSystem;

/// <summary>
/// On the title (F96) the story tree and the route map read the most recent save, the load page's 最近存档: once the panel
/// is open there, that save is put into the game's player, mission, item and shop data the way the game's own load does,
/// without its scene. The shop too: a 直接跳转 there saves through the game's CreateSaveData, which writes the shop, and the
/// shop has no lists before a load or a new game (F100). Starting a new game or loading a save resets that data first, as before; the prologue's restore data is left alone.
/// </summary>
internal static class TitleSave
{
    private static int scene = -1;
    private static string number = "";
    private static GameTime time;
    // Formatted when shown, so they follow the panel's language (F119).
    internal static string Source => number.Length > 0 ? L.Format("按最近存档（第 {0} 格 · {1}）", number, Conditions.TimeName(time)) : "";
    /// <summary>The same save as the base a 直接跳转 starts from: 最近存档（第 N 格 · date）.</summary>
    internal static string Base => number.Length > 0 ? L.Format("最近存档（第 {0} 格 · {1}）", number, Conditions.TimeName(time)) : "";

    /// <summary>Reads it once per visit to the title; true when it has just been read (views built before should be built again).</summary>
    internal static bool Ensure(ManualLogSource log)
    {
        var active = SceneManager.GetActiveScene();
        if (active.name != "Title") { number = ""; return false; }
        if (scene == active.handle) return false;
        scene = active.handle; number = "";
        try
        {
            var system = SaveSystem.Instance;
            string slot = system != null ? system.CurrentSlot : "";
            if (system == null || string.IsNullOrEmpty(slot) || system.GetSaveData(slot) is not GameSave save) { log.LogInfo("TITLE_SAVE none"); return false; }
            PlayerStatManagerData.Instance?.Load(save);
            MissionManagerData.Instance?.Load(save);
            ItemDatabase.Instance?.Load(save);
            ShopDatabase.Instance?.Load(save);
            time = new GameTime(save.CurrentYear, save.CurrentMonth, save.CurrentStage);
            number = int.TryParse(slot, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n.ToString(CultureInfo.InvariantCulture) : slot;
            log.LogInfo($"TITLE_SAVE slot={slot} time={save.CurrentYear}/{save.CurrentMonth}/{save.CurrentStage}");
            return true;
        }
        catch (Exception ex) { log.LogWarning("TITLE_SAVE " + ex); return false; }
    }
}
