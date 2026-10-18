using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Mortal.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ZhaoHuoSystem;

/// <summary>
/// 收藏 and 备注 on the manual save slots of the load and save pages (F50). A filled slot gets a star under its number
/// and its note after the save time; until set, both show only while the pointer is on the slot. The star toggles on
/// a click, the note opens a small editor. Stored by <see cref="SaveNoteStore"/>, never in a save. Saving over a slot
/// keeps them (they are in plain sight on the row just saved); deleting the save in game drops them.
/// </summary>
internal sealed class SaveNotes(ManualLogSource log, SaveNoteStore store) : IDisposable
{
    private sealed class Row
    {
        public MonoBehaviour Slot = null!; public RectTransform Root = null!; public Canvas? Canvas;
        public Button Star = null!, Note = null!; public Text StarLabel = null!, NoteLabel = null!;
        public string Fitted = "", FittedFor = "";
    }
    // The game's own gold (slot numbers) for the player's marks; 「＋备注」 and the empty star stay muted.
    private static readonly Color Gold = new(.85f, .69f, .47f, 1), Muted = new(.85f, .84f, .84f, .55f);
    private static readonly MethodInfo? UserFolder = AccessTools.Method(typeof(SaveSystem), "GetUserFolder");
    private readonly List<MonoBehaviour> panels = new();
    private readonly Dictionary<MonoBehaviour, Row> rows = new();
    private string account = "Save";
    private Font? font;
    private long revision = -1;
    private string? editing;
    private string editingSlot = "", editingTitle = "", draft = "";
    private bool focusDraft;
    private Texture2D? dim, background, input, buttonFill, buttonHover, primary, primaryHover;
    private GUIStyle? title, hint, field, plain, accent;
    private float styleScale = -1;
    private const string DraftControl = "zh.note.draft";

    /// <summary>The note editor or the overwrite question is open: it takes the whole screen's pointer and the keyboard.</summary>
    public bool Editing => editing != null || overwrite != null;
    // A favourite's overwrite waits for the player's answer (F73); the slot the answer allowed, while it is written.
    private string? overwrite, allowed;
    private string overwriteText = "";
    /// <summary>After an allowed overwrite: the open save page shows the new save.</summary>
    public Action? Overwritten;

    /// <summary>
    /// Before the game writes a save to this slot: a starred slot that already holds a save is held back and the
    /// player is asked once more (F73), whichever way the save came (the save page, the story menu, the map). True
    /// while held: nothing is written unless the answer is 覆盖, which writes it then.
    /// </summary>
    public bool HoldOverwrite(string slot)
    {
        try
        {
            if (slot == allowed) return false;
            string key = SaveNoteStore.Key(Account(), slot);
            if (!store.Favourite(key) || SaveSystem.Instance == null || SaveSystem.Instance.GetSaveData(slot) == null) return false;
            overwrite = slot;
            string note = store.Note(key);
            overwriteText = L.Format("{0} 是收藏的存档", SlotName(slot)) + (note.Length > 0 ? L.Format("（{0}）", note) : "") + L.Text("，覆盖后原来的进度就没有了。");
            log.LogInfo("SAVE_NOTE overwrite asked slot=" + slot);
            return true;
        }
        catch (Exception ex) { log.LogWarning("SAVE_NOTE overwrite " + ex.GetBaseException().Message); return false; }
    }
    private static string SlotName(string slot)
    {
        string format = L.GameText("System/SaveSlotText", "");
        return format.Contains("{0}") ? string.Format(format, slot) : slot;
    }
    private void AnswerOverwrite(bool yes)
    {
        string? slot = overwrite; overwrite = null;
        if (!yes || slot == null) { log.LogInfo("SAVE_NOTE overwrite cancelled slot=" + slot); return; }
        try { allowed = slot; SaveSystem.Instance.SaveGameData(slot); log.LogInfo("SAVE_NOTE overwrite done slot=" + slot); }
        catch (Exception ex) { log.LogWarning("SAVE_NOTE overwrite " + ex.GetBaseException().Message); }
        finally { allowed = null; }
        Overwritten?.Invoke();
    }

