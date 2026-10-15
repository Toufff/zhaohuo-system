using System;
using System.Collections.Generic;
using System.Globalization;

namespace ZhaoHuoSystem;

/// <summary>One literal consequence of a story call, e.g. stat mental +5.</summary>
public sealed class OptionEffect
{
    public string Kind = "";   // stat, set-stat, relation, set-relation, flag, set-flag, item-add, item-remove, talent
    public string Key = "";
    public string ItemType = ""; // book, misc, special for item effects
    public int Value;
    /// <summary>The game adjusts the amount by the current value ("center"/"mental" modes).</summary>
    public bool Approximate;
}

/// <summary>Effects that apply only on one arm of a Switch checkpoint. Arm 0 means none of the switch items matched.</summary>
public sealed class OptionCase
{
    public List<KeyValuePair<string, int>> When = new();
    public List<OptionEffect> Effects = new();
}

/// <summary>Read-only consequences of one menu option or dice result, derived from script text alone.</summary>
public sealed class OptionPreview
{
    public string Key = "";
    /// <summary>Condition checkpoint deciding whether the option is offered.</summary>
    public string Requirement = "";
    public List<OptionEffect> Effects = new();
    public List<OptionCase> Cases = new();
    public string Pool = "", Dice = "", NextScript = "";
    public bool Menu, Unknown;
    public bool Empty => Effects.Count == 0 && Cases.Count == 0 && Pool.Length == 0 && Dice.Length == 0 && NextScript.Length == 0 && !Menu && !Unknown;
}

/// <summary>
/// Static, bounded reading of generated story Lua: option tables, choose / dice result branches,
/// Switch arms and literal statmodifymanager calls. Nothing is executed and later scripts are not followed here.
/// </summary>
public static class LuaOptions
{
    /// <summary>Key "" holds effects outside any option branch (used when a script is someone's next script).</summary>
    public const string ScriptLevel = "";

    private enum T { Name, String, Number, Symbol, Marker }
    private readonly struct Token
    {
        public readonly T Type; public readonly string Text;
        public Token(T type, string text) { Type = type; Text = text; }
        public bool Is(string text) => (Type == T.Name || Type == T.Symbol) && Text == text;
    }
    private sealed class Entry { public string Key = "", Requirement = ""; }
    private sealed class Frame
    {
        public string Kind = "other"; // choice, switch, other
        public string Var = "";
        public int Arm;
        public string Option = "";     // choice frames: key of the current arm
        public bool AwaitDo;
    }

