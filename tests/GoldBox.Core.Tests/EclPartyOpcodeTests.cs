using GoldBox.Core.Ecl;
using GoldBox.Core.Tests.Support;
using static GoldBox.Core.Tests.Support.Opnd;

namespace GoldBox.Core.Tests;

/// <summary>
/// The party opcodes read from the program in s11 (docs/re/ecl-format.md "Party, damage and text-box opcodes") and
/// the script-switch rules ("Steps, doors and script switches").
/// </summary>
public class EclPartyOpcodeTests
{
    private const int F = 0x9700;

    private static EclCharacter Member(string name, byte hp = 20)
    {
        var c = new EclCharacter { Name = name };
        c[EclPartyRules.Hp] = hp;
        c[EclPartyRules.InPlay] = 1;
        return c;
    }

    private static EclParty Party(params string[] names)
    {
        var p = new EclParty();
        foreach (var n in names)
            p.Members.Add(Member(n));
        p.CurrentIndex = 0;
        return p;
    }

    private static VmRig Rig(EclAsm asm, EclParty party, EclVmPolicy? policy = null)
    {
        var rig = VmRig.Of(asm, policy, m => m.Party = party);
        rig.Memory.Write(EclPartyRules.PartySizeCell, (ushort)party.Members.Count);
        return rig;
    }

    // ---- LOADCHAR --------------------------------------------------------------------------------------------------

    [Fact]
    public void LoadChar_selects_by_list_index_and_exit_restores_the_selected_character()
    {
        var party = Party("A", "B", "C");
        var rig = Rig(new EclAsm()
            .Op(EclOpcodes.LoadChar, Imm8(2))
            .Op(EclOpcodes.LoadChar, Imm8(9))          // past the end: unchanged
            .Op(EclOpcodes.Exit), party);
        rig.Vm.CommandRead(); // DS:4436 = A
        Assert.Equal(EclStopReason.Exit, rig.Run().Reason);
        Assert.Equal(0, party.CurrentIndex); // EXIT restored A
        Assert.False(party.ChangedByScript);
    }

    [Fact]
    public void LoadChar_past_the_end_keeps_the_current_character()
    {
        var party = Party("A", "B", "C");
        var rig = Rig(new EclAsm().Op(EclOpcodes.LoadChar, Imm8(2)).Op(EclOpcodes.LoadChar, Imm8(9)), party);
        rig.Vm.Step();
        rig.Vm.Step();
        Assert.Equal(2, party.CurrentIndex);
    }

    [Fact]
    public void LoadChar_keeps_its_choice_until_exit()
    {
        var party = Party("A", "B", "C");
        var rig = Rig(new EclAsm().Op(EclOpcodes.LoadChar, Imm8(1)), party);
        rig.Vm.CommandRead();
        rig.Vm.Step();
        Assert.Equal(1, party.CurrentIndex);
        Assert.True(party.ChangedByScript);
    }

    [Fact]
    public void LoadChar_bit_0x80_removes_the_current_character_after_both_zero_writes()
    {
        var party = Party("A", "B", "C");
        var b = party.Members[1];
        var rig = Rig(new EclAsm()
            .Op(EclOpcodes.LoadChar, Imm8(0x81))       // flags not armed: only selects B
            .Op(EclOpcodes.Save, Imm8(0), Mem(0x6B00))
            .Op(EclOpcodes.Save, Imm8(0), Mem(0x6C00))
            .Op(EclOpcodes.LoadChar, Imm8(0x81))       // removes B
            .Op(EclOpcodes.Exit), party);
        rig.Vm.CommandRead(); // A selected
        rig.Vm.Step();
        Assert.Equal(3, party.Members.Count);
        Assert.Equal(1, party.CurrentIndex);
        rig.Run(0);
        Assert.DoesNotContain(b, party.Members);
        Assert.Equal(["A", "C"], party.Members.Select(m => m.Name));
        Assert.Equal(2, rig[EclPartyRules.PartySizeCell]);
        Assert.False(rig.Memory.Ds84D2 || rig.Memory.Ds84D3);
        Assert.Equal("A", party.Current!.Name); // predecessor, and EXIT restored A as well
    }

