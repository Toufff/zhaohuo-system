using System;
using System.Collections.Generic;
using System.Linq;
using Mortal.Core;
using Mortal.Free;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ZhaoHuoSystem;

// Read-only current-scene view. Never invokes mission Check/Execute, GetActiveMissions,
// UpdateCheckMissions, GetDialog or GetActiveConditionResult (all may write/randomize).
internal static class MapOverview
{
    public static List<MapLocationSnapshot> Read(PlayerStatManagerData player, MissionManagerData? missions)
    {
        var result = new List<MapLocationSnapshot>();
        var scene = SceneManager.GetActiveScene();
        foreach (var controller in Resources.FindObjectsOfTypeAll<PositionController>())
        {
            if (controller == null || controller.gameObject.scene != scene || !controller.gameObject.activeInHierarchy || !controller.enabled) continue;
            try
            {
            var position = R.RequiredField<PositionType>(controller, "_position");
            var colliders = R.Items(R.Field(controller, "_colliders")).OfType<Component>().ToList();
            bool colliderVisible = colliders.Any(x => x.gameObject.activeInHierarchy && (x is not Behaviour b || b.enabled));
            var camera = Camera.main;
            var canvas = controller.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.worldCamera != null) camera = canvas.worldCamera;
            var point = camera != null ? camera.WorldToScreenPoint(controller.transform.position) : Vector3.zero;
            bool projected = camera != null && point.z > 0 && point.x >= 0 && point.x <= Screen.width && point.y >= 0 && point.y <= Screen.height;
            if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            { point = controller.transform.position; projected = point.x >= 0 && point.x <= Screen.width && point.y >= 0 && point.y <= Screen.height; }
            // A loaded/active object alone is not evidence of current map visibility.
            bool visible = colliderVisible && projected;
            if (!visible) continue;
            bool active = R.RequiredField<bool>(controller, "_active");
            bool processing = FreeManager.Instance != null && FreeManager.Instance.IsProcessing;
            int cost = R.RequiredField<int>(controller, "_costAction");
            bool mainLock = missions != null && missions.MainMissionStart && missions.MainMissionPosition != PositionType.無;
            bool allowedByMain = !mainLock || missions!.MainMissionPosition == position;
            bool enoughAction = player.CurrentActionCount >= cost;
            var location = new MapLocationSnapshot
            {
                Id = scene.name + ":" + (int)position + ":" + controller.GetInstanceID(),
                Name = PositionName(position), Visible = true, HasScreenPosition = projected, ScreenX = point.x, ScreenY = point.y,
                // The gate leads to places outside the sect that have no map location of their own (F32).
                EntryScript = R.Field<string>(controller, "_scriptName", ""), SurfaceReachable = position == PositionType.大門,
                Detail = L.Text("当前场景地点") + "\n" + scene.name + " / " + position + "\n" + L.Format("行动消耗 {0}", cost) +
                    (active ? "" : "\n" + L.Text("地点未启用")) + (!allowedByMain ? "\n" + L.Text("主线限制") : "") + (processing ? "\n" + L.Text("原生流程处理中") : "")
            };
            if (missions != null)
            {
                var seen = new HashSet<MissionCheckData>();
                if (missions.MainCheckData != null && missions.MainCheckData.Position == position && !string.IsNullOrWhiteSpace(missions.MainCheckData.StartScript))
                {
                    var check = missions.MainCheckData; seen.Add(check);
                    location.Entries.Add(Entry("main:" + check.Key, check, L.Text("原生已判定主线"), player, active && !processing && allowedByMain && missions.MainMissionStart, cost, false));
                }
                int priority = 0;
                if (missions.ActiveSubMissions != null) foreach (var pair in missions.ActiveSubMissions)
                {
                    var check = pair.Value;
                    if (check == null || check.Position != position || string.IsNullOrWhiteSpace(check.StartScript)) continue;
                    priority++;
                    seen.Add(check);
                    var entry = Entry("active:" + pair.Key + ":" + check.Key, check, L.Text("原生已判定支线"), player, active && !processing && !mainLock && enoughAction && priority == 1, cost, true);
                    entry.Detail += "\n" + L.Format("原生任务顺序 {0}", priority);
                    if (mainLock) entry.Detail += "\n" + L.Text("主线限制");
                    if (priority > 1) entry.Detail += "\n" + L.Text("当前地点有更高优先任务");
                    location.Entries.Add(entry);
                }
                // Read only the mission's current state, never the future/clear sequence.
                foreach (var mission in CurrentMissions(missions))
                {
                    if (string.IsNullOrWhiteSpace(mission.CurrentKey) || mission.CurrentKey == "clear") continue;
                    var check = R.Items(R.Field(mission, "_missions")).OfType<MissionCheckData>().FirstOrDefault(x => x.Key == mission.CurrentKey);
                    if (check == null || seen.Contains(check) || check.Position != position || !MapSemantics.IsCurrentCandidate(mission.CurrentKey, check.Key, !string.IsNullOrWhiteSpace(check.StartScript), InDateWindow(check, player.GameTime))) continue;
                    seen.Add(check);
                    var entry = Entry("candidate:" + mission.Name + ":" + check.Key, check, L.Text("当前阶段候选"), player, false, cost, true);
                    entry.Detail += "\n" + L.Text("候选条件预览，未进入当前原生任务集合");
                    location.Entries.Add(entry);
                }
            }
            result.Add(location);
            }
            catch (Exception) { /* One unreadable controller cannot invalidate the active action/value snapshots. */ }
        }
        return result.OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<MissionData> CurrentMissions(MissionManagerData manager)
    {
        if (manager.Main != null) yield return manager.Main;
        var sub = R.Field<SubMissionsData>(manager, "_subMissions");
        if (sub != null) foreach (var mission in sub.List) if (mission != null) yield return mission;
    }

