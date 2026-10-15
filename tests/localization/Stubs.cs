// Inert test doubles: this project never loads Unity/game assemblies or a game process.
namespace Mortal.Core
{
    public sealed class Resolver
    {
        public string Language = "ChineseSimplified";
        public bool Throw;
        public string LastKey = "";
        public string GetCurrentLang() => Throw ? throw new InvalidOperationException("unavailable") : Language;
        public string GetString(string key)
        {
            LastKey = key;
            if (Throw) throw new InvalidOperationException("unavailable");
            return Lean.Localization.LeanLocalization.CurrentTranslations.TryGetValue(key, out var entry) ? entry.Data : key;
        }
    }
    public sealed class LocalizationManager
    {
        public static LocalizationManager? Instance { get; set; } = new();
        public Resolver LocaleResolver { get; } = new();
    }
    public sealed class GameStat
    {
        public string Key = "mental";
        public string Name = "incorrect internal label";
        public string StatType = "心理衛生";
        public string GetLocaleKey() => "PlayerStat/" + Key;
    }
    public enum RelationshipStatType { 小師妹, 葉雲舟, 未知角色 }
    public sealed class FlagData
    {
        public string name = "RAW_FLAG";
        public string DevNote = "";
        public string GetNameKey() => "Flag/Name/" + name;
    }
}
namespace Lean.Localization
{
    public sealed class Translation
    {
        public string Data = "";
        public bool Primary;
    }
    public static class LeanLocalization
    {
        public static readonly Dictionary<string, Translation> CurrentTranslations = new(StringComparer.Ordinal);
    }
}
namespace ZhaoHuoSystem
{
    public sealed class FlagInfo
    {
        public bool Verified;
        public string Name = "";
    }
    public static class FlagDictionary
    {
        public static FlagInfo GetInfo(string id) => id == "flag:H00001"
            ? new() { Verified = true, Name = "青城派" } : new() { Name = id };
    }
}
