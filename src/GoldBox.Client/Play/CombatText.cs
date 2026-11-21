using GoldBox.Core.Game;
using GoldBox.Data.Por;

namespace GoldBox.Client.Play;

/// <summary>The fight's texts the combat screen shows (where each is used: the routine in the comment).</summary>
public enum CombatMsg
{
    // Main menu (0x9E6D): the entries' words.
    MenuMove, MenuViewAim, MenuUse, MenuCast, MenuTurn, MenuQuickDone,
    // Alt-M, Alt-X (0x9B21); end of round (0xA087).
    MagicOn, MagicOff, DoesNotWork, TeammateDying, ContinueBattle, Guarding,
    // Alt-X's message (0x1638E).
    GodsIntervene,
    // Move (0xA1E6), attack by moving (0xA54C).
    MoveLeft, Flee, Blocked, NotWithThatWeapon,
    // Done menu (0xA64C), bandage (0xA806), speed menu (0xA8FD).
    DoneGuard, DoneDelayQuit, DoneBandage, DoneSpeedExit, IsBandaged, GameSpeed, Slower, Faster, SpeedExit,
    // Attack messages (0x12A2D–0x12AEA, the message routine before 0x12AF4).
    Backstabs, SlaysHelpless, Attacks, FromBehind, HittingFor, Point, Points, OfDamage, AndMisses, LostSpell, GoesDown,
    AndIsDying, IsKilled,
    // Flee (0x13465), sweep (0x1367D), turn undead (0x13965), cast (0x14C0F).
    GotAway, EscapeBlocked, Sweeps, TurnsUndead, IsTurned, IsDestroyed, NothingHappens, CampOnlySpell, BeginsCasting,
    // Attack-ally check (0x15193), aim (0x1529B, 0x155F5).
    AttackAlly, Range, Target, AimNextPrevManual, AimCenterExit, AimPrompt, UseCursorKeys,
    // Spell cast message (0x27C38; the fight's cast message 0x2A332), the yes/no menu (0x2D979).
    Casts, CastsASpell, SpellLabel, YesNo,
    // Spell aim: a creature already picked (0x14B79), the abort question (0x27EF2).
    AlreadyTargeted, AbortSpell, SpellAborted,
    // The status panel (START 0x1583; its strings are in START.EXE, offsets in the unpacked image).
    PanelHitpoints, PanelAc, PanelHelpless,
    // The View menu (0x21AED): entries and prompt.
    ViewItems, ViewSpells, ViewTrade, ViewDrop, ViewExit, ViewPrompt,
    // The Items screen (0x21F32): entries, title, column heading, messages; let go (0x21DAA).
    ItemsReady, ItemsUse, ItemsTrade, ItemsDrop, ItemsHalve, ItemsJoin, ItemsSell, ItemsId, ItemsTitle, ItemsHeading,
    MustBeReadied, Your, GoneForever, DropIt, MustBeUnreadied, ScribeWarning, OkayToLose,
    // Ready (0x2250A), Halve (0x22827), item use (0x22AC4), drop money (0x234E1).
    Cursed, WrongClass, AlreadyUsing, HandsFull, CannotHalve, UsesAnItem, ItemLabel, SelectCoin, HowMuch, WillYouDrop,
    // The AI's messages (0xABC0–0xBC8C).
    FleesInPanic, AiMagicOn, AiMagicOff, ForcedToFlee, Surrenders,
    // The slain helpless defender's words (0x12AF4); the AI's move prompt (0xB396, AiMoveStep).
    CruelBlow, AiMoveLeft,
    // Non-weapon damage (0x2C22E): the damage, the element, a lost spell, the down words; an effect resisted (0x2C540).
    Takes, PointsOfDamage, TakesOnePoint, FromFire, FromCold, FromElectricity, FromAcid, FromMagic, SpellLostSpell,
    SpellGoesDown, SpellAndIsDying, SpellIsKilled, Unaffected,
    // The monster specials' messages (class 0x0E handlers): the gaze 83 (0x10908) with its reflection and the stoning
    // (0xECB3's text), the charm gaze 84 (0x10AD2), the lightning breath 88 (0x2A0F1), the acid breath 121 (0x11877).
    HandlerGazes, HandlerGazeReflected, HandlerStoned, HandlerCharmGazes, HandlerBreathes, HandlerSpitsAcid,
    // The fight start's row-24 line (0xE402: the unit's CS:0x1E1E, GAME.OVR 0xE3EF).
    BattleBegins,
    // 0xECB3's other callers' texts: handler 22 (0xF406), 32 (0xF7A4), 57 (0x100A4), the poisoned hit (0x10230), the
    // level drain (status 8 at 0x10E15 / 0x10F35, status 6 at 0x10FAB), 95 (0x1129D).
    StatusPoisonDeath, StatusCollapses, StatusRotsAway, StatusPoisonKilled, StatusDeadForever, StatusDrainKilled,
    StatusFallsDead,
    // The messages before them: the poisoned hit (0x101F2), mummy rot 57 (0x1001F), the level drain (0x10DEC: the
    // drained words + "a level" or the count + the levels word).
    Poisoned, Rots, Drained, DrainedOneLevel, DrainedLevels,
    // s30: the revive 0x2C753 (by the hostile byte: enemy, party side), handler 103's put-out-of-play (0x2BDF0), and the
    // handler texts 87 (0x110E0), 73 (0x16240), 77 (0x10629), 75 (0x10485), 131 (0x11CB1), 40 (0xF9A4), 135 (0x11DCA).
    StatusRevivedEnemy, StatusRevived, StatusEscapes, HandlerDiseased, HandlerRakes, HandlerHeldFast, HandlerSucksBlood,
    HandlerStronger, HandlerAirClears, HandlerNeedsGiantStrength,
    SpellReduced, SpellEffectRemoved, SpellCloudFormed, SpellBlindnessCured, SpellMagicDispelled,
    SpellCurseLifted, SpellItemUncursed, SpellLevelRestored,
    // START 0x2EEA's healed words (HP 0x11B = 0x32 or not): handlers 50, 62 and Cure Light Wounds (s39).
    FullyHealed, PartiallyHealed,
}

