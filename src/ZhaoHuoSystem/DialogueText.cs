using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ZhaoHuoSystem;

/// <summary>
/// F123 对白文字大小: the story's say boxes draw their lines larger. The size is applied where a Text lays itself out
/// (its generation settings), never written into the component, so at 原版 nothing is touched and other mods that
/// change text sizes see the game's own values. The big centred narration is left as it is.
/// </summary>
internal static class DialogueText
{
    public static readonly int[] Sizes = { 100, 115, 130, 150 };
    private static int percent = 100;
    private static readonly Dictionary<int, bool> dialogue = new();

    /// <summary>The chosen size in percent; 100 is the game's own.</summary>
    public static int Percent
    {
        get => percent;
        set
        {
            int next = System.Array.IndexOf(Sizes, value) >= 0 ? value : 100;
            if (next == percent) return;
            percent = next;
            // The say boxes on screen lay their text out again at the new size.
            foreach (var say in Resources.FindObjectsOfTypeAll<Fungus.SayDialog>())
                if (say != null && say.gameObject.scene.IsValid() && R.Field(say, "storyText") is Text text && text != null) text.SetAllDirty();
        }
    }

    /// <summary>A say box's story line, other than the centred narration (whose box has no frame behind it).</summary>
    private static bool IsDialogue(Text text)
    {
        int id = text.GetInstanceID();
        if (dialogue.TryGetValue(id, out bool yes)) return yes;
        var say = text.GetComponentInParent<Fungus.SayDialog>();
        yes = say != null && ReferenceEquals(R.Field(say, "storyText"), text) && !say.name.Contains("Center");
        if (dialogue.Count > 512) dialogue.Clear();
        return dialogue[id] = yes;
    }

    [HarmonyPatch(typeof(Text), nameof(Text.GetGenerationSettings))]
    private static class Layout
    {
        private static void Postfix(Text __instance, ref TextGenerationSettings __result)
        {
            if (percent == 100 || !IsDialogue(__instance)) return;
            float factor = percent / 100f;
            __result.fontSize = Mathf.Max(1, Mathf.RoundToInt(__result.fontSize * factor));
            if (__result.resizeTextForBestFit)
            {
                __result.resizeTextMaxSize = Mathf.Max(1, Mathf.RoundToInt(__result.resizeTextMaxSize * factor));
                __result.resizeTextMinSize = Mathf.Max(1, Mathf.RoundToInt(__result.resizeTextMinSize * factor));
            }
        }
    }
}
