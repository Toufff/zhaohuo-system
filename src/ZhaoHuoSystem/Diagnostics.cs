using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx.Logging;
using UnityEngine;

namespace ZhaoHuoSystem;

/// <summary>
/// The mod's own session log. Each game start writes one file under BepInEx/ZhaoHuoSystem/logs and the last five are
/// kept, so restarting after a problem never erases it. Only this plugin's lines and exceptions raised in its code are
/// recorded, each with time and scene. A repeated warning is written three times and then only counted.
/// </summary>
internal sealed class Diagnostics : ILogListener
{
    private const int KeptSessions = 5, RepeatLimit = 3;
    private readonly string source;
    private readonly StreamWriter? writer;
    private readonly Dictionary<string, int> repeats = new(StringComparer.Ordinal);
    private readonly object gate = new();
    public string LogDirectory { get; }
    /// <summary>Kept current by the plugin on scene change; log lines may arrive from any thread.</summary>
    public static volatile string Scene = "";
    public LogLevel LogLevelFilter => LogLevel.All;

    public Diagnostics(string root, string source, string header)
    {
        this.source = source; LogDirectory = Path.Combine(root, "logs");
        try
        {
            Directory.CreateDirectory(LogDirectory);
            foreach (var old in new DirectoryInfo(LogDirectory).GetFiles("session-*.log").OrderByDescending(file => file.Name).Skip(KeptSessions - 1)) old.Delete();
            string path = Path.Combine(LogDirectory, "session-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".log");
            writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };
            writer.WriteLine(header);
        }
        catch (Exception) { writer = null; }
        Application.logMessageReceived += UnityMessage;
    }

    public void LogEvent(object sender, LogEventArgs eventArgs)
    {
        if (eventArgs.Source?.SourceName != source) return;
        Write(eventArgs.Level.ToString(), eventArgs.Data?.ToString() ?? "", (eventArgs.Level & (LogLevel.Warning | LogLevel.Error | LogLevel.Fatal)) != 0);
    }
    // Exceptions from this plugin's code that escaped every guard, e.g. inside a Harmony patch.
    private void UnityMessage(string condition, string stackTrace, LogType type)
    {
        // Lines this plugin logged itself may come back through Unity's log; they are already written.
        if ((type == LogType.Exception || type == LogType.Error) && stackTrace != null && stackTrace.IndexOf("ZhaoHuoSystem", StringComparison.Ordinal) >= 0
            && (condition == null || condition.IndexOf(source, StringComparison.Ordinal) < 0))
            Write("Exception", condition + "\n" + stackTrace.Trim(), true);
    }
    private void Write(string level, string message, bool counted)
    {
        lock (gate)
        {
            if (writer == null) return;
            if (counted)
            {
                repeats.TryGetValue(message, out int seen); repeats[message] = ++seen;
                if (seen > RepeatLimit) return;
                if (seen == RepeatLimit) message += "  (further repeats are only counted)";
            }
            try { writer.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " [" + level + "] [" + Scene + "] " + message); }
            catch (Exception) { }
        }
    }
    /// <summary>Warnings and errors seen more often than written.</summary>
    public string RepeatSummary()
    {
        lock (gate) return string.Join("\n", repeats.Where(pair => pair.Value > RepeatLimit).OrderByDescending(pair => pair.Value).Select(pair => pair.Value + "x  " + pair.Key));
    }
    /// <summary>Copies the kept session logs, including the one still being written.</summary>
    public void CopyLogs(string target)
    {
        foreach (var file in new DirectoryInfo(LogDirectory).GetFiles("session-*.log"))
        {
            using var input = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var output = File.Create(Path.Combine(target, file.Name));
            input.CopyTo(output);
        }
    }
    public void Dispose()
    {
        Application.logMessageReceived -= UnityMessage;
        lock (gate)
        {
            try
            {
                string summary = RepeatSummary();
                if (summary.Length > 0) writer?.WriteLine("REPEATS\n" + summary);
                writer?.Dispose();
            }
            catch (Exception) { }
        }
    }
}

