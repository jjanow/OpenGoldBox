using GoldBox.Core.Ecl;

namespace GoldBox.Core.Game;

// The effect engine of Pool of Radiance DOS v1.3 (docs/re/spells-effects.md): the trigger dispatcher GAME.OVR 0x2B1E4
// and the per-effect trigger 0x2B04A, the removal 0x2AF10, the add routines 0x2BD3C / 0x2C540, the saving throw
// 0x2BC4D, non-weapon damage 0x2C22E, healing 0x2C647, the terrain check 0x2B9D1 and the effect handlers (table
// DS:6828, built at 0x11DFD; handlers in the overlay unit at 0xEC5B). Every rule cites its address; what is not read
// is [?]. Messages are events (EffectEvent), never text.

/// <summary>
/// The combat map's aura test of 0x2B04A for the four aura effects (Silence 15', the two Protection 10' and Prayer):
/// the program builds a target list around the carrier (path code 0x3045B with the carrier's square from 0x31321 /
/// 0x31349, range 6 for Prayer, else 1, the carrier's icon size 0x31371) and looks for the creature's combat slot
/// (0x31399) in it. Implemented by the combat-map code. [?] The target-list rule itself is not read here.
/// </summary>
public interface IFightAuraRange
{
    bool InAuraRange(EclCharacter carrier, EclCharacter creature, int range);
}

/// <summary>The terrain under a creature on the combat map (0x313F3 with direction 8, the terrain byte).</summary>
public interface IFightTerrain
{
    /// <summary>Terrain index of the creature's own square; 0x1E is a stinking cloud.</summary>
    int TerrainUnder(EclCharacter c);
}

