using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace ZhaoHuoSystem;

/// <summary>Local raycast shield and native input gates. Never mutates action bindings or EventSystem state.</summary>
internal sealed class NativeInputGuard : IDisposable
{
    private readonly ToolWindow window;
    private readonly ManualLogSource log;
    private readonly InputIsolationPolicy policy = new();
    private readonly Dictionary<InputAction, ActionOrigin> origins = new();
    private RectTransform? shieldRoot, shieldRect;
    private bool disposed;
    private Vector2 pointer;
    private int buttons;
    public bool BlocksPointer => policy.BlockPointer;
    public bool BlocksKeyboard => policy.BlockKeyboard;

    public NativeInputGuard(ToolWindow window, ManualLogSource log)
    {
        this.window = window; this.log = log;
        if (Mouse.current != null) { pointer = Mouse.current.position.ReadValue(); buttons = MouseButtons(Mouse.current); }
        InputSystem.onBeforeUpdate += BeforeUpdate;
        InputSystem.onEvent += InputEvent;
        InputSystem.onActionChange += ActionChanged;
        Synchronize();
    }
    public void Tick() => Synchronize();
    public void FocusResolved() { policy.FocusResolved(); Synchronize(); }
    public bool Suppress(InputAction.CallbackContext context)
    {
        bool blocked = SuppressDevice(context.control?.device);
        origins[context.action] = new ActionOrigin { Frame = Time.frameCount, Blocked = blocked };
        return blocked;
    }
    private bool SuppressDevice(InputDevice? device) => Application.isFocused && (device is Keyboard ? policy.BlockKeyboard : device is Pointer && policy.BlockPointer);
    public bool Suppress(InputAction action)
    {
        // Navigation polls after all queued events. Preserve the origin's decision
        // even if a later mouse event in the frame moved outside the mod window.
        if (origins.TryGetValue(action, out var origin) && origin.Frame == Time.frameCount) return origin.Blocked;
        return SuppressDevice(action.activeControl?.device);
    }
    private void ActionChanged(object source, InputActionChange change)
    {
        if (source is InputAction action && (change == InputActionChange.ActionStarted || change == InputActionChange.ActionPerformed))
            origins[action] = new ActionOrigin { Frame = Time.frameCount, Blocked = SuppressDevice(action.activeControl?.device) };
    }
    private void BeforeUpdate() => Synchronize();
    private void InputEvent(InputEventPtr inputEvent, InputDevice device)
    {
        if (disposed || !Application.isFocused || device is not Mouse mouse || !inputEvent.IsA<StateEvent>() && !inputEvent.IsA<DeltaStateEvent>()) return;
        try
        {
            // Delta events may contain only one control. Retain missing values
            // from the preceding event, never substitute the OS queue's final position.
            if (mouse.position.ReadValueFromEvent(inputEvent, out Vector2 position)) pointer = position;
            ReadButton(mouse.leftButton, inputEvent, 1); ReadButton(mouse.rightButton, inputEvent, 2); ReadButton(mouse.middleButton, inputEvent, 4);
            ReadButton(mouse.forwardButton, inputEvent, 8); ReadButton(mouse.backButton, inputEvent, 16);
            Synchronize();
        }
        catch (Exception ex) { Fail(ex); }
    }
    private void ReadButton(UnityEngine.InputSystem.Controls.ButtonControl control, InputEventPtr inputEvent, int mask)
    {
        if (!control.ReadValueFromEvent(inputEvent, out float value)) return;
        if (value >= control.pressPointOrDefault) buttons |= mask; else buttons &= ~mask;
    }
    private static int MouseButtons(Mouse mouse) => (mouse.leftButton.isPressed ? 1 : 0) | (mouse.rightButton.isPressed ? 2 : 0)
        | (mouse.middleButton.isPressed ? 4 : 0) | (mouse.forwardButton.isPressed ? 8 : 0) | (mouse.backButton.isPressed ? 16 : 0);
    public void ObserveGui(Event current)
    {
        if (current.type != EventType.MouseDown && current.type != EventType.MouseUp && current.type != EventType.MouseDrag && current.type != EventType.MouseMove && current.type != EventType.ScrollWheel) return;
        // IMGUI can replay a different event queue after InputSystem. With a
        // real Mouse, onEvent remains the sole position/button sequence owner.
        if (Mouse.current == null)
        {
            pointer = new Vector2(current.mousePosition.x, Screen.height - current.mousePosition.y);
            if (current.button >= 0 && current.button < 3)
            {
                int mask = current.button == 0 ? 1 : current.button == 1 ? 2 : 4;
                if (current.type == EventType.MouseDown) buttons |= mask;
                if (current.type == EventType.MouseUp) buttons &= ~mask;
            }
        }
        if (current.type == EventType.MouseDown)
        {
            window.PrepareInputRegions();
            bool over = window.Visible ? window.WindowRect.Contains(current.mousePosition) : window.LauncherRect.Contains(current.mousePosition);
            if (!over) window.RequestInputBlur();
        }
        Synchronize();
    }
    private void Synchronize()
    {
        if (disposed) return;
        try
        {
            if (!Application.isFocused) { Reset(); return; }
            window.PrepareInputRegions();
            Vector2 guiPointer = new(pointer.x, Screen.height - pointer.y);
            bool over = window.Visible ? window.WindowRect.Contains(guiPointer) : window.LauncherRect.width > 0 && window.LauncherRect.Contains(guiPointer);
            if (policy.Pointer(Time.frameCount, over, buttons)) window.RequestInputBlur();
            policy.Keyboard(Time.frameCount, window.HasTextFocus, false);
            UpdateShield();
        }
        catch (Exception ex) { Fail(ex); }
    }
    private void EnsureShield()
    {
        if (shieldRoot != null) return;
        var root = new GameObject("ZhaoHuo.PointerShield", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        UnityEngine.Object.DontDestroyOnLoad(root);
        shieldRoot = (RectTransform)root.transform;
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32760;
        var child = new GameObject("Window", typeof(RectTransform), typeof(Image)); child.transform.SetParent(root.transform, false);
        shieldRect = (RectTransform)child.transform; shieldRect.anchorMin = shieldRect.anchorMax = new Vector2(0, 1); shieldRect.pivot = new Vector2(0, 1);
        var image = child.GetComponent<Image>(); image.color = Color.clear; image.raycastTarget = true; image.canvasRenderer.cullTransparentMesh = false;
    }
    private void UpdateShield()
    {
        EnsureShield();
        Rect region = window.Visible ? window.WindowRect : window.LauncherRect;
        shieldRect!.gameObject.SetActive(region.width > 0 && region.height > 0);
        shieldRect.anchoredPosition = new Vector2(region.x, -region.y); shieldRect.sizeDelta = region.size;
    }
    private void Fail(Exception ex) { log.LogWarning("INPUT_GUARD released: " + ex.GetType().Name + " " + ex.Message); Reset(); }
    public void Reset()
    {
        policy.Reset(); origins.Clear(); buttons = 0;
        window.CancelInputCapture();
        if (shieldRect != null) shieldRect.gameObject.SetActive(false);
    }
    public void Dispose()
    {
        if (disposed) return;
        InputSystem.onBeforeUpdate -= BeforeUpdate; InputSystem.onEvent -= InputEvent; InputSystem.onActionChange -= ActionChanged;
        Reset(); disposed = true;
        if (shieldRoot != null) UnityEngine.Object.Destroy(shieldRoot.gameObject);
    }
    private sealed class ActionOrigin { public int Frame; public bool Blocked; }
}
