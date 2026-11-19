namespace GoldBox.Core.Game;

/// <summary>
/// The texts the program passes to the effect routine 0x2C540 (docs/re/spells-effects.md "Effect messages"): keys of
/// the program's text catalog (<c>GoldBox.Data.Por.PorTextCatalog</c>, GAME.OVR offsets of Pascal strings in the
/// routines' code), never the text itself. Every spell routine that calls the resolution 0x278DB passes its own text
/// (33 call sites in the unit at 0x27018); Bless / Curse (0x27F67) and Haste / Slow (0x2977C) take it from their two
/// wrappers. Hold Person (0x287DD), Animate Dead (0x291FF), the fear and paralysing auras (0x10794, 0x107CB, 0x108C7),
/// the charm gaze (0x10C20) and the stinking cloud's terrain check (0x2BACB, 0x2BB1A) call 0x2C540 themselves. The
/// damage-only routines pass an empty text (spells 4, 9, 15, 20, 28, 47, 64, 65): no message.
/// </summary>
public static class EffectTexts
{
    public const string Blessed = "EffectMsg.Blessed", Cursed = "EffectMsg.Cursed", Affected = "EffectMsg.Affected",
        Protected = "EffectMsg.Protected", ColdResistant = "EffectMsg.ColdResistant", Charmed = "EffectMsg.Charmed",
        Friendly = "EffectMsg.Friendly", Shielded = "EffectMsg.Shielded", Asleep = "EffectMsg.Asleep",
        Held = "EffectMsg.Held", FireResistant = "EffectMsg.FireResistant", Silenced = "EffectMsg.Silenced",
        SlowPoison = "EffectMsg.SlowPoison", SnakeCharm = "EffectMsg.SnakeCharm", Invisible = "EffectMsg.Invisible",
        Knock = "EffectMsg.Knock", Duplicated = "EffectMsg.Duplicated", Weakened = "EffectMsg.Weakened",
        Animated = "EffectMsg.Animated", Blind = "EffectMsg.Blind", Diseased = "EffectMsg.Diseased",
        Praying = "EffectMsg.Praying", BestowCurse = "EffectMsg.BestowCurse", Blinking = "EffectMsg.Blinking",
        Hasted = "EffectMsg.Hasted", Slowed = "EffectMsg.Slowed", Speedy = "EffectMsg.Speedy",
        Paralyzed = "EffectMsg.Paralyzed", InvisibleItem = "EffectMsg.InvisibleItem", Reading = "EffectMsg.Reading",
        FearParalysis = "EffectMsg.FearParalysis", FearWeakened = "EffectMsg.FearWeakened",
        AuraParalysis = "EffectMsg.AuraParalysis", CharmGaze = "EffectMsg.CharmGaze",
        Coughs = "EffectMsg.Coughs", Nausea = "EffectMsg.Nausea";

    /// <summary>Every key (the catalog must hold each one).</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Blessed, Cursed, Affected, Protected, ColdResistant, Charmed, Friendly, Shielded, Asleep, Held, FireResistant,
        Silenced, SlowPoison, SnakeCharm, Invisible, Knock, Duplicated, Weakened, Animated, Blind, Diseased, Praying,
        BestowCurse, Blinking, Hasted, Slowed, Speedy, Paralyzed, InvisibleItem, Reading, FearParalysis, FearWeakened,
        AuraParalysis, CharmGaze, Coughs, Nausea,
    ];

    /// <summary>
    /// The text each spell routine passes to the resolution 0x278DB (spell routine table DS:6B84 + 4·id; call site in
    /// the comment). Spells not listed pass an empty text or do not use the resolution.
    /// </summary>
    private static readonly Dictionary<int, string> BySpell = new()
    {
        [1] = Blessed,        // 0x28047 → 0x27F67 (0x28013)
        [2] = Cursed,         // 0x2807C → 0x27F67
        [5] = Affected,       // 0x28101 (0x28127), shared by 5, 11, 18, 22, 29
        [11] = Affected,
        [18] = Affected,
        [22] = Affected,
        [29] = Affected,
        [6] = Protected,      // 0x2813B (0x28161), shared by 6, 7, 16, 17, 52, 53, 54
        [7] = Protected,
        [16] = Protected,
        [17] = Protected,
        [52] = Protected,
        [53] = Protected,
        [54] = Protected,
        [8] = ColdResistant,  // 0x2817A (0x281A0)
        [10] = Charmed,       // 0x281F5 (0x2827A)
        [14] = Friendly,      // 0x283F0 (0x28449)
        [19] = Shielded,      // 0x284B1 (0x284D7)
        [21] = Asleep,        // 0x28533 (0x28661)
        [23] = Held,          // 0x28670: 0x2C540 itself (0x287DD), spells 23 and 49
        [49] = Held,
        [24] = FireResistant, // 0x28801 (0x28827)
        [25] = Silenced,      // 0x2883A (0x28860)
        [26] = SlowPoison,    // 0x28873 (0x288E6)
        [27] = SnakeCharm,    // 0x28927 (0x289D3)
        [30] = Invisible,     // 0x28A2E (0x28A54), spells 30 and 50
        [50] = Invisible,
        [31] = Knock,         // 0x28A67 (0x28A8D)
        [32] = Duplicated,    // 0x28AA2 (0x28AD1)
        [33] = Weakened,      // 0x28AE4 (0x28B0A)
        [36] = Animated,      // 0x29087: 0x2C540 itself (0x291FF)
        [38] = Blind,         // 0x29275 (0x2929B)
        [40] = Diseased,      // 0x29364 (0x2938A)
        [42] = Praying,       // 0x294EE (0x2952F)
        [44] = BestowCurse,   // 0x2961C (0x29642)
        [45] = Blinking,      // 0x29655 (0x2967B)
        [48] = Hasted,        // 0x298AA → 0x2977C (0x2984A)
        [55] = Slowed,        // 0x29C21 → 0x2977C
        [57] = Speedy,        // 0x29E1A (0x29E54)
        [61] = Paralyzed,     // 0x29FB1 (0x29FD7)
        [63] = InvisibleItem, // 0x2A042 (0x2A068)
        [67] = Reading,       // 0x2A0BA (0x2A0E0)
    };

    /// <summary>The text key a spell's routine passes to 0x2C540, or null (an empty text: no message).</summary>
    public static string? ForSpell(int spell) => BySpell.TryGetValue(spell, out var k) ? k : null;
}
