using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Mortal.Core;

namespace ZhaoHuoSystem;

/// <summary>
/// Presentation only. Refreshing the locale never changes game state, stable IDs,
/// pending selections, value drafts or the adapter's gameplay revision.
/// </summary>
public static class L
{
    private static FieldInfo? leanTranslations;
    private static FieldInfo? leanPrimary;
    private static readonly Dictionary<string, string> gameTerms = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> displayTexts = new(StringComparer.Ordinal);
    public static long Revision { get; private set; }
    /// <summary>The panel's language: the player's choice in the title bar, else the game's (F119).</summary>
    public static string LanguageId { get; private set; } = LocalizationCatalog.Simplified;
    /// <summary>The game's own language (简中 / 繁中 / 韩语).</summary>
    public static string GameLanguageId { get; private set; } = LocalizationCatalog.Simplified;
    /// <summary>The player's choice in the title bar; "" follows the game.</summary>
    public static string Override { get; private set; } = "";
    /// <summary>
    /// The community Japanese mod is installed: it turns the game's 简体中文 into Japanese through the game's own text
    /// tables, so following the game then means Japanese, and the game's terms read through them are Japanese too.
    /// </summary>
    public static bool JapaneseMod { get; set; }
    /// <summary>
    /// The community English patch (velvetcakes1/LoM_en: XUnity.AutoTranslator with its own translation file) is installed:
    /// the game still runs in 简体中文 underneath, so following the game then means English. The game's own terms stay
    /// Chinese in its tables; the patch translates them on screen.
    /// </summary>
    public static bool EnglishPatch { get; set; }
    /// <summary>The game's own terms (names, options, stats) read through its tables are in the panel's language.</summary>
    public static bool GameSpeaksPanelLanguage => LanguageId == GameLanguageId
        || LanguageId == LocalizationCatalog.Japanese && JapaneseMod && GameLanguageId == LocalizationCatalog.Simplified;
    public static string[] FontNames => LanguageId == LocalizationCatalog.Korean
        ? new[] { "Malgun Gothic", "맑은 고딕", "Noto Sans CJK KR", "Microsoft YaHei", "Microsoft JhengHei", "Arial" }
        : LanguageId == LocalizationCatalog.Traditional
            ? new[] { "Microsoft JhengHei", "Microsoft YaHei", "Noto Sans CJK TC", "Malgun Gothic", "Arial" }
            : LanguageId == LocalizationCatalog.Japanese
                ? new[] { "Yu Gothic UI", "Meiryo UI", "Meiryo", "MS UI Gothic", "Microsoft YaHei", "Arial" }
                : LanguageId == LocalizationCatalog.English
                    ? new[] { "Segoe UI", "Arial", "Microsoft YaHei", "Microsoft JhengHei", "Malgun Gothic" }
                    : new[] { "Microsoft YaHei", "Microsoft JhengHei", "Noto Sans CJK SC", "Malgun Gothic", "Arial" };

    public static bool RefreshLanguage()
    {
        string? current;
        try { current = LocalizationManager.Instance?.LocaleResolver?.GetCurrentLang(); }
        catch { return false; }
        // The resolver can be unavailable during startup. Retain the last known locale.
        if (string.IsNullOrWhiteSpace(current)) return false;
        GameLanguageId = LocalizationCatalog.OfficialLanguage(current);
        return Apply();
    }
    /// <summary>The title bar's choice; "" (or anything unknown) follows the game.</summary>
    public static bool SetOverride(string? language)
    {
        Override = LocalizationCatalog.Supported(language) ? language! : "";
        return Apply();
    }
    /// <summary>What following the game means: its own language, or Japanese / English through a community translation.</summary>
    public static string FollowId => GameLanguageId != LocalizationCatalog.Simplified ? GameLanguageId
        : JapaneseMod ? LocalizationCatalog.Japanese : EnglishPatch ? LocalizationCatalog.English : GameLanguageId;
    private static bool Apply()
    {
        string next = Override.Length > 0 ? Override : FollowId;
        if (next == LanguageId) return false;
        LanguageId = next;
        Revision++;
        gameTerms.Clear();
        displayTexts.Clear();
        return true;
    }

