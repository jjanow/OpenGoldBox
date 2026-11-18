using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

// Spells of Pool of Radiance DOS v1.3 (docs/re/spells-effects.md "Spells"): the spell table (DS:31FA + 16·id), the
// combat cast 0x14C0F, the cast 0x27C38, the combat target pick 0x148BE, the resolution 0x278DB, duration 0x277EE,
// range 0x27741, casting level START 0x3262, the memorised-spell removal START 0x202B, and the spell routines (table
// DS:6B84 + 4·id, built at 0x2A42B). Rules cite their addresses; what is not read is [?].

/// <summary>One spell table entry (16 bytes at DS:31FA + 16·id; field names ours).</summary>
public readonly record struct SpellInfo(byte CastClass, byte Level, byte RangeFixed, byte RangeVar, byte DurFixed,
    byte DurVar, byte Area, byte Target, byte Save, byte SaveType, byte Effect, byte WhenCast, byte CastTime,
    byte AiPriority, byte Offensive, byte AiRadius)
{
    public static SpellInfo From(ReadOnlySpan<byte> b) => new(b[0], b[1], b[2], b[3], b[4], b[5], b[6], b[7], b[8], b[9],
        b[10], b[11], b[12], b[13], b[14], b[15]);
}

/// <summary>The spell table, 67 entries (1..56 spells, 57..67 item powers), read from the player's program.</summary>
public sealed class SpellTable
{
    public const int Count = 67, EntrySize = 16;
    /// <summary>DS offset of entry 0 (entry id at DS:31FA + 16·id).</summary>
    public const int DsOffset = 0x31FA;

    private readonly SpellInfo[] _e = new SpellInfo[Count + 1];

    public SpellTable(IReadOnlyDictionary<int, SpellInfo> entries)
    {
        foreach (var (k, v) in entries)
            if (k is >= 1 and <= Count)
                _e[k] = v;
    }

    /// <summary>From the data segment bytes (DS:31FA + 16 onwards: 67 × 16).</summary>
    public static SpellTable FromBytes(ReadOnlySpan<byte> entries1To67)
    {
        var d = new Dictionary<int, SpellInfo>();
        for (int i = 1; i <= Count && i * EntrySize <= entries1To67.Length; i++)
            d[i] = SpellInfo.From(entries1To67.Slice((i - 1) * EntrySize, EntrySize));
        return new SpellTable(d);
    }

    public SpellInfo this[int id] => id is >= 0 and <= Count ? _e[id] : default;
}

/// <summary>
/// The combat map's target list (0x3045B: the list DS:6718 built around a square with a radius and an icon size, its
/// slots mapped to creatures through DS:65B9). Implemented by the combat-map code. [?] Its rule is not read here.
/// </summary>
public interface IFightTargetList
{
    /// <summary>Creatures in the list built around square (x, y) with <paramref name="radius"/> (icon size 1).</summary>
    IReadOnlyList<EclCharacter> Around(int x, int y, int radius);
    /// <summary>Creatures in the list built around a creature's square with its own icon size (0x31371).</summary>
    IReadOnlyList<EclCharacter> AroundCreature(EclCharacter c, int radius);
}

/// <summary>
/// Casting in a fight (<see cref="IFightSpells"/>), reading the spell table from the player's program. Shares the
/// effect engine <see cref="EffectHandlers"/> (saves, damage, effects) and its RNG.
/// </summary>
public sealed partial class FightSpells : IFightSpells
{
    /// <summary>Creature record offsets: memorised spells 0x17..0x2B (bit 7 = not yet memorised), cleric level 0x96,
    /// magic-user level 0x9B, hit-dice byte 0x73 (Sleep).</summary>
    public const int Memorised = 0x17, MemorisedSlots = 21, ClericLevel = 0x96, MageLevel = 0x9B, HitDice = 0x73;

    private readonly Func<ushort, ushort> _random;
    private Fight? _fight;

    public FightSpells(SpellTable table, EffectHandlers effects, Func<ushort, ushort> random)
    {
        Table = table;
        Effects = effects;
        _random = random;
        Effects.CasterLevel ??= s => CasterLevel(s);
        Effects.SpellLost ??= (_, spell) => { if (Current is { } cur) RemoveSpell(cur, spell); };
    }

    public SpellTable Table { get; }
    public EffectHandlers Effects { get; }

