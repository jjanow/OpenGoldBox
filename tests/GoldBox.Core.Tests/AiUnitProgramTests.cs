using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;

namespace GoldBox.Core.Tests;

/// <summary>
/// The AI unit's own routines (GAME.OVR unit 0x38) against the program, s52: the sections worth, step, morale, finish
/// and choose of <c>program-v1.3/ai_cast.json</c> (<c>tools/scripts/por_ai_oracle.py</c>): the weapon worth 0xBE4D,
/// the step test 0xB235, morale 0xBC97 (the status change 0x2BDF0 trapped), the finish 0xBB0F, and the item and spell
/// choices 0xAFA4 / 0xB105 with the should-cast test 0xAEAB run and the use / cast logged.
/// </summary>
public partial class AiCastProgramTests
{
    private static readonly Lazy<byte[]> Steps = new(() => PorProgram.Open(PorData.Open())!.AiStepOffsets(41)!);

    private static string Hex(ReadOnlySpan<byte> b) => Convert.ToHexString(b).ToLowerInvariant();

    [OracleFact]
    public void Weapon_worth_matches_0xBE4D()
    {
        int n = 0;
        var fails = new List<string>();
        foreach (var c in Root.Value.GetProperty("worth").EnumerateArray())
        {
            var k = Build(c, 1);
            k.Fight.Record(k.All[0]).Other = c.GetProperty("other").GetInt32() != 0 ? k.All[1] : null;
            var item = Convert.FromHexString(c.GetProperty("item").GetString()!);
            int got = MonsterAi.WeaponPriority(k.Fight, k.All[0], item), want = c.GetProperty("worth").GetInt32();
            if (got != want)
                fails.Add($"case {n} item {c.GetProperty("item").GetString()![92..]}: {got} vs {want}");
            n++;
        }
        Assert.True(n >= 200);
        Assert.True(fails.Count == 0, string.Join("\n", fails.Take(20)));
    }

    [OracleFact]
    public void Can_step_matches_0xB235()
    {
        int n = 0;
        var fails = new List<string>();
        foreach (var c in Root.Value.GetProperty("step").EnumerateArray())
        {
            var k = Build(c, 1);
            var rec = k.Fight.Record(k.All[0]);
            rec[MonsterAi.StepRow] = (byte)c.GetProperty("row").GetInt32();
            rec[CombatRecord.Movement] = (byte)c.GetProperty("move").GetInt32();
            var ai = new MonsterAi(Steps.Value);
            bool ok = ai.CanStep(k.Fight, k.All[0], c.GetProperty("attempt").GetInt32(), c.GetProperty("base").GetInt32(), out bool edge);
            var got = (ok ? 1 : 0, edge ? 1 : 0);
            var want = (c.GetProperty("result").GetInt32(), c.GetProperty("edge").GetInt32());
            if (got != want)
                fails.Add($"case {n}: {got} vs {want}");
            n++;
        }
        Assert.True(n >= 300);
        Assert.True(fails.Count == 0, string.Join("\n", fails.Take(20)));
    }

    [OracleFact]
    public void Morale_matches_0xBC97()
    {
        int n = 0;
        var fails = new List<string>();
        foreach (var c in Root.Value.GetProperty("morale").EnumerateArray())
        {
            var k = Build(c, 1);
            var me = k.All[0];
            var rec = k.Fight.Record(me);
            new byte[] { 1, 1, 1, 7, 0, 0, 6, 0 }.CopyTo(rec.Bytes, 0);
            rec[MonsterAi.ForcedFlee] = (byte)c.GetProperty("forced").GetInt32();
            k.Fight.State.HostileHpPercent = (byte)c.GetProperty("sidePct").GetInt32();
            k.Fight.Memory.Write(MonsterAi.MoraleThresholdCell, (ushort)c.GetProperty("threshold").GetInt32());
            k.Fight.State.Morale = 0xEE;
            var said = new List<int>();
            var ai = new MonsterAi(Steps.Value) { Message = (_, m) => { if (m == AiMessage.Surrenders) said.Add(4); } };
            bool done = ai.CheckMorale(k.Fight, me);
            // A surrender's 0x2BDF0 is trapped in the oracle (its dropped-effects removal 0x2BEF4 does not run there):
            // the effects are compared only without one.
            bool gaveUp = c.GetProperty("surrender").GetArrayLength() > 0;
            var got = (done ? 1 : 0, Hex(rec.Bytes), (int)k.Fight.State.Morale,
                gaveUp ? "" : string.Join(",", me.Effects.Select(e => (int)e[0])), string.Join(",", said));
            var want = (c.GetProperty("result").GetInt32(), c.GetProperty("combat").GetString(), c.GetProperty("morale").GetInt32(),
                gaveUp ? "" : string.Join(",", c.GetProperty("effects").EnumerateArray().Select(e => e.GetInt32())),
                string.Join(",", c.GetProperty("surrender").EnumerateArray().Select(e => e.GetInt32())));
            if (got != want)
                fails.Add($"case {n}: {got} vs {want}");
            n++;
        }
        Assert.True(n >= 200);
        Assert.True(fails.Count == 0, string.Join("\n", fails.Take(20)));
    }

