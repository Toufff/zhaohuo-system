using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ZhaoHuoSystem;

/// <summary>
/// Favourites and notes for the manual save slots, kept in a file of the mod's own (F50). No save file is read or
/// written for them, so removing the mod leaves every save exactly as it was. One line per slot: the game's account
/// folder and slot number ("76561198…/003"), 1 or 0 for the star, and the note, separated by tabs.
/// </summary>
public sealed class SaveNoteStore
{
    public const int MaxNote = 60;
    private const string Header = "# 赵活系统：存档收藏与备注。只有本模组读取，游戏存档不受影响；删掉本文件即全部清空。";
    private sealed class Entry { public bool Favourite; public string Note = ""; }
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly string? path;

    public SaveNoteStore(string? path)
    {
        this.path = path;
        if (path == null || !File.Exists(path)) return;
        foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var parts = line.Split(new[] { '\t' }, 3);
            if (parts.Length < 2 || parts[0].Length == 0) continue;
            var entry = new Entry { Favourite = parts[1] == "1", Note = Clean(parts.Length > 2 ? parts[2] : "") };
            if (entry.Favourite || entry.Note.Length > 0) entries[parts[0]] = entry;
        }
    }

    public static string Key(string account, string slot) => account + "/" + slot;
    public bool Favourite(string key) => entries.TryGetValue(key, out var entry) && entry.Favourite;
    public string Note(string key) => entries.TryGetValue(key, out var entry) ? entry.Note : "";
    public void SetFavourite(string key, bool favourite) => Change(key, favourite, Note(key));
    /// <summary>An empty note removes it.</summary>
    public void SetNote(string key, string note) => Change(key, Favourite(key), Clean(note));
    /// <summary>The save itself was deleted in game: its star and note go with it.</summary>
    public bool Remove(string key)
    {
        if (!entries.Remove(key)) return false;
        Write(); return true;
    }

    /// <summary>One line, no tabs, at most <see cref="MaxNote"/> characters (never cutting a character in half).</summary>
    public static string Clean(string note)
    {
        var text = new StringBuilder(note.Length);
        foreach (char c in note) text.Append(char.IsControl(c) ? ' ' : c);
        string clean = text.ToString().Trim();
        if (clean.Length <= MaxNote) return clean;
        int cut = char.IsHighSurrogate(clean[MaxNote - 1]) ? MaxNote - 1 : MaxNote;
        return clean.Substring(0, cut).TrimEnd();
    }

    private void Change(string key, bool favourite, string note)
    {
        bool had = entries.TryGetValue(key, out var old);
        if (had && old!.Favourite == favourite && old.Note == note || !had && !favourite && note.Length == 0) return;
        if (favourite || note.Length > 0) entries[key] = new Entry { Favourite = favourite, Note = note };
        else entries.Remove(key);
        Write();
    }

    /// <summary>Written whole on every change, through a temporary file so a crash never leaves half a file.</summary>
    private void Write()
    {
        if (path == null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var lines = new List<string> { Header };
        lines.AddRange(entries.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => entry.Key + "\t" + (entry.Value.Favourite ? "1" : "0") + "\t" + entry.Value.Note));
        string temporary = path + ".tmp";
        File.WriteAllLines(temporary, lines, new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);
    }
}
