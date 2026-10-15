using System;
using System.Collections.Generic;

namespace ZhaoHuoSystem;

internal enum FieldIdentity { New, SameReference, Ambiguous }

internal sealed class FieldIdentityIndex
{
    private readonly Dictionary<string, List<object>> objects = new(StringComparer.Ordinal);
    public void Clear() => objects.Clear();
    public FieldIdentity Register(string id, object source)
    {
        if (!objects.TryGetValue(id, out var previous)) { objects.Add(id, new List<object> { source }); return FieldIdentity.New; }
        foreach (var item in previous) if (ReferenceEquals(item, source)) return FieldIdentity.SameReference;
        previous.Add(source);
        return FieldIdentity.Ambiguous;
    }
}
