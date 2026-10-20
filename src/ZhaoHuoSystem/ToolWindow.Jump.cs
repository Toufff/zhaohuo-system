using System;
using System.Collections.Generic;
using System.Linq;
using Mortal.Core;
using UnityEngine;

namespace ZhaoHuoSystem;

/// <summary>Where 跳转 or 直接跳转 goes (F98): a kept start, or for 直接跳转 the script, date and main mission to set.</summary>
internal sealed class JumpTarget
{
    public string Title = "";
    /// <summary>The kept start 跳转 writes; null when there is none, and only 直接跳转 is offered.</summary>
    public StoryReview.Node? Node;
    public string Script = "", MainMission = "";
    public int[]? Date;
    /// <summary>A start kept in memory this scene (回想's 回到这段开头, F101), written in place of a kept node, with its music.</summary>
    public GameSave? Save;
    public string Music = "", Env = "";
}

/// <summary>One option that sets a flag a branch reads, as 决定它的选项 lists it (F101).</summary>
internal sealed class SourceRow
{
    public string Text = "";
    /// <summary>The kept start of the script where the option is chosen; null when it was never kept.</summary>
    public StoryReview.Node? Node;
    public string Title = "";
}

/// <summary>One manual slot as the slot picker shows it.</summary>
internal sealed class SlotLine
{
    public string Slot = "", Date = "", Saved = "", Note = "";
    public bool Empty, Star;
}

// 跳转 (F98): pick a manual slot (an empty one, or one to overwrite after a second question), the start is written into it
// and loaded at once. 直接跳转 first says what may break. The dialogs sit over the whole window; nothing behind them reacts.
public sealed partial class ToolWindow
{
    private enum JumpDialog { None, Help, Warning, Slots, Overwrite, Sources }
    private JumpDialog _dialog;
    private JumpTarget? _jumpTarget;
    private bool _jumpDirect;
    private List<SlotLine> _slotLines = new();
    private SlotLine? _slotPick;
    private Vector2 _dialogScroll;
    internal Func<List<SlotLine>>? ReadSlots;
    internal Func<JumpTarget, bool, string, OperationResult>? Jump;
    /// <summary>What 直接跳转 starts from: 当前进度, or on the title the 最近存档 it read.</summary>
    internal Func<string>? JumpBase;
    private bool InGame => _snapshot.Ready || CombatCheats.Current != CombatCheats.Mode.None;
    // 决定它的选项 (F101): the branch, the flags it reads, and for each the options that set it once they are found.
    private string _sourceBranch = "";
    private List<StoryLog.Flag> _sourceFlags = new();
    private List<List<SourceRow>>? _sourceRows;
    internal Func<bool>? SourcesReady;
    internal Func<float>? SourcesProgress;
    internal Func<string, List<SourceRow>>? FindSources;

    // Opened from 回想 with the panel closed: closing the dialog closes the panel again, uncovering the log.
    private bool _closeWithDialog;
    /// <summary>From the game's 回想 (F101): the panel opens on 跳转's slot picker for a start kept this scene.</summary>
    internal void JumpFromLog(JumpTarget target) { OpenFromLog(); OpenJump(target, false); }
    private void OpenFromLog() { if (!_visible) { _closeWithDialog = true; Visible = true; } }
    /// <summary>From the game's 回想 (F101): the panel opens on the options that set the flags this branch reads.</summary>
    internal void SourcesFromLog(string branch, List<StoryLog.Flag> flags)
    {
        OpenFromLog();
        _sourceBranch = branch; _sourceFlags = flags; _sourceRows = null;
        _dialog = JumpDialog.Sources; _dialogScroll = Vector2.zero; GUI.FocusControl(null);
    }

