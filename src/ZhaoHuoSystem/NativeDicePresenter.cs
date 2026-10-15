using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using HarmonyLib;
using Mortal.Story;
using UnityEngine;
using UnityEngine.UI;

namespace ZhaoHuoSystem;

/// <summary>Own branch buttons in the actual dice canvas. Native controls/listeners are never cloned or enabled.</summary>
internal sealed class NativeDicePresenter : IDisposable
{
    private readonly IGameAdapter adapter;
    private readonly ManualLogSource log;
    private DiceMenuDialog? dialog;
    private RectTransform? root;
    private Text? feedback;
    private readonly List<Button> branches = new();
    private readonly List<Button> nativeRows = new();
    private Font? font;
    private long languageRevision = -1;
    private string context = "", message = "";
    private float messageUntil;
    private bool attached, failed, observedActive;
    private float began;

    public NativeDicePresenter(IGameAdapter adapter, ManualLogSource log) { this.adapter = adapter; this.log = log; }

    public void Begin(DiceMenuDialog value)
    {
        Clear(); dialog = value; began = Time.unscaledTime;
        // ExecuteRoll's prefix runs before SetActive/options setup. Construction is deferred.
    }

    public void End(DiceMenuDialog value) { if (ReferenceEquals(value, dialog)) Clear(); }
    public void RefreshPresentation() => languageRevision = -1;

    public void Update(bool hidden)
    {
        if (dialog == null || failed) return;
        if (hidden) { if (root != null) root.gameObject.SetActive(false); return; }
        try
        {
            if (!dialog.gameObject.activeInHierarchy)
            {
                if (observedActive) Clear();
                else if (Time.unscaledTime - began > 5) Fail("dice dialog did not become active");
                return;
            }
            observedActive = true;
            if (!adapter.Snapshot.Dice.Active)
            {
                if (root != null) root.gameObject.SetActive(false);
                if (!attached && Time.unscaledTime - began > 2) Fail("native dice snapshot unavailable");
                return;
            }
            if (root != null) root.gameObject.SetActive(true);
            if (!attached) Attach();
            if (!attached) { if (Time.unscaledTime - began > 2) Fail("native dice result anchors unavailable"); return; }
            Render();
        }
        catch (Exception ex) { Fail(ex.GetType().Name + ": " + ex.Message); }
    }

    private void Attach()
    {
        if (dialog == null) return;
        var cached = AccessTools.Property(dialog.GetType(), "CachedButtons")?.GetValue(dialog, null);
        nativeRows.Clear();
        foreach (var value in R.Items(cached)) if (value is Button button && button.gameObject.activeInHierarchy) nativeRows.Add(button);
        if (nativeRows.Count == 0 || dialog.transform is not RectTransform) return;
        // A raycast-free root fills the existing canvas. Only owned buttons receive pointer events.
        root = NewRect("ZhaoHuo.Dice", dialog.transform);
        root.pivot = new Vector2(.5f, .5f);
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.sizeDelta = Vector2.zero;
        root.anchoredPosition = Vector2.zero;
        root.SetAsLastSibling();
        feedback = NewText("Feedback", root);
        for (int i = 0; i < nativeRows.Count; i++)
        {
            int index = i;
            branches.Add(NewButton("Branch." + i, root, () =>
            {
                var dice = adapter.Snapshot.Dice;
                if (index >= dice.Branches.Count) return;
                var result = adapter.RollDiceBranch(dice.Branches[index].Id);
                // The wheel itself confirms success; only a refusal needs words.
                message = result.Success ? "" : result.Message; messageUntil = Time.unscaledTime + 5;
            }));
        }
        attached = true;
        log.LogInfo("DICE_INLINE attached owned branch buttons; native result listeners unchanged");
    }

    private void Render()
    {
        if (root == null || feedback == null || dialog == null) return;
        var dice = adapter.Snapshot.Dice;
        string identity = dice.Min.ToString(CultureInfo.InvariantCulture) + "|" + dice.Max + "|" + dice.Addition;
        if (context != identity) { context = identity; message = ""; }
        if (languageRevision != L.Revision)
        {
            if (font != null) UnityEngine.Object.Destroy(font);
            font = Font.CreateDynamicFontFromOSFont(L.FontNames, 18);
            foreach (var text in root.GetComponentsInChildren<Text>(true)) text.font = font;
            languageRevision = L.Revision;
        }
        // One column of branch buttons left of the native options, clear of their overflowing text.
        float columnWidth = 76, rowsLeft = float.MaxValue, lowest = float.MaxValue;
        for (int i = 0; i < branches.Count; i++)
        {
            var button = branches[i];
            bool visible = i < dice.Branches.Count && nativeRows[i] != null && nativeRows[i].gameObject.activeInHierarchy;
            button.gameObject.SetActive(visible);
            if (!visible) continue;
            var branch = dice.Branches[i];
            SetLabel(button, !branch.Reachable ? L.Text("不可达") : branch.RawValue.HasValue && dice.PendingRawValue == branch.RawValue ? L.Text("已指定")
                : L.Format("指定 {0}", NativeUiState.ChanceLabel(branch.Chance)));
            button.interactable = dice.CanSpecify && branch.Reachable && branch.RawValue.HasValue;
            columnWidth = Mathf.Max(columnWidth, Width(button, 76));
            rowsLeft = Mathf.Min(rowsLeft, VisualBounds(nativeRows[i]).xMin);
        }
        float columnX = Mathf.Max(root.rect.xMin + 12, rowsLeft - columnWidth - 10);
        for (int i = 0; i < branches.Count; i++)
        {
            if (!branches[i].gameObject.activeSelf) continue;
            var row = VisualBounds(nativeRows[i]);
            Place(branches[i].transform as RectTransform, columnX, row.center.y + 16, columnWidth, 32);
            lowest = Mathf.Min(lowest, row.center.y - 16);
        }
        bool showMessage = message.Length > 0 && Time.unscaledTime < messageUntil && lowest < float.MaxValue;
        feedback.gameObject.SetActive(showMessage);
        if (showMessage)
        {
            feedback.text = message;
            Place(feedback.rectTransform, columnX, lowest - 6, Mathf.Min(root.rect.xMax - columnX - 12, 420), 28);
        }
    }