    /// <summary>DS:6DBF: an item is being used (casting level 6, no memorised spell removed).</summary>
    public bool UsingItem { get; set; }

    /// <summary>The spell target list DS:6C94 (count) / DS:6C95 + 4·(i−1); null entries are dropped targets.</summary>
    public List<EclCharacter?> Targets { get; } = [];

    /// <summary>DS:6DB9 / DS:6DBA: the aimed square (the caster's square until an aim moves it; Lightning Bolt's
    /// rebound moves it too).</summary>
    public (int X, int Y) Aim { get; set; }

    public event Action<EclCharacter, int, EffectEvent>? Event;
    /// <summary>[Display hook] 0x27D48: the cast message (caster, spell) shown before the target pick when the spell
    /// is not cast from an item or scroll (DS:6DBF = 0).</summary>
    public Action<EclCharacter, int>? Announce { get; set; }
    /// <summary>[Display hook] 0x22B58: an item with a power (DS:6DBF = 1, not a scroll) is about to be used (user, item).</summary>
    public Action<EclCharacter, byte[]>? ItemUsed { get; set; }
    public List<string> Diagnostics { get; } = [];

    private EclCharacter? Current => Effects.CurrentCharacter();
    private FightState F => _fight?.State ?? _scratch;
    private readonly FightState _scratch = new();
    private bool InCombat => Effects.InCombat;

    private byte Dice(int n, int s) => FightRules.Dice(_random, (byte)n, (byte)s);
    private void Diag(string s)
    {
        if (!Diagnostics.Contains(s))
            Diagnostics.Add(s);
    }

    private void Bind(Fight fight)
    {
        if (!ReferenceEquals(_fight, fight))
        {
            _fight = fight;
            Effects.Attach(fight);
        }
    }

    // ---- Spell lists --------------------------------------------------------------------------------------------

    /// <summary>
    /// Spell ids the creature can cast now: with its combat record's cast allowed (+1) set, every distinct memorised id
    /// (0x17..0x2B, non-zero, bit 7 clear) the table allows in a fight (+11 ≠ 0). [?] The menu's own list (0x239D6) is
    /// not read; this is the filter the cast path applies (0x14C0F refuses +11 = 0).
    /// </summary>
    public IReadOnlyList<int> Castable(Fight fight, EclCharacter caster)
    {
        if (fight.Record(caster)[CombatRecord.CastAllowed] == 0)
            return [];
        var ids = new List<int>();
        for (int i = 0; i < MemorisedSlots; i++)
        {
            byte s = caster[Memorised + i];
            if (s == 0 || (s & 0x80) != 0 || ids.Contains(s))
                continue;
            if (Table[s].WhenCast != 0)
                ids.Add(s);
        }
        return ids;
    }

    /// <summary>START 0x202B: the first memorised slot holding <paramref name="spell"/> (exact byte) is cleared.</summary>
    public static void RemoveSpell(EclCharacter c, int spell)
    {
        for (int i = 0; i < MemorisedSlots; i++)
            if (c[Memorised + i] == (byte)spell)
            {
                c[Memorised + i] = 0;
                return;
            }
    }

    // ---- Levels, range, duration ------------------------------------------------------------------------------------

    /// <summary>
    /// START 0x3262: the current character's casting level: class 0 → cleric level 0x96, 1 → magic-user level 0x9B,
    /// 2 → 12; while an item is used (DS:6DBF) any class but 2 gives 6. [?] Another class byte leaves it unset (0 here).
    /// </summary>
    public byte CasterLevel(int spell)
    {
        var cur = Current;
        byte lvl = Table[spell].CastClass switch
        {
            0 => cur?[ClericLevel] ?? 0,
            1 => cur?[MageLevel] ?? 0,
            2 => 12,
            _ => 0,
        };
        if (UsingItem && Table[spell].CastClass != 2)
            lvl = 6;
        return lvl;
    }

    /// <summary>
    /// 0x27741: range in squares: fixed + per-level × casting level (byte), or with an item per-level × 6 + fixed; 0 with
    /// an area byte ≠ 0 → 1; 255 (an attack-roll spell) → 1.
    /// </summary>
    public byte Range(int spell)
    {
        var t = Table[spell];
        byte r = !UsingItem ? (byte)(t.RangeFixed + t.RangeVar * CasterLevel(spell)) : (byte)(t.RangeVar * 6 + t.RangeFixed);
        if (r == 0 && t.Area != 0)
            r = 1;
        if (r == 0xFF)
            r = 1;
        return r;
    }

