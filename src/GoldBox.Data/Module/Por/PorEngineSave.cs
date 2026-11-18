using System.Globalization;
using GoldBox.Core.Ecl;
using GoldBox.Core.Ecl.Profiles;
using GoldBox.Core.Explore;
using GoldBox.Data.Por;

namespace GoldBox.Data.Module.Por;

/// <summary>
/// Pool of Radiance's original save files ↔ the engine save (docs/module-format.md §15). The engine save mirrors
/// <c>SAVGAMx.DAT</c> and the <c>CHRDATxn.SAV/.ITM/.SPC</c> files without loss: the VM regions the profile marks as
/// saved, the position, the disk, location type, interface state and party size bytes, the name slots raw (they hold
/// leftover memory), and each member's records with its file stem. <see cref="ToOriginalFiles"/> writes the original
/// files again byte for byte. Both directions go through <see cref="PorSaveGame"/>, so a game loaded from an engine save
/// is loaded by the same code as one loaded from the original files.
/// </summary>
public static class PorEngineSave
{
    /// <summary>The module-relative or save-folder file name of slot <paramref name="letter"/>'s engine save (<c>a.json</c>).</summary>
    public static string FileName(char letter) => char.ToLowerInvariant(letter) + ".json";

    /// <summary>A module identity for a save, from a module chain (id, version, <see cref="ModuleContentHash"/>).</summary>
    public static SaveModuleRef Identity(ModuleResolver modules) => new()
    {
        Id = modules.Main.Manifest.Id,
        Version = modules.Main.Manifest.Version,
        ContentHash = ModuleContentHash.Of(modules),
    };

    /// <summary>The identity of the content a game runs on: a module's, or <c>por</c> with no hash for a game folder.</summary>
    public static SaveModuleRef Identity(IPorContent content) =>
        content is PorModule m ? Identity(m.Modules) : new SaveModuleRef { Id = "por" };

    /// <summary>
    /// Converts an original save: <paramref name="savgam"/> (13,137 bytes) and the party's files, read by
    /// <paramref name="readFile"/> (file name → bytes, null when absent; a missing <c>.SAV</c> is skipped as the
    /// program does). <paramref name="scriptBlock"/>, when given, is used to note whether the stored script equals
    /// the module's copy.
    /// </summary>
    public static EngineSaveDocument FromOriginal(byte[] savgam, Func<string, byte[]?>? readFile, SaveModuleRef module,
        Func<int, int, byte[]?>? scriptBlock = null, SourceRef? source = null, EclProfile? profile = null)
    {
        var sg = new PorSaveGame(savgam);
        List<SavedCharacter>? party = null;
        if (readFile is not null)
        {
            party = [];
            foreach (var stem in sg.PartyFileStems)
            {
                if (readFile(stem + ".SAV") is not { } sav)
                    continue;
                var (items, itemsTail) = Split(readFile(stem + ".ITM"), EclCharacter.ItemRecordSize);
                var (effects, effectsTail) = Split(readFile(stem + ".SPC"), EclCharacter.EffectRecordSize);
                party.Add(Member(stem, sav, items, effects) with { ItemsTrailing = itemsTail, EffectsTrailing = effectsTail });
            }
        }
        return FromSaveGame(savgam, sg, party, module, scriptBlock, source, profile, SlotOfFile(source?.Container));
    }

    /// <summary>The slot letter of a <c>SAVGAMx.DAT</c> file name, or null.</summary>
    private static string? SlotOfFile(string? name) =>
        name is { Length: 11 } n && n.StartsWith("SAVGAM", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".DAT", StringComparison.OrdinalIgnoreCase)
            && char.ToUpperInvariant(n[6]) is >= 'A' and <= 'J' and var c ? c.ToString() : null;

