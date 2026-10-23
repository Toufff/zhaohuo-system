using System;
using UnityEngine;

namespace ZhaoHuoSystem;

// F110: the first time the panel opens, a player with no ending in 汗青书 is asked once to play the first run without mods.
// Nothing is locked. 「先关掉剧透类显示」 turns off the overlays that give the story away; 设置's top row turns them all on
// or off again.
public sealed partial class ToolWindow
{
    /// <summary>An ending is lit in 汗青书; null while the game's library is not loaded.</summary>
    internal Func<bool?>? HasEnding;
    /// <summary>The reminder was answered (or an ending was found): it is not shown again.</summary>
    internal bool FirstRunDone;
    internal Action? FirstRunHandled;
    // 回想中的剧情分支 as it was before the spoilers went off, for turning them on again.
    private int _logModeBeforeSpoilers;

    private void MaybeShowFirstRun()
    {
        if (FirstRunDone || _dialog != JumpDialog.None) return;
        switch (HasEnding?.Invoke())
        {
            case true: FinishFirstRun(); break;
            case false: _dialog = JumpDialog.FirstRun; _dialogScroll = Vector2.zero; GUI.FocusControl(null); break;
        }
    }
    private void FinishFirstRun() { FirstRunDone = true; FirstRunHandled?.Invoke(); }

    private const string FirstRunBody = "强烈建议第一周目不用任何模组，原版打到「最后的暗器」或「武林传奇」结局，再回来用赵活系统。";

    private void DrawFirstRun(Rect r)
    {
        Label(new Rect(r.x, r.y, r.width, S(30)), "先自己打一周目吧", _heading);
        Label(new Rect(r.x, r.y + S(38), r.width, TextHeight(_label, L.Text(FirstRunBody), r.width)), FirstRunBody, _label);
        float ok = ButtonWidth("知道了", 80), quiet = ButtonWidth("先关掉剧透类显示", 150), done = ButtonWidth("我已通关，不再提示", 160), y = r.yMax - S(32);
        if (DialogButton(new Rect(r.x, y, ok, S(32)), "知道了", "firstrun.ok", true)) { FinishFirstRun(); CloseDialog(); return; }
        if (Button(new Rect(r.x + ok + S(10), y, quiet, S(32)), "先关掉剧透类显示", "dialog.firstrun.quiet", _button, L.Text("关闭剧情分支小框、地图事件数字、显示被隐藏的选项，回想中的剧情分支改为不显示。")))
        {
            SetSpoilers(false); FinishFirstRun(); CloseDialog();
            Message(L.Text("已关闭剧透类显示，可在「设置」页重新打开"), false);
            return;
        }
        if (Button(new Rect(r.xMax - done, y, done, S(32)), "我已通关，不再提示", "dialog.firstrun.done", _button, L.Text("以后不再出现这个提醒。"))) { FinishFirstRun(); CloseDialog(); }
    }

    /// <summary>How many of the spoiler overlays are on: 0 none, 1 some, 2 all.</summary>
    private int SpoilerState()
    {
        int on = (ShowBranches ? 1 : 0) + (ShowMapCounts ? 1 : 0) + (ShowHiddenOptions ? 1 : 0) + (LogBranches != 2 ? 1 : 0);
        return on == 0 ? 0 : on == 4 ? 2 : 1;
    }
    private void SetSpoilers(bool on)
    {
        if (!on && LogBranches != 2) _logModeBeforeSpoilers = LogBranches;
        ShowBranches = ShowMapCounts = ShowHiddenOptions = on;
        LogBranches = on ? _logModeBeforeSpoilers : 2;
        SettingsChanged?.Invoke();
    }
}