    /// <summary>
    /// 0x277EE: duration in minutes. Spell 40: 1d6 × 10; 57 and 61: 5d4; 59: 1d4 × 10 + 40; 63: in a fight 2d10 × 10,
    /// else (1d10 + 10) × 10; 67: 1440; else fixed + per-level × casting level (16-bit).
    /// </summary>
    public ushort Duration(int spell) => spell switch
    {
        0x28 => (ushort)(Dice(1, 6) * 10),
        0x39 or 0x3D => Dice(5, 4),
        0x3B => (ushort)(Dice(1, 4) * 10 + 0x28),
        0x3F => InCombat ? (ushort)(Dice(2, 10) * 10) : (ushort)((Dice(1, 10) + 10) * 10),
        0x43 => 0x5A0,
        _ => (ushort)(Table[spell].DurFixed + Table[spell].DurVar * CasterLevel(spell)),
    };

    // ---- Seam members for the menu and the AI -------------------------------------------------------------------

    /// <summary>Table byte 11 ("when") ≠ 0: castable in a fight (0x14C0F refuses 0).</summary>
    public bool UsableInCombat(int spell) => Table[spell].WhenCast != 0;

    /// <summary>Casting delay in initiative steps: table byte 12 div 3 (0x14C0F).</summary>
    public int CastingTime(int spell) => Table[spell].CastTime / 3;

    /// <summary>Table bytes 13–15 the AI reads (0xAEAB): priority, needs targets, area.</summary>
    public (byte Priority, byte Targeted, byte Area) AiInfo(int spell) =>
        (Table[spell].AiPriority, Table[spell].Offensive, Table[spell].AiRadius);

    /// <summary>0x27741 with <paramref name="caster"/> as the current character (casting level START 0x3262).</summary>
    public int Range(Fight fight, EclCharacter caster, int spell)
    {
        Bind(fight);
        var saved = fight.State.Current;
        fight.State.Current = caster;
        try
        {
            return Range(spell);
        }
        finally
        {
            fight.State.Current = saved;
        }
    }

    // CastAuto (0x14C0F with ai = 1), the AI's aim 0x14611, the cure test 0x143F3 and the item use 0x22AC4 are in
    // FightSpells.Ai.cs.

    // ---- Casting in a fight -------------------------------------------------------------------------------------

    /// <summary>
    /// The menu's and AI's cast (0x14C0F) at a creature target or a square: a spell the table does not allow in a
    /// fight (+11 = 0) is refused. Casting time ÷ 3 = 0: cast at once (<see cref="CastNow"/>) and the actions end
    /// (START 0x31D7). Otherwise the caster begins casting: combat record +0 = spell, initiative − delay (at least 1);
    /// the cast happens when its turn comes again (<see cref="ContinueCasting"/>). Returns true when cast or begun.
    /// </summary>
    public bool Cast(Fight fight, EclCharacter caster, int spell, EclCharacter? target, (int X, int Y)? square) =>
        Begin(fight, caster, spell, () => Pick(fight, caster, spell, target is null ? [] : [target], square));

    /// <summary>
    /// [Seam addition] As <see cref="Cast"/> with every pick of a several-target spell (area modes 1–7 take up to
    /// (area AND 3) + 1 creatures, 0x148BE).
    /// </summary>
    public bool CastOn(Fight fight, EclCharacter caster, int spell, IReadOnlyList<EclCharacter> targets) =>
        Begin(fight, caster, spell, () => Pick(fight, caster, spell, targets, null));

    /// <summary>
    /// [Seam addition] The turn of a creature that began casting (combat record +0 &gt; 0): the menu (0x9B21) and the AI
    /// cast the spell now (0x27C38) and the actions end. False when there was no spell being cast.
    /// </summary>
    public bool ContinueCasting(Fight fight, EclCharacter caster, IReadOnlyList<EclCharacter> targets, (int X, int Y)? square)
    {
        Bind(fight);
        var rec = fight.Record(caster);
        int spell = rec[CombatRecord.SpellCast];
        if (spell == 0)
            return false;
        CastNow(fight, caster, spell, () => Pick(fight, caster, spell, targets, square));
        FightRules.EndActions(rec);
        return true;
    }