    /// <summary>After a page refreshed its slots from disk.</summary>
    public void Refreshed(MonoBehaviour panel)
    {
        if (!panels.Contains(panel)) panels.Add(panel);
        account = Account();
        // Slots of a page left behind in an earlier scene.
        foreach (var dead in rows.Keys.Where(slot => slot == null).ToList()) rows.Remove(dead);
    }

    /// <summary>The game deleted a save (the load page's delete): its star and note go too.</summary>
    public void Deleted(string slot)
    {
        try { if (store.Remove(SaveNoteStore.Key(Account(), slot))) log.LogInfo("SAVE_NOTE removed slot=" + slot); }
        catch (Exception ex) { log.LogWarning("SAVE_NOTE " + ex.GetBaseException().Message); }
    }

    private static string Account()
    {
        try { return SaveSystem.Instance != null && UserFolder?.Invoke(SaveSystem.Instance, null) is string folder && folder.Length > 0 ? folder : "Save"; }
        catch { return "Save"; }
    }

    public void Update(bool hidden)
    {
        panels.RemoveAll(panel => panel == null);
        if (panels.Count == 0) return;
        if (font == null || revision != L.Revision)
        {
            if (font != null) UnityEngine.Object.Destroy(font);
            font = Font.CreateDynamicFontFromOSFont(L.FontNames, 20); revision = L.Revision;
            foreach (var row in rows.Values) row.FittedFor = "";
        }
        Vector2? pointer = Mouse.current != null ? Mouse.current.position.ReadValue() : null;
        foreach (var panel in panels)
        {
            if (R.Field(panel, "_saveSlots") is not Array slots) continue;
            bool shown = !hidden && panel.isActiveAndEnabled;
            foreach (var item in slots)
                if (item is MonoBehaviour slot && slot != null && RowOf(slot) is Row row) Layout(row, shown, pointer);
        }
    }

    private Row? RowOf(MonoBehaviour slot)
    {
        if (rows.TryGetValue(slot, out var row)) return row;
        if (R.Field<Button>(slot, "_button") is not Button native || native == null) return null;
        var root = (RectTransform)native.transform;
        row = new Row { Slot = slot, Root = root };
        row.Star = Create("ZhaoHuo.SaveStar", root, () => ToggleStar(row), out row.StarLabel);
        row.Note = Create("ZhaoHuo.SaveNote", root, () => Open(row), out row.NoteLabel);
        rows[slot] = row;
        return row;
    }

