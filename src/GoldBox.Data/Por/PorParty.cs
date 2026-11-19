using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Data.Por;

/// <summary>
/// Builds the VM's party from the character files a save names (docs/re/save-format.md "Loading"): for each stem
/// <c>CHRDATxn</c>, the 285-byte <c>.SAV</c> record, the 63-byte item records of <c>.ITM</c> (absent = no items) and
/// the 9-byte effect records of <c>.SPC</c> (absent = no effects).
/// </summary>
public static class PorParty
{
    /// <summary>
    /// Refresh a legacy party when it enters play. Load joins each member into an empty roster, assigning icon
    /// slots in party order and running the level-stat routine (GAME.OVR 0x1F9C9 → 0x1F42C → 0x2AA87).
    /// </summary>
    internal static void RefreshOnLoad(EclParty party, LevelStatsTables? tables)
    {
        for (int slot = 0; slot < party.Members.Count; slot++)
        {
            var member = party.Members[slot];
            member[PartyMenu.IconOrder] = (byte)slot;
            if (tables is not null)
                LevelStats.Apply(member, tables);
        }
    }

    /// <summary>
    /// Loads the members named by <paramref name="stems"/>; <paramref name="find"/> maps a file name to a path or
    /// null. Missing <c>.SAV</c> files are skipped. The current character is member 0 (GAME.OVR 0x1FB99).
    /// </summary>
    public static EclParty Load(IEnumerable<string> stems, Func<string, string?> find)
    {
        var party = new EclParty();
        foreach (var stem in stems)
        {
            var sav = find(stem + ".SAV");
            if (sav is null)
                continue;
            var c = new EclCharacter(File.ReadAllBytes(sav));
            if (find(stem + ".ITM") is { } itm)
            {
                var b = File.ReadAllBytes(itm);
                for (int k = 0; k + EclCharacter.ItemRecordSize <= b.Length; k += EclCharacter.ItemRecordSize)
                    c.Items.Add(b[k..(k + EclCharacter.ItemRecordSize)]);
            }
            if (find(stem + ".SPC") is { } spc)
            {
                var b = File.ReadAllBytes(spc);
                for (int k = 0; k + EclCharacter.EffectRecordSize <= b.Length; k += EclCharacter.EffectRecordSize)
                    c.Effects.Add(b[k..(k + EclCharacter.EffectRecordSize)]);
            }
            party.Members.Add(c);
        }
        party.CurrentIndex = party.Members.Count > 0 ? 0 : -1;
        return party;
    }

    /// <summary>
    /// Writes the party as save slot <paramref name="letter"/> (GAME.OVR 0x1FD3E's per-member save): member n's
    /// 285-byte record to <c>CHRDAT&lt;letter&gt;n.SAV</c>; the old <c>.ITM</c> and <c>.SPC</c> are deleted and written
    /// again only when the member has items or effects (records as they are, 63 / 9 bytes each, list order). Files of
    /// members beyond the party stay (the program leaves them too). [?] The program also deletes the roster files
    /// (<c>&lt;name&gt;.CHA/.ITM/.SPC</c>) of every member; its name-to-file rule is not read, so they are left.
    /// </summary>
    public static void Save(EclParty party, string folder, char letter)
    {
        Directory.CreateDirectory(folder);
        for (int i = 0; i < party.Members.Count && i < 8; i++)
        {
            var c = party.Members[i];
            var stem = Path.Combine(folder, PorSaveGame.Stem(letter, i + 1));
            File.WriteAllBytes(stem + ".SAV", c.Record);
            Replace(stem + ".ITM", c.Items);
            Replace(stem + ".SPC", c.Effects);
        }
    }

    private static void Replace(string path, List<byte[]> records)
    {
        // An absent file means "none" on load, so a stale one must go (any case of the name).
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileName(path);
        foreach (var f in Directory.EnumerateFiles(dir).Where(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase)).ToList())
            File.Delete(f);
        if (records.Count == 0)
            return;
        using var s = File.Create(path);
        foreach (var r in records)
            s.Write(r);
    }

    /// <summary>Loads the party a save names from the files in <paramref name="folders"/> (first match wins, any case).</summary>
    public static EclParty Load(PorSaveGame save, params string[] folders)
    {
        var maps = folders.Where(Directory.Exists)
            .Select(d => Directory.EnumerateFiles(d)
                .GroupBy(f => Path.GetFileName(f).ToUpperInvariant())
                .ToDictionary(g => g.Key, g => g.First()))
            .ToList();
        return Load(save.PartyFileStems, name =>
            maps.Select(m => m.TryGetValue(name.ToUpperInvariant(), out var p) ? p : null).FirstOrDefault(p => p is not null));
    }
}