    private bool Begin(Fight fight, EclCharacter caster, int spell, Func<bool> pick)
    {
        Bind(fight);
        if (spell <= 0 || Table[spell].WhenCast == 0)
            return false;
        var rec = fight.Record(caster);
        int delay = Table[spell].CastTime / 3;
        if (delay == 0)
        {
            CastNow(fight, caster, spell, pick);
            FightRules.EndActions(rec);
            return true;
        }
        Event?.Invoke(caster, spell, EffectEvent.BeginsCasting);
        rec[CombatRecord.SpellCast] = (byte)spell;
        // The turn ends with the actions not over: the creature is picked again this round (an AI cast too, DOSBox
        // fight_casts q29 → q30: the charmed cleric's Bless).
        fight.Postpone(caster, rec.InitiativeValue > delay ? (sbyte)(rec.InitiativeValue - delay) : (sbyte)1);
        return true;
    }

    /// <summary>
    /// 0x27C38 in a fight, after the target pick: not picked → the spell is aborted and lost (the AI path; the menu
    /// asks first). Picked → the caster's invisibility goes (0x2BEBC), the memorised copy is removed from the current
    /// character (START 0x202B; not while an item is used), DS:681B = spell, the spell routine runs, then DS:681B and
    /// DS:6820 = 0. Returns true when the routine ran.
    /// </summary>
    public bool CastNow(Fight fight, EclCharacter caster, int spell, Func<bool> pick)
    {
        Bind(fight);
        var saved = fight.State.Current;
        fight.State.Current = caster;
        try
        {
            if (!UsingItem)
                Announce?.Invoke(caster, spell);
            if (!pick())
            {
                Event?.Invoke(caster, spell, EffectEvent.SpellAborted);
                if (!UsingItem)
                    RemoveSpell(caster, spell);
                return false;
            }
            // 0x27D9C–0x27E00 (in a fight): the caster faces the aimed square DS:6DB9/6DBA, the first direction whose
            // cone holds it (0x300CD), set through 0x31232 (+9 = d); its own square gives 0.
            if (InCombat && fight.Map is { } map && map.OnMap(caster))
            {
                var (cx, cy) = map.Position(caster);
                var (ax, ay) = Aim;
                fight.Record(caster)[CombatRecord.Facing] = (byte)map.DirectionToward(cx, cy, ax, ay);
                // 0x27E05–0x27E56: the cast's flight to the aim, resident; Fireball (47) and Lightning Bolt (51) have
                // their own cue (docs/re/sound.md §2.4).
                fight.State.Sound.BeginResident();
                fight.State.Sound.Cue(spell switch { 47 => Sound.SoundCue.Fireball, 51 => Sound.SoundCue.LightningBolt, _ => Sound.SoundCue.Cast });
                fight.State.Sound.EndResident();
            }
            Effects.RemoveInvisibility(caster);
            if (!UsingItem)
                RemoveSpell(caster, spell);
            Event?.Invoke(caster, spell, EffectEvent.SpellCast);
            Effects.Cells.Spell = (byte)spell;
            Routine(spell, caster);
            Effects.Cells.Spell = 0;
            Effects.Cells.MultiTarget = 0;
            return true;
        }
        finally
        {
            fight.State.Current = saved;
        }
    }

    // ---- Target pick (0x148BE) --------------------------------------------------------------------------------------

