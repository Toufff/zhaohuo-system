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
    OperationResult SpecifyDice(int rawValue);
    OperationResult SpecifyDiceBranch(string branchId);
    /// <summary>Specify a reachable branch, then press the game's own start (or restart) once.</summary>
    OperationResult RollDiceBranch(string branchId);
    OperationResult CancelDice();
}