    [Fact]
    public void LoadChar_removing_the_selected_head_keeps_the_new_head()
    {
        var party = Party("A", "B");
        var rig = Rig(new EclAsm()
            .Op(EclOpcodes.Save, Imm8(0), Mem(0x6B00))
            .Op(EclOpcodes.Save, Imm8(0), Mem(0x6C00))
            .Op(EclOpcodes.LoadChar, Imm8(0x80))
            .Op(EclOpcodes.Exit), party);
        rig.Vm.CommandRead(); // A selected, and A is removed: DS:4430 cleared, no restore
        rig.Run();
        Assert.Equal("B", party.Current!.Name);
        Assert.False(party.ChangedByScript);
    }

    [Fact]
    public void Script_init_clears_the_removal_flags()
    {
        var rig = Rig(new EclAsm().Op(EclOpcodes.Exit), Party("A"));
        rig.Memory.Ds84D2 = rig.Memory.Ds84D3 = true;
        rig.Vm.InitScript();
        Assert.False(rig.Memory.Ds84D2 || rig.Memory.Ds84D3);
    }

    // ---- WHO -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Who_sets_the_current_character_and_it_survives_exit()
    {
        var party = Party("A", "B", "C");
        var rig = Rig(new EclAsm().Op(EclOpcodes.Who, Text("WHO")).Op(EclOpcodes.Exit), party);
        rig.Host.CharacterPicks.Enqueue(2);
        rig.Vm.CommandRead();
        rig.Run();
        Assert.Equal(2, party.CurrentIndex);
    }

    [Fact]
    public void Who_after_escape_leaves_no_current_character()
    {
        var party = Party("A", "B", "C");
        party.CurrentIndex = 1;
        var rig = Rig(new EclAsm().Op(EclOpcodes.Who, Text("WHO")).Op(EclOpcodes.Exit), party);
        rig.Host.CharacterPicks.Enqueue(IEclHost.NoCharacter);
        rig.Vm.CommandRead();
        rig.Run();
        Assert.Equal(-1, party.CurrentIndex);
        Assert.Null(party.Current);
    }

    // ---- CHKPARTY --------------------------------------------------------------------------------------------------

    [Fact]
    public void ChkParty_reports_min_max_average_or_an_effect()
    {
        var party = Party("A", "B", "C");
        byte[] moves = [12, 6, 9];
        for (int i = 0; i < 3; i++)
            party.Members[i][0x11C] = moves[i];
        party.Members[1].Effects.Add([19, 0, 0, 0, 0, 0, 0, 0, 0]);
        var rig = Rig(new EclAsm()
            .Op(EclOpcodes.ChkParty, Mem(0x6C1B), Imm8(0), Mem(F), Mem(F + 1), Mem(F + 2), Mem(F + 3))
            .Op(EclOpcodes.ChkParty, Imm8(0), Imm8(19), Mem(F + 4), Mem(F + 5), Mem(F + 6), Mem(F + 7))
            .Op(EclOpcodes.ChkParty, Imm8(0), Imm8(20), Mem(F + 8), Mem(F + 8), Mem(F + 8), Mem(F + 8))
            .Op(EclOpcodes.Save, Imm8(77), Mem(F + 9))
            .Op(EclOpcodes.ChkParty, Imm16(0x6BA8), Imm8(0), Mem(F + 9), Mem(F + 9), Mem(F + 9), Mem(F + 9))
            .Op(EclOpcodes.Exit), party);
        rig.Run();
        Assert.Equal([6, 12, 9, 0], new[] { rig[F], rig[F + 1], rig[F + 2], rig[F + 3] }.Select(x => (int)x));
        Assert.Equal([0, 0, 0, 1], new[] { rig[F + 4], rig[F + 5], rig[F + 6], rig[F + 7] }.Select(x => (int)x));
        Assert.Equal(0, rig[F + 8]);   // nobody has effect 20
        Assert.Equal(77, rig[F + 9]);  // unknown selector: nothing written
    }

