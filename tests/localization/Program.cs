using System.Globalization;
using System.Text.RegularExpressions;
using Lean.Localization;
using Mortal.Core;
using ZhaoHuoSystem;

internal static class Program
{
    private static int passed;
    private static void Main(string[] args)
    {
        Test("catalogue has unique source keys and matching format arguments", () =>
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int row = 0; row < LocalizationCatalog.Rows.GetLength(0); row++)
            {
                string source = LocalizationCatalog.Rows[row, 0];
                Require(seen.Add(source), "Duplicate key " + source);
                var expected = Indices(source);
                for (int col = 0; col < 3; col++)
                {
                    string value = LocalizationCatalog.Rows[row, col];
                    Require(!string.IsNullOrWhiteSpace(value), "Missing translation " + source);
                    Equal(expected, Indices(value));
                    string.Format(CultureInfo.InvariantCulture, value, Enumerable.Range(0, 16).Cast<object>().ToArray());
                }
            }
        });
        Test("only three official locales are selected", () =>
        {
            foreach (string unsupported in new[] { "English", "Japanese", "ko", "zh-CN", "community" })
                Equal(LocalizationCatalog.Simplified, LocalizationCatalog.OfficialLanguage(unsupported));
            Equal(LocalizationCatalog.Traditional, LocalizationCatalog.OfficialLanguage("ChineseTraditional"));
            Equal(LocalizationCatalog.Korean, LocalizationCatalog.OfficialLanguage("Korean"));
        });
        Test("language revision changes only with language", () =>
        {
            long before = L.Revision;
            SetLanguage("ChineseTraditional"); Equal(before + 1, L.Revision);
            Require(!L.RefreshLanguage(), "Same locale should not change revision");
            Equal(before + 1, L.Revision);
            SetLanguage("Korean"); Equal(before + 2, L.Revision);
        });
        Test("three-language copy and formatting", () =>
        {
            SetLanguage("ChineseSimplified"); Equal("数值", L.Text("数值"));
            SetLanguage("ChineseTraditional"); Equal("數值", L.Text("数值"));
            SetLanguage("Korean"); Equal("수치", L.Text("数值"));
            Equal("이번 지정: RAW_ID", L.Format("本次指定：{0}", "RAW_ID"));
            Equal("맵_葉雲舟", L.Text("맵_葉雲舟"));
        });
        Test("official stat term uses locale key rather than internal name", () =>
        {
            SetLanguage("ChineseSimplified");
            Term("PlayerStat/mental", "心相", true);
            Equal("心相", L.StatName(new GameStat()));
            Equal("PlayerStat/mental", LocalizationManager.Instance!.LocaleResolver.LastKey);
            Term("PlayerStat/life", "<b>体力</b>", true);
            Equal("体力", L.StatName(new GameStat { Key = "life" }));
        });
        Test("default-language term fallback does not override canonical Simplified stat labels", () =>
        {
            Term("PlayerStat/m-sword", "武功刀劍", false);
            Equal("刀剑", L.StatName(new GameStat { Key = "m-sword" }));
            Equal("内力", L.StatName(new GameStat { Key = "stamina" }));
            Equal("抗麻", L.StatName(new GameStat { Key = "paralysis-resistance" }));
        });
        Test("all initial attribute names match the verified native page and official terms still win", () =>
        {
            SetLanguage("ChineseSimplified");
            var expected = new[]
            {
                ("life", "体力"), ("stamina", "内力"), ("dexterity", "轻功"), ("m-sword", "刀剑"),
                ("m-fist", "拳掌"), ("m-projectile", "暗器"), ("talking", "嘴力"), ("literacy", "学问"),
                ("poison-resistance", "抗毒"), ("paralysis-resistance", "抗麻"), ("disposition", "性情"), ("behaviour", "处世"),
                ("karma", "道德"), ("training", "修养"), ("money", "银两"), ("martial-point", "武学"), ("poison", "炼丹"), ("weapon", "锻造"),
                ("mental", "心相"), ("internal", "阴阳内功"), ("fate", "命运"),
                ("buddhism", "释学"), ("confucianism", "儒学"), ("taoism", "道学"), ("lover", "心上人")
            };
            foreach (var item in expected) Equal(item.Item2, L.StatName(new GameStat { Key = item.Item1 }));
            Term("PlayerStat/poison", "官方炼丹名称", true);
            Equal("官方炼丹名称", L.StatName(new GameStat { Key = "poison" }));
            Equal("属性", L.GroupName("attributes"));
            SetLanguage("ChineseTraditional"); Equal("屬性", L.GroupName("attributes"));
            Equal("門派", L.GroupName("sect")); Equal("風雲史", L.GroupName("library"));
            Equal("煉丹", L.Text("炼丹"));
            SetLanguage("Korean"); Equal("능력치", L.GroupName("attributes")); Equal("연단", L.Text("炼丹"));
            Equal("문파", L.GroupName("sect")); Equal("풍운사", L.GroupName("library"));
            SetLanguage("ChineseSimplified");
        });
        Test("enum hover reads official numeric terms and distinguishes missing labels from unlisted values", () =>
        {
            SetLanguage("ChineseSimplified");
            Term("PlayerInfo/Title/0", "唐门杂鱼", true); Term("PlayerInfo/Lover/0", "小师妹", true); Term("PlayerInfo/Lover/99", "自己", true);
            Equal("唐门杂鱼", L.EnumValueName("PlayerStat/title", 0)); Equal("小师妹", L.EnumValueName("PlayerStat/lover", 0));
            var lovers = L.EnumValues("PlayerStat/lover"); Equal(10, lovers.Count); Equal("自己", lovers[99]);
            Equal("名称资源不可用", lovers[8]); Equal("未收录", L.EnumValueName("PlayerStat/lover", 9));
            SetLanguage("Korean"); Term("PlayerInfo/Title/0", "당문졸개", true); Term("PlayerInfo/Lover/0", "소사매", true);
            Equal("당문졸개", L.EnumValueName("PlayerStat/title", 0)); Equal("소사매", L.EnumValues("PlayerStat/lover")[0]);
            Equal("이름 리소스 없음", L.EnumValueName("PlayerStat/title", 25)); Equal("미등록", L.EnumValueName("PlayerStat/lover", -1));
            Equal("불교", L.StatName(new GameStat { Key = "buddhism" })); Equal("유교", L.StatName(new GameStat { Key = "confucianism" }));
            Equal("각법", L.StatName(new GameStat { Key = "m-leg" })); Equal("창곤", L.StatName(new GameStat { Key = "m-stick" }));
            SetLanguage("ChineseSimplified");
        });
        Test("official relation uses verified Character term", () =>
        {
            Term("Character/special3", "官方人物名", true);
            Equal("官方人物名", L.RelationName(RelationshipStatType.葉雲舟));
            Equal("Character/special3", LocalizationManager.Instance!.LocaleResolver.LastKey);
            Equal("小师妹", L.RelationName(RelationshipStatType.小師妹));
            Equal("未知角色", L.RelationName(RelationshipStatType.未知角色));
        });
        Test("flag official, semantic, short-note and raw fallbacks remain separate", () =>
        {
            Term("Flag/Name/OFFICIAL", "官方标记", true);
            Equal("官方标记", L.FlagName(new FlagData { name = "OFFICIAL", DevNote = "unverified" }));
            Equal("青城派", L.FlagName(new FlagData { name = "H00001" }));
            Equal("贡献度", L.FlagName(new FlagData { name = "NOTE", DevNote = "贡献度\nprivate detail" }));
            Equal("RAW_FLAG", L.FlagName(new FlagData { DevNote = "旗標暫存1" }));
            Equal("RAW_FLAG", L.FlagName(new FlagData { DevNote = new string('長', 49) }));
        });
        Test("official terms are refreshed when language changes", () =>
        {
            SetLanguage("Korean"); Term("PlayerStat/mental", "심상", true);
            Equal("심상", L.StatName(new GameStat()));
            Require(L.FontNames.Contains("Malgun Gothic"), "Korean font is missing");
            Term("PlayerStat/mental", "updated", true);
            Equal("심상", L.StatName(new GameStat()));
            SetLanguage("ChineseTraditional"); Equal("updated", L.StatName(new GameStat()));
        });
        Test("unavailable resolver retains locale and read fallback", () =>
        {
            LocalizationManager.Instance!.LocaleResolver.Throw = true;
            long before = L.Revision;
            Require(!L.RefreshLanguage(), "Unavailable resolver must not reset locale");
            Equal(before, L.Revision); Equal("未核驗", L.GameText("missing", "未核验"));
            LocalizationManager.Instance.LocaleResolver.Throw = false;
        });
        Test("Chinese display conversion is explicit and does not affect Text identifiers", () =>
        {
            SetLanguage("ChineseSimplified");
            const string identifier = "flag:門派_ABC";
            Equal(identifier, L.Text(identifier));
            if (OperatingSystem.IsWindows())
                for (int i = 0; i < 64; i++) Equal("门派行动" + i, L.DisplayText("門派行動" + i));
            SetLanguage("ChineseTraditional");
            if (OperatingSystem.IsWindows()) Equal("門派行動", L.DisplayText("门派行动"));
            SetLanguage("Korean"); Equal("門派行動", L.DisplayText("門派行動"));
            Equal("대장간", L.DisplayText("鍛冶場"));
        });
        Test("all stable groups have three translated labels", () =>
        {
            foreach (string language in new[] { "ChineseSimplified", "ChineseTraditional", "Korean" })
            {
                SetLanguage(language);
                foreach (string id in new[] { "all", "attributes", "general", "alignment", "martial", "hidden", "other", "sect", "relationships", "factions", "flags", "talents", "books", "items", "development", "library" })
                    Require(L.GroupName(id) != id, "Untranslated group " + id);
            }
        });
        if (args.Length > 0) Test("literal Text and Format calls have complete catalogue coverage", () => Audit(args[0]));
        Console.WriteLine($"Localization tests: {passed} passed; {LocalizationCatalog.Rows.GetLength(0)} three-language resource rows");
    }

    private static void Audit(string root)
    {
        var missing = new List<string>();
        var pattern = new Regex("L\\.(?:Text|Format)\\(\\s*\"(?<key>(?:\\\\.|[^\"\\\\])*)\"");
        var statusPattern = new Regex("(?:SetStatus\\(|Feedback\\(\\s*(?:true|false)\\s*,)\\s*\"(?<key>(?:\\\\.|[^\"\\\\])*)\"");
        foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "src", "ZhaoHuoSystem"), "*.cs"))
        {
            string source = File.ReadAllText(path);
            foreach (Match match in pattern.Matches(source))
            {
                string key = Regex.Unescape(match.Groups["key"].Value);
                if (!LocalizationCatalog.Contains(key)) missing.Add(Path.GetFileName(path) + ": " + key);
            }
            if (Path.GetFileName(path) == "GameAdapter.cs")
                foreach (Match match in statusPattern.Matches(source))
                {
                    string key = Regex.Unescape(match.Groups["key"].Value);
                    if (!LocalizationCatalog.Contains(key)) missing.Add(Path.GetFileName(path) + ": " + key);
                }
            // UI helpers translate exact labels centrally, so their plain literals
            // must also be catalogued even when no L.Text appears at the call site.
            if (new[] { "ToolWindow.cs", "NativeDicePresenter.cs", "MapIndicators.cs" }.Contains(Path.GetFileName(path)))
            {
                foreach (Match literal in Regex.Matches(source, "\"(?<key>(?:\\\\.|[^\"\\\\])*)\""))
                {
                    string key = Regex.Unescape(literal.Groups["key"].Value);
                    if (Regex.IsMatch(key, @"[\u4e00-\u9fff]") && !LocalizationCatalog.Contains(key)) missing.Add(Path.GetFileName(path) + ": " + key);
                }
            }
        }
        Require(missing.Count == 0, string.Join(Environment.NewLine, missing.Distinct()));
    }
    private static string Indices(string text) => string.Join(",", Regex.Matches(text, @"\{(\d+)(?:[^}]*)\}").Select(x => x.Groups[1].Value).OrderBy(x => x));
    private static void Term(string key, string value, bool primary) => LeanLocalization.CurrentTranslations[key] = new() { Data = value, Primary = primary };
    private static void SetLanguage(string value) { LocalizationManager.Instance!.LocaleResolver.Language = value; L.RefreshLanguage(); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; actual {actual}"); }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
}