    private static MapEventSnapshot Entry(string id, MissionCheckData check, string source, PlayerStatManagerData player, bool nativeReady, int cost, bool needsAction)
    {
        var conditions = MissionConditions(check, player);
        if (needsAction) conditions.Add(new() { Text = L.Format("行动次数 >= {0}", cost), Satisfied = player.CurrentActionCount >= cost, Detail = L.Format("当前 {0}", player.CurrentActionCount) });
        var desc = R.Field<string>(check, "_desc", "").Trim();
        string shortDesc = desc.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (shortDesc.Length > 52) shortDesc = shortDesc.Substring(0, 52) + "…";
        return new()
        {
            Id = "map:" + id, Title = shortDesc.Length > 0 ? L.DisplayText(shortDesc) : source + " · " + check.Key,
            Detail = source + "\n" + L.Text("地图任务预览，只读") + "\n" + check.Key + " / " + check.StartScript + (desc.Length > 0 ? "\n" + L.DisplayText(desc) : ""),
            Known = conditions.All(x => x.Known), Ready = MapSemantics.Ready(nativeReady, conditions), Conditions = conditions
        };
    }

    internal static List<ConditionSnapshot> MissionConditions(MissionCheckData check, PlayerStatManagerData player)
    {
        var result = new List<ConditionSnapshot>();
        try
        {
            result.Add(new() { Text = L.Text("任务启用"), Satisfied = R.RequiredField<bool>(check, "_active") });
            var conditions = R.RequiredField<ConditionResultItem[]>(check, "_conditions");
            foreach (var condition in conditions) result.Add(Conditions.Read(condition));
            if (R.RequiredField<bool>(check, "_dayCheck"))
            {
                var type = R.Field(check, "_timeCheckType")?.ToString();
                var current = player.GameTime;
                if (type == "特定")
                {
                    var time = R.RequiredField<GameTime>(check, "_specificTime");
                    result.Add(new() { Text = L.Text("时间") + " = " + Conditions.TimeName(time), Satisfied = current == time });
                }
                else if (type == "區間")
                {
                    var min = R.RequiredField<GameTime>(check, "_minTime"); var max = R.RequiredField<GameTime>(check, "_maxTime");
                    result.Add(new() { Text = Conditions.TimeName(min) + " <= " + L.Text("时间") + " <= " + Conditions.TimeName(max), Satisfied = current >= min && current <= max });
                }
                else if (type == "週期")
                {
                    // This build's native Check ignores _periodStage; do not invent a date gate.
                    result.Add(new() { Text = L.Text("周期日期：原生未限制"), Satisfied = true });
                }
                else result.Add(new() { Text = L.Text("时间条件未核验"), Known = false });
            }
        }
        catch (Exception ex) { result.Add(new() { Text = L.Text("条件未核验"), Known = false, Detail = ex.GetBaseException().Message }); }
        return result;
    }

    private static bool InDateWindow(MissionCheckData check, GameTime current)
    {
        if (R.Field(check, "_dayCheck") is not true) return true;
        var type = R.Field(check, "_timeCheckType")?.ToString();
        if (type == "特定") return R.Field<GameTime>(check, "_specificTime") is GameTime time && MapSemantics.InDateWindow(current.ConvertToRounds(), 0, time.ConvertToRounds(), 0);
        if (type == "區間") return R.Field<GameTime>(check, "_minTime") is GameTime min && R.Field<GameTime>(check, "_maxTime") is GameTime max && MapSemantics.InDateWindow(current.ConvertToRounds(), 1, min.ConvertToRounds(), max.ConvertToRounds());
        return type == "週期" && MapSemantics.InDateWindow(current.ConvertToRounds(), 2, 0, 0);
    }
    internal static string PositionName(PositionType position) => L.DisplayText(position.ToString());
}