    public static Dictionary<string, OptionPreview> Analyze(string source)
    {
        var tokens = Tokenize(source ?? "");
        var result = new Dictionary<string, OptionPreview>(StringComparer.Ordinal) { [ScriptLevel] = new OptionPreview() };
        var tables = new Dictionary<string, Dictionary<int, Entry>>(StringComparer.Ordinal);
        var choices = new Dictionary<string, Dictionary<int, Entry>>(StringComparer.Ordinal);
        var switches = new Dictionary<string, string>(StringComparer.Ordinal);
        var conditions = new Dictionary<string, string>(StringComparer.Ordinal);
        var finished = new HashSet<string>(StringComparer.Ordinal);
        var stack = new List<Frame>();
        Dictionary<int, Entry>? lastRoll = null;

        OptionPreview? Current(out List<KeyValuePair<string, int>> when)
        {
            when = new List<KeyValuePair<string, int>>();
            for (int s = stack.Count - 1; s >= 0; s--)
            {
                var frame = stack[s];
                if (frame.Kind == "switch") when.Insert(0, new(switches[frame.Var], frame.Arm));
                if (frame.Kind != "choice") continue;
                if (frame.Option.Length == 0) return null;
                return Option(frame.Option);
            }
            return result[ScriptLevel];
        }
        OptionPreview Option(string key)
        {
            if (!result.TryGetValue(key, out var preview)) result[key] = preview = new OptionPreview { Key = key };
            return preview;
        }
        void Add(OptionEffect effect)
        {
            var preview = Current(out var when);
            if (preview == null) return;
            if (when.Count == 0) { preview.Effects.Add(effect); return; }
            var group = preview.Cases.Find(c => SameWhen(c.When, when));
            if (group == null) preview.Cases.Add(group = new OptionCase { When = when });
            group.Effects.Add(effect);
        }
        void Mark(Action<OptionPreview> action) { var preview = Current(out _); if (preview != null) action(preview); }
        // Reads "V == N" between if/elseif and then; anything else is an ordinary block.
        (string Var, int Arm, int Next) ReadTest(int start)
        {
            int then = start; while (then < tokens.Count && !tokens[then].Is("then")) then++;
            if (then - start == 3 && tokens[start].Type == T.Name && tokens[start + 1].Is("==") && tokens[start + 2].Type == T.Number
                && int.TryParse(tokens[start + 2].Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int arm))
                return (tokens[start].Text, arm, then + 1);
            return ("", 0, then + 1);
        }
        void EnterArm(Frame frame, int arm, int next)
        {
            frame.Arm = arm; frame.Option = "";
            if (frame.Kind != "choice") return;
            string key = choices[frame.Var].TryGetValue(arm, out var entry) ? entry.Key : "";
            if (key.Length == 0 && next < tokens.Count && tokens[next].Type == T.Marker) key = tokens[next].Text;
            if (key.Length == 0) return;
            // One key, one branch: a second branch with the same key cannot be told apart.
            if (finished.Contains(key)) Option(key).Unknown = true;
            frame.Option = key;
            var preview = Option(key);
            if (entry != null && entry.Requirement.Length > 0) preview.Requirement = entry.Requirement;
        }
        void LeaveArm(Frame frame) { if (frame.Kind == "choice" && frame.Option.Length > 0) finished.Add(frame.Option); }

        for (int i = 0; i < tokens.Count;)
        {
            var token = tokens[i];
            // Block structure.
            if (token.Is("if"))
            {
                var (var, arm, next) = ReadTest(i + 1);
                var frame = new Frame { Kind = choices.ContainsKey(var) ? "choice" : switches.ContainsKey(var) ? "switch" : "other", Var = var };
                stack.Add(frame); EnterArm(frame, arm, next); i = next; continue;
            }
            if (token.Is("elseif"))
            {
                var (var, arm, next) = ReadTest(i + 1);
                if (stack.Count > 0)
                {
                    var frame = stack[stack.Count - 1]; LeaveArm(frame);
                    if (frame.Kind != "other" && var != frame.Var) frame.Kind = "other";
                    EnterArm(frame, arm, next);
                }
                i = next; continue;
            }
            if (token.Is("else")) { if (stack.Count > 0) { var frame = stack[stack.Count - 1]; LeaveArm(frame); EnterArm(frame, 0, i + 1); } i++; continue; }
            if (token.Is("end")) { if (stack.Count > 0) { LeaveArm(stack[stack.Count - 1]); stack.RemoveAt(stack.Count - 1); } i++; continue; }
            if (token.Is("for") || token.Is("while")) { stack.Add(new Frame { AwaitDo = true }); i++; continue; }
            if (token.Is("do")) { if (stack.Count > 0 && stack[stack.Count - 1].AwaitDo) stack[stack.Count - 1].AwaitDo = false; else stack.Add(new Frame()); i++; continue; }
            if (token.Is("function") || token.Is("repeat")) { stack.Add(new Frame()); i++; continue; }
            if (token.Is("until")) { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); i++; continue; }

            // local T = {}
            int at = token.Is("local") ? i + 1 : i;
            if (at + 3 < tokens.Count && tokens[at].Type == T.Name && tokens[at + 1].Is("=") && tokens[at + 2].Is("{") && tokens[at + 3].Is("}"))
            { tables[tokens[at].Text] = new Dictionary<int, Entry>(); i = at + 4; continue; }
            // T[N] = "..." | V and "..." or "..."
            if (tokens[i].Type == T.Name && i + 5 < tokens.Count && tables.ContainsKey(tokens[i].Text) && tokens[i + 1].Is("[") && tokens[i + 2].Type == T.Number && tokens[i + 3].Is("]") && tokens[i + 4].Is("=")
                && int.TryParse(tokens[i + 2].Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
            {
                var entry = new Entry(); int j = i + 5;
                if (tokens[j].Type == T.Name && j + 2 < tokens.Count && tokens[j + 1].Is("and") && tokens[j + 2].Type == T.String)
                { entry.Requirement = conditions.TryGetValue(tokens[j].Text, out var name) ? name : ""; entry.Key = OptionKey(tokens[j + 2].Text); j += 3; }
                else if (tokens[j].Type == T.String) { entry.Key = OptionKey(tokens[j].Text); j++; }
                tables[tokens[i].Text][index] = entry; i = j; continue;
            }
            // V = choose(T) | checkpointmanager.Switch/Condition/Position("x") | dicemenudialog.ResultSelection
            if (tokens[at].Type == T.Name && at + 1 < tokens.Count && tokens[at + 1].Is("="))
            {
                string var = tokens[at].Text; int j = at + 2;
                if (Matches(tokens, j, "choose", "(") && j + 3 < tokens.Count && tokens[j + 2].Type == T.Name && tokens[j + 3].Is(")"))
                {
                    choices[var] = tables.TryGetValue(tokens[j + 2].Text, out var options) ? new Dictionary<int, Entry>(options) : new Dictionary<int, Entry>();
                    Mark(p => p.Menu = true); i = j + 4; continue;
                }
                if (Matches(tokens, j, "dicemenudialog", ".", "ResultSelection"))
                { choices[var] = lastRoll ?? new Dictionary<int, Entry>(); i = j + 3; continue; }
                if (Matches(tokens, j, "checkpointmanager", ".") && j + 5 < tokens.Count && tokens[j + 3].Is("(") && tokens[j + 4].Type == T.String && tokens[j + 5].Is(")"))
                {
                    string api = tokens[j + 2].Text, name = tokens[j + 4].Text;
                    if (api == "Switch") switches[var] = name;
                    else if (api == "Condition") conditions[var] = name;
                    else if (api == "Position") Mark(p => p.Pool = name);
                    i = j + 6; continue;
                }
            }
            if (Matches(tokens, i, "checkpointmanager", ".", "Position", "(") && i + 4 < tokens.Count && tokens[i + 4].Type == T.String)
            { string name = tokens[i + 4].Text; Mark(p => p.Pool = name); i += 5; continue; }
            if (Matches(tokens, i, "checkpointmanager", ".", "Dice", "(") && i + 4 < tokens.Count && tokens[i + 4].Type == T.String)
            { string name = tokens[i + 4].Text; Mark(p => p.Dice = name); i += 5; continue; }
            if (Matches(tokens, i, "dicemenudialog", ".", "ExecuteRoll", "(") && i + 4 < tokens.Count && tokens[i + 4].Type == T.Name)
            { lastRoll = tables.TryGetValue(tokens[i + 4].Text, out var rows) ? new Dictionary<int, Entry>(rows) : null; i += 5; continue; }
            if (Matches(tokens, i, "luamanager", ".", "SetNextScript", "("))
            {
                if (i + 5 < tokens.Count && tokens[i + 4].Type == T.String && tokens[i + 5].Is(")")) { string next = tokens[i + 4].Text; Mark(p => p.NextScript = next); }
                i += 4; continue;
            }
            if (Matches(tokens, i, "statmodifymanager", ".") && i + 3 < tokens.Count && tokens[i + 2].Type == T.Name && tokens[i + 3].Is("("))
            {
                var args = new List<Token>(); int j = i + 4, depth = 0;
                for (; j < tokens.Count; j++)
                {
                    if (tokens[j].Is("(")) depth++;
                    else if (tokens[j].Is(")")) { if (depth-- == 0) break; }
                    else if (depth == 0 && !tokens[j].Is(",")) args.Add(tokens[j]);
                }
                Effect(tokens[i + 2].Text, args, Add, () => Mark(p => p.Unknown = true));
                i = j + 1; continue;
            }
            i++;
        }
        return result;
    }

    private static void Effect(string api, List<Token> args, Action<OptionEffect> add, Action unknown)
    {
        string Text(int n) => n < args.Count && args[n].Type == T.String ? args[n].Text : "";
        bool Number(int n, out int value)
        {
            value = 0;
            if (n >= args.Count) return false;
            // Negative literals arrive as "-" followed by a number.
            if (args[n].Is("-") && n + 1 < args.Count && args[n + 1].Type == T.Number && int.TryParse(args[n + 1].Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) { value = -value; args.RemoveAt(n); return true; }
            return args[n].Type == T.Number && int.TryParse(args[n].Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
        string kind = api switch
        {
            "Player" => "stat", "SetPlayer" => "set-stat", "Character" => "relation", "SetCharacter" => "set-relation",
            "AddFlag" => "flag", "SetFlag" => "set-flag", "AddTalent" => "talent",
            "AddBook" or "AddMisc" or "AddSpecial" => "item-add", "RemoveBook" or "RemoveMisc" or "RemoveSpecial" => "item-remove",
            _ => ""
        };
        // Display-only or bookkeeping calls (AddStory, GetDisplayTime, missions, achievements) are not consequences shown here.
        if (kind.Length == 0) return;
        string key = Text(0);
        if (key.Length == 0) { unknown(); return; }
        var effect = new OptionEffect { Kind = kind, Key = key, ItemType = api.EndsWith("Book", StringComparison.Ordinal) ? "book" : api.EndsWith("Misc", StringComparison.Ordinal) ? "misc" : api.EndsWith("Special", StringComparison.Ordinal) ? "special" : "" };
        if (kind == "item-remove") effect.Value = 1;
        else if (!Number(1, out effect.Value)) { unknown(); return; }
        if (kind == "stat") { string mode = Text(2); effect.Approximate = mode == "center" || mode == "mental"; }
        add(effect);
    }

    /// <summary>
    /// Short bracket segments for an option, in reading order: requirement, certain effects, conditional arms, then
    /// random / follow-up markers. State: 1 met, -1 unmet, 0 neutral or unknown. Nothing here claims "no effect".
    /// A default arm reads "否则" only right after a shown arm of the same switch; otherwise it names the
    /// conditions it stands for (<paramref name="otherwise"/>), so an "else" never appears without its "if".
    /// </summary>
    public static List<HintSegment> Describe(OptionPreview preview, Func<OptionEffect, string> effectText,
        Func<string, int, KeyValuePair<string, int>> switchArm, Func<string, KeyValuePair<string, int>> requirement, Func<string, object[], string> format,
        Func<string, string>? otherwise = null)
    {
        var segments = new List<HintSegment>();
        HintSegment Plain(string text, int state = 0) => new() { Full = text, Summary = text, Head = text, State = state };
        if (preview.Requirement.Length > 0)
        {
            // An offered option already meets its requirement; only a failing one is worth words.
            var needed = requirement(preview.Requirement);
            if (needed.Value == -1) segments.Add(Plain(format("需 {0}", new object[] { needed.Key }), -1));
        }
        var certain = EffectParts.Of(preview.Effects, effectText);
        if (!certain.Empty) segments.Add(new HintSegment { Full = certain.Line(" ", format), Summary = certain.Overview(format), Head = certain.Line("、", format) });
        var previous = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in preview.Cases)
        {
            var arms = group.When.ConvertAll(when => switchArm(when.Key, when.Value));
            var body = EffectParts.Of(group.Effects, effectText);
            if (body.Empty) continue;
            int state = arms.TrueForAll(arm => arm.Value == 1) ? 1 : arms.Exists(arm => arm.Value == -1) ? -1 : 0;
            var named = arms.FindAll(arm => arm.Key.Length > 0).ConvertAll(arm => arm.Key);
            if (named.Count == 0 && otherwise != null && !group.When.TrueForAll(when => previous.Contains(when.Key)))
                named = group.When.ConvertAll(when => otherwise(when.Key)).FindAll(label => label.Length > 0);
            string full = body.Line(" ", format), overview = body.Overview(format), condition = string.Join(" · ", named);
            segments.Add(named.Count == 0
                ? new HintSegment { Full = format("否则：{0}", new object[] { full }), Summary = format("否则：{0}", new object[] { overview }),
                    Head = format("否则", Array.Empty<object>()), Body = "→ " + body.Line("、", format), State = state }
                : new HintSegment { Full = format("若 {0}：{1}", new object[] { condition, full }), Summary = format("若…：{0}", new object[] { overview }),
                    Head = format("若 {0}", new object[] { condition }), Body = "→ " + body.Line("、", format), State = state });
            previous = new HashSet<string>(group.When.ConvertAll(when => when.Key), StringComparer.Ordinal);
        }
        if (preview.Pool.Length > 0) segments.Add(Plain(format("随机事件", Array.Empty<object>())));
        if (preview.Dice.Length > 0) segments.Add(Plain(format("掷骰", Array.Empty<object>())));
        if (preview.Menu) segments.Add(Plain(format("后续选择", Array.Empty<object>())));
        if (preview.Unknown) segments.Add(Plain("?"));
        return segments;
    }
    private sealed class EffectParts
    {
        // Stats, relations and items lead; story flags follow, so a cut-off line still shows what matters most.
        private readonly List<string> named = new(), flags = new();
        private int unnamed;
        public bool Empty => named.Count == 0 && flags.Count == 0 && unnamed == 0;
        public static EffectParts Of(List<OptionEffect> effects, Func<OptionEffect, string> effectText)
        {
            var parts = new EffectParts();
            foreach (var effect in effects)
            {
                string text = effectText(effect);
                bool flag = effect.Kind == "flag" || effect.Kind == "set-flag";
                if (text.Length > 0) (flag ? parts.flags : parts.named).Add(text);
                else if (flag) parts.unnamed++;
            }
            return parts;
        }
        /// <summary>Every effect, flags by name.</summary>
        public string Line(string separator, Func<string, object[], string> format)
        {
            var all = new List<string>(named); all.AddRange(flags);
            if (unnamed > 0) all.Add(format("标记×{0}", new object[] { unnamed }));
            return string.Join(separator, all);
        }
        /// <summary>Stats, relations and items; story flags only counted.</summary>
        public string Overview(Func<string, object[], string> format)
        {
            var all = new List<string>(named);
            if (flags.Count + unnamed > 0) all.Add(format("标记×{0}", new object[] { flags.Count + unnamed }));
            return string.Join(" ", all);
        }
    }
    /// <summary>Merge a literal next script's own top-level consequences into the option that leads to it (one level only).</summary>
    public static OptionPreview Merge(OptionPreview option, OptionPreview? next)
    {
        if (next == null) return option;
        var merged = new OptionPreview { Key = option.Key, Requirement = option.Requirement, Pool = option.Pool.Length > 0 ? option.Pool : next.Pool,
            Dice = option.Dice.Length > 0 ? option.Dice : next.Dice, Menu = option.Menu || next.Menu, Unknown = option.Unknown || next.Unknown };
        merged.Effects.AddRange(option.Effects); merged.Effects.AddRange(next.Effects);
        merged.Cases.AddRange(option.Cases); merged.Cases.AddRange(next.Cases);
        return merged;
    }

    /// <summary>"exit+O_KEY+0+0", "O_KEY|>=50", "~O_KEY" and plain keys all name the same option key.</summary>
    public static string OptionKey(string raw)
    {
        string text = (raw ?? "").TrimStart('~');
        var plus = text.Split('+');
        if (plus.Length == 4) text = plus[1];
        int bar = text.IndexOf('|');
        if (bar >= 0) text = text.Substring(0, bar);
        return text == "x" ? "" : text.Trim();
    }

    private static bool SameWhen(List<KeyValuePair<string, int>> a, List<KeyValuePair<string, int>> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (a[i].Key != b[i].Key || a[i].Value != b[i].Value) return false;
        return true;
    }
    private static bool Matches(List<Token> tokens, int at, params string[] texts)
    {
        if (at + texts.Length > tokens.Count) return false;
        for (int i = 0; i < texts.Length; i++) if (tokens[at + i].Text != texts[i] || tokens[at + i].Type == T.String) return false;
        return true;
    }

    private static List<Token> Tokenize(string source)
    {
        var tokens = new List<Token>();
        for (int i = 0; i < source.Length;)
        {
            char c = source[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '-' && i + 1 < source.Length && source[i + 1] == '-')
            {
                i += 2;
                if (SkipLong(source, ref i, out _)) continue;
                int start = i; while (i < source.Length && source[i] != '\n' && source[i] != '\r') i++;
                string comment = source.Substring(start, i - start).Trim();
                if (comment.StartsWith("index:", StringComparison.Ordinal)) tokens.Add(new Token(T.Marker, comment.Substring(6).Trim()));
                continue;
            }
            if (c == '"' || c == '\'')
            {
                var text = new System.Text.StringBuilder(); i++;
                while (i < source.Length && source[i] != c)
                {
                    if (source[i] == '\\' && i + 1 < source.Length) { text.Append(source[i + 1]); i += 2; continue; }
                    if (source[i] == '\n') break;
                    text.Append(source[i++]);
                }
                i++; tokens.Add(new Token(T.String, text.ToString())); continue;
            }
            if (c == '[') { int start = i; if (SkipLong(source, ref i, out var content)) { tokens.Add(new Token(T.String, content)); continue; } i = start; }
            if (char.IsLetter(c) || c == '_')
            {
                int start = i; while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) i++;
                tokens.Add(new Token(T.Name, source.Substring(start, i - start))); continue;
            }
            if (char.IsDigit(c))
            {
                int start = i; while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '.')) i++;
                tokens.Add(new Token(T.Number, source.Substring(start, i - start))); continue;
            }
            string two = i + 1 < source.Length ? source.Substring(i, 2) : "";
            if (two == "==" || two == "~=" || two == "<=" || two == ">=" || two == "..") { tokens.Add(new Token(T.Symbol, two)); i += 2; continue; }
            tokens.Add(new Token(T.Symbol, c.ToString())); i++;
        }
        return tokens;
    }
    private static bool SkipLong(string source, ref int index, out string content)
    {
        content = "";
        if (index >= source.Length || source[index] != '[') return false;
        int next = index + 1; while (next < source.Length && source[next] == '=') next++;
        if (next >= source.Length || source[next] != '[') return false;
        string close = "]" + new string('=', next - index - 1) + "]";
        int end = source.IndexOf(close, next + 1, StringComparison.Ordinal);
        content = end < 0 ? source.Substring(next + 1) : source.Substring(next + 1, end - next - 1);
        index = end < 0 ? source.Length : end + close.Length;
        return true;
    }
}

/// <summary>One consequence segment in three forms: one line, the inline overview, and the hover's lines.</summary>
public sealed class HintSegment
{
    /// <summary>Everything on one line, e.g. 「若 道德 45>=60：变心+1 攻略小梅=1」 (shop lines).</summary>
    public string Full = "";
    /// <summary>Inline overview: arm conditions left to the hover, story flags only counted, e.g. 「若…：变心+1 标记×1」.</summary>
    public string Summary = "";
    /// <summary>Hover: the requirement, condition or marker line; for an arm, its effects follow on their own line.</summary>
    public string Head = "", Body = "";
    /// <summary>1 met, -1 unmet, 0 neutral or unknown.</summary>
    public int State;
}