/// <summary>
/// Time spent in this plugin's own code per section: the current minute plus session totals. Allocation-free; main
/// thread only. Once a minute a warning is logged if the mod cost a noticeable share of the frame.
/// </summary>
internal static class Perf
{
    private sealed class Stat { public long Count, Ticks, Max, WindowCount, WindowTicks, WindowMax; }
    private static readonly Dictionary<string, Stat> stats = new(StringComparer.Ordinal);
    private static long frames, windowFrames, windowStart = Stopwatch.GetTimestamp();
    private const double FrameBudgetMs = 1, SpikeMs = 33;

    public readonly struct Scope : IDisposable
    {
        private readonly string name; private readonly long start;
        public Scope(string name) { this.name = name; start = Stopwatch.GetTimestamp(); }
        public void Dispose() => Add(name, Stopwatch.GetTimestamp() - start);
    }
    public static Scope Time(string name) => new(name);
    private static void Add(string name, long ticks)
    {
        if (!stats.TryGetValue(name, out var stat)) stats[name] = stat = new Stat();
        stat.Count++; stat.Ticks += ticks; stat.Max = Math.Max(stat.Max, ticks);
        stat.WindowCount++; stat.WindowTicks += ticks; stat.WindowMax = Math.Max(stat.WindowMax, ticks);
    }
    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    /// <summary>Call once per frame. Returns a warning line when the closing minute was noticeably expensive.</summary>
    public static string? Frame()
    {
        frames++; windowFrames++;
        long now = Stopwatch.GetTimestamp();
        if (Ms(now - windowStart) < 60000) return null;
        string? warning = null;
        double perFrame = windowFrames == 0 ? 0 : stats.Where(pair => pair.Key == "update" || pair.Key == "gui").Sum(pair => Ms(pair.Value.WindowTicks)) / windowFrames;
        var spikes = stats.Where(pair => Ms(pair.Value.WindowMax) >= SpikeMs).Select(pair => pair.Key + " " + Ms(pair.Value.WindowMax).ToString("0.0", CultureInfo.InvariantCulture) + "ms").ToList();
        if (perFrame >= FrameBudgetMs || spikes.Count > 0)
            warning = "PERF mod " + perFrame.ToString("0.00", CultureInfo.InvariantCulture) + "ms/frame over " + windowFrames + " frames" + (spikes.Count > 0 ? "; spikes: " + string.Join(", ", spikes) : "") + "; " + Line(window: true);
        foreach (var stat in stats.Values) stat.WindowCount = stat.WindowTicks = stat.WindowMax = 0;
        windowFrames = 0; windowStart = now;
        return warning;
    }
    private static string Line(bool window) => string.Join("; ", stats.OrderByDescending(pair => window ? pair.Value.WindowTicks : pair.Value.Ticks).Select(pair =>
    {
        var stat = pair.Value; long count = window ? stat.WindowCount : stat.Count, ticks = window ? stat.WindowTicks : stat.Ticks, max = window ? stat.WindowMax : stat.Max;
        return pair.Key + " n=" + count + " avg=" + (count == 0 ? 0 : Ms(ticks) / count).ToString("0.000", CultureInfo.InvariantCulture) + "ms max=" + Ms(max).ToString("0.0", CultureInfo.InvariantCulture) + "ms";
    }));
    public static string Report()
    {
        double perFrame = frames == 0 ? 0 : stats.Where(pair => pair.Key == "update" || pair.Key == "gui").Sum(pair => Ms(pair.Value.Ticks)) / frames;
        return "frames=" + frames + " mod=" + perFrame.ToString("0.000", CultureInfo.InvariantCulture) + "ms/frame\nsession: " + Line(window: false).Replace("; ", "\n  ")
            + "\ncurrent minute: " + Line(window: true).Replace("; ", "\n  ");
    }
}