    private void OpenJump(JumpTarget target, bool direct)
    {
        _jumpTarget = target; _jumpDirect = direct; _dialogScroll = Vector2.zero; GUI.FocusControl(null);
        if (direct) _dialog = JumpDialog.Warning; else OpenSlots();
    }
    private void OpenSlots()
    {
        try { _slotLines = ReadSlots?.Invoke() ?? new List<SlotLine>(); }
        catch (Exception ex) { _slotLines = new List<SlotLine>(); Message(L.Format("读取存档格失败：{0}", ex.GetBaseException().Message), true); }
        _dialog = JumpDialog.Slots; _dialogScroll = Vector2.zero;
    }
    private void OpenHelp() { _dialog = JumpDialog.Help; _dialogScroll = Vector2.zero; GUI.FocusControl(null); }
    private void CloseDialog()
    {
        _dialog = JumpDialog.None; _slotPick = null;
        if (_closeWithDialog) { _closeWithDialog = false; Visible = false; }
    }

    private void JumpInto(SlotLine line)
    {
        if (_jumpTarget == null) { CloseDialog(); return; }
        var result = Execute(() => Jump?.Invoke(_jumpTarget, _jumpDirect, line.Slot) ?? OperationResult.Fail(L.Text("存档系统未就绪")));
        if (result.Success) CloseDialog();
        else if (_dialog == JumpDialog.Overwrite) _dialog = JumpDialog.Slots;
    }

    /// <summary>Over the whole window, after everything else (drawn with input disabled while this is open).</summary>
    private void DrawDialog(float width, float height)
    {
        Fill(new Rect(0, 0, width, height), new Color(0, 0, 0, .55f));
        bool tall = _dialog == JumpDialog.Help || _dialog == JumpDialog.Slots || _dialog == JumpDialog.Sources;
        float boxWidth = Mathf.Min(S(tall ? 640 : 520), width - S(40));
        float boxHeight = _dialog switch
        {
            JumpDialog.Help or JumpDialog.Slots or JumpDialog.Sources => Mathf.Min(S(560), height - S(60)),
            _ => Mathf.Min(height - S(40), DialogTextHeight(boxWidth - S(40)) + S(136))
        };
        var box = new Rect((width - boxWidth) / 2, Mathf.Max(S(20), (height - boxHeight) / 2), boxWidth, boxHeight);
        // The game's frame is see-through: the plain fill goes under it, so the panel behind does not show.
        GUI.DrawTexture(box, _dialogFill);
        if (!GameSkin.Draw(box, GameSkin.Part.Card, _scale, Color.white)) GUI.DrawTexture(new Rect(box.x, box.y, box.width, S(3)), _jumpDialogAccent);
        var inner = new Rect(box.x + S(20), box.y + S(18), box.width - S(40), box.height - S(36));
        switch (_dialog)
        {
            case JumpDialog.Help: DrawHelp(inner); break;
            case JumpDialog.Warning: DrawWarning(inner); break;
            case JumpDialog.Slots: DrawSlots(inner); break;
            case JumpDialog.Overwrite: DrawOverwrite(inner); break;
            case JumpDialog.Sources: DrawSources(inner); break;
        }
        // A press outside the box goes nowhere.
        var current = Event.current;
        if (current.type == EventType.MouseDown && !box.Contains(current.mousePosition)) current.Use();
    }

    /// <summary>The text of the warning or the overwrite question at this width, for the box to hold it all.</summary>
    private float DialogTextHeight(float width)
    {
        if (_dialog == JumpDialog.Warning && _jumpTarget is { } target)
        {
            string date = target.Date is { Length: 3 } d && d[0] > 0 ? RouteDate(d) : L.Text("原来的日期");
            string body = L.Format("「{0}」没有收录进度。直接跳转会以{1}的数值和剧情标记为基础，把剧情改到这一段的开头、日期改到{2}。", target.Title, JumpBase?.Invoke() ?? L.Text("当前进度"), date);
            string risk = L.Text("中间剧情本该设置的标记不会有：之后可能出现对话对不上、分支判断出错，甚至卡住无法继续。只建议用来试看，不要在这个存档上长期玩下去。");
            return TextHeight(_label, body, width) + TextHeight(_unmetSmall, risk, width);
        }
        if (_dialog == JumpDialog.Overwrite && _slotPick is { } line && _jumpTarget is { } jump)
        {
            string body = L.Format("原来的存档（{0} · 存于 {1}）会被「{2}」替换，无法恢复。", line.Date, line.Saved, jump.Title) + (line.Star ? L.Text("这是收藏的存档，星标和备注也会一起清掉。") : "");
            return TextHeight(line.Star ? _unmet : _label, body, width);
        }
        return S(60);
    }