/// <summary>Something an effect handler shows in the text window (the program prints a message there).</summary>
public enum EffectEvent
{
    StartsTraining, Silenced, LostImage, Coughing, Ages, Weakens, ParalysisFailed,
    GainsItem, Unaffected, EffectGained, CastingLost, NonWeaponDamage, MissileDodged, Hardier,
    FearsDragon, Charmed, Healed, SpellCast, SpellAborted, BeginsCasting, Other,
    /// <summary>Non-weapon damage put the creature out of play (0x2C44A: the "goes down" / dying / killed message).</summary>
    GoesDown,
    /// <summary>
    /// The end of 0x2C44A after <see cref="GoesDown"/> (0x2C4E5–0x2C527): outside a fight PauseOnly; in a fight, after
    /// 0x2BEF4 and trigger 0x0D, CreatureDowned 0x3154B (the downed picture and its pause) when the creature is still out
    /// of play, else PauseOnly; then START 0x17E3 clears the message area. Raised after the map removal.
    /// </summary>
    GoesDownDone,
    /// <summary>
    /// The end of 0x2C22E for a target still in play after its non-weapon damage (0x2C44D → 0x2C527): START 0x17E3 clears
    /// the message area (in a fight rows 10–21). DOSBox-X s29: the bolt's damage message gone before the next flight.
    /// </summary>
    DamageDone,
    /// <summary>
    /// Handlers 15 (0xF246) and 44 (0xFDC3) after their 1-point damage: outside a fight (DS:49F3 ≠ 5) START 0x0767 draws
    /// the party list (0xF28A / 0xF299, 0xFE0A / 0xFE19). No text.
    /// </summary>
    HandlerDamageListed,
    // 0xECB3's callers (docs/re/spells-effects.md "Status changes"): each passes its own text (a Pascal string in the
    // handler unit 0xEC5B), shown by START 0x16EC from row 10 without the pause flag before the dead / stone / gone test.
    /// <summary>Handler 22 (0xF406, text CS:0764): poisoned when slow poison ends.</summary>
    PoisonDeath,
    /// <summary>Handler 32 (0xF7A4, text CS:0B11): an animated dead creature's effect goes.</summary>
    Collapses,
    /// <summary>Handler 57 (0x100A4, text CS:138A): the last stage of mummy rot.</summary>
    RotsAway,
    /// <summary>The poisoned hit 0x101A0 (0x10230, text CS:153B), after <see cref="Poisoned"/>.</summary>
    PoisonKilled,
    /// <summary>Level drain 0x10D7D, status 8 (0x10E15 animated, 0x10F35 hit dice below 2; text CS:20FB).</summary>
    DeadForever,
    /// <summary>Level drain 0x10D7D, status 6 at 0 HP (0x10FAB, text CS:2118).</summary>
    DrainKilled,
    /// <summary>Handler 95 (0x1129D, text CS:2605).</summary>
    FallsDead,
    /// <summary>
    /// 0xECB3's end (0xED2E–0xED5D), only when the status was set: CreatureDowned 0x3154B (picture and pause in a fight)
    /// when the creature is still out of play, then PauseOnly, START 0x17E3 (message area cleared) and, outside a fight,
    /// START 0x0767 (the party list). Raised after the map removal.
    /// </summary>
    StatusDone,
    // The messages those handlers show before 0xECB3 (START 0x16EC from row 10).
    /// <summary>The poisoned hit 0x101A0 (text CS:152F at 0x101F2): no pause flag, then PauseOnly (0x101F7), no clear.</summary>
    Poisoned,
    /// <summary>Handler 57 (text CS:1382 at 0x1001F): with the pause flag.</summary>
    Rots,
    /// <summary>
    /// Level drain 0x10D7D (0x10DEC, with the pause flag): CS:20EF + (count &gt; 1: Str(count) (START 0x1037) + CS:20DF;
    /// else CS:20E7); the count is <see cref="EffectHandlers.DrainCount"/>.
    /// </summary>
    Drained,
    // The monster specials' own messages (class 0x0E handlers; each START 0x16EC(creature, its text, row, pause flag)
    // with the text a Pascal string in the handler's code segment; docs/re/spells-effects.md "Handler messages").
    /// <summary>Handler 83 (0x10930): the gazer's gaze, row 10, no pause.</summary>
    Gazes,
    /// <summary>Handler 83 (0x10A0A): the target's mirror turns the gaze back, row 12, no pause.</summary>
    GazeReflected,
    /// <summary>Handler 83 (0x10AAA → 0xECB3): the victim turns to stone, row 10, no pause.</summary>
    Stoned,
    /// <summary>Handler 84 (0x10B6F): the charm gaze, row 10, no pause.</summary>
    CharmGazes,
    /// <summary>Handler 88 (0x2A137): the lightning breath, row 10, with the pause.</summary>
    Breathes,
    /// <summary>Handler 121 (0x118D9): the acid breath, row 10, with the pause.</summary>
    SpitsAcid,
    // s30: the revive and put-out-of-play routines and the handler texts that were raised as Other (docs/re/
    // spells-effects.md "Status changes" and "Handler messages").
    /// <summary>
    /// 0x2C753 (callers: handlers 78, 99, 102, Animate Dead 0x291CE), raised once the creature is back in play: in a fight
    /// 0x319D2(creature, 3, 0) first, then START 0x16EC(creature, text, row 10, pause 1); the text by the hostile byte
    /// 0x10E (1: CS:1848 of unit 0x2AEEA, else CS:185C); then the team count (START 0x2F7B).
    /// </summary>
    Revived,
    /// <summary>
    /// Handler 103's own 0x319D2(creature, 3, 0) (0x11534: the view on the creature, no cursor box), every time it runs,
    /// before 0x2BDF0. On its trigger (class 0x0D, the creature already out of play) 0x2BDF0 then does nothing, so this
    /// view is all that shows.
    /// </summary>
    EscapeView,
    /// <summary>
    /// Handler 103 (0x1151A → 0x2BDF0, status 3, text CS:28A5) for a creature still in play: 0x319D2 and START
    /// 0x16EC(row 10, pause 1).
    /// </summary>
    Escapes,
    /// <summary>Handler 87 (0x110E0, text CS:23E0): the last target, START 0x16EC from row 10 with the pause.</summary>
    Diseased,
    /// <summary>Handler 73 (0x16240, text CS:39C9 of unit 0x127FB): the raker, row 10, with the pause.</summary>
    Rakes,
    /// <summary>Handler 77 (0x10629, text CS:1928): the held target, row 10, with the pause.</summary>
    HeldFast,
    /// <summary>Handler 75 (0x10485, text CS:175E): the drinker, row 10, with the pause.</summary>
    SucksBlood,
    /// <summary>
    /// Handler 131 (0x11CB1 / 0x11CD8, text CS:2F4B): the wearer, row 10, with the pause. In a fight the handler sets
    /// DS:49F3 = 2 around the call, so START 0x16EC draws the text-window layout (not the panel).
    /// </summary>
    Stronger,
    /// <summary>Handler 40 (0xF9A4, text CS:0CA8): START 0x1693, row 24 in green with the pause; no creature.</summary>
    AirClears,
    /// <summary>Handler 135 (0x11DCA, text CS:311B): START 0x1693, row 24 in green with the pause.</summary>
    NeedsGiantStrength,
    // Spell routines' own messages (docs/re/spells-effects.md "Spell routine messages").
    SpellStrengthened, SpellReduced, SpellEffectRemoved, SpellCloudFormed, SpellBlindnessCured,
    SpellMagicDispelled, SpellCurseLifted, SpellItemUncursed, SpellLevelRestored,
}