    // IMGUI wraps only at spaces when a line has one, so a space beside Chinese or Japanese text (「改回 100 后」) pushed the
    // rest of a note to the next line early; there those spaces become no-break ones and the line wraps by character (F119).
    private static readonly Regex CjkSpace = new("(?<=[\u3000-\u30ff\u4e00-\u9fff\uff00-\uffef]) | (?=[\u3000-\u30ff\u4e00-\u9fff\uff00-\uffef])");
    public static string NoBreak(string text)
        => text.IndexOf(' ') >= 0 && (LanguageId == LocalizationCatalog.Simplified || LanguageId == LocalizationCatalog.Traditional || LanguageId == LocalizationCatalog.Japanese)
            ? CjkSpace.Replace(text, "\u00a0") : text;
    public static string Text(string sourceChinese) => LocalizationCatalog.Get(LanguageId, sourceChinese);
    public static string Format(string sourceTemplate, params object[] args) => string.Format(CultureInfo.InvariantCulture, Text(sourceTemplate), args);

    /// <summary>Display-only fallback for native human copy; never pass IDs, keys or script text.</summary>
    public static string DisplayText(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (LocalizationCatalog.TryDisplay(text, LanguageId, out var known)) return known;
        // English, and Japanese without the community mod, show the game's text in the game's own script; Korean and the
        // Japanese mod keep it as it is, since a 简繁 conversion would only spoil Japanese kanji (F119).
        string script = LanguageId == LocalizationCatalog.Simplified || LanguageId == LocalizationCatalog.Traditional ? LanguageId
            : LanguageId == LocalizationCatalog.English || LanguageId == LocalizationCatalog.Japanese && !GameSpeaksPanelLanguage ? GameLanguageId : "";
        if (script != LocalizationCatalog.Simplified && script != LocalizationCatalog.Traditional || Environment.OSVersion.Platform != PlatformID.Win32NT) return text;
        if (displayTexts.TryGetValue(text, out var cached)) return cached;
        try
        {
            var buffer = new StringBuilder(text.Length * 2 + 2);
            uint flags = script == LocalizationCatalog.Traditional ? 0x04000000u : 0x02000000u;
            // Include the null terminator: LCMapStringEx otherwise need not terminate
            // the destination, while StringBuilder marshalling requires termination.
            if (LCMapStringEx(script == LocalizationCatalog.Traditional ? "zh-TW" : "zh-CN", flags, text, -1, buffer, buffer.Capacity, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) > 0)
            {
                // Native notes and descriptions repeat in periodic snapshots. Keep the
                // cache bounded and clear it with the locale, never with gameplay state.
                if (displayTexts.Count >= 4096) displayTexts.Clear();
                return displayTexts[text] = buffer.ToString();
            }
        }
        catch { }
        return text;
    }