    /// <summary>A wrapped text's height with room under its last line: the OS font draws a little taller than IMGUI measures,
    /// and an exact fit cuts the last line in half.</summary>
    private float TextHeight(GUIStyle style, string text, float width) => style.CalcHeight(new GUIContent(text), Mathf.Max(S(40), width)) + S(6);

    // ---- 界面风格 (F99): the game's frames behind our controls, its serif on our text ----
    /// <summary>After the styles are built: the game's serif where it has one, the warm line with its frames; plain otherwise.</summary>
    private void ApplySkin()
    {
        _skinRevision = SkinKey; _bare.Clear();
        _line = GameSkin.Has(GameSkin.Part.Window) ? _lineWarm : _lineGrey;
        var serif = GameSkin.Serif(L.LanguageId, false);
        var bold = GameSkin.Serif(L.LanguageId, true) ?? serif;
        if (serif != null)
            foreach (var style in new[] { _label, _muted, _button, _primary, _tabButton, _tabSelected, _listItem, _listSelected, _routeItem, _routeItemSelected, _input, _launcher, _met, _unmet, _routeLink })
                if (style != null) style.font = serif;
        if (bold != null) foreach (var style in new[] { _title, _heading, _caption }) if (style != null) style.font = bold;
        // The window's name in the game's brush script, as its own pages title themselves.
        _brandTitle = GameSkin.Brush(L.LanguageId) is Font brush
            ? new GUIStyle(_title) { font = brush, fontStyle = FontStyle.Normal, fontSize = Mathf.RoundToInt(_title.fontSize * 1.25f) }
            : _title;
        if (GameSkin.Has(GameSkin.Part.Window) && _routeLink != null) { _routeLink.normal.textColor = SkinGold; _routeMark.normal.textColor = SkinGold; }
        if (GameSkin.Has(GameSkin.Part.Window)) { _scrollbar = SkinScroll(true); _hscrollbar = SkinScroll(false); }
    }

    /// <summary>
    /// A thin scrollbar in the skin's tones. IMGUI finds a scrollbar's thumb and arrow buttons by name in its skin, so the three
    /// go into the skin's custom styles under names of our own (the arrows with no size).
    /// </summary>
    private GUIStyle SkinScroll(bool vertical)
    {
        string name = vertical ? "zhskinvscroll" : "zhskinhscroll";
        _scrollTrack ??= Texture(new Color(0, 0, 0, .28f));
        _scrollThumb ??= Texture(new Color(.80f, .68f, .48f, .55f));
        _scrollThumbHover ??= Texture(new Color(.92f, .80f, .56f, .85f));
        var track = new GUIStyle { name = name };
        track.normal.background = _scrollTrack;
        var thumb = new GUIStyle { name = name + "thumb" };
        thumb.normal.background = _scrollThumb; thumb.hover.background = thumb.active.background = _scrollThumbHover;
        var less = new GUIStyle { name = name + (vertical ? "upbutton" : "leftbutton") };
        var more = new GUIStyle { name = name + (vertical ? "downbutton" : "rightbutton") };
        if (vertical) { track.fixedWidth = thumb.fixedWidth = S(9); } else { track.fixedHeight = thumb.fixedHeight = S(9); }
        var skin = GUI.skin;
        var styles = new List<GUIStyle>(skin.customStyles ?? new GUIStyle[0]);
        styles.RemoveAll(style => style != null && (style.name == thumb.name || style.name == less.name || style.name == more.name));
        styles.Add(thumb); styles.Add(less); styles.Add(more);
        skin.customStyles = styles.ToArray();
        return track;
    }
    private Texture2D? _scrollTrack, _scrollThumb, _scrollThumbHover;
    private static readonly Color SkinGold = new(.93f, .80f, .56f, 1);
    private GUIStyle _brandTitle = null!;