    /// <summary>
    /// 0x148BE with the aim (0x14611) already made by the caller: DS:6820 = 0, list cleared. Area mode = area AND 0x0F:
    /// 0 → the caster. 0x0F → the creature aimed at (combat record +0x0A of the caster); without one, the list around
    /// the aimed square with radius 0 (DS:6820 = 1). 8–0x0E → the list around the aimed square (or the creature's) with
    /// radius mode AND 7 (DS:6820 = 1). 1–7 → up to (area AND 3) + 1 picks, a creature already in the list is not
    /// taken twice. False (not picked) when the list stays empty or the aim failed.
    /// </summary>
    public bool Pick(Fight fight, EclCharacter caster, int spell, IReadOnlyList<EclCharacter> aimed, (int X, int Y)? square)
    {
        // DS:6DB9/6DBA: the caster's square (0x148E2), then the square of the last aim made (0x14874).
        Targets.Clear();
        Effects.Cells.MultiTarget = 0;
        if (fight.Map is { } fm && fm.OnMap(caster))
            Aim = fm.Position(caster);
        if (square is { } aimSq)
            Aim = aimSq;
        else if (aimed.Count > 0 && fight.Map is { } am && am.OnMap(aimed[^1]))
            Aim = am.Position(aimed[^1]);
        int mode = Table[spell].Area & 0x0F;
        if (mode == 0)
        {
            Targets.Add(caster);
            return true;
        }
        if (mode == 0x0F)
        {
            if (aimed.Count > 0)
            {
                fight.Record(caster).Other = aimed[0];
                Targets.Add(aimed[0]);
                return true;
            }
            if (square is not { } sq)
                return false;
            Targets.AddRange(Around(fight, sq, 0));
            Effects.Cells.MultiTarget = 1;
            return true;
        }
        if (mode is >= 8 and <= 0x0E)
        {
            (int X, int Y)? centre = square;
            if (centre is null && aimed.Count > 0 && fight.Map is { } map)
                centre = map.Position(aimed[0]);
            if (centre is not { } c)
                return false;
            Targets.AddRange(Around(fight, c, mode & 7));
            Effects.Cells.MultiTarget = 1;
            return true;
        }
        int picks = (Table[spell].Area & 3) + 1;
        foreach (var a in aimed)
        {
            if (picks == 0)
                break;
            if (!Targets.Contains(a))
                Targets.Add(a);
            picks--;
        }
        return Targets.Count > 0;
    }

    private IReadOnlyList<EclCharacter> Around(Fight fight, (int X, int Y) sq, int radius)
    {
        if (fight.Geometry is IFightTargetList tl)
            return tl.Around(sq.X, sq.Y, radius);
        if (fight.Map is { } map)
        {
            Diag("[?] area target list (0x3045B) not available: creatures within the map distance taken instead");
            return fight.List.Where(c => map.OnMap(c) &&
                map.Distance(sq.X, sq.Y, map.Position(c).X, map.Position(c).Y) <= radius).ToList();
        }
        Diag("[?] area spells need the combat map");
        return [];
    }

    // ---- Resolution (0x278DB) -----------------------------------------------------------------------------------

    /// <summary>
    /// 0x278DB: element mask DS:6819 = <paramref name="element"/> (0 when there is no damage); nothing without targets.
    /// Effect strength = <paramref name="strength"/> or, when 0, the casting level. For each target still in the list:
    /// a save (type +9, modifier 0) when the table's save byte (+8) is set; for a spell with fixed range 255 the target's
    /// stats are recalculated, its class 0x0B effects run and the caster's attack roll (0x2BBA1) against its AC byte
    /// 0x111 must hit, else damage is 0 (for this and every later target) and it counts as saved; damage &gt; 0 is dealt
    /// as non-weapon damage (reduction = save byte); with an effect id (+10) the effect is added (0x2C540) for the
    /// spell's duration, the save byte deciding whether a save negates. Then DS:6819 = 0.
    /// Draws per target: the save 1d20, the attack 1d20, the duration dice (spells 40, 57, 59, 61, 63).
    /// </summary>
    public void Resolve(int spell, byte strength, bool onRemove, byte damage, byte element, EclCharacter caster)
    {
        var info = Table[spell];
        F.DamageFlags = damage == 0 ? (byte)0 : element;
        if (Targets.Count == 0)
            return;
        byte level = strength == 0 ? CasterLevel(spell) : strength;
        foreach (var t in Targets.ToList())
        {
            if (t is null)
                continue;
            bool saved = info.Save != 0 && Effects.SavingThrow(t, info.SaveType, 0);
            if (Table[Effects.Cells.Spell].RangeFixed == 0xFF)
            {
                Effects.Trigger(FightRules.TriggerDefenderAc, t, F);
                if (!FightRules.AttackRoll(caster, t, t[FightRules.Ac], F, _fight?.Memory ?? new EclMemory(), _random, Effects))
                {
                    damage = 0;
                    saved = true;
                }
            }
            if (damage > 0)
                Effects.NonWeaponDamage(t, damage, info.Save, saved);
            if (info.Effect > 0)
                Effects.AddEffectWithMessage(t, info.Effect, Duration(spell), level, onRemove, info.Save, saved,
                    EffectEvent.EffectGained);
        }
        F.DamageFlags = 0;
    }

