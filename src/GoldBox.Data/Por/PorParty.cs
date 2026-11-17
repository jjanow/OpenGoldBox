using GoldBox.Core.Ecl;

namespace GoldBox.Data.Por;

/// <summary>
/// Builds the VM's party from the character files a save names (docs/re/save-format.md "Loading"): for each stem
/// <c>CHRDATxn</c>, the 285-byte <c>.SAV</c> record, the 63-byte item records of <c>.ITM</c> (absent = no items) and
/// the 9-byte effect records of <c>.SPC</c> (absent = no effects).
/// </summary>
public static class PorParty
{
    /// <summary>
    /// Loads the members named by <paramref name="stems"/>; <paramref name="find"/> maps a file name to a path or
    /// null. Missing <c>.SAV</c> files are skipped. The current character is member 0 ([?] which character is current
    /// after a load is not read).
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