    /// <summary>A style without its own backgrounds, for text drawn over one of the game's frames.</summary>
    private GUIStyle Bare(GUIStyle style)
    {
        if (_bare.TryGetValue(style, out var bare)) return bare;
        bare = new GUIStyle(style);
        foreach (var state in new[] { bare.normal, bare.hover, bare.active, bare.focused, bare.onNormal, bare.onHover, bare.onActive, bare.onFocused }) state.background = null;
        return _bare[style] = bare;
    }

    /// <summary>
    /// A button with the game's look: an ordinary button is its framed button, lit under the pointer as the game's are; a
    /// primary one is lit; tabs and list rows show the option bar under the pointer and the warm one when selected.
    /// </summary>
    private bool SkinnedButton(Rect r, GUIContent content, GUIStyle style)
    {
        bool framed = style == _button || style == _launcher, primary = style == _primary;
        bool selected = style == _tabSelected || style == _listSelected || style == _routeItemSelected, quiet = style == _tabButton || style == _listItem || style == _routeItem;
        if (!framed && !primary && !selected && !quiet || !GameSkin.Has(GameSkin.Part.Button)) return GUI.Button(r, content, style);
        bool hover = GUI.enabled && _hoverAllowed && r.Contains(Event.current.mousePosition);
        var tint = GUI.enabled ? Color.white : new Color(1, 1, 1, .45f);
        if (framed || primary) GameSkin.Draw(r, (primary || hover) && GameSkin.Has(GameSkin.Part.ButtonLit) ? GameSkin.Part.ButtonLit : GameSkin.Part.Button, _scale, tint);
        else if (selected) GameSkin.Draw(r, GameSkin.Has(GameSkin.Part.RowSelected) ? GameSkin.Part.RowSelected : GameSkin.Part.Row, _scale, tint);
        else if (hover) GameSkin.Draw(r, GameSkin.Part.Row, _scale, tint);
        return GUI.Button(r, content, Bare(style));
    }

    /// <summary>A text field over the game's input bar (or its plain look).</summary>
    private string InputField(Rect r, string text, int max = -1)
    {
        var style = GameSkin.Draw(r, GameSkin.Part.Input, _scale, Color.white) ? Bare(_input) : _input;
        return max > 0 ? GUI.TextField(r, text, max, style) : GUI.TextField(r, text, style);
    }

    private bool DialogButton(Rect r, string text, string name, bool primary = false) => Button(r, text, "dialog." + name, primary ? _primary : _button);

    private void DrawWarning(Rect r)
    {
        var target = _jumpTarget!;
        Label(new Rect(r.x, r.y, r.width, S(30)), "直接跳转可能出错", _heading);
        string date = target.Date is { Length: 3 } d && d[0] > 0 ? RouteDate(d) : L.Text("原来的日期");
        string body = L.Format("「{0}」没有收录进度。直接跳转会以{1}的数值和剧情标记为基础，把剧情改到这一段的开头、日期改到{2}。", target.Title, JumpBase?.Invoke() ?? L.Text("当前进度"), date);
        float bodyHeight = TextHeight(_label, body, r.width);
        Label(new Rect(r.x, r.y + S(38), r.width, bodyHeight), body, _label);
        string risk = L.Text("中间剧情本该设置的标记不会有：之后可能出现对话对不上、分支判断出错，甚至卡住无法继续。只建议用来试看，不要在这个存档上长期玩下去。");
        Label(new Rect(r.x, r.y + S(46) + bodyHeight, r.width, TextHeight(_unmetSmall, risk, r.width)), risk, _unmetSmall);
        float yes = ButtonWidth("仍然直接跳转", 120), no = ButtonWidth("取消", 80);
        if (DialogButton(new Rect(r.xMax - yes - no - S(10), r.yMax - S(32), yes, S(32)), "仍然直接跳转", "warning.yes")) OpenSlots();
        if (DialogButton(new Rect(r.xMax - no, r.yMax - S(32), no, S(32)), "取消", "warning.no", true)) CloseDialog();
    }