/// <summary>The data-segment cells the effect handlers read and write besides those in <see cref="FightState"/>.</summary>
public sealed class EffectCells
{
    /// <summary>DS:6816: the saving-throw roll being made (0x2BC4D).</summary>
    public byte SaveRoll { get; set; }
    /// <summary>DS:6817: the effect being added (0x2C540); an immunity handler (class 9) clears it.</summary>
    public byte PendingEffect { get; set; }
    // DS:6825 (morale being checked, class 17) is FightState.Morale: the morale check 0xBC97 reads what the handlers
    // (Bless, Curse, Charm) wrote there.
    /// <summary>DS:681B: the spell being cast (0 none).</summary>
    public byte Spell { get; set; }
    /// <summary>DS:6820: the spell has several targets.</summary>
    public byte MultiTarget { get; set; }
    /// <summary>DS:681F: the effect lock (handlers do not re-add effects while set).</summary>
    public byte Lock { get; set; }
    /// <summary>DS:682A: the saving-throw type being rolled.</summary>
    public byte SaveType { get; set; }
    /// <summary>DS:6826: the creature a handler looks at (usually the one in combat record +0x0A).</summary>
    public EclCharacter? Other { get; set; }
}

/// <summary>Monster names some handlers compare against (data of the player's program; <c>PorEffectTables</c>).</summary>
public sealed class EffectNameTables
{
    /// <summary>DS:0332 + 16·i, i = 1..4: names a gnome gets +1 to hit against (handler 18).</summary>
    public IReadOnlyList<byte[]> GnomeFoes { get; init; } = [];
    /// <summary>DS:0372 + 16·i, i = 1..8 (the program reads 8 slots): names a dwarf gets +1 to hit against (26).</summary>
    public IReadOnlyList<byte[]> DwarfFoes { get; init; } = [];
    /// <summary>DS:03F2 + 16·i, i = 1..8: names that get −4 to hit a dwarf (47).</summary>
    public IReadOnlyList<byte[]> GiantFoes { get; init; } = [];
    /// <summary>The two names in the code segment that get −4 to hit a gnome (48).</summary>
    public IReadOnlyList<byte[]> GnomeDefenseFoes { get; init; } = [];
}

/// <summary>
/// The program's effect engine, as <see cref="IFightEffects"/> for the fight and as the class-12 / class-16 hook of the
/// ECL DAMAGE opcode (<see cref="EclTrigger"/>). Dice come from <c>random</c> (Turbo Pascal Random(n), 0..n−1) through the
/// dice routine 0x2BCCD in the program's order.
/// </summary>
public sealed partial class EffectHandlers : IFightEffects
{
    /// <summary>Effect ids each trigger class runs, in order (0x2B1E4, the compare chain on the class byte).</summary>
    public static readonly IReadOnlyDictionary<int, byte[]> Classes = new Dictionary<int, byte[]>
    {
        [0] = [0x7E],
        [1] = [0x3F, 0x25, 0x19, 0x47],
        [2] = [0x55, 0x56, 0x57, 0x44, 0x4F, 0x50, 0x4C, 0x49, 0x4D],
        [3] = [0x40, 0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x4F, 0x55, 0x56, 0x57],
        [4] = [0x1D, 0x03, 0x06],
        [5] = [0x1C, 0x29, 0x68, 0x78, 0x65, 0x73, 0x74, 0x77, 0x7B, 0x60, 0x5E, 0x3C, 0x7A, 0x75],
        [6] = [0x71, 0x3D, 0x7A, 0x3C, 0x5B, 0x0A, 0x14, 0x69, 0x6A, 0x70, 0x72, 0x76, 0x11, 0x5D, 0x65, 0x1C],
        [7] = [0x33, 0x34, 0x35, 0x1F],
        [8] = [0x63, 0x51, 0x52, 0x59, 0x48, 0x38],
        [9] = [0x69, 0x6A, 0x6B, 0x6C, 0x6D, 0x6E, 0x6F, 0x70, 0x7C, 0x7D],
        [10] = [0x01, 0x02, 0x21, 0x24, 0x31, 0x03, 0x06, 0x12, 0x1A],
        [11] = [0x21, 0x11, 0x08, 0x09, 0x2D, 0x2E, 0x1E],
        [12] = [0x08, 0x09, 0x0A, 0x11, 0x14, 0x21, 0x24, 0x2D, 0x2E, 0x31, 0x3D, 0x6F, 0x7D, 0x5A, 0x61],
        [13] = [0x63, 0x64, 0x67, 0x4B, 0x4A],
        [14] = [0x53, 0x54, 0x58, 0x79],
        [15] = [0x15, 0x1E, 0x4A, 0x4B, 0x0B],
        [16] = [0x19, 0x47, 0x25, 0x2F, 0x30, 0x59],
        [17] = [0x01, 0x02, 0x0B],
        [18] = [0x27, 0x2A, 0x3A],
        [19] = [0x62, 0x17, 0x48, 0x38, 0x0B],
    };