    [Fact]
    public void ChkParty_takes_the_selector_from_the_value_unless_type_1()
    {
        var party = Party("A");
        party.Members[0][0x79] = 40;
        var rig = Rig(new EclAsm()
            .Op(EclOpcodes.Save, Imm16(0x6BA7), Mem(F + 10))
            .Op(EclOpcodes.ChkParty, Mem3(F + 10), Imm8(0), Mem(F), Mem(F + 1), Mem(F + 2), Mem(F + 3)) // type 3: value
            .Op(EclOpcodes.Exit), party);
        rig.Run();
        Assert.Equal([40, 40, 40, 0], new[] { rig[F], rig[F + 1], rig[F + 2], rig[F + 3] }.Select(x => (int)x));
    }

    // ---- SPELL -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Spell_writes_to_the_low_byte_addresses_only()
    {
        var party = Party("A", "B");
        party.Members[0][0x17 + 5] = 31;
        var rig = Rig(new EclAsm()
            .Op(EclOpcodes.Save, Imm8(9), Mem(0x6E7D))
            .Op(EclOpcodes.Spell, Imm8(31), Mem(0x6EB8), Mem(0x6EB9))
            .Op(EclOpcodes.Exit), party, new EclVmPolicy { SpellFoundResidue = 0 });
        rig.Run();
        Assert.Equal(9, rig[0x6E7D]);                         // ECL3 block 14's test cell keeps its value
        Assert.Equal(5, rig.Memory.SpecialWriteValues[0x00B8]); // index of the match
        Assert.Equal(1, rig.Memory.SpecialWriteValues[0x00B9]); // one member scanned
    }

    [Fact]
    public void Spell_scans_only_the_first_member_and_honours_the_residue_policy()
    {
        var party = Party("A", "B");
        party.Members[1][0x18] = 31;
        var asm = new EclAsm().Op(EclOpcodes.Spell, Imm8(31), Mem(0xB8), Mem(0xB9)).Op(EclOpcodes.Exit);
        var rig = Rig(asm, party, new EclVmPolicy { SpellFoundResidue = 0 });
        rig.Run();
        Assert.Equal(255, rig.Memory.SpecialWriteValues[0x00B8]);
        Assert.Equal(1, rig.Memory.SpecialWriteValues[0x00B9]);

        var rig2 = Rig(asm, Party("A")); // default: the residue DOSBox showed
        rig2.Run();
        Assert.Equal(1, rig2.Memory.SpecialWriteValues[0x00B8]);
        Assert.Equal(0, rig2.Memory.SpecialWriteValues[0x00B9]);
    }

    /// <summary>
    /// v1.3's one SPELL (ECL3 block 14 $9ECA, "cast knock") writes to $007D / $007E, which no VM area holds, so the
    /// residue byte decides nothing: with the search run (0) or skipped (non-zero, 0xCA in DOSBox there) every VM
    /// cell, the party and the script's next test come out the same.
    /// </summary>
    [Fact]
    public void Spell_in_the_knock_door_script_does_not_depend_on_the_residue()
    {
        (ushort[], ushort[], ushort[], int) Run(byte residue)
        {
            var party = Party("A", "B");
            party.Members[0][0x17 + 3] = 31; // the first member has Knock memorised: the search finds it
            var rig = Rig(new EclAsm()
                .Op(EclOpcodes.Save, Imm8(7), Mem(0x6E7D))
                .Op(EclOpcodes.Spell, Imm8(31), Mem(0x6E7D), Mem(0x6E7E))
                .Op(EclOpcodes.Exit), party, new EclVmPolicy { SpellFoundResidue = residue });
            rig.Run();
            return (rig.Memory.Cells4900.ToArray(), rig.Memory.Cells6B00.ToArray(), rig.Memory.Cells9700.ToArray(),
                party.CurrentIndex);
        }
        var searched = Run(0);
        var skipped = Run(0xCA);
        Assert.Equal(searched.Item1, skipped.Item1);
        Assert.Equal(searched.Item2, skipped.Item2);
        Assert.Equal(searched.Item3, skipped.Item3);
        Assert.Equal(searched.Item4, skipped.Item4);
    }