    private void DrawSlots(Rect r)
    {
        var target = _jumpTarget!;
        Label(new Rect(r.x, r.y, r.width, S(30)), "选择存档格", _heading);
        string intro = L.Format("把「{0}」写进一个手动存档格，写好后立刻读取，进入这一段。可以选空格，也可以覆盖已有存档（会再确认一次）。", target.Title)
            + (InGame ? L.Text("当前没有保存的进度会丢失。") : "");
        float introHeight = TextHeight(_small, intro, r.width);
        Label(new Rect(r.x, r.y + S(34), r.width, introHeight), intro, _small);
        float top = r.y + S(42) + introHeight, cancel = ButtonWidth("取消", 80);
        var list = new Rect(r.x, top, r.width, r.yMax - S(42) - top);
        GUI.DrawTexture(new Rect(list.x, list.y - S(4), list.width, 1), _line);
        float row = S(40), content = list.width - S(18);
        _dialogScroll = BeginScroll(list, _dialogScroll, new Rect(0, 0, content, Mathf.Max(list.height, _slotLines.Count * row)));
        try
        {
            for (int i = 0; i < _slotLines.Count; i++)
            {
                float y = i * row;
                if (y + row < _dialogScroll.y || y > _dialogScroll.y + list.height) continue;
                var line = _slotLines[i];
                var rect = new Rect(0, y, content, row - S(2));
                string tip = line.Empty ? L.Text("空格：直接写入") : L.Text("已有存档：覆盖前会再确认");
                if (Button(rect, "", "slot." + line.Slot, _routeItem, tip)) { if (line.Empty) JumpInto(line); else { _slotPick = line; _dialog = JumpDialog.Overwrite; } }
                Label(new Rect(S(10), y + S(8), S(86), S(24)), L.Format("第 {0} 格", int.TryParse(line.Slot, out int n) ? n : 0), _label);
                if (line.Empty) Label(new Rect(S(100), y + S(8), content - S(110), S(24)), "空", _muted);
                else
                {
                    Label(new Rect(S(100), y + S(8), S(170), S(24)), (line.Star ? "★ " : "") + line.Date, _label);
                    Label(new Rect(S(276), y + S(10), S(150), S(22)), line.Saved, _small);
                    if (line.Note.Length > 0) Label(new Rect(S(430), y + S(10), content - S(440), S(22)), line.Note, _small, line.Note);
                }
                GUI.DrawTexture(new Rect(0, y + row - 1, content, 1), _line);
            }
        }
        finally { EndScroll(); }
        if (DialogButton(new Rect(r.xMax - cancel, r.yMax - S(32), cancel, S(32)), "取消", "slots.cancel")) CloseDialog();
    }

    private void DrawOverwrite(Rect r)
    {
        var line = _slotPick!;
        int number = int.TryParse(line.Slot, out int n) ? n : 0;
        Label(new Rect(r.x, r.y, r.width, S(30)), L.Format("覆盖第 {0} 格？", number), _heading);
        string body = L.Format("原来的存档（{0} · 存于 {1}）会被「{2}」替换，无法恢复。", line.Date, line.Saved, _jumpTarget!.Title)
            + (line.Star ? L.Text("这是收藏的存档，星标和备注也会一起清掉。") : "");
        Label(new Rect(r.x, r.y + S(40), r.width, TextHeight(line.Star ? _unmet : _label, body, r.width)), body, line.Star ? _unmet : _label);
        float yes = ButtonWidth("覆盖并跳转", 110), back = ButtonWidth("返回", 80);
        if (DialogButton(new Rect(r.xMax - yes - back - S(10), r.yMax - S(32), yes, S(32)), "覆盖并跳转", "overwrite.yes")) JumpInto(line);
        if (DialogButton(new Rect(r.xMax - back, r.yMax - S(32), back, S(32)), "返回", "overwrite.back", true)) { _slotPick = null; _dialog = JumpDialog.Slots; }
    }

