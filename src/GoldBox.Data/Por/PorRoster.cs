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
/// [?] The program's file name is the name through its own clean-up (8.3); ours keeps letters, digits, '-' and '_'
/// and replaces the rest with '_' (two names that clean up the same share a file, as in the program).
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

    /// <summary>LoadChar (0x1EAD2): the character by its name, or null when no file has it.</summary>
    public EclCharacter? Load(string name)
    {
        var stem = Stem(name);
        if (Find(stem + ".json") is { } json)
            return PorEngineSave.Character(ModuleJson.Read<SavedCharacter>(json));
        if (Find(stem + ".cha") is not { } cha)
            return null;
        var c = new EclCharacter(File.ReadAllBytes(cha));
        foreach (var r in Records(Find(stem + ".itm"), EclCharacter.ItemRecordSize))
            c.Items.Add(r);
        foreach (var r in Records(Find(stem + ".spc"), EclCharacter.EffectRecordSize))
            c.Effects.Add(r);
        return c;
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