/// <summary>
/// The program's own fight texts, read from the player's GAME.OVR (PoR DOS v1.3: Pascal strings in the overlay units'
/// code segments, at the file offsets below; nothing of the text is in our code). For another GAME.OVR, or an offset
/// that does not hold a string, a short wording of ours is used instead (same scheme as <see cref="GameText"/>).
/// </summary>
public sealed class CombatText
{
    private static readonly Dictionary<CombatMsg, string> Table = new()
    {
        [CombatMsg.MenuMove] = "MOVE ",
        [CombatMsg.MenuViewAim] = "VIEW AIM ",
        [CombatMsg.MenuUse] = "USE ",
        [CombatMsg.MenuCast] = "CAST ",
        [CombatMsg.MenuTurn] = "TURN ",
        [CombatMsg.MenuQuickDone] = "QUICK DONE",
        [CombatMsg.MagicOn] = "AUTO MAGIC ON",
        [CombatMsg.MagicOff] = "AUTO MAGIC OFF",
        [CombatMsg.DoesNotWork] = "NOT ALLOWED",
        [CombatMsg.GodsIntervene] = "ALT-X: HOSTILES REMOVED",
        [CombatMsg.TeammateDying] = "A PARTY MEMBER IS DYING",
        [CombatMsg.ContinueBattle] = "FIGHT ON:",
        [CombatMsg.Guarding] = "ON GUARD",
        [CombatMsg.MoveLeft] = "MOVE, MOVES LEFT: ",
        [CombatMsg.Flee] = "RUN AWAY:",
        [CombatMsg.Blocked] = "CAN'T GO THERE",
        [CombatMsg.NotWithThatWeapon] = "NOT WITH THIS WEAPON",
        [CombatMsg.DoneGuard] = "GUARD ",
        [CombatMsg.DoneDelayQuit] = "DELAY QUIT ",
        [CombatMsg.DoneBandage] = "BANDAGE ",
        [CombatMsg.DoneSpeedExit] = "SPEED EXIT",
        [CombatMsg.IsBandaged] = "IS BANDAGED",
        [CombatMsg.GameSpeed] = "SPEED (",
        [CombatMsg.Slower] = "SLOWER ",
        [CombatMsg.Faster] = "FASTER ",
        [CombatMsg.SpeedExit] = "EXIT",
        [CombatMsg.Backstabs] = "-BACKSTAB-",
        [CombatMsg.SlaysHelpless] = "KILLS THE HELPLESS",
        [CombatMsg.Attacks] = "ATTACKS",
        [CombatMsg.FromBehind] = "(BEHIND) ",
        [CombatMsg.HittingFor] = "HITS FOR ",
        [CombatMsg.Point] = " HP ",
        [CombatMsg.Points] = " HP ",
        [CombatMsg.OfDamage] = "DAMAGE",
        [CombatMsg.AndMisses] = "MISSES",
        [CombatMsg.LostSpell] = "SPELL LOST",
        [CombatMsg.GoesDown] = "FALLS",
        [CombatMsg.AndIsDying] = "FALLS, DYING",
        [CombatMsg.IsKilled] = "IS DEAD",
        [CombatMsg.GotAway] = "ESCAPED",
        [CombatMsg.EscapeBlocked] = "CAN'T ESCAPE",
        [CombatMsg.Sweeps] = "SWEEPS",
        [CombatMsg.TurnsUndead] = "TURNS UNDEAD",
        [CombatMsg.IsTurned] = "IS TURNED",
        [CombatMsg.IsDestroyed] = "IS DESTROYED",
        [CombatMsg.NothingHappens] = "NO EFFECT",
        [CombatMsg.CampOnlySpell] = "NOT IN A FIGHT",
        [CombatMsg.BeginsCasting] = "STARTS A SPELL",
        [CombatMsg.AttackAlly] = "HIT A FRIEND: ",
        [CombatMsg.Range] = "DISTANCE ",
        [CombatMsg.Target] = "TARGET ",
        [CombatMsg.AimNextPrevManual] = "NEXT PREV MANUAL ",
        [CombatMsg.AimCenterExit] = "CENTER EXIT",
        [CombatMsg.AimPrompt] = "AIMING:",
        [CombatMsg.UseCursorKeys] = "(MOVE THE CURSOR) ",
        [CombatMsg.Casts] = "CASTS",
        [CombatMsg.CastsASpell] = "CASTS A SPELL",
        [CombatMsg.SpellLabel] = "SPELL:",
        [CombatMsg.YesNo] = "YES NO",
        [CombatMsg.AlreadyTargeted] = "PICKED ALREADY",
        [CombatMsg.AbortSpell] = "GIVE UP THE SPELL? ",
        [CombatMsg.SpellAborted] = "SPELL GIVEN UP",
        [CombatMsg.ViewItems] = "ITEMS ",
        [CombatMsg.ViewSpells] = "SPELLS ",
        [CombatMsg.ViewTrade] = "TRADE ",
        [CombatMsg.ViewDrop] = "DROP ",
        [CombatMsg.ViewExit] = "EXIT",
        [CombatMsg.ViewPrompt] = "VIEW:",
        [CombatMsg.ItemsReady] = "READY",
        [CombatMsg.ItemsUse] = " USE",
        [CombatMsg.ItemsTrade] = " TRADE",
        [CombatMsg.ItemsDrop] = " DROP",
        [CombatMsg.ItemsHalve] = " HALVE",
        [CombatMsg.ItemsJoin] = " JOIN",
        [CombatMsg.ItemsSell] = " SELL",
        [CombatMsg.ItemsId] = " ID",
        [CombatMsg.ItemsTitle] = "ITEMS",
        [CombatMsg.ItemsHeading] = "READY ITEM",
        [CombatMsg.MustBeReadied] = "READY IT FIRST",
        [CombatMsg.Your] = "THE ",
        [CombatMsg.GoneForever] = "WILL BE LOST",
        [CombatMsg.DropIt] = "DROP: ",
        [CombatMsg.MustBeUnreadied] = "UNREADY IT FIRST",
        [CombatMsg.ScribeWarning] = " WANTED TO COPY THIS SCROLL",
        [CombatMsg.OkayToLose] = "LOSE IT? ",
        [CombatMsg.Cursed] = "CURSED",
        [CombatMsg.WrongClass] = "NOT FOR THIS CLASS",
        [CombatMsg.AlreadyUsing] = "IN USE: ",
        [CombatMsg.HandsFull] = "NO FREE HAND",
        [CombatMsg.CannotHalve] = "CAN'T SPLIT",
        [CombatMsg.UsesAnItem] = "USES AN ITEM",
        [CombatMsg.ItemLabel] = "ITEM:",
        [CombatMsg.SelectCoin] = "WHICH COINS",
        [CombatMsg.HowMuch] = "HOW MANY ",
        [CombatMsg.WillYouDrop] = "TO DROP? ",
        [CombatMsg.FleesInPanic] = "RUNS IN FEAR",
        [CombatMsg.AiMagicOn] = "MAGIC ON",
        [CombatMsg.AiMagicOff] = "MAGIC OFF",
        [CombatMsg.ForcedToFlee] = "MUST RUN",
        [CombatMsg.Surrenders] = "GIVES UP",
        [CombatMsg.CruelBlow] = "FINISHED OFF",
        [CombatMsg.AiMoveLeft] = "MOVE, MOVES LEFT: ",
        [CombatMsg.Takes] = "SUFFERS ",
        [CombatMsg.PointsOfDamage] = " HP ",
        [CombatMsg.TakesOnePoint] = "SUFFERS 1 HP ",
        [CombatMsg.FromFire] = "BY FIRE",
        [CombatMsg.FromCold] = "BY FROST",
        [CombatMsg.FromElectricity] = "BY LIGHTNING",
        [CombatMsg.FromAcid] = "BY ACID",
        [CombatMsg.FromMagic] = "BY MAGIC",
        [CombatMsg.SpellLostSpell] = "SPELL LOST",
        [CombatMsg.SpellGoesDown] = "FALLS",
        [CombatMsg.SpellAndIsDying] = ", DYING",
        [CombatMsg.SpellIsKilled] = "IS DEAD",
        [CombatMsg.Unaffected] = "NO EFFECT",
        [CombatMsg.HandlerGazes] = "STARES",
        [CombatMsg.HandlerGazeReflected] = "TURNS THE STARE BACK",
        [CombatMsg.HandlerStoned] = "BECOMES STONE",
        [CombatMsg.HandlerCharmGazes] = "STARES",
        [CombatMsg.HandlerBreathes] = "EXHALES LIGHTNING",
        [CombatMsg.HandlerSpitsAcid] = "SPRAYS ACID",
        [CombatMsg.BattleBegins] = "TO ARMS!",
        [CombatMsg.StatusPoisonDeath] = "SUCCUMBS TO POISON",
        [CombatMsg.StatusCollapses] = "FALLS APART",
        [CombatMsg.StatusRotsAway] = "CRUMBLES TO DUST",
        [CombatMsg.StatusPoisonKilled] = "IS DEAD",
        [CombatMsg.StatusDeadForever] = "IS GONE FOR GOOD",
        [CombatMsg.StatusDrainKilled] = "IS DEAD",
        [CombatMsg.StatusFallsDead] = "DROPS DEAD",
        [CombatMsg.Poisoned] = "IS ENVENOMED",
        [CombatMsg.Rots] = "DECAYS",
        [CombatMsg.Drained] = "LOSES ",
        [CombatMsg.DrainedOneLevel] = "ONE LEVEL",
        [CombatMsg.DrainedLevels] = " LVLS",
        [CombatMsg.StatusRevivedEnemy] = "RISES AND SMIRKS",
        [CombatMsg.StatusRevived] = "STANDS AGAIN",
        [CombatMsg.StatusEscapes] = "BECOMES MIST AND FLEES",
        [CombatMsg.FullyHealed] = "IS WHOLE AGAIN",
        [CombatMsg.PartiallyHealed] = "FEELS BETTER",
        [CombatMsg.HandlerDiseased] = "IS SICKENED",
        [CombatMsg.HandlerRakes] = "CLAWS AGAIN",
        [CombatMsg.HandlerHeldFast] = "IS GRIPPED",
        [CombatMsg.HandlerSucksBlood] = "DRINKS BLOOD",
        [CombatMsg.HandlerStronger] = "GAINS MIGHT",
        [CombatMsg.HandlerAirClears] = "THE CLOUD THINS",
        [CombatMsg.HandlerNeedsGiantStrength] = "NEEDS A GIANT'S MIGHT",
        [CombatMsg.SpellReduced] = "RETURNS TO NORMAL SIZE",
        [CombatMsg.SpellEffectRemoved] = "LOSES AN EFFECT",
        [CombatMsg.SpellCloudFormed] = "CONJURES NOXIOUS FOG",
        [CombatMsg.SpellBlindnessCured] = "REGAINS SIGHT",
        [CombatMsg.SpellMagicDispelled] = "LOSES AN ENCHANTMENT",
        [CombatMsg.SpellCurseLifted] = "IS FREED FROM THE CURSE",
        [CombatMsg.SpellItemUncursed] = "CARRIES ONE FEWER CURSED ITEM",
        [CombatMsg.SpellLevelRestored] = "REGAINS A LEVEL",
    };

