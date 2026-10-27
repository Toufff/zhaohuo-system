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
        /// <summary>The values a sum adds up (F77: 一键设置 puts the gap on one of them).</summary>
        public List<Variable>? Parts;
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
    /// <summary>
    /// 决定它的选项 (F101): the story flags a condition (or a switch, whose items are conditions) reads, with their values now,
    /// in the order first met. Nothing is evaluated.
    /// </summary>
    public static List<FlagData> Flags(object? condition)
    {
        var found = new List<FlagData>();
        void Condition(object? node, int depth)
        {
            if (node == null || depth > 12) return;
            foreach (var item in R.Items(R.Field(node, "_items")))
                if (item is StatCompareItem) { Group(R.Field(item, "_value1"), depth + 1); Group(R.Field(item, "_value2"), depth + 1); }
                else Condition(item, depth + 1);
        }
        void Group(object? group, int depth)
        {
            if (group == null || depth > 12) return;
            foreach (var reference in R.Items(R.Field(group, "_value")))
                switch (R.Property(reference, "CheckType")?.ToString())
                {
                    case "旗標": if (R.Field<FlagData>(reference, "_flag") is FlagData flag && flag != null && !found.Contains(flag)) found.Add(flag); break;
                    case "數值群組": Group(R.Field(reference, "_statGroup"), depth + 1); break;
                    case "條件設定": Condition(R.Field(reference, "_condition"), depth + 1); break;
                }
        }
        try { Condition(condition, 0); } catch { }
        return found;
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
        if (op != "Sum" && op != "Max" && op != "Min" && op != "Multiply" && op != "And" && op != "Or") throw new InvalidOperationException("未核验的数值组运算 " + op);
        // And / Or (1.0.6001): 1 when every / any value is not 0, else 0.
        var total = op switch
        {
            "Max" => values.Max(x => x.Value), "Min" => values.Min(x => x.Value), "Multiply" => values.Aggregate(1, (a, b) => unchecked(a * b.Value)),
            "And" => values.All(x => x.Value != 0) ? 1 : 0, "Or" => values.Any(x => x.Value != 0) ? 1 : 0, _ => values.Sum(x => x.Value)
        };
        string join = op switch { "Sum" => " + ", "Multiply" => " × ", "And" => " " + L.Text("且") + " ", "Or" => L.Text(" 或 "), _ => ", " };
        return new() { Text = "(" + string.Join(join, values.Select(x => x.Text)) + ")", Value = total, Known = values.All(x => x.Known), Parts = op == "Sum" ? values : null };
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
    /// <summary>
    /// Whether one run of an event with these flag changes leaves this condition unmet: an AND fails when any part
    /// fails, an OR when every part does. Only "flag op constant" parts can fail; anything else is taken to stay.
    /// </summary>
    internal static bool EndsAfterOnce(object? condition, FlagChanges changes, int depth = 0)
    {
        if (condition == null || depth > 12 || changes.Empty) return false;
        var parts = R.Items(R.Field(condition, "_items")).Select(item => item is StatCompareItem ? ClauseEnds(item, changes) : EndsAfterOnce(item, changes, depth + 1)).ToList();
        if (parts.Count == 0) return false;
        bool or = condition is ConditionResultData || R.Field(condition, "_logicOp") is LogicOperatorType.OR;
        return or ? parts.All(x => x) : parts.Any(x => x);
    }
    private static bool ClauseEnds(object item, FlagChanges changes)
    {
        string op = Symbol(R.RequiredField<StatCompareType>(item, "_compareOp"));
        var (leftFlag, leftConstant) = Single(R.Field(item, "_value1"));
        var (rightFlag, rightConstant) = Single(R.Field(item, "_value2"));
        if (leftFlag != null && rightConstant.HasValue) return LuaOptions.Ends(op, rightConstant.Value, changes, leftFlag);
        if (rightFlag != null && leftConstant.HasValue) return LuaOptions.Ends(LuaOptions.Mirror(op), leftConstant.Value, changes, rightFlag);
        return false;
    }
    // A value group that is exactly one plain flag or one constant.
    private static (string? Flag, int? Constant) Single(object? group)
    {
        var values = R.Items(R.Field(group, "_value")).ToList();
        if (values.Count != 1 || R.Field(values[0], "_negative") is true) return (null, null);
        return R.Property(values[0], "CheckType")?.ToString() switch
        {
            "旗標" => (R.Field<FlagData>(values[0], "_flag") is FlagData flag && flag != null ? flag.name : null, null),
            "常數" => (null, R.Field(values[0], "_constant") is int constant ? constant : null),
            _ => (null, null)
        };
    }
    /// <summary>
    /// The writes that make this condition hold (F74): each unmet "value op constant" part sets that value to the nearest
    /// one that passes; a sum of values takes the gap on one of them. An AND needs every part, an OR (and a condition
    /// list) the part with the fewest writes. Null when a part cannot be met by writing one value (time, random, nested
    /// conditions, max/min of values); empty when it holds.
    /// </summary>
    internal static List<KeyValuePair<string, int>>? Satisfy(object? condition, int depth = 0)
    {
        try
        {
            if (condition == null || depth > 12) return null;
            var parts = R.Items(R.RequiredField<System.Collections.IEnumerable>(condition, "_items")).Select(item => item is StatCompareItem ? SatisfyCompare(item, depth + 1) : Satisfy(item, depth + 1)).ToList();
            if (parts.Count == 0) return null;
            if (condition is ConditionResultData || R.Field(condition, "_logicOp") is LogicOperatorType.OR)
                return parts.Where(part => part != null).OrderBy(part => part!.Count).FirstOrDefault();
            if (parts.Exists(part => part == null)) return null;
            var writes = new List<KeyValuePair<string, int>>();
            foreach (var part in parts)
                foreach (var write in part!)
                {
                    int same = writes.FindIndex(x => x.Key == write.Key);
                    if (same < 0) writes.Add(write);
                    else if (writes[same].Value != write.Value) return null; // two parts want one value differently
                }
            return writes;
        }
        catch { return null; }
    }
    private static List<KeyValuePair<string, int>>? SatisfyCompare(object item, int depth)
    {
        var left = Group(R.Field(item, "_value1"), depth); var right = Group(R.Field(item, "_value2"), depth);
        var op = R.RequiredField<StatCompareType>(item, "_compareOp");
        if (!left.Known || !right.Known) return null;
        if (GameStatUtils.StatCompare(left.Value, right.Value, op)) return new();
        bool Constant(Variable v) => v.Literal && v.FieldId.Length == 0 && v.TimeText.Length == 0;
        if (Constant(right) && ValueSemantics.Suggested(right.Value, Symbol(op)) is int target) return Field(left) ? new() { new(left.FieldId, target) } : Share(left, target);
        if (Constant(left) && ValueSemantics.Suggested(left.Value, LuaOptions.Mirror(Symbol(op))) is int mirrored) return Field(right) ? new() { new(right.FieldId, mirrored) } : Share(right, mirrored);
        return null;
    }
    private static bool Field(Variable v) => v.FieldId.Length > 0 && v.Sign == 1 && !v.Nested && v.TimeText.Length == 0;
    /// <summary>
    /// A sum short of its target (F77, e.g. the 六道 counters 天道 + 饿鬼道 + … > 1): the whole gap goes on one value
    /// in it; going up, the first still at 0 (as play would add a new one), else the first; coming down, the largest,
    /// never below 0.
    /// </summary>
    private static List<KeyValuePair<string, int>>? Share(Variable sum, int target)
    {
        var fields = sum.Parts?.Where(Field).ToList();
        if (fields == null || fields.Count == 0) return null;
        long gap = (long)target - sum.Value;
        var part = gap > 0 ? fields.FirstOrDefault(x => x.Value == 0) ?? fields[0] : fields.OrderByDescending(x => x.Value).First();
        long value = part.Value + gap;
        if (value > int.MaxValue || gap < 0 && value < 0) return null;
        return new() { new(part.FieldId, (int)value) };
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
    /// <summary>
    /// 剧情路线图 (F89): a condition group worded by what it asks, without the current values, with what the map reasons
    /// about: dates compared with now, flags it needs set, a series' scheduled time, and its opposite when it is a single
    /// comparison with a number.
    /// </summary>
    internal static RouteGroup RouteGroupOf(object? item)
    {
        var group = new RouteGroup();
        if (item == null) return group;
        try
        {
            var parts = new List<string>();
            var items = R.Items(R.Field(item, "_items")).Where(x => x != null).ToList();
            foreach (var part in items)
            {
                if (part is not StatCompareItem) { parts.Add("(" + Expression(Read(part)) + ")"); continue; }
                object? v1 = R.Field(part, "_value1"), v2 = R.Field(part, "_value2");
                string n1 = R.Name(v1), n2 = R.Name(v2);
                var left = Group(v1, 0); var right = Group(v2, 0);
                // Bookkeeping by the value's name or by the flag's own words (the prototype read both).
                if (NoiseName.IsMatch(n1) || NoiseName.IsMatch(n2) || NoiseName.IsMatch(left.Text) || NoiseName.IsMatch(right.Text)) group.Noise = true;
                string op = Symbol(R.RequiredField<StatCompareType>(part, "_compareOp"));
                parts.Add(RouteText(left, op, right, n1));
                bool number = right.Literal && right.FieldId.Length == 0 && right.TimeText.Length == 0;
                bool clock = left.TimeText.Length > 0 && !left.Literal;
                if (left.FieldId.StartsWith("flag:", StringComparison.Ordinal) && number && right.Value >= 1 && op is "=" or ">=") group.Requires.Add((left.FieldId.Substring(5), left.Text));
                if (clock && right.Literal && right.TimeText.Length > 0) group.Times.Add((op, Date(right.Value)));
                if (clock && op == "=" && n2.StartsWith("時間_S", StringComparison.Ordinal)) group.Scheduled = true;
                if (items.Count == 1 && number && left.FieldId.Length > 0) group.Flip = RouteText(left, Flipped(op), right, n1);
            }
            group.Text = string.Join(R.Field(item, "_logicOp") is LogicOperatorType.OR ? L.Text(" 或 ") : L.Text("，"), parts);
        }
        catch { /* A condition that cannot be read stays without words. */ }
        return group;
    }
    private static readonly System.Text.RegularExpressions.Regex NoiseName = new("旗標暫存|旗标暂存|決鬥泛用|决斗泛用|通用決鬥成敗|通用决斗成败");
    private static int[] Date(int rounds) { int v = Math.Max(0, rounds - 1); return new[] { v / 36 + 1, v % 36 / 3 + 1, v % 3 + 1 }; }
    private static string Flipped(string op) => op switch { ">=" => "<", "<=" => ">", ">" => "<=", "<" => ">=", "=" => "!=", _ => "=" };
    private static string RouteText(Variable left, string op, Variable right, string leftName = "")
    {
        // A named sum of values (數值_丐幫談判) reads by its name, as the game's designers called it.
        if (left.FieldId.Length == 0 && left.Text.StartsWith("(", StringComparison.Ordinal) && leftName.StartsWith("數值_", StringComparison.Ordinal))
            left = new Variable { Text = L.DisplayText(leftName.Substring(3)), Value = left.Value, Known = left.Known, Literal = left.Literal };
        if (left.TimeText.Length > 0 && !left.Literal && right.TimeText.Length > 0)
            return L.Format(op switch { ">=" => "时间不早于{0}", "<=" => "时间不晚于{0}", "=" => "时间为{0}", ">" => "时间晚于{0}", "<" => "时间早于{0}", _ => "时间不是{0}" }, right.TimeText);
        bool flag = left.FieldId.StartsWith("flag:", StringComparison.Ordinal);
        string name = left.FieldId.StartsWith("relation:", StringComparison.Ordinal) ? L.Format("{0}好感", left.Text) : left.Text;
        if (right.Literal && right.FieldId.Length == 0 && right.TimeText.Length == 0)
        {
            if (flag && op == "=" && right.Value == 0) return L.Format("未{0}", name);
            if (flag && op == "=" && right.Value == 1) return name;
            return name + op + right.Value.ToString(CultureInfo.InvariantCulture);
        }
        return name + op + right.Text;
    }

    /// <summary>Flags a condition reads, through its groups, value groups and nested conditions (剧情路线图, F89).</summary>
    internal static IEnumerable<string> FlagsRead(object? condition, int depth = 0)
    {
        var flags = new HashSet<string>(StringComparer.Ordinal);
        void Value(object? group, int level)
        {
            if (group == null || level > 12) return;
            foreach (var reference in R.Items(R.Field(group, "_value")))
            {
                if (reference == null) continue;
                switch (R.Property(reference, "CheckType")?.ToString())
                {
                    case "旗標": if (R.Field<FlagData>(reference, "_flag") is FlagData f && f != null) flags.Add(f.name); break;
                    case "數值群組": Value(R.Field(reference, "_statGroup"), level + 1); break;
                    case "條件設定": Walk(R.Field(reference, "_condition"), level + 1); break;
                }
            }
        }
        void Walk(object? node, int level)
        {
            if (node == null || level > 12) return;
            foreach (var part in R.Items(R.Field(node, "_items")))
            {
                if (part is StatCompareItem) { Value(R.Field(part, "_value1"), level + 1); Value(R.Field(part, "_value2"), level + 1); }
                else Walk(part, level + 1);
            }
        }
        try { Walk(condition, depth); } catch { }
        return flags;
    }
    internal static string Symbol(StatCompareType op) => op switch { StatCompareType.Equal => "=", StatCompareType.NotEqual => "!=", StatCompareType.Less => "<", StatCompareType.LessEqual => "<=", StatCompareType.Greater => ">", _ => ">=" };
}
