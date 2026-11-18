using GoldBox.Data.Por;

namespace GoldBox.Client.Play;

/// <summary>The fight's texts the combat screen shows (where each is used: the routine in the comment).</summary>
public enum CombatMsg
{
    // Main menu (0x9E6D): the entries' words.
    MenuMove, MenuViewAim, MenuUse, MenuCast, MenuTurn, MenuQuickDone,
    // Alt-M, Alt-X (0x9B21); end of round (0xA087).
    MagicOn, MagicOff, DoesNotWork, TeammateDying, ContinueBattle,
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
        [CombatMsg.TeammateDying] = "A PARTY MEMBER IS DYING",
        [CombatMsg.ContinueBattle] = "FIGHT ON:",
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
    };

    /// <summary>START.EXE strings (offsets in the unpacked image, PorProgram.Image), read before the status panel 0x1583.</summary>
    private static readonly Dictionary<CombatMsg, string> StartTable = new()
    {
        [CombatMsg.PanelHitpoints] = "HP",
        [CombatMsg.PanelAc] = "AC",
        [CombatMsg.PanelHelpless] = "(HELPLESS)",
    };

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
    }

    /// <summary>How many of the texts came from the game files.</summary>
    public int FromGame => _texts.Count;

    /// <summary>The text, upper case as the font draws it (spaces kept: the program's strings carry their own).</summary>
    public string this[CombatMsg id] =>
        (_texts.TryGetValue(id, out var s) ? s : Table.TryGetValue(id, out var t) ? t : StartTable[id]).ToUpperInvariant();

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
