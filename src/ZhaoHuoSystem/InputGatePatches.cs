using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace ZhaoHuoSystem;

// These native callbacks were verified against the fingerprinted game's input paths.
// Gate only callbacks reached from a real InputAction; script/auto continuation APIs stay intact.
[HarmonyPatch]
internal static class NativeActionInputGate
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        string[] targets =
        {
            "Mortal.Core.InputButton:InputActionChanged",
            "Mortal.Free.FreeManager:NextInputAction", "Mortal.Free.FreeManager:MenuActionChanged",
            "Mortal.Story.StoryManager:MenuActionChanged",
            "Mortal.Battle.GameLevelManager:MenuActionChange", "Mortal.Battle.GameLevelManager:PlayerInputActionTriggered",
            "Mortal.Combat.CombatUIManager:MenuActionChanged",
            "Mortal.Core.CreditPanel:PerformedAction", "Mortal.Core.CgPanel:PerformedAction", "Mortal.Core.EndGamePanel:PerformedAction",
            "Mortal.Battle.GameOverPanel:ContinueAction", "Mortal.Combat.CombatManager:NextInputAction",
            "Mortal.Story.DiceMenuDialog:NextInputAction", "Mortal.Story.DiceMenuDialog:DestinyAction",
            "Mortal.Story.DiceMenuDialog:DestinyRestartAction", "Mortal.Story.DiceMenuDialog:RangeAction",
            "Mortal.Story.StoryManager:SubmitActionChanged", "Mortal.Story.StoryManager:StatusActionChanged",
            "Mortal.Story.StoryManager:AutoActionChanged", "Mortal.Story.StoryManager:LogActionChanged", "Mortal.Story.StoryManager:SkipActionPerformed",
            "Mortal.Free.FreeManager:StatusActionChanged", "Mortal.Free.FreeManager:SaveActionPerformed", "Mortal.Free.FreeManager:ToggleSelectedPerformed",
            "Mortal.Core.ButtonNavigationInputAction:NextActionPerformed", "Mortal.Core.ButtonNavigationInputAction:PreviousActionPerfomed",
            "Mortal.Core.CustomMenuDialog:NavigateActionChanged", "Mortal.Core.FateBonusPanel:PlusActionPerformed", "Mortal.Core.FateBonusPanel:MinusActionPerformed",
            "Mortal.Core.LoadGamePanel:DeleteActionPerformed", "Mortal.Core.LongClickButton:ActionStarted", "Mortal.Core.LongClickButton:ActionPerformed",
            "Mortal.Core.PlayerStatusPanel:NavLeft_1_Started", "Mortal.Core.PlayerStatusPanel:NavRight_1_Started",
            "Mortal.Core.ToggleNavigationInputAction:NextActionPerformed", "Mortal.Core.ToggleNavigationInputAction:PreviousActionPerfomed",
            "Mortal.Core.ToggleSelectedInputAction:SelectedAction", "Mortal.Story.ShopPanel:NavRighPerformed", "Mortal.Story.ShopPanel:NavLeftPerformed",
            "Mortal.Story.ShopPanel:MinusActionPerformed", "Mortal.Story.ShopPanel:PlusActionPerformed",
            "Mortal.Battle.PlayerController:MoveActionChanged", "Mortal.Battle.PlayerController:DodgeActionStarted",
            "Mortal.Battle.PlayerController:Attack1ActionStarted", "Mortal.Battle.PlayerController:Attack2ActionStarted",
            "Mortal.Battle.PlayerController:Attack2ActionPerformed", "Mortal.Battle.PlayerController:TalkActionStarted", "Mortal.Battle.SkillButton:InputActionTriggered",
            "Mortal.Combat.CombatUIManager:SpeedActionPerformed", "Mortal.Combat.CombatUIManager:StatusActionChanged",
            "Mortal.Combat.CombatActionButtonPanel:NavigateActionChanged", "Mortal.Pinball.SpringLauncher2D:OnLaunchStarted"
        };
        var seen = new HashSet<MethodBase>();
        foreach (string target in targets)
        {
            int colon = target.IndexOf(':'); string typeName = target.Substring(0, colon);
            int assemblyEnd = typeName.IndexOf('.', "Mortal.".Length);
            string assemblyName = typeName.Substring(0, assemblyEnd);
            var type = AccessTools.TypeByName(typeName) ?? Type.GetType(typeName + ", " + assemblyName, false);
            var method = type == null ? null : AccessTools.Method(type, target.Substring(colon + 1), new[] { typeof(InputAction.CallbackContext) });
            if (method != null && seen.Add(method)) yield return method;
            else Debug.LogWarning("ZhaoHuo INPUT_GATE missing " + target);
        }
    }
    private static bool Prefix(InputAction.CallbackContext __0)
    {
        // Native canceled callbacks clear held movement/long-click state. Never
        // suppress cleanup merely because focus moved into an editor mid-gesture.
        return __0.canceled || Plugin.Instance?.SuppressGameInput(__0) != true;
    }
}