    /// <summary>As <see cref="FromOriginal"/> with the party already in memory (the camp's Save: the records as they are).</summary>
    public static EngineSaveDocument FromOriginal(byte[] savgam, EclParty? party, SaveModuleRef module,
        Func<int, int, byte[]?>? scriptBlock = null, EclProfile? profile = null)
    {
        var sg = new PorSaveGame(savgam);
        var members = party?.Members.Select((c, i) => Member(i < sg.PartyFileStems.Count ? sg.PartyFileStems[i] : null,
            c.Record, c.Items.Count > 0 ? [.. c.Items] : null, c.Effects.Count > 0 ? [.. c.Effects] : null)).ToList();
        return FromSaveGame(savgam, sg, members, module, scriptBlock, null, profile, null);
    }

    /// <summary>Converts the save the files in <paramref name="folders"/> hold (party files: first folder that has them, any case).</summary>
    public static EngineSaveDocument FromFolders(byte[] savgam, string? fileName, SaveModuleRef module,
        Func<int, int, byte[]?>? scriptBlock, params string[] folders)
    {
        var maps = folders.Where(Directory.Exists)
            .Select(d => Directory.EnumerateFiles(d)
                .GroupBy(f => Path.GetFileName(f).ToUpperInvariant())
                .ToDictionary(g => g.Key, g => g.First()))
            .ToList();
        byte[]? Read(string name) =>
            maps.Select(m => m.TryGetValue(name.ToUpperInvariant(), out var p) ? p : null).FirstOrDefault(p => p is not null)
                is { } path ? File.ReadAllBytes(path) : null;
        return FromOriginal(savgam, Read, module, scriptBlock,
            fileName is null ? null : new SourceRef { Container = Path.GetFileName(fileName).ToUpperInvariant() });
    }

    private static EngineSaveDocument FromSaveGame(byte[] savgam, PorSaveGame sg, List<SavedCharacter>? party,
        SaveModuleRef module, Func<int, int, byte[]?>? scriptBlock, SourceRef? source, EclProfile? profile, string? fileSlot)
    {
        profile ??= EclProfiles.Default;
        if (!profile.ScriptBytesSaved || profile.Handlers != EclHandlerSet.Por13)
            throw new NotSupportedException($"VM profile {profile.Name}: not PoR's save layout");
        var regions = new List<SavedRegion>();
        foreach (var r in profile.Regions.Where(r => r.Saved))
            regions.Add(new SavedRegion { Name = r.Name, Start = r.Start, CellBits = r.CellBits, Cells = RegionBytes(savgam, r.Name) });
        bool? matches = scriptBlock?.Invoke(sg.Disk, sg.ScriptId) is { } block ? sg.ScriptAreaMismatches(block) == 0 : null;
        // The slot: the SAVGAMx.DAT letter when the file name is known, else the letter of the first stem.
        string? slot = fileSlot ?? (sg.PartyFileStems.FirstOrDefault() is { Length: 8 } s && s.StartsWith("CHRDAT", StringComparison.Ordinal)
            && s[6] is >= 'A' and <= 'J' ? s[6].ToString() : null);
        ushort Cell(int a) => sg.Area4900[a - 0x4900];
        return new EngineSaveDocument
        {
            FormatVersion = EngineSaveDocument.CurrentFormatVersion,
            Module = module,
            Vm = profile.Name,
            Memory = regions,
            Position = new SavedPosition { X = sg.X, Y = sg.Y, Facing = sg.Facing, WallAhead = sg.WallAhead, EventByte = sg.EventByte },
            Script = new SavedScript { Key = KeyRef.Of(ResourceKey.Script(sg.ScriptId)), Disk = sg.Disk, MatchesModule = matches },
            Party = party,
            Clock = new SaveClock
            {
                Year = Cell(GameClock.FirstCell + 6), Month = Cell(GameClock.FirstCell + 5), Day = Cell(GameClock.FirstCell + 4),
                Hour = Cell(GameClock.FirstCell + 3), Minute = Cell(GameClock.FirstCell + 2) * 10 + Cell(GameClock.FirstCell + 1),
            },
            Por = new PorSaveLayout
            {
                Slot = slot,
                Disk = sg.Disk,
                LocationType = sg.LocationType,
                InterfaceState = sg.InterfaceState,
                PartySize = sg.PartySize,
                NameSlots = savgam[PorSaveGame.OffNames..PorSaveGame.Size],
            },
            Source = source,
        };
    }

