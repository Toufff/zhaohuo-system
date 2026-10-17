using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Mortal.Core;

namespace ZhaoHuoSystem;

/// <summary>
/// 收集 tab: the game's 讲经堂 lists (风云史 achievements, 生死簿 deaths, 汗青书 endings) and unlocking them the way
/// the game does. A 风云史 unlock also unlocks its Steam achievement, like the game's own unlock (otherwise the game
/// would never grant it later, since it skips items already counted). Nothing is relocked. The global save is
/// written once per click, after one copy of it per session.
/// </summary>
internal static class LibraryCollections
{
    public static readonly string[] Sections = { "achievement", "dead", "end" };
    public static ManualLogSource? Log;
    private static bool backedUp;

    public static string SectionName(string id) => id switch
    {
        "achievement" => L.GameText("System/Menu/LibraryAchievement", "风云史"),
        "dead" => L.GameText("System/Menu/LibraryDead", "生死簿"),
        _ => L.GameText("System/Menu/LibraryEnd", "汗青书")
    };

    public static List<LibraryItemData> Items(string id)
    {
        var library = LibrarySystem.Instance;
        var collection = library == null ? null : id switch { "achievement" => library.Achievement, "dead" => library.Dead, _ => library.EndGame };
        return collection == null ? new List<LibraryItemData>() : collection.List.Where(item => item != null).ToList();
    }

    public static string Title(LibraryItemData item) => L.GameText(item.GetTitleKey(), item.name);
    public static string Description(LibraryItemData item) => L.GameText(item.GetDescKey(), "");
    /// <summary>What an achievement gives a new save, on one line.</summary>
    public static string Award(LibraryItemData item) => string.Join(L.Text("；"), L.GameText(item.GetAwardKey(), "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));

    public static OperationResult Unlock(string id, IEnumerable<LibraryItemData> items)
    {
        var targets = items.Where(item => item != null && item.Count == 0).ToList();
        if (targets.Count == 0) return OperationResult.Fail(L.Text("没有需要解锁的项目"));
        if (SaveSystem.Instance == null) return OperationResult.Fail(L.Text("讲经堂数据未载入"));
        Backup();
        foreach (var item in targets)
        {
            item.Add();
            if (id == "achievement") SaveSystem.Instance.SetAchivement(item.name);
        }
        SaveSystem.Instance.SaveUniverseData();
        // The 天命点 total (FateAwardPanel) is counted only when its panel starts; the game's own unlocks happen in
        // stories, after which the scene and the panel are rebuilt. Here the panel is already up, so it counts again (F69).
        int refreshed = 0;
        foreach (var panel in UnityEngine.Resources.FindObjectsOfTypeAll<FateAwardPanel>())
            if (panel != null && panel.gameObject.scene.IsValid()) { panel.UpdateData(); refreshed++; }
        Log?.LogInfo($"LIBRARY_UNLOCK section={id} count={targets.Count} fatePanels={refreshed} items={string.Join(",", targets.Select(x => x.name))}");
        return OperationResult.Ok(L.Format("已解锁 {0} 项", targets.Count));
    }

    // One copy of the global save (Save_universe.dat and its .json) before this session's first unlock.
    private static void Backup()
    {
        if (backedUp) return;
        try
        {
            var file = AccessTools.Method(typeof(SaveSystem), "GetSaveDataFile")?.Invoke(SaveSystem.Instance, new object[] { "universe" }) as string;
            if (string.IsNullOrEmpty(file)) { Log?.LogWarning("LIBRARY_BACKUP no save path"); return; }
            var target = Path.Combine(Paths.BepInExRootPath, "ZhaoHuoSystem", "backups", "universe-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(target);
            foreach (var path in new[] { file!, Path.ChangeExtension(file!, ".json") })
                if (File.Exists(path)) File.Copy(path, Path.Combine(target, Path.GetFileName(path)), true);
            backedUp = true;
            Log?.LogInfo("LIBRARY_BACKUP " + target);
        }
        catch (Exception ex) { Log?.LogWarning("LIBRARY_BACKUP " + ex.GetBaseException().Message); }
    }
}