    /// <summary>START.EXE strings (offsets in the unpacked image, PorProgram.Image), read before the status panel 0x1583.</summary>
    private static readonly Dictionary<CombatMsg, string> StartTable = new()
    {
        [CombatMsg.PanelHitpoints] = "HP",
        [CombatMsg.PanelAc] = "AC",
        [CombatMsg.PanelHelpless] = "(HELPLESS)",
    };

    /// <summary>
    /// Our wordings of the effect messages (0x2C540's texts, Core <see cref="EffectTexts"/>), used when the game files
    /// do not give the program's own.
    /// </summary>
    private static readonly Dictionary<string, string> EffectTable = new()
    {
        [EffectTexts.Blessed] = "GAINS A BLESSING",
        [EffectTexts.Cursed] = "GETS A CURSE",
        [EffectTexts.Affected] = "FEELS THE SPELL",
        [EffectTexts.Protected] = "GAINS PROTECTION",
        [EffectTexts.ColdResistant] = "RESISTS COLD",
        [EffectTexts.Charmed] = "FALLS UNDER A CHARM",
        [EffectTexts.Friendly] = "TURNS FRIENDLY",
        [EffectTexts.Shielded] = "GAINS A SHIELD",
        [EffectTexts.Asleep] = "DOZES OFF",
        [EffectTexts.Held] = "CANNOT MOVE",
        [EffectTexts.FireResistant] = "RESISTS FIRE",
        [EffectTexts.Silenced] = "GOES SILENT",
        [EffectTexts.SlowPoison] = "FEELS THE SPELL",
        [EffectTexts.SnakeCharm] = "FALLS UNDER A CHARM",
        [EffectTexts.Invisible] = "VANISHES",
        [EffectTexts.Knock] = "HEARS A KNOCK",
        [EffectTexts.Duplicated] = "GAINS IMAGES",
        [EffectTexts.Weakened] = "LOSES STRENGTH",
        [EffectTexts.Animated] = "RISES AGAIN",
        [EffectTexts.Blind] = "LOSES SIGHT",
        [EffectTexts.Diseased] = "FALLS ILL",
        [EffectTexts.Praying] = "PRAYS",
        [EffectTexts.BestowCurse] = "GETS A CURSE",
        [EffectTexts.Blinking] = "FLICKERS",
        [EffectTexts.Hasted] = "SPEEDS UP",
        [EffectTexts.Slowed] = "SLOWS DOWN",
        [EffectTexts.Speedy] = "SPEEDS UP",
        [EffectTexts.Paralyzed] = "CANNOT MOVE",
        [EffectTexts.InvisibleItem] = "VANISHES",
        [EffectTexts.Reading] = "CAN READ IT",
        [EffectTexts.FearParalysis] = "FREEZES IN FEAR",
        [EffectTexts.FearWeakened] = "SHAKES IN FEAR",
        [EffectTexts.AuraParalysis] = "CANNOT MOVE",
        [EffectTexts.CharmGaze] = "FALLS UNDER A CHARM",
        [EffectTexts.Coughs] = "COUGHS",
        [EffectTexts.Nausea] = "RETCHES",
    };