    /// <summary>The aura effects 0x2B04A also finds on other creatures (set constant at unit 0xB0 CS:0x140).</summary>
    public static readonly byte[] AuraEffects = [0x15, 0x2D, 0x2E, 0x31];

    /// <summary>Statuses healing works on (set constant at unit 0xB0 CS:0x173D, 0x2C647): okay, animated, unconscious, dying.</summary>
    public static readonly byte[] HealableStatus = [0, 1, 4, 5];

    /// <summary>Creature record offsets the handlers use (docs/re/character-format.md; names ours).</summary>
    public const int Str = 0x10, Con = 0x14, Cha = 0x15, StrExc = 0x16, Race = 0x2E, Age = 0x30, HpMax = 0x32,
        AttackLevel = 0x6B, Size = 0x6C, Saves = 0x6D, MoveBase = 0x72, UndeadLevel = 0x76, Npc = 0x84,
        ClassLevels = 0x96, FighterLevel = 0x98, CreatureType = 0x9F, Alignment = 0xA0, HpRolled = 0xB1,
        SaveBonus = 0x101, Status = 0x10C, InPlay = 0x10D, Hostile = 0x10E, Auto = 0x10F, Hp = 0x11B;

    /// <summary>The NPC byte the charm and animate-dead handlers set (0xB3) and clear.</summary>
    public const byte AnimatedMember = 0xB3;

    private readonly Func<ushort, ushort> _random;
    private readonly Dictionary<EclCharacter, byte[]> _baseline = new(ReferenceEqualityComparer.Instance);
    private FightState _fight = new();

    public EffectHandlers(Func<ushort, ushort> random, EffectNameTables? names = null)
    {
        _random = random;
        Names = names ?? new EffectNameTables();
    }

    public EffectNameTables Names { get; }
    public EffectCells Cells { get; } = new();

    /// <summary>DS:49F3 = 5: a fight is running (auras need the map, damage changes the team counts).</summary>
    public bool InCombat { get; set; }
    /// <summary>The creature list (DS:5D96).</summary>
    public Func<IReadOnlyList<EclCharacter>> List { get; set; } = () => [];
    /// <summary>The current character (DS:5D92). In a fight: <see cref="FightState.Current"/> when set.</summary>
    public Func<EclCharacter?> CurrentCharacter { get; set; } = () => null;
    public IFightGeometry? Geometry { get; set; }
    public BaseItemTable? Items { get; set; }
    public IFightAuraRange? AuraRange { get; set; }
    public IFightTerrain? TerrainMap { get; set; }
    /// <summary>Called when a creature leaves the combat map (0x3154B); the map's business.</summary>
    public Action<EclCharacter>? LeaveMap { get; set; }

    /// <summary>
    /// The stat recalculation (START 0x1758) where the program runs it around effects: before trigger 0x0B (the attack
    /// routine 0x13C01 and the spell resolution recalculate the defender first) and inside handlers 23 and 30. [?] It is
    /// not modelled: the default restores the THAC0 and AC bytes (0x110–0x112) the creature had when the handlers first
    /// saw it, so handlers that lower them (Blind, coughing) do not pile up across attacks.
    /// </summary>
    public Action<EclCharacter>? Recalculate { get; set; }

    public event Action<EclCharacter, EffectEvent>? Event;
    public List<string> Diagnostics { get; } = [];

    /// <summary>Hooks this engine to a fight: list, current creature, geometry, items, map seams, combat flag.</summary>
    public void Attach(Fight fight)
    {
        _fight = fight.State;
        _attached = fight;
        InCombat = true;
        List = () => fight.List;
        CurrentCharacter = () => fight.State.Current;
        Geometry = fight.Geometry;
        Items = fight.Items;
        AuraRange ??= fight.Geometry as IFightAuraRange;
        TerrainMap ??= fight.Geometry as IFightTerrain;
    }

