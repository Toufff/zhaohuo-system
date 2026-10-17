using ZhaoHuoSystem;

internal static partial class Program
{
    private static void SaveNoteTests()
    {
        Test("save stars and notes survive a reload, per account and slot, and empty entries leave the file", () =>
        {
            string folder = Path.Combine(Path.GetTempPath(), "zhaohuo-notes-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(folder, "save-notes.txt");
            try
            {
                var store = new SaveNoteStore(path);
                string a = SaveNoteStore.Key("76561198000000001", "003"), b = SaveNoteStore.Key("76561198000000002", "003");
                store.SetFavourite(a, true); store.SetNote(a, "  龙湘线\t分支前\n  "); store.SetNote(b, "另一个账号");
                var again = new SaveNoteStore(path);
                Require(again.Favourite(a) && again.Note(a) == "龙湘线 分支前", "one line, trimmed, kept with the star");
                Require(!again.Favourite(b) && again.Note(b) == "另一个账号", "accounts are kept apart");
                again.SetNote(b, " "); again.SetFavourite(a, false);
                Require(new SaveNoteStore(path).Note(a) == "龙湘线 分支前" && !File.ReadAllText(path).Contains("76561198000000002"), "an empty note with no star is dropped");
                Require(again.Remove(a) && !again.Remove(a) && new SaveNoteStore(path).Note(a) == "", "a deleted save takes its note");
                Require(!File.Exists(path + ".tmp"), "no temporary file left behind");
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        });
        Test("save notes are capped without splitting a character, and a missing file is an empty store", () =>
        {
            Equal(SaveNoteStore.MaxNote, SaveNoteStore.Clean(new string('长', 80)).Length);
            string pairs = string.Concat(Enumerable.Repeat("😀", 40));
            string cut = SaveNoteStore.Clean(pairs);
            Require(cut.Length <= SaveNoteStore.MaxNote && !char.IsHighSurrogate(cut[cut.Length - 1]), "surrogate pair kept whole");
            var empty = new SaveNoteStore(Path.Combine(Path.GetTempPath(), "zhaohuo-missing-" + Guid.NewGuid().ToString("N"), "save-notes.txt"));
            Require(empty.Note("Save/001") == "" && !empty.Favourite("Save/001"), "nothing read");
        });
    }
}
