using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Core.Tests;

/// <summary>
/// The <c>temple</c> capture (fixtures README, s12): a patched save A (slot C) whose vector 0 gives the first character
/// 150 cp, 37 sp, 9 ep, 3 gp, 600 pp (3010 gold), DAMAGE for 10 (HP 11 → 1) and opens the temple. Then Cure
/// Blindness on a character who is not blind (cast anyway, paid), Cure Light / Serious / Critical Wounds, and Raise
/// Dead on a living character without the money. Each state is <c>dosbox_state.py fight</c> output (the party record
/// and RandSeed are what is used here).
/// </summary>
public class TempleCaptureTests
{
    private static string Dir => Path.Combine(Captures.Dir!, "temple");

    private static JsonElement Load(string name) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir, name, "fight.json"))).RootElement;

    private static uint Seed(JsonElement s) => s.GetProperty("vars").GetProperty("randseed").GetUInt32();

    private static EclCharacter First(JsonElement s)
    {
        var p = s.GetProperty("creatures")[0];
        var c = new EclCharacter(Convert.FromHexString(p.GetProperty("record").GetString()!));
        c.Items.AddRange(p.GetProperty("items").EnumerateArray().Select(i => Convert.FromHexString(i.GetProperty("record").GetString()!)));
        c.Effects.AddRange(p.GetProperty("effects").EnumerateArray().Select(e => Convert.FromHexString(e.GetProperty("record").GetString()!)));
        return c;
    }

    private static uint[] Pool(JsonElement s) => s.GetProperty("pool_67f4").EnumerateArray().Select(v => v.GetUInt32()).ToArray();

    private static ushort[] Coins(EclCharacter c) => Enumerable.Range(0, 7).Select(k => c.Word(Money.Coins + 2 * k)).ToArray();

    /// <summary>One heal-list entry from the state before the payment question to the state after the answer.</summary>
    private static void Entry(string before, string after, TempleRules.Cure cure, Money.Payer payer)
    {
        var s0 = Load(before);
        var s1 = Load(after);
        var c = First(s0);
        var pool = Pool(s0);
        var rng = new TurboPascalRandom(Seed(s0));
        Assert.Equal(payer, Money.Pay(c, pool, TempleRules.Price(cure), wideCharacterTotal: false));
        if (payer != Money.Payer.None)
            TempleRules.Apply(cure, c, n => rng.Next((ushort)n));
        var game = First(s1);
        Assert.Equal(Coins(game), Coins(c));
        Assert.Equal(game[0x11B], c[0x11B]);
        Assert.Equal(game[0x10C], c[0x10C]);
        Assert.Equal(Pool(s1), pool);
        Assert.Equal(Seed(s1), rng.Seed); // cures 2–4 draw 1–3 times, the others never
        // The payment rewrites the coins but not the weight word (+0x102); it stays as it was.
        Assert.Equal(First(s0).Word(Money.Weight), game.Word(Money.Weight));
    }

    [CaptureFact]
    public void Cure_blindness_on_a_sighted_character_still_costs_1000() =>
        Entry("t05_blind_price", "t06_blind_paid", TempleRules.Cure.Blindness, Money.Payer.Character);

    [CaptureFact]
    public void Cure_light_wounds_heals_1d8() =>
        Entry("t08_clw_price", "t09_clw_paid", TempleRules.Cure.CureLight, Money.Payer.Character);

    [CaptureFact]
    public void Cure_serious_wounds_heals_2d8_plus_1_capped_at_max() =>
        Entry("t11_csw_price", "t12_csw_paid", TempleRules.Cure.CureSerious, Money.Payer.Character);

    [CaptureFact]
    public void Cure_critical_wounds_at_full_hp_still_pays_and_draws() =>
        Entry("t14_ccw_price", "t15_ccw_paid", TempleRules.Cure.CureCritical, Money.Payer.Character);

    [CaptureFact]
    public void Raise_dead_without_the_money_pays_nothing() =>
        Entry("t18_raise_price", "t19_raise_no_money", TempleRules.Cure.RaiseDead, Money.Payer.None);

    [CaptureFact]
    public void Conditions_and_the_weight_word_after_view()
    {
        var list = First(Load("t03_heal_list"));
        Assert.False(TempleRules.Applies(TempleRules.Cure.Blindness, list)); // the "cure anyway" question showed
        Assert.False(TempleRules.Applies(TempleRules.Cure.RaiseDead, list));
        Assert.True(TempleRules.Applies(TempleRules.Cure.CureLight, list));
        // The weight word stayed stale through every payment (538: items + the 38 coins of save A, which the
        // patch's SAVEs replaced) and was recomputed when View opened: 500 + 192 platinum.
        var paid = First(Load("t19_raise_no_money"));
        var view = First(Load("t20_view"));
        Assert.Equal(538, paid.Word(Money.Weight));
        Assert.Equal(Coins(paid), Coins(view));
        Assert.Equal(paid.Word(Money.Weight) - 38 + 192, view.Word(Money.Weight));
    }
}