    private static Button Create(string name, Transform parent, Action click, out Text label)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        root.transform.SetParent(parent, false);
        // Clear but still hit by the pointer, so the click lands here and not on the slot underneath.
        var image = root.GetComponent<Image>(); image.color = Color.clear; image.canvasRenderer.cullTransparentMesh = false;
        var own = root.GetComponent<Button>(); own.targetGraphic = image; own.transition = Selectable.Transition.None;
        own.navigation = new Navigation { mode = Navigation.Mode.None };
        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text)); labelObject.transform.SetParent(root.transform, false);
        label = labelObject.GetComponent<Text>(); label.raycastTarget = false;
        label.horizontalOverflow = HorizontalWrapMode.Overflow; label.verticalOverflow = VerticalWrapMode.Overflow;
        var rect = (RectTransform)labelObject.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        own.onClick.AddListener(() => click());
        root.SetActive(false);
        return own;
    }

    private string KeyOf(Row row) => SaveNoteStore.Key(account, R.Field<string>(row.Slot, "_slot", ""));

    private void Layout(Row row, bool shown, Vector2? pointer)
    {
        if (!shown || R.Field(row.Slot, "_currentData") == null) { Show(row.Star, false); Show(row.Note, false); return; }
        // Normal and hover each have a save-time text at the same place; either serves.
        Text? time = null;
        if (R.Field<Text[]>(row.Slot, "_timeText") is Text[] times)
            foreach (var candidate in times)
                if (candidate != null && (time == null || candidate.isActiveAndEnabled)) time = candidate;
        var number = R.Field<Text>(row.Slot, "_slotText");
        if (time == null || number == null) { Show(row.Star, false); Show(row.Note, false); return; }
        string key = KeyOf(row), note = store.Note(key);
        bool favourite = store.Favourite(key);
        // Found once the page is active (a closed page has no active parent canvas to find).
        if (row.Canvas == null) row.Canvas = row.Root.GetComponentInParent<Canvas>()?.rootCanvas;
        var camera = row.Canvas == null || row.Canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : row.Canvas.worldCamera;
        bool hover = pointer.HasValue && RectTransformUtility.RectangleContainsScreenPoint(row.Root, pointer.Value, camera);
        // Both sit on the save-time line, centred on its first row of text.
        Rect timeRect = Local(row.Root, time.rectTransform), numberRect = Local(row.Root, number.rectTransform);
        float scale = time.rectTransform.rect.width > 0 ? timeRect.width / time.rectTransform.rect.width : 1;
        float line = Mathf.Min(timeRect.height, time.fontSize * 1.3f * scale), middle = timeRect.yMax - line / 2, size = time.fontSize * scale;

        Show(row.Star, favourite || hover);
        if (row.Star.gameObject.activeSelf)
        {
            Style(row.StarLabel, favourite ? "★" : "☆", Mathf.RoundToInt(size * 1.3f), favourite ? Gold : Muted, TextAnchor.MiddleCenter);
            Place(row.Star, numberRect.xMin + 8 * scale, middle, size * 1.7f, size * 1.7f);
        }
        Show(row.Note, note.Length > 0 || hover);
        if (row.Note.gameObject.activeSelf)
        {
            float left = timeRect.xMin + time.preferredWidth * scale + 12 * scale, width = Mathf.Max(0, row.Root.rect.xMax - 14 * scale - left);
            string text = note.Length > 0 ? note : L.Text("＋备注");
            Style(row.NoteLabel, null, Mathf.RoundToInt(size), note.Length > 0 ? Gold : Muted, TextAnchor.MiddleLeft);
            Fit(row, text, width);
            Place(row.Note, left, middle, Mathf.Min(width, row.NoteLabel.preferredWidth), line + 6 * scale);
        }
    }

    private void Style(Text label, string? text, int size, Color color, TextAnchor alignment)
    {
        label.font = font; label.fontSize = Mathf.Max(10, size); label.color = color; label.alignment = alignment;
        if (text != null) label.text = text;
    }

    /// <summary>Longer notes end in 「…」 where the row ends; the editor shows them whole.</summary>
    private static void Fit(Row row, string text, float width)
    {
        string signature = text + "\n" + Mathf.RoundToInt(width) + "\n" + row.NoteLabel.fontSize;
        if (row.FittedFor == signature) { row.NoteLabel.text = row.Fitted; return; }
        row.NoteLabel.text = text;
        for (int n = text.Length - 1; n > 0 && row.NoteLabel.preferredWidth > width; n--)
            row.NoteLabel.text = text.Substring(0, char.IsLowSurrogate(text[n]) ? n - 1 : n).TrimEnd() + "…";
        row.Fitted = row.NoteLabel.text; row.FittedFor = signature;
    }

    private static readonly Vector3[] Corners = new Vector3[4];
    private static Rect Local(RectTransform root, RectTransform child)
    {
        var corners = Corners; child.GetWorldCorners(corners);
        Vector2 min = root.InverseTransformPoint(corners[0]), max = root.InverseTransformPoint(corners[2]);
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static void Place(Button button, float left, float middle, float width, float height)
    {
        var rect = (RectTransform)button.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, .5f);
        // Anchored to the slot's left edge, vertically to its centre.
        var parent = ((RectTransform)rect.parent).rect;
        rect.anchoredPosition = new Vector2(left - parent.xMin, middle - parent.center.y);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void Show(Button button, bool show) { if (button.gameObject.activeSelf != show) button.gameObject.SetActive(show); }

    private void ToggleStar(Row row)
    {
        string key = KeyOf(row);
        bool favourite = !store.Favourite(key);
        try { store.SetFavourite(key, favourite); log.LogInfo($"SAVE_NOTE star slot={R.Field<string>(row.Slot, "_slot", "")} on={favourite}"); StarChanged?.Invoke(); }
        catch (Exception ex) { log.LogWarning("SAVE_NOTE " + ex.GetBaseException().Message); }
    }
    /// <summary>After a star is set or taken away.</summary>
    public Action? StarChanged;
    /// <summary>Whether this account's slot is starred.</summary>
    public bool IsFavourite(string slot) => store.Favourite(SaveNoteStore.Key(Account(), slot));

    private void Open(Row row)
    {
        editing = KeyOf(row); editingSlot = R.Field<string>(row.Slot, "_slot", "");
        editingTitle = L.Format("{0} 的备注", R.Field<Text>(row.Slot, "_slotText")?.text ?? editingSlot);
        draft = store.Note(editing); focusDraft = true;
        log.LogInfo("SAVE_NOTE edit slot=" + editingSlot);
    }

    /// <summary>Also on a scene change, outside OnGUI, so focus is left to the next draw. A held overwrite is dropped.</summary>
    public void Close() { editing = null; draft = ""; overwrite = null; }

    private void Commit()
    {
        if (editing == null) return;
        try { store.SetNote(editing, draft); log.LogInfo($"SAVE_NOTE note slot={editingSlot} length={SaveNoteStore.Clean(draft).Length}"); }
        catch (Exception ex) { log.LogWarning("SAVE_NOTE " + ex.GetBaseException().Message); }
        foreach (var row in rows.Values) row.FittedFor = "";
        Close();
    }

    /// <summary>
    /// The editor, centred over a dimmed screen. Drawn alone while open: every click and key is its own, so nothing
    /// reaches the slots behind it. Enter saves, Esc cancels; saving it empty removes the note.
    /// </summary>
    public void Draw()
    {
        if (overwrite != null) { DrawOverwrite(); return; }
        if (editing == null) return;
        EnsureStyles();
        float k = UiScale.Current, width = Mathf.Min(520 * k, Screen.width - 16), height = 176 * k;
        var e = Event.current;
        var box = new Rect((Screen.width - width) / 2, (Screen.height - height) / 2, width, height);
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { e.Use(); Finish(false); return; }
        // Enter while an IME is composing only finishes the word.
        if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && !Composing()) { e.Use(); Finish(true); return; }
        if (e.type == EventType.Repaint) { GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), dim!); GUI.DrawTexture(box, background!); }
        float pad = 16 * k, x = box.x + pad, inner = width - pad * 2, y = box.y + pad;
        GUI.Label(new Rect(x, y, inner, 26 * k), editingTitle, title);
        y += 34 * k;
        GUI.SetNextControlName(DraftControl);
        draft = GUI.TextField(new Rect(x, y, inner, 36 * k), draft, SaveNoteStore.MaxNote, field);
        if (focusDraft && e.type == EventType.Repaint) { GUI.FocusControl(DraftControl); focusDraft = false; }
        y += 44 * k;
        GUI.Label(new Rect(x, y, inner, 22 * k), L.Text("回车保存，Esc 取消；留空保存即删除备注。"), hint);
        float bw = 96 * k, bh = 32 * k, by = box.yMax - pad - bh;
        if (GUI.Button(new Rect(box.xMax - pad - bw, by, bw, bh), L.Text("保存"), accent)) { Finish(true); return; }
        if (GUI.Button(new Rect(box.xMax - pad - bw * 2 - 10 * k, by, bw, bh), L.Text("取消"), plain)) { Finish(false); return; }
        if (e.isMouse || e.type == EventType.ScrollWheel) e.Use();
    }
    /// <summary>The overwrite question, laid out like the editor: 覆盖 writes the held save, 取消 (or Esc) drops it.</summary>
    private void DrawOverwrite()
    {
        EnsureStyles();
        float k = UiScale.Current, width = Mathf.Min(520 * k, Screen.width - 16), pad = 16 * k, inner = width - pad * 2;
        float textHeight = Mathf.Max(24 * k, hint!.CalcHeight(new GUIContent(overwriteText), inner)), height = pad * 2 + 34 * k + textHeight + 50 * k;
        var e = Event.current;
        var box = new Rect((Screen.width - width) / 2, (Screen.height - height) / 2, width, height);
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { e.Use(); AnswerOverwrite(false); return; }
        if (e.type == EventType.Repaint) { GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), dim!); GUI.DrawTexture(box, background!); }
        float x = box.x + pad, y = box.y + pad;
        GUI.Label(new Rect(x, y, inner, 26 * k), L.Text("覆盖收藏的存档？"), title);
        y += 34 * k;
        var wrap = new GUIStyle(hint) { wordWrap = true, fontSize = title!.fontSize - Mathf.RoundToInt(2 * k) };
        wrap.normal.textColor = Palette.Text;
        GUI.Label(new Rect(x, y, inner, textHeight), overwriteText, wrap);
        float bw = 96 * k, bh = 32 * k, by = box.yMax - pad - bh;
        if (GUI.Button(new Rect(box.xMax - pad - bw, by, bw, bh), L.Text("覆盖"), accent)) { AnswerOverwrite(true); return; }
        if (GUI.Button(new Rect(box.xMax - pad - bw * 2 - 10 * k, by, bw, bh), L.Text("取消"), plain)) { AnswerOverwrite(false); return; }
        if (e.isMouse || e.type == EventType.ScrollWheel) e.Use();
    }
    private void Finish(bool save)
    {
        if (save) Commit(); else Close();
        if ((GUI.GetNameOfFocusedControl() ?? "") == DraftControl) GUI.FocusControl(null);
    }

    private static bool Composing()
    {
        try { return !string.IsNullOrEmpty(Input.compositionString); }
        catch (InvalidOperationException) { return false; }
    }

    private void EnsureStyles()
    {
        float k = UiScale.Current;
        if (title != null && Mathf.Abs(styleScale - k) < .01f && revision == L.Revision && font != null) return;
        styleScale = k;
        if (font == null || revision != L.Revision)
        {
            if (font != null) UnityEngine.Object.Destroy(font);
            font = Font.CreateDynamicFontFromOSFont(L.FontNames, 20); revision = L.Revision;
        }
        dim ??= Solid(new Color(0, 0, 0, .45f)); background ??= Solid(new Color(.07f, .08f, .09f, .97f));
        input ??= Solid(new Color(.18f, .26f, .34f, 1)); buttonFill ??= Solid(new Color(.15f, .18f, .21f, 1)); buttonHover ??= Solid(new Color(.25f, .32f, .39f, 1));
        primary ??= Solid(new Color(.22f, .37f, .51f, 1)); primaryHover ??= Solid(new Color(.29f, .46f, .61f, 1));
        GUIStyle TextStyle(int size, Color color)
        {
            var style = new GUIStyle { font = font, fontSize = Mathf.RoundToInt(size * k), alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            style.normal.textColor = color; return style;
        }
        title = TextStyle(17, Palette.Text); title.fontStyle = FontStyle.Bold;
        hint = TextStyle(13, new Color(.73f, .76f, .79f, 1));
        field = TextStyle(17, Palette.Text); field.padding = new RectOffset((int)(9 * k), (int)(9 * k), 0, 0);
        field.normal.background = field.focused.background = field.hover.background = input;
        field.focused.textColor = field.hover.textColor = Palette.Text;
        GUIStyle ButtonStyle(Texture2D normal, Texture2D hover)
        {
            var style = TextStyle(15, Palette.Text); style.alignment = TextAnchor.MiddleCenter;
            style.normal.background = normal; style.hover.background = style.active.background = hover;
            style.hover.textColor = style.active.textColor = Palette.Text; return style;
        }
        plain = ButtonStyle(buttonFill!, buttonHover!); accent = ButtonStyle(primary!, primaryHover!);
    }

    private static Texture2D Solid(Color color)
    {
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave }; texture.SetPixel(0, 0, color); texture.Apply(); return texture;
    }

    public void Dispose()
    {
        foreach (var row in rows.Values)
        {
            if (row.Star != null) UnityEngine.Object.Destroy(row.Star.gameObject);
            if (row.Note != null) UnityEngine.Object.Destroy(row.Note.gameObject);
        }
        rows.Clear(); panels.Clear();
        if (font != null) UnityEngine.Object.Destroy(font);
        foreach (var texture in new[] { dim, background, input, buttonFill, buttonHover, primary, primaryHover }) if (texture != null) UnityEngine.Object.Destroy(texture);
    }
}