    // ---- ROB -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Rob_cuts_money_and_loses_items_with_the_carried_over_chance()
    {
        var party = Party("A", "B");
        var a = party.Members[0];
        a.SetWord(0x88, 10);   // copper
        a.SetWord(0x8E, 99);   // gold
        static byte[] Item(int weight)
        {
            var r = new byte[EclCharacter.ItemRecordSize];
            r[0x37] = (byte)weight;
            r[0x38] = (byte)(weight >> 8);
            return r;
        }
        var heavy = Item(300);  // chance 60 → 0
        var light = Item(10);   // chance stays 0 (carried over)
        a.Items.AddRange([heavy, light]);
        var rig = Rig(new EclAsm().Op(EclOpcodes.Rob, Imm8(0), Imm8(60), Imm8(100)).Op(EclOpcodes.Exit), party);
        rig.Host.RandomValues.Enqueue(0); // 1d100 = 1 > 0: kept
        rig.Host.RandomValues.Enqueue(0); // 1d100 = 1 > 0: kept
        rig.Run();
        Assert.Equal(4, a.Word(0x88));   // 10 × 0.4
        Assert.Equal(39, a.Word(0x8E));  // 99 × 0.4 = 39.6 → 39
        Assert.Equal(2, a.Items.Count);
        Assert.Equal(2, rig.Host.RandomLog.Count); // one 1d100 per item, also at chance 0
    }

    [Fact]
    public void Rob_of_the_whole_party_loses_light_items_on_a_low_roll()
    {
        var party = Party("A", "B");
        party.Members[1].Items.Add(new byte[EclCharacter.ItemRecordSize]);
        var rig = Rig(new EclAsm().Op(EclOpcodes.Rob, Imm8(1), Imm8(10), Imm8(0)).Op(EclOpcodes.Exit), party);
        rig.Host.RandomValues.Enqueue(9); // 1d100 = 10 ≤ 10 (the chance is v2, not v3)
        rig.Run();
        Assert.Empty(party.Members[1].Items);
    }

    // ---- DAMAGE ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Damage_save_form_on_a_random_member_draws_damage_then_target_then_save()
    {
        var party = Party("A", "B", "C");
        party.Members[1][0x6D + 2] = 15; // save target, type 2
        var rig = Rig(new EclAsm().Op(EclOpcodes.Damage, Imm8(0x80 | 3), Imm8(2), Imm8(6), Imm8(1), Imm8(2)).Op(EclOpcodes.Exit), party);
        foreach (var r in new ushort[] { 2, 3, 1, 9 }) // 2d6 = 3 + 4, +1 → 8; target 1d3 = 2 (B); save 1d20 = 10 + 3 < 15
            rig.Host.RandomValues.Enqueue(r);
        rig.Run();
        Assert.Equal([6, 6, 3, 20], rig.Host.RandomLog.Select(x => (int)x.Range));
        Assert.Equal(12, party.Members[1][EclPartyRules.Hp]);
        Assert.Equal(20, party.Members[0][EclPartyRules.Hp]);
        Assert.Contains("Damage B 8", rig.Host.Calls);
    }

    [Fact]
    public void Damage_attack_form_rerolls_damage_after_every_attack()
    {
        var party = Party("A");
        party.Members[0][EclPartyRules.Ac] = 50;
        var rig = Rig(new EclAsm().Op(EclOpcodes.Damage, Imm8(2), Imm8(1), Imm8(4), Imm8(0), Imm8(40)).Op(EclOpcodes.Exit), party);
        // d = 1d4; r = 1d1 (before the loop); per attack: r, 1d20, new d.
        foreach (var r in new ushort[] { 3, 0, 0, 14, 1, 0, 0, 2 })
            rig.Host.RandomValues.Enqueue(r);
        rig.Run();
        // attack 1: 15 + 40 > 50 hits for 4; attack 2: 1 misses.
        Assert.Equal(16, party.Members[0][EclPartyRules.Hp]);
        Assert.Equal(8, rig.Host.RandomLog.Count);
    }

