using System;

namespace ZhaoHuoSystem;

/// <summary>Pointer press ownership and keyboard focus, independent of Unity execution order.</summary>
public sealed class InputIsolationPolicy
{
    private int previousButtons, ownedButtons, releaseFrame = -1, gameReleaseFrame = -1, keyboardReleaseFrame = -1, focusPendingFrame = -1;
    private bool wasEditing;
    public bool BlockPointer { get; private set; }
    public bool BlockKeyboard { get; private set; }
    public int OwnedButtons => ownedButtons;

    public bool Pointer(int frame, bool overMod, int buttons)
    {
        int pressed = buttons & ~previousButtons, released = previousButtons & ~buttons;
        bool outsidePress = pressed != 0 && !overMod;
        if ((released & ownedButtons) != 0) releaseFrame = frame;
        if ((released & ~ownedButtons) != 0) gameReleaseFrame = frame;
        ownedButtons = (ownedButtons & buttons) | (overMod ? pressed : 0);
        // IMGUI decides text focus only in a later OnGUI pass; keyboard events queued
        // behind this press must not reach the game before that decision.
        if (overMod && pressed != 0) focusPendingFrame = frame;
        // A new outside press is a distinct gesture, including later queued events
        // in the same Unity frame. It must not inherit an earlier release latch.
        if (outsidePress && ownedButtons == 0) releaseFrame = -1;
        bool gameGesture = (buttons & ~ownedButtons) != 0 || gameReleaseFrame == frame;
        BlockPointer = ownedButtons != 0 || releaseFrame == frame || overMod && !gameGesture;
        previousButtons = buttons;
        return outsidePress;
    }
    public void Keyboard(int frame, bool mainTextEditing, bool inlineTextEditing)
    {
        bool editing = mainTextEditing || inlineTextEditing;
        if (wasEditing && !editing) keyboardReleaseFrame = frame;
        bool focusPending = focusPendingFrame >= 0 && frame - focusPendingFrame <= 1;
        BlockKeyboard = editing || keyboardReleaseFrame == frame || focusPending;
        wasEditing = editing;
    }
    /// <summary>OnGUI has handled the press, so text focus is now authoritative.</summary>
    public void FocusResolved() => focusPendingFrame = -1;
    public void Reset()
    {
        previousButtons = ownedButtons = 0; releaseFrame = gameReleaseFrame = keyboardReleaseFrame = focusPendingFrame = -1;
        wasEditing = BlockPointer = BlockKeyboard = false;
    }
    public static bool IsTextControl(string? name) => name == "zh.event.search" || name == "zh.value.search"
        || name != null && (name.StartsWith("zh.value.", StringComparison.Ordinal) || name.StartsWith("zh.review.", StringComparison.Ordinal) || name.StartsWith("zh.settings.", StringComparison.Ordinal));
}
