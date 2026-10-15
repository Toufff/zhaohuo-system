using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using MoonSharp.Interpreter;
using ZhaoHuoSystem;

internal static partial class Program
{
    private const string Marker = "__zhaohuo_wait_position(";
    private static int passed, failed;

    private static int Main(string[] args)
    {
        LexicalTests();
        FixedEffectTests();
        ValueTests();
        OptionTests();
        string gameDir = args.Length > 0 ? args[0] : @"F:\SteamLibrary\steamapps\common\LegendOfMortal";
        string managed = Path.Combine(gameDir, "Mortal_Data", "Managed");
        try
        {
            // Read assemblies in place. No game assembly is copied to the project or output.
            AssemblyLoadContext.Default.Resolving += (_, name) =>
            {
                string path = Path.Combine(managed, name.Name + ".dll");
                return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
            };
            AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managed, "Fungus.dll"));
            InterpreterTests();
            NativeEntryTests(managed);
        }
        catch (Exception exception)
        {
            failed++;
            Console.WriteLine("FAIL actual MoonSharp availability: " + exception.GetBaseException());
        }
        Console.WriteLine($"Native tests: {passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }

    private static void FixedEffectTests()
    {
        const string effect = "statmodifymanager.Player('money',5,'',0)";
        Test("fixed effects preserve literal keys, signs and source order", () =>
        {
            var effects = LuaGate.FixedEffects("statmodifymanager . Player ( \"life\", +7, \"\", 1 ); statmodifymanager.Player('poison-resistance',-3,'',0)");
            Equal(2, effects.Count); Equal("life", effects[0].Key); Equal(7, effects[0].Value);
            Equal("poison-resistance", effects[1].Key); Equal(-3, effects[1].Value);
        });
        Test("fixed effects ignore quoted text and escaped quotes", () =>
        {
            foreach (string literal in new[] { "local s=\"statmodifymanager.Player('money',5,'',0)\"", "local s='statmodifymanager.Player(\"money\",5,\"\",0)'", "local s='escaped \\' statmodifymanager.Player(\"money\",5,\"\",0)'", "local s=\"if for while repeat function return goto break\"" })
            {
                Equal(0, LuaGate.FixedEffects(literal).Count);
                Equal(1, LuaGate.FixedEffects(literal + "\n" + effect).Count);
            }
        });
        Test("fixed effects ignore line and every long bracket comment or string", () =>
        {
            foreach (string newline in new[] { "\n", "\r", "\r\n" })
            {
                Equal(0, LuaGate.FixedEffects("-- " + effect).Count);
                Equal(1, LuaGate.FixedEffects("-- " + effect + newline + effect).Count);
            }
            for (int level = 0; level <= 5; level++)
            {
                string eq = new('=', level);
                string bracket = "[" + eq + "[ if function " + effect + "]" + eq + "]";
                Equal(0, LuaGate.FixedEffects("local s=" + bracket).Count);
                Equal(1, LuaGate.FixedEffects("local s=" + bracket + "\n" + effect).Count);
                Equal(0, LuaGate.FixedEffects("--" + bracket).Count);
                Equal(1, LuaGate.FixedEffects("--" + bracket + "\n" + effect).Count);
            }
        });
        Test("fixed effects stop before every conditional or flow-control keyword", () =>
        {
            foreach (string control in new[] { "if true then", "for i=1,2 do", "while true do", "repeat", "function f()", "return", "goto target", "break" })
            {
                Equal(0, LuaGate.FixedEffects(control + "\n" + effect).Count);
                var effects = LuaGate.FixedEffects("statmodifymanager.Player('life',1,'',0)\n" + control + "\n" + effect);
                Equal(1, effects.Count); Equal("life", effects[0].Key);
            }
            Equal(1, LuaGate.FixedEffects("local gift_if=1; local returnvalue=2; " + effect).Count);
        });
        Test("fixed effects ignore mode, dynamic, escaped or overflowing arguments", () =>
        {
            foreach (string source in new[] { "statmodifymanager.Player('money',5,'=',0)", "statmodifymanager.Player('money',5,'add',0)", "statmodifymanager.Player('money',n,'',0)", "statmodifymanager.Player(key,5,'',0)", "statmodifymanager.Player('money',5,'',display)", "statmodifymanager.Player('money',2147483648,'',0)", "statmodifymanager.Player('mo\\nney',5,'',0)", "statmodifymanager.Player('money',5,'',0,1)", "statmodifymanager:Player('money',5,'',0)" })
                Equal(0, LuaGate.FixedEffects(source).Count);
        });
        Test("fixed effects honor identifier and whitespace or comment member boundaries", () =>
        {
            foreach (string prefix in new[] { "x", "_", "中", "obj.", "obj:", "obj . ", "obj : ", "obj .\n", "obj : --[=[member]=]\n", "obj . --member\n" })
                Equal(0, LuaGate.FixedEffects(prefix + effect).Count);
        });
        Test("fixed effects do not claim skipped short-circuit calls", () =>
        {
            Equal(0, LuaGate.FixedEffects("local x=false and " + effect).Count);
            Equal(0, LuaGate.FixedEffects("local x=true or " + effect).Count);
        });
    }

    private static void LexicalTests()
    {
        Test("static dot call: quotes and whitespace", () =>
        {
            foreach (string source in new[] { "checkpointmanager.Position(\"A\")", "return checkpointmanager . Position ( 'A_19' )", "checkpointmanager\n.\nPosition\n(\n\"A\"\n)" })
            {
                string result = LuaGate.Instrument(source, 37);
                Equal(1, Count(result, Marker));
                Require(result.Contains(source.TrimStart().StartsWith("return ") ? source[7..] : source, StringComparison.Ordinal), "Original call missing");
                Require(result.Contains(",37)", StringComparison.Ordinal), "Token missing");
            }
        });
        Test("short strings and escaped quotes remain exact", () =>
        {
            string[] sources =
            {
                "local s = 'checkpointmanager.Position(\"A\")'",
                "local s = \"checkpointmanager.Position('A')\"",
                "local s = 'escaped \\' checkpointmanager.Position(\"A\")'",
                "local s = \"escaped \\\" checkpointmanager.Position('A')\"",
                "local s = 'backslash \\\\ checkpointmanager.Position(\"A\")'",
            };
            foreach (string source in sources) Equal(source, LuaGate.Instrument(source, 2));
        });
        Test("long strings: all bracket levels and mismatched closers", () =>
        {
            for (int level = 0; level <= 5; level++)
            {
                string eq = new('=', level);
                string source = "local s = [" + eq + "[ checkpointmanager.Position(\"A\") " + (level > 0 ? "]] ignored " : "") + "]" + eq + "]";
                Equal(source, LuaGate.Instrument(source, 2));
                string plus = source + "\nreturn checkpointmanager.Position('B')";
                Equal(1, Count(LuaGate.Instrument(plus, 2), Marker));
            }
        });
        Test("line comments and CRLF preserve next active call", () =>
        {
            foreach (string newline in new[] { "\n", "\r", "\r\n" })
            {
                string comment = "-- checkpointmanager.Position(\"A\")";
                Equal(comment, LuaGate.Instrument(comment, 2));
                string source = comment + newline + "checkpointmanager.Position(\"B\")";
                string result = LuaGate.Instrument(source, 2);
                Require(result.Contains(comment + newline, StringComparison.Ordinal), "Comment changed");
                Equal(1, Count(result, Marker));
            }
        });
        Test("long comments: all bracket levels", () =>
        {
            for (int level = 0; level <= 5; level++)
            {
                string eq = new('=', level);
                string comment = "--[" + eq + "[ checkpointmanager.Position(\"A\") ]" + eq + "]";
                Equal(comment, LuaGate.Instrument(comment, 2));
                Equal(1, Count(LuaGate.Instrument(comment + "\ncheckpointmanager.Position('B')", 2), Marker));
            }
        });
        Test("unterminated literals and comments are not rewritten", () =>
        {
            foreach (string source in new[] { "'checkpointmanager.Position(\"A\")", "[==[ checkpointmanager.Position(\"A\")", "--[=[ checkpointmanager.Position('A')" })
                Equal(source, LuaGate.Instrument(source, 2));
        });
        Test("identifier boundaries and direct members", () =>
        {
            foreach (string source in new[] { "xcheckpointmanager.Position('A')", "_checkpointmanager.Position('A')", "1checkpointmanager.Position('A')", "中checkpointmanager.Position('A')", "obj.checkpointmanager.Position('A')", "obj:checkpointmanager.Position('A')", "checkpointmanager.PositionExtra('A')" })
                Equal(source, LuaGate.Instrument(source, 2));
        });
        Test("whitespace-separated member access stays unchanged", () =>
        {
            foreach (string source in new[] { "obj . checkpointmanager.Position('A')", "obj : checkpointmanager.Position('A')", "obj .\n checkpointmanager.Position('A')", "obj :\tcheckpointmanager.Position('A')", "obj . -- member comment\n checkpointmanager.Position('A')", "obj : --[=[member comment]=] checkpointmanager.Position('A')" })
                Equal(source, LuaGate.Instrument(source, 2));
        });
        Test("multiple static calls are each retained once", () =>
        {
            string source = "local a=checkpointmanager.Position('A'); local b=checkpointmanager.Position(\"B\"); return a,b";
            string result = LuaGate.Instrument(source, 9);
            Equal(2, Count(result, Marker));
            Equal(1, Count(result, "checkpointmanager.Position('A')"));
            Equal(1, Count(result, "checkpointmanager.Position(\"B\")"));
        });
        Test("dynamic or unsupported calls stay byte-for-byte unchanged", () =>
        {
            foreach (string source in new[] { "checkpointmanager.Position(id)", "checkpointmanager.Position('A' .. suffix)", "checkpointmanager.Position(makeId())", "checkpointmanager.Position([[A]])", "checkpointmanager.Position('A\\nB')", "checkpointmanager:Position('A')", "checkpointmanager['Position']('A')", "checkpointmanager.Position('A', 1)" })
                Equal(source, LuaGate.Instrument(source, 2));
        });
        Test("repeated instrumentation is idempotent", () =>
        {
            string once = LuaGate.Instrument("return checkpointmanager.Position('A')", 17);
            string twice = LuaGate.Instrument(once, 18);
            Equal(once, twice);
        });
        Test("marker-like content inside ordinary literals does not skip instrumentation", () =>
        {
            string source = "local s='__zhaohuo_instrumented'; -- ordinary __zhaohuo_instrumented comment\nreturn checkpointmanager.Position('A')";
            Equal(1, Count(LuaGate.Instrument(source, 17), Marker));
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InterpreterTests()
    {
        Console.WriteLine("Actual Lua interpreter assembly: " + typeof(Script).Assembly.FullName);
        Test("actual MoonSharp main thread never polls or yields", () =>
        {
            var script = NewScript();
            string source = LuaGate.Instrument("return checkpointmanager.Position('A')", 51);
            DynValue result = script.DoString(source);
            Equal("hit:A", result.String);
            Equal(1d, script.Globals.Get("calls").Number);
            Equal(0d, script.Globals.Get("polls").Number);
        });
        Test("actual MoonSharp coroutine waits, releases, executes once", () =>
        {
            var script = NewScript();
            string source = LuaGate.Instrument("return checkpointmanager.Position('A')", 52);
            script.DoString("co=coroutine.create(function() " + source + " end)");
            script.DoString("assert(coroutine.resume(co))");
            Equal("suspended", script.DoString("return coroutine.status(co)").String);
            Equal(0d, script.Globals.Get("calls").Number);
            Equal(1d, script.Globals.Get("polls").Number);
            script.DoString("assert(coroutine.resume(co))");
            Equal(0d, script.Globals.Get("calls").Number);
            Equal(2d, script.Globals.Get("polls").Number);
            DynValue result = script.DoString("gate=false; local ok,value=coroutine.resume(co); assert(ok); return value");
            Equal("hit:A", result.String);
            Equal("dead", script.DoString("return coroutine.status(co)").String);
            Equal(1d, script.Globals.Get("calls").Number);
            Equal(3d, script.Globals.Get("polls").Number);
            script.DoString("local ok=coroutine.resume(co); assert(not ok)");
            Equal(1d, script.Globals.Get("calls").Number);
        });
        Test("actual MoonSharp multiple expressions preserve result and call count", () =>
        {
            var script = NewScript();
            string source = LuaGate.Instrument("local a=checkpointmanager.Position('A'); local b=checkpointmanager.Position('B'); return a,b", 53);
            script.DoString("gate=false; co=coroutine.create(function() " + source + " end)");
            DynValue result = script.DoString("local ok,a,b=coroutine.resume(co); assert(ok); return a,b");
            Equal("hit:A", result.Tuple[0].String); Equal("hit:B", result.Tuple[1].String);
            Equal(2d, script.Globals.Get("calls").Number);
            Equal(2d, script.Globals.Get("polls").Number);
        });
        Test("actual MoonSharp original expression keeps multiple return values", () =>
        {
            var script = NewScript();
            script.DoString("checkpointmanager.Position=function(id) calls=calls+1; return 7,9 end");
            DynValue result = script.DoString(LuaGate.Instrument("return checkpointmanager.Position('A')", 54));
            Equal(7d, result.Tuple[0].Number); Equal(9d, result.Tuple[1].Number);
            Equal(1d, script.Globals.Get("calls").Number);
        });
    }

    private static Script NewScript()
    {
        var script = new Script(CoreModules.Preset_Default);
        script.DoString("calls=0; polls=0; gate=true; checkpointmanager={Position=function(id) calls=calls+1; return 'hit:'..id end}; __zhaohuo_wait_position=function(id,token) polls=polls+1; return gate end");
        return script;
    }

    private static void Test(string name, Action action)
    {
        try { action(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception exception) { failed++; Console.WriteLine("FAIL " + name + ": " + exception.GetBaseException().Message); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}; actual {actual}"); }
    private static int Count(string text, string value)
    {
        int count = 0, offset = 0;
        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0) { count++; offset += value.Length; }
        return count;
    }
}