    private static SavedCharacter Member(string? stem, byte[] record, List<byte[]>? items, List<byte[]>? effects) => new()
    {
        Stem = stem,
        Fields = record.Length == PorRecordFields.Creature.RecordSize ? PorRecordFields.Creature.Decode(record) : null,
        Raw = [.. record],
        Items = items?.Select(r => Record(PorRecordFields.Item, r)).ToList(),
        Effects = effects?.Select(r => Record(PorRecordFields.Effect, r)).ToList(),
    };

    private static RecordDocument Record(RecordFieldCodec codec, byte[] r) =>
        new() { Fields = r.Length == codec.RecordSize ? codec.Decode(r) : null, Raw = [.. r] };

    /// <summary>
    /// A file of fixed-size records (null when absent) and the bytes after the last whole record (null when none). The
    /// game's load ignores such a tail (it reads whole records until the end); it is kept for the round trip.
    /// </summary>
    private static (List<byte[]>? Records, byte[]? Trailing) Split(byte[]? file, int size)
    {
        if (file is null)
            return (null, null);
        int n = file.Length / size;
        return ([.. Enumerable.Range(0, n).Select(k => file[(k * size)..((k + 1) * size)])],
            file.Length % size != 0 ? file[(n * size)..] : null);
    }

    /// <summary>The bytes of a saved region as the save file holds them.</summary>
    private static byte[] RegionBytes(byte[] savgam, string name) => name switch
    {
        "4900" => savgam[PorSaveGame.Off4900..PorSaveGame.Off6B00],
        "6B00" => savgam[PorSaveGame.Off6B00..PorSaveGame.Off9700],
        "9700" => savgam[PorSaveGame.Off9700..PorSaveGame.OffScript],
        "script" => savgam[PorSaveGame.OffScript..PorSaveGame.OffPosition],
        _ => throw new NotSupportedException($"region {name} is not in PoR's save"),
    };

    private static int RegionOffset(string name) => name switch
    {
        "4900" => PorSaveGame.Off4900,
        "6B00" => PorSaveGame.Off6B00,
        "9700" => PorSaveGame.Off9700,
        "script" => PorSaveGame.OffScript,
        _ => throw new FormatException($"engine save: region {name} is not in PoR's save"),
    };

    /// <summary>
    /// Checks that a save fits this engine and the content: the format's major version, the VM profile (refused when it
    /// is not <paramref name="profile"/>), the regions the profile saves (names, starts, sizes) and the module id
    /// (refused when it differs). A different module version or content hash is not refused (the save holds its own
    /// script; maps and records are read from the module) and is returned as a note [?].
    /// </summary>
    public static List<string> Check(EngineSaveDocument save, EclProfile profile, SaveModuleRef? module)
    {
        var notes = new List<string>();
        if (!save.FormatVersion.StartsWith("0.", StringComparison.Ordinal))
            throw new FormatException($"engine save format {save.FormatVersion} is newer than this engine reads ({EngineSaveDocument.CurrentFormatVersion})");
        if (save.Vm != profile.Name)
            throw new FormatException($"engine save is for VM profile {save.Vm}, the content runs {profile.Name}");
        var want = profile.Regions.Where(r => r.Saved).ToList();
        if (save.Memory.Count != want.Count)
            throw new FormatException($"engine save has {save.Memory.Count} memory regions, profile {profile.Name} saves {want.Count}");
        foreach (var (have, r) in save.Memory.Zip(want))
        {
            int size = (r.End - r.Start + 1) * (r.CellBits / 8);
            if (have.Name != r.Name || have.Start != r.Start || have.CellBits != r.CellBits || have.Cells.Length != size)
                throw new FormatException($"engine save region {have.Name} (${have.Start:X4}, {have.CellBits} bits, {have.Cells.Length} bytes) does not match the profile's {r.Name}");
        }
        if (save.Por is null)
            throw new FormatException("engine save has no PoR layout block (por)");
        if (module is not null)
        {
            if (!string.Equals(save.Module.Id, module.Id, StringComparison.Ordinal))
                throw new FormatException($"engine save is for module {save.Module.Id}, not {module.Id}");
            if (save.Module.Version != module.Version)
                notes.Add($"[?] save made with module version {save.Module.Version ?? "(none)"}, running {module.Version ?? "(none)"}");
            if (save.Module.ContentHash is { } h && module.ContentHash is { } h2 && h != h2)
                notes.Add("[?] save made with other module content (hash differs); the saved script is used, maps and records come from this module");
        }
        return notes;
    }