    /// <summary>Row rect plus any overflowing option text, which the game draws past the button's own rect.</summary>
    private Rect VisualBounds(Button row)
    {
        var bounds = Bounds(row.transform as RectTransform);
        foreach (var text in row.GetComponentsInChildren<Text>())
        {
            if (!text.isActiveAndEnabled || string.IsNullOrEmpty(text.text) || text.horizontalOverflow != HorizontalWrapMode.Overflow) continue;
            var rect = Bounds(text.rectTransform);
            float local = text.rectTransform.rect.width, width = text.preferredWidth * (local > 0 ? rect.width / local : 1);
            float left = text.alignment switch
            {
                TextAnchor.UpperCenter or TextAnchor.MiddleCenter or TextAnchor.LowerCenter => rect.center.x - width / 2,
                TextAnchor.UpperRight or TextAnchor.MiddleRight or TextAnchor.LowerRight => rect.xMax - width,
                _ => rect.xMin
            };
            bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, left), bounds.yMin, Mathf.Max(bounds.xMax, left + width), bounds.yMax);
        }
        return bounds;
    }
    private void Fail(string reason)
    {
        // Without owned buttons the native dialog simply keeps its own flow.
        log.LogWarning("DICE_INLINE unavailable: " + reason + "; native dice flow unchanged");
        Clear(); failed = true;
    }
    private Rect Bounds(RectTransform? rect)
    {
        if (rect == null || root == null) throw new MissingReferenceException("native dice row");
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        var min = root.InverseTransformPoint(corners[0]); var max = root.InverseTransformPoint(corners[2]);
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
    private static RectTransform NewRect(string name, Transform parent)
    {
        var value = new GameObject(name, typeof(RectTransform)); value.transform.SetParent(parent, false);
        var rect = (RectTransform)value.transform; rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.pivot = new Vector2(0, 1); return rect;
    }
    private Text NewText(string name, Transform parent)
    {
        var rect = NewRect(name, parent); var text = rect.gameObject.AddComponent<Text>();
        text.font = font; text.fontSize = 16; text.color = new Color(.94f, .95f, .96f, 1); text.raycastTarget = false;
        text.alignment = TextAnchor.MiddleLeft; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate; return text;
    }
    private Button NewButton(string name, Transform parent, Action action)
    {
        var rect = NewRect(name, parent); var image = rect.gameObject.AddComponent<Image>(); image.color = Color.white;
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        var colors = button.colors; colors.normalColor = new Color(.15f, .18f, .21f, 1); colors.highlightedColor = new Color(.25f, .32f, .39f, 1);
        colors.pressedColor = new Color(.22f, .37f, .51f, 1); colors.selectedColor = colors.highlightedColor; colors.disabledColor = new Color(.15f, .16f, .17f, .65f); button.colors = colors;
        var label = NewText("Label", rect); label.alignment = TextAnchor.MiddleCenter; Stretch(label.rectTransform, 6, 2);
        button.onClick.AddListener(() => action()); return button;
    }
    private static void SetLabel(Button? button, string text) { if (button != null) button.GetComponentInChildren<Text>().text = text; }
    private static float Width(Button? button, float min) => button == null ? min : Mathf.Max(min, button.GetComponentInChildren<Text>().preferredWidth + 20);
    private static void Place(RectTransform? rect, float x, float y, float width, float height)
    {
        if (rect == null) return;
        rect.anchorMin = rect.anchorMax = rect.parent is RectTransform parent ? parent.pivot : new Vector2(.5f, .5f);
        rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, height);
    }
    private static void Stretch(RectTransform rect, float horizontal, float vertical)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(horizontal, vertical); rect.offsetMax = new Vector2(-horizontal, -vertical); }
    private void Clear()
    {
        if (root != null) { root.gameObject.SetActive(false); UnityEngine.Object.Destroy(root.gameObject); }
        if (font != null) UnityEngine.Object.Destroy(font);
        root = null; font = null; dialog = null; feedback = null;
        branches.Clear(); nativeRows.Clear(); context = message = ""; attached = observedActive = failed = false; languageRevision = -1;
    }
    public void Dispose() => Clear();
}