    private EclCharacter? Current => CurrentCharacter();
    private FightState F => _fight;

    private byte Dice(int count, int sides) => FightRules.Dice(_random, (byte)count, (byte)sides);

    private void Emit(EclCharacter c, EffectEvent e) => Event?.Invoke(c, e);

    private void Diag(string s)
    {
        if (!Diagnostics.Contains(s))
            Diagnostics.Add(s);
    }

    // ---- IFightEffects ----------------------------------------------------------------------------------------------

    /// <summary>0x2B1E4: runs every effect id of the class in order on the creature (each through 0x2B04A).</summary>
    public void Trigger(int trigger, EclCharacter c, FightState fight)
    {
        _fight = fight;
        if (trigger == FightRules.TriggerDefenderAc)
            RecalculateStats(c);
        if (!Classes.TryGetValue(trigger, out var ids))
            return;
        foreach (var id in ids)
            TriggerEffect(c, id);
    }

    /// <summary>
    /// After <see cref="FightRules.RemoveFirstEffect"/> unlinked an effect: the program (0x2AF10) runs its handler with
    /// "removed" only when the record's byte 4 is set, and before unlinking it ([?] order differs here).
    /// </summary>
    public void Removed(EclCharacter c, byte[] effect)
    {
        if (effect.Length > 4 && effect[4] != 0)
            CallHandler(effect[0], c, effect, true);
    }

    /// <summary>0x2B9D1, run for every creature at the end of a round after its trigger-0x13 effects.</summary>
    public void Terrain(EclCharacter c, FightState fight)
    {
        _fight = fight;
        TerrainCheck(c);
    }

    // ---- The trigger of one effect (0x2B04A) -------------------------------------------------------------------------

    /// <summary>
    /// 0x2B04A: when the creature has the effect (first instance, START 0x2D39) its handler runs with that record. Else,
    /// for the aura effects (<see cref="AuraEffects"/>), the list is walked from the head while nothing is found: a
    /// carrier counts outside a fight; in a fight only when the creature is in the carrier's range (6 for Prayer 0x31,
    /// else 1). The handler then runs on the creature with the carrier's record.
    /// </summary>
    public void TriggerEffect(EclCharacter c, byte id)
    {
        var node = FindEffect(c, id);
        bool found = node is not null;
        if (!found && AuraEffects.Contains(id))
        {
            foreach (var p in List())
            {
                if (found)
                    break;
                var pn = FindEffect(p, id);
                if (pn is null)
                    continue;
                node = pn;
                if (!InCombat)
                    found = true;
                else if (AuraRange is { } ar)
                    found = ar.InAuraRange(p, c, id == 0x31 ? 6 : 1);
                else
                    Diag($"[?] aura effect {id} in a fight needs the combat map's range test");
            }
        }
        if (found)
            CallHandler(id, c, node!, false);
    }

    /// <summary>START 0x2D39: the first effect record with this id, or null.</summary>
    public static byte[]? FindEffect(EclCharacter c, byte id) => c.Effects.FirstOrDefault(e => e.Length > 0 && e[0] == id);

    // ---- Effect list (0x2BD3C, 0x2AF10, 0x2C540, 0x2BEF4, 0x2BEBC) ------------------------------------------------

    /// <summary>0x2BD3C: a new 9-byte record at the end of the list: id, minutes, param, "removal runs the handler".</summary>
    public static byte[] AddEffect(EclCharacter c, byte id, ushort minutes, byte param, bool onRemove)
    {
        var e = new byte[EclCharacter.EffectRecordSize];
        e[0] = id;
        e[1] = (byte)minutes;
        e[2] = (byte)(minutes >> 8);
        e[3] = param;
        e[4] = onRemove ? (byte)1 : (byte)0;
        c.Effects.Add(e);
        return e;
    }

    /// <summary>
    /// 0x2AF10: the record given, or the first with this id; nothing when none. When its byte 4 is set the handler of
    /// <paramref name="id"/> runs with "removed" first; then it leaves the list.
    /// </summary>
    public void RemoveEffect(EclCharacter c, byte id, byte[]? node = null)
    {
        node ??= FindEffect(c, id);
        if (node is null)
            return;
        if (node[4] != 0)
            CallHandler(id, c, node, true);
        int i = c.Effects.FindIndex(e => ReferenceEquals(e, node));
        if (i >= 0)
            c.Effects.RemoveAt(i);
    }