    /// <summary>The <c>SAVGAMx.DAT</c> bytes of a save (the name slots as stored, unless <paramref name="letter"/> renames the set).</summary>
    public static byte[] ToSavgam(EngineSaveDocument save, char? letter = null)
    {
        var por = save.Por ?? throw new FormatException("engine save has no PoR layout block (por)");
        var d = new byte[PorSaveGame.Size];
        d[PorSaveGame.OffDisk] = (byte)por.Disk;
        foreach (var r in save.Memory)
        {
            int off = RegionOffset(r.Name);
            if (off + r.Cells.Length > PorSaveGame.OffPosition)
                throw new FormatException($"engine save region {r.Name} is too long");
            r.Cells.CopyTo(d, off);
        }
        var p = save.Position;
        d[PorSaveGame.OffPosition] = (byte)p.X;
        d[PorSaveGame.OffPosition + 1] = (byte)p.Y;
        d[PorSaveGame.OffPosition + 2] = (byte)p.Facing;
        d[PorSaveGame.OffPosition + 3] = (byte)p.WallAhead;
        d[PorSaveGame.OffPosition + 4] = (byte)p.EventByte;
        d[PorSaveGame.OffLocationType] = (byte)por.LocationType;
        d[PorSaveGame.OffInterface] = (byte)por.InterfaceState;
        d[PorSaveGame.OffPartySize] = (byte)por.PartySize;
        bool rename = letter is { } l && !string.Equals(por.Slot, char.ToUpperInvariant(l).ToString(), StringComparison.Ordinal);
        if (!rename && por.NameSlots is { } names)
        {
            if (names.Length != PorSaveGame.Size - PorSaveGame.OffNames)
                throw new FormatException($"engine save name slots are {names.Length} bytes, expected {PorSaveGame.Size - PorSaveGame.OffNames}");
            names.CopyTo(d, PorSaveGame.OffNames);
        }
        var stems = Stems(save, letter);
        for (int i = 0; i < stems.Count && i < 8; i++)
        {
            int o = PorSaveGame.OffNames + 41 * i;
            if (!rename && por.NameSlots is not null)
                continue;   // the stored slots already hold the stems
            d[o] = (byte)stems[i].Length;
            System.Text.Encoding.ASCII.GetBytes(stems[i]).CopyTo(d, o + 1);
        }
        return d;
    }

    /// <summary>The file stems of the name slots: as stored, or <c>CHRDAT&lt;letter&gt;n</c> for a renamed set.</summary>
    private static List<string> Stems(EngineSaveDocument save, char? letter)
    {
        var por = save.Por!;
        bool rename = letter is { } l && !string.Equals(por.Slot, char.ToUpperInvariant(l).ToString(), StringComparison.Ordinal);
        if (rename || por.NameSlots is null)
        {
            char c = letter ?? (por.Slot is { Length: 1 } s ? s[0] : 'A');
            return [.. Enumerable.Range(1, Math.Min(por.PartySize, 8)).Select(n => PorSaveGame.Stem(c, n))];
        }
        var tmp = new byte[PorSaveGame.Size];
        por.NameSlots.CopyTo(tmp, PorSaveGame.OffNames);
        tmp[PorSaveGame.OffPartySize] = (byte)por.PartySize;
        return [.. new PorSaveGame(tmp).PartyFileStems];
    }