    private static string searchKey = "";
    private static string[] searchForms = new[] { "" };
    /// <summary>
    /// A search word as typed, in 简体 and in 繁体 (F116): the names follow the game's language and a few IDs keep the
    /// game's 繁体, while the player may type either.
    /// </summary>
    public static string[] SearchForms(string text)
    {
        text ??= "";
        if (text == searchKey) return searchForms;
        var forms = new List<string> { text };
        if (text.Length > 0 && Environment.OSVersion.Platform == PlatformID.Win32NT)
            foreach (var (locale, flags) in new[] { ("zh-CN", 0x02000000u), ("zh-TW", 0x04000000u) })
            {
                try
                {
                    var buffer = new StringBuilder(text.Length * 2 + 2);
                    if (LCMapStringEx(locale, flags, text, -1, buffer, buffer.Capacity, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) > 0 && !forms.Contains(buffer.ToString())) forms.Add(buffer.ToString());
                }
                catch { }
            }
        searchKey = text; searchForms = forms.ToArray();
        return searchForms;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int LCMapStringEx(string localeName, uint flags, string source, int sourceLength, StringBuilder destination, int destinationLength, IntPtr version, IntPtr reserved, IntPtr sortHandle);

    /// <summary>Read an official term. A default-language Lean fallback is not a target-language translation.</summary>
    public static string GameText(string key, string fallback)
    {
        // The game has no English, nor Japanese without the community mod: the panel's own word comes first then (F119).
        if (!GameSpeaksPanelLanguage && LocalizationCatalog.Has(LanguageId, fallback)) return Text(fallback);
        if (!string.IsNullOrWhiteSpace(key))
        {
            if (gameTerms.TryGetValue(key, out var cached)) return cached;
            try
            {
                string? value = LocalizationManager.Instance?.LocaleResolver?.GetString(key);
                if (!string.IsNullOrWhiteSpace(value) && value != key && HasCurrentLanguageTranslation(key) != false)
                {
                    string clean = Regex.Replace(value, "<[^>]+>", "");
                    if (!string.IsNullOrWhiteSpace(clean)) return gameTerms[key] = clean;
                }
            }
            catch { /* An unavailable locale table must not prevent the tool from opening. */ }
        }
        return Text(fallback);
    }

    // Lean is already installed with the game. Reflection avoids a new assembly dependency.
    // LeanPhrase.Compile sets Primary only for primaryLanguage; secondary-language data has false.
    // null means a resolver other than the verified Lean implementation is in use.
    private static bool? HasCurrentLanguageTranslation(string key)
    {
        try
        {
            if (leanTranslations == null)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var lean = assembly.GetType("Lean.Localization.LeanLocalization", false);
                    if (lean == null) continue;
                    leanTranslations = lean.GetField("CurrentTranslations", BindingFlags.Public | BindingFlags.Static);
                    break;
                }
            }
            if (leanTranslations?.GetValue(null) is not IDictionary table) return null;
            object? translation = table[key];
            if (translation == null) return false;
            leanPrimary ??= translation.GetType().GetField("Primary");
            if (leanPrimary?.GetValue(translation) is bool primary) return primary;
        }
        catch { }
        return null;
    }

    public static string StatName(GameStat stat)
    {
        if (stat == null) return Text("未核验属性");
        string key = stat.GetLocaleKey();
        string suffix = key.Substring(key.LastIndexOf('/') + 1);
        string fallback = StatFallbacks.TryGetValue(suffix, out var name) ? name : stat.StatType.ToString();
        return GameText(key, fallback);
    }

    public static string RelationName(RelationshipStatType type)
    {
        string id = type.ToString();
        if (!Relations.TryGetValue(id, out var relation)) return id;
        // Named-character fallbacks retain the verified Chinese name when an official
        // translation is absent; inventing Korean proper-name spellings would hide gaps.
        return GameText("Character/" + relation.Key, relation.Value);
    }

    public static Dictionary<int, string> EnumValues(string statLocaleKey)
    {
        var result = new Dictionary<int, string>();
        foreach (int value in NativeEnumSemantics.Values(statLocaleKey)) result[value] = EnumValueName(statLocaleKey, value);
        return result;
    }
    public static string EnumValueName(string statLocaleKey, int value) => NativeEnumSemantics.TryLocaleKey(statLocaleKey, value, out var localeKey)
        ? GameText(localeKey, "名称资源不可用") : Text("未收录");

    public static string FlagName(FlagData flag)
    {
        if (flag == null) return Text("未核验标记");
        string fallback = flag.name;
        var info = FlagDictionary.GetInfo("flag:" + flag.name);
        if (info.Verified) fallback = Text(info.Name);
        else
        {
            // Native notes remain explicitly unverified in the adapter's Reason/detail.
            // Taking a short first line is presentation only, never a type or writeability inference.
            string[] lines = (flag.DevNote ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string note = lines.Length > 0 ? lines[0].Trim() : "";
            if (note.Length > 0 && note.Length <= 48 && !note.StartsWith("旗標暫存", StringComparison.Ordinal) && !note.StartsWith("旗标暂存", StringComparison.Ordinal))
                fallback = DisplayText(note);
        }
        return GameText(flag.GetNameKey(), fallback);
    }

    public static string GroupName(string stableId) => stableId == "library" ? GameText("System/Menu/LibraryAchievement", "风云史") : Text(stableId switch
    {
        "all" => "全部", "attributes" => "属性", "general" => "一般", "alignment" => "阴阳与善恶", "martial" => "武学",
        "hidden" => "隐藏", "other" => "其他", "relationships" => "人物关系", "factions" => "门派关系",
        "flags" => "标记", "talents" => "技能", "books" => "书籍", "items" => "物品", "development" => "开发", "sect" => "门派", "library" => "风云史",
        _ => stableId
    });

    private static readonly Dictionary<string, string> StatFallbacks = new(StringComparer.Ordinal)
    {
        ["life"] = "体力", ["stamina"] = "内力", ["dexterity"] = "轻功", ["money"] = "银两",
        ["charisma"] = "魅力", ["literacy"] = "学问", ["mental"] = "心相", ["fate"] = "命运",
        ["disposition"] = "性情", ["behaviour"] = "处世", ["training"] = "修养", ["karma"] = "道德",
        ["talking"] = "嘴力", ["faction"] = "门派规模", ["fame"] = "名声", ["people"] = "门派人数",
        ["team"] = "向心力", ["weapon"] = "锻造", ["poison"] = "炼丹", ["action"] = "行动次数",
        ["contribution"] = "贡献度", ["cook"] = "厨艺", ["poison-resistance"] = "抗毒", ["paralysis-resistance"] = "抗麻",
        ["title"] = "称号", ["lover"] = "心上人", ["add-action-1"] = "额外行动次数 1", ["girl"] = "娘化",
        ["internal"] = "阴阳内功", ["combat-defence"] = "防御", ["change-heart"] = "变心",
        ["martial-point"] = "武学", ["total-martial-point"] = "累计武学点数", ["assets"] = "门派资产",
        ["faction-contribution"] = "门派贡献", ["total-weapon-point"] = "累计锻造点数", ["total-poison-point"] = "累计毒药点数",
        ["total-contribution"] = "累计贡献度", ["add-action-2"] = "额外行动次数 2", ["add-action-3"] = "额外行动次数 3",
        ["m-sword"] = "刀剑", ["m-projectile"] = "暗器", ["m-fist"] = "拳掌", ["m-leg"] = "腿法",
        ["m-odd"] = "奇门", ["m-soft"] = "软兵器", ["m-stick"] = "枪棍", ["m-internal"] = "内功",
        ["confucianism"] = "儒学", ["taoism"] = "道学", ["buddhism"] = "释学",
        ["default-battle-hp"] = "战役预设血量", ["combat-attack-dice"] = "攻击暴击", ["combat-weapon-dice"] = "暗器暴击"
    };

    // Verified StringValue metadata + CharacterIntroPanel's Character/{key} lookup.
    private static readonly Dictionary<string, KeyValuePair<string, string>> Relations = new(StringComparer.Ordinal)
    {
        ["小師妹"] = Pair("sister1", "小师妹"), ["大師兄"] = Pair("brother1", "大师兄"),
        ["二師兄"] = Pair("brother2", "二师兄"), ["三師兄"] = Pair("brother3", "三师兄"),
        ["四師兄"] = Pair("brother4", "四师兄"), ["掌門人"] = Pair("master", "掌门人"),
        ["瑞杏"] = Pair("girl1", "瑞杏"), ["葉雲舟"] = Pair("special3", "叶云舟"),
        ["葉雲裳"] = Pair("girl2", "叶云裳"), ["樊嘯天"] = Pair("special4", "樊啸天"),
        ["萬里鵬程"] = Pair("special6", "万里鹏程"), ["劉顎"] = Pair("special1", "刘颚"),
        ["龍湘"] = Pair("girl4", "龙湘"), ["龍淵"] = Pair("special2", "龙渊"),
        ["石公遠"] = Pair("special7", "石公远"), ["南宮深"] = Pair("special102", "南宫深"),
        ["南宮淺"] = Pair("special103", "南宫浅"), ["尹志平"] = Pair("special205", "尹志平"),
        ["福韞"] = Pair("special206", "福韫"), ["孟珙"] = Pair("special215_1", "孟珙"),
        ["虞小梅"] = Pair("girl5", "虞小梅"), ["夏侯蘭"] = Pair("girl6", "夏侯兰"),
        ["郁竹"] = Pair("girl7", "郁竹"), ["魏菊"] = Pair("girl8", "魏菊"),
        ["上官螢"] = Pair("girl9", "上官萤"), ["王二壯"] = Pair("special401", "王二壮"),
        ["趙逵"] = Pair("special403", "赵逵"), ["丹霞子"] = Pair("special404", "丹霞子"),
        ["申屠龍"] = Pair("special405", "申屠龙"), ["車軒轅"] = Pair("special409", "车轩辕"),
        ["主角"] = Pair("player", "主角"), ["虞小梅_探險隊長"] = Pair("girl5_11", "虞小梅"),
        ["宋悲"] = Pair("special800", "宋悲"), ["鶴手"] = Pair("special805_0", "鹤手"),
        ["解無塵"] = Pair("special808", "解无尘"), ["李富貴"] = Pair("special809", "李富贵"),
        ["夏靈犀"] = Pair("special825", "夏灵犀"), ["孟仙謠"] = Pair("special831", "孟仙谣"),
        ["瑞笙"] = Pair("special999", "瑞笙"), ["葉雲啾_1"] = Pair("girl2_3_1", "叶云啾"),
        ["葉雲啾_2"] = Pair("girl2_4", "叶云啾")
    };
    private static KeyValuePair<string, string> Pair(string key, string value) => new(key, value);
}
