using ZhaoHuoSystem;

internal static partial class Program
{
    // Synthetic scripts in the generated story shape; no game text is included.
    private const string Menu = """
        local talk = {}
        talk[1] = "exit+O_A_001+0+0"
        local Has_Item = checkpointmanager.Condition("Has_Item")
        talk[2] = Has_Item and "default+O_A_002+0+0" or "~x"
        talk[3] = "event+O_A_003+0+0"
        talk[4] = "default+O_A_004+0+0"
        local choice_talk = choose(talk)
        if choice_talk == 1 then
            --index:O_A_001
            statmodifymanager.Player("mental", 40, "", 1)
            statmodifymanager.Character("sister1", -2, 1)
            statmodifymanager.AddFlag("Talk_01", 1)
            say(luamanager.GetStoryText("N_A_001")) -- end if then else inside a comment
        elseif choice_talk == 2 then
            --index:O_A_002
            local switch1_result = checkpointmanager.Switch("Lover_001")
            if switch1_result == 1 then
                statmodifymanager.Player("mental", 70, "", 1)
            elseif switch1_result == 2 then
                statmodifymanager.Player("change-heart", 2, "center", 1)
            end
            statmodifymanager.RemoveMisc("7002")
        elseif choice_talk == 3 then
            --index:O_A_003
            var1 = checkpointmanager.Position("Room_Work_001")
            luamanager.SetNextScript(var1)
        elseif choice_talk == 4 then
            --index:O_A_004
            local dice1 = {}
            local Work_result = checkpointmanager.Dice("Work_02", random_dice1)
            dice1[1] = "O_A_005|>=50"
            dice1[2] = "O_A_006|<50"
            runwait(dicemenudialog.ExecuteRoll(dice1, 1, "Work_02"))
            local choice_dice1 = dicemenudialog.ResultSelection
            if choice_dice1 == 1 then
                --index:O_A_005
                luamanager.SetNextScript("work_301")
            elseif choice_dice1 == 2 then
                --index:O_A_006
                statmodifymanager.Player("money", -500, "", 1)
            end
        end
        statmodifymanager.Player("team", 1, "", 1)
        """;