    private readonly Dictionary<string, string> _effects = [];
    private readonly Dictionary<CombatMsg, string> _texts = [];

    /// <summary>Our wordings only (no game files, or another version of GAME.OVR).</summary>
    public CombatText()
    {
    }

    public CombatText(PorProgramData? program)
    {
        if (program is null)
            return;
        foreach (var id in Table.Keys.Concat(StartTable.Keys))
            if (program.Text("CombatMsg." + id) is { } s)
                _texts[id] = s;
        foreach (var key in EffectTexts.All)
            if (program.Text(key) is { } s)
                _effects[key] = s;
    }

    /// <summary>How many of the effect messages came from the game files.</summary>
    public int EffectsFromGame => _effects.Count;

    /// <summary>An effect message (0x2C540's text) by its key (Core <see cref="EffectTexts"/>), upper case: the
    /// program's from the game files, else our wording (the key itself for an unknown key).</summary>
    public string Effect(string key) =>
        (_effects.TryGetValue(key, out var s) ? s : EffectTable.TryGetValue(key, out var t) ? t : key).ToUpperInvariant();

    /// <summary>How many of the texts came from the game files.</summary>
    public int FromGame => _texts.Count;

    /// <summary>The text, upper case as the font draws it (spaces kept: the program's strings carry their own).</summary>
    public string this[CombatMsg id] =>
        (_texts.TryGetValue(id, out var s) ? s : Table.TryGetValue(id, out var t) ? t : StartTable[id]).ToUpperInvariant();

