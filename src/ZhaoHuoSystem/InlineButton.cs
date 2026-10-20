using System;
using UnityEngine;
using UnityEngine.UI;

namespace ZhaoHuoSystem;

/// <summary>
/// A small mod button placed in the game's own canvas, right after the visible text of a native line (「最大消费」's
/// count, the free map's date) or in a row of its own (pinball). Same colours as the dice buttons; sized from the
/// native line's font and the player's scale.
/// </summary>
internal static class InlineButton
{
    public static Button Create(string name, Transform parent, Action click, out Text label)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        root.transform.SetParent(parent, false);
        var image = root.GetComponent<Image>(); image.color = Color.white;
        var button = root.GetComponent<Button>(); button.targetGraphic = image;
        // Pad and keyboard navigation stay on the game's own targets.
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        var colors = button.colors; colors.normalColor = new Color(.15f, .18f, .21f, 1); colors.highlightedColor = new Color(.25f, .32f, .39f, 1);
        colors.pressedColor = new Color(.22f, .37f, .51f, 1); colors.selectedColor = colors.highlightedColor; button.colors = colors;
        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text)); labelObject.transform.SetParent(root.transform, false);
        label = labelObject.GetComponent<Text>(); label.alignment = TextAnchor.MiddleCenter; label.color = new Color(.94f, .95f, .96f, 1); label.raycastTarget = false;
        var labelRect = (RectTransform)labelObject.transform; labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one; labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
        button.onClick.AddListener(() => click());
        root.SetActive(false);
        return button;
    }

    /// <summary>
    /// Right of the line's visible text, centred on its first row; <paramref name="size"/> is the label's size against the
    /// line's. The game's canvas already scales with the screen.
    /// </summary>
    public static void Place(Button button, Text label, Text line, Font font, string text, float size = .8f)
    {
        label.font = font; label.text = text;
        label.fontSize = Mathf.Max(10, Mathf.RoundToInt(line.fontSize * size * UiScale.User));
        var rect = line.rectTransform.rect;
        float visible = line.preferredWidth, row = Mathf.Min(rect.height, line.fontSize * 1.3f);
        float right = line.alignment switch
        {
            TextAnchor.UpperRight or TextAnchor.MiddleRight or TextAnchor.LowerRight => rect.xMax,
            TextAnchor.UpperCenter or TextAnchor.MiddleCenter or TextAnchor.LowerCenter => rect.center.x + visible / 2,
            _ => rect.xMin + visible
        };
        float middle = line.alignment switch
        {
            TextAnchor.UpperLeft or TextAnchor.UpperCenter or TextAnchor.UpperRight => rect.yMax - row / 2,
            TextAnchor.LowerLeft or TextAnchor.LowerCenter or TextAnchor.LowerRight => rect.yMin + row / 2,
            _ => rect.center.y
        };
        var own = (RectTransform)button.transform;
        own.anchorMin = own.anchorMax = new Vector2(0, .5f); own.pivot = new Vector2(0, .5f);
        float height = line.fontSize * size * 1.6875f * UiScale.User;
        own.sizeDelta = new Vector2(label.preferredWidth + height * .8f, height);
        own.anchoredPosition = new Vector2(right - rect.xMin + height * .4f, middle - rect.center.y);
        Skin(button, height);
    }

    /// <summary>
    /// 界面风格: the game's framed button behind the label, lit under the pointer (or while <paramref name="chosen"/>), as in
    /// the panel, dimmed while the button cannot be pressed; the flat colours when the skin is off or not found. The frame's
    /// clear margin reaches outside the button, so the frame shows at the button's size. Cheap to call every frame: only a
    /// change is written.
    /// </summary>
    internal static void Skin(Button button, float height, bool chosen = false)
    {
        var normal = GameSkin.Sliced(GameSkin.Part.Button, out float scale, out float pad);
        var root = button.GetComponent<Image>();
        var existing = button.transform.Find("Frame");
        if (normal == null)
        {
            if (existing != null && existing.gameObject.activeSelf) existing.gameObject.SetActive(false);
            button.transition = Selectable.Transition.ColorTint; button.targetGraphic = root; root.color = Color.white;
            return;
        }
        Image frame;
        if (existing == null)
        {
            var made = new GameObject("Frame", typeof(RectTransform), typeof(Image));
            made.transform.SetParent(button.transform, false); made.transform.SetAsFirstSibling();
            frame = made.GetComponent<Image>(); frame.type = Image.Type.Sliced; frame.raycastTarget = false;
            var stretch = (RectTransform)made.transform; stretch.anchorMin = Vector2.zero; stretch.anchorMax = Vector2.one;
        }
        else { frame = existing.GetComponent<Image>(); if (!existing.gameObject.activeSelf) existing.gameObject.SetActive(true); }
        var lit = GameSkin.Sliced(GameSkin.Part.ButtonLit, out _, out _);
        if (chosen && lit != null) normal = lit;
        // The panel draws it at its scale on a 30-pixel button; the same proportion here.
        float k = scale * height / 30;
        frame.sprite = normal;
        if (Mathf.Abs(frame.pixelsPerUnitMultiplier - 1 / k) > .0001f) frame.pixelsPerUnitMultiplier = 1 / k;
        var frameRect = (RectTransform)frame.transform;
        var margin = new Vector2(pad * k, pad * k);
        if (frameRect.offsetMax != margin || frameRect.offsetMin != -margin) { frameRect.offsetMin = -margin; frameRect.offsetMax = margin; }
        frame.color = button.interactable ? Color.white : new Color(1, 1, 1, .45f);
        button.transition = Selectable.Transition.SpriteSwap; button.targetGraphic = frame;
        var state = new SpriteState { highlightedSprite = lit, pressedSprite = lit, selectedSprite = lit };
        if (!button.spriteState.Equals(state)) button.spriteState = state;
        // Still catches the pointer; a little dark under the frame's see-through middle keeps the label clear on bright maps.
        root.color = new Color(0, 0, 0, .45f);
    }
}