    /// <summary>0x27F67: Bless / Curse on one side: DS:6820 = 1; targets not on <paramref name="side"/> leave the list,
    /// and for Bless (spell 1) in a fight also those with an enemy beside them (START 0x2FD3 radius 1); then
    /// <see cref="Resolve"/> with nothing extra.</summary>
    private void CastOneSide(int side, EclCharacter caster)
    {
        Effects.Cells.MultiTarget = 1;
        for (int i = 0; i < Targets.Count; i++)
        {
            var t = Targets[i];
            if (t is null)
                continue;
            if (t[FightRules.Hostile] != (byte)side ||
                (Effects.Cells.Spell == 1 && InCombat && EnemiesBeside(t) > 0))
                Targets[i] = null;
        }
        Resolve(Effects.Cells.Spell, 0, false, 0, 0, caster);
    }

    /// <summary>START 0x2FD3 with radius 1: creatures of the other side (START 0x2F57) in the creature's target list.</summary>
    private int EnemiesBeside(EclCharacter c)
    {
        byte other = c[FightRules.Hostile] == 0 ? (byte)1 : (byte)0;
        if (_fight?.Geometry is IFightTargetList tl)
            return tl.AroundCreature(c, 1).Count(x => x[FightRules.Hostile] == other);
        Diag("[?] Bless's enemy-beside test (START 0x2FD3) needs the combat map's target list");
        return 0;
    }

    private EclCharacter? First => Targets.Count > 0 ? Targets[0] : null;

    // ---- Spell routines (DS:6B84 + 4·id) ----------------------------------------------------------------------------

    /// <summary>Spell ids with a routine modelled here.</summary>
    public static readonly int[] Modelled =
    [
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31,
        32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60,
        61, 62, 63, 64, 65, 66, 67,
    ];

    /// <summary>The routine of a spell; the targets are in <see cref="Targets"/>, DS:681B = spell.</summary>
    public void Routine(int spell, EclCharacter caster)
    {
        switch (spell)
        {
            case 1: // 0x28027 Bless: the caster's side
                CastOneSide(caster[FightRules.Hostile], caster);
                return;
            case 2: // 0x28058 Curse: the other side (START 0x2F57)
                CastOneSide(caster[FightRules.Hostile] == 0 ? 1 : 0, caster);
                return;
            case 3: // 0x28083 Cure Light Wounds: first target healed 1d8 (0x2C647, no bypass); message
                if (Targets.Count > 0 && First is { } h && Effects.RestoreHitPoints(h, Dice(1, 8), false))
                    Event?.Invoke(h, spell, EffectEvent.Healed);
                return;
            case 4: // 0x280BF Cause Light Wounds: 1d8 (count to DS:681C), element 8
                F.DamageDice = 1;
                Resolve(spell, 0, false, Dice(1, 8), 8, caster);
                return;
            case 5 or 11 or 18 or 22 or 29: // 0x28101: plain resolution
            case 6 or 7 or 16 or 17 or 52 or 53 or 54: // 0x2813B
            case 8: // 0x2817A
            case 19: // 0x284B1
            case 24 or 25: // 0x28801, 0x2883A
            case 30 or 50: // 0x28A2E
            case 31: // 0x28A67
            case 33: // 0x28AE4
                Resolve(spell, 0, false, 0, 0, caster);
                return;
            case 9: // 0x281A8 Burning Hands: damage = casting level, element 9
                Resolve(spell, 0, false, CasterLevel(spell), 9, caster);
                return;
            case 10: // 0x281F5 Charm Person
                CharmPerson(spell, caster);
                return;
            case 12: // 0x282BD Enlarge
                Enlarge(spell);
                return;
            case 13: // 0x28386 Reduce: first target fails a save vs. 4 and loses an Enlarge (0x2BF6B) → message
                if (Targets.Count > 0 && First is { } r && !Effects.SavingThrow(r, 4, 0) && RemoveWithMessage(r, 0x0C))
                    Event?.Invoke(r, spell, EffectEvent.Other);
                return;
            case 14: // 0x283F0 Friends: first target's Charisma + 2d4 (at most 25); effect strength = the old value
            {
                if (First is not { } t14)
                    return;
                byte old = t14[EffectHandlers.Cha];
                t14[EffectHandlers.Cha] = (byte)(t14[EffectHandlers.Cha] + Dice(2, 4));
                if (t14[EffectHandlers.Cha] > 0x19)
                    t14[EffectHandlers.Cha] = 0x19;
                Resolve(spell, old, true, 0, 0, caster);
                return;
            }
            case 15: // 0x28451 Magic Missile: (level ÷ 2) d4 (count to DS:681C) + level ÷ 2, element 8
            {
                byte lvl = CasterLevel(spell);
                F.DamageDice = (byte)(lvl >> 1);
                byte d = (byte)(Dice(lvl >> 1, 4) + (lvl >> 1));
                Resolve(spell, 0, false, d, 8, caster);
                return;
            }
            case 20: // 0x284DF Shocking Grasp: casting level + 1d8 (count to DS:681C), element 0x0C (attack roll: range 255)
            {
                byte lvl = CasterLevel(spell);
                F.DamageDice = 1;
                byte d = (byte)(lvl + Dice(1, 8));
                Resolve(spell, 0, false, d, 0x0C, caster);
                return;
            }
            case 21: // 0x28533 Sleep
                Sleep(spell, caster);
                return;
            case 23 or 49: // 0x2866F Hold Person
                HoldPerson(spell);
                return;
            case 26: // 0x28873 Slow Poison
                SlowPoison(spell, caster);
                return;
            case 27: // 0x28927 Snake Charm
                SnakeCharm(spell, caster);
                return;
            case 28: // 0x289DB Spiritual Hammer: resolution with removal on, then handler 0x17 on the first target
                Resolve(spell, 0, true, 0, 0, caster);
                if (First is { } t28)
                    Effects.CallHandler(0x17, t28, null, false);
                return;
            case 32: // 0x28AA2 Mirror Image: strength 1d4 (images)
                Resolve(spell, Dice(1, 4), false, 0, 0, caster);
                return;
        }
        if (MoreRoutine(spell, caster))
            return;
        Diag($"[?] spell routine {spell} not modelled");
    }