[HarmonyPatch(typeof(InputSystemUIInputModule), "ProcessNavigation")]
internal static class NativeNavigationInputGate
{
    private static readonly MethodInfo SendNavigation = AccessTools.PropertyGetter(typeof(EventSystem), nameof(EventSystem.sendNavigationEvents));
    private static readonly MethodInfo WasPressed = AccessTools.Method(typeof(InputAction), nameof(InputAction.WasPressedThisFrame));
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(SendNavigation)) { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(NativeNavigationInputGate), nameof(CanNavigate)); }
            else if (instruction.Calls(WasPressed)) { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(NativeNavigationInputGate), nameof(Pressed)); }
            yield return instruction;
        }
    }
    // ProcessNavigation already delivered updateSelected before this original
    // property read, so native inline InputField typing remains operational.
    private static bool CanNavigate(EventSystem system) => system.sendNavigationEvents && Plugin.Instance?.SuppressKeyboard != true && Plugin.Instance?.SuppressNavigation != true;
    private static bool Pressed(InputAction action) => action.WasPressedThisFrame() && Plugin.Instance?.SuppressGameInput(action) != true;
}

[HarmonyPatch(typeof(Fungus.DialogInput), "Update")]
internal static class LegacyDialogInputGate
{
    private static readonly MethodInfo MouseDown = AccessTools.Method(typeof(Input), nameof(Input.GetMouseButtonDown), new[] { typeof(int) });
    private static readonly MethodInfo ButtonDown = AccessTools.Method(typeof(Input), nameof(Input.GetButtonDown), new[] { typeof(string) });
    private static readonly MethodInfo ButtonHeld = AccessTools.Method(typeof(Input), nameof(Input.GetButton), new[] { typeof(string) });
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(MouseDown)) instruction.operand = AccessTools.Method(typeof(LegacyDialogInputGate), nameof(Mouse));
            else if (instruction.Calls(ButtonDown)) instruction.operand = AccessTools.Method(typeof(LegacyDialogInputGate), nameof(Down));
            else if (instruction.Calls(ButtonHeld)) instruction.operand = AccessTools.Method(typeof(LegacyDialogInputGate), nameof(Held));
            yield return instruction;
        }
    }
    private static bool Mouse(int button) => Plugin.Instance?.SuppressPointer != true && Input.GetMouseButtonDown(button);
    private static bool Down(string button) => Plugin.Instance?.SuppressKeyboard != true && !(button == "Submit" && Plugin.Instance?.SuppressSubmit == true) && Input.GetButtonDown(button);
    private static bool Held(string button) => Plugin.Instance?.SuppressKeyboard != true && !(button == "Submit" && Plugin.Instance?.SuppressSubmit == true) && Input.GetButton(button);
}