    // 说明: the jump rules first, then how to read the map and what the controls do.
    private static readonly (string Title, string[] Lines)[] HelpText =
    {
        ("跳转", new[]
        {
            "「跳转」从模组收录的进度开始一段剧情。装着赵活系统玩到有选项、掷骰或战斗的剧情段时，会自动收录这一段开头的完整进度；每段留最新一份，加了收藏（☆）的那份不会被新收录覆盖。地图和列表里带 ▶ 的就是有收录进度、可以跳转的。",
            "「直接跳转」用于没有收录进度的地方：以当前进度（标题画面时是最近存档）的数值和剧情标记为基础，把剧情改到这一段开头、日期改到这一段的日期。中间剧情本该设置的标记不会有，可能出现对话对不上、分支出错甚至卡住，只建议试看。",
            "点了之后先选一个手动存档格：空格直接写入，已有存档会再确认一次是否覆盖。写好后立刻读取，进入这一段；当前没有保存的进度会丢失。"
        }),
        ("怎么看图", new[]
        {
            "横轴是游戏日历，每条车道是一条路线。圆点是主线的一站，方块是会改走主线的支线（襄阳行、山洞九日、雪山行）；下方的胶囊是支线，按怎么进入分成三组。",
            "颜色越偏黄越少读过（全部周目），越偏灰读得越多。蓝圈是这一局走过的站，淡蓝圈是推断走过的，金圈是下一个节点。虚线经过自由行动，上方拱起的线是跳过时间；「终」表示这里有结局，「+N」是变体。",
            "点一站或一段支线，右侧列出它的小节、进入条件和从这里岔出去的支线；点右侧带 › 的名字，地图会移过去并闪一下标出它。"
        }),
        ("操作", new[]
        {
            "按住左键拖动地图；滚轮上下移动，Shift + 滚轮左右移动，Ctrl + 滚轮或上方的 −、+ 缩放。地图和右侧之间的竖线可以拖动调整宽度。",
            "上方「分岔点」按钮移到主线分开的地方；「显示下一个节点」移到这一局的下一站；「其他内容」列出没画在时间轴上的闲聊、地点和会议事件；「刷新」重新读取游戏数据生成路线图。"
        })
    };

