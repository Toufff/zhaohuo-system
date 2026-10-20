using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using Mortal.Core;
using Mortal.Story;
using UnityEngine;

namespace ZhaoHuoSystem;

/// <summary>
/// 决定它的选项 (F101): the options that set a story flag. Which scripts set it, and which scripts go on to each script, are
/// read from all the story scripts' text once a session, a slice a frame; the options themselves come from the analysis the
/// option hints use. A flag the script sets as it starts is put down to the options that lead into it. Read only.
/// </summary>
internal sealed class FlagSources
{
    internal sealed class Source
    {
        /// <summary>Where the player chooses (its kept start is what 跳转 writes), and the option's key and text.</summary>
        public string Script = "", Option = "", Text = "";
        /// <summary>What it does to the flag: set to Value, or add Value.</summary>
        public int Value;
        public bool Set;
        /// <summary>The script the option leads into, which sets the flag as it starts; "" when the option sets it itself.</summary>
        public string Via = "";
        /// <summary>The script sets it as it starts and no option is known to lead there.</summary>
        public bool NoChoice;
    }

    private static readonly Regex FlagSet = new("statmodifymanager\\.(?:SetFlag|AddFlag)\\(\"(\\w+)\"");
    private static readonly Regex NextScript = new("luamanager\\s*\\.\\s*(?:SetNextScript|SetCurrentStoryScript)\\s*\\(\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
    private Dictionary<string, List<string>>? setters, parents;
    public bool Building { get; private set; }
    public bool Ready => setters != null;
    public float Progress { get; private set; }

    public IEnumerator Build(ManualLogSource log)
    {
        if (Building || Ready) yield break;
        Building = true; Progress = 0;
        var watch = Stopwatch.StartNew();
        var setBy = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var from = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        TextAsset[] assets;
        try
        {
            string prefix = LuaManager.Instance != null ? R.Field<string>(LuaManager.Instance, "_scriptPrefix", "Story") ?? "Story" : "Story";
            assets = Resources.LoadAll<TextAsset>(prefix + "/ChineseTraditional");
        }
        catch (Exception ex) { log.LogWarning("FLAG_SOURCES " + ex.GetBaseException().Message); Building = false; yield break; }
        for (int i = 0; i < assets.Length; i++)
        {
            try
            {
                var asset = assets[i];
                if (asset == null) continue;
                string name = asset.name.ToLowerInvariant(), text = asset.text;
                foreach (Match match in FlagSet.Matches(text)) Add(setBy, match.Groups[1].Value, name);
                foreach (Match match in NextScript.Matches(text)) Add(from, match.Groups[1].Value, name);
                Resources.UnloadAsset(asset);
            }
            catch (Exception ex) { log.LogWarning("FLAG_SOURCES " + ex.GetBaseException().Message); }
            Progress = (i + 1f) / assets.Length;
            if (i % 40 == 39) yield return null;
        }
        setters = setBy; parents = from; Building = false;
        log.LogInfo($"FLAG_SOURCES scripts={assets.Length} flags={setBy.Count} {watch.ElapsedMilliseconds}ms");
    }
    private static void Add(Dictionary<string, List<string>> map, string key, string script)
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = new List<string>();
        if (!list.Contains(script)) list.Add(script);
    }

    public List<Source> Find(string flag, Func<string, Dictionary<string, OptionPreview>?> map, Func<string, string> optionText)
    {
        var found = new List<Source>();
        if (setters == null || parents == null || !setters.TryGetValue(flag, out var scripts)) return found;
        void Note(Source source)
        {
            if (!found.Exists(known => known.Script == source.Script && known.Option == source.Option && known.Value == source.Value && known.Set == source.Set && known.Via == source.Via)) found.Add(source);
        }
        foreach (string script in scripts)
        {
            if (map(script) is not { } options) continue;
            foreach (var pair in options)
                foreach (var effect in Effects(pair.Value, flag))
                {
                    bool set = effect.Kind == "set-flag";
                    if (pair.Key != LuaOptions.ScriptLevel) { Note(new Source { Script = script, Option = pair.Key, Text = optionText(pair.Key), Value = effect.Value, Set = set }); continue; }
                    // Set as the script starts: the options leading into it decide, through scripts that go on without a choice.
                    bool led = false;
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { script };
                    void Lead(string target, int depth)
                    {
                        if (!parents.TryGetValue(target, out var callers)) return;
                        foreach (string caller in callers)
                            if (map(caller) is { } up)
                                foreach (var option in up)
                                {
                                    if (!string.Equals(option.Value.NextScript, target, StringComparison.OrdinalIgnoreCase)) continue;
                                    if (option.Key != LuaOptions.ScriptLevel) { led = true; Note(new Source { Script = caller, Option = option.Key, Text = optionText(option.Key), Value = effect.Value, Set = set, Via = script }); }
                                    else if (depth < 6 && seen.Add(caller)) Lead(caller, depth + 1);
                                }
                    }
                    Lead(script, 0);
                    if (!led) Note(new Source { Script = script, Value = effect.Value, Set = set, NoChoice = true });
                }
        }
        return found;
    }
    // On the option itself and on its switch arms.
    private static IEnumerable<OptionEffect> Effects(OptionPreview preview, string flag) =>
        preview.Effects.Concat(preview.Cases.SelectMany(arm => arm.Effects)).Where(effect => (effect.Kind == "flag" || effect.Kind == "set-flag") && effect.Key == flag);
}