    /// <summary>0x2BF6B: the first record of the effect leaves (with a message) when the creature has one.</summary>
    private bool RemoveWithMessage(EclCharacter c, byte id)
    {
        if (EffectHandlers.FindEffect(c, id) is not { } n)
            return false;
        Event?.Invoke(c, 0, EffectEvent.Other);
        Effects.RemoveEffect(c, id, n);
        return true;
    }

    /// <summary>
    /// 0x281F5: a first target of type (0x9F) above 1 or size (0x6C) above 1 is unaffected (message). Else resolution
    /// with strength = casting level + the caster's hostile byte (signed) × 128 (byte), removal on; then, when the
    /// target has a charm (0x0B), its handler runs on it (not removed).
    /// </summary>
    private void CharmPerson(int spell, EclCharacter caster)
    {
        if (First is not { } t)
            return;
        if (t[EffectHandlers.CreatureType] > 1 || t[EffectHandlers.Size] > 1)
        {
            Event?.Invoke(t, spell, EffectEvent.Unaffected);
            return;
        }
        byte strength = (byte)(CasterLevel(spell) + ((sbyte)caster[FightRules.Hostile] << 7));
        Resolve(spell, strength, true, 0, 0, caster);
        if (EffectHandlers.FindEffect(t, 0x0B) is { } n)
            Effects.CallHandler(0x0B, t, n, false);
    }

    /// <summary>
    /// 0x282BD: Strength 18 with the exceptional part (DS:4845) by casting level (1 → 0, 2 → 1, 3 → 51, 4 → 76, 5 → 91,
    /// 6 → 100; other levels keep what DS:4845 holds: the last Enlarge's value, or the last effect level Dispel Magic
    /// looked at); 0x2C046 raises the first target (message when it rose); effect 0x0C is added for the duration with
    /// the code 0x2C046 returned, removal on.
    /// </summary>
    private void Enlarge(int spell)
    {
        byte lvl = CasterLevel(spell);
        Ds4845 = lvl switch { 1 => 0, 2 => 1, 3 => 0x33, 4 => 0x4C, 5 => 0x5B, 6 => 0x64, _ => Ds4845 };
        if (First is not { } t)
            return;
        if (Effects.RaiseStrength(t, 0x12, Ds4845, out byte code))
            Event?.Invoke(t, spell, EffectEvent.Other);
        EffectHandlers.AddEffect(t, 0x0C, Duration(spell), code, true);
    }

