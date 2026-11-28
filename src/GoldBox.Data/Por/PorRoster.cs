using System.Text;
using GoldBox.Core.Ecl;
using GoldBox.Data.Module;
using GoldBox.Data.Module.Por;

namespace GoldBox.Data.Por;

/// <summary>
/// The party menu's character files (Pool of Radiance DOS v1.3, GAME.OVR SaveLoad unit; docs/re/start-menu.md
/// "Roster"). The program keeps them on the save disk: <c>charlist.txt</c>, one name per line, and per character
/// <c>NAME.cha</c> (the record) with <c>.itm</c> / <c>.spc</c> when it has items / effects. Create and Remove write a
/// character (SaveChar 0x1E684 with no file name); Add lists <c>charlist.txt</c> (0x1DFF6) and loads (0x1EAD2).
/// <para>
/// The engine keeps the same list file and writes each character as one JSON file, a save's party entry
/// (<see cref="SavedCharacter"/>, docs/module-format.md §15). A character copied from the DOS game
/// (<c>NAME.CHA</c> and its <c>.ITM</c> / <c>.SPC</c>) is read when no JSON file of that name exists.
/// The program's file name is the name through its own clean-up (<see cref="DosStem"/>, START 0x8445, read s52); ours
/// keeps letters, digits, '-' and '_' and replaces the rest with '_' (two names that clean up the same share a file, as
/// in the program); a DOS file is looked up by the program's rule.
/// </para>
/// </summary>
public sealed class PorRoster(string folder)
{
    public const string ListFile = "charlist.txt";
    /// <summary>Party members' names are marked so in the list (LoadCharlist 0x1DFF6); marked lines are not written back.</summary>
    public const string Mark = "* ";

    public string Folder { get; } = folder;

    /// <summary>
    /// LoadCharlist (0x1DFF6): the names in the list file, in order; a name some party member has is marked
    /// (<see cref="Mark"/>). Empty when there is no list.
    /// </summary>
    public List<string> Entries(EclParty? party)
    {
        var path = Find(ListFile);
        if (path is null)
            return [];
        var names = party?.Members.Select(m => m.Name).ToHashSet(StringComparer.Ordinal) ?? [];
        return File.ReadAllLines(path, Encoding.Latin1)
            .Select(n => names.Contains(n) ? Mark + n : n)
            .ToList();
    }

