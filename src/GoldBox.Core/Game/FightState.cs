using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

/// <summary>
/// The 22-byte combat record each creature gets at the fight start (far pointer at creature record +0x108, allocated
/// and zeroed by GAME.OVR 0xD8B2; docs/re/combat-format.md "Combat record"). Field names are ours; the offsets are
/// those the fight code reads and writes.
/// </summary>
public sealed class CombatRecord
{
    public const int Size = 22;
    /// <summary>Offsets: spell being cast (+0), cast allowed (+1), item use allowed (+2), initiative (+3, signed),
    /// attack phase (+4), sweep budget (+5), movement in half squares (+6), guarding (+7), attacked this round (+8),
    /// facing (+9), creature attacked last (+0x0A, far pointer), rounds dying (+0x0E), attacks from other creatures this
    /// turn (+0x0F), sum of their angles mod 8 (+0x12), not a party member (+0x13).</summary>
    public const int SpellCast = 0, CastAllowed = 1, ItemUseAllowed = 2, Initiative = 3, AttackPhase = 4, SweepBudget = 5,
        Movement = 6, Guarding = 7, MadeAttack = 8, Facing = 9, Dying = 0x0E, FlankCount = 0x0F, FlankSpread = 0x12,
        NotParty = 0x13;
    /// <summary>+0x10: turned by turn undead (0x13A84; the AI reads it). +0x11: has tried turning undead this fight
    /// (0x139A5; the menu's Turn entry needs 0). docs/re/combat-menu.md.</summary>
    public const int Turned = 0x10, TriedTurning = 0x11;

    public byte[] Bytes { get; } = new byte[Size];

    public byte this[int offset]
    {
        get => Bytes[offset];
        set => Bytes[offset] = value;
    }

    public sbyte InitiativeValue
    {
        get => (sbyte)Bytes[Initiative];
        set => Bytes[Initiative] = (byte)value;
    }

    /// <summary>+0x0A: the creature this one attacked last (MakeAttack 0x14118 writes the defender's pointer).</summary>
    public EclCharacter? Other { get; set; }
}

/// <summary>
/// The arguments of the attack message routine 0x12AF4 (read s19): <paramref name="Manner"/> 1 from behind, 2 a
/// backstab, 3 a slain helpless defender (0x13CA6), else 0 (0x13E30–0x13E52); <paramref name="Damage"/> the damage
/// shown (DS:6818 on a hit, 1 for the helpless defender, 0 on the miss); <paramref name="Hit"/>. Plus what the routine
/// saw before its damage: the defender in play (its name's colour on row 12) and a spell it was casting (+0 &gt; 0,
/// lost on damage).
/// </summary>
public readonly record struct AttackMessage(EclCharacter Attacker, EclCharacter Defender, int Manner, byte Damage, bool Hit,
    bool DefenderWasInPlay, bool SpellLost);

/// <summary>
/// Data-segment state of a running fight (GAME.OVR 0x9881 and the units it calls): the combat records, the round
/// counter, the team counts and the scratch cells the attack code passes values through. Headless.
/// </summary>
public sealed class FightState
{
    private readonly Dictionary<EclCharacter, CombatRecord> _records = new(ReferenceEqualityComparer.Instance);

    /// <summary>The program's sound calls in a fight (docs/re/sound.md §2.4); none by default.</summary>
    public Sound.ISoundSink Sound { get; set; } = GoldBox.Core.Sound.NoSound.Instance;

    /// <summary>The combat record of a creature (created on first use, zeroed, as 0xD8B2 does at the fight start).</summary>
    public CombatRecord Record(EclCharacter c)
    {
        if (!_records.TryGetValue(c, out var r))
            _records[c] = r = new CombatRecord();
        return r;
    }

    /// <summary>DS:6DE3: rounds ended in this fight (END of round 0xA087 adds 1). Odd rounds round half attacks up
    /// (<see cref="FightRules.AttacksFromRate"/>). [?] Its value at the fight start is not read; taken as 0.</summary>
    public byte RoundCounter { get; set; }

    /// <summary>DS:6814 / DS:6815: creatures in play on the party side (hostile 0) and the enemy side (hostile 1)
    /// (START 0x2F7B); the damage routine lowers them when a creature drops.</summary>
    public byte[] TeamCount { get; } = new byte[2];

    /// <summary>DS:681A: the rate (attacks or movement, in halves) the trigger-0x12 effects may change.</summary>
    public byte RateX2 { get; set; }
    /// <summary>DS:681D: 1 while the movement is being computed (0x1294B), 0 for attack rates.</summary>
    public bool CalculatingMovement { get; set; }
    /// <summary>DS:6818: damage of the current hit (byte).</summary>
    public byte PendingDamage { get; set; }
    /// <summary>DS:6819: cleared before the damage effects run (0x12A08). [?] what the effects put there.</summary>
    public byte DamageFlags { get; set; }
    /// <summary>DS:681C: dice count of the last damage roll (0x2BD18).</summary>
    public byte DamageDice { get; set; }
    /// <summary>DS:6822: the last attack roll, signed byte; a natural 20 is stored as 100.</summary>
    public sbyte AttackRoll { get; set; }
    /// <summary>DS:6823/6824: hits made in phase 1 / 2 by the current attack (index 1, 2).</summary>
    public byte[] HitsMade { get; } = new byte[3];
    /// <summary>DS:6E2D/6E2E: attacks made in phase 1 / 2 by the current attack (index 1, 2).</summary>
    public byte[] AttacksMade { get; } = new byte[3];
    /// <summary>DS:6E2F: the enemy side's hit points left, in 5 % steps (0x15074).</summary>
    public byte HostileHpPercent { get; set; }
    /// <summary>DS:6E30: quick (computer-controlled) party members may cast spells. The menu's Alt-M toggles it, the
    /// fight start (0xE457) clears it, the AI reads it (0xB187).</summary>
    public bool QuickMagic { get; set; }
    /// <summary>DS:681E: set by effect handlers during the target check 0x13880 (triggers 1 and 0) when the creature
    /// can't be targeted; cleared by the check before it runs them.</summary>
    public byte TargetBlocked { get; set; }
    /// <summary>The creature acting now (DS:5D92 during the fight).</summary>
    public EclCharacter? Current { get; set; }

    /// <summary>
    /// Display hook (no game state): each call of the attack message routine 0x12AF4 from the attack routine 0x13C01
    /// (a hit, a slain helpless defender, or the miss after no hit), called after its damage part
    /// (<see cref="FightRules.ApplyAttackResult"/>). A front end shows the program's message here.
    /// </summary>
    public Action<AttackMessage>? AttackMessage { get; set; }

    /// <summary>
    /// Display hook (no game state): the moving icon START 0x25C9 (a missile's, a spell's or a bolt's flight, a gaze or
    /// breath, the aim cursor's Next / Prev), called where the program calls it: the missiles and casts inside their
    /// resident sound bracket, the effect handlers and the aim with none. A front end animates it there.
    /// </summary>
    public Action<FlightAnimation>? Flight { get; set; }

    // Cells the monster AI (GAME.OVR 0xABCF, docs/re/combat-ai.md) passes to the effect handlers and the map.
    /// <summary>DS:6825: the morale value the trigger-0x11 effects see (morale check 0xBC97).</summary>
    public byte Morale { get; set; }
    /// <summary>Arena (DS:6716) byte +6: lines ignore obstacles (read by 0x2FF74); set for the second pass of the
    /// target search 0x15FDF and cleared at its end.</summary>
    public bool IgnoreObstacles { get; set; }
}
