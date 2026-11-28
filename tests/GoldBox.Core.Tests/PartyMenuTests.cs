using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;

namespace GoldBox.Core.Tests;

/// <summary>The party menu's rules (GAME.OVR 0x19AE0 and its Add / Remove / join), the roster files and the new game.</summary>
public class PartyMenuTests
{
    /// <summary>The program's roster file stem (START 0x8445 / 0x8376, read s52): the ten characters removed one by one
    /// (a match right after a removed one is skipped), the first 8 kept, a..z upper case.</summary>
    [Theory]
    [InlineData("Bob", "BOB")]
    [InlineData("a.b*c,d?e", "ABCDE")]
    [InlineData("Longname12345", "LONGNAME")]
    [InlineData("SIR  BOB", "SIR BOB")]
    [InlineData("x//y", "X/Y")]
    [InlineData("Tom-Lee_2", "TOM-LEE_")]
    public void Roster_file_stem_is_the_programs(string name, string stem) => Assert.Equal(stem, PorRoster.DosStem(name));

    public sealed class PorDataFactAttribute : FactAttribute
    {
        public PorDataFactAttribute()
        {
            if (!PorData.Present)
                Skip = PorData.SkipReason;
        }
    }

    private static EclCharacter Pc(string name, byte random = 1, bool npc = false)
    {
        var c = new EclCharacter { Name = name };
        c[PartyMenu.RandomByte] = random;
        c[PartyMenu.Npc] = npc ? (byte)0xB1 : (byte)0;
        return c;
    }

    [Fact]
    public void Menu_offers_entries_by_current_character()
    {
        var none = PartyMenu.Enabled(false, trainOn: true);
        Assert.Equal([true, false, false, false, false, true, false, true, false, false, true], none);
        var some = PartyMenu.Enabled(true, trainOn: false);
        Assert.Equal([true, true, true, false, true, true, true, false, true, true, true], some);
        Assert.True(PartyMenu.Enabled(true, trainOn: true)[(int)PartyMenuItem.Train]);
        // Train, once on, is not cleared while a character is current (0x19B4D–0x19B60 only sets it).
        Assert.True(PartyMenu.Enabled(true, trainOn: false, some.Select((_, i) => i == 3).ToArray())[(int)PartyMenuItem.Train]);
    }

    [Fact]
    public void Join_takes_the_first_free_icon_slot_and_becomes_current()
    {
        var party = new EclParty();
        int stats = 0;
        PartyMenu.Join(party, Pc("A"), _ => stats++);
        PartyMenu.Join(party, Pc("B"), _ => stats++);
        party.CurrentIndex = 0;
        PartyMenu.RemoveCurrent(party);
        PartyMenu.Join(party, Pc("C"), _ => stats++);
        Assert.Equal(["B", "C"], party.Members.Select(m => m.Name));
        Assert.Equal([1, 0], party.Members.Select(m => (int)m[PartyMenu.IconOrder]));
        Assert.Equal(1, party.CurrentIndex);
        Assert.Equal(3, stats);
    }

    [Fact]
    public void Add_refuses_duplicates_and_a_seventh_player_character_but_takes_npcs_to_eight()
    {
        var party = new EclParty();
        for (int i = 0; i < 6; i++)
            Assert.True(PartyMenu.TryAdd(party, Pc("P" + i), null).Joined);
        Assert.False(PartyMenu.TryAdd(party, Pc("P2"), null).Joined); // same name and byte 0xAB
        var (joined, pcs) = PartyMenu.TryAdd(party, Pc("P9"), null);
        Assert.False(joined);
        Assert.True(PartyMenu.AddFull(party, pcs));
        Assert.True(PartyMenu.TryAdd(party, Pc("N1", npc: true), null).Joined);
        Assert.True(PartyMenu.TryAdd(party, Pc("N2", npc: true), null).Joined);
        Assert.False(PartyMenu.TryAdd(party, Pc("N3", npc: true), null).Joined);
        Assert.Equal(8, party.Members.Count);
        // Same name, other random byte: a different character.
        var small = new EclParty();
        PartyMenu.TryAdd(small, Pc("X", 1), null);
        Assert.True(PartyMenu.TryAdd(small, Pc("X", 2), null).Joined);
    }

    [Fact]
    public void Add_recounts_the_party_after_a_pick_that_loads_no_one()
    {
        // 0x1C9EC reads its count byte unset after a marked name or another key (stack residue); fixed (user, s53):
        // the party's player characters are counted, NPCs left out.
        var party = new EclParty();
        for (int i = 0; i < 5; i++)
            PartyMenu.Join(party, Pc("P" + i), null);
        PartyMenu.Join(party, Pc("N1", npc: true), null);
        Assert.Equal(5, PartyMenu.CountPlayerCharacters(party));
        Assert.False(PartyMenu.AddFull(party, PartyMenu.CountPlayerCharacters(party)));
        PartyMenu.Join(party, Pc("P5"), null);
        Assert.True(PartyMenu.AddFull(party, PartyMenu.CountPlayerCharacters(party)));
    }

    [Fact]
    public void Remove_makes_the_one_before_current_or_the_next_when_first()
    {
        var party = new EclParty();
        foreach (var n in new[] { "A", "B", "C" })
            PartyMenu.Join(party, Pc(n), null);
        party.CurrentIndex = 2;
        PartyMenu.RemoveCurrent(party);
        Assert.Equal(1, party.CurrentIndex);
        party.CurrentIndex = 0;
        PartyMenu.RemoveCurrent(party);
        Assert.Equal("B", party.Current!.Name);
        PartyMenu.RemoveCurrent(party);
        Assert.Equal(-1, party.CurrentIndex);
    }