    [OracleFact]
    public void Finish_matches_0xBB0F()
    {
        int n = 0;
        var fails = new List<string>();
        foreach (var c in Root.Value.GetProperty("finish").EnumerateArray())
        {
            var k = Build(c, 1);
            var me = k.All[0];
            if (c.GetProperty("item").ValueKind == JsonValueKind.String)
                me.Items.Add(Convert.FromHexString(c.GetProperty("item").GetString()!));
            var rec = k.Fight.Record(me);
            new byte[] { 2, 1, 1, (byte)c.GetProperty("initiative").GetInt32(), 0, 0, 6, 0 }.CopyTo(rec.Bytes, 0);
            bool r = MonsterAi.FinishRound(k.Fight, me);
            var got = (r ? 1 : 0, Hex(rec.Bytes));
            var want = (c.GetProperty("result").GetInt32(), c.GetProperty("combat").GetString());
            if (got != want)
                fails.Add($"case {n}: {got} vs {want}");
            n++;
        }
        Assert.True(n >= 100);
        Assert.True(fails.Count == 0, string.Join("\n", fails.Take(20)));
    }

    /// <summary>The real spell code for what the AI asks (priority, range, cure target); the cast and the item use logged.</summary>
    private sealed class LoggedSpells(FightSpells inner, bool scripted) : IFightSpells
    {
        public int Cast { get; private set; }
        public byte[]? Used { get; private set; }
        public IReadOnlyList<int> Castable(Fight fight, EclCharacter caster) => inner.Castable(fight, caster);
        bool IFightSpells.Cast(Fight fight, EclCharacter caster, int spell, EclCharacter? target, (int X, int Y)? square) => false;
        public (byte Priority, byte Targeted, byte Area) AiInfo(int spell) => inner.AiInfo(spell);
        public int Range(Fight fight, EclCharacter caster, int spell) => inner.Range(fight, caster, spell);
        public bool FindCureTarget(Fight fight, EclCharacter caster) => inner.FindCureTarget(fight, caster);
        public bool CastAuto(Fight fight, EclCharacter caster, int spell)
        {
            Cast = spell;
            return scripted;
        }
        public void UseItem(Fight fight, EclCharacter user, byte[] item) => Used = item;
    }

    [OracleFact]
    public void Item_and_spell_choice_match_0xAFA4_and_0xB105()
    {
        int n = 0;
        var fails = new List<string>();
        foreach (var c in Root.Value.GetProperty("choose").EnumerateArray())
        {
            var k = Build(c, c.GetProperty("seed").GetUInt32());
            var me = k.All[0];
            var items = c.GetProperty("items").EnumerateArray().Select(e => Convert.FromHexString(e.GetString()!)).ToList();
            me.Items.AddRange(items);
            Convert.FromHexString(c.GetProperty("combat").GetString()!).CopyTo(k.Fight.Record(me).Bytes, 0);
            var teams = c.GetProperty("teams");
            k.Fight.State.TeamCount[0] = (byte)teams[0].GetInt32();
            k.Fight.State.TeamCount[1] = (byte)teams[1].GetInt32();
            k.Fight.State.QuickMagic = c.GetProperty("quick").GetInt32() != 0;
            k.Fight.Memory.Write(MonsterAi.NoItemUseCell, (ushort)c.GetProperty("anti").GetInt32());
            var spells = new LoggedSpells(k.Spells, c.GetProperty("scripted").GetInt32() != 0);
            k.Fight.Spells = spells;
            var ai = new MonsterAi(Steps.Value);
            bool r = c.GetProperty("op").GetString() == "item" ? ai.UseItem(k.Fight, me) : ai.CastSpell(k.Fight, me);
            var got = (r ? 1 : 0, k.Rng.Seed, spells.Used is null ? -1 : items.IndexOf(spells.Used), spells.Cast);
            var want = (c.GetProperty("result").GetInt32(), c.GetProperty("seedAfter").GetUInt32(), c.GetProperty("used").GetInt32(),
                c.GetProperty("cast").GetInt32());
            if (got != want)
                fails.Add($"case {n} {c.GetProperty("op").GetString()}: {got} vs {want}");
            n++;
        }
        Assert.True(n >= 300);
        Assert.True(fails.Count == 0, string.Join("\n", fails.Take(20)));
    }
}
