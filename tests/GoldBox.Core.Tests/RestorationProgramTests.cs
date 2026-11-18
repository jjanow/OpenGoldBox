using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>
/// Restoration (spell 56, GAME.OVR 0x29C5B) at and past the race level limit, against the program itself: the private
/// fixture <c>program-v1.3/restoration.json</c> (<c>tools/scripts/por_restore_oracle.py</c>) holds a grid of drained
/// party-member records (every row of START 0x3AF7 at, below and above its level with Strength 16–18, multi-classes
/// with one class at its limit, levels 9–14 and 255) before and after the routine ran under emulation with the real
/// race limit and level stats (0x2AA87). The no-class index is seeded with DOSBox-X's 8, so every byte is compared.
/// </summary>
public class RestorationProgramTests
{
    private static readonly string? File_ = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } d
        ? Path.Combine(d, "..", "program-v1.3", "restoration.json") : null;

    public sealed class OracleFactAttribute : FactAttribute
    {
        public OracleFactAttribute()
        {
            if (!PorData.Present)
                Skip = PorData.SkipReason;
            else if (File_ is null || !File.Exists(File_))
                Skip = "needs the private fixture program-v1.3/restoration.json";
        }
    }

    private static readonly Lazy<(SpellTable Table, int[] Xp, LevelStatsTables Levels)> Program = new(() =>
    {
        var p = PorProgram.Open(PorData.Open())!;
        return (PorSpellData.Spells(p)!, PorSpellData.ExperienceTable(p)!, PorSpellData.LevelTables(p)!);
    });

    /// <summary>Runs spell 56 on <paramref name="target"/> with the seams wired as the game wires them.</summary>
    private static void Restore(EclCharacter target)
    {
        var (table, xp, levels) = Program.Value;
        var caster = new EclCharacter { Name = "CASTER" };
        var memory = new EclMemory { Party = new EclParty { CurrentIndex = 0 } };
        memory.Party.Members.AddRange([caster, target]);
        var fx = new EffectHandlers(_ => 0) { ExperienceTable = xp };
        FightKit.WireLevels(fx, levels);
        var fight = new Fight(memory, new EncounterState(), new BaseItemTable([]), _ => 0, new EndActionsController(),
            new GridGeometry(), fx);
        var spells = new FightSpells(table, fx, _ => 0);
        fight.Spells = spells;
        spells.Range(fight, caster, 1); // binds the engine to the fight
        fx.InCombat = false;
        fight.State.Current = caster;
        spells.Targets.Add(target);
        fx.Cells.Spell = 56;
        spells.Routine(56, caster);
        fx.Cells.Spell = 0;
    }

    [OracleFact]
    public void Restoration_matches_the_program_at_and_past_the_race_limit()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(File_!));
        Assert.Equal(FightSpells.RestorationResidueIndex, doc.RootElement.GetProperty("residue").GetInt32());
        var fails = new List<string>();
        int n = 0, residue = 0, raised = 0;
        foreach (var k in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var before = Convert.FromHexString(k.GetProperty("before").GetString()!);
            var want = Convert.FromHexString(k.GetProperty("after").GetString()!);
            var c = new EclCharacter((byte[])before.Clone());
            Restore(c);
            for (int b = 0; b < want.Length; b++)
                if (c.Record[b] != want[b])
                    fails.Add($"case {n} +{b:X2}: {c.Record[b]} vs {want[b]}");
            if (want[0x9E] != before[0x9E])
                residue++;
            else if (want[0x74] != before[0x74])
                raised++;
            n++;
        }
        Assert.True(n >= 600, $"{n} cases");
        Assert.True(residue > 50 && raised > 300, $"{residue} with no class, {raised} raised");
        Assert.True(fails.Count == 0, $"{fails.Count} differences:\n" + string.Join("\n", fails.Take(40)));
    }

    private static EclCharacter Drained(byte race, byte strength, int cls, byte level, int xp, int cls2 = -1, byte level2 = 0)
    {
        var c = new EclCharacter { Name = "T" };
        c[0x2E] = race;
        c[0x10] = strength;
        c[EffectHandlers.ClassLevels + cls] = level;
        if (cls2 >= 0)
            c[EffectHandlers.ClassLevels + cls2] = level2;
        c[0x74] = 1;
        c[0x75] = 6;
        c[EffectHandlers.HpMax] = 30;
        c[EffectHandlers.HpRolled] = 30;
        c[FightRules.Hp] = 30;
        c[0xAC] = (byte)xp;
        c[0xAD] = (byte)(xp >> 8);
        c[0xAE] = (byte)(xp >> 16);
        c[0xAF] = (byte)(xp >> 24);
        return c;
    }

    private static int Xp(EclCharacter c) => c[0xAC] | (c[0xAD] << 8) | (c[0xAE] << 16) | (c[0xAF] << 24);

    /// <summary>
    /// The cases spells-effects.md "Restoration at the race limit" names, on the player's program: one level below the
    /// limit the level comes back; at the limit (START 0x3AF7 is an equality) a single-class character keeps its
    /// level, gets the hit points back, the gender byte + 1 (index 8) and experience 10,000,000; a multi-class
    /// half-elf raises the other class; a gnome fighter above its limit gains another level.
    /// </summary>
    [PorFact]
    public void Race_limit_cases()
    {
        const int Fighter = 2, Cleric = 0;
        // Elf fighter (race 2) of level 6, Strength 18: the limit is 7, so 6 → 7, experience up to the level-7 threshold.
        var below = Drained(2, 18, Fighter, 6, 0);
        Restore(below);
        Assert.Equal(7, below[EffectHandlers.ClassLevels + Fighter]);
        Assert.Equal(Program.Value.Xp[Fighter * 14 + 7], Xp(below));
        Assert.Equal(36, below[EffectHandlers.HpMax]);
        Assert.Equal(0, below[0x74]);

        // Elf fighter of level 7: stopped. Hit points back, no level, gender + 1, experience 10,000,000.
        var at = Drained(2, 18, Fighter, 7, 50_000);
        Restore(at);
        Assert.Equal(7, at[EffectHandlers.ClassLevels + Fighter]);
        Assert.Equal(1, at[0x9E]);
        Assert.Equal(10_000_000, Xp(at));
        Assert.Equal(36, at[EffectHandlers.HpMax]);
        Assert.Equal(0, at[0x74]);

        // Half-elf (4) cleric 5 (stopped) / fighter 6: the fighter gains the level.
        var multi = Drained(4, 17, Cleric, 5, 0, Fighter, 6);
        Restore(multi);
        Assert.Equal(5, multi[EffectHandlers.ClassLevels + Cleric]);
        Assert.Equal(7, multi[EffectHandlers.ClassLevels + Fighter]);
        Assert.Equal(0, multi[0x9E]);

        // Gnome (3) fighter of level 7, above the limit of 6: not stopped (equality), 7 → 8.
        var past = Drained(3, 18, Fighter, 7, 0);
        Restore(past);
        Assert.Equal(8, past[EffectHandlers.ClassLevels + Fighter]);
        Assert.Equal(Program.Value.Xp[Fighter * 14 + 8], Xp(past));
    }

    private static readonly string? LimitDir = FightCapture.Folder("restoration_limit");

    public sealed class LimitFactAttribute : FactAttribute
    {
        public LimitFactAttribute()
        {
            if (!PorData.Present)
                Skip = PorData.SkipReason;
            else if (LimitDir is null)
                Skip = "needs the private DOSBox captures (restoration_limit)";
        }
    }

    /// <summary>
    /// DOSBox-X (fixture <c>restoration_limit</c>, README; residue probe): a scroll of two Restorations read outside a
    /// fight on two drained party members with no class to raise, a human fighter 8 (the fighter's top level, male)
    /// and an elf fighter 7 with Strength 18 (the elf's limit row, female). The probe read the class index as 8 both
    /// times (BP 3BD0), and the camp save after it holds the records the routine gives with index 8 plus the stat
    /// recalculation (START 0x1758): no level, hit points + g, experience 10,000,000, gender + 1 (the elf's 1 → 2,
    /// which View shows as "COPPER").
    /// </summary>
    [LimitFact]
    public void At_the_limit_matches_the_dosbox_party_records()
    {
        var before = PorParty.Load(PorSaveGame.Open(Path.Combine(LimitDir!, "before", "SAVGAMF.DAT")), Path.Combine(LimitDir!, "before"));
        var after = PorParty.Load(PorSaveGame.Open(Path.Combine(LimitDir!, "after", "SAVGAMG.DAT")), Path.Combine(LimitDir!, "after"));
        var items = PorData.Open().ItemTypes()!;
        var diffs = new List<string>();
        foreach (var (m, gender) in new[] { (0, 1), (2, 2) })
        {
            var c = before.Members[m];
            Restore(c);
            StatRecalc.Recalculate(c, items);
            var want = after.Members[m];
            Assert.Equal(gender, want[0x9E]);
            for (int b = 0; b < EclCharacter.RecordSize; b++)
                if (b is not (>= 0x7F and < 0x83 or >= 0xC8 and < 0x108) && c[b] != want[b]) // pointers are the program's
                    diffs.Add($"{c.Name} +{b:X2}: {c[b]} vs DOSBox {want[b]}");
            Assert.Equal(10_000_000, Xp(c));
        }
        Assert.True(diffs.Count == 0, string.Join("\n", diffs));
    }
}
