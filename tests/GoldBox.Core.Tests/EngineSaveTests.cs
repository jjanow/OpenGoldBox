using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Ecl.Profiles;
using GoldBox.Data.Module;
using GoldBox.Data.Module.Por;
using GoldBox.Data.Por;

namespace GoldBox.Core.Tests;

/// <summary>
/// The engine save (docs/module-format.md §15) on a made-up PoR save: original files → engine save → JSON → original
/// files is byte for byte, leftover bytes included; the checks a load makes. No game files needed.
/// </summary>
public class EngineSaveTests
{
    private static readonly SaveModuleRef Module = new() { Id = "por", Version = "1.3.0", ContentHash = "ab" };

    /// <summary>A save of slot B with two members: stems in the name slots, leftover text after them, odd cell values.</summary>
    private static Dictionary<string, byte[]> OriginalFiles()
    {
        var rng = new Random(5);
        var s = new byte[PorSaveGame.Size];
        rng.NextBytes(s);                                   // every byte "leftover", then the known fields over it
        s[PorSaveGame.OffDisk] = 3;
        void Cell(int addr, int v)
        {
            s[PorSaveGame.Off4900 + 2 * (addr - 0x4900)] = (byte)v;
            s[PorSaveGame.Off4900 + 2 * (addr - 0x4900) + 1] = (byte)(v >> 8);
        }
        Cell(0x49C5, 0x0114);                                // high byte set: the map id is the low byte
        Cell(0x49C6 + 1, 7);
        Cell(0x49C6 + 2, 4);
        Cell(0x49C6 + 3, 9);
        s[PorSaveGame.OffPartySize] = 2;
        for (int i = 0; i < 2; i++)
        {
            var stem = PorSaveGame.Stem('B', i + 1);
            s[PorSaveGame.OffNames + 41 * i] = (byte)stem.Length;
            System.Text.Encoding.ASCII.GetBytes(stem).CopyTo(s, PorSaveGame.OffNames + 41 * i + 1);
        }
        byte[] Bytes(int n)
        {
            var b = new byte[n];
            rng.NextBytes(b);
            return b;
        }
        return new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["SAVGAMB.DAT"] = s,
            ["CHRDATB1.SAV"] = Bytes(EclCharacter.RecordSize),
            ["CHRDATB1.ITM"] = Bytes(3 * EclCharacter.ItemRecordSize),
            ["CHRDATB2.SAV"] = Bytes(EclCharacter.RecordSize),
            ["CHRDATB2.SPC"] = Bytes(2 * EclCharacter.EffectRecordSize),
        };
    }

    private static EngineSaveDocument Convert(Dictionary<string, byte[]> files) =>
        PorEngineSave.FromOriginal(files["SAVGAMB.DAT"], n => files.GetValueOrDefault(n), Module);

    [Fact]
    public void Original_files_round_trip_through_the_json_byte_for_byte()
    {
        var files = OriginalFiles();
        var doc = Convert(files);
        var json = JsonSerializer.Serialize(doc, ModuleJson.Options);
        var back = JsonSerializer.Deserialize<EngineSaveDocument>(json, ModuleSchemas.Strict)!;
        var written = PorEngineSave.ToOriginalFiles(back);
        Assert.Equal(files.Keys.Order(), written.Select(f => f.Name).Order());
        foreach (var (name, bytes) in written)
            Assert.Equal(files[name], bytes);

        Assert.Equal("B", back.Por!.Slot);
        Assert.Equal(20, back.Script.Key.Id);
        Assert.Equal(3, back.Script.Disk);
        Assert.Null(back.Script.MatchesModule);
        Assert.Equal(new SaveClock { Minute = 47, Hour = 9 }, back.Clock with { Year = 0, Month = 0, Day = 0 });
        Assert.Equal(["4900", "6B00", "9700", "script"], back.Memory.Select(r => r.Name));
        Assert.Null(back.Party![0].Effects);                    // no .SPC file
        Assert.Null(back.Party[1].Items);                       // no .ITM file
        Assert.Equal(3, back.Party[0].Items!.Count);
    }

    [Fact]
    public void Loading_gives_the_memory_and_party_of_the_original()
    {
        var files = OriginalFiles();
        var doc = Convert(files);
        var a = new EclMemory();
        new PorSaveGame(files["SAVGAMB.DAT"]).ApplyTo(a);
        var b = new EclMemory();
        PorEngineSave.ToSaveGame(doc).ApplyTo(b);
        Assert.Equal(a.Cells4900.ToArray(), b.Cells4900.ToArray());
        Assert.Equal(a.Cells6B00.ToArray(), b.Cells6B00.ToArray());
        Assert.Equal(a.Cells9700.ToArray(), b.Cells9700.ToArray());
        Assert.Equal(a.ScriptArea.ToArray(), b.ScriptArea.ToArray());
        Assert.Equal((a.Disk, a.ViewMode, a.GameSpeed, a.Position.X, a.Position.Y, a.Position.Facing, a.Position.WallAhead, a.Position.EventByte),
            (b.Disk, b.ViewMode, b.GameSpeed, b.Position.X, b.Position.Y, b.Position.Facing, b.Position.WallAhead, b.Position.EventByte));
        var party = PorEngineSave.Party(doc)!;
        Assert.Equal(0, party.CurrentIndex);
        Assert.Equal(files["CHRDATB1.SAV"], party.Members[0].Record);
        Assert.Equal(3, party.Members[0].Items.Count);
        Assert.Equal(2, party.Members[1].Effects.Count);
    }

    [Fact]
    public void A_partial_record_at_the_end_of_a_party_file_is_kept_but_not_loaded()
    {
        var files = OriginalFiles();
        files["CHRDATB1.ITM"] = [.. files["CHRDATB1.ITM"], 1, 2, 3];
        files["CHRDATB2.SPC"] = [7];
        var doc = Convert(files);
        Assert.Equal([1, 2, 3], doc.Party![0].ItemsTrailing);
        Assert.Empty(doc.Party[1].Effects!);                    // a file with no whole record: present, empty
        foreach (var (name, bytes) in PorEngineSave.ToOriginalFiles(doc))
            Assert.Equal(files[name], bytes);
        var party = PorEngineSave.Party(doc)!;                  // the game's load reads whole records only
        Assert.Equal(3, party.Members[0].Items.Count);
        Assert.Empty(party.Members[1].Effects);
    }

    [Fact]
    public void A_renamed_set_keeps_its_stems_and_the_save_files_slot()
    {
        var files = OriginalFiles();
        var s = files["SAVGAMB.DAT"];
        for (int i = 0; i < 2; i++)
        {
            System.Text.Encoding.ASCII.GetBytes($"HERO{i + 1}").CopyTo(s, PorSaveGame.OffNames + 41 * i + 1);
            s[PorSaveGame.OffNames + 41 * i] = 5;
            files[$"HERO{i + 1}.SAV"] = files[$"CHRDATB{i + 1}.SAV"];
            files.Remove($"CHRDATB{i + 1}.SAV");
        }
        files["HERO1.ITM"] = files["CHRDATB1.ITM"];
        files["HERO2.SPC"] = files["CHRDATB2.SPC"];
        files.Remove("CHRDATB1.ITM");
        files.Remove("CHRDATB2.SPC");
        var doc = PorEngineSave.FromOriginal(s, n => files.GetValueOrDefault(n), Module, source: new SourceRef { Container = "SAVGAMB.DAT" });
        Assert.Equal("B", doc.Por!.Slot);
        Assert.Equal(["HERO1", "HERO2"], doc.Party!.Select(m => m.Stem));
        var written = PorEngineSave.ToOriginalFiles(doc);
        Assert.Equal(files.Keys.Order(), written.Select(f => f.Name).Order());
        foreach (var (name, bytes) in written)
            Assert.Equal(files[name], bytes);
    }

    [Fact]
    public void Exporting_to_another_slot_renames_the_set()
    {
        var doc = Convert(OriginalFiles());
        var written = PorEngineSave.ToOriginalFiles(doc, 'e');
        Assert.Equal(["SAVGAME.DAT", "CHRDATE1.SAV", "CHRDATE1.ITM", "CHRDATE2.SAV", "CHRDATE2.SPC"], written.Select(f => f.Name));
        var sg = new PorSaveGame(written[0].Bytes);
        Assert.Equal(["CHRDATE1", "CHRDATE2"], sg.PartyFileStems);
        Assert.All(written[0].Bytes[(PorSaveGame.OffNames + 9)..(PorSaveGame.OffNames + 41)], b => Assert.Equal(0, b));
    }

    [Fact]
    public void A_load_checks_the_profile_regions_and_module()
    {
        var doc = Convert(OriginalFiles());
        Assert.Empty(PorEngineSave.Check(doc, EclProfiles.Por13, Module));
        Assert.Throws<FormatException>(() => PorEngineSave.Check(doc with { Vm = "ecl-pod" }, EclProfiles.Por13, Module));
        Assert.Throws<FormatException>(() => PorEngineSave.Check(doc, EclProfiles.Por13, Module with { Id = "curse" }));
        Assert.Throws<FormatException>(() => PorEngineSave.Check(doc with { Memory = doc.Memory[..3] }, EclProfiles.Por13, Module));
        Assert.Throws<FormatException>(() => PorEngineSave.Check(doc with { FormatVersion = "1.0" }, EclProfiles.Por13, Module));
        // Another module version is refused; other content with the same version is loaded, with a note.
        Assert.Throws<FormatException>(() => PorEngineSave.Check(doc, EclProfiles.Por13, Module with { Version = "1.4.0" }));
        var notes = PorEngineSave.Check(doc, EclProfiles.Por13, Module with { ContentHash = "cd" });
        Assert.Single(notes);
        Assert.StartsWith("[?]", notes[0]);
        // A game folder has no hash: nothing to compare.
        Assert.Empty(PorEngineSave.Check(doc, EclProfiles.Por13, new SaveModuleRef { Id = "por", Version = "1.3.0" }));
    }

    [Fact]
    public void The_camps_save_writes_the_programs_bytes()
    {
        var m = new EclMemory { Disk = 2, ViewMode = 1, GameSpeed = 4 };
        m.Write(0x49C5, 20);
        m.Position.X = 5;
        var party = new EclParty();
        party.Members.Add(new EclCharacter());
        party.Members[0].Items.Add(new byte[EclCharacter.ItemRecordSize]);
        party.CurrentIndex = 0;
        var bytes = PorSaveGame.Build(m, 1, 'c', 2);
        var doc = PorEngineSave.FromOriginal(bytes, party, Module);
        var written = PorEngineSave.ToOriginalFiles(doc);
        Assert.Equal(["SAVGAMC.DAT", "CHRDATC1.SAV", "CHRDATC1.ITM"], written.Select(f => f.Name));
        Assert.Equal(bytes, written[0].Bytes);
        Assert.Equal("C", doc.Por!.Slot);
        Assert.Equal(2, doc.Por.InterfaceState);
    }
}
