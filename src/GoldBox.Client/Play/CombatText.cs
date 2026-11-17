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
    // Spell cast message (0x27C38), the yes/no menu (0x2D979).
    Casts, YesNo,
    // Spell aim: a creature already picked (0x14B79), the abort question (0x27EF2).
    AlreadyTargeted, AbortSpell, SpellAborted,
    // The status panel (START 0x1583; its strings are in START.EXE, offsets in the unpacked image).
    PanelHitpoints, PanelAc, PanelHelpless,
}

/// <summary>
/// The program's own fight texts, read from the player's GAME.OVR (PoR DOS v1.3: Pascal strings in the overlay units'
/// code segments, at the file offsets below; nothing of the text is in our code). For another GAME.OVR, or an offset
/// that does not hold a string, a short wording of ours is used instead (same scheme as <see cref="GameText"/>).
/// </summary>
public sealed class CombatText
{
    private static readonly Dictionary<CombatMsg, (int Offset, string Ours)> Table = new()
    {
        [CombatMsg.MenuMove] = (0x9E00, "MOVE "),
        [CombatMsg.MenuViewAim] = (0x9E06, "VIEW AIM "),
        [CombatMsg.MenuUse] = (0x9E10, "USE "),
        [CombatMsg.MenuCast] = (0x9E15, "CAST "),
        [CombatMsg.MenuTurn] = (0x9E1B, "TURN "),
        [CombatMsg.MenuQuickDone] = (0x9E21, "QUICK DONE"),
        [CombatMsg.MagicOn] = (0x9AFC, "AUTO MAGIC ON"),
        [CombatMsg.MagicOff] = (0x9B05, "AUTO MAGIC OFF"),
        [CombatMsg.DoesNotWork] = (0x9B0F, "NOT ALLOWED"),
        [CombatMsg.TeammateDying] = (0xA05F, "A PARTY MEMBER IS DYING"),
        [CombatMsg.ContinueBattle] = (0xA076, "FIGHT ON:"),
        [CombatMsg.MoveLeft] = (0xA1BB, "MOVE, MOVES LEFT: "),
        [CombatMsg.Flee] = (0xA1D8, "RUN AWAY:"),
        [CombatMsg.Blocked] = (0xA1DE, "CAN'T GO THERE"),
        [CombatMsg.NotWithThatWeapon] = (0xA537, "NOT WITH THIS WEAPON"),
        [CombatMsg.DoneGuard] = (0xA605, "GUARD "),
        [CombatMsg.DoneDelayQuit] = (0xA60C, "DELAY QUIT "),
        [CombatMsg.DoneBandage] = (0xA618, "BANDAGE "),
        [CombatMsg.DoneSpeedExit] = (0xA621, "SPEED EXIT"),
        [CombatMsg.IsBandaged] = (0xA7FA, "IS BANDAGED"),
        [CombatMsg.GameSpeed] = (0xA8D6, "SPEED ("),
        [CombatMsg.Slower] = (0xA8E8, "SLOWER "),
        [CombatMsg.Faster] = (0xA8F0, "FASTER "),
        [CombatMsg.SpeedExit] = (0xA8F8, "EXIT"),
        [CombatMsg.Backstabs] = (0x12A2D, "-BACKSTAB-"),
        [CombatMsg.SlaysHelpless] = (0x12A39, "KILLS THE HELPLESS"),
        [CombatMsg.Attacks] = (0x12A48, "ATTACKS"),
        [CombatMsg.FromBehind] = (0x12A50, "(BEHIND) "),
        [CombatMsg.HittingFor] = (0x12A73, "HITS FOR "),
        [CombatMsg.Point] = (0x12A80, " HP "),
        [CombatMsg.Points] = (0x12A88, " HP "),
        [CombatMsg.OfDamage] = (0x12A91, "DAMAGE"),
        [CombatMsg.AndMisses] = (0x12A9B, "MISSES"),
        [CombatMsg.LostSpell] = (0x12AA6, "SPELL LOST"),
        [CombatMsg.GoesDown] = (0x12AB3, "FALLS"),
        [CombatMsg.AndIsDying] = (0x12ABD, "FALLS, DYING"),
        [CombatMsg.IsKilled] = (0x12AEA, "IS DEAD"),
        [CombatMsg.GotAway] = (0x1344A, "ESCAPED"),
        [CombatMsg.EscapeBlocked] = (0x13453, "CAN'T ESCAPE"),
        [CombatMsg.Sweeps] = (0x13676, "SWEEPS"),
        [CombatMsg.TurnsUndead] = (0x1392B, "TURNS UNDEAD"),
        [CombatMsg.IsTurned] = (0x1393B, "IS TURNED"),
        [CombatMsg.IsDestroyed] = (0x13945, "IS DESTROYED"),
        [CombatMsg.NothingHappens] = (0x13952, "NO EFFECT"),
        [CombatMsg.CampOnlySpell] = (0x14BF0, "NOT IN A FIGHT"),
        [CombatMsg.BeginsCasting] = (0x14C00, "STARTS A SPELL"),
        [CombatMsg.AttackAlly] = (0x15185, "HIT A FRIEND: "),
        [CombatMsg.Range] = (0x15264, "DISTANCE "),
        [CombatMsg.Target] = (0x15270, "TARGET "),
        [CombatMsg.AimNextPrevManual] = (0x15278, "NEXT PREV MANUAL "),
        [CombatMsg.AimCenterExit] = (0x1528A, "CENTER EXIT"),
        [CombatMsg.AimPrompt] = (0x15296, "AIMING:"),
        [CombatMsg.UseCursorKeys] = (0x155E2, "(MOVE THE CURSOR) "),
        [CombatMsg.Casts] = (0x27C16, "CASTS"),
        [CombatMsg.YesNo] = (0x2D979, "YES NO"),
        [CombatMsg.AlreadyTargeted] = (0x148A8, "PICKED ALREADY"),
        [CombatMsg.AbortSpell] = (0x27C1C, "GIVE UP THE SPELL? "),
        [CombatMsg.SpellAborted] = (0x27C2A, "SPELL GIVEN UP"),
    };