    [Fact]
    public void Damage_to_the_whole_party_without_save_can_end_the_run()
    {
        var party = Party("A", "B");
        var rig = Rig(new EclAsm()
            .Op(EclOpcodes.Damage, Imm8(0x80 | 0x40 | 0x20), Imm8(1), Imm8(1), Imm8(40), Imm8(0))
            .Op(EclOpcodes.Save, Imm8(1), Mem(F)), party);
        var r = rig.Run();
        Assert.Equal(EclStopReason.ExternalStop, r.Reason);
        Assert.Equal(0, rig[F]);
        Assert.All(party.Members, m => Assert.Equal(6, m[EclPartyRules.Status])); // 41 − 20 = 21 over: dead
        Assert.Contains("Damage A 41 dies", rig.Host.Calls);
    }

    [Theory]
    [InlineData(10, 0, 4, 0, 6, 1)]   // hp ≥ d: hp − d, status unchanged
    [InlineData(10, 0, 10, 4, 0, 0)]  // exactly 0: unconscious, out of play
    [InlineData(10, 0, 15, 5, 0, 0)]  // 5 below: dying
    [InlineData(10, 0, 20, 6, 0, 0)]  // 10 below: dead
    [InlineData(10, 1, 10, 6, 0, 0)]  // animated at 0: dead
    [InlineData(0, 4, 3, 5, 0, 0)]    // unconscious hit again: dying
    public void Damage_status_rules(byte hp, byte status, int damage, byte newStatus, byte newHp, byte inPlay)
    {
        var m = Member("X", hp);
        m[EclPartyRules.Status] = status;
        new EclPartyRules(new EclMemory(), _ => 0).Apply(m, damage);
        Assert.Equal(newStatus, m[EclPartyRules.Status]);
        Assert.Equal(newHp, m[EclPartyRules.Hp]);
        Assert.Equal(inPlay, m[EclPartyRules.InPlay]);
    }

    [Fact]
    public void Damage_ignores_dead_creatures()
    {
        var m = Member("X", 0);
        m[EclPartyRules.Status] = 6;
        m[EclPartyRules.InPlay] = 0;
        var reports = 0;
        var rules = new EclPartyRules(new EclMemory(), _ => 0);
        rules.Damaged += _ => reports++;
        rules.Apply(m, 5);
        Assert.Equal(0, reports);
    }

    // ---- ADDNPC ----------------------------------------------------------------------------------------------------

    [Fact]
    public void AddNpc_appends_the_record_as_current_and_sets_morale()
    {
        var party = Party("A", "B");
        party.Members[0][0xBF] = 0;
        party.Members[1][0xBF] = 1;
        var rig = Rig(new EclAsm().Op(EclOpcodes.AddNpc, Imm8(24), Imm8(99)).Op(EclOpcodes.Exit), party);
        rig.Host.NpcFactory = m => new EclCharacter { Name = "NPC" + m };
        rig.Run();
        Assert.Equal(3, party.Members.Count);
        var npc = party.Current!;
        Assert.Equal("NPC24", npc.Name);
        Assert.Equal(2, npc[0xBF]);
        Assert.Equal(0xB1, npc[EclPartyRules.Morale]);
        Assert.Equal(1, npc[EclPartyRules.Hostile]); // monster 24
        Assert.Equal(3, rig[EclPartyRules.PartySizeCell]);
    }

    [Fact]
    public void AddNpc_with_a_full_party_only_sets_the_current_characters_morale()
    {
        var party = Party("1", "2", "3", "4", "5", "6", "7", "8");
        party.CurrentIndex = 3;
        var rig = Rig(new EclAsm().Op(EclOpcodes.AddNpc, Imm8(5), Imm8(100)).Op(EclOpcodes.Exit), party);
        rig.Host.NpcFactory = _ => throw new InvalidOperationException("not called with 8 members");
        rig.Run();
        Assert.Equal(8, party.Members.Count);
        Assert.Equal(0xB2, party.Members[3][EclPartyRules.Morale]);
        Assert.Equal(0, party.Members[3][EclPartyRules.Hostile]);
    }

    // ---- PROTECT ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Protect_draws_bytes_plus_one_until_0_or_255()
    {
        var asm = new EclAsm().Op(EclOpcodes.Protect, L("runes")).Op(EclOpcodes.Exit).Label("runes").Bytes(4, 7, 1, 0xFF, 9);
        var rig = VmRig.Of(asm);
        rig.Run();
        Assert.Contains(rig.Host.Calls, c => c.StartsWith("DrawGlyphs", StringComparison.Ordinal) && c.EndsWith(" 5 8 2", StringComparison.Ordinal));
    }

