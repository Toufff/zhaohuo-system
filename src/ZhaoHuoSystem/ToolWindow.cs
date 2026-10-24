using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mortal.Core;
using UnityEngine;

namespace ZhaoHuoSystem;

/// <summary>Game-native IMGUI surface. All game access belongs to IGameAdapter.</summary>
public sealed partial class ToolWindow : IDisposable
{
    private readonly IGameAdapter _adapter;
    private readonly Dictionary<string, ValueDraft> _drafts = new(StringComparer.Ordinal);
    private readonly List<Texture2D> _textures = new();
    private ToolSnapshot _snapshot = new();
    private bool _visible;
    private bool _initialized;
    private bool _clearFocusOnDraw;
    private float _scale = 1;
    private int _tab;
    private string _eventSearch = "";
    private string _valueSearch = "";
    private string _settingsSearch = "", _slotsText = "";
    private Vector2 _settingsScroll;
    private float _settingsHeight;
    private string _valueGroup = "all";
    private bool _mapOverview;
    private bool _eventViewChosen, _lastActionWaiting, _lastPreview;
    private float _valueRowWidth;
    private readonly Dictionary<string, RowMeasure> _rowMeasures = new(StringComparer.Ordinal);
    private string _mapLocation = "";
    private Vector2 _mapScroll;
    private long _languageRevision = -1;
    private string _selectedEvent = "";
    private string _context = "";
    private long _contextRevision = long.MinValue;
    private string _expandedValue = "";
    private bool _eligibleOnly;
    private Vector2 _eventScroll, _conditionScroll, _valueScroll, _collectionScroll;
    private string _collection = "achievement";
    private float _collectionConfirm;
    private List<ValueSnapshot>? _valueSource;
    private readonly List<ValueSnapshot> _filteredValues = new();
    private string _filteredSearch = "", _filteredGroup = "";
    // Groups that do match the search when the selected one is empty (F49).
    private readonly List<string> _searchElsewhere = new();
    private float _lastValueContentHeight, _lastValueViewportHeight;
    private string _message = "";
    private bool _messageError;
    private float _messageUntil;
    private bool _dragging, _resizing;
    private Vector2 _dragOrigin;
    private Rect _dragRect;
    private Font? _font;
    private bool _ownsFont;
    private GUIStyle _label = null!, _muted = null!, _title = null!, _heading = null!;
    private GUIStyle _button = null!, _primary = null!, _tabButton = null!, _tabSelected = null!, _launcher = null!;
    private GUIStyle _input = null!, _small = null!, _met = null!, _unmet = null!, _tooltip = null!, _metSmall = null!;
    private GUIStyle _listItem = null!, _listSelected = null!, _scrollbar = null!, _hscrollbar = null!, _caption = null!, _unmetSmall = null!;
    private Texture2D _dialogFill = null!, _jumpDialogAccent = null!, _cardFill = null!, _lineGrey = null!, _lineWarm = null!, _skinUnder = null!;
    private GUIStyle _smallLine = null!;
    private string _statusShown = "";
    private float _statusSince;
    private readonly Dictionary<GUIStyle, GUIStyle> _bare = new();
    private int _skinRevision = -2;
    private static int SkinKey => GameSkin.Enabled ? GameSkin.Revision : -1;
    // Fully satisfied events share one green treatment with the map markers.
    private GUIStyle _listReady = null!, _readySmall = null!;
    // Event consequences carry the mod's one colour meaning, so these styles render its marks as rich text.
    private GUIStyle _rich = null!, _richBody = null!, _richLine = null!;
    private Texture2D _background = null!, _line = null!, _section = null!, _selection = null!;
    private string _hoverText = "";
    private ValueSnapshot? _hoverEnum;
    private GUIStyle _enumLine = null!, _enumHeader = null!;
    private bool _hoverAllowed = true;
    private readonly Stack<bool> _hoverClips = new();

