using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace ZhaoHuoSystem;

internal static class R
{
    private static readonly ConcurrentDictionary<string, FieldInfo> Fields = new();
    public static object? Field(object? obj, string name)
    {
        if (obj == null) return null;
        var type = obj.GetType();
        var key = type.FullName + ":" + name;
        if (!Fields.TryGetValue(key, out var field))
        {
            for (var current = type; current != null && field == null; current = current.BaseType)
                field = current.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) return null;
            Fields[key] = field;
        }
        return field.GetValue(obj);
    }
    public static T Field<T>(object? obj, string name, T fallback = default!) => Field(obj, name) is T value ? value : fallback;
    public static T RequiredField<T>(object? obj, string name) => Field(obj, name) is T value ? value : throw new MissingFieldException(obj?.GetType().FullName ?? "null", name);
    public static void Set(object obj, string name, object value)
    {
        Field(obj, name);
        Fields[obj.GetType().FullName + ":" + name].SetValue(obj, value);
    }
    /// <summary>A method called with its optional parameters at their defaults: game updates add some (build 26237817 gave
    /// SaveSystem.CreateSaveData its includeStoryLog), and an Invoke with fewer arguments throws.</summary>
    public static object? Call(MethodInfo? method, object? target)
    {
        if (method == null) return null;
        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];
        for (int i = 0; i < parameters.Length; i++) args[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : Type.Missing;
        return method.Invoke(target, args);
    }
    public static object? Property(object? obj, string name) => obj?.GetType().GetProperty(name)?.GetValue(obj);
    public static int IntProperty(object? obj, string name) => Property(obj, name) is int value ? value : 0;
    public static string Name(object? obj) => obj is UnityEngine.Object asset ? asset.name : "";
    public static IEnumerable<object> Items(object? obj)
    {
        if (obj is IEnumerable list) foreach (var item in list) if (item != null) yield return item;
    }
}