    // ---- Script switches -------------------------------------------------------------------------------------------

    private sealed class Scripts(Dictionary<(int, int), byte[]> blocks) : IEclScriptSource
    {
        public byte[]? LoadBlock(int disk, int script) => blocks.GetValueOrDefault((disk, script));
    }

    private static byte[] Countdown(int mark, int? nextScript = null)
    {
        var a = new EclAsm()
            .Label("move").Op(EclOpcodes.Save, Imm8(1), Mem(F + mark)).Op(EclOpcodes.Exit)
            .Label("search").Op(EclOpcodes.Save, Imm8(1), Mem(F + mark + 1)).Op(EclOpcodes.Exit)
            .Label("init").Op(EclOpcodes.Save, Imm8(1), Mem(F + mark + 4));
        if (nextScript is { } n)
            a.Op(EclOpcodes.NewEcl, Imm8(n));
        else
            a.Op(EclOpcodes.Exit);
        return a.Vector(0, "move").Vector(1, "search").Vector(4, "init").Build();
    }

    [Fact]
    public void NewEcl_in_a_vector_4_skips_the_newest_scripts_vector_4()
    {
        var first = new EclAsm().Op(EclOpcodes.NewEcl, Imm8(2));
        var src = new Scripts(new() { [(0, 2)] = Countdown(10, nextScript: 3), [(0, 3)] = Countdown(20) });
        var rig = VmRig.Of(first);
        rig.Vm.Scripts = src;
        Assert.Equal(EclStopReason.NewScript, rig.Run().Reason);
        Assert.True(rig.Vm.ScriptChanged);
        rig.Vm.FollowScriptChanges();
        Assert.False(rig.Vm.ScriptChanged);
        Assert.Equal(3, rig.Vm.CurrentScript);
        Assert.Equal(1, rig[F + 14]);  // script 2's vector 4 ran (and switched)
        Assert.Equal(0, rig[F + 10]);  // script 2's vectors 0/1 did not
        Assert.Equal(0, rig[F + 24]);  // script 3's vector 4 skipped
        Assert.Equal([1, 1], new[] { rig[F + 20], rig[F + 21] }.Select(x => (int)x)); // script 3's vectors 0 and 1
    }

    [Theory]
    [InlineData(1, false, true)]  // 3D → 3D: always redrawn
    [InlineData(3, false, false)] // leaving the wilderness, no LOADFILES in vector 4: no redraw (0x3FD2–0x3FF5)
    [InlineData(3, true, true)]   // ... with a LOADFILES: redrawn
    public void Script_switch_redraw_follows_49F9_49FA_and_4431(byte before, bool loadFiles, bool redrawn)
    {
        var init = new EclAsm().Label("init");
        if (loadFiles)
            init.Op(EclOpcodes.LoadFiles, Imm8(255), Imm8(0), Imm8(255));
        var target = init.Op(EclOpcodes.Exit).Label("v").Op(EclOpcodes.Exit)
            .Vector(4, "init").Vector(0, "v").Vector(1, "v").Build();
        var rig = VmRig.Of(new EclAsm().Op(EclOpcodes.NewEcl, Imm8(2)));
        rig.Vm.Scripts = new Scripts(new() { [(0, 2)] = target });
        rig.Memory.ViewMode = before;
        var reads = new List<bool>();
        rig.Vm.ScriptSwitchEventByte = after => { reads.Add(after); return 0; };
        rig.Run();
        rig.Vm.FollowScriptChanges();
        Assert.Equal(before, rig.Memory.PreviousViewMode);
        Assert.Equal(1, rig.Memory.ViewMode);
        Assert.Equal(loadFiles, rig.Memory.FilesLoadedSinceSwitch);
        Assert.Equal(redrawn, reads.Contains(true));
    }