    /// <summary>0x2BEF4: the first instance of each effect in DS:0C14 leaves a creature that drops.</summary>
    public void RemoveEffectsHp0(EclCharacter c)
    {
        foreach (var id in FightRules.DroppedEffects)
            RemoveEffect(c, id);
    }

    /// <summary>0x2BEBC: every invisibility (0x19) record leaves the creature, one after the other.</summary>
    public void RemoveInvisibility(EclCharacter c)
    {
        while (FindEffect(c, FightRules.InvisibilityEffect) is { } n)
            RemoveEffect(c, FightRules.InvisibilityEffect, n);
    }

    /// <summary>
    /// 0x2C540: DS:6817 = id and the creature's immunity effects (class 9) run. Unaffected (a message, nothing added)
    /// when they cleared DS:6817, or when it saved and the save negates (<paramref name="saveNegates"/> = 1). Else an
    /// existing record with this id leaves first when it has minutes left and the new duration is longer, or the new
    /// one is permanent (0); then the new record is added. Last, when the caller's text (<paramref name="text"/>: its
    /// key in the program's text catalog, <see cref="EffectTexts"/>) is not empty, the message shows: START 0x2B8B
    /// (creature, effect frames, text) then START 0x2383 (rows 10–21 cleared); <see cref="EffectText"/> holds the key
    /// during that event (<paramref name="message"/>, or <see cref="EffectEvent.EffectGained"/>). An empty text (null)
    /// shows nothing (0x2C603 tests its length); <paramref name="message"/> is then still raised for listeners.
    /// Returns true when added.
    /// </summary>
    public bool AddEffectWithMessage(EclCharacter c, byte id, ushort minutes, byte param, bool onRemove, byte saveNegates,
        bool saved, EffectEvent? message = null, string? text = null)
    {
        Cells.PendingEffect = id;
        Trigger(9, c, F);
        if (Cells.PendingEffect == 0 || (saved && saveNegates == 1))
        {
            Emit(c, EffectEvent.Unaffected);
            return false;
        }
        if (FindEffect(c, id) is { } old)
        {
            ushort left = (ushort)(old[1] | (old[2] << 8));
            if ((left > 0 && minutes > left) || minutes == 0)
                RemoveEffect(c, id, old);
        }
        AddEffect(c, id, minutes, param, onRemove);
        if (text is not null)
        {
            EffectText = text;
            try
            {
                Emit(c, message ?? EffectEvent.EffectGained);
            }
            finally
            {
                EffectText = null;
            }
        }
        else if (message is { } m)
            Emit(c, m);
        return true;
    }

    /// <summary>
    /// The text key of the effect message being shown (0x2C540's text argument, <see cref="EffectTexts"/>) while its
    /// event runs; null otherwise.
    /// </summary>
    public string? EffectText { get; private set; }

    /// <summary>0xEC7A: adds the effect with "removal runs the handler" unless the effect lock DS:681F is set.</summary>
    private bool AddEffectIfNoLock(EclCharacter c, byte id, byte param, ushort minutes)
    {
        if (Cells.Lock != 0)
            return false;
        AddEffect(c, id, minutes, param, true);
        return true;
    }

    /// <summary>0xEC5B: with 0 or the effect being added (DS:6817), damage DS:6818 and DS:6817 become 0.</summary>
    private void Nullify(byte id)
    {
        if (id == 0 || Cells.PendingEffect == id)
        {
            F.PendingDamage = 0;
            Cells.PendingEffect = 0;
        }
    }

    // ---- Rolls ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// The saving throw 0x2BC4D: 1d20 into DS:6816; 1 fails, 20 saves; else DS:6816 = roll + 0x101 + modifier (byte
    /// sum, both signed), DS:682A = type, the class-12 effects run, and it saves when the save byte 0x6D + type ≤ DS:6816
    /// (unsigned).
    /// </summary>
    public bool SavingThrow(EclCharacter c, int type, int modifier)
    {
        Cells.SaveRoll = Dice(1, 20);
        if (Cells.SaveRoll == 1)
            return false;
        if (Cells.SaveRoll == 20)
            return true;
        Cells.SaveRoll = (byte)(Cells.SaveRoll + (sbyte)c[SaveBonus] + (sbyte)(byte)modifier);
        Cells.SaveType = (byte)type;
        Trigger(12, c, F);
        return c[Saves + (sbyte)(byte)type] <= Cells.SaveRoll;
    }

