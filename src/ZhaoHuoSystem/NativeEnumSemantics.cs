using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ZhaoHuoSystem;

// Verified numeric term keys only; game character/title text remains in the game's locale resources.
internal static class NativeEnumSemantics
{
    private static readonly IReadOnlyList<int> Titles = Array.AsReadOnly(Enumerable.Range(0, 26).ToArray());
    private static readonly IReadOnlyList<int> Lovers = Array.AsReadOnly(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 99 });
    private static readonly IReadOnlyList<int> Empty = Array.AsReadOnly(Array.Empty<int>());

    public static IReadOnlyList<int> Values(string statLocaleKey) => statLocaleKey switch
    {
        "PlayerStat/title" => Titles,
        "PlayerStat/lover" => Lovers,
        _ => Empty
    };
    public static bool TryLocaleKey(string statLocaleKey, int value, out string localeKey)
    {
        localeKey = "";
        if (!Values(statLocaleKey).Contains(value)) return false;
        localeKey = (statLocaleKey == "PlayerStat/title" ? "PlayerInfo/Title/" : "PlayerInfo/Lover/") + value.ToString(CultureInfo.InvariantCulture);
        return true;
    }
}
