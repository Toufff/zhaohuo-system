using ZhaoHuoSystem;

var values = new List<ValueSnapshot> {
    new() { Id = "stat:alignment", Name = "善恶", GroupId = "alignment", Group = "阴阳与善恶", SearchText = "good evil 善惡" },
    new() { Id = "relation:person", Name = "角色", GroupId = "relationships", Group = "角色关系" },
    new() { Id = "faction:one", Name = "门派", GroupId = "factions", Group = "门派关系" },
    new() { Id = "stat:道德", Name = "道德", GroupId = "attributes", Group = "属性", SearchText = "PlayerStat/karma" },
    new() { Id = "stat:門派規模", Name = "门派规模", GroupId = "sect", Group = "门派" },
    new() { Id = "library:achievement:one", Name = "见闻", GroupId = "library", Group = "风云史", Writable = false }
};
void Assert(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
{
    // 回想 (F101): offsets into the log as GetPrettyHistory writes it, and the drawn index past rich-text tags.
    var log = new List<KeyValuePair<string, string>> { new("唐布衣", "师弟。"), new("", "旁白"), new("赵活系统", "<size=24>剧情分支：A</size>\n<color=#D9B077>↩ 回</color>") };
    string pretty = "\n";
    foreach (var e in log) { if (e.Key.Length > 0) pretty += "<size=28><color=#D9B077>" + e.Key + "</color></size>\n"; pretty += e.Value + "\n\n"; }
    var starts = NativeUiState.LogTextStarts(log, 1000);
    Assert(starts.Select((at, i) => pretty.Substring(at, log[i].Value.Length) == log[i].Value).All(x => x), "log offsets match the pretty history exactly");
    var last = NativeUiState.LogTextStarts(log, 2);
    Assert(last[0] == -1 && last[1] == 1, "entries past the log's limit are left out and the rest start after its line break");
    string rich = "<size=24>剧情分支：<color=#8FDEA3>X 3>=1</color></size>";
    int raw = rich.IndexOf("X", StringComparison.Ordinal);
    Assert(NativeUiState.DrawnIndex(rich, raw) == "剧情分支：".Length, "tags take no place before the drawn character");
    Assert(NativeUiState.DrawnIndex("a<5b", 3) == 3 && NativeUiState.DrawnIndex("x 3>=1", 6) == 6, "angle brackets that are not tags are drawn");
}
Assert(string.Join(",", NativeUiState.Groups(values)) == "all,attributes,sect,alignment,relationships,factions,library", "player-facing groups keep sect state, relations and library distinct");
Assert(NativeUiState.MatchesValue(values[3], "attributes", "PlayerStat/karma") && !NativeUiState.MatchesValue(values[3], "alignment", ""), "moved attribute is searchable exactly once in its player-facing group");
Assert(NativeUiState.MatchesValue(values[4], "sect", "stat:門派規模") && !NativeUiState.MatchesValue(values[4], "factions", ""), "sect state is searchable by stable ID without becoming diplomatic relations");
Assert(NativeUiState.MatchesValue(values[5], "library", "library:achievement:one") && !NativeUiState.MatchesValue(values[5], "other", "") && !values[5].Writable, "library count has its own page and remains read-only");
values[0].Name = "선악"; values[0].Group = "음양과 선악";
Assert(NativeUiState.MatchesValue(values[0], "alignment", "stat:alignment"), "raw ID survives language change");
Assert(NativeUiState.MatchesValue(values[0], "alignment", "善惡"), "cross-language aliases remain searchable");
Assert(NativeUiState.MatchesValue(values[0], "alignment", "선악"), "current localized name remains searchable");
Assert(!NativeUiState.MatchesValue(values[1], "factions", ""), "faction filter cannot include people");
Assert(NativeUiState.ChanceLabel(50 / 100.0) == "50%" && NativeUiState.ChanceLabel(1.0) == "100%" && NativeUiState.ChanceLabel(0) == "0%", "dice branch chance is a whole percent");
Assert(NativeUiState.ChanceLabel(1 / 1000.0) == "<1%" && NativeUiState.ChanceLabel(999 / 1000.0) == ">99%", "near-certain or near-impossible branches are never rounded to 0% or 100%");
Assert(NativeUiState.ChanceLabel(null) == "?", "uncounted range is explicit");
{
    // Base 0–80 plus +100: the total is always ≥80, so 「<80」 needs a raw value worked back from the total.
    int Branch(int raw) => raw + 100 >= 80 ? 1 : 2;
    var raws = NativeUiState.DiceRaws(0, 80, 99, new[] { 1, 2 }, Branch, out var hits);
    Assert(raws[1] == 0 && hits![1] == 81 && !hits.ContainsKey(2), "natural rolls all land on ≥80 and keep the natural share");
    Assert(raws[2] == -21 && Branch(raws[2]) == 2, "an unreachable branch gets the raw value nearest the range: −21 + 100 = 79");
    var high = NativeUiState.DiceRaws(0, 80, 99, new[] { 1, 2 }, raw => raw + 100 >= 200 ? 2 : 1, out _);
    Assert(raws.Count == 2 && !high.ContainsKey(2), "a branch needing a raw value above the wheel's 99 is left to be forced");
}
{
    // Width of "（a；b）" counted in characters stands in for the native text generator.
    float Measure(List<KeyValuePair<string, int>> list) => 2 + list.Sum(s => s.Key.Length) + Math.Max(0, list.Count - 1);
    var segments = new List<KeyValuePair<string, int>> { new("abcd", 1), new("efgh", 0), new("ij", -1) };
    var whole = NativeUiState.FitSegments(segments, Measure, 14, out bool cutWhole);
    Assert(ReferenceEquals(whole, segments) && !cutWhole, "consequences that fit stay whole");
    var some = NativeUiState.FitSegments(segments, Measure, 11, out bool cutSome);
    Assert(cutSome && some.Count == 2 && some[0].Key == "abcd" && some[0].Value == 1 && some[1].Key == "…", "a long list keeps leading segments and their colour, then …");
    var part = NativeUiState.FitSegments(segments, Measure, 5, out _);
    Assert(part.Count == 1 && part[0].Key == "ab…" && part[0].Value == 1, "an overlong first segment is shortened by characters");
    var none = NativeUiState.FitSegments(segments, Measure, 2, out bool cutNone);
    Assert(cutNone && none.Count == 1 && none[0].Key == "…", "no room still says more exists");
    string marked = Tone.Mark(-1, "若…：") + "性情" + Tone.Mark(-1, "-1") + " 嘴力" + Tone.Mark(1, "+1") + " 标记" + Tone.Mark(0, "=0");
    Assert(NativeUiState.ToneColors(marked, "#0F0", "#F00") == "<color=#F00>若…：</color>性情<color=#F00>-1</color> 嘴力<color=#0F0>+1</color> 标记=0", "one meaning: unmet and losses red, gains green");
    Assert(NativeUiState.ToneColors(marked, null, null) == "若…：性情-1 嘴力+1 标记=0" && Tone.Plain(marked) == "若…：性情-1 嘴力+1 标记=0", "plain targets lose the marks entirely");
    Assert(NativeUiState.ToneColors(marked.Substring(0, 2), "#0F0", "#F00") == "<color=#F00>若</color>", "a cut inside a mark still closes its colour");
}
Console.WriteLine("UI state tests passed; Unity layout and native input are not exercised.");


var enumMeanings = new Dictionary<int, string> { [99] = "self", [2] = "second", [0] = "zero", [1] = "first" };
var enumRows = NativeUiState.EnumRows(enumMeanings);
Assert(enumRows.Select(row => row.Key).SequenceEqual(new[] { 0, 1, 2, 99 }), "enum tooltip preserves sparse numeric values in ascending order");
Assert(enumRows[0].Value == "zero" && enumRows[^1].Value == "self", "zero is an explicit label and sparse 99 is never renumbered");
foreach (var language in new[] { "简中测试名称", "繁中測試名稱", "검증한표시이름" })
{
    var labels = Enumerable.Range(0, 26).Select(number => number + " = " + language).ToList();
    float Measure(string text, float width) => Math.Max(1, (float)Math.Ceiling(text.Length * 12f / width)) * 17;
    var grid = NativeUiState.EnumTooltipLayout(labels, 1256, 696, 230, 140, 18, 11, 22, 30, Measure, width => Measure("Current = " + language, width) + 9);
    Assert(grid.Columns == 2 && grid.Rows == 13 && grid.VisibleCount == 26 && !grid.Truncated, "all 26 translated enum values fit two columns at 1280x720: " + language);
    Assert(grid.Width <= 1256 && grid.Height <= 696, "enum tooltip stays within 1280x720: " + language);
}
var lovers = Enumerable.Range(0, 10).Select(number => number + " = label").ToList();
var loverGrid = NativeUiState.EnumTooltipLayout(lovers, 1256, 696, 160, 140, 18, 11, 22, 30, (_, _) => 20, _ => 28);
Assert(loverGrid.Columns == 1 && loverGrid.VisibleCount == 10 && !loverGrid.Truncated, "ten meanings remain a compact one-column hover");
var longList = Enumerable.Range(0, 200).Select(number => number + " = long translated enum label").ToList();
var limitedGrid = NativeUiState.EnumTooltipLayout(longList, 300, 220, 250, 140, 18, 11, 22, 30, (_, _) => 36, _ => 32);
Assert(limitedGrid.Truncated && limitedGrid.VisibleCount < longList.Count && limitedGrid.VisibleCount >= 0, "small screens explicitly report incomplete mappings rather than silently clipping");
Assert(limitedGrid.Width <= 300 && limitedGrid.Height <= 220 && limitedGrid.Rows * limitedGrid.Columns == limitedGrid.VisibleCount, "fallback grid preserves bounds and complete visible cells");
var noEnums = NativeUiState.EnumTooltipLayout(Array.Empty<string>(), 1256, 696, 160, 140, 18, 11, 22, 30, (_, _) => 20, _ => 28);
Assert(noEnums.VisibleCount == 0 && noEnums.Columns == 0, "ordinary integer fields never acquire an empty enum tooltip");

var input = new InputIsolationPolicy();
Assert(!input.Pointer(1, false, 0) && !input.BlockPointer, "open panel does not block the pointer outside its bounds");
input.Pointer(1, true, 0);
Assert(input.BlockPointer, "hover over the mod reserves only its local pointer region");
Assert(input.Pointer(1, false, 1) && !input.BlockPointer && input.OwnedButtons == 0, "inside hover then same-event outside press passes on its first click");
input.Pointer(2, true, 1);
Assert(!input.BlockPointer, "game-origin drag crossing the mod retains game ownership");
input.Pointer(2, false, 0);
Assert(!input.BlockPointer, "game-origin release outside is delivered normally");
input.Pointer(3, true, 1);
Assert(input.BlockPointer && input.OwnedButtons == 1, "first queued press inside the mod is claimed before callbacks");
input.Pointer(4, false, 1);
Assert(input.BlockPointer, "mod-origin drag remains captured after leaving the window");
input.Pointer(4, false, 0);
Assert(input.BlockPointer, "mod-origin release remains gated for the release frame");
input.Pointer(4, false, 0);
Assert(input.BlockPointer, "repeated before-update calls cannot drop a release-only gate");
input.Pointer(5, false, 0);
Assert(!input.BlockPointer, "the next idle frame restores outside interaction without a scan delay");
input.Pointer(6, true, 2); input.Pointer(6, false, 0);
Assert(input.BlockPointer, "right-button release-only callbacks stay gated outside");
input.Pointer(6, false, 1);
Assert(!input.BlockPointer, "a distinct later outside press in the same frame is not swallowed by the prior release latch");
input.Reset(); input.Pointer(7, true, 4); input.Pointer(8, false, 4);
Assert(input.BlockPointer && input.OwnedButtons == 4, "middle-button ownership survives a boundary crossing");
input.Reset();
Assert(!input.BlockPointer && !input.BlockKeyboard && input.OwnedButtons == 0, "focus loss or scene reset releases all owned policy state");
Assert(!InputIsolationPolicy.IsTextControl("zh.button.tab1") && !InputIsolationPolicy.IsTextControl("zh.button.value.apply.stat:money"), "button focus is not text editing");
Assert(InputIsolationPolicy.IsTextControl("zh.event.search") && InputIsolationPolicy.IsTextControl("zh.value.search") && InputIsolationPolicy.IsTextControl("zh.value.stat:money"), "only named search and numeric editors isolate keyboard input");
Assert(InputIsolationPolicy.IsTextControl("zh.settings.search") && InputIsolationPolicy.IsTextControl("zh.settings.slots") && !InputIsolationPolicy.IsTextControl("zh.button.settings.slots.up"), "the settings search and slot count isolate keyboard input, their buttons do not");
input.Keyboard(10, true, false);
Assert(input.BlockKeyboard, "main text editing gates keyboard shortcuts");
input.Pointer(10, false, 1); input.Keyboard(10, false, false);
Assert(!input.BlockPointer && input.BlockKeyboard, "outside first click passes while same-frame keyboard blur stays isolated");
input.Keyboard(11, false, false);
Assert(!input.BlockKeyboard, "keyboard shortcuts resume after blur's release frame");
input.Keyboard(12, false, true);
Assert(input.BlockKeyboard, "native inline dice text editing shares the keyboard gate");
input.Keyboard(12, true, false);
Assert(input.BlockKeyboard, "moving between inline and main text editors never opens a keyboard gap");
input.Keyboard(13, false, false); input.Keyboard(14, false, false);
Assert(!input.BlockKeyboard, "both text owners can release without reviving an old focus");
Console.WriteLine("Input policy tests are offline; native EventSystem, pointer dispatch and IMGUI remain untested in game.");

input.Reset(); input.Pointer(20, false, 2); input.Pointer(21, true, 2); input.Pointer(21, true, 0);
Assert(!input.BlockPointer, "game-origin right release inside completes release-only native actions");
input.Pointer(21, true, 0);
Assert(!input.BlockPointer, "same-frame navigation polling retains the game-origin release decision");
input.Pointer(22, true, 0);
Assert(input.BlockPointer, "the mod local hover resumes after the external release frame");

Assert(NativeUiState.ConditionOperand("攻略龙湘", "0", false) + "=" + NativeUiState.ConditionOperand("1", "1", true) == "攻略龙湘 0=1", "live operand shows its current value before the comparison");
Assert(NativeUiState.ConditionOperand("行动次数", "3", false) + ">=" + NativeUiState.ConditionOperand("1", "1", true) == "行动次数 3>=1", "constant target stays a bare value");
Assert(NativeUiState.ConditionOperand("Z00002", "", false) == "Z00002 ?", "unknown current value is explicit, never 0");
Assert(NativeUiState.ConditionOperand("(A 或 B)", null, false) == "(A 或 B)", "nested condition shows only its expression");
Assert(NativeUiState.ConditionOperand("2年12月下旬", "2年12月下旬", true) == "2年12月下旬", "fixed date target is not repeated");

input.Reset(); input.Pointer(30, true, 1); input.Keyboard(30, false, false);
Assert(input.BlockKeyboard, "keyboard queued behind a press inside the mod waits for OnGUI text focus");
input.FocusResolved(); input.Keyboard(30, false, false);
Assert(!input.BlockKeyboard, "a resolved button click never becomes a lasting keyboard gate");
input.Pointer(31, true, 0); input.Pointer(40, true, 1); input.Keyboard(41, false, false);
Assert(input.BlockKeyboard, "IMGUI one frame behind InputSystem keeps the press protected");
input.Keyboard(42, false, false);
Assert(!input.BlockKeyboard, "an unresolved press releases the keyboard after a bounded frame");
input.Reset(); input.Pointer(50, false, 1); input.Keyboard(50, false, false);
Assert(!input.BlockKeyboard, "an outside press never gates game keyboard input");

Assert(NativeUiState.TryParseAnchor(NativeUiState.FormatAnchor(.25f, 1), out var anchorX, out var anchorY) && Math.Abs(anchorX - .25f) < .0001f && anchorY == 1, "launcher anchor round-trips through the mod config");
Assert(!NativeUiState.TryParseAnchor("", out _, out _) && !NativeUiState.TryParseAnchor("0.5", out _, out _) && !NativeUiState.TryParseAnchor("abc,0.2", out _, out _) && !NativeUiState.TryParseAnchor("1.5,0.2", out _, out _) && !NativeUiState.TryParseAnchor("NaN,0.2", out _, out _), "empty or damaged launcher config falls back to the default spot");

Assert(NativeUiState.IsBindableKey("F8") && NativeUiState.IsBindableKey("H") && NativeUiState.IsBindableKey("Alpha1"), "ordinary keys can be bound");
Assert(!NativeUiState.IsBindableKey("None") && !NativeUiState.IsBindableKey("Escape") && !NativeUiState.IsBindableKey("Mouse0") && !NativeUiState.IsBindableKey("LeftShift") && !NativeUiState.IsBindableKey("RightControl") && !NativeUiState.IsBindableKey("JoystickButton0"), "Esc, mouse, joystick and lone modifiers are refused");
Assert(NativeUiState.InputSystemKeyName("F8") == "F8" && NativeUiState.InputSystemKeyName("Alpha1") == "Digit1" && NativeUiState.InputSystemKeyName("Keypad5") == "Numpad5" && NativeUiState.InputSystemKeyName("Return") == "Enter" && NativeUiState.InputSystemKeyName("BackQuote") == "Backquote", "KeyCode names map to InputSystem keys");
Assert(NativeUiState.VirtualKey("F8") == 0x77 && NativeUiState.VirtualKey("F1") == 0x70 && NativeUiState.VirtualKey("H") == 0x48 && NativeUiState.VirtualKey("Alpha0") == 0x30 && NativeUiState.VirtualKey("Keypad9") == 0x69 && NativeUiState.VirtualKey("Comma") == 0, "native fallback keys match Win32 codes or stay unmapped");