    [Fact]
    public void Roster_keeps_the_programs_list_rules()
    {
        var dir = Directory.CreateTempSubdirectory("goldbox-roster-");
        try
        {
            var roster = new PorRoster(dir.FullName);
            Assert.Empty(roster.Entries(null));
            var a = Pc("ALF");
            a.Effects.Add([0x5A, 0, 0, 0xFF, 0, 0, 0, 0, 0]);
            roster.Save(a, null);
            roster.Save(Pc("BO"), null);
            Assert.Equal(["ALF", "BO"], roster.Entries(null));
            var party = new EclParty();
            PartyMenu.Join(party, roster.Load("ALF")!, null);
            Assert.Equal(["* ALF", "BO"], roster.Entries(party));
            // A save while ALF is in the party drops ALF's line (SaveChar re-writes the list without party members).
            roster.Save(Pc("CY"), party);
            Assert.Equal(["BO", "CY"], roster.Entries(null));
            var back = roster.Load("ALF")!;
            Assert.Equal(a.Record, back.Record);
            Assert.Single(back.Effects);
            Assert.Null(roster.Load("NOBODY"));
        }
        finally
        {
            dir.Delete(true);
        }
    }

    /// <summary>
    /// The roster deletes of the game Save (0x1FD3E) and Load (0x1F5E8), the option of user s61 (off by default in the
    /// Client): Save erases each member's files that exist, each on its own; Load only when the character file (JSON or
    /// <c>.cha</c>) exists, else none. Other characters' files and the list stay.
    /// </summary>
    [Fact]
    public void Roster_deletes_follow_the_programs_save_and_load_rules()
    {
        var dir = Directory.CreateTempSubdirectory("goldbox-roster-");
        try
        {
            var roster = new PorRoster(dir.FullName);
            roster.Save(Pc("ALF"), null);
            roster.Save(Pc("BO"), null);
            File.WriteAllBytes(Path.Combine(dir.FullName, "ALF.SPC"), new byte[9]); // a DOS file next to the JSON
            Assert.Equal(2, roster.RemoveSaved([Pc("ALF")]));
            Assert.Null(roster.Load("ALF"));
            Assert.NotNull(roster.Load("BO"));
            Assert.Equal(["ALF", "BO"], roster.Entries(null)); // the program's Save leaves charlist.txt alone

            // Load: no character file → the .itm stays (0x1FA6B jumps past all three).
            File.WriteAllBytes(Path.Combine(dir.FullName, "CY.ITM"), new byte[63]);
            Assert.Equal(0, roster.RemoveLoaded([Pc("CY")]));
            Assert.True(File.Exists(Path.Combine(dir.FullName, "CY.ITM")));
            // Save erases it on its own test.
            Assert.Equal(1, roster.RemoveSaved([Pc("CY")]));
            // Load with the character file: every file present goes.
            File.WriteAllBytes(Path.Combine(dir.FullName, "BO.ITM"), new byte[63]);
            Assert.Equal(2, roster.RemoveLoaded([Pc("BO")]));
            Assert.Null(roster.Load("BO"));
            Assert.Empty(dir.GetFiles().Where(f => f.Name != PorRoster.ListFile));
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void Roster_reads_a_character_copied_from_the_dos_game()
    {
        var dir = Directory.CreateTempSubdirectory("goldbox-roster-");
        try
        {
            var c = Pc("THORIN");
            File.WriteAllBytes(Path.Combine(dir.FullName, "THORIN.CHA"), c.Record);
            File.WriteAllBytes(Path.Combine(dir.FullName, "THORIN.SPC"), new byte[18]);
            File.WriteAllLines(Path.Combine(dir.FullName, "CHARLIST.TXT"), ["THORIN"]);
            var roster = new PorRoster(dir.FullName);
            Assert.Equal(["THORIN"], roster.Entries(null));
            var back = roster.Load("THORIN")!;
            Assert.Equal(c.Record, back.Record);
            Assert.Equal(2, back.Effects.Count);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    /// <summary>
    /// Begin on a new game: a created character joins, the game starts on ECL3 block 0 at 15,1 facing west (DOSBox-X,
    /// s19: the party menu's Begin shows "15,1 W 00:00" and the area's first text) and the entry runs to its first text.
    /// </summary>
    [PorDataFact]
    public void New_game_starts_on_script_0_at_15_1_west()
    {
        var content = PorData.Open();
        var tables = content.Program()!.CreationTables()!;
        var items = PorFight.Kit(content)!.Items;
        var party = new EclParty();
        var (hero, _) = CharacterCreationProgramTests.Create(tables, items, 99, [1, 1, 1, 1], 0, "HERO");
        PartyMenu.Join(party, hero, c => LevelStats.Apply(c, tables.Levels));
        var host = new ScriptedEclHost { StopWhenInputMissing = true };
        var player = new PorPlayer(host, new ScriptedEncounterUi(host.MenuChoices) { StopWhenInputMissing = true },
            new RecordingCombatRunner(), new ScriptedAreaUi(), QuickFight: true);
        var g = PorGame.NewGame(content, PorViewArt.Load(content), party, player, 5);
        Assert.Equal((15, 1, Facing.West), (g.Memory.Position.X, g.Memory.Position.Y, g.Memory.Position.Facing));
        Assert.Equal(1, g.Memory.Read(EclPartyRules.PartySizeCell));
        g.RunNewGameEntry();
        Assert.Equal(0, g.Vm.CurrentScript);
        Assert.Equal(3, g.Memory.Disk);
        Assert.Equal((15, 1, Facing.West), (g.Memory.Position.X, g.Memory.Position.Y, g.Memory.Position.Facing));
        Assert.Contains(host.Calls, c => c.StartsWith("Print", StringComparison.Ordinal));
        Assert.Same(hero, g.Memory.Party!.Members[0]);
    }
}
