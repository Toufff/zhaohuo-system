using System.Collections.Generic;
using System.Linq;

namespace ZhaoHuoSystem;

internal static class MapSemantics
{
    public static bool IsCurrentCandidate(string currentKey, string candidateKey, bool hasScript, bool inDateWindow) =>
        currentKey.Length > 0 && currentKey != "clear" && currentKey == candidateKey && hasScript && inDateWindow;
    public static bool Ready(bool nativeReady, IEnumerable<ConditionSnapshot> conditions) => nativeReady && conditions.All(x => x.Known && x.Satisfied);
    // Match the verified engine's date checks: 0 specific, 1 interval, 2 periodic (unrestricted).
    public static bool InDateWindow(int current, int kind, int min, int max) => kind switch
    { 0 => current == min, 1 => current >= min && current <= max, 2 => true, _ => false };
}