    /// <summary>
    /// 决定它的选项 (F101): each flag the branch reads with its value then, and the options that set it; one kept by 剧情回顾
    /// can be jumped back to (跳转's slot picker), the rest are named only.
    /// </summary>
    private void DrawSources(Rect r)
    {
        Label(new Rect(r.x, r.y, r.width, S(30)), "决定这个分支的选项", _heading);
        string intro = _sourceBranch + "\n" + L.Text("下面是这个分支读的标记，和剧本里设置它们的选项。回到那一段重新选，之后就可能走另一支；只有收录过的段落能跳转（每段只留最新一份），之后的进度要重新玩。");
        float introHeight = TextHeight(_small, intro, r.width);
        Label(new Rect(r.x, r.y + S(34), r.width, introHeight), intro, _small);
        float top = r.y + S(42) + introHeight, close = ButtonWidth("取消", 80);
        var list = new Rect(r.x, top, r.width, r.yMax - S(42) - top);
        GUI.DrawTexture(new Rect(list.x, list.y - S(4), list.width, 1), _line);
        if (SourcesReady?.Invoke() != true)
            Label(new Rect(list.x, list.y + S(8), list.width, S(26)), L.Format("正在查找设置这些标记的剧本……{0}%", Mathf.RoundToInt((SourcesProgress?.Invoke() ?? 0) * 100)), _muted);
        else
        {
            if (_sourceRows == null)
            {
                try { _sourceRows = _sourceFlags.Select(flag => FindSources?.Invoke(flag.Id) ?? new List<SourceRow>()).ToList(); }
                catch (Exception ex) { _sourceRows = _sourceFlags.Select(_ => new List<SourceRow>()).ToList(); Message(L.Format("查找失败：{0}", ex.GetBaseException().Message), true); }
            }
            float row = S(34), heading = S(34), content = list.width - S(18), jump = ButtonWidth("跳转", 72), textWidth = content - jump - S(24), total = 0;
            // A long option wraps; its row grows to hold it.
            float RowHeight(SourceRow source) => Mathf.Max(row, TextHeight(_label, source.Text, textWidth) + S(8));
            for (int i = 0; i < _sourceFlags.Count; i++) total += heading + (_sourceRows[i].Count == 0 ? row : _sourceRows[i].Sum(RowHeight)) + S(6);
            _dialogScroll = BeginScroll(list, _dialogScroll, new Rect(0, 0, content, Mathf.Max(list.height, total)));
            try
            {
                float y = 0;
                for (int i = 0; i < _sourceFlags.Count; i++)
                {
                    var flag = _sourceFlags[i];
                    Label(new Rect(0, y + S(6), content, S(26)), L.Format("{0} · 当时 {1}", flag.Name, flag.Value), _caption, flag.Id);
                    y += heading;
                    if (_sourceRows[i].Count == 0)
                    {
                        Label(new Rect(S(10), y + S(5), content - S(10), S(24)), "剧本里没找到设置它的选项：可能由任务或游戏系统设置。", _muted);
                        y += row;
                    }
                    foreach (var source in _sourceRows[i])
                    {
                        float height = RowHeight(source);
                        Label(new Rect(S(10), y + S(4), textWidth, height - S(4)), source.Text, source.Node != null ? _label : _muted, source.Text);
                        if (source.Node is { } node)
                        {
                            if (Button(new Rect(content - jump, y + S(2), jump, S(30)), "跳转", "source.jump." + i + "." + node.Id, tooltip: L.Text("选一个存档格，写好后直接进入这一段")))
                            { OpenJump(new JumpTarget { Title = source.Title, Node = node, Script = node.Script }, false); break; }
                        }
                        else Label(new Rect(content - jump, y + S(7), jump, S(22)), "未收录", _small, L.Text("没玩到过这一段，或收录在装模组之前"));
                        GUI.DrawTexture(new Rect(S(10), y + height - 1, content - S(10), 1), _line);
                        y += height;
                    }
                    y += S(6);
                }
            }
            finally { EndScroll(); }
        }
        if (DialogButton(new Rect(r.xMax - close, r.yMax - S(32), close, S(32)), "取消", "sources.close")) CloseDialog();
    }

    private void DrawHelp(Rect r)
    {
        Label(new Rect(r.x, r.y, r.width, S(30)), "说明", _heading);
        var list = new Rect(r.x, r.y + S(40), r.width, r.height - S(84));
        float content = list.width - S(18), total = 0;
        foreach (var (title, lines) in HelpText)
        {
            total += S(34);
            foreach (string line in lines) total += TextHeight(_label, L.Text(line), content) + S(8);
        }
        _dialogScroll = BeginScroll(list, _dialogScroll, new Rect(0, 0, content, Mathf.Max(list.height, total)));
        try
        {
            float y = 0;
            foreach (var (title, lines) in HelpText)
            {
                Label(new Rect(0, y + S(4), content, S(26)), title, _caption);
                y += S(34);
                foreach (string line in lines)
                {
                    float h = TextHeight(_label, L.Text(line), content);
                    Label(new Rect(0, y, content, h), line, _label);
                    y += h + S(8);
                }
            }
        }
        finally { EndScroll(); }
        float close = ButtonWidth("知道了", 90);
        if (DialogButton(new Rect(r.xMax - close, r.yMax - S(32), close, S(32)), "知道了", "help.close", true)) CloseDialog();
    }
}