    /// <summary>
    /// 0x28533: DS:6820 = 1; budget = 4d4. Each target costs by its hit-dice byte 0x73 (signed): ≤ 1 → 1, 2 → 2,
    /// 3 → 4, 4 → 6, 5 → 10 for race byte 0 else 20, more → 20 ([?] negative values cost 20 too). A target that is not
    /// animated (status 1), not already asleep (0x35) and fits in what is left is paid for; the others leave the list.
    /// Then the resolution. Draws: 4d4 first.
    /// </summary>
    private void Sleep(int spell, EclCharacter caster)
    {
        Effects.Cells.MultiTarget = 1;
        int budget = Dice(4, 4);
        for (int i = 0; i < Targets.Count; i++)
        {
            var t = Targets[i];
            if (t is null)
                continue;
            sbyte hd = (sbyte)t[HitDice];
            int cost = hd switch
            {
                >= 0 and <= 1 => 1,
                2 => 2,
                3 => 4,
                4 => 6,
                5 => t[EffectHandlers.Race] == 0 ? 10 : 20,
                _ => 20,
            };
            if (t[EffectHandlers.Status] != 1 && EffectHandlers.FindEffect(t, 0x35) is null && budget >= cost)
                budget -= cost;
            else
                Targets[i] = null;
        }
        Resolve(spell, 0, false, 0, 0, caster);
    }

    /// <summary>
    /// 0x2866F (spells 23 and 49): save modifier by the number of targets: 1 → −2 for spell 23, else −3; 2 → −1; 3 or 4
    /// → 0 ([?] other counts leave it unset). From the last target to the first: a save (type +9); a target of type
    /// above 1 or size above 1 counts as saved; the table effect is added (0x2C540) for the duration with the casting
    /// level, no removal call, the save byte deciding. Draws per target: the save.
    /// </summary>
    private void HoldPerson(int spell)
    {
        int n = Targets.Count;
        sbyte mod = n switch { 1 => spell == 0x17 ? (sbyte)-2 : (sbyte)-3, 2 => -1, 3 or 4 => 0, _ => 0 };
        var info = Table[spell];
        for (int i = n - 1; i >= 0; i--)
        {
            if (Targets[i] is not { } t)
                continue;
            if (i < n - 1)
            {
                // 0x286F5–0x2874E: a further target gets the cast's flight from the caster, resident (sound.md §2.4).
                F.Sound.BeginResident();
                F.Sound.Cue(Sound.SoundCue.Cast);
                F.Sound.EndResident();
            }
            bool saved = Effects.SavingThrow(t, info.SaveType, mod);
            if (t[EffectHandlers.CreatureType] > 1 || t[EffectHandlers.Size] > 1)
                saved = true;
            Effects.AddEffectWithMessage(t, info.Effect, Duration(spell), CasterLevel(spell), false, info.Save, saved,
                EffectEvent.EffectGained);
        }
    }

    /// <summary>
    /// 0x28873: an animated first target leaves the list and nothing happens. A poisoned one (0x37): HP 0 → 1;
    /// resolution with strength 255, removal on; handler 0x4E (removed, no record); effect 0x0F for 10 minutes, param
    /// 255, removal on.
    /// </summary>
    private void SlowPoison(int spell, EclCharacter caster)
    {
        if (First is not { } t)
            return;
        if (t[EffectHandlers.Status] == 1)
        {
            Targets[0] = null;
            return;
        }
        if (EffectHandlers.FindEffect(t, 0x37) is null)
            return;
        if (t[FightRules.Hp] == 0)
            t[FightRules.Hp] = 1;
        Resolve(spell, 0xFF, true, 0, 0, caster);
        Effects.CallHandler(0x4E, t, null, true);
        EffectHandlers.AddEffect(t, 0x0F, 10, 0xFF, true);
    }

    /// <summary>
    /// 0x28927: budget = the caster's HP; the list is rebuilt from the whole creature list: every snake (type 0x0E)
    /// whose HP fit in what is left is taken and paid for; then the resolution.
    /// </summary>
    private void SnakeCharm(int spell, EclCharacter caster)
    {
        int budget = caster[FightRules.Hp];
        Targets.Clear();
        foreach (var p in Effects.List())
        {
            if (p[EffectHandlers.CreatureType] == 0x0E && budget >= p[FightRules.Hp])
            {
                budget = (byte)(budget - p[FightRules.Hp]);
                Targets.Add(p);
            }
        }
        Resolve(spell, 0, false, 0, 0, caster);
    }
}
