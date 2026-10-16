using System;
using System.Collections.Generic;

namespace ZhaoHuoSystem;

public sealed class ConditionSnapshot
{
    public string Text { get; set; } = "";
    public string Detail { get; set; } = "";
    public bool Satisfied { get; set; }
    public bool Known { get; set; } = true;
    public string FieldId { get; set; } = "";
    public int? SuggestedValue { get; set; }
    public List<ConditionSnapshot> Children { get; set; } = new();
    public string Operator { get; set; } = "";
}

public sealed class EventSnapshot
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public bool Eligible { get; set; }
    public int Weight { get; set; }
    public List<ConditionSnapshot> Conditions { get; set; } = new();
    /// <summary>What the event's script does once it runs, in the option hints' forms; empty when nothing is known.</summary>
    public List<HintSegment> Consequences { get; set; } = new();
}

public sealed class ValueSnapshot
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Group { get; set; } = "";
    public string GroupId { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Reason { get; set; } = "";
    public string SearchText { get; set; } = "";
    public string Kind { get; set; } = "integer";
    public int Value { get; set; }
    public int? BaseValue { get; set; }
    public int? AdditionValue { get; set; }
    public int Min { get; set; } = int.MinValue;
    public int Max { get; set; } = int.MaxValue;
    public bool Writable { get; set; }
    public Dictionary<int, string> Options { get; set; } = new();
    // Informational mapping for hover only; never changes the integer editor to an enum selector.
    public Dictionary<int, string> EnumValues { get; set; } = new();
}

// Map entries are informational. Their identifiers never enter the actionable event pool.
public sealed class MapLocationSnapshot
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Detail { get; set; } = "";
    public bool Visible { get; set; }
    public bool HasScreenPosition { get; set; }
    public float ScreenX { get; set; }
    public float ScreenY { get; set; }
    public List<MapEventSnapshot> Entries { get; set; } = new();
    /// <summary>The location's own entry script, and whether one-shot events reachable from it are surfaced here.</summary>
    public string EntryScript { get; set; } = "";
    public bool SurfaceReachable { get; set; }
}

public sealed class MapEventSnapshot
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public bool Ready { get; set; }
    public bool Known { get; set; }
    public List<ConditionSnapshot> Conditions { get; set; } = new();
}

public sealed class DiceBranchSnapshot
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public bool Reachable { get; set; }
    /// <summary>Raw dice value that lands on this branch; may lie outside the native range when the branch is only
    /// reachable by working back from the total. Null when even that is impossible and the branch is forced.</summary>
    public int? RawValue { get; set; }
    /// <summary>Share of the native raw range landing on this branch; null when the range was too wide to count.</summary>
    public double? Chance { get; set; }
}

public sealed class DiceSnapshot
{
    public bool Active { get; set; }
    public bool CanSpecify { get; set; }
    public bool ManualStart { get; set; }
    public string Name { get; set; } = "";
    public string Detail { get; set; } = "";
    public int Min { get; set; } = 0;
    public int Max { get; set; } = 100;
    public int Addition { get; set; }
    public int? RawValue { get; set; }
    public int? PendingRawValue { get; set; }
    public List<DiceBranchSnapshot> Branches { get; set; } = new();
}

public sealed class ToolSnapshot
{
    public long Revision { get; set; }
    public bool Ready { get; set; }
    public string Version { get; set; } = "";
    public string Location { get; set; } = "";
    public string Action { get; set; } = "";
    public string ContextDetail { get; set; } = "";
    public string Status { get; set; } = "";
    public string PendingEventId { get; set; } = "";
    /// <summary>While an action waits: the event specified last time at this same action, if it is in the pool now.</summary>
    public string RepeatEventId { get; set; } = "";
    public bool ActionSelectionEnabled { get; set; } = true;
    public bool ActionWaiting { get; set; }
    public List<EventSnapshot> Events { get; set; } = new();
    /// <summary>Hovered menu row's pool, read-only; empty whenever a real checkpoint pool exists.</summary>
    public string PreviewTitle { get; set; } = "";
    public List<EventSnapshot> PreviewEvents { get; set; } = new();
    public List<ValueSnapshot> Values { get; set; } = new();
    public List<MapLocationSnapshot> MapLocations { get; set; } = new();
    public DiceSnapshot Dice { get; set; } = new();
}

public sealed class OperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public static OperationResult Ok(string message) => new() { Success = true, Message = message };
    public static OperationResult Fail(string message) => new() { Message = message };
}

public interface IGameAdapter
{
    ToolSnapshot Snapshot { get; }
    void Refresh();
    OperationResult SetValue(string fieldId, string input);
    OperationResult SpecifyEvent(string eventId);
    OperationResult CancelEvent();
    OperationResult SetActionSelection(bool enabled);
    OperationResult ContinueAction();
    /// <summary>Specify the event chosen last time at this same action, then continue it.</summary>
    OperationResult RepeatLastChoice();
    OperationResult SpecifyDice(int rawValue);
    OperationResult SpecifyDiceBranch(string branchId);
    /// <summary>Specify a reachable branch, then press the game's own start (or restart) once.</summary>
    OperationResult RollDiceBranch(string branchId);
    OperationResult CancelDice();
}

/// <summary>
/// The mod's single colour meaning. Green = positive: a condition holds, a value rises, something is gained.
/// Red = negative: a condition fails, a value falls, something is lost. Text carries invisible marks;
/// each surface turns them into its own shade (light on dark panels, deep on the game's cream rows).
/// </summary>
public static class Tone
{
    public const char Positive = '\uE000', Negative = '\uE001', End = '\uE002';
    public const string PositiveOnDark = "#8FDEA3", NegativeOnDark = "#FF9494", PositiveOnLight = "#2E7D3C", NegativeOnLight = "#B03030";
    /// <summary>Marks text positive (sign &gt; 0) or negative (sign &lt; 0); zero stays plain.</summary>
    public static string Mark(int sign, string text) => text.Length == 0 || sign == 0 ? text : (sign > 0 ? Positive : Negative) + text + End;
    public static string Plain(string text) => text.Replace(Positive.ToString(), "").Replace(Negative.ToString(), "").Replace(End.ToString(), "");
}

/// <summary>One consequence segment in three forms: one line, the inline overview, and the hover's lines.</summary>
public sealed class HintSegment
{
    /// <summary>Everything on one line, e.g. 「若 道德 45>=60：变心+1 攻略小梅=1」 (shop lines).</summary>
    public string Full = "";
    /// <summary>Inline overview: arm conditions left to the hover, story flags only counted, e.g. 「若…：变心+1 标记×1」.</summary>
    public string Summary = "";
    /// <summary>Hover: the requirement, condition or marker line; for an arm, its effects follow on their own line.</summary>
    public string Head = "", Body = "";
    /// <summary>1 met, -1 unmet, 0 neutral or unknown.</summary>
    public int State;
}
