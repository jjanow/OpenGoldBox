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
/// Data-segment state of a running fight (GAME.OVR 0x9881 and the units it calls): the combat records, the round
/// counter, the team counts and the scratch cells the attack code passes values through. Headless.
/// </summary>
public sealed class FightState
{
    private readonly Dictionary<EclCharacter, CombatRecord> _records = new(ReferenceEqualityComparer.Instance);

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
    /// <summary>The creature acting now (DS:5D92 during the fight).</summary>
    public EclCharacter? Current { get; set; }
}
