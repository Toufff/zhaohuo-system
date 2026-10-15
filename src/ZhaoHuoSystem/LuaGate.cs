using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace ZhaoHuoSystem;

// A small lexer keeps code instrumentation out of quoted strings and all Lua comment/string forms.
internal static class LuaGate
{
    private const string Marker = "--[=[__zhaohuo_instrumented]=]\n";
    private static readonly Regex Call = new(@"\Gcheckpointmanager\s*\.\s*Position\s*\(\s*([""'])([A-Za-z0-9_]+)\1\s*\)");
    private static readonly Regex FixedPlayer = new(@"\Gstatmodifymanager\s*\.\s*Player\s*\(\s*([""'])([A-Za-z0-9_-]+)\1\s*,\s*([+-]?\d+)\s*,\s*([""'])\4\s*,\s*\d+\s*\)");
    private static readonly Regex Branch = new(@"\G(?:if|for|while|repeat|function|return|goto|break|and|or)\b");
    internal static List<KeyValuePair<string, int>> FixedEffects(string source)
    {
        var result = new List<KeyValuePair<string, int>>();
        var previousSignificant = '\0';
        for (var i = 0; i < source.Length;)
        {
            if (source[i] == '\'' || source[i] == '"')
            {
                var quote = source[i++]; while (i < source.Length) { var c = source[i++]; if (c == '\\' && i < source.Length) i++; else if (c == quote) break; }
                previousSignificant = 'v'; continue;
            }
            if (source[i] == '-' && i + 1 < source.Length && source[i + 1] == '-')
            {
                i += 2; if (!SkipLong(source, ref i)) while (i < source.Length && source[i] != '\n' && source[i] != '\r') i++; continue;
            }
            if (source[i] == '[' && SkipLong(source, ref i)) { previousSignificant = 'v'; continue; }
            var boundary = previousSignificant != '.' && previousSignificant != ':' && (i == 0 || !(char.IsLetterOrDigit(source[i - 1]) || source[i - 1] == '_'));
            if (boundary && Branch.IsMatch(source, i)) break;
            var match = boundary ? FixedPlayer.Match(source, i) : Match.Empty;
            if (match.Success && int.TryParse(match.Groups[3].Value, out var amount))
            { result.Add(new(match.Groups[2].Value, amount)); i += match.Length; previousSignificant = ')'; continue; }
            if (!char.IsWhiteSpace(source[i])) previousSignificant = source[i];
            i++;
        }
        return result;
    }
    public static string Instrument(string source, long token)
    {
        if (source.StartsWith(Marker, StringComparison.Ordinal)) return source;
        var output = new StringBuilder(source.Length + 256);
        var changed = false;
        var previousSignificant = '\0';
        for (var i = 0; i < source.Length;)
        {
            var start = i;
            if (source[i] == '\'' || source[i] == '"')
            {
                var quote = source[i++];
                while (i < source.Length) { var c = source[i++]; if (c == '\\' && i < source.Length) i++; else if (c == quote) break; }
                output.Append(source, start, i - start); previousSignificant = 'v'; continue;
            }
            if (source[i] == '-' && i + 1 < source.Length && source[i + 1] == '-')
            {
                i += 2;
                if (!SkipLong(source, ref i)) while (i < source.Length && source[i] != '\n' && source[i] != '\r') i++;
                output.Append(source, start, i - start); continue;
            }
            if (source[i] == '[' && SkipLong(source, ref i)) { output.Append(source, start, i - start); previousSignificant = 'v'; continue; }
            var boundary = previousSignificant != '.' && previousSignificant != ':' && (i == 0 || !(char.IsLetterOrDigit(source[i - 1]) || source[i - 1] == '_'));
            var match = boundary ? Call.Match(source, i) : Match.Empty;
            if (match.Success)
            {
                // The original call is retained exactly once. Main-thread/non-coroutine execution never yields.
                output.Append("(function() local _,__zh_main=coroutine.running(); if not __zh_main and __zhaohuo_wait_position then while __zhaohuo_wait_position(\"")
                    .Append(match.Groups[2].Value).Append("\",").Append(token)
                    .Append(") do coroutine.yield() end end return ").Append(match.Value).Append(" end)()");
                i += match.Length; changed = true; previousSignificant = ')'; continue;
            }
            if (!char.IsWhiteSpace(source[i])) previousSignificant = source[i];
            output.Append(source[i++]);
        }
        return (changed ? Marker : "") + output.ToString();
    }
    private static bool SkipLong(string source, ref int index)
    {
        var start = index;
        if (index >= source.Length || source[index] != '[') return false;
        var next = index + 1; while (next < source.Length && source[next] == '=') next++;
        if (next >= source.Length || source[next] != '[') return false;
        var close = "]" + new string('=', next - index - 1) + "]";
        var end = source.IndexOf(close, next + 1, StringComparison.Ordinal);
        index = end < 0 ? source.Length : end + close.Length;
        return index > start;
    }
}