    [Fact]
    public void NewEcl_in_the_vector_4_of_a_load_runs_nothing_until_the_next_command()
    {
        var rig = VmRig.Of(new EclAsm().Op(EclOpcodes.Exit));
        rig.Vm.Scripts = new Scripts(new() { [(0, 3)] = Countdown(20) });
        rig.Memory.LoadScriptBlock(Countdown(10, nextScript: 3));
        rig.Vm.SetCurrentScript(2, 0);
        var r = rig.Vm.RunLoadedGame();
        Assert.Equal(EclStopReason.NewScript, r.Reason);
        Assert.Equal(3, rig.Vm.CurrentScript);
        Assert.True(rig.Vm.ScriptChanged);                 // DS:442F left set
        Assert.Equal(0, rig[F + 20] + rig[F + 21] + rig[F + 24]);
        // Next command: vector 0 of script 3 runs; the flag is still set, so the switch is followed (4, 0, 1).
        Assert.Equal(EclStopReason.Exit, rig.Vm.RunVector(0).Reason);
        Assert.True(rig.Vm.ScriptChanged);
        rig.Vm.FollowScriptChanges();
        Assert.Equal(1, rig[F + 24]);
        Assert.False(rig.Vm.ScriptChanged);
    }

    [Fact]
    public void Script_switch_restores_the_current_character_after_vector_1()
    {
        var party = Party("A", "B");
        var second = new EclAsm()
            .Label("move").Op(EclOpcodes.LoadChar, Imm8(1)).Op(EclOpcodes.Save, Imm8(1), Mem(F)).Op(EclOpcodes.Return)
            .Label("search").Op(EclOpcodes.Exit)
            .Label("init").Op(EclOpcodes.Exit)
            .Vector(0, "move").Vector(1, "search").Vector(4, "init");
        var rig = Rig(new EclAsm().Op(EclOpcodes.NewEcl, Imm8(5)), party);
        rig.Vm.Scripts = new Scripts(new() { [(0, 5)] = second.Build() });
        rig.Run();
        // vector 0 selects B and does not EXIT (RETURN falls through into the search code's EXIT, which restores the
        // selected character only when one was noted). Without CommandRead, Selected is null: 0x4016 restores A.
        rig.Vm.FollowScriptChanges();
        Assert.Equal("A", party.Current?.Name);
    }

    [Fact]
    public void View_mode_follows_the_wilderness_rule_after_vector_4()
    {
        var wild = new EclAsm()
            .Label("init").Op(EclOpcodes.Save, Imm8(0), Mem(0x49E6)).Op(EclOpcodes.Exit)
            .Label("other").Op(EclOpcodes.Exit)
            .Vector(0, "other").Vector(1, "other").Vector(4, "init");
        var rig = VmRig.Of(new EclAsm().Op(EclOpcodes.NewEcl, Imm8(26)));
        rig.Vm.Scripts = new Scripts(new() { [(0, 26)] = wild.Build() });
        rig.Run();
        rig.Vm.FollowScriptChanges();
        Assert.Equal(3, rig.Memory.ViewMode);
    }
}

/// <summary>The forward key's $6DD5 rule (GAME.OVR 0x16D58).</summary>
public class ForwardKeyEdgeTests
{
    private static byte[] Planes(int x, int y, int eastWall, int eastBarrier)
    {
        var p = new byte[1024];
        p[y * 16 + x] = (byte)eastWall;                          // plane 0: N high nibble, E low nibble
        p[768 + y * 16 + x] = (byte)(eastBarrier << 2); // E face: bits 2–3 (DoorMap.Shift)
        return p;
    }

    [Theory]
    [InlineData(0, 0, true)]   // no wall: leaves the map
    [InlineData(3, 1, true)]   // passable wall (archway)
    [InlineData(3, 2, true)]   // a door on the edge: still "left the map"
    [InlineData(3, 0, false)]  // impassable face on the edge: $6DD5 stays 0
    public void Edge_step_sets_6DD5_only_through_a_face_that_is_not_impassable(int wall, int barrier, bool leaves)
    {
        Assert.Equal(leaves, GoldBox.Core.Doors.DoorMap.LeavesMap(Planes(15, 4, wall, barrier), 15, 4, 2));
        Assert.False(GoldBox.Core.Doors.DoorMap.LeavesMap(Planes(14, 4, wall, barrier), 14, 4, 2)); // inside the grid
    }
}
