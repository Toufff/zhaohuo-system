using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mortal.Core;

namespace ZhaoHuoSystem;

// Read-only interpretation. It deliberately never calls ConditionResultData.Execute or random getters.
internal static class Conditions
{
    private sealed class Variable
    {
        public int Value;
        public string Text = "";
        public string FieldId = "";
        public bool Known = true;
        public int Sign = 1;
        public string TimeText = "";
        // Constants and fixed dates show only themselves; nested conditions show only their expression.
        public bool Literal, Nested;
        public string Operand() => NativeUiState.ConditionOperand(Literal && TimeText.Length > 0 ? TimeText : Text,
            Nested ? null : !Known ? "" : TimeText.Length > 0 ? TimeText : (Value * Sign).ToString(CultureInfo.InvariantCulture), Literal);
    }
    public static ConditionSnapshot Read(object? condition, int depth = 0)
    {
        try
        {
            if (condition == null || depth > 12) return new() { Text = L.Text("条件未核验"), Detail = L.Text("未核验的条件"), Satisfied = false, Known = false };
            var children = R.Items(R.RequiredField<System.Collections.IEnumerable>(condition, "_items")).Select(item => item is StatCompareItem ? Compare(item, depth + 1) : Read(item, depth + 1)).ToList();
            var op = condition is ConditionResultData ? "OR" : R.RequiredField<LogicOperatorType>(condition, "_logicOp").ToString();
            if (op != "AND" && op != "OR") throw new InvalidOperationException("未核验的逻辑运算 " + op);
            return new() { Text = op, Operator = op, Children = children, Known = children.All(x => x.Known), Satisfied = children.Count > 0 && children.All(x => x.Known) && (op == "OR" ? children.Any(x => x.Satisfied) : children.All(x => x.Satisfied)), Detail = R.Name(condition) };
        }
        catch (Exception ex) { return new() { Text = L.Text("条件未核验"), Detail = ex.GetBaseException().Message, Satisfied = false, Known = false }; }
    }
    private static ConditionSnapshot Compare(object item, int depth)
    {
        var left = Group(R.Field(item, "_value1"), depth);
        var right = Group(R.Field(item, "_value2"), depth);
        var op = R.RequiredField<StatCompareType>(item, "_compareOp");
        if (!Enum.IsDefined(typeof(StatCompareType), op)) throw new InvalidOperationException("未核验的比较运算");
        var satisfied = left.Known && right.Known && GameStatUtils.StatCompare(left.Value, right.Value, op);
        var result = new ConditionSnapshot
        {
            Text = left.Operand() + Symbol(op) + right.Operand(),
            Detail = $"{left.FieldId} {left.Value} {Symbol(op)} {right.FieldId} {right.Value}" + ((!left.Known || !right.Known) ? L.Text("；含未核验或随机条件，不能指定") : ""),
            Satisfied = satisfied, Known = left.Known && right.Known
        };
        if (!satisfied && left.Known && right.Known && left.Sign == 1 && left.FieldId.Length > 0 && !left.FieldId.StartsWith("flag:") && right.FieldId.Length == 0)
        {
            // Suggested targets use the same displayed-final-value semantics as SetValue.
            // Do not subtract stat additions here; the adapter alone derives the base target.
            var target = ValueSemantics.Suggested(right.Value, Symbol(op));
            if (target.HasValue) { result.FieldId = left.FieldId; result.SuggestedValue = target; }
        }
        return result;
    }
    private static Variable Group(object? group, int depth)
    {
        if (group == null || depth > 12) return new() { Text = L.Text("未核验"), Known = false };
        var values = R.Items(R.Field(group, "_value")).Select(x => Reference(x, depth + 1)).ToList();
        if (values.Count == 1) return values[0];
        if (values.Count == 0) return new() { Text = "0", Value = 0, Known = false };
        var op = R.RequiredField<StatGroupType>(group, "_groupType").ToString();
        if (op != "Sum" && op != "Max" && op != "Min" && op != "Multiply") throw new InvalidOperationException("未核验的数值组运算 " + op);
        var total = op switch { "Max" => values.Max(x => x.Value), "Min" => values.Min(x => x.Value), "Multiply" => values.Aggregate(1, (a, b) => unchecked(a * b.Value)), _ => values.Sum(x => x.Value) };
        return new() { Text = "(" + string.Join(op == "Sum" ? " + " : op == "Multiply" ? " × " : ", ", values.Select(x => x.Text)) + ")", Value = total, Known = values.All(x => x.Known) };
    }
    private static Variable Reference(object reference, int depth)
    {
        var type = R.Property(reference, "CheckType")?.ToString() ?? "";
        var sign = R.RequiredField<bool>(reference, "_negative") ? -1 : 1;
        Variable v;
        switch (type)
        {
            case "角色數值":
                var stat = R.Field<GameStat>(reference, "_stat");
                v = new() { Text = stat == null ? L.Text("未核验属性") : GameAdapter.StatName(stat), FieldId = stat == null ? "" : "stat:" + stat.StatType, Value = stat?.FinalValue ?? 0, Known = stat != null }; break;
            case "好感度":
                var relation = R.Field<RelationshipStat>(reference, "_relationship");
                v = new() { Text = relation == null ? L.Text("未核验关系") : L.RelationName(relation.Type), FieldId = relation == null ? "" : "relation:" + relation.Type, Value = relation?.Value ?? 0, Known = relation != null }; break;
            case "旗標":
                var flag = R.Field<FlagData>(reference, "_flag");
                v = new() { Text = flag == null ? L.Text("未核验标记") : L.FlagName(flag), FieldId = flag == null ? "" : "flag:" + flag.name, Value = flag?.State ?? 0, Known = flag != null }; break;
            case "常數":
                var constant = R.RequiredField<int>(reference, "_constant");
                v = new() { Value = constant, Text = constant.ToString(), Literal = true }; break;
            case "數值群組": v = Group(R.Field(reference, "_statGroup"), depth + 1); break;
            case "條件設定":
                var nested = Read(R.Field(reference, "_condition"), depth + 1);
                v = new() { Value = nested.Satisfied ? 1 : 0, Text = "(" + Expression(nested) + ")", Known = nested.Known, Nested = true }; break;
            case "遊戲時間":
                var timeData = R.Field<GameTimeData>(reference, "_gameTime"); var time = timeData?.GameTime;
                // Only the player's own clock is "current"; mission deadlines are fixed targets.
                bool clock = timeData != null && PlayerStatManagerData.Instance != null && ReferenceEquals(timeData, R.Field(PlayerStatManagerData.Instance, "_gameTimeData"));
                v = new() { Text = L.Text("时间"), TimeText = time is null ? L.Text("未核验时间") : TimeName(time), Value = time?.ConvertToRounds() ?? 0, Known = time is not null, Literal = time is not null && !clock }; break;
            default:
                var mapping = type switch { "書籍" => ("_book", "CurrentCount"), "貴重品" => ("_specialItem", "CurrentCount"), "雜物" => ("_misc", "CurrentCount"), "技能" => ("_talentData", "Level"), "秘笈等級" => ("_book", "CurrentMartialLevel"), "開發項目等級" => ("_upgradeItemData", "CurrentLevel"), "風雲史" => ("_libraryAchieve", "Count"), _ => ("", "") };
                var asset = R.Field(reference, mapping.Item1);
                var value = R.Property(asset, mapping.Item2);
                v = new() { Text = AssetName(asset, type), Value = value is int i ? i : 0, Known = value is int, FieldId = AssetFieldId(asset, type) };
                if (type == "已開發項目")
                {
                    var upgrade = R.Field<UpgradeItemData>(reference, "_upgradeItemData");
                    v = new() { Text = upgrade == null ? L.Text("开发项目") : L.GameText(upgrade.GetNameKey(), upgrade.Key), Value = upgrade?.AlreadyDevelop == true ? 1 : 0, Known = upgrade != null };
                }
                if (type == "書籍精通")
                {
                    var books = R.Field<BookDataCollection>(reference, "_bookCollection");
                    v = new() { Text = L.Text("精通秘籍数"), Value = books?.List.Count(x => x.IsMaxLevel) ?? 0, Known = books != null };
                }
                break;
        }
        if (type != "角色數值" && type != "好感度" && type != "旗標" && type != "常數") sign = 1;
        v.Value *= sign; v.Sign = sign;
        if (sign < 0) v.Text = "−" + v.Text;
        return v;
    }
    internal static string TimeName(GameTime time) => L.Format("{0}年{1}月{2}", time.Year, time.Month, L.GameText(time.StageText, time.Stage.ToString()));
    private static string AssetName(object? asset, string type) => asset switch
    {
        ItemData item => L.GameText(item.GetNameKey(), item.Id),
        PlayerTalentData talent => L.GameText(talent.GetIdKey(), talent.Id),
        UpgradeItemData upgrade => L.GameText(upgrade.GetNameKey(), upgrade.Key),
        LibraryItemData library => L.GameText(library.GetTitleKey(), R.Name(library)),
        _ => L.DisplayText(R.Name(asset).Length > 0 ? R.Name(asset) : type)
    };
    private static string AssetFieldId(object? asset, string type) => asset switch
    {
        Book book when type == "秘笈等級" => "book:" + book.Id + ":level",
        ItemData item => "item:" + item.ItemType + ":" + item.Id,
        PlayerTalentData talent => "talent:" + talent.Id,
        UpgradeItemData upgrade => "development:" + upgrade.Key + ":level",
        LibraryItemData library => "library:achievement:" + library.name,
        _ => ""
    };
    internal static string Expression(ConditionSnapshot condition) => condition.Children.Count == 0 ? condition.Text : string.Join(" " + condition.Operator + " ", condition.Children.Select(x => "(" + Expression(x) + ")"));
    private static string Symbol(StatCompareType op) => op switch { StatCompareType.Equal => "=", StatCompareType.NotEqual => "!=", StatCompareType.Less => "<", StatCompareType.LessEqual => "<=", StatCompareType.Greater => ">", _ => ">=" };
}
