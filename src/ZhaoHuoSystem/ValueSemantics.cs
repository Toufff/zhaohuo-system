using System;

namespace ZhaoHuoSystem;

// Pure arithmetic shared by snapshot ranges, target editing and non-game tests.
internal static class ValueSemantics
{
    public static bool WriteAndVerify(int target, Action<int> write, Func<int> read, out int actual)
    {
        write(target);
        actual = read();
        return actual == target;
    }
    // The native initial attribute allocation page is the player-facing grouping.
    // Keys and order were verified from PlayerBonusStatData -> GameStat bindings.
    private static readonly string[] AttributeKeys =
    {
        "PlayerStat/life", "PlayerStat/stamina", "PlayerStat/dexterity", "PlayerStat/m-sword",
        "PlayerStat/m-fist", "PlayerStat/m-projectile", "PlayerStat/talking", "PlayerStat/literacy",
        "PlayerStat/poison-resistance", "PlayerStat/paralysis-resistance", "PlayerStat/disposition", "PlayerStat/behaviour",
        "PlayerStat/karma", "PlayerStat/training", "PlayerStat/money", "PlayerStat/martial-point",
        "PlayerStat/poison", "PlayerStat/weapon",
        // Additional scalar fields directly bound by the native PropertyStatPanel.
        "PlayerStat/mental", "PlayerStat/internal", "PlayerStat/fate",
        // The user explicitly grouped these three from the native page screenshot.
        "PlayerStat/buddhism", "PlayerStat/confucianism", "PlayerStat/taoism"
    };
    // The native HomeStatPanel binds these five fields as one sect page.
    private static readonly string[] SectKeys =
    { "PlayerStat/faction", "PlayerStat/fame", "PlayerStat/people", "PlayerStat/team", "PlayerStat/assets" };
    private static readonly string[] RetainedMartialKeys =
    { "PlayerStat/m-internal", "PlayerStat/m-leg", "PlayerStat/m-odd", "PlayerStat/m-soft", "PlayerStat/m-stick" };
    public static bool IsRetainedMartial(string localeKey) => Array.IndexOf(RetainedMartialKeys, localeKey) >= 0;
    public static int AttributeOrder(string localeKey)
    {
        int index = Array.IndexOf(AttributeKeys, localeKey);
        return index >= 0 ? index : int.MaxValue;
    }
    public static int CatalogOrder(string localeKey)
    {
        int attribute = AttributeOrder(localeKey);
        if (attribute != int.MaxValue) return attribute;
        int sect = Array.IndexOf(SectKeys, localeKey);
        return sect >= 0 ? AttributeKeys.Length + sect : int.MaxValue;
    }
    public static int SafeCatalogOrder(Func<string> readLocaleKey)
    {
        try { return CatalogOrder(readLocaleKey()); }
        catch { return int.MaxValue; } // Preserve per-field failure isolation before enumeration.
    }
    public static string GroupId(string localeKey, int property) => AttributeOrder(localeKey) != int.MaxValue ? "attributes" : Array.IndexOf(SectKeys, localeKey) >= 0 ? "sect" : IsRetainedMartial(localeKey) ? "hidden" : property switch
    { 0 => "general", 1 => "alignment", 2 => "alignment", 3 => "martial", 9 => "hidden", _ => "other" };

    public static bool TryRange(int baseMin, int baseMax, int addition, int finalMax, out int min, out int max)
    {
        min = max = 0;
        if (baseMin > baseMax || finalMax < baseMin) return false;
        // The native getter uses int addition. Overflow would invalidate a monotone interval.
        long low = (long)baseMin + addition, high = (long)baseMax + addition;
        if (low < int.MinValue || high > int.MaxValue) return false;
        min = Clamp(low, baseMin, finalMax); max = Clamp(high, baseMin, finalMax);
        return true;
    }

    public static bool TryBase(int target, int currentBase, int baseMin, int baseMax, int addition, int finalMax, out int result)
    {
        result = currentBase;
        if (!TryRange(baseMin, baseMax, addition, finalMax, out var min, out var max) || target < min || target > max) return false;
        if (Clamp((long)currentBase + addition, baseMin, finalMax) == target && currentBase >= baseMin && currentBase <= baseMax) return true;
        long inverse = (long)target - addition;
        result = (int)Math.Max(baseMin, Math.Min(baseMax, inverse));
        return Clamp((long)result + addition, baseMin, finalMax) == target;
    }

    public static int? Suggested(int right, string comparison) => comparison switch
    {
        ">" => right == int.MaxValue ? null : right + 1,
        "!=" => right == int.MaxValue ? right - 1 : right + 1,
        "<" => right == int.MinValue ? null : right - 1,
        _ => right
    };
    private static int Clamp(long value, int min, int max) => (int)Math.Max(min, Math.Min(max, value));
}