    public ToolWindow(IGameAdapter adapter) => _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));

    public bool Visible
    {
        get => _visible;
        set
        {
            // On the title screen the events and values wait for a game; the panel opens on 回顾 instead (F96).
            if (value && !_visible && _adapter.Snapshot.Title && _tab <= 1) _tab = 4;
            bool opening = value && !_visible;
            _visible = value;
            if (!value) { CancelInputCapture(); IsPointerOver = false; _dialog = JumpDialog.None; _closeWithDialog = false; }
            if (opening) MaybeShowFirstRun();
        }
    }
    /// <summary>Story and map scenes, the fights (F64: their 战斗 tab is what the panel is for there) and the title (F96).</summary>
    private static bool LauncherWanted(ToolSnapshot snapshot) => snapshot.Ready || snapshot.Title || CombatCheats.Current != CombatCheats.Mode.None;

    /// <summary>Physical screen coordinates, matching Mouse.current after converting Y.</summary>
    public Rect WindowRect { get; set; }
    public Rect LauncherRect { get; private set; }
    public bool IsPointerOver { get; private set; }
    public bool HasKeyboardFocus { get; private set; }
    public bool HasTextFocus { get; private set; }
    private bool _clearHotControlOnDraw;
    private int _ownedHotControl;
    /// <summary>Launcher position as a fraction of the free screen area; null keeps the default spot.</summary>
    public Vector2? LauncherAnchor { get; set; }
    public Action<Vector2>? LauncherMoved;
    /// <summary>Panel and hide hotkeys (KeyCode.None = unbound) and the screenshot-friendly hidden mode.</summary>
    public KeyCode PanelKey { get; private set; } = KeyCode.F8;
    public KeyCode HideKey { get; private set; } = KeyCode.None;
    /// <summary>Turns 全局加速 on and off (F102).</summary>
    public KeyCode SpeedKey { get; private set; } = KeyCode.None;
    public bool Hidden { get; private set; }
    /// <summary>The story-branch box alone (F45); every other overlay is unaffected.</summary>
    public bool ShowBranches { get; set; } = true;
    /// <summary>设置/回想中的剧情分支 (F106): 0 简略, 1 完整, 2 不显示.</summary>
    public int LogBranches { get; set; }
    /// <summary>The branch box goes back to its bottom-left corner (F68).</summary>
    public Action? BranchPlaceReset;
    /// <summary>The map's event counts and their hover lists alone (F58).</summary>
    public bool ShowMapCounts { get; set; } = true;
    /// <summary>「跳过本旬」 and 「本旬行动点无限」 beside the date, together (F62).</summary>
    public bool ShowRoundButtons { get; set; } = true;
    public bool ShowHiddenOptions { get; set; } = true;
    /// <summary>Manual save slots on the load and save pages (F51); a page built with another count builds them again when it opens (F90).</summary>
    public int SaveSlots { get; set; } = 50;
    /// <summary>设置/存档功能 (F105).</summary>
    public bool SaveFeatures { get; set; } = true;
    public const int MinSaveSlots = 20, MaxSaveSlots = 99;
    /// <summary>The pause after a shown stat change: 0 the game's 0.5 s, 1 short, 2 none (F95).</summary>
    public int StatPause { get; set; }
    /// <summary>The free-play action transitions at three times the speed (F95).</summary>
    public bool FastFreeTransitions { get; set; }
    public Action? SettingsChanged;
    /// <summary>Battle-tab toggles live in CombatCheats; this only reports a change so it can be saved.</summary>
    public Action? CombatChanged;
    /// <summary>Writes the diagnostics folder and returns its path.</summary>
    public Func<string>? ExportDiagnostics;
    private int _capturing; // 0 none, 1 panel key, 2 hide key, 3 speed key
    public bool CapturingKey => _capturing != 0;
    public void LoadSettings(KeyCode panelKey, KeyCode hideKey, bool hidden, KeyCode speedKey)
    {
        PanelKey = panelKey; HideKey = hideKey == panelKey ? KeyCode.None : hideKey;
        SpeedKey = speedKey == PanelKey || speedKey == HideKey ? KeyCode.None : speedKey;
        Hidden = hidden && (PanelKey != KeyCode.None || HideKey != KeyCode.None);
    }
    /// <summary>Hides every floating mod surface. Refused when no hotkey could bring it back.</summary>
    public bool SetHidden(bool hidden)
    {
        if (hidden && PanelKey == KeyCode.None && HideKey == KeyCode.None) { Message(L.Text("请先绑定面板或隐藏快捷键"), true); return false; }
        Hidden = hidden;
        if (hidden) Visible = false;
        SettingsChanged?.Invoke();
        return true;
    }
    private bool _launcherDragged;
    private Vector2 _launcherPress, _launcherGrab;

    public void RequestInputBlur()
    {
        HasKeyboardFocus = HasTextFocus = false; _clearFocusOnDraw = true;
    }
    public void CancelInputCapture()
    {
        RequestInputBlur(); _dragging = _resizing = false; _clearHotControlOnDraw = true; _capturing = 0;
        if (_launcherDragged && LauncherAnchor.HasValue) LauncherMoved?.Invoke(LauncherAnchor.Value);
        _launcherDragged = false;
    }
    public void PrepareInputRegions()
    {
        float scale = UiScale.Current;
        if (Hidden) LauncherRect = default;
        else if (Visible)
        {
            if (WindowRect.width <= 0) WindowRect = new Rect(42 * scale, 58 * scale, 900 * scale, 620 * scale);
            ClampWindow(scale); LauncherRect = default;
        }
        else LauncherRect = LauncherWanted(_adapter.Snapshot) ? ComputeLauncherRect(scale) : default;
    }
    // While 全局加速 is on, its multiple shows on the launcher.
    private static string LauncherText => L.Text("赵活系统") + (GlobalSpeed.On ? " " + GlobalSpeed.Label(GlobalSpeed.Level) : "");
    private Rect ComputeLauncherRect(float scale)
    {
        float width = 116 * scale, height = 34 * scale, margin = 14 * scale;
        float freeX = Mathf.Max(0, Screen.width - width), freeY = Mathf.Max(0, Screen.height - height);
        // A fraction of the free area survives resolution changes and always stays on screen.
        if (LauncherAnchor is Vector2 anchor) return new Rect(Mathf.Clamp01(anchor.x) * freeX, Mathf.Clamp01(anchor.y) * freeY, width, height);
        // Keep below the game's date as well as its status bar. At larger display
        // heights the game's HUD keeps scaling after this panel's font scale caps.
        float top = Mathf.Max(220 * scale, Screen.height * (220f / 1080f));
        return new Rect(margin, Mathf.Min(top, Screen.height - height - margin), width, height);
    }
    private void ApplyPendingBlur()
    {
        if (_clearFocusOnDraw) { if (OwnFocus()) GUI.FocusControl(null); _clearFocusOnDraw = false; }
        if (_clearHotControlOnDraw) { if (_ownedHotControl != 0 && GUIUtility.hotControl == _ownedHotControl) GUIUtility.hotControl = 0; _ownedHotControl = 0; _clearHotControlOnDraw = false; }
    }

    public void ShowEvents(bool currentAction = false)
    {
        _mapOverview = !currentAction && _adapter.Snapshot.MapLocations.Count > 0;
        _eventViewChosen = true;
        _tab = 0; Visible = true; _clearFocusOnDraw = true;
    }

    public void RefreshPresentation()
    {
        // IDs, drafts, focus and scroll belong to the user, never to translated labels.
        _languageRevision = -1;
        _valueSource = null;
        _reviewBuilt = -1; _treeDirty = true; Review?.ForgetReading();
        foreach (var pair in _drafts)
            if (pair.Value.ErrorTemplate.Length > 0) pair.Value.Error = L.Format(pair.Value.ErrorTemplate, pair.Value.ErrorArgs);
    }

    /// <summary>Closed-panel mouse entry. Plugin owns game input guarding when it opens.</summary>
    public bool DrawLauncher()
    {
        ApplyPendingBlur();
        LauncherRect = default;
        if (Visible || Hidden) return false;
        ReadSnapshot();
        IsPointerOver = false; HasKeyboardFocus = HasTextFocus = false;
        if (!LauncherWanted(_snapshot)) return false;

        float scale = UiScale.Current;
        if (_background == null || Math.Abs(scale - _scale) > .02f || _languageRevision != L.Revision || _skinRevision != SkinKey) BuildStyles(scale);
        LauncherRect = ComputeLauncherRect(scale);
        IsPointerOver = LauncherRect.Contains(Event.current.mousePosition);

        Color oldColor = GUI.color, oldContent = GUI.contentColor, oldBackground = GUI.backgroundColor;
        bool oldEnabled = GUI.enabled;
        int oldDepth = GUI.depth;
        try
        {
            GUI.depth = -10000;
            GUI.color = GUI.contentColor = GUI.backgroundColor = Color.white;
            GUI.enabled = true;
            if (OwnFocus()) GUI.FocusControl(null);
            return LauncherInput(scale);
        }
        finally
        {
            GUI.color = oldColor; GUI.contentColor = oldContent; GUI.backgroundColor = oldBackground;
            GUI.enabled = oldEnabled; GUI.depth = oldDepth;
        }
    }

    public void Draw()
    {
        if (!Visible)
        {
            if (OwnFocus()) GUI.FocusControl(null);
            return;
        }

        float scale = UiScale.Current;
        if (_background == null || Math.Abs(scale - _scale) > .02f || _languageRevision != L.Revision || _skinRevision != SkinKey)
        {
            // A new scale resizes the open window with its content instead of overflowing it.
            if (_initialized && _background != null && Math.Abs(scale - _scale) > .02f)
                WindowRect = new Rect(WindowRect.x, WindowRect.y, WindowRect.width * scale / _scale, WindowRect.height * scale / _scale);
            BuildStyles(scale);
        }
        if (!_initialized)
        {
            if (WindowRect.width <= 0) WindowRect = new Rect(S(42), S(58), S(900), S(620));
            _initialized = true;
        }
        ClampWindow();
        ApplyPendingBlur();
        var current = Event.current;
        if (_capturing != 0 && current.type == EventType.KeyDown && current.keyCode != KeyCode.None)
        {
            // The next ordinary key becomes the binding; Esc cancels. One key never drives both actions.
            if (current.keyCode == KeyCode.Escape) _capturing = 0;
            else if (NativeUiState.IsBindableKey(current.keyCode.ToString()))
            {
                var key = current.keyCode;
                if (PanelKey == key) PanelKey = KeyCode.None;
                if (HideKey == key) HideKey = KeyCode.None;
                if (SpeedKey == key) SpeedKey = KeyCode.None;
                if (_capturing == 1) PanelKey = key; else if (_capturing == 2) HideKey = key; else SpeedKey = key;
                _capturing = 0; SettingsChanged?.Invoke();
            }
            else Message(L.Text("该按键不可用"), true);
            current.Use();
        }
        HandleWindowDrag(current);
        IsPointerOver = WindowRect.Contains(current.mousePosition);

        ReadSnapshot();

        Color oldColor = GUI.color, oldContent = GUI.contentColor, oldBackground = GUI.backgroundColor;
        bool oldEnabled = GUI.enabled;
        int oldDepth = GUI.depth;
        _hoverText = ""; _hoverEnum = null;
        _hoverAllowed = true;
        try
        {
            GUI.depth = -10000;
            GUI.color = GUI.contentColor = GUI.backgroundColor = Color.white;
            GUI.enabled = true;
            // The game's window is see-through; a dark layer under it keeps the text clear over bright scenes (F99).
            GUI.DrawTexture(WindowRect, GameSkin.Has(GameSkin.Part.Window) ? _skinUnder : _background);
            GameSkin.Draw(WindowRect, GameSkin.Part.Window, _scale, Color.white);
            GUI.BeginGroup(WindowRect);
            try
            {
                float width = WindowRect.width, height = WindowRect.height;
                // An open jump dialog takes every press; what lies behind it is drawn but does not react (F98).
                bool modal = _dialog != JumpDialog.None;
                // Nor shows its hints over the dialog.
                if (modal) { GUI.enabled = false; _hoverAllowed = false; _languageOpen = false; }
                else LanguageInput(current);
                DrawHeader(width);
                DrawTabs(width);
                Rect body = new(S(14), S(94), width - S(28), height - S(134));
                switch (_tab)
                {
                    case 0: DrawEvents(body); break;
                    case 2: DrawCombat(body); break;
                    case 3: DrawCollection(body); break;
                    case 4: DrawReview(body); break;
                    case 5: DrawSettings(body); break;
                    default: DrawValues(body); break;
                }
                DrawStatus(new Rect(S(14), height - S(33), width - S(42), S(25)));
                GUI.DrawTexture(new Rect(0, height - S(40), width, 1), _line);
                // Two small strokes describe the actual resize handle.
                GUI.DrawTexture(new Rect(width - S(16), height - S(7), S(10), 1), _line);
                GUI.DrawTexture(new Rect(width - S(11), height - S(12), S(5), 1), _line);
                GUI.enabled = true; _hoverAllowed = true;
                DrawLanguageMenu();
                if (modal) DrawDialog(width, height);
            }
            finally { GUI.EndGroup(); }
            if (GUIUtility.hotControl == 0) _ownedHotControl = 0;
            else if (IsPointerOver && current.type != EventType.Layout && current.type != EventType.Repaint) _ownedHotControl = GUIUtility.hotControl;
            HasKeyboardFocus = OwnFocus();
            // Waiting for a new hotkey isolates the keyboard exactly like text editing.
            HasTextFocus = InputIsolationPolicy.IsTextControl(GUI.GetNameOfFocusedControl()) || _capturing != 0;
            DrawTooltip(current.mousePosition);

            // Native input gates and a local raycast shield protect the same region.
            if (IsPointerOver && (current.type == EventType.ScrollWheel || current.type == EventType.MouseDown || current.type == EventType.MouseUp)) current.Use();
        }
        finally
        {
            GUI.color = oldColor; GUI.contentColor = oldContent; GUI.backgroundColor = oldBackground;
            GUI.enabled = oldEnabled; GUI.depth = oldDepth;
        }
    }

    // The mod's own version right after the title (F97); hovering either shows the game's build.
    private static readonly string ModVersion = "v" + typeof(ToolWindow).Assembly.GetName().Version.ToString(3);
    private void DrawHeader(float width)
    {
        string close = PanelKey == KeyCode.None ? L.Text("收起") : L.Format("收起 {0}", KeyLabel(PanelKey));
        float closeWidth = ButtonWidth(close, 93), refreshWidth = ButtonWidth("刷新", 65);
        float titleWidth = _brandTitle.CalcSize(new GUIContent(L.Text("赵活系统"))).x, versionWidth = _small.CalcSize(new GUIContent(ModVersion)).x + S(8);
        Label(new Rect(S(14), S(5), titleWidth + S(4), S(34)), "赵活系统", _brandTitle, _snapshot.Version);
        Label(new Rect(S(14) + titleWidth + S(10), S(9), versionWidth, S(28)), ModVersion, _small, _snapshot.Version);
        float refreshX = width - closeWidth - refreshWidth - S(22), languageWidth = LanguageButtonWidth();
        DrawLanguageButton(new Rect(refreshX - languageWidth - S(8), S(8), languageWidth, S(28)));
        if (Button(new Rect(refreshX, S(8), refreshWidth, S(28)), "刷新", "refresh")) Refresh();
        if (Button(new Rect(width - closeWidth - S(14), S(8), closeWidth, S(28)), close, "close"))
        {
            GUI.FocusControl(null);
            Visible = false;
        }
        GUI.DrawTexture(new Rect(0, S(43), width, 1), _line);
    }

    private void DrawTabs(float width)
    {
        // 设置 always stays the last tab.
        string[] names = { "事件", "数值", "战斗", "收集", "回顾", "设置" };
        for (int i = 0; i < names.Length; i++)
        {
            Rect r = new(S(14 + i * 95), S(51), S(86), S(32));
            if (Button(r, names[i], "tab" + i, _tab == i ? _tabSelected : _tabButton))
            {
                _tab = i; _capturing = 0;
                // The review page reads the free slot and its list again each time it is opened.
                if (i == 4) { _reviewBuilt = -1; _treeDirty = true; Review?.ForgetReading(); }
                GUI.FocusControl(null);
            }
        }
        GUI.DrawTexture(new Rect(S(14), S(87), width - S(28), 1), _line);
    }

    private void DrawEvents(Rect r)
    {
        float mapWidth = ButtonWidth("当前地图", 95), actionWidth = ButtonWidth("当前行动", 95);
        if (Button(new Rect(r.x, r.y, actionWidth, S(29)), "当前行动", "event.action", !_mapOverview ? _tabSelected : _tabButton)) _mapOverview = false;
        if (Button(new Rect(r.x + actionWidth + S(6), r.y, mapWidth, S(29)), "当前地图", "event.map", _mapOverview ? _tabSelected : _tabButton)) _mapOverview = true;
        r.y += S(39); r.height -= S(39);
        if (_mapOverview) { DrawMapOverview(r); return; }
        bool knownLocation = _snapshot.Ready && !string.IsNullOrWhiteSpace(_snapshot.Location);
        bool previewing = Previewing;
        string context = knownLocation ? _snapshot.Location : "尚未读取当前位置";
        if (knownLocation && !string.IsNullOrWhiteSpace(_snapshot.Action)) context += " · " + _snapshot.Action;
        if (knownLocation && previewing) context += " · " + L.Format("预览：{0}", _snapshot.PreviewTitle);
        Label(new Rect(r.x, r.y, r.width, S(28)), context, _heading, _snapshot.ContextDetail);
        Rect bottom = new(r.x, r.yMax - S(43), r.width, S(36));
        DrawEventPending(bottom);
        if (!knownLocation)
        {
            Empty(new Rect(r.x, r.y + S(48), r.width, S(85)), "在游戏中选择行动后显示对应事件。");
            return;
        }

        float leftWidth = Mathf.Min(S(232), r.width * .33f);
        GUI.SetNextControlName("zh.event.search");
        _eventSearch = Search(new Rect(r.x, r.y + S(34), r.width - S(142), S(31)), _eventSearch, "搜索当前行动事件", "zh.event.search");
        Rect filter = new(r.xMax - S(132), r.y + S(34), S(132), S(31));
        if (Button(filter, "仅可触发", "eligible", _eligibleOnly ? _tabSelected : _button)) _eligibleOnly = !_eligibleOnly;
        Rect list = new(r.x, r.y + S(76), leftWidth, r.height - S(126));
        Rect detail = new(list.xMax + S(16), list.y, r.width - leftWidth - S(16), list.height);
        GUI.DrawTexture(new Rect(list.xMax + S(7), list.y, 1, list.height), _line);

        var items = new List<EventSnapshot>();
        var source = previewing ? _snapshot.PreviewEvents : _snapshot.Events;
        foreach (var item in source)
            if ((!_eligibleOnly || item.Eligible) && Matches(_eventSearch, item.Title, item.Id, item.Detail)) items.Add(item);
        // Events that can trigger now come first, each group in the pool's own order; 「随机」 stays above them all (F70).
        items = items.Where(item => item.Eligible).Concat(items.Where(item => !item.Eligible)).ToList();
        // While an action waits, the list itself is the choice: the selected row is what 继续本次行动 triggers, and the
        // 「随机」 row at the top (the default) leaves it to the game. Outside a wait, selecting only shows details.
        bool choosing = _snapshot.ActionWaiting && !previewing;
        EventSnapshot? selected = FindEvent(_selectedEvent);
        if (selected == null && !choosing)
        {
            selected = items.Count > 0 ? items[0] : null;
            _selectedEvent = selected?.Id ?? "";
        }
        if (items.Count == 0) Empty(list, source.Count == 0 ? "当前没有待选择的事件。" : "没有匹配的事件。");
        else
        {
            float randomHeight = choosing ? S(52) : 0;
            float viewWidth = list.width - S(18), listHeight = randomHeight;
            var rowHeights = new List<float>();
            foreach (var item in items)
            {
                float rowHeight = Mathf.Max(S(26), _label.CalcHeight(new GUIContent(item.Title), viewWidth - S(18))) + S(37) + (item.Consequences.Count > 0 ? S(20) : 0);
                rowHeights.Add(rowHeight); listHeight += rowHeight;
            }
            Rect view = new(0, 0, viewWidth, Mathf.Max(list.height, listHeight));
            _eventScroll = BeginScroll(list, _eventScroll, view);
            try
            {
                float y = 0;
                if (choosing)
                {
                    Rect random = new(0, 0, view.width, randomHeight - S(5));
                    if (Button(random, "", "event.random", _selectedEvent.Length == 0 ? _listSelected : _listItem)) ChooseEvent(null);
                    Label(new Rect(S(9), random.y + S(5), random.width - S(18), S(24)), "随机", _label);
                    Label(new Rect(S(9), random.y + S(27), random.width - S(18), S(20)), "按游戏原本的权重", _small);
                    y = randomHeight;
                }
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    float consequenceHeight = item.Consequences.Count > 0 ? S(20) : 0, titleHeight = rowHeights[i] - S(37) - consequenceHeight;
                    Rect row = new(0, y, view.width, rowHeights[i] - S(5));
                    if (Button(row, "", "event." + item.Id, item.Id == _selectedEvent ? _listSelected : item.Eligible ? _listReady : _listItem, EventDetail(item)))
                    {
                        selected = item;
                        if (choosing) ChooseEvent(item); else { _selectedEvent = item.Id; _conditionScroll = Vector2.zero; }
                    }
                    Label(new Rect(S(9), row.y + S(5), row.width - S(18), titleHeight), item.Title, _label, EventDetail(item));
                    // One overview line, cut to the row like the option brackets; the detail pane has everything.
                    if (consequenceHeight > 0) GUI.Label(new Rect(S(9), row.y + S(5) + titleHeight, row.width - S(18), consequenceHeight), EventLine(item, row.width - S(18)), _richLine);
                    string state = item.Id == _snapshot.PendingEventId ? "本次触发" : item.Eligible ? "可触发" : "条件未满足";
                    Label(new Rect(S(9), row.y + S(7) + titleHeight + consequenceHeight, row.width - S(18), S(22)), state, item.Eligible ? _readySmall : _small);
                    y += rowHeights[i];
                }
            }
            finally { EndScroll(); }
        }
        if (selected == null)
        {
            if (!choosing) return;
            Label(new Rect(detail.x, detail.y, detail.width, S(36)), "随机", _heading);
            Label(new Rect(detail.x, detail.y + S(42), detail.width, S(60)), "继续本次行动时由游戏按权重决定触发哪个事件。", _muted);
            return;
        }
        float contentWidth = detail.width - S(18);
        float headingHeight = Mathf.Max(S(36), _heading.CalcHeight(new GUIContent(selected.Title), contentWidth) + S(6));
        float contentHeight = headingHeight + S(9) + Consequences(selected, contentWidth, 0, false);
        foreach (var condition in selected.Conditions) contentHeight += ConditionHeight(condition, contentWidth);
        if (selected.Conditions.Count == 0) contentHeight += S(30);
        _conditionScroll = BeginScroll(detail, _conditionScroll, new Rect(0, 0, contentWidth, Mathf.Max(detail.height, contentHeight)));
        try
        {
            Label(new Rect(0, 0, contentWidth, headingHeight), selected.Title, _heading, EventDetail(selected));
            float y = headingHeight + S(9);
            y += Consequences(selected, contentWidth, y, true);
            if (selected.Conditions.Count == 0) Label(new Rect(0, y, contentWidth, S(30)), "无额外条件", _muted);
            foreach (var condition in selected.Conditions) DrawCondition(condition, 0, ref y, contentWidth);
        }
        finally { EndScroll(); }
    }

    private static string Toned(string text) => NativeUiState.ToneColors(text, Tone.PositiveOnDark, Tone.NegativeOnDark);
    /// <summary>The list row's one-line overview, leading segments first, closed by 「…」 when cut.</summary>
    private string EventLine(EventSnapshot item, float width)
    {
        var segments = item.Consequences.ConvertAll(segment => new KeyValuePair<string, int>(segment.Summary, segment.State));
        var fit = NativeUiState.FitSegments(segments, list => _richLine.CalcSize(new GUIContent(Toned(string.Join("；", list.ConvertAll(pair => pair.Key))))).x, width, out _);
        return Toned(string.Join("；", fit.ConvertAll(pair => pair.Key)));
    }
    /// <summary>
    /// The detail pane's 「触发后」 block, laid out like the option hover (condition line, then 「→ effects」 indented),
    /// followed by the 「触发条件」 heading. Returns its height; draws only when asked.
    /// </summary>
    private float Consequences(EventSnapshot item, float width, float top, bool draw)
    {
        if (item.Consequences.Count == 0) return 0;
        float y = top;
        if (draw) Label(new Rect(0, y, width, S(24)), "触发后", _muted);
        y += S(26);
        foreach (var segment in item.Consequences)
        {
            string head = Toned(segment.Head);
            float h = _rich.CalcHeight(new GUIContent(head), width - S(8));
            if (draw) GUI.Label(new Rect(S(8), y, width - S(8), h), head, _rich);
            y += h;
            if (segment.Body.Length > 0)
            {
                string body = Toned(segment.Body);
                float b = _richBody.CalcHeight(new GUIContent(body), width - S(26));
                if (draw) GUI.Label(new Rect(S(26), y, width - S(26), b), body, _richBody);
                y += b;
            }
            y += S(4);
        }
        y += S(8);
        if (draw) Label(new Rect(0, y, width, S(24)), "触发条件", _muted);
        y += S(28);
        return y - top;
    }

    /// <summary>While an action waits: choosing a row makes it this action's event; null (the 「随机」 row) leaves it to the game.
    /// A row whose conditions are not met can be read, but the action stays random.</summary>
    // F115: 「设为 N」 on the viewed row's condition: once the row can run, it becomes this action's event.
    private string _specifyWhenMet = "";
    private void ChooseEvent(EventSnapshot? item)
    {
        _selectedEvent = item?.Id ?? ""; _conditionScroll = Vector2.zero; _specifyWhenMet = "";
        if (item != null && item.Eligible) { if (_snapshot.PendingEventId != item.Id) Execute(() => _adapter.SpecifyEvent(item.Id)); }
        else if (_snapshot.PendingEventId.Length > 0) Execute(_adapter.CancelEvent);
    }

    private void DrawEventPending(Rect r)
    {
        bool waiting = _snapshot.ActionWaiting && !Previewing;
        if (!waiting)
        {
            Label(new Rect(r.x, r.y, r.width, r.height), "行动停下时，在列表中选中事件再继续，就会触发它。", _muted);
            return;
        }
        var viewed = FindEvent(_selectedEvent);
        if (_specifyWhenMet.Length > 0 && _specifyWhenMet == _selectedEvent && viewed != null && viewed.Eligible && _snapshot.PendingEventId.Length == 0)
        { _specifyWhenMet = ""; Execute(() => _adapter.SpecifyEvent(viewed.Id)); }
        var pending = FindEvent(_snapshot.PendingEventId);
        string name = pending != null ? pending.Title : viewed != null && !viewed.Eligible ? L.Format("随机（{0} 条件未满足）", viewed.Title) : L.Text("随机");
        float continueWidth = S(141), repeatWidth = ButtonWidth("重复上次选择", 140);
        Label(new Rect(r.x, r.y, r.width - continueWidth - repeatWidth - S(22), r.height), L.Format("本次触发：{0}", name), _label, name);
        // The event chosen last time at this same action, chosen again and continued in one click.
        var repeat = _snapshot.RepeatEventId.Length > 0 ? FindEvent(_snapshot.RepeatEventId) : null;
        string tip = repeat == null ? L.Text("这个行动还没有选过事件") : repeat.Eligible ? L.Format("上次选择：{0}", repeat.Title) : L.Format("上次选择：{0}（现在条件未满足）", repeat.Title);
        bool previous = GUI.enabled;
        GUI.enabled = repeat != null && repeat.Eligible && _snapshot.Ready;
        if (Button(new Rect(r.xMax - continueWidth - S(10) - repeatWidth, r.y, repeatWidth, S(33)), "重复上次选择", "event.repeat", _button, tip))
        {
            var result = Execute(_adapter.RepeatLastChoice);
            if (result.Success) { GUI.FocusControl(null); Visible = false; }
        }
        GUI.enabled = previous;
        if (Button(new Rect(r.xMax - continueWidth, r.y, continueWidth, S(33)), "继续本次行动", "event.continue", _primary))
        {
            var result = Execute(_adapter.ContinueAction);
            if (result.Success) { GUI.FocusControl(null); Visible = false; }
        }
    }

    private void DrawCondition(ConditionSnapshot condition, float x, ref float y, float width)
    {
        if (condition.Children.Count == 1)
        {
            DrawCondition(condition.Children[0], x, ref y, width);
            return;
        }
        if (condition.Children.Count > 0)
        {
            string op = condition.Operator.Equals("OR", StringComparison.OrdinalIgnoreCase) || condition.Operator == "或" ? "或" : "且";
            Label(new Rect(x, y, width - x, S(25)), op, _muted, condition.Detail);
            y += S(27);
            float start = y;
            foreach (var child in condition.Children) DrawCondition(child, x + S(16), ref y, width);
            GUI.DrawTexture(new Rect(x + S(3), start, 1, Mathf.Max(S(8), y - start - S(7))), _line);
            y += S(5);
            return;
        }
        bool correctable = CanCorrect(condition);
        float buttonWidth = correctable ? ButtonWidth(L.Format("设为 {0}", condition.SuggestedValue.GetValueOrDefault()), 94) : 0;
        float textWidth = Mathf.Max(S(40), width - x - buttonWidth - (correctable ? S(10) : 0));
        string text = string.IsNullOrWhiteSpace(condition.Text) ? condition.FieldId : condition.Text;
        float height = Mathf.Max(S(31), _label.CalcHeight(new GUIContent(text), textWidth) + S(9));
        string detail = condition.Detail;
        if (!string.IsNullOrEmpty(condition.FieldId)) detail += (detail.Length > 0 ? "\n" : "") + condition.FieldId;
        Label(new Rect(x, y + S(3), textWidth, height - S(6)), text, condition.Known ? condition.Satisfied ? _met : _unmet : _muted, detail);
        if (correctable && Button(new Rect(width - buttonWidth, y, buttonWidth, S(29)), L.Format("设为 {0}", condition.SuggestedValue.GetValueOrDefault()), "condition." + condition.FieldId))
        {
            string id = condition.FieldId, input = condition.SuggestedValue!.Value.ToString(CultureInfo.InvariantCulture);
            if (Execute(() => _adapter.SetValue(id, input)).Success && _snapshot.ActionWaiting) _specifyWhenMet = _selectedEvent;
        }
        y += height + S(5);
    }

    private float ConditionHeight(ConditionSnapshot condition, float width)
    {
        if (condition.Children.Count == 1) return ConditionHeight(condition.Children[0], width);
        if (condition.Children.Count > 0)
        {
            float total = S(32);
            foreach (var child in condition.Children) total += ConditionHeight(child, width - S(16));
            return total;
        }
        float button = CanCorrect(condition) ? S(104) : 0;
        return Mathf.Max(S(31), _label.CalcHeight(new GUIContent(condition.Text), Mathf.Max(S(40), width - button)) + S(9)) + S(5);
    }

    private bool CanCorrect(ConditionSnapshot condition)
    {
        if (condition.Satisfied || !condition.SuggestedValue.HasValue || string.IsNullOrEmpty(condition.FieldId)) return false;
        var value = FindValue(condition.FieldId);
        return value != null && value.Writable && value.Kind != "boolean" && value.Kind != "bool" && value.Options.Count == 0
            && condition.SuggestedValue.Value >= value.Min && condition.SuggestedValue.Value <= value.Max;
    }

    // 设置: a search over the names and notes (F91); the rows scroll when the window is too short for them all.
    private void DrawSettings(Rect area)
    {
        string searched = _settingsSearch;
        _settingsSearch = Search(new Rect(area.x, area.y, area.width, S(32)), _settingsSearch, "搜索设置名称或说明", "zh.settings.search");
        if (_settingsSearch != searched) _settingsScroll = Vector2.zero;
        Rect rows = new(area.x, area.y + S(44), area.width, area.height - S(44));
        float width = rows.width - S(18);
        _settingsScroll = BeginScroll(rows, _settingsScroll, new Rect(0, 0, width, Mathf.Max(rows.height, _settingsHeight)));
        try { _settingsHeight = SettingsRows(new Rect(0, 0, width, rows.height)); }
        finally { EndScroll(); }
    }

    /// <summary>A setting's name; two lines when its language needs them (F119).</summary>
    private void SettingName(Rect r, string text)
    {
        _nameHeight = Mathf.Max(S(32), TextHeight(_label, L.Text(text), r.width));
        Label(new Rect(r.x, r.y, r.width, _nameHeight), text, _label);
    }
    private float _nameHeight;
    /// <summary>A note beside a setting's control, as tall as its text; the row's height, at least the old fixed one (F119).</summary>
    private float SettingNote(Rect r, string note, float least)
    {
        float height = TextHeight(_muted, L.Text(note), r.width);
        Label(new Rect(r.x, r.y, r.width, height), note, _muted);
        float row = Mathf.Max(S(least), height + S(11), _nameHeight + S(8));
        _nameHeight = 0;
        return row;
    }

    private float SettingsRows(Rect r)
    {
        float y = r.y, labelWidth = Mathf.Min(S(220), r.width * .36f), x = r.x + labelWidth + S(10);
        string query = _settingsSearch.Trim();
        // A row stays while its name or note holds the words, as shown in this language or in 简体.
        bool Shown(params string[] texts) => query.Length == 0 || texts.Any(text => Matches(query, L.Text(text), text));
        // F110: the overlays that give the story away, all on or all off in one place; each still has its own row below.
        string note = "剧情分支小框、地图事件数字、显示被隐藏的选项、回想中的剧情分支，一次全开或全关；下面也能分别设置。";
        if (Shown("剧透类显示", note))
        {
            SettingName(new Rect(r.x, y, labelWidth, 0), "剧透类显示");
            int spoilers = SpoilerState();
            if (Button(new Rect(x, y, S(110), S(30)), spoilers switch { 2 => "全部开启", 0 => "全部关闭", _ => "部分开启" }, "settings.spoilers", spoilers == 0 ? _button : _tabSelected))
                SetSpoilers(spoilers == 0);
            y += SettingNote(new Rect(x + S(124), y + S(3), Mathf.Max(S(80), r.xMax - x - S(124)), 0), note, 52);
        }
        // Pausing each action for a choice is the one gameplay switch here; on by default, saved like the rest.
        note = "行动时先停下，让你选这次触发的事件；关闭后按游戏原本随机。";
        if (Shown("行动前选择结果", note))
        {
            SettingName(new Rect(r.x, y, labelWidth, 0), "行动前选择结果");
            if (Button(new Rect(x, y, S(110), S(30)), _snapshot.ActionSelectionEnabled ? "开启" : "关闭", "settings.selection", _snapshot.ActionSelectionEnabled ? _tabSelected : _button))
                Execute(() => _adapter.SetActionSelection(!_snapshot.ActionSelectionEnabled));
            y += SettingNote(new Rect(x + S(124), y + S(3), Mathf.Max(S(80), r.xMax - x - S(124)), 0), note, 52);
        }
        // Size of every mod surface on top of the automatic screen-height scale.
        float step = ButtonWidth("−", 44);
        if (Shown("界面缩放"))
        {
            SettingName(new Rect(r.x, y, labelWidth, 0), "界面缩放");
            if (Button(new Rect(x, y, step, S(30)), "−", "settings.scale.down")) SetScale(UiScale.User - .1f);
            Label(new Rect(x + step + S(8), y, S(70), S(32)), L.Format("{0}%", Mathf.RoundToInt(UiScale.User * 100)), _label);
            if (Button(new Rect(x + step + S(84), y, step, S(30)), "+", "settings.scale.up")) SetScale(UiScale.User + .1f);
            if (Button(new Rect(x + step * 2 + S(94), y, ButtonWidth("恢复", 70), S(30)), "恢复", "settings.scale.reset")) SetScale(1f);
            y += S(44);
        }
        // F99: the game's own frames and serif, or the plain look this panel had before.
        note = "用游戏自己的边框、按钮和字体绘制面板；「简洁」是原来的纯色样式。素材取自游戏已载入的资源，个别部位要进过一次剧情或地图后才换上。";
        if (Shown("界面风格", note))
        {
            SettingName(new Rect(r.x, y, labelWidth, 0), "界面风格");
            if (Button(new Rect(x, y, S(110), S(30)), GameSkin.Enabled ? "游戏素材" : "简洁", "settings.skin", GameSkin.Enabled ? _tabSelected : _button))
            {
                GameSkin.Enabled = !GameSkin.Enabled; SettingsChanged?.Invoke();
            }
            y += SettingNote(new Rect(x + S(124), y + S(3), Mathf.Max(S(80), r.xMax - x - S(124)), 0), note, 52);
        }
        // Only the story-branch box; 隐藏全部悬浮窗 below still hides everything.
        note = "剧情按条件分支时弹出，可拖到别处并记住位置；关闭后不再弹出。";
        if (Shown("显示剧情分支小框", "重置位置", note))
        {
            SettingName(new Rect(r.x, y, labelWidth, 0), "显示剧情分支小框");
            if (Button(new Rect(x, y, S(110), S(30)), ShowBranches ? "开启" : "关闭", "settings.branches", ShowBranches ? _tabSelected : _button))
            {
                ShowBranches = !ShowBranches; SettingsChanged?.Invoke();
            }
            float resetWidth = ButtonWidth("重置位置", 96);
            if (Button(new Rect(x + S(118), y, resetWidth, S(30)), "重置位置", "settings.branches.reset", tooltip: L.Text("把小框放回左下角"))) BranchPlaceReset?.Invoke();
            float branchDetailX = x + S(118) + resetWidth + S(14);
            y += SettingNote(new Rect(branchDetailX, y + S(3), Mathf.Max(S(80), r.xMax - branchDetailX), 0), note, 52);
        }
        // F106: 回想's branch lines, apart from the box.
        note = "回想里记下每次剧情分支：简略为一行，鼠标移上去看完整条件；完整为原来的多行；也可以不显示。";
        if (Shown("回想中的剧情分支", note))
        {
            SettingName(new Rect(r.x, y, labelWidth, 0), "回想中的剧情分支");
            string[] modes = { "简略", "完整", "不显示" };
            float modeWidth = modes.Max(text => ButtonWidth(text, 64)), modeX = x;
            for (int i = 0; i < modes.Length; i++, modeX += modeWidth + S(6))
                if (Button(new Rect(modeX, y, modeWidth, S(30)), modes[i], "settings.logbranches." + i, LogBranches == i ? _tabSelected : _button))
                { LogBranches = i; SettingsChanged?.Invoke(); }
            y += SettingNote(new Rect(modeX + S(8), y + S(3), Mathf.Max(S(80), r.xMax - modeX - S(8)), 0), note, 52);
        }
        // F107: a button under 议题 / 走访 menus naming the options the game hid; never changes the menu.
        note = "议题、走访等菜单的标题旁显示「另有N个选项未满足条件」，点开看这些选项和各自的条件；菜单本身不变。";
        if (Shown("显示被隐藏的选项", note))
        {
            SettingName(new Rect(r.x, y, labelWidth, 0), "显示被隐藏的选项");
            if (Button(new Rect(x, y, S(110), S(30)), ShowHiddenOptions ? "开启" : "关闭", "settings.hiddenoptions", ShowHiddenOptions ? _tabSelected : _button))
            {
                ShowHiddenOptions = !ShowHiddenOptions; SettingsChanged?.Invoke();
            }
            y += SettingNote(new Rect(x + S(124), y + S(3), Mathf.Max(S(80), r.xMax - x - S(124)), 0), note, 52);
        }
        // Only the map's counts (贴吧 36 楼); 隐藏全部悬浮窗 below still hides everything.
        note = "地图上地点旁的事件数字和悬停说明；关闭后不再显示，其他悬浮显示照常。";
        if (Shown("显示地图事件数字", note))
        {
            SettingName(new Rect(r.x, y, labelWidth, 0), "显示地图事件数字");
            if (Button(new Rect(x, y, S(110), S(30)), ShowMapCounts ? "开启" : "关闭", "settings.mapcounts", ShowMapCounts ? _tabSelected : _button))
            {
                ShowMapCounts = !ShowMapCounts; SettingsChanged?.Invoke();
            }
            y += SettingNote(new Rect(x + S(124), y + S(3), Mathf.Max(S(80), r.xMax - x - S(124)), 0), note, 52);
        }
        note = "骰子界面点「指定」后不播转盘、不等「继续」，直接出结果；结果仍由游戏结算，和真掷出来的一样。";
        if (Shown("指定后直接出结果", note))
        {
            SettingName(new Rect(r.x, y, labelWidth, 0), "指定后直接出结果");
            if (Button(new Rect(x, y, S(110), S(30)), _adapter.DirectDiceResult ? "开启" : "关闭", "settings.directdice", _adapter.DirectDiceResult ? _tabSelected : _button))
            {
                _adapter.DirectDiceResult = !_adapter.DirectDiceResult; SettingsChanged?.Invoke();
            }
            y += SettingNote(new Rect(x + S(124), y + S(3), Mathf.Max(S(80), r.xMax - x - S(124)), 0), note, 52);
        }
        note = "日期右边的「跳过本旬」和「本旬行动点无限」；关闭后两个都不显示，其他悬浮显示照常。";
        if (Shown("显示本旬按钮", note))
        {
            SettingName(new Rect(r.x, y, labelWidth, 0), "显示本旬按钮");
            if (Button(new Rect(x, y, S(110), S(30)), ShowRoundButtons ? "开启" : "关闭", "settings.roundbuttons", ShowRoundButtons ? _tabSelected : _button))
            {
                ShowRoundButtons = !ShowRoundButtons; SettingsChanged?.Invoke();
            }
            y += SettingNote(new Rect(x + S(124), y + S(3), Mathf.Max(S(80), r.xMax - x - S(124)), 0), note, 52);
        }
        // F112: the six may rise past the game's 100; the gauges keep reading 100 as full.
        note = "体力、内力、轻功、刀剑、暗器、拳掌的上限。改回 100 后，超过 100 的部分会在下次变动或读档时被游戏压回 100。";
        if (Shown("六维上限", note))
        {
            SettingName(new Rect(r.x, y, labelWidth, 0), "六维上限");
            float capX = x;
            foreach (int cap in StatRules.Caps)
            {
                string text = cap == 100 ? L.Text("100（原版）") : cap.ToString(CultureInfo.InvariantCulture);
                float capWidth = ButtonWidth(text, 64);
                if (Button(new Rect(capX, y, capWidth, S(30)), text, "settings.statcap." + cap, StatRules.Cap == cap ? _tabSelected : _button))
                { StatRules.Cap = cap; SettingsChanged?.Invoke(); }
                capX += capWidth + S(6);
            }
            // The three buttons leave little room beside them: the note takes its own line.
            y += S(38);
            float noteHeight = TextHeight(_muted, L.Text(note), r.xMax - x);
            Label(new Rect(x, y, r.xMax - x, noteHeight), note, _muted);
            y += noteHeight + S(14);
        }
        // F95: pacing the player can see stays the game's own unless chosen here.
        note = "剧情里数值变化弹出提示后停一下再继续；没有弹出提示的变化（变化为 0、隐藏的数值）一律不停。";
        if (Shown("数值变化停顿", note))
        {
            SettingName(new Rect(r.x, y, labelWidth, 0), "数值变化停顿");
            float pauseWidth = new[] { "0.5 秒（原版）", "0.2 秒", "不停顿" }.Max(text => ButtonWidth(text, 110));
            if (Button(new Rect(x, y, pauseWidth, S(30)), StatPause switch { 1 => "0.2 秒", 2 => "不停顿", _ => "0.5 秒（原版）" }, "settings.statpause", StatPause == 0 ? _button : _tabSelected))
            {
                StatPause = (StatPause + 1) % 3; SettingsChanged?.Invoke();
            }
            y += SettingNote(new Rect(x + pauseWidth + S(14), y + S(3), Mathf.Max(S(80), r.xMax - x - pauseWidth - S(14)), 0), note, 52);
        }
        // F102: the whole clock, fights included; the player turns it off if a fight goes wrong.
        note = "整个游戏按倍数变快：剧情出字、等待和动画，自由行动地图，回合制决斗和即时战斗都会变快，战斗里叠加在游戏自己的速度上，暂停照旧。背景音乐不变，自动播放仍留足阅读时间。即时战斗的敌人也会变快，不合适随时关掉。";
        if (Shown("全局加速", "加速快捷键", note))
        {
            SettingName(new Rect(r.x, y, labelWidth, 0), "全局加速");
            if (Button(new Rect(x, y, S(110), S(30)), GlobalSpeed.On ? "开启" : "关闭", "settings.speed", GlobalSpeed.On ? _tabSelected : _button)) { GlobalSpeed.On = !GlobalSpeed.On; SettingsChanged?.Invoke(); }
            float levelX = x + S(118), levelWidth = ButtonWidth("1.5×", 64);
            for (int i = 0; i < GlobalSpeed.Multiples.Length; i++)
                if (Button(new Rect(levelX + i * (levelWidth + S(6)), y, levelWidth, S(30)), GlobalSpeed.Label(i), "settings.speed." + i, GlobalSpeed.Level == i ? _tabSelected : _button))
                { GlobalSpeed.Level = i; SettingsChanged?.Invoke(); }
            y += S(40);
            float speedNote = TextHeight(_muted, L.Text(note), r.width);
            Label(new Rect(r.x, y, r.width, speedNote), note, _muted);
            y += Mathf.Max(S(72), speedNote + S(6));
            KeyRow(r.x, x, ref y, labelWidth, "加速快捷键", SpeedKey, 3);
        }
        note = "地点行动后的停顿、转场遮罩和回到地图时的淡入；加快后快 3 倍，约 3.5 秒缩到 1.2 秒。";
        if (Shown("自由行动过场", note))
        {
            SettingName(new Rect(r.x, y, labelWidth, 0), "自由行动过场");
            if (Button(new Rect(x, y, S(110), S(30)), FastFreeTransitions ? "加快" : "原版", "settings.freetransition", FastFreeTransitions ? _tabSelected : _button))
            {
                FastFreeTransitions = !FastFreeTransitions; SettingsChanged?.Invoke();
            }
            y += SettingNote(new Rect(x + S(124), y + S(3), Mathf.Max(S(80), r.xMax - x - S(124)), 0), note, 52);
        }
        // F105: one switch back to the game's own saving; nothing stored is removed, so turning it on again restores all.
        note = "剧情中随时存档、更多手动存档格、存档排序、收藏和备注。关闭后都按原版：读档、存档页只显示 20 格；第 21 格以后的存档、收藏和备注都还在，重新开启就恢复。";
        if (Shown("存档功能", note))
        {
            SettingName(new Rect(r.x, y, labelWidth, 0), "存档功能");
            if (Button(new Rect(x, y, S(110), S(30)), SaveFeatures ? "开启" : "原版", "settings.savefeatures", SaveFeatures ? _tabSelected : _button))
            {
                SaveFeatures = !SaveFeatures; SettingsChanged?.Invoke();
            }
            y += SettingNote(new Rect(x + S(124), y + S(3), Mathf.Max(S(80), r.xMax - x - S(124)), 0), note, 74);
        }
        // F90: typed, or ten at a time; a page builds its slots again the next time it opens.
        note = "读档、存档页的手动存档格数（20–99，游戏原为 20），可直接输入，下次打开读档或存档页时生效。调小或卸载模组后，多出的存档只是不显示，文件仍在。";
        if (Shown("手动存档数", note))
        {
            // Only counts while 存档功能 is on.
            bool slotsEnabled = GUI.enabled; GUI.enabled = slotsEnabled && SaveFeatures;
            SettingName(new Rect(r.x, y, labelWidth, 0), "手动存档数");
            float field = S(56);
            if (Button(new Rect(x, y, step, S(30)), "−", "settings.slots.down")) { GUI.FocusControl(null); SetSaveSlots(((SaveSlots + 9) / 10 - 1) * 10); }
            const string slotsField = "zh.settings.slots";
            if (GUI.GetNameOfFocusedControl() != slotsField) _slotsText = SaveSlots.ToString();
            GUI.SetNextControlName(slotsField);
            _slotsText = new string(InputField(new Rect(x + step + S(8), y, field, S(30)), _slotsText, 2).Where(char.IsDigit).ToArray());
            if (int.TryParse(_slotsText, out int typed) && typed >= MinSaveSlots && typed <= MaxSaveSlots && typed != SaveSlots) SetSaveSlots(typed);
            if (Button(new Rect(x + step + field + S(16), y, step, S(30)), "+", "settings.slots.up")) { GUI.FocusControl(null); SetSaveSlots((SaveSlots / 10 + 1) * 10); }
            float below = TextHeight(_muted, L.Text(note), r.width);
            Label(new Rect(r.x, y + S(38), r.width, below), note, _muted);
            GUI.enabled = slotsEnabled;
            y += Mathf.Max(S(88), S(46) + below);
        }
        if (Shown("面板快捷键")) KeyRow(r.x, x, ref y, labelWidth, "面板快捷键", PanelKey, 1);
        if (Shown("隐藏全部悬浮窗", "隐藏快捷键"))
        {
            SettingName(new Rect(r.x, y, labelWidth, 0), "隐藏全部悬浮窗");
            if (Button(new Rect(x, y, S(110), S(30)), Hidden ? "开启" : "关闭", "settings.hidden", Hidden ? _tabSelected : _button)) SetHidden(!Hidden);
            y += S(44);
            KeyRow(r.x, x, ref y, labelWidth, "隐藏快捷键", HideKey, 2);
            var keys = new List<string>();
            if (PanelKey != KeyCode.None) keys.Add(KeyLabel(PanelKey));
            if (HideKey != KeyCode.None) keys.Add(KeyLabel(HideKey));
            Label(new Rect(r.x, y + S(4), r.width, S(50)), keys.Count > 0 ? L.Format("隐藏后按 {0} 恢复显示。", string.Join(" / ", keys)) : L.Text("请先绑定面板或隐藏快捷键"), _muted);
            y += S(60);
        }
        note = "把最近 5 次游戏的日志、当前状态、配置和性能统计放进一个文件夹，路径会复制到剪贴板。";
        if (Shown("反馈诊断", "导出诊断", note))
        {
            SettingName(new Rect(r.x, y, labelWidth, 0), "反馈诊断");
            if (Button(new Rect(x, y, ButtonWidth("导出诊断", 110), S(30)), "导出诊断", "settings.export"))
            {
                try { Message(L.Format("已导出诊断：{0}（路径已复制）", ExportDiagnostics?.Invoke() ?? ""), false); }
                catch (Exception ex) { Message(L.Format("导出诊断失败：{0}", ex.GetBaseException().Message), true); }
            }
            float below = TextHeight(_muted, L.Text(note), r.width);
            Label(new Rect(r.x, y + S(38), r.width, below), note, _muted);
            y += Mathf.Max(S(88), S(46) + below);
        }
        if (y == r.y) Empty(r, L.Format("没有名称或说明含「{0}」的设置。", query));
        return y;
    }
    private void SetSaveSlots(int value)
    {
        value = Mathf.Clamp(value, MinSaveSlots, MaxSaveSlots);
        if (value == SaveSlots) return;
        SaveSlots = value; SettingsChanged?.Invoke();
    }
    // 收集: the game's 讲经堂 lists, one section at a time, with per-item and whole-section unlock (F42).
    private void DrawCollection(Rect r)
    {
        float x = r.x;
        foreach (string id in LibraryCollections.Sections)
        {
            var all = LibraryCollections.Items(id);
            string text = LibraryCollections.SectionName(id) + " " + all.Count(item => item.Count > 0) + "/" + all.Count;
            float width = Mathf.Max(S(95), _button.CalcSize(new GUIContent(text)).x + S(16));
            if (Button(new Rect(x, r.y, width, S(29)), text, "collection." + id, _collection == id ? _tabSelected : _tabButton))
            { _collection = id; _collectionScroll = Vector2.zero; _collectionConfirm = 0; }
            x += width + S(6);
        }
        r.y += S(39); r.height -= S(39);
        var items = LibraryCollections.Items(_collection);
        if (items.Count == 0) { Empty(r, "讲经堂数据未载入"); return; }
        // Unlocking a whole section asks once more; the question stands for a few seconds.
        bool confirming = Time.unscaledTime < _collectionConfirm;
        float allWidth = Mathf.Max(ButtonWidth("全部解锁", 95), ButtonWidth("确定全部解锁？", 95));
        bool previous = GUI.enabled;
        GUI.enabled = previous && items.Any(item => item.Count == 0);
        if (Button(new Rect(r.xMax - allWidth, r.y, allWidth, S(29)), confirming ? "确定全部解锁？" : "全部解锁", "collection.all", confirming ? _primary : _button))
        {
            if (confirming) { _collectionConfirm = 0; Execute(() => LibraryCollections.Unlock(_collection, items)); }
            else _collectionConfirm = Time.unscaledTime + 3f;
        }
        GUI.enabled = previous;
        string note = _collection == "achievement" ? "解锁时会一并解锁对应的Steam成就，解锁后不能撤销；带奖励的成就在新开存档时生效。" : "解锁后在讲经堂里可以查看；记在全局存档里，不影响当前存档。";
        Label(new Rect(r.x, r.y, r.width - allWidth - S(12), S(46)), note, _muted);
        Rect rows = new(r.x, r.y + S(54), r.width, r.height - S(54));
        GUI.DrawTexture(new Rect(rows.x, rows.y - S(4), rows.width, 1), _line);
        bool awards = _collection == "achievement";
        float rowHeight = S(awards ? 54 : 36), contentWidth = rows.width - S(18), stateWidth = Mathf.Max(ButtonWidth("解锁", 74), ButtonWidth("已解锁", 74));
        _collectionScroll = BeginScroll(rows, _collectionScroll, new Rect(0, 0, contentWidth, Mathf.Max(rows.height, items.Count * rowHeight)));
        try
        {
            for (int i = 0; i < items.Count; i++)
            {
                float y = i * rowHeight;
                if (y + rowHeight < _collectionScroll.y || y > _collectionScroll.y + rows.height) continue;
                var item = items[i];
                string description = LibraryCollections.Description(item);
                Label(new Rect(S(8), y + S(4), contentWidth - stateWidth - S(24), S(28)), LibraryCollections.Title(item), _label, description);
                if (awards)
                {
                    string award = LibraryCollections.Award(item);
                    if (award.Length > 0) Label(new Rect(S(8), y + S(28), contentWidth - stateWidth - S(24), S(24)), award, _small, description);
                }
                var state = new Rect(contentWidth - stateWidth - S(4), y + S(3), stateWidth, S(28));
                if (item.Count > 0) Label(state, "已解锁", _met);
                else if (Button(state, "解锁", "collection.item." + item.name)) Execute(() => LibraryCollections.Unlock(_collection, new[] { item }));
                GUI.DrawTexture(new Rect(0, y + rowHeight - 1, contentWidth, 1), _line);
            }
        }
        finally { EndScroll(); }
    }

    // 剧情回顾 (F84): the kept starts of branch scripts, latest game date first; 存入 writes one into the slot above.
    internal StoryReview? Review;
    internal Func<StoryReview.Node, OperationResult>? ReviewStar;
    private sealed class ReviewRow { public string Header = "", Title = "", Summary = "", Detail = ""; public bool Described; public StoryReview.Node? Node; }
    private readonly List<ReviewRow> _reviewRows = new();
    private string _reviewSearch = "", _reviewBuiltSearch = "", _reviewStatus = "";
    private int _reviewBuilt = -1;
    private long _reviewLanguage = -1;
    private Vector2 _reviewScroll;

    private void DrawReview(Rect r)
    {
        if (Review == null) { Empty(r, "剧情回顾未载入"); return; }
        _reviewStatus = _snapshot.Title ? TitleSource : "";
        // 收录节点 | 剧情树 | 路线图 on the left; 说明 on the right, and the route map's own buttons beside it.
        float nodesWidth = ButtonWidth("收录节点", 95), treeWidth = ButtonWidth("剧情树", 95), routeWidth = ButtonWidth("路线图", 95);
        if (Button(new Rect(r.x, r.y, nodesWidth, S(29)), "收录节点", "review.nodes", !_treeMode && !_routeMode ? _tabSelected : _tabButton)) { _treeMode = false; _routeMode = false; }
        if (Button(new Rect(r.x + nodesWidth + S(6), r.y, treeWidth, S(29)), "剧情树", "review.tree", _treeMode ? _tabSelected : _tabButton)) { _treeMode = true; _routeMode = false; _treeDirty = true; }
        if (Button(new Rect(r.x + nodesWidth + treeWidth + S(12), r.y, routeWidth, S(29)), "路线图", "review.route", _routeMode ? _tabSelected : _tabButton)) { _routeMode = true; _treeMode = false; _routeSideKey = ""; }
        float help = ButtonWidth("说明", 70);
        if (Button(new Rect(r.xMax - help, r.y, help, S(29)), "说明", "review.help", tooltip: L.Text("跳转规则、怎么看路线图、各个按钮的作用"))) OpenHelp();
        if (_routeMode) RouteToolbar(r.y, r.x + nodesWidth + treeWidth + routeWidth + S(30), r.xMax - help - S(6));
        r.y += S(39); r.height -= S(39);
        if (_routeMode) { DrawRouteMap(r); return; }
        if (_treeMode) { DrawStoryTree(r); return; }
        _reviewSearch = Search(new Rect(r.x, r.y, r.width, S(32)), _reviewSearch, "搜索剧本、主线或选项", "zh.review.search");
        Label(new Rect(r.x, r.y + S(38), r.width, S(46)), "有选项、掷骰或战斗的剧情段，开头会自动收录，每段只留最新一份。点「跳转」选一个存档格，写好后直接进入这一段的开头。", _muted);
        if (_reviewBuilt != Review.Revision || _reviewBuiltSearch != _reviewSearch || _reviewLanguage != L.Revision) BuildReviewRows();
        Rect rows = new(r.x, r.y + S(92), r.width, r.height - S(92));
        GUI.DrawTexture(new Rect(rows.x, rows.y - S(4), rows.width, 1), _line);
        if (_reviewRows.Count == 0) { Empty(rows, _reviewSearch.Length > 0 ? "没有符合搜索的节点" : "还没有收录。进入有选项、掷骰或战斗的剧情段时会自动收录。"); return; }
        float header = S(32), row = S(58), contentWidth = rows.width - S(18), save = ButtonWidth("跳转", 74), star = S(34);
        _reviewScroll = BeginScroll(rows, _reviewScroll, new Rect(0, 0, contentWidth, Mathf.Max(rows.height, _reviewRows.Sum(item => item.Node == null ? header : row))));
        try
        {
            float y = 0;
            foreach (var item in _reviewRows)
            {
                float height = item.Node == null ? header : row;
                if (y + height >= _reviewScroll.y && y <= _reviewScroll.y + rows.height)
                {
                    if (item.Node is not StoryReview.Node node) Label(new Rect(S(4), y + S(5), contentWidth - S(8), S(24)), item.Header, _heading);
                    else
                    {
                        // Read marks are worked out only for rows that come into view.
                        if (!item.Described) { (item.Summary, item.Detail) = Review.Describe(node); item.Described = true; }
                        if (Button(new Rect(S(4), y + S(13), star, S(30)), node.Favourite ? "★" : "☆", "review.star." + node.Id, node.Favourite ? _primary : _button,
                            node.Favourite ? "取消收藏（删掉这一份）" : "收藏这一份，以后不会被新收录覆盖"))
                        {
                            Execute(() => ReviewStar?.Invoke(node) ?? OperationResult.Fail(L.Text("存档系统未就绪")));
                            break;
                        }
                        Label(new Rect(star + S(12), y + S(4), contentWidth - save - star - S(28), S(26)), item.Title, _label, item.Detail);
                        // One line; the whole of it is in the hover.
                        Label(new Rect(star + S(12), y + S(30), contentWidth - save - star - S(28), S(24)), item.Summary, _smallLine, item.Detail);
                        if (Button(new Rect(contentWidth - save - S(4), y + S(13), save, S(30)), "跳转", "review.jump." + node.Id, tooltip: L.Text("选一个存档格，写好后直接进入这一段")))
                        {
                            OpenJump(new JumpTarget { Title = item.Title, Node = node, Script = node.Script }, false);
                            break;
                        }
                        GUI.DrawTexture(new Rect(0, y + height - 1, contentWidth, 1), _line);
                    }
                }
                y += height;
            }
        }
        finally { EndScroll(); }
    }

    // 剧情树 (F87) as a folding tree: main missions, then each segment and the ways out of it. Opening a way analyses the
    // segment it leads into; a segment already laid out above reads 「→ 接到」 instead of opening again.
    internal StoryTree? Tree;
    /// <summary>On the title: which save the tree and the map read (F96); empty elsewhere.</summary>
    internal string TitleSource = "";
    /// <summary>The game's data changed under the review views (the title's 最近存档 was read).</summary>
    internal void ReviewDataChanged() { _treeDirty = true; _routeLayoutMap = null; _routeSideKey = ""; }
    private sealed class TreeRow { public int Depth; public string Key = "", Text = "", Status = "", Tooltip = ""; public bool CanOpen, Open, Header; public StoryReview.Node? Kept; }
    private readonly List<TreeRow> _treeRows = new();
    private readonly HashSet<string> _treeOpen = new(StringComparer.Ordinal);
    private bool _treeMode, _treeDirty = true;
    private long _treeLanguage = -1;
    private Vector2 _treeScroll;

    private void DrawStoryTree(Rect r)
    {
        if (Tree == null) { Empty(r, "剧情回顾未载入"); return; }
        string intro = L.Text("只列主线：按主线任务的触发日期排列，点开一层才分析下一层；上面已经展开过的段落写「→ 接到」。条件按当前存档的数值显示，未读、部分已读按全部周目读过的对白算。");
        Label(new Rect(r.x, r.y, r.width, S(46)), _snapshot.Title && TitleSource.Length > 0 ? TitleSource + "。" + intro : intro, _muted);
        if (_treeDirty || _treeLanguage != L.Revision) BuildTreeRows();
        Rect rows = new(r.x, r.y + S(54), r.width, r.height - S(54));
        GUI.DrawTexture(new Rect(rows.x, rows.y - S(4), rows.width, 1), _line);
        if (_treeRows.Count == 0) { Empty(rows, "主线任务数据未载入"); return; }
        float height = S(30), indent = S(18), contentWidth = rows.width - S(18), save = ButtonWidth("跳转", 74);
        _treeScroll = BeginScroll(rows, _treeScroll, new Rect(0, 0, contentWidth, Mathf.Max(rows.height, _treeRows.Count * height)));
        try
        {
            for (int i = 0; i < _treeRows.Count; i++)
            {
                float y = i * height;
                if (y + height < _treeScroll.y || y > _treeScroll.y + rows.height) continue;
                var row = _treeRows[i];
                float x = S(4) + row.Depth * indent, right = contentWidth - (row.Kept != null ? save + S(8) : 0);
                if (row.CanOpen && Button(new Rect(x, y + S(3), S(24), S(24)), row.Open ? "▾" : "▸", "tree.open." + row.Key, _tabButton))
                {
                    if (!_treeOpen.Remove(row.Key)) _treeOpen.Add(row.Key);
                    _treeDirty = true;
                    break;
                }
                string text = row.Text + (row.Status.Length > 0 ? "〔" + row.Status + "〕" : "");
                Label(new Rect(x + S(28), y + S(3), Mathf.Max(S(40), right - x - S(32)), S(26)), text, row.Header || row.Depth == 0 ? _label : _small, row.Tooltip.Length > 0 ? row.Tooltip : text);
                if (row.Kept is StoryReview.Node node && Button(new Rect(contentWidth - save - S(4), y + S(1), save, S(28)), "跳转", "tree.jump." + row.Key, tooltip: L.Text("选一个存档格，写好后直接进入这一段")))
                {
                    OpenJump(new JumpTarget { Title = row.Text, Node = node, Script = node.Script }, false);
                    break;
                }
            }
        }
        finally { EndScroll(); }
    }

    private void BuildTreeRows()
    {
        _treeRows.Clear();
        _treeDirty = false; _treeLanguage = L.Revision;
        var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = Review?.Latest() ?? new Dictionary<string, StoryReview.Node>();
        foreach (var root in Tree!.Roots())
        {
            string key = "m:" + root.Key;
            bool open = _treeOpen.Contains(key);
            _treeRows.Add(new TreeRow { Key = key, Text = root.Title, Tooltip = root.Key + " · " + root.Script, CanOpen = true, Open = open });
            if (open) AddSegment(Tree.Graph.Get(root.Script), 1, key, shown, kept);
        }
    }

    private void AddSegment(StoryGraph.Segment segment, int depth, string path, HashSet<string> shown, Dictionary<string, StoryReview.Node> kept)
    {
        if (!shown.Add(segment.Id)) { _treeRows.Add(new TreeRow { Depth = depth, Text = L.Format("→ 接到 {0}", segment.Id), Tooltip = StoryTree.SegmentTitle(segment) }); return; }
        kept.TryGetValue(segment.Last, out var node);
        _treeRows.Add(new TreeRow { Depth = depth, Header = true, Text = StoryTree.SegmentTitle(segment), Tooltip = string.Join(" → ", segment.Scripts), Kept = node });
        for (int i = 0; i < segment.Ways.Count; i++)
        {
            var way = segment.Ways[i];
            string key = path + "/" + segment.Id + "#" + i, end = StoryTree.WayEnd(way);
            bool follow = way.Target.Length > 0 && !way.Ends, open = follow && _treeOpen.Contains(key);
            string text = (i == segment.Ways.Count - 1 ? "└ " : "├ ") + StoryTree.WayLabel(way) + (follow ? " → " + way.Target : "") + (end.Length > 0 ? " → " + end : "");
            _treeRows.Add(new TreeRow { Depth = depth + 1, Key = key, Text = text, Status = Tree!.Reading(way), CanOpen = follow, Open = open });
            if (open) AddSegment(Tree.Graph.Get(way.Target), depth + 2, key, shown, kept);
        }
    }

    private void BuildReviewRows()
    {
        _reviewRows.Clear();
        _reviewBuilt = Review!.Revision; _reviewBuiltSearch = _reviewSearch; _reviewLanguage = L.Revision;
        int year = int.MinValue, month = int.MinValue;
        foreach (var node in Review.Nodes.OrderByDescending(n => n.Year).ThenByDescending(n => n.Month).ThenByDescending(n => n.Stage).ThenByDescending(n => n.Kept))
        {
            string title = StoryReview.Title(node);
            if (!Matches(_reviewSearch, title, StoryReview.SearchText(node))) continue;
            if (node.Year != year || node.Month != month) { year = node.Year; month = node.Month; _reviewRows.Add(new ReviewRow { Header = L.Format("{0}年{1}月", year, month) }); }
            _reviewRows.Add(new ReviewRow { Node = node, Title = (node.Favourite ? "★ " : "") + title });
        }
    }

    private void DrawCombat(Rect r)
    {
        float y = r.y, labelWidth = Mathf.Min(S(150), r.width * .3f), x = r.x + labelWidth + S(10), detailX = x + S(124);
        string mode = !CombatCheats.Installed ? "战斗开关未挂接，不会生效" : CombatCheats.Current switch
        {
            CombatCheats.Mode.Battle => "当前：战役中", CombatCheats.Mode.Combat => "当前：决斗中", _ => "当前：不在战斗中"
        };
        Label(new Rect(r.x, y, r.width, S(28)), mode, _muted);
        y += S(38);
        CombatRow(r, ref y, labelWidth, x, detailX, "无敌", ref CombatCheats.Invincible, "战役中主角与友军不受伤害；决斗中主角不掉血");
        CombatRow(r, ref y, labelWidth, x, detailX, "一击必杀", ref CombatCheats.OneHit, "敌人受到伤害即倒下");
        CombatRow(r, ref y, labelWidth, x, detailX, "气力不减", ref CombatCheats.KeepStamina, "主角的气不会被消耗");
        CombatRow(r, ref y, labelWidth, x, detailX, "技能无冷却", ref CombatCheats.NoCooldown, "战役技能用后立刻可再用");
        CombatRow(r, ref y, labelWidth, x, detailX, "双方最低 1 血", ref CombatCheats.KeepAlive, "仅决斗：双方的血都不会低于 1，谁都不会被打死，适合要拖回合的决斗");
        string note = L.Text("开关立即生效，只改战斗中的伤害与消耗，不写存档。") + "\n"
            + L.Text("剧情安排的扣血和保底血量照常生效；个别决斗要等主角掉血才推进，若迟迟不结束，打倒对手或关掉无敌即可。") + "\n"
            + L.Text("战役中鼠标被锁定时，先暂停游戏再点面板。");
        if (CombatCheats.Missing.Count > 0) note += "\n" + L.Text("部分战斗功能未能挂接，详见日志。");
        Label(new Rect(r.x, y + S(4), r.width, Mathf.Max(S(40), r.yMax - y - S(4))), note, _muted);
    }
    private void CombatRow(Rect r, ref float y, float labelWidth, float x, float detailX, string label, ref bool value, string detail)
    {
        SettingName(new Rect(r.x, y, labelWidth, 0), label);
        if (Button(new Rect(x, y, S(110), S(30)), value ? "开启" : "关闭", "combat." + label, value ? _tabSelected : _button))
        {
            value = !value; CombatChanged?.Invoke();
        }
        // A narrow window wraps the explanation instead of clipping it.
        float detailWidth = r.xMax - detailX, detailHeight = Mathf.Max(S(30), _muted.CalcHeight(new GUIContent(L.NoBreak(L.Text(detail))), detailWidth));
        Label(new Rect(detailX, y + S(3), detailWidth, detailHeight), detail, _muted);
        y += Mathf.Max(S(42), detailHeight + S(12), _nameHeight + S(8));
    }
    private void SetScale(float value)
    {
        UiScale.User = Mathf.Clamp(Mathf.Round(value * 10) / 10, UiScale.MinUser, UiScale.MaxUser);
        SettingsChanged?.Invoke();
    }
    private void KeyRow(float labelX, float x, ref float y, float labelWidth, string label, KeyCode key, int slot)
    {
        Label(new Rect(labelX, y, labelWidth, S(32)), label, _label);
        string current = _capturing == slot ? L.Text("按下新按键，Esc 取消") : KeyLabel(key);
        float valueWidth = Mathf.Max(S(110), _label.CalcSize(new GUIContent(current)).x + S(20));
        Label(new Rect(x, y, valueWidth, S(32)), current, _capturing == slot ? _met : _label);
        float bx = x + valueWidth + S(10), change = ButtonWidth("修改", 70), clear = ButtonWidth("清除", 70);
        if (Button(new Rect(bx, y, change, S(30)), "修改", "settings.bind" + slot)) { _capturing = _capturing == slot ? 0 : slot; GUI.FocusControl(null); }
        bool previous = GUI.enabled;
        GUI.enabled = key != KeyCode.None;
        if (Button(new Rect(bx + change + S(8), y, clear, S(30)), "清除", "settings.clear" + slot)) ClearKey(slot);
        GUI.enabled = previous;
        y += S(44);
    }
    private void ClearKey(int slot)
    {
        if (slot == 1) PanelKey = KeyCode.None; else if (slot == 2) HideKey = KeyCode.None; else SpeedKey = KeyCode.None;
        _capturing = 0; SettingsChanged?.Invoke();
    }
    private string KeyLabel(KeyCode key)
    {
        if (key == KeyCode.None) return L.Text("未绑定");
        string name = key.ToString();
        return name.StartsWith("Alpha", StringComparison.Ordinal) && name.Length == 6 ? name.Substring(5) : name;
    }

    private void DrawMapOverview(Rect r)
    {
        Label(new Rect(r.x, r.y, r.width, S(28)), "当前地图 · 只读总览", _heading);
        var locations = _snapshot.MapLocations;
        if (!_snapshot.Ready || locations.Count == 0)
        { Empty(new Rect(r.x, r.y + S(35), r.width, r.height - S(35)), "当前地图没有可读取的地点事件。"); return; }
        float leftWidth = Mathf.Min(S(230), r.width * .32f);
        Rect list = new(r.x, r.y + S(36), leftWidth, r.height - S(36));
        Rect detail = new(list.xMax + S(16), list.y, r.width - leftWidth - S(16), list.height);
        var selected = locations.Find(item => item.Id == _mapLocation && item.Visible);
        if (selected == null) { selected = locations.Find(item => item.Visible); _mapLocation = selected?.Id ?? ""; }
        int visibleCount = locations.FindAll(item => item.Visible).Count;
        _mapListScroll = BeginScroll(list, _mapListScroll, new Rect(0, 0, list.width - S(18), Mathf.Max(list.height, visibleCount * S(53))));
        try
        {
            float y = 0;
            foreach (var location in locations)
            {
                if (!location.Visible) continue;
                if (Button(new Rect(0, y, list.width - S(18), S(48)), L.Format("{0} · {1}", location.Name, location.Entries.Count), "map." + location.Id, location.Id == _mapLocation ? _listSelected : location.Entries.Exists(entry => entry.Ready) ? _listReady : _listItem, location.Detail))
                { _mapLocation = location.Id; selected = location; _mapScroll = Vector2.zero; }
                y += S(53);
            }
        }
        finally { EndScroll(); }
        if (selected == null) return;
        float width = detail.width - S(18), total = S(43);
        foreach (var entry in selected.Entries)
        {
            total += Mathf.Max(S(29), _heading.CalcHeight(new GUIContent(entry.Title), entry.Ready ? width - S(16) : width)) + S(29);
            foreach (var condition in entry.Conditions) total += ReadOnlyConditionHeight(condition, width);
            if (entry.Conditions.Count == 0) total += S(28);
            total += S(17);
        }
        _mapScroll = BeginScroll(detail, _mapScroll, new Rect(0, 0, width, Mathf.Max(detail.height, total)));
        try
        {
            Label(new Rect(0, 0, width, S(30)), selected.Name, _heading, selected.Detail + "\n" + selected.Id);
            float y = S(43);
            if (selected.Entries.Count == 0) Label(new Rect(0, y, width, S(36)), "此地点没有当前事件。", _muted);
            foreach (var entry in selected.Entries)
            {
                float h = Mathf.Max(S(29), _heading.CalcHeight(new GUIContent(entry.Title), entry.Ready ? width - S(16) : width));
                if (entry.Ready) GUI.DrawTexture(new Rect(0, y, width, h), TextureOnce("ready", new Color(.18f, .45f, .27f, .92f)));
                Label(entry.Ready ? new Rect(S(8), y, width - S(16), h) : new Rect(0, y, width, h), entry.Title, _heading, entry.Detail + "\n" + entry.Id); y += h;
                Label(new Rect(0, y, width, S(26)), entry.Known ? entry.Ready ? "待触发" : "候选" : "条件未核验", entry.Known && entry.Ready ? _metSmall : _small); y += S(29);
                foreach (var condition in entry.Conditions) DrawReadOnlyCondition(condition, 0, ref y, width);
                if (entry.Conditions.Count == 0) { Label(new Rect(0, y, width, S(25)), "无额外条件", _muted); y += S(28); }
                GUI.DrawTexture(new Rect(0, y + S(5), width, 1), _line); y += S(17);
            }
        }
        finally { EndScroll(); }
    }

    private Vector2 _mapListScroll;
    private float ReadOnlyConditionHeight(ConditionSnapshot condition, float width)
    {
        if (condition.Children.Count == 1) return ReadOnlyConditionHeight(condition.Children[0], width);
        if (condition.Children.Count > 0)
        {
            float height = S(30);
            foreach (var child in condition.Children) height += ReadOnlyConditionHeight(child, width - S(16));
            return height;
        }
        return Mathf.Max(S(30), _label.CalcHeight(new GUIContent(condition.Text), width) + S(8));
    }

    private void DrawReadOnlyCondition(ConditionSnapshot condition, float x, ref float y, float width)
    {
        if (condition.Children.Count == 1) { DrawReadOnlyCondition(condition.Children[0], x, ref y, width); return; }
        if (condition.Children.Count > 0)
        {
            Label(new Rect(x, y, width - x, S(25)), condition.Operator == "OR" || condition.Operator == "或" ? "或" : "且", _muted); y += S(30);
            foreach (var child in condition.Children) DrawReadOnlyCondition(child, x + S(16), ref y, width);
            return;
        }
        float h = Mathf.Max(S(30), _label.CalcHeight(new GUIContent(condition.Text), width - x) + S(8));
        Label(new Rect(x, y, width - x, h), condition.Text, condition.Known ? condition.Satisfied ? _met : _unmet : _muted, condition.Detail);
        y += h;
    }

    private void DrawValues(Rect r)
    {
        string focusedBefore = GUI.GetNameOfFocusedControl() ?? "";
        bool editingValue = focusedBefore.StartsWith("zh.value.", StringComparison.Ordinal) && focusedBefore != "zh.value.search";
        string focusedId = editingValue ? focusedBefore.Substring("zh.value.".Length) : "";
        EventType originalEvent = Event.current.type;
        string enterTarget = editingValue && EnterKeyDown() ? focusedId : "";
        // Route Return once by the original field ID before virtualized controls can
        // recycle an IMGUI numeric ID. A removed/filtered field has no commit target.
        if (enterTarget.Length > 0) Event.current.Use();
        _valueSearch = Search(new Rect(r.x, r.y, r.width, S(32)), _valueSearch, "搜索名称或原始 ID", "zh.value.search");
        var groups = NativeUiState.Groups(_snapshot.Values);
        if (!groups.Contains(_valueGroup)) _valueGroup = "all";
        float groupX = r.x, groupY = r.y + S(42);
        foreach (string group in groups)
        {
            string groupLabel = group == "all" ? L.Text("全部") : L.GroupName(group);
            float width = Mathf.Max(S(63), _label.CalcSize(new GUIContent(groupLabel)).x + S(24));
            if (groupX + width > r.xMax && groupX > r.x) { groupX = r.x; groupY += S(35); }
            if (Button(new Rect(groupX, groupY, width, S(28)), groupLabel, "group." + group, group == _valueGroup ? _tabSelected : _tabButton))
            { _valueGroup = group; _valueScroll = Vector2.zero; }
            groupX += width + S(6);
        }
        float tableY = groupY + S(42);
        Label(new Rect(r.x + S(8), tableY, r.width * .40f, S(24)), "名称", _small);
        Label(new Rect(r.x + r.width * .42f, tableY, S(100), S(24)), "实际值", _small);
        Label(new Rect(r.xMax - S(232), tableY, S(220), S(24)), "设定", _small);
        GUI.DrawTexture(new Rect(r.x, tableY + S(28), r.width, 1), _line);
        Rect rows = new(r.x, tableY + S(34), r.width, r.yMax - tableY - S(34));
        bool listChanged = !ReferenceEquals(_valueSource, _snapshot.Values) || _filteredSearch != _valueSearch || _filteredGroup != _valueGroup;
        if (listChanged)
        {
            _valueSource = _snapshot.Values; _filteredSearch = _valueSearch; _filteredGroup = _valueGroup;
            _filteredValues.Clear();
            foreach (var value in _snapshot.Values)
                if (L.SearchForms(_valueSearch).Any(q => NativeUiState.MatchesValue(value, _valueGroup, q))) _filteredValues.Add(value);
            _searchElsewhere.Clear();
            if (_filteredValues.Count == 0 && _valueSearch.Trim().Length > 0)
                foreach (string group in groups)
                    if (group != "all" && group != _valueGroup && _snapshot.Values.Exists(value => L.SearchForms(_valueSearch).Any(q => NativeUiState.MatchesValue(value, group, q)))) _searchElsewhere.Add(group);
        }
        var values = _filteredValues;
        _valueRowWidth = rows.width - S(18);
        float totalHeight = 0;
        foreach (var value in values) totalHeight += ValueHeight(value);
        if (values.Count == 0)
        {
            // A forgotten search word is the usual reason a group looks empty: say so, and where it does match.
            string empty = _snapshot.Values.Count == 0 ? L.Text("进入存档后读取数值。")
                : _valueSearch.Trim().Length == 0 ? L.Text("没有匹配的数值。")
                : L.Format("没有名称或 ID 含「{0}」的数值。", _valueSearch.Trim())
                    + (_searchElsewhere.Count > 0 ? "\n" + L.Format("这些分组里有：{0}", string.Join(L.Text("、"), _searchElsewhere.Select(L.GroupName))) : "");
            Empty(rows, empty); return;
        }
        float contentWidth = rows.width - S(18);
        Vector2 scrollBefore = _valueScroll;
        _valueScroll = BeginScroll(rows, _valueScroll, new Rect(0, 0, contentWidth, Mathf.Max(rows.height, totalHeight)));
        bool layoutChanged = listChanged || scrollBefore != _valueScroll || Math.Abs(_lastValueContentHeight - totalHeight) > .1f || Math.Abs(_lastValueViewportHeight - rows.height) > .1f;
        bool focusedRowDrawn = false;
        try
        {
            float y = 0;
            foreach (var value in values)
            {
                float height = ValueHeight(value);
                bool focused = editingValue && value.Id == focusedId;
                // Creating IMGUI editors for every one of the 1,000+ game flags costs
                // most of a frame even when BeginScrollView clips their pixels.
                if (focused || y + height >= _valueScroll.y - S(2) && y <= _valueScroll.y + rows.height + S(2))
                {
                    DrawValue(value, new Rect(0, y, contentWidth, height), enterTarget);
                    focusedRowDrawn |= focused;
                }
                y += height;
            }
        }
        finally { EndScroll(); }
        _lastValueContentHeight = totalHeight; _lastValueViewportHeight = rows.height;
        if (editingValue && layoutChanged && originalEvent != EventType.MouseDown && originalEvent != EventType.MouseUp)
        {
            // Reassociate the previous named editor after the visible control set
            // changes. Drafts remain keyed by field ID, never by recycled control ID.
            GUI.FocusControl(focusedRowDrawn ? focusedBefore : null);
        }
    }

    private void DrawValue(ValueSnapshot value, Rect r, string enterTarget)
    {
        var draft = Draft(value);
        string name = string.IsNullOrWhiteSpace(value.Name) ? FlagDictionary.GetInfo(value.Id).Name : value.Name;
        string detail = value.Id + "\n" + value.Detail;
        if (value.BaseValue.HasValue && value.AdditionValue.HasValue)
            detail += "\n" + L.Format("基础 {0}；加成 {1}；实际 {2}", value.BaseValue.Value, value.AdditionValue.Value, value.Value);
        if (!value.Writable && !string.IsNullOrWhiteSpace(value.Reason)) detail += "\n" + value.Reason;
        if (string.IsNullOrWhiteSpace(value.Detail)) detail += FlagDictionary.GetInfo(value.Id).Meaning;
        var nameRect = new Rect(S(8), r.y + S(6), r.width * .40f - S(18), ValueBaseHeight(value) - S(12));
        Label(nameRect, name, _label, detail); HoverEnum(nameRect, value);
        string current = value.Options.TryGetValue(value.Value, out string label) ? label : value.Value.ToString(CultureInfo.InvariantCulture);
        var currentRect = new Rect(r.width * .42f, r.y + S(6), r.width * .18f - S(10), S(30));
        // F111: a stat can be locked against the game, for this run or always; the button cycles through the three.
        if (value.Writable && value.Id.StartsWith("stat:", StringComparison.Ordinal) && Enum.TryParse(value.Id.Substring(5), out GameStatType statType) && StatRules.Lockable(statType))
        {
            var mode = StatRules.LockOf(statType);
            string lockText = mode switch { StatRules.Lock.Session => "本次锁定", StatRules.Lock.Always => "一直锁定", _ => "锁定" };
            float lockWidth = ButtonWidth("一直锁定", 76);
            var lockRect = new Rect(currentRect.xMax - lockWidth, currentRect.y - S(2), lockWidth, S(30));
            currentRect.width = Mathf.Max(S(30), currentRect.width - lockWidth - S(6));
            if (Button(lockRect, lockText, "value.lock." + value.Id, mode == StatRules.Lock.None ? _button : _tabSelected,
                L.Text("点一下切换：锁定 → 本次锁定（重启游戏后解除）→ 一直锁定（换存档、重启都保留）→ 解除。锁住后剧情、物品、战斗都改不动它；在这里手动改仍然有效。")))
            {
                var next = StatRules.Cycle(statType);
                Message(next switch { StatRules.Lock.Session => L.Format("{0} 已锁定（本次运行）", name), StatRules.Lock.Always => L.Format("{0} 已一直锁定", name), _ => L.Format("{0} 已解除锁定", name) }, false);
            }
        }
        Label(currentRect, current, _label, value.Value.ToString(CultureInfo.InvariantCulture)); HoverEnum(currentRect, value);
        float editorWidth = Mathf.Min(S(245), r.width * .38f);
        Rect editor = new(r.width - editorWidth, r.y + S(4), editorWidth, S(32));
        bool previous = GUI.enabled;
        GUI.enabled = value.Writable;
        if (!value.Writable)
        {
            editor.height = ValueBaseHeight(value) - S(8);
            Label(editor, string.IsNullOrWhiteSpace(value.Reason) ? "只读" : value.Reason, _small, detail);
        }
        else if (value.Kind == "boolean" || value.Kind == "bool")
        {
            if (Button(editor, value.Value == 0 ? "开启" : "关闭", "value.bool." + value.Id)) Execute(() => _adapter.SetValue(value.Id, value.Value == 0 ? "1" : "0"));
        }
        else if (value.Options.Count > 0)
        {
            if (Button(editor, L.Format("{0} · 选择", current), "value.option." + value.Id)) _expandedValue = _expandedValue == value.Id ? "" : value.Id;
        }
        else
        {
            string control = "zh.value." + value.Id;
            GUI.SetNextControlName(control);
            string edited = InputField(new Rect(editor.x, editor.y, editor.width - ButtonWidth("应用", 67) - S(8), editor.height), draft.Text);
            bool submitValue = enterTarget == value.Id;
            if (edited != draft.Text) { draft.Text = edited; draft.Dirty = true; draft.PendingReadback = false; draft.Error = ""; draft.ErrorTemplate = ""; }
            if (Button(new Rect(editor.xMax - ButtonWidth("应用", 67), editor.y, ButtonWidth("应用", 67), editor.height), "应用", "value.apply." + value.Id) || submitValue) CommitValue(value, draft);
        }
        GUI.enabled = previous;
        float extraY = r.y + ValueBaseHeight(value) - S(3);
        if (!string.IsNullOrEmpty(draft.Error))
        {
            Label(new Rect(editor.x, extraY, editor.width, S(24)), draft.Error, _small); extraY += S(27);
        }
        if (_expandedValue == value.Id && value.Options.Count > 0)
        {
            foreach (var option in value.Options)
            {
                GUI.enabled = value.Writable;
                if (Button(new Rect(editor.x, extraY, editor.width, S(29)), option.Value, "value.choice." + value.Id + "." + option.Key, option.Key == value.Value ? _tabSelected : _button))
                {
                    var result = Execute(() => _adapter.SetValue(value.Id, option.Key.ToString(CultureInfo.InvariantCulture)));
                    if (result.Success) _expandedValue = "";
                }
                GUI.enabled = previous;
                extraY += S(33);
            }
        }
        GUI.DrawTexture(new Rect(0, r.yMax - 1, r.width, 1), _line);
    }

    private float ValueHeight(ValueSnapshot value)
    {
        bool error = _drafts.TryGetValue(value.Id, out var draft) && !string.IsNullOrEmpty(draft.Error);
        return ValueBaseHeight(value) + (error ? S(27) : 0) + (_expandedValue == value.Id ? value.Options.Count * S(33) : 0);
    }

    private float ValueBaseHeight(ValueSnapshot value)
    {
        float width = Mathf.Max(S(300), _valueRowWidth);
        if (_rowMeasures.TryGetValue(value.Id, out var cached) && cached.Name == value.Name && cached.Reason == value.Reason && cached.Writable == value.Writable && cached.Width == width && cached.Scale == _scale && cached.LanguageRevision == L.Revision)
            return cached.Height;
        float height = Mathf.Max(S(45), _label.CalcHeight(new GUIContent(value.Name), width * .40f - S(18)) + S(12));
        if (!value.Writable && !string.IsNullOrWhiteSpace(value.Reason))
            height = Mathf.Max(height, _small.CalcHeight(new GUIContent(value.Reason), Mathf.Min(S(245), width * .38f)) + S(12));
        _rowMeasures[value.Id] = new RowMeasure { Name = value.Name, Reason = value.Reason, Writable = value.Writable, Width = width, Scale = _scale, LanguageRevision = L.Revision, Height = height };
        return height;
    }
    private sealed class RowMeasure
    {
        public string Name = "", Reason = "";
        public bool Writable;
        public float Width, Scale, Height;
        public long LanguageRevision;
    }

    private ValueDraft Draft(ValueSnapshot value)
    {
        if (!_drafts.TryGetValue(value.Id, out var draft))
        {
            draft = new ValueDraft(); _drafts.Add(value.Id, draft);
        }
        if (!draft.Dirty || draft.PendingReadback && int.TryParse(draft.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int expected) && expected == value.Value)
        {
            draft.Text = value.Value.ToString(CultureInfo.InvariantCulture); draft.Dirty = false; draft.PendingReadback = false; draft.Error = ""; draft.ErrorTemplate = "";
        }
        return draft;
    }

    private void CommitValue(ValueSnapshot value, ValueDraft draft)
    {
        if (!value.Writable) return;
        if (!int.TryParse(draft.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)) { draft.ErrorTemplate = "请输入整数"; draft.ErrorArgs = Array.Empty<object>(); draft.Error = L.Text(draft.ErrorTemplate); return; }
        if (number < value.Min || number > value.Max) { draft.ErrorTemplate = "范围为 {0}–{1}"; draft.ErrorArgs = new object[] { value.Min, value.Max }; draft.Error = L.Format(draft.ErrorTemplate, draft.ErrorArgs); return; }
        draft.ErrorTemplate = "";
        var result = Execute(() => _adapter.SetValue(value.Id, draft.Text));
        draft.Error = result.Success ? "" : result.Message;
        draft.PendingReadback = result.Success;
        // Preserve the exact draft until the adapter's subsequent snapshot confirms it.
        var actual = FindValue(value.Id);
        if (result.Success && actual != null && actual.Value == number)
        { draft.Dirty = false; draft.PendingReadback = false; draft.Text = actual.Value.ToString(CultureInfo.InvariantCulture); }
    }

    private void DrawStatus(Rect r)
    {
        // On 回顾 the bar carries the review's own facts (the map's counts, which save it reads), not the event status (F98).
        string status = Time.realtimeSinceStartup < _messageUntil ? _message : _snapshot.ActionWaiting ? "等待继续" : _snapshot.Status;
        // A note about what just changed goes after a few seconds; a waiting action or a pending choice stays. Timed from when
        // it changed, whichever page was open, so an old note does not come back on another page.
        if (status != _statusShown) { _statusShown = status; _statusSince = Time.realtimeSinceStartup; }
        if (_tab == 4) { status = Time.realtimeSinceStartup < _messageUntil ? _message : _reviewStatus; Label(r, OneLine(L.Text(status), r.width), _small, status); return; }
        bool lasting = _snapshot.ActionWaiting || _snapshot.PendingEventId.Length > 0 || Time.realtimeSinceStartup < _messageUntil;
        if (!lasting && Time.realtimeSinceStartup - _statusSince > 6) status = "";
        if (string.IsNullOrWhiteSpace(status)) status = _snapshot.Ready ? "" : "等待游戏数据";
        Label(r, OneLine(L.Text(status), r.width), _small, status);
    }

    /// <summary>The bottom bar's text cut to one line with …, for the longer languages (F119); the hover shows all of it.</summary>
    private string OneLine(string text, float width)
    {
        if (_small.CalcSize(new GUIContent(text)).x <= width) return text;
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_small.CalcSize(new GUIContent(text.Substring(0, mid) + "…")).x <= width) lo = mid; else hi = mid - 1;
        }
        return text.Substring(0, lo).TrimEnd() + "…";
    }

    private void Refresh()
    {
        try
        {
            _adapter.Refresh();
            ReadSnapshot();
        }
        catch (Exception exception) { Message(L.Format("读取失败：{0}", exception.Message), true); }
    }

    private void ReadSnapshot()
    {
        try
        {
            _snapshot = _adapter.Snapshot ?? new ToolSnapshot();
            if (!_eventViewChosen && _snapshot.Ready) { _mapOverview = _snapshot.MapLocations.Count > 0 && !_snapshot.ActionWaiting; _eventViewChosen = true; }
            // Each new wait starts on 「随机」, matching the adapter, which clears any earlier choice.
            if (_snapshot.ActionWaiting && !_lastActionWaiting) { _mapOverview = false; _selectedEvent = ""; }
            if (Previewing && !_lastPreview) _mapOverview = false;
            _lastPreview = Previewing;
            _lastActionWaiting = _snapshot.ActionWaiting;
            string context = _snapshot.Location + "\n" + _snapshot.Action;
            if (_snapshot.Revision != _contextRevision)
            {
                _context = context; _contextRevision = _snapshot.Revision;
                _selectedEvent = ""; _eventScroll = _conditionScroll = Vector2.zero;
            }
        }
        catch (Exception exception) { Message(L.Format("读取失败：{0}", exception.Message), true); }
    }

    private OperationResult Execute(Func<OperationResult> operation)
    {
        OperationResult result;
        try { result = operation() ?? OperationResult.Fail("操作未返回结果"); }
        catch (Exception exception)
        {
            // A call into the game by reflection wraps what it threw; the player is shown that, the log gets all of it.
            var cause = exception.GetBaseException();
            Plugin.Instance?.Log.LogError("OPERATION_FAILED " + exception);
            result = OperationResult.Fail(L.Format("操作失败：{0}", cause.Message));
        }
        Message(result.Message, !result.Success);
        ReadSnapshot();
        return result;
    }

    internal void Message(string message, bool error)
    { _message = message; _messageError = error; _messageUntil = Time.realtimeSinceStartup + 9; }

    private bool Previewing => _snapshot.Events.Count == 0 && _snapshot.PreviewEvents.Count > 0;
    private EventSnapshot? FindEvent(string id) => string.IsNullOrEmpty(id) ? null : _snapshot.Events.Find(item => item.Id == id) ?? _snapshot.PreviewEvents.Find(item => item.Id == id);
    private ValueSnapshot? FindValue(string id) => _snapshot.Values.Find(value => value.Id == id);
    private static string EventDetail(EventSnapshot item) => item.Title + "\n" + item.Id + "\n" + L.Format("权重 {0}", item.Weight) + (string.IsNullOrWhiteSpace(item.Detail) ? "" : "\n" + item.Detail);
    private static bool Matches(string query, params string[] fields)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        foreach (string q in L.SearchForms(query.Trim()))
            foreach (string field in fields) if (field != null && Tone.Plain(field).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    private string Search(Rect r, string value, string placeholder, string name)
    {
        // A filled box offers × to clear it; the word otherwise keeps filtering across groups and is easy to forget.
        // The field comes first: IMGUI keeps keyboard focus by control order, and a × drawn before it once the first
        // character lands took the focus away, so an input method lost the rest of the word (F116).
        float clear = string.IsNullOrEmpty(value) ? 0 : S(34);
        GUI.SetNextControlName(name);
        string next = InputField(new Rect(r.x, r.y, r.width - S(34), r.height), value);
        if (string.IsNullOrEmpty(next) && GUI.GetNameOfFocusedControl() != name)
            Label(new Rect(r.x + S(9), r.y + S(3), r.width - S(18), r.height - S(6)), placeholder, _muted);
        if (clear > 0 && Button(new Rect(r.xMax - clear + S(4), r.y, clear - S(4), r.height), "×", name + ".clear", tooltip: L.Text("清除搜索")))
        { GUI.FocusControl(null); return ""; }
        return next;
    }

    private float ButtonWidth(string text, float minimum = 63) => Mathf.Max(S(minimum), _button.CalcSize(new GUIContent(L.Text(text))).x + S(16));

    private bool Button(Rect r, string text, string name, GUIStyle? style = null, string tooltip = "")
    {
        GUI.SetNextControlName("zh.button." + name);
        var content = new GUIContent(L.Text(text));
        bool clicked = SkinnedButton(r, content, style ?? _button);
        Hover(r, tooltip);
        return clicked;
    }

    private void Label(Rect r, string text, GUIStyle style, string tooltip = "")
    { GUI.Label(r, L.NoBreak(L.Text(text ?? "")), style); Hover(r, L.Text(tooltip)); }

    private void Hover(Rect r, string text)
    {
        if (_hoverAllowed && !string.IsNullOrWhiteSpace(text) && r.Contains(Event.current.mousePosition)) { _hoverText = text; _hoverEnum = null; }
    }

    private void HoverEnum(Rect r, ValueSnapshot value)
    {
        if (value.EnumValues.Count > 0 && _hoverAllowed && r.Contains(Event.current.mousePosition)) { _hoverEnum = value; _hoverText = ""; }
    }

    private void Empty(Rect r, string text) => Label(new Rect(r.x + S(8), r.y + S(18), r.width - S(16), Mathf.Min(r.height, S(80))), text, _muted);
    private Vector2 BeginScroll(Rect position, Vector2 scroll, Rect view, bool horizontal = false)
    {
        _hoverClips.Push(_hoverAllowed);
        _hoverAllowed = _hoverAllowed && position.Contains(Event.current.mousePosition);
        return GUI.BeginScrollView(position, scroll, view, false, false, horizontal ? _hscrollbar : GUIStyle.none, _scrollbar);
    }
    private void EndScroll() { GUI.EndScrollView(); _hoverAllowed = _hoverClips.Pop(); }
    private float S(float value) => value * _scale;
    private static bool OwnFocus() => (GUI.GetNameOfFocusedControl() ?? "").StartsWith("zh.", StringComparison.Ordinal);
    private static bool EnterKeyDown()
    {
        var current = Event.current;
        return current.type == EventType.KeyDown && (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter);
    }
    private static bool ConsumeEnterFor(string control, bool wasEnterDown, bool focusedBefore)
    {
        if (!wasEnterDown || !focusedBefore && GUI.GetNameOfFocusedControl() != control) return false;
        // Preserve the original key event across TextField consuming it. Looking up focus
        // on both sides also supports names registered during this OnGUI pass.
        if (Event.current.type != EventType.Used) Event.current.Use();
        return true;
    }

    private void HandleWindowDrag(Event current)
    {
        int dragId = GUIUtility.GetControlID(0x5A4801, FocusType.Passive);
        Vector2 pointer = current.mousePosition;
        // The title bar drags the window up to the buttons on its right (the language one included, F119).
        float titleWidth = _languageButton.width > 0 ? _languageButton.x - S(4) : WindowRect.width - S(192);
        var title = new Rect(WindowRect.x, WindowRect.y, titleWidth, S(43));
        var resize = new Rect(WindowRect.xMax - S(23), WindowRect.yMax - S(23), S(23), S(23));
        if (current.type == EventType.MouseDown && current.button == 0 && (resize.Contains(pointer) || title.Contains(pointer)))
        {
            _resizing = resize.Contains(pointer); _dragging = !_resizing;
            _dragOrigin = pointer; _dragRect = WindowRect; GUIUtility.hotControl = dragId; _ownedHotControl = dragId; current.Use();
        }
        else if (GUIUtility.hotControl == dragId && (_dragging || _resizing))
        {
            if (current.type == EventType.MouseDrag)
            {
                Vector2 offset = pointer - _dragOrigin;
                WindowRect = _resizing ? new Rect(_dragRect.x, _dragRect.y, _dragRect.width + offset.x, _dragRect.height + offset.y) : new Rect(_dragRect.x + offset.x, _dragRect.y + offset.y, _dragRect.width, _dragRect.height);
                ClampWindow(); current.Use();
            }
            if (current.type == EventType.MouseUp)
            { _dragging = _resizing = false; GUIUtility.hotControl = 0; current.Use(); }
        }
    }

    /// <summary>A press that moves past a small threshold drags the launcher; otherwise the release opens the panel.</summary>
    private bool LauncherInput(float scale)
    {
        var current = Event.current;
        int id = GUIUtility.GetControlID(0x5A4802, FocusType.Passive);
        switch (current.GetTypeForControl(id))
        {
            case EventType.MouseDown when current.button == 0 && LauncherRect.Contains(current.mousePosition):
                GUIUtility.hotControl = _ownedHotControl = id; _launcherDragged = false;
                _launcherPress = current.mousePosition; _launcherGrab = current.mousePosition - LauncherRect.position;
                current.Use(); break;
            case EventType.MouseDrag when GUIUtility.hotControl == id:
                if (!_launcherDragged && (current.mousePosition - _launcherPress).magnitude > S(6)) _launcherDragged = true;
                if (_launcherDragged)
                {
                    Vector2 target = current.mousePosition - _launcherGrab;
                    float freeX = Mathf.Max(1, Screen.width - LauncherRect.width), freeY = Mathf.Max(1, Screen.height - LauncherRect.height);
                    LauncherAnchor = new Vector2(Mathf.Clamp01(target.x / freeX), Mathf.Clamp01(target.y / freeY));
                    LauncherRect = ComputeLauncherRect(scale);
                }
                current.Use(); break;
            case EventType.MouseUp when GUIUtility.hotControl == id:
                GUIUtility.hotControl = _ownedHotControl = 0; current.Use();
                if (_launcherDragged) { _launcherDragged = false; if (LauncherAnchor.HasValue) LauncherMoved?.Invoke(LauncherAnchor.Value); return false; }
                if (!LauncherRect.Contains(current.mousePosition)) return false;
                ShowEvents(); return true;
            case EventType.Repaint:
                if (GameSkin.Draw(LauncherRect, LauncherRect.Contains(current.mousePosition) && GameSkin.Has(GameSkin.Part.ButtonLit) ? GameSkin.Part.ButtonLit : GameSkin.Part.Button, scale, Color.white))
                    Bare(_launcher).Draw(LauncherRect, new GUIContent(LauncherText), id);
                else _launcher.Draw(LauncherRect, new GUIContent(LauncherText), id);
                break;
        }
        return false;
    }

    private void ClampWindow(float? inputScale = null)
    {
        float layoutScale = inputScale ?? _scale;
        float margin = 8 * layoutScale, maxWidth = Mathf.Max(1, Screen.width - margin * 2), maxHeight = Mathf.Max(1, Screen.height - margin * 2);
        Rect window = WindowRect;
        window.width = Mathf.Clamp(window.width, Mathf.Min(700 * layoutScale, maxWidth), maxWidth);
        window.height = Mathf.Clamp(window.height, Mathf.Min(470 * layoutScale, maxHeight), maxHeight);
        window.x = Mathf.Clamp(window.x, margin, Mathf.Max(margin, Screen.width - window.width - margin));
        window.y = Mathf.Clamp(window.y, margin, Mathf.Max(margin, Screen.height - window.height - margin));
        WindowRect = window;
    }

    private void DrawTooltip(Vector2 pointer)
    {
        if (!IsPointerOver || Event.current.type != EventType.Repaint) return;
        if (_hoverEnum != null) { DrawEnumTooltip(pointer, _hoverEnum); return; }
        if (string.IsNullOrWhiteSpace(_hoverText)) return;
        float width = Mathf.Min(S(430), Screen.width - S(24));
        string text = NativeUiState.ToneColors(_hoverText, Tone.PositiveOnDark, Tone.NegativeOnDark);
        float height = Mathf.Min(_tooltip.CalcHeight(new GUIContent(text), width), Screen.height - S(24));
        var r = new Rect(Mathf.Min(pointer.x + S(15), Screen.width - width - S(10)), Mathf.Min(pointer.y + S(22), Screen.height - height - S(10)), width, height);
        if (GameSkin.Has(GameSkin.Part.Card)) { GUI.DrawTexture(r, _cardFill); GameSkin.Draw(r, GameSkin.Part.Card, _scale, Color.white); GUI.Label(r, text, Bare(_tooltip)); }
        else GUI.Label(r, text, _tooltip);
    }

    private void DrawEnumTooltip(Vector2 pointer, ValueSnapshot value)
    {
        var rows = NativeUiState.EnumRows(value.EnumValues);
        var lines = new List<string>();
        float preferredWidth = S(150);
        foreach (var row in rows)
        {
            string line = row.Key.ToString(CultureInfo.InvariantCulture) + " = " + (string.IsNullOrWhiteSpace(row.Value) ? L.Text("未收录") : row.Value);
            lines.Add(line); preferredWidth = Mathf.Max(preferredWidth, _enumLine.CalcSize(new GUIContent(line)).x + S(4));
        }
        string currentLabel = value.EnumValues.TryGetValue(value.Value, out var meaning) && !string.IsNullOrWhiteSpace(meaning) ? meaning : L.Text("未收录");
        string header = value.Name + " · " + value.Value.ToString(CultureInfo.InvariantCulture) + " = " + currentLabel;
        float padding = S(11), gap = S(18), footerHeight = S(30);
        var grid = NativeUiState.EnumTooltipLayout(lines, Mathf.Max(1, Screen.width - S(24)), Mathf.Max(1, Screen.height - S(24)),
            Mathf.Min(S(430), preferredWidth), S(140), gap, padding, S(22), footerHeight,
            (text, width) => _enumLine.CalcHeight(new GUIContent(text), width) + S(5),
            width => _enumHeader.CalcHeight(new GUIContent(header), width) + S(9));
        float x = Mathf.Clamp(pointer.x + S(15), S(12), Mathf.Max(S(12), Screen.width - grid.Width - S(12)));
        float y = Mathf.Clamp(pointer.y + S(22), S(12), Mathf.Max(S(12), Screen.height - grid.Height - S(12)));
        var box = new Rect(x, y, grid.Width, grid.Height);
        GUI.DrawTexture(box, _tooltip.normal.background);
        GUI.BeginGroup(box);
        try
        {
            GUI.Label(new Rect(padding, padding, grid.Width - padding * 2, grid.HeaderHeight), header, _enumHeader);
            for (int i = 0; i < grid.VisibleCount && grid.Rows > 0; i++)
            {
                int column = i / grid.Rows, row = i % grid.Rows;
                var cell = new Rect(padding + column * (grid.ColumnWidth + gap), padding + grid.HeaderHeight + row * grid.RowHeight, grid.ColumnWidth, grid.RowHeight);
                GUI.Label(cell, lines[i], _enumLine);
            }
            if (grid.Truncated)
                GUI.Label(new Rect(padding, grid.Height - footerHeight - padding, grid.Width - padding * 2, footerHeight),
                    L.Format("屏幕空间不足，显示 {0}/{1}", grid.VisibleCount, lines.Count), _enumLine);
        }
        finally { GUI.EndGroup(); }
    }

    private void BuildStyles(float scale)
    {
        _scale = scale;
        if (_background == null)
        {
            _background = Texture(new Color(0, 0, 0, .78f));
            _lineGrey = Texture(new Color(.66f, .70f, .74f, .24f));
            _lineWarm = Texture(new Color(.80f, .70f, .52f, .30f));
            _skinUnder = Texture(new Color(.02f, .02f, .03f, .84f));
            _section = Texture(new Color(.13f, .15f, .18f, .85f));
            _selection = Texture(new Color(.20f, .35f, .49f, .76f));
        }
        if (_languageRevision != L.Revision || _font == null)
        {
            if (_ownsFont && _font != null) UnityEngine.Object.Destroy(_font);
            try { _font = Font.CreateDynamicFontFromOSFont(L.FontNames, 20); _ownsFont = _font != null; }
            catch { _font = GUI.skin.font; _ownsFont = false; }
            _languageRevision = L.Revision;
        }
        Color text = new(.94f, .95f, .96f, 1), muted = new(.73f, .76f, .79f, 1);
        _label = TextStyle(16, text); _label.wordWrap = true;
        _muted = TextStyle(15, muted); _muted.wordWrap = true;
        _small = TextStyle(13, muted); _small.wordWrap = true;
        _heading = TextStyle(18, text); _heading.fontStyle = FontStyle.Bold; _heading.wordWrap = true;
        RouteStyles();
        _routeItem = ButtonStyle(TextureOnce("clear", Color.clear), _section, text); _routeItem.alignment = TextAnchor.MiddleLeft; _routeItem.wordWrap = true;
        _routeItemSelected = ButtonStyle(_selection, _selection, text); _routeItemSelected.alignment = TextAnchor.MiddleLeft; _routeItemSelected.wordWrap = true;
        _title = TextStyle(18, text); _title.fontStyle = FontStyle.Bold;
        _met = new GUIStyle(_label); _met.normal.textColor = Palette.Positive;
        _unmet = new GUIStyle(_label); _unmet.normal.textColor = Palette.Negative;
        _unmetSmall = new GUIStyle(_small); _unmetSmall.normal.textColor = Palette.Negative;
        _smallLine = new GUIStyle(_small) { wordWrap = false, clipping = TextClipping.Clip };
        _caption = TextStyle(14, text); _caption.fontStyle = FontStyle.Bold;
        _dialogFill = TextureOnce("dialog", new Color(.07f, .08f, .10f, .98f));
        _cardFill = TextureOnce("card", new Color(.04f, .05f, .07f, .92f));
        _jumpDialogAccent = TextureOnce("accent", new Color(.22f, .37f, .51f, 1));
        _metSmall = new GUIStyle(_small); _metSmall.normal.textColor = _met.normal.textColor;
        _button = ButtonStyle(_section, TextureOnce("hover", new Color(.26f, .29f, .33f, 1)), text);
        _primary = ButtonStyle(TextureOnce("primary", new Color(.22f, .37f, .51f, 1)), TextureOnce("primaryHover", new Color(.29f, .46f, .61f, 1)), text);
        _launcher = ButtonStyle(_background, TextureOnce("launcherHover", new Color(0, 0, 0, .9f)), text);
        _tabButton = ButtonStyle(TextureOnce("clear", Color.clear), _section, muted);
        _tabSelected = ButtonStyle(_selection, _selection, text);
        _listItem = ButtonStyle(TextureOnce("clear", Color.clear), _section, text);
        _listSelected = ButtonStyle(_selection, _selection, text);
        _listReady = ButtonStyle(TextureOnce("ready", new Color(.18f, .45f, .27f, .92f)), TextureOnce("readyHover", new Color(.23f, .53f, .33f, .95f)), text);
        _readySmall = TextStyle(13, text); _readySmall.wordWrap = true;
        _rich = TextStyle(15, text); _rich.wordWrap = true; _rich.richText = true;
        _richBody = new GUIStyle(_rich); _richBody.normal.textColor = Palette.Effects;
        _richLine = TextStyle(13, muted); _richLine.wordWrap = false; _richLine.clipping = TextClipping.Clip; _richLine.richText = true;
        _input = TextStyle(16, text);
        _input.alignment = TextAnchor.MiddleLeft; _input.padding = new RectOffset((int)S(9), (int)S(9), (int)S(3), (int)S(3));
        _input.normal.background = TextureOnce("input", new Color(.10f, .12f, .14f, 1));
        _input.focused.background = TextureOnce("inputFocused", new Color(.18f, .26f, .34f, 1));
        _input.hover.background = _input.normal.background;
        _input.focused.textColor = _input.hover.textColor = text;
        _tooltip = TextStyle(14, text); _tooltip.wordWrap = true; _tooltip.richText = true;
        _tooltip.padding = new RectOffset((int)S(11), (int)S(11), (int)S(8), (int)S(8));
        _tooltip.normal.background = TextureOnce("tooltip", new Color(.06f, .07f, .08f, .98f));
        _enumLine = TextStyle(14, text); _enumLine.wordWrap = true;
        _enumHeader = new GUIStyle(_enumLine) { fontStyle = FontStyle.Bold };
        _scrollbar = new GUIStyle(GUI.skin.verticalScrollbar) { fixedWidth = S(13) };
        _hscrollbar = new GUIStyle(GUI.skin.horizontalScrollbar) { fixedHeight = S(13) };
        ApplySkin();
    }

    private GUIStyle TextStyle(int size, Color color)
    {
        var style = new GUIStyle { font = _font, fontSize = Mathf.RoundToInt(S(size)), alignment = TextAnchor.MiddleLeft, richText = false, clipping = TextClipping.Clip };
        style.normal.textColor = color;
        return style;
    }

    private GUIStyle ButtonStyle(Texture2D background, Texture2D hover, Color color)
    {
        var style = TextStyle(15, color);
        style.alignment = TextAnchor.MiddleCenter; style.wordWrap = true;
        style.padding = new RectOffset((int)S(5), (int)S(5), 0, 0);
        style.normal.background = background; style.hover.background = hover;
        style.active.background = _selection; style.focused.background = hover;
        style.hover.textColor = style.active.textColor = style.focused.textColor = color;
        return style;
    }

    private readonly Dictionary<string, Texture2D> _namedTextures = new(StringComparer.Ordinal);
    private Texture2D TextureOnce(string name, Color color)
    {
        if (_namedTextures.TryGetValue(name, out var existing)) return existing;
        var texture = Texture(color); _namedTextures.Add(name, texture); return texture;
    }
    private Texture2D Texture(Color color)
    {
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        texture.SetPixel(0, 0, color); texture.Apply(); _textures.Add(texture); return texture;
    }

    public void Dispose()
    {
        foreach (var texture in _textures) if (texture != null) UnityEngine.Object.Destroy(texture);
        _textures.Clear(); _namedTextures.Clear();
        if (_ownsFont && _font != null) UnityEngine.Object.Destroy(_font);
    }

    private sealed class ValueDraft
    {
        public string Text = "";
        public bool Dirty;
        public bool PendingReadback;
        public string Error = "";
        public string ErrorTemplate = "";
        public object[] ErrorArgs = Array.Empty<object>();
    }
}