    /// <summary>SaveCharlist (0x1E19E): the entries back to the list file, marked ones left out.</summary>
    public void WriteEntries(IEnumerable<string> entries)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllLines(Find(ListFile) ?? Path.Combine(Folder, ListFile),
            entries.Where(e => !e.StartsWith('*')), Encoding.Latin1);
    }

    /// <summary>
    /// SaveChar (0x1E684) with no file name: the list is re-read (party members marked) and written back with this name
    /// appended, so party members' lines drop out; then the character's file is written over any old one.
    /// </summary>
    public void Save(EclCharacter c, EclParty? party)
    {
        var entries = Entries(party);
        entries.Add(c.Name);
        WriteEntries(entries);
        var stem = Stem(c.Name);
        ModuleJson.Write(Path.Combine(Folder, stem + ".json"), PorEngineSave.Character(c, stem));
    }

    /// <summary>LoadChar (0x1EAD2): the character by its name, or null when no file has it. (The program allocates the
    /// first item before testing EOF, so an empty <c>.itm</c> would give one stale record; it never writes one.)</summary>
    public EclCharacter? Load(string name)
    {
        var stem = Stem(name);
        if (Find(stem + ".json") is { } json)
            return PorEngineSave.Character(ModuleJson.Read<SavedCharacter>(json));
        var dos = DosStem(name);
        if (Find(dos + ".cha") is not { } cha)
            return null;
        var c = new EclCharacter(File.ReadAllBytes(cha));
        foreach (var r in Records(Find(dos + ".itm"), EclCharacter.ItemRecordSize))
            c.Items.Add(r);
        foreach (var r in Records(Find(dos + ".spc"), EclCharacter.EffectRecordSize))
            c.Effects.Add(r);
        return c;
    }

    /// <summary>
    /// The game Save's roster deletes (0x1FD3E, 0x20124–0x2023C: after each member's save files, its <c>.cha</c>,
    /// <c>.itm</c> and <c>.spc</c> are erased, each when it exists), on this folder only: per member its JSON file and
    /// any DOS-named <c>.cha</c> / <c>.itm</c> / <c>.spc</c>, each when present. A character in a saved game is then no
    /// longer in the roster (no copies); Remove writes it back. An option, off by default (user s61): the Client runs
    /// it only when <c>RosterDeletes</c> is on. Returns the files removed.
    /// </summary>
    public int RemoveSaved(IEnumerable<EclCharacter> members)
    {
        int n = 0;
        foreach (var c in members)
            foreach (var f in Files(c.Name))
                n += Erase(f);
        return n;
    }

    /// <summary>
    /// The game Load's roster deletes (0x1F5E8, 0x1F9DD–0x1FA6B): per member, only when its character file exists (the
    /// program: <c>.cha</c>; here the JSON file or a DOS <c>.cha</c>) every one of its files present is erased; with no
    /// character file none is touched. Same option as <see cref="RemoveSaved"/>. Returns the files removed.
    /// </summary>
    public int RemoveLoaded(IEnumerable<EclCharacter> members)
    {
        int n = 0;
        foreach (var c in members)
        {
            var files = Files(c.Name);
            if (files[0] is null && files[1] is null)
                continue;
            foreach (var f in files)
                n += Erase(f);
        }
        return n;
    }

    /// <summary>A name's files here: JSON, <c>.cha</c>, <c>.itm</c>, <c>.spc</c> (null where absent).</summary>
    private string?[] Files(string name)
    {
        var dos = DosStem(name);
        return [Find(Stem(name) + ".json"), Find(dos + ".cha"), Find(dos + ".itm"), Find(dos + ".spc")];
    }

    private static int Erase(string? path)
    {
        if (path is null || !File.Exists(path))
            return 0;
        File.Delete(path);
        return 1;
    }

    private static IEnumerable<byte[]> Records(string? path, int size)
    {
        if (path is null)
            yield break;
        var b = File.ReadAllBytes(path);
        for (int o = 0; o + size <= b.Length; o += size)
            yield return b[o..(o + size)];
    }

    /// <summary>The file stem of a name (see the class remarks).</summary>
    public static string Stem(string name)
    {
        var sb = new StringBuilder();
        foreach (char ch in name)
            sb.Append(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' ? char.ToUpperInvariant(ch) : '_');
        return sb.Length > 0 ? sb.ToString() : "_";
    }

    /// <summary>The characters the program's clean-up removes, in its order (DS:0D1A, ten bytes).</summary>
    public const string DosRemoved = " .*,?/\\:;|";

    /// <summary>
    /// The program's file stem of a name (START 0x8445, read s52): each character of <see cref="DosRemoved"/> in turn is
    /// removed by START 0x8376, which walks the positions 1..(length at its start) and deletes a match without looking
    /// at the position again (so of two adjacent matches the second stays); then the first 8 characters, upper case.
    /// </summary>
    public static string DosStem(string name)
    {
        var s = new StringBuilder(name);
        foreach (char r in DosRemoved)
        {
            int n = s.Length;
            for (int i = 0; i < n; i++)
                if (i < s.Length && s[i] == r)
                    s.Remove(i, 1);
        }
        var t = s.ToString();
        // Turbo Pascal's UpCase (START 0xC7AA) changes a..z only
        return new string((t.Length > 8 ? t[..8] : t).Select(ch => ch is >= 'a' and <= 'z' ? (char)(ch - 32) : ch).ToArray());
    }

    /// <summary>A file in the folder, any case (the DOS files are upper case).</summary>
    private string? Find(string name)
    {
        if (!Directory.Exists(Folder))
            return null;
        var exact = Path.Combine(Folder, name);
        if (File.Exists(exact))
            return exact;
        return Directory.EnumerateFiles(Folder).FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
    }
}