    // ---- Damage, status, healing ------------------------------------------------------------------------------------

    /// <summary>
    /// Non-weapon damage 0x2C22E (spells, poison, items): DS:6818 = damage, class-6 effects; when saved, reduction 1
    /// cancels it and 2 halves it; nothing more at 0. Else (message) START 0x2DC8 applies it; in a fight the target's
    /// cast allowed (+1) = 0 and a spell it was casting is lost (+0; START 0x202B [?]). A target now out of play, in a
    /// fight: its DS:0C14 effects go (0x2BEF4), class 0x0D runs, and still out of play it leaves the map (0x3154B).
    /// </summary>
    public void NonWeaponDamage(EclCharacter c, byte damage, byte saveReduction, bool saved)
    {
        F.PendingDamage = damage;
        Trigger(6, c, F);
        if (saved)
        {
            if (saveReduction == 1)
                F.PendingDamage = 0;
            else if (saveReduction == 2)
                F.PendingDamage = (byte)(F.PendingDamage >> 1);
        }
        if (F.PendingDamage == 0)
            return;
        Emit(c, EffectEvent.NonWeaponDamage);
        FightRules.InflictDamage(c, F.PendingDamage, InCombat ? F : null);
        if (InCombat)
        {
            var rec = F.Record(c);
            rec[CombatRecord.CastAllowed] = 0;
            if (rec[CombatRecord.SpellCast] > 0)
            {
                Emit(c, EffectEvent.CastingLost);
                SpellLost?.Invoke(c, rec[CombatRecord.SpellCast]);
                rec[CombatRecord.SpellCast] = 0;
            }
        }
        if (c[InPlay] != 0)
        {
            Emit(c, EffectEvent.DamageDone);
            return;
        }
        Emit(c, EffectEvent.GoesDown); // 0x2C458–0x2C4E0: the message, in and out of a fight
        if (InCombat)
        {
            RemoveEffectsHp0(c);
            Trigger(FightRules.TriggerHpZero, c, F);
            if (c[InPlay] == 0)
                LeaveMap?.Invoke(c);
        }
        Emit(c, EffectEvent.GoesDownDone); // 0x2C4E5–0x2C527: CreatureDowned's or the pause, then START 0x17E3
    }

    /// <summary>START 0x202B: a spell being cast is lost with its memorised copy [?] (the spell code removes it).</summary>
    public Action<EclCharacter, byte>? SpellLost { get; set; }

    /// <summary>
    /// 0xECB3: <paramref name="message"/> (the caller's text, START 0x16EC from row 10 without the pause flag, shown
    /// first whatever the status); a creature already dead, stone or gone (6, 7, 8) is left, the text staying in the
    /// panel. Else it takes <paramref name="status"/>, out of play with 0 HP, loses its DS:0C14 effects, runs class 0x0D
    /// and, still out of play, leaves the map (0x3154B, the program calls it in and out of a fight); then
    /// <see cref="EffectEvent.StatusDone"/> (PauseOnly, message area cleared). The team counts are not touched here (the
    /// next count does it).
    /// </summary>
    public void InflictStatus(EclCharacter c, byte status, EffectEvent message)
    {
        Emit(c, message);
        if (c[Status] is 6 or 7 or 8)
            return;
        c[Status] = status;
        c[InPlay] = 0;
        c[Hp] = 0;
        RemoveEffectsHp0(c);
        Trigger(FightRules.TriggerHpZero, c, F);
        if (c[InPlay] == 0)
            LeaveMap?.Invoke(c);
        Emit(c, EffectEvent.StatusDone);
    }

    /// <summary>The count the level drain 0x10D7D was last called with (its message shows it; handlers 85 → 1, 86 → 2).</summary>
    public int DrainCount { get; private set; }

    /// <summary>
    /// 0x2C647: healing. Nothing for a status outside <see cref="HealableStatus"/>. Below full HP with
    /// <paramref name="bypass"/>: healed. Otherwise a creature with rot (0x32) is not healed, nor one at full HP with
    /// <paramref name="bypass"/>. Healing adds to HP (byte), at most the maximum 0x32; then a creature out of play that is
    /// dying (5) becomes unconscious (4), and an unconscious one outside a fight runs handler 0x4E (removed set, no
    /// record). True when healed.
    /// </summary>
    public bool RestoreHitPoints(EclCharacter c, byte hp, bool bypass)
    {
        if (!HealableStatus.Contains(c[Status]))
            return false;
        if (!(c[Hp] < c[HpMax] && bypass))
        {
            if (FindEffect(c, 0x32) is not null)
                return false;
            if (bypass)
                return false;
        }
        c[Hp] = (byte)(c[Hp] + hp);
        if (c[Hp] > c[HpMax])
            c[Hp] = c[HpMax];
        if (c[InPlay] == 0)
        {
            if (c[Status] == 5)
                c[Status] = 4;
            if (c[Status] == 4 && !InCombat)
                CallHandler(0x4E, c, null, true);
        }
        return true;
    }