    /// <summary>
    /// The original files of a save, name → bytes: <c>SAVGAMx.DAT</c> and per member <c>.SAV</c>, plus <c>.ITM</c> and
    /// <c>.SPC</c> when the member has items or effects (an absent file means none, as the program writes). Named after
    /// the stored stems, or after slot <paramref name="letter"/> when it differs from the save's own.
    /// </summary>
    public static IReadOnlyList<(string Name, byte[] Bytes)> ToOriginalFiles(EngineSaveDocument save, char? letter = null)
    {
        var por = save.Por ?? throw new FormatException("engine save has no PoR layout block (por)");
        bool rename = letter is { } l && !string.Equals(por.Slot, char.ToUpperInvariant(l).ToString(), StringComparison.Ordinal);
        char slot = letter ?? (por.Slot is { Length: 1 } s ? s[0] : throw new FormatException("engine save has no slot letter: give one"));
        var files = new List<(string, byte[])> { (PorSaveGame.FileName(slot), ToSavgam(save, letter)) };
        var party = save.Party ?? [];
        for (int i = 0; i < party.Count && i < 8; i++)
        {
            var m = party[i];
            var stem = !rename && m.Stem is { } st ? st : PorSaveGame.Stem(slot, i + 1);
            files.Add((stem + ".SAV", m.Raw));
            if (m.Items is { } items)
                files.Add((stem + ".ITM", [.. items.SelectMany(r => r.Raw), .. m.ItemsTrailing ?? []]));
            if (m.Effects is { } effects)
                files.Add((stem + ".SPC", [.. effects.SelectMany(r => r.Raw), .. m.EffectsTrailing ?? []]));
        }
        return files;
    }

    /// <summary>The party of a save as the VM holds it (records as stored; member 0 current), or null when it has none.</summary>
    public static EclParty? Party(EngineSaveDocument save)
    {
        if (save.Party is null)
            return null;
        var party = new EclParty();
        foreach (var m in save.Party)
        {
            var c = new EclCharacter([.. m.Raw]);
            foreach (var r in m.Items ?? [])
                c.Items.Add([.. r.Raw]);
            foreach (var r in m.Effects ?? [])
                c.Effects.Add([.. r.Raw]);
            party.Members.Add(c);
        }
        party.CurrentIndex = party.Members.Count > 0 ? 0 : -1;
        return party;
    }

    /// <summary>One character as a save's party entry holds it (record, items, effects; null lists when empty, as the
    /// program writes no file for them). Used by the party menu's roster (<see cref="Por.PorRoster"/>).</summary>
    public static SavedCharacter Character(EclCharacter c, string? stem) =>
        Member(stem, c.Record, c.Items.Count > 0 ? [.. c.Items] : null, c.Effects.Count > 0 ? [.. c.Effects] : null);

    /// <summary>A party entry back as the VM's character (raw bytes are authoritative).</summary>
    public static EclCharacter Character(SavedCharacter m)
    {
        var c = new EclCharacter([.. m.Raw]);
        foreach (var r in m.Items ?? [])
            c.Items.Add([.. r.Raw]);
        foreach (var r in m.Effects ?? [])
            c.Effects.Add([.. r.Raw]);
        return c;
    }

    /// <summary>The save as <see cref="PorSaveGame"/>, the reader the game loads with.</summary>
    public static PorSaveGame ToSaveGame(EngineSaveDocument save) => new(ToSavgam(save));

    public static EngineSaveDocument Read(string path) => ModuleJson.Read<EngineSaveDocument>(path);

    public static void Write(string path, EngineSaveDocument save) => ModuleJson.Write(path, save);

    /// <summary>True for a path that names an engine save (<c>.json</c>) rather than an original <c>SAVGAMx.DAT</c>.</summary>
    public static bool IsEngineSave(string path) => path.EndsWith(".json", true, CultureInfo.InvariantCulture);
}