    /// <summary>A text in the program's own case (the Amiga draws its bytes as they are; <see cref="AmigaView.Case"/>).</summary>
    public string Raw(CombatMsg id) => _texts.TryGetValue(id, out var s) ? s : Table.TryGetValue(id, out var t) ? t : StartTable[id];

    /// <summary>A spell routine's own text and whether it goes through START 0x2B8B with effect frames.</summary>
    internal static (CombatMsg Text, bool Animated)? SpellMessage(EffectEvent e) => e switch
    {
        EffectEvent.SpellStrengthened => (CombatMsg.HandlerStronger, false),
        EffectEvent.SpellReduced => (CombatMsg.SpellReduced, false),
        EffectEvent.SpellEffectRemoved => (CombatMsg.SpellEffectRemoved, false),
        EffectEvent.SpellCloudFormed => (CombatMsg.SpellCloudFormed, false),
        EffectEvent.SpellBlindnessCured => (CombatMsg.SpellBlindnessCured, true),
        EffectEvent.SpellMagicDispelled => (CombatMsg.SpellMagicDispelled, true),
        EffectEvent.SpellCurseLifted => (CombatMsg.SpellCurseLifted, true),
        EffectEvent.SpellItemUncursed => (CombatMsg.SpellItemUncursed, true),
        EffectEvent.SpellLevelRestored => (CombatMsg.SpellLevelRestored, false),
        _ => null,
    };

    /// <summary>The words of a menu text ("VIEW AIM " → VIEW, AIM): the program builds its menu lines from these.</summary>
    public IReadOnlyList<string> Words(CombatMsg id) => this[id].Split(' ', StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>The spell names from the player's START.EXE (<see cref="PorSpellNames"/>), 1-based.</summary>
public sealed class SpellNames
{
    private readonly IReadOnlyList<string> _names = [];

    public int Count => _names.Count;

    /// <summary>The name of spell <paramref name="id"/> (1-based), or null.</summary>
    public string? this[int id] => id >= 1 && id <= _names.Count ? _names[id - 1] : null;

    public SpellNames()
    {
    }

    public SpellNames(IReadOnlyList<string>? names) => _names = names ?? [];

    /// <summary>Scans the unpacked START.EXE image.</summary>
    public SpellNames(byte[] image) => _names = PorSpellNames.Scan(image);
}
