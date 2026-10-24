using System;
using System.Collections.Generic;
using System.Linq;
using Mortal.Core;

namespace ZhaoHuoSystem;

internal sealed class ValueBinding
{
    public object Source { get; }
    public Func<int> Read { get; }
    public Action<int>? Write { get; }
    public ValueSnapshot View { get; }
    public ValueBinding(object source, ValueSnapshot view, Func<int> read, Action<int>? write = null)
    { Source = source; View = view; Read = read; Write = write; }
}

// Only enumerates collections owned by the active player's native managers.
// No Resources scan, progression evaluation, consumable use or reward execution occurs here.
internal static class ValueCatalog
{
    public static IEnumerable<ValueBinding> Read(PlayerStatManagerData player)
    {
        if (player.Talents != null) foreach (var talent in player.Talents.List)
        {
            if (talent == null) continue;
            int max = R.Field<int>(talent, "_maxLevel", -1);
            var view = View("talent:" + talent.Id, L.GameText(talent.GetIdKey(), talent.Id), "talents", talent.Level, 0, max, max >= 0);
            view.Detail += "\n" + talent.GetIdKey();
            yield return new(talent, view, () => talent.Level, value => player.Talents.Set(talent.Id, value));
        }

        var items = ItemDatabase.Instance;
        if (items != null)
        {
            foreach (var collection in new[] { items.Books, items.Miscs, items.Special, items.Consumables })
            {
                if (collection == null) continue;
                foreach (var item in collection.List)
                {
                    if (item == null) continue;
                    var name = L.GameText(item.GetNameKey(), item.Id);
                    bool persistent = item.ItemType != GameItemType.消耗品;
                    var count = View("item:" + item.ItemType + ":" + item.Id, name + " · " + L.Text("数量"), item is Book ? "books" : "items", item.CurrentCount, 0, int.MaxValue, persistent);
                    count.Reason = persistent ? "" : L.Text("原生存档未记录此类数量，暂只读");
                    count.Detail += "\n" + item.GetNameKey();
                    yield return new(item, count, () => item.CurrentCount, value => items.SetItem(item.ItemType, item.Id, value));
                    if (item is not Book book || !book.IsMartial) continue;
                    var levels = book.LevelData;
                    int maxLevel = levels?.Length ?? 0;
                    var level = View("book:" + book.Id + ":level", name + " · " + L.Text("等级"), "books", book.CurrentMartialLevel, 0, maxLevel, false);
                    level.Reason = L.Text("升级会改变永久属性，等级暂只读");
                    yield return new(book, level, () => book.CurrentMartialLevel);
                    bool valid = book.CurrentCount > 0 && book.CurrentMartialLevel >= 0 && book.CurrentMartialLevel < maxLevel && levels![book.CurrentMartialLevel] != null;
                    int threshold = valid ? levels![book.CurrentMartialLevel].Points : 0;
                    var training = View("book:" + book.Id + ":points", name + " · " + L.Text("修炼点数"), "books", book.CurrentLevelPoints, 0, Math.Max(0, threshold - 1), valid && threshold > 0);
                    training.Reason = training.Writable ? L.Text("仅调整本级点数；升级仍由原生修炼结算") : L.Text("未持有、已满级或当前等级配置不可用");
                    // Preserve level: SetMartial is the game's own load/setup API, not Learn/DirectLevelUp.
                    yield return new(book, training, () => book.CurrentLevelPoints, value => book.SetMartial(book.CurrentMartialLevel, value));
                }
            }
        }

        foreach (var collectionName in new[] { "_weaponUpgradeItems", "_poisonUpgradeItems" })
        {
            var collection = R.Field<UpgradeItemCollectionData>(player, collectionName);
            if (collection == null) continue;
            foreach (var item in collection.List)
            {
                if (item == null) continue;
                var name = L.GameText(item.GetNameKey(), item.Key);
                var levels = item.UpgradeLevels;
                int maxLevel = levels?.Length ?? 0;
                // No level of any weapon or poison changes a flag or grants a talent (all 40 checked, F113): the level is
                // written as it is, -1 being 未开发. A few conditions read it (e.g. 切鱼刀 >= 0 opens a sect scene). An item
                // that resets at its last level (made, then started again) stops one short of it.
                int topLevel = R.Field(item, "_resetWhenMaxLevel") is true ? Math.Max(-1, maxLevel - 1) : maxLevel;
                var level = View("development:" + item.Key + ":level", name + " · " + L.Text("等级"), "development", item.CurrentLevel, -1, topLevel, maxLevel > 0);
                level.Detail += "\n" + L.Text("只改等级，不改剧情标记；-1 为未开发。读这项等级的剧情条件按新等级判定。战斗只用当前在用的那件，改等级不会换上它；当前在用的那件之后开发下一件时，游戏按等级退还九成银两和锻造点数。");
                level.Reason = maxLevel > 0 ? "" : L.Text("没有等级配置");
                yield return new(item, level, () => item.CurrentLevel, value =>
                {
                    if (value == item.CurrentLevel) return;
                    item.Setup(value, 0, item.AlreadyDevelop); item.UpdateStatus();
                });
                bool valid = item.CurrentLevel >= 0 && item.CurrentLevel < maxLevel && levels![item.CurrentLevel] != null;
                int threshold = valid ? levels![item.CurrentLevel].LevelPoints : 0;
                var points = View("development:" + item.Key + ":points", name + " · " + L.Text("开发点数"), "development", item.CurrentPoints, 0, Math.Max(0, threshold - 1), valid && threshold > 0);
                points.Reason = points.Writable ? L.Text("仅调整本级点数；升级仍由原生开发结算") : L.Text("未启用、已满级或当前等级配置不可用");
                yield return new(item, points, () => item.CurrentPoints, value => { item.Setup(item.CurrentLevel, value, item.AlreadyDevelop); item.UpdateStatus(); });
            }
        }
        var battleSkills = R.Field<BattleSkillDataCollection>(player, "_battleSkills");
        if (battleSkills != null) foreach (var item in battleSkills.List)
        {
            if (item == null) continue;
            var view = View("battle-skill:" + item.Key + ":level", L.GameText(item.GetNameKey(), item.Key), "talents", item.Level, 1, int.MaxValue, false);
            view.Reason = L.Text("技能效果与等级上限尚未核验，暂只读");
            yield return new(item, view, () => item.Level);
        }
        var library = LibrarySystem.Instance;
        if (library != null && library.Achievement != null) foreach (var item in library.Achievement.List)
        {
            if (item == null) continue;
            var view = View("library:achievement:" + item.name, L.GameText(item.GetTitleKey(), item.Title), "library", item.Count, 0, int.MaxValue, false);
            view.Reason = L.Text("全局见闻与奖励关联，次数暂只读");
            yield return new(item, view, () => item.Count);
        }
    }

    public static ValueSnapshot View(string id, string name, string groupId, int value, int min, int max, bool writable) => new()
    { Id = id, Name = name, GroupId = groupId, Group = L.GroupName(groupId), Value = value, Min = min, Max = max, Writable = writable, Detail = id, SearchText = id + " " + name };
}
