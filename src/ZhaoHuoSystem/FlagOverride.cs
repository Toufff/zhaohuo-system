namespace ZhaoHuoSystem;

/// <summary>
/// 按此改标记 (F109): in 决定这个分支的选项, an option on a route never played is taken by writing the flag value it gives
/// into the start of the segment where the branch was decided, then entering that start; the branch is decided again by
/// the game. Whether a row can do that, and the value it writes.
/// </summary>
internal static class FlagOverride
{
    internal enum State
    {
        Ready,
        /// <summary>The start already has the value the option gives.</summary>
        Same,
        /// <summary>The segment's start was not kept (a 回想 line from before a load or a scene change).</summary>
        NoStart,
        /// <summary>The option, or the script setting the flag, is the segment itself: replaying it sets the flag again.</summary>
        InSegment,
    }

    /// <summary>The flag is <paramref name="before"/> at the segment's start; the option sets it to, or adds, <paramref name="value"/>.</summary>
    internal static State Plan(bool hasStart, bool inSegment, int before, bool set, int value, out int after)
    {
        after = set ? value : before + value;
        if (inSegment) return State.InSegment;
        if (!hasStart) return State.NoStart;
        return after == before ? State.Same : State.Ready;
    }
}
