using System;
using System.Collections.Generic;

namespace ZhaoHuoSystem;

public sealed class FlagInfo
{
    public string Id { get; }
    public string Name { get; }
    public string Kind { get; }
    public string Meaning { get; }
    public IReadOnlyDictionary<int, string> Options { get; }
    public bool Verified { get; }
    public string GroupId { get; }

    internal FlagInfo(string id, string name, string kind, string meaning, bool verified, string groupId = "flags")
    {
        Id = id; Name = name; Kind = kind; Meaning = meaning; Verified = verified; GroupId = groupId;
        Options = new Dictionary<int, string>();
    }
}

/// <summary>
/// Short semantic facts verified against local assets plus native/Lua references.
/// No game scripts or configuration dumps are embedded. Naming never implies a boolean type.
/// </summary>
public static class FlagDictionary
{
    private static readonly Dictionary<string, FlagInfo> Entries = Build();

    public static bool TryGet(string fieldId, out FlagInfo info) => Entries.TryGetValue(fieldId, out info!);

    public static string Resolve(string fieldId) => GetInfo(fieldId).Name;

    public static FlagInfo GetInfo(string fieldId)
    {
        if (TryGet(fieldId, out var info)) return info;
        return new FlagInfo(fieldId, fieldId, "integer", "含义尚未核验", false);
    }

    private static Dictionary<string, FlagInfo> Build()
    {
        var entries = new Dictionary<string, FlagInfo>(StringComparer.Ordinal);
        // These are field labels, not a copy of game configuration or story content.
        string[] verifiedNames =
        {
            "體力", "內力", "輕功", "銀兩", "魅力", "學問", "心理衛生", "命運", "性情", "處世", "修養", "道德", "嘴力",
            "門派規模", "門派名聲", "門派人數", "向心力", "鍛造", "毒藥", "行動次數", "個人貢獻度", "廚藝", "抗毒", "抗麻",
            "稱號", "愛人", "額外行動次數_1", "娘化", "陰陽內功", "防禦", "變心", "武學點數", "全武學點數", "門派資產", "門派貢獻",
            "全鍛造點數", "全毒藥點數", "全貢獻度", "額外行動次數_2", "額外行動次數_3", "武功刀劍", "武功暗器", "武功拳掌", "武功腿法",
            "武功奇門", "武功軟兵器", "武功槍棍", "武功內功", "儒學", "道學", "佛學", "戰役預設血量", "攻擊爆擊", "暗器爆擊"
        };
        foreach (string name in verifiedNames)
        {
            string id = "stat:" + name;
            entries.Add(id, new FlagInfo(id, name, "integer", "游戏属性字段；取值与可写范围由运行时适配器提供。", true));
        }
        // Native FlagData.State and MissionManagerData.SetFlag/AddFlag are signed int with no clamp.
        // These are faction disposition scores, not character relationships or faction size/fame.
        var factions = new Dictionary<string, string>
        {
            ["H00001"] = "青城派", ["H00002"] = "全真教", ["H00003"] = "峨嵋派",
            ["H00004"] = "崆峒派", ["H00005"] = "嵩山派", ["H00006"] = "点苍派",
            ["H01001"] = "南宫世家", ["H01002"] = "锦香宫", ["H01003"] = "上官世家",
            ["H02001"] = "丐帮", ["H02002"] = "飞石帮", ["H02003"] = "泥教",
            ["M21000"] = "飞天门", ["M21001"] = "玄功门", ["M21002"] = "夺魄门", ["M21003"] = "铁拳门"
        };
        foreach (var pair in factions)
        {
            string id = "flag:" + pair.Key;
            entries.Add(id, new FlagInfo(id, pair.Value, "integer", "门派关系分值；有正负分，不使用角色好感的 0–100 范围。原生接口未设置上下界。", true, "factions"));
        }
        entries.Add("flag:T0002", new FlagInfo("flag:T0002", "下一旬行动扣减", "integer", "下一旬重置行动次数时扣除该值，然后由原生流程归零。", true));
        return entries;
    }
}