    /// <summary>START.EXE strings (offsets in the unpacked image, PorProgram.Image), read before the status panel 0x1583.</summary>
    private static readonly Dictionary<CombatMsg, (int Offset, string Ours)> StartTable = new()
    {
        [CombatMsg.PanelHitpoints] = (0x156B, "HP"),
        [CombatMsg.PanelAc] = (0x1575, "AC"),
        [CombatMsg.PanelHelpless] = (0x1578, "(HELPLESS)"),
    };

    private readonly Dictionary<CombatMsg, string> _texts = [];

    /// <summary>Our wordings only (no game files, or another version of GAME.OVR).</summary>
    public CombatText()
    {
    }

    public CombatText(PorProgram? program)
    {
        if (program?.Overlay is not { Length: GameText.V13OverlaySize })
            return;
        foreach (var (id, (offset, _)) in Table)
            if (program.OverlayString(offset) is { } s)
                _texts[id] = s;
        foreach (var (id, (offset, _)) in StartTable)
            if (ImageString(program.Image, offset) is { } s)
                _texts[id] = s;
    }

    /// <summary>A Pascal string of printable characters at an offset of the unpacked START.EXE image, or null.</summary>
    private static string? ImageString(byte[] image, int offset)
    {
        if (offset < 0 || offset >= image.Length)
            return null;
        int n = image[offset];
        if (n == 0 || offset + 1 + n > image.Length)
            return null;
        var b = image.AsSpan(offset + 1, n);
        return b.IndexOfAnyExceptInRange((byte)0x20, (byte)0x7E) >= 0 ? null : System.Text.Encoding.Latin1.GetString(b);
    }

    /// <summary>How many of the texts came from the game files.</summary>
    public int FromGame => _texts.Count;

    /// <summary>The text, upper case as the font draws it (spaces kept: the program's strings carry their own).</summary>
    public string this[CombatMsg id] =>
        (_texts.TryGetValue(id, out var s) ? s : Table.TryGetValue(id, out var t) ? t.Ours : StartTable[id].Ours).ToUpperInvariant();

    /// <summary>The words of a menu text ("VIEW AIM " → VIEW, AIM): the program builds its menu lines from these.</summary>
    public IReadOnlyList<string> Words(CombatMsg id) => this[id].Split(' ', StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>
/// The spell names from the player's START.EXE: an array of fixed-size Pascal strings found by its shape (the first
/// run of at least <see cref="MinRun"/> consecutive names with one stride in the unpacked image). [?] The table's
/// address is not read from the code: the run with the widest entries is taken, starting at spell 1 (Lee's spell numbering; spell 21 is the
/// one FightSpellsTests casts as Sleep).
/// </summary>
public sealed class SpellNames
{
    public const int MinRun = 50;

    private readonly List<string> _names = [];

    public int Count => _names.Count;

    /// <summary>The name of spell <paramref name="id"/> (1-based), or null.</summary>
    public string? this[int id] => id >= 1 && id <= _names.Count ? _names[id - 1] : null;

    public SpellNames()
    {
    }

    public SpellNames(byte[] image)
    {
        // The run with the widest entries (the item-name words and the item kinds form runs with narrower ones).
        int best = 0, bestO = 0, bestS = 0;
        for (int stride = 12; stride <= 48; stride++)
            for (int o = 0; o + stride * MinRun < image.Length; o++)
            {
                int n = Run(image, o, stride);
                if (n >= MinRun && stride >= bestS)
                    (best, bestO, bestS) = (n, o, stride);
                if (n >= MinRun)
                    o += (n - 1) * stride;
            }
        if (best < MinRun)
            return;
        for (int k = 0; k < best; k++)
            _names.Add(System.Text.Encoding.Latin1.GetString(image, bestO + k * bestS + 1, image[bestO + k * bestS]));
    }

    private static int Run(byte[] b, int o, int stride)
    {
        int n = 0;
        for (int p = o; p + stride <= b.Length; p += stride, n++)
        {
            int len = b[p];
            if (len < 3 || len >= stride || !char.IsAsciiLetterUpper((char)b[p + 1]))
                return n;
            for (int i = 1; i <= len; i++)
            {
                byte c = b[p + i];
                if (!(char.IsAsciiLetter((char)c) || c is (byte)' ' or (byte)'\'' or (byte)',' or (byte)'-' || char.IsAsciiDigit((char)c)))
                    return n;
            }
        }
        return n;
    }
}