    // ---- Terrain (0x2B9D1) ------------------------------------------------------------------------------------------

    /// <summary>
    /// 0x2B9D1: a creature in play standing in a stinking cloud (terrain 0x1E) that has none of the effects 0x1E, 0x1F,
    /// 0x20, 0x6F, 0x7D saves vs. type 0 (no modifier). Saved: coughing (0x1E) for 1 minute; failed: helpless (0x1F) for
    /// 1d4 + 1 minutes (the dice are rolled after the save). Both are added with 0x2C540 (param 255, no removal call,
    /// save does not negate) and their handler then runs.
    /// </summary>
    public void TerrainCheck(EclCharacter c)
    {
        if (c[InPlay] == 0)
            return;
        if (TerrainMap is null)
        {
            if (InCombat)
                Diag("[?] terrain check needs the combat map (IFightTerrain)");
            return;
        }
        if (TerrainMap.TerrainUnder(c) != 0x1E)
            return;
        foreach (byte id in new byte[] { 0x1E, 0x1F, 0x20, 0x6F, 0x7D })
            if (FindEffect(c, id) is not null)
                return;
        bool saved = SavingThrow(c, 0, 0);
        if (saved)
        {
            AddEffectWithMessage(c, 0x1E, 1, 0xFF, false, 0, saved, EffectEvent.Coughing, EffectTexts.Coughs); // text 0x2BACB
            CallHandler(0x1E, c, FindEffect(c, 0x1E), false);
        }
        else
        {
            ushort minutes = (ushort)(Dice(1, 4) + 1);
            AddEffectWithMessage(c, 0x1F, minutes, 0xFF, false, 0, saved, EffectEvent.EffectGained, EffectTexts.Nausea); // text 0x2BB1A
            CallHandler(0x1F, c, FindEffect(c, 0x1F), false);
        }
    }

    // ---- The stat recalculation stand-in ---------------------------------------------------------------------------

    private void RecalculateStats(EclCharacter c)
    {
        if (Recalculate is { } r)
        {
            r(c);
            return;
        }
        if (_baseline.TryGetValue(c, out var b))
        {
            c[0x110] = b[0];
            c[0x111] = b[1];
            c[0x112] = b[2];
        }
        else
            _baseline[c] = [c[0x110], c[0x111], c[0x112]];
    }

    /// <summary>Forgets the stand-in baseline of a creature (after its equipment or level changed).</summary>
    public void ResetBaseline(EclCharacter c) => _baseline.Remove(c);

    // ---- DAMAGE (ECL 0x2E) ------------------------------------------------------------------------------------------

    /// <summary>
    /// The hook of <see cref="EclPartyRules.EffectTrigger"/>: class 12 puts <paramref name="roll"/> into the save cell
    /// DS:6816 (with DS:682A = <paramref name="saveType"/>), class 16 into the attack roll DS:6822; the class runs on the
    /// creature outside a fight (auras from any carrier); the cell is returned.
    /// </summary>
    public int EclTrigger(EclCharacter m, int cls, int roll, int saveType = 0)
    {
        var saved = (_fight, InCombat);
        _fight = new FightState();
        InCombat = false;
        try
        {
            if (cls == 12)
            {
                Cells.SaveRoll = (byte)roll;
                Cells.SaveType = (byte)saveType;
                Trigger(12, m, _fight);
                return Cells.SaveRoll;
            }
            _fight.AttackRoll = (sbyte)(byte)roll;
            Trigger(cls, m, _fight);
            return _fight.AttackRoll;
        }
        finally
        {
            (_fight, InCombat) = saved;
        }
    }

    /// <summary>Wires <paramref name="rules"/>' DAMAGE hook to this engine (list and current character from its party).</summary>
    public void AttachTo(EclPartyRules rules)
    {
        List = () => rules.Memory.Party?.Members ?? [];
        CurrentCharacter = () => rules.Memory.Party?.Current;
        rules.EffectTrigger = (m, cls, roll) => EclTrigger(m, cls, roll, rules.LastSaveType);
    }
}