    private static void OptionTests()
    {
        var map = LuaOptions.Analyze(Menu);
        Test("option branch keeps only its own literal effects", () =>
        {
            var a = map["O_A_001"];
            Require(a.Effects.Count == 3 && a.Effects[0].Kind == "stat" && a.Effects[0].Key == "mental" && a.Effects[0].Value == 40, "stat effect");
            Require(a.Effects[1].Kind == "relation" && a.Effects[1].Value == -2, "negative relation literal");
            Require(a.Effects[2].Kind == "flag" && a.Cases.Count == 0 && !a.Unknown, "flag effect, nothing conditional");
        });
        Test("shared effects after the menu are never credited to one option", () =>
        {
            Require(!map["O_A_004"].Effects.Exists(e => e.Key == "team") && map[LuaOptions.ScriptLevel].Effects.Exists(e => e.Key == "team"), "post-menu effect at script level");
        });
        Test("switch arms stay conditional and keep their requirement", () =>
        {
            var b = map["O_A_002"];
            Require(b.Requirement == "Has_Item", "availability condition linked through the option table");
            Require(b.Cases.Count == 2 && b.Cases[0].When[0].Key == "Lover_001" && b.Cases[0].When[0].Value == 1 && b.Cases[1].When[0].Value == 2, "two switch arms");
            Require(b.Cases[1].Effects[0].Approximate, "center mode depends on the current value");
            Require(b.Effects.Count == 1 && b.Effects[0].Kind == "item-remove" && b.Effects[0].ItemType == "misc", "unconditional item use after the switch");
        });
        Test("random pool, dice and follow-up script are markers, not invented effects", () =>
        {
            Require(map["O_A_003"].Pool == "Room_Work_001" && map["O_A_003"].NextScript.Length == 0, "pool; variable next script is not followed");
            Require(map["O_A_004"].Dice == "Work_02" && map["O_A_004"].Effects.Count == 0, "dice option");
            Require(map["O_A_005"].NextScript == "work_301" && map["O_A_006"].Effects[0].Value == -500, "dice result rows map by table, not marker");
        });
        Test("option keys parse from every native row format", () =>
        {
            Require(LuaOptions.OptionKey("exit+O_X+0+0") == "O_X" && LuaOptions.OptionKey("O_X|>=50") == "O_X" && LuaOptions.OptionKey("~O_X") == "O_X" && LuaOptions.OptionKey("~x") == "", "row formats");
        });
        Test("duplicate option keys are reported unknown, not merged silently", () =>
        {
            var dup = LuaOptions.Analyze("local t = {}\nt[1] = \"O_D\"\nlocal c = choose(t)\nif c == 1 then\nstatmodifymanager.Player(\"mental\", 1, \"\", 1)\nend\nlocal t2 = {}\nt2[1] = \"O_D\"\nlocal c2 = choose(t2)\nif c2 == 1 then\nstatmodifymanager.Player(\"mental\", 9, \"\", 1)\nend");
            Require(dup["O_D"].Unknown, "second branch with the same key");
        });
        Test("variable amounts are unknown rather than guessed", () =>
        {
            var v = LuaOptions.Analyze("local t = {}\nt[1] = \"O_V\"\nlocal c = choose(t)\nif c == 1 then\nstatmodifymanager.Player(\"money\", var1, \"\", 1)\nend");
            Require(v["O_V"].Unknown && v["O_V"].Effects.Count == 0, "variable argument");
        });
        Test("describe keeps condition state, else arm and unnamed flags explicit", () =>
        {
            var segments = LuaOptions.Describe(LuaOptions.Merge(map["O_A_002"], null), e => e.Kind == "flag" ? "" : e.Key + e.Value.ToString("+0;-0;0"),
                (name, arm) => arm == 1 ? new KeyValuePair<string, int>("A 3>=5", -1) : new KeyValuePair<string, int>("", 1),
                name => new KeyValuePair<string, int>("C 1=1", 1), (template, args) => string.Format(template.Replace("{0}", "{0}").Replace("{1}", "{1}"), args));
            Require(Tone.Plain(segments[0].Full) == "7002+1", "a met requirement adds no words");
            Require(Tone.Plain(segments[1].Full) == "若 A 3>=5：mental+70" && segments[1].State == -1, "unmet conditional arm");
            Require(segments[1].Full.StartsWith(Tone.Negative + "若 A 3>=5：" + Tone.End, StringComparison.Ordinal), "only the unmet condition lead is toned red");
            Require(Tone.Plain(segments[2].Full) == "否则：change-heart+2" && segments[2].State == 1 && segments[2].Full[0] == Tone.Positive, "else arm toned green");
            var failing = LuaOptions.Describe(map["O_A_002"], e => e.Key, (n, a) => new KeyValuePair<string, int>("", 0), name => new KeyValuePair<string, int>("C 0=1", -1), (t, a) => string.Format(t, a));
            Require(failing[0].Full == Tone.Negative + "需 C 0=1" + Tone.End && failing[0].State == -1, "an unmet requirement leads in red");
            var flags = LuaOptions.Describe(map["O_A_001"], e => e.Kind == "flag" ? "" : e.Key, (n, a) => default, n => default, (t, a) => string.Format(t, a));
            Require(flags[0].Full.EndsWith("标记×1", StringComparison.Ordinal), "unnamed flag counted, never dropped");
        });
        Test("an else arm never appears without its if", () =>
        {
            KeyValuePair<string, int> Arm(string name, int arm) => new(arm == 1 ? "A 3>=5" : "", arm == 1 ? -1 : 1);
            string Format(string template, object[] args) => string.Format(template, args);
            // Arm 1 shows nothing here, so the default arm names the condition it stands for.
            var lone = LuaOptions.Describe(map["O_A_002"], e => e.Key == "mental" ? "" : e.Key, Arm, n => default, Format, n => "未满足 A 3>=5");
            Require(lone.Count == 2 && Tone.Plain(lone[1].Full) == "若 未满足 A 3>=5：change-heart" && lone[1].State == 1, "orphan else names its condition");
            var paired = LuaOptions.Describe(map["O_A_002"], e => e.Key, Arm, n => default, Format, n => "未满足 A 3>=5");
            Require(Tone.Plain(paired[1].Full) == "若 A 3>=5：mental" && Tone.Plain(paired[2].Full) == "否则：change-heart", "else right after its own if");
            Require(Tone.Plain(paired[1].Summary) == "若…：mental" && Tone.Plain(paired[1].Head) == "若 A 3>=5" && paired[1].Body == "→ mental", "inline leaves the condition to the hover, which splits it from its effects");
            Require(Tone.Plain(paired[2].Summary) == "否则：change-heart" && Tone.Plain(paired[2].Head) == "否则" && paired[2].Body == "→ change-heart", "else keeps its word inline and in the hover");
        });
        Test("named story flags follow stats so a cut line keeps what matters", () =>
        {
            var f = LuaOptions.Analyze("local t = {}\nt[1] = \"O_F\"\nlocal c = choose(t)\nif c == 1 then\nstatmodifymanager.SetFlag(\"F1\", 1)\nstatmodifymanager.Player(\"money\", 5, \"\", 1)\nend");
            var segments = LuaOptions.Describe(f["O_F"], e => e.Kind == "set-flag" ? e.Key + "=" + e.Value : e.Key, (n, a) => default, n => default, (t, a) => string.Format(t, a));
            Require(segments.Count == 1 && segments[0].Full == "money F1=1", "flag after stat: " + segments[0].Full);
            Require(segments[0].Summary == "money 标记×1" && segments[0].Head == "money、F1=1", "inline counts flags; the hover names them, separated by 、");
        });
        Test("an action event script's own top level is what triggering it does", () =>
        {
            // Shape of a daily-work result script: plain stat calls with display waits, no menu.
            var work = LuaOptions.Analyze("statmodifymanager.Player(\"mental\", -10, \"\", 1)\nwait(statmodifymanager.GetDisplayTime(\"mental\"))\nstatmodifymanager.Player(\"money\", -500, \"\", 1)\nstatmodifymanager.Player(\"team\", 5, \"\", 1)")[LuaOptions.ScriptLevel];
            var segments = LuaOptions.Describe(work, e => e.Key + Tone.Mark(e.Value, e.Value.ToString("+0;-0;0")), (n, a) => default, n => default, (t, a) => string.Format(t, a));
            Require(segments.Count == 1 && Tone.Plain(segments[0].Summary) == "mental-10 money-500 team+5", "all three changes in source order");
            Require(segments[0].Summary.Contains(Tone.Negative + "-10" + Tone.End) && segments[0].Summary.Contains(Tone.Positive + "+5" + Tone.End), "losses toned red, gains green");
        });
        Test("a one-shot event is one its own flag changes end after a single run, not a counter with runs left", () =>
        {
            OptionEffect Add(int d) => new() { Kind = "flag", Key = "F", Value = d };
            OptionEffect Set(int v) => new() { Kind = "set-flag", Key = "F", Value = v };
            // 行商 大失败: "Door_Work_201 < 2" with +1 runs twice; 龙湘服务生1: "!= -1" with +1 never ends.
            Require(!LuaOptions.EndsAfterOnce("<", 2, new[] { Add(1) }) && !LuaOptions.EndsAfterOnce("!=", -1, new[] { Add(1) }), "counters with runs left");
            // 福韫 揍你: "E20005 = 1" set to 0; plain "= 0" / "< 1" / "<= 0" counters end at once.
            Require(LuaOptions.EndsAfterOnce("=", 1, new[] { Set(0) }) && LuaOptions.EndsAfterOnce("=", 0, new[] { Add(1) }), "set away and equality");
            Require(LuaOptions.EndsAfterOnce("<", 1, new[] { Add(1) }) && LuaOptions.EndsAfterOnce("<=", 0, new[] { Add(1) }), "single-run counters");
            Require(!LuaOptions.EndsAfterOnce(">=", 1, new[] { Add(1) }) && LuaOptions.EndsAfterOnce(">=", 1, new[] { Set(0) }), "growing counters stay, a reset ends");
            Require(!LuaOptions.EndsAfterOnce("=", 0, new OptionEffect[0]) && LuaOptions.Mirror("<") == ">" && LuaOptions.Mirror("=") == "=", "no change, mirrored sides");
        });
        Test("an event's own flag set later in its story, inside a choice, still makes it one-shot; a location menu ends the search", () =>
        {
            // Shape of 龍之反省: the result script only moves on; the story sets its flag in one choice; then back to the gate menu.
            var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ev_503"] = "luamanager.SetNextScript(\"story\")",
                ["story"] = "local t = {}\nt[1] = \"O_S_1\"\nt[2] = \"O_S_2\"\nlocal c = choose(t)\nif c == 1 then\nluamanager.SetNextScript(\"story_a\")\nelseif c == 2 then\nluamanager.SetNextScript(\"story_b\")\nend",
                ["story_a"] = "statmodifymanager.SetFlag(\"S1\", 1)\nstatmodifymanager.AddFlag(\"C\", 1)\nluamanager.SetNextScript(\"hub\")",
                ["story_b"] = "luamanager.SetNextScript(\"hub\")",
                ["hub"] = "statmodifymanager.SetFlag(\"H\", 1)\nvar1 = checkpointmanager.Position(\"Door_02\")"
            };
            var maps = new Dictionary<string, Dictionary<string, OptionPreview>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, OptionPreview>? Map(string name) => texts.TryGetValue(name, out var t) ? (maps.TryGetValue(name, out var m) ? m : maps[name] = LuaOptions.Analyze(t)) : null;
            var changes = LuaOptions.EventFlags("ev_503", Map, name => texts.TryGetValue(name, out var t) ? t : null);
            Require(changes.Certain.Count == 0 && changes.Possible.ContainsKey("S1") && !changes.Possible.ContainsKey("H"), "flag found two scripts on, the gate menu's own flags left out");
            Require(LuaOptions.Ends("<=", 0, changes, "S1") && LuaOptions.Ends("=", 0, changes, "S1"), "S0023_01 <= 0 ends after one run");
            // A counter met on one path still has runs left (F40), and nothing ends a flag the story never touches.
            Require(!LuaOptions.Ends("<", 2, changes, "C") && !LuaOptions.Ends("=", 0, changes, "H"), "counter and untouched flag");
            var certain = LuaOptions.EventFlags("story_a", Map, name => texts.TryGetValue(name, out var t) ? t : null);
            Require(certain.Certain.ContainsKey("S1") && !LuaOptions.Ends("<", 2, certain, "C"), "top-level changes still run as one sequence");
        });
        Test("the script a fight goes on to is followed like a next script; a literal next script in the same block wins", () =>
        {
            // Shape of ch3_8_2_3: register the continuation, then change to the fight; the continuation sets a flag and draws a pool.
            var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["before"] = "statmodifymanager.SetFlag(\"A\", 1)\nluamanager.SetCurrentStoryScript(\"after\")\nluamanager.ChangeScene(\"Combat\", \"1309_01\", \"Story\")",
                ["after"] = "statmodifymanager.SetFlag(\"S1\", 1)\nvar1 = checkpointmanager.Position(\"Pool_After\")",
                ["both"] = "luamanager.SetCurrentStoryScript(\"later\")\nluamanager.SetNextScript(\"now\")"
            };
            Dictionary<string, OptionPreview>? Map(string name) => texts.TryGetValue(name, out var t) ? LuaOptions.Analyze(t) : null;
            string? Text(string name) => texts.TryGetValue(name, out var t) ? t : null;
            Equal("after", LuaOptions.Analyze(texts["before"])[LuaOptions.ScriptLevel].NextScript);
            Equal("now", LuaOptions.Analyze(texts["both"])[LuaOptions.ScriptLevel].NextScript);
            Require(LuaOptions.Reach("before", Text, _ => Array.Empty<string>()).Exists(r => r.Pool == "Pool_After"), "pool drawn after the fight is reachable");
            var changes = LuaOptions.EventFlags("before", Map, Text);
            Require(changes.Certain.ContainsKey("A") && changes.Certain.ContainsKey("S1"), "the continuation's own flags count");
        });
        Test("an option's next script that only starts a fight shows what happens after the fight; a plain jump stays one level", () =>
        {
            var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["launch"] = "luamanager.SetCurrentStoryScript(\"after\")\nluamanager.ChangeScene(\"Battle\", \"S2321_01\", \"Story\")",
                ["after"] = "statmodifymanager.Player(\"fame\", 2, \"\", 1)\nluamanager.SetNextScript(\"later\")",
                ["plain"] = "statmodifymanager.Player(\"training\", 1, \"\", 1)\nluamanager.SetNextScript(\"after\")"
            };
            string? Text(string name) => texts.TryGetValue(name, out var t) ? t : null;
            var launch = LuaOptions.NextTop("launch", Text)!;
            Require(launch.Effects.Exists(e => e.Key == "fame") && launch.NextScript.Length == 0, "rewards after the fight, nothing further");
            var plain = LuaOptions.NextTop("plain", Text)!;
            Require(plain.Effects.Exists(e => e.Key == "training") && !plain.Effects.Exists(e => e.Key == "fame"), "a direct jump is not followed");
            Require(LuaOptions.NextTop("missing", Text) == null, "unknown script");
        });
        Test("a conditional arm that jumps into a story is kept and named; the default arm's jump is not", () =>
        {
            // Shape of 飞天门's 修炼: a switch whose first arm leads into a story, whose default arm is the ordinary training.
            const string script = "local t = {}\nt[1] = \"immortal+free+immortal\"\nt[2] = \"training2+free+training2\"\nlocal c = choose(t)\nif c == 1 then\nelseif c == 2 then\n"
                + "local s = checkpointmanager.Switch(\"Story_Gate\")\nif s == 1 then\nluamanager.SetNextScript(\"S2600_01\")\nelseif s == 2 then\nluamanager.SetNextScript(\"training_02\")\nend\nend\n";
            var preview = LuaOptions.Analyze(script)["training2+free+training2"];
            Equal("S2600_01", preview.Cases.Find(c => c.When.Count == 1 && c.When[0].Value == 1)?.Story);
            var segments = LuaOptions.Describe(preview, _ => "", (name, arm) => arm == 1 ? new KeyValuePair<string, int>("拜码头", 1) : new KeyValuePair<string, int>("", -1),
                _ => new KeyValuePair<string, int>("", 1), (template, args) => args.Length == 0 ? template : string.Format(template.Replace("{0}", "{0}"), args));
            Require(segments.Count == 1 && segments[0].Full.Contains("触发剧情") && segments[0].Head.Contains("拜码头") && segments[0].State == 1, "story arm shown once, with its condition");
        });
        Test("a locked row is matched by its place in the option table, its locked text and the rows already shown", () =>
        {
            const string script = "local Ch_1 = checkpointmanager.Condition(\"Ch_1\")\nlocal Ch_2 = checkpointmanager.Condition(\"Ch_2\")\nlocal t = {}\nt[1] = \"O_A\"\nt[2] = Ch_1 and \"O_B\" or \"~x\"\nt[3] = Ch_2 and \"O_C\" or \"~O_C_Locked\"\nlocal c = choose(t)\nif c == 1 then\nelseif c == 2 then\nstatmodifymanager.Player(\"fame\", 2, \"\", 1)\nend\n"
                + "local u = {}\nu[1] = \"O_Other\"\nu[2] = Ch_2 and \"O_D\" or \"~x\"\nlocal d = choose(u)\n";
            var map = LuaOptions.Analyze(script);
            var menus = map[LuaOptions.ScriptLevel].Menus;
            Equal(2, menus.Count);
            Equal("x", menus[0][2].Locked); Equal("Ch_1", menus[0][2].Requirement); Equal("O_C_Locked", menus[0][3].Locked); Equal("", menus[0][1].Locked);
            Equal("O_B", menus[0][2].Text); Equal("O_A", menus[0][1].Text); // what the row shows once opened (F74)
            var scripts = new[] { (IEnumerable<Dictionary<int, OptionSlot>>)menus };
            // Two tables lock row 2 behind 「x」: the row above tells them apart.
            Equal("O_B", LuaOptions.LockedSlot(scripts, 2, "x", new Dictionary<int, string> { [1] = "O_A" })?.Key);
            Equal("O_D", LuaOptions.LockedSlot(scripts, 2, "x", new Dictionary<int, string> { [1] = "O_Other" })?.Key);
            Require(LuaOptions.LockedSlot(scripts, 2, "x", new Dictionary<int, string>()) == null, "nothing shown yet: two different answers, none given");
            Equal("O_C", LuaOptions.LockedSlot(scripts, 3, "O_C_Locked", new Dictionary<int, string>())?.Key);
            Require(LuaOptions.LockedSlot(scripts, 3, "x", new Dictionary<int, string>()) == null, "locked text must match");
        });
        Test("pools reachable from a location follow scripts, menu options and pool results with their option path", () =>
        {
            // Shape of the gate: own pool -> a result that opens the "where to" menu -> a shop with two pools; one option loops back.
            var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["gate"] = "var1 = checkpointmanager.Position(\"Gate_Pool\")\nluamanager.SetNextScript(var1)",
                ["gate_a"] = "luamanager.SetNextScript(\"outside\")",
                ["outside"] = "local t = {}\nt[1] = \"O_Shop\"\nt[2] = \"O_Back\"\nlocal c = choose(t)\nif c == 1 then\n--index:O_Shop\nluamanager.SetNextScript(\"shop\")\nelseif c == 2 then\n--index:O_Back\nluamanager.SetNextScript(\"gate\")\nend",
                ["shop"] = "var1 = checkpointmanager.Position(\"Shop_Work\")\nvar2 = checkpointmanager.Position(\"Shop_Extra\")\nluamanager.SetNextScript(var2)",
            };
            var pools = new Dictionary<string, string[]> { ["Gate_Pool"] = new[] { "gate_a" }, ["Shop_Work"] = new string[0], ["Shop_Extra"] = new string[0] };
            var reached = LuaOptions.Reach("gate", n => texts.TryGetValue(n, out var t) ? t : null, p => pools.TryGetValue(p, out var s) ? s : Array.Empty<string>());
            Require(reached.Count == 3 && reached[0].Pool == "Gate_Pool" && reached[0].Path.Count == 0, "the location's own pool first, no menu passed");
            Require(reached.Skip(1).Select(r => r.Pool).OrderBy(p => p).SequenceEqual(new[] { "Shop_Extra", "Shop_Work" }), "both pools a branch draws, once each, loop ignored");
            Require(reached.Skip(1).All(r => r.Path.SequenceEqual(new[] { "O_Shop" })), "outside pools carry the menu option that leads there");
        });
        Test("a literal next script contributes its own top level once", () =>
        {
            var next = LuaOptions.Analyze("statmodifymanager.Player(\"money\", 1000, \"\", 1)\nluamanager.SetNextScript(\"deeper\")")[LuaOptions.ScriptLevel];
            next.NextScript = "";
            var merged = LuaOptions.Merge(map["O_A_005"], next);
            Require(merged.Effects.Count == 1 && merged.Effects[0].Value == 1000, "one level merged");
        });

        Test("an option that sets the main mission is marked; a story-scene loop is told from the map side (F80)", () =>
        {
            var m = LuaOptions.Analyze("local t = {}\nt[1] = \"O_M1\"\nt[2] = \"O_M2\"\nlocal c = choose(t)\nif c == 1 then\nstatmodifymanager.Player(\"action\", -2, \"\", 1)\nstatmodifymanager.Mission(\"Main\", \"M1\")\nelseif c == 2 then\nstatmodifymanager.Mission(\"Sub\", \"M2\")\nend");
            Require(m["O_M1"].SetsMain && !m["O_M2"].SetsMain, "main only");
            string? Entry(string name) => name switch
            {
                "loop_entry" => "luamanager.SetCurrentStoryScript(\"loop_menu\")\nluamanager.ChangeScene(\"Story\", \"\", \"\")",
                "map_entry" => "luamanager.SetCurrentStoryScript(\"loop_menu\")\nluamanager.ChangeScene(\"Free\", \"\", \"\")",
                _ => null
            };
            Require(LuaOptions.StoryLoop("loop_entry", "loop_menu", Entry), "entry sets the menu and the story scene");
            Require(LuaOptions.StoryLoop("Loop_Menu", "loop_menu", Entry), "the menu is its own travel script");
            Require(!LuaOptions.StoryLoop("map_entry", "loop_menu", Entry) && !LuaOptions.StoryLoop("loop_entry", "other", Entry) && !LuaOptions.StoryLoop("", "loop_menu", Entry), "not a loop");
        });

        // Optional offline audit over a local, untracked dump of the game's scripts.
        string? dump = Environment.GetEnvironmentVariable("ZHAOHUO_LUA_DUMP");
        if (dump == null || !Directory.Exists(dump)) return;
        if (File.Exists(Path.Combine(dump, "ch2_4_3_break_start_01.txt")))
            Test("real story-scene loops (破庙, opening sections, …) and 客栈夜晚「出门散心」 setting the main mission (F80)", () =>
            {
                string? Text(string name) { var path = Path.Combine(dump, name.ToLowerInvariant() + ".txt"); return File.Exists(path) ? File.ReadAllText(path) : null; }
                foreach (var (travel, menu) in new[] { ("ch2_4_3_break_start_01", "ch2_4_3_break_start_01"), ("ch3_6_1_break_01", "ch3_6_1_break_start_01"), ("ch6_4_3_break_01", "ch6_4_3_break_start_01"),
                    ("S2503_break_01", "S2503_break_start_01"), ("section_01_free_start_01", "section_01_free_start_02"), ("section_02_free_start_01", "section_02_free_start_02") })
                    Require(LuaOptions.StoryLoop(travel, menu, Text), travel + " loop");
                Require(!LuaOptions.StoryLoop("ch2_2_3_1_travel_02", "ch2_4_3_break_start_01", Text), "a stale travel script is not this loop");
                var night = LuaOptions.Analyze(Text("ch3_1_7_6_break_start_01")!);
                Require(night["O_3_1_7_6_Break_01_001"].SetsMain && !night["O_3_1_7_6_Break_01_002"].SetsMain, "出门散心 sets the main mission");
                var temple = LuaOptions.Analyze(Text("ch2_4_3_break_start_01")!);
                Require(!temple.Any(pair => pair.Key.Length > 0 && pair.Value.SetsMain), "破庙 menu rows leave the main mission alone");
            });
        if (File.Exists(Path.Combine(dump, "section_01_free_08_01.txt")))
            Test("real 火闪电 fight (section_01_free_08_01) reaches M20045, set in the script after the fight", () =>
            {
                string? Text(string name) { var path = Path.Combine(dump, name.ToLowerInvariant() + ".txt"); return File.Exists(path) ? File.ReadAllText(path) : null; }
                var changes = LuaOptions.EventFlags("section_01_free_08_01", name => Text(name) is string t ? LuaOptions.Analyze(t) : null, Text);
                Require(changes.Certain.ContainsKey("M20045") || changes.Possible.ContainsKey("M20045"), "M20045 after the fight");
            });
        if (File.Exists(Path.Combine(dump, "section_01_free_01.txt")))
            Test("real 飞天门 修炼 (section_01_free_01) arm leads into S2600_01", () =>
            {
                var map = LuaOptions.Analyze(File.ReadAllText(Path.Combine(dump, "section_01_free_01.txt")));
                Require(map.Values.Any(p => p.Cases.Exists(c => c.Story == "S2600_01")), "story arm");
            });
        if (File.Exists(Path.Combine(dump, "ch2_4_2.txt")))
            Test("real ch2_4_2 locked row 「~O_2_4_2_002」 stands for O_2_4_2_001 behind Ch_2_4_2_001", () =>
            {
                var menus = LuaOptions.Analyze(File.ReadAllText(Path.Combine(dump, "ch2_4_2.txt")))[LuaOptions.ScriptLevel].Menus;
                var slot = LuaOptions.LockedSlot(new[] { (IEnumerable<Dictionary<int, OptionSlot>>)menus }, 1, "O_2_4_2_002", new Dictionary<int, string>());
                Require(slot != null && slot.Key == "O_2_4_2_001" && slot.Requirement == "Ch_2_4_2_001", "slot");
            });
        if (File.Exists(Path.Combine(dump, "s2321_02_03_02.txt")))
            Test("real 「崆峒探险队强袭六道峡！」 battle launcher shows the rewards after the battle", () =>
            {
                string? Text(string name) { var path = Path.Combine(dump, name.ToLowerInvariant() + ".txt"); return File.Exists(path) ? File.ReadAllText(path) : null; }
                var top = LuaOptions.NextTop("S2321_02_03_02", Text)!;
                Require(top.Effects.Exists(e => e.Key == "martial-point") && top.Effects.Exists(e => e.Key == "fame"), "martial-point and fame after the battle");
            });
        if (File.Exists(Path.Combine(dump, "door_02_503.txt")))
            Test("real 龍之反省 (door_02_503) ends through S0023_01 set inside a later choice", () =>
            {
                string? Text(string name) { var path = Path.Combine(dump, name.ToLowerInvariant() + ".txt"); return File.Exists(path) ? File.ReadAllText(path) : null; }
                var changes = LuaOptions.EventFlags("door_02_503", name => Text(name) is string t ? LuaOptions.Analyze(t) : null, Text);
                Require(LuaOptions.Ends("<=", 0, changes, "S0023_01"), "S0023_01 <= 0");
            });
        int scripts = 0, options = 0, withEffects = 0, unknown = 0, failures = 0;
        foreach (var file in Directory.EnumerateFiles(dump, "*.txt"))
        {
            try
            {
                var result = LuaOptions.Analyze(File.ReadAllText(file)); scripts++;
                foreach (var pair in result)
                {
                    if (pair.Key.Length == 0) continue;
                    options++; if (!pair.Value.Empty) withEffects++; if (pair.Value.Unknown) unknown++;
                }
            }
            catch (Exception ex) { failures++; Console.WriteLine("AUDIT_FAIL " + Path.GetFileName(file) + " " + ex.Message); }
        }
        Console.WriteLine($"AUDIT scripts={scripts} options={options} withConsequence={withEffects} unknown={unknown} failures={failures}");
        foreach (string name in (Environment.GetEnvironmentVariable("ZHAOHUO_LUA_SHOW") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            foreach (var pair in LuaOptions.Analyze(File.ReadAllText(Path.Combine(dump, name + ".txt"))))
            {
                var p = pair.Value;
                Console.WriteLine($"SHOW {name} [{pair.Key}] req={p.Requirement} pool={p.Pool} dice={p.Dice} next={p.NextScript} menu={p.Menu} unknown={p.Unknown} effects={string.Join(" ", p.Effects.ConvertAll(e => e.Kind + ":" + e.Key + ":" + e.Value))} cases={string.Join(" | ", p.Cases.ConvertAll(c => string.Join("&", c.When.ConvertAll(w => w.Key + "#" + w.Value)) + "=>" + string.Join(" ", c.Effects.ConvertAll(e => e.Key + ":" + e.Value))))}");
            }
    }
}
