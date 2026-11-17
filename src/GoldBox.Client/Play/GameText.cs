using GoldBox.Data.Por;

namespace GoldBox.Client.Play;

/// <summary>The program's messages the Client's screens show (where each is used: the routine in the comment).</summary>
public enum Msg
{
    // Temple (0x4577–0x4F44, 0x5205)
    CureAnyway, WillOnlyCost, GoldPieces, PayForCure, TempleNoMoney, IsCured, HowCanWeHelp,
    NotBlind, NotDiseased, NotDead, NotPoisoned, NotCursed, NotStoned, TempleMoneyLeft, TempleGoBack,
    // After combat (0x5DBB–0x6A8B)
    LostDuel, WonDuel, PartyWon, PartyFled, FoundTreasure, DuelistReceives, EachReceives, ExperiencePoints, PressEnter,
    LootTitle, ItemsHeader, TakePrompt, TreasureLeft, ClaimTreasure, TakesShare, MonstersRejoice,
    // Shop (0x6CF3–0x71E7)
    ShopTitle, ShopOverloaded, ShopNoMoney, ShopMoneyLeft, ShopGoBack,
    // Camp (0x19654)
    MakesCamp, CampPrompt, QuitToDos,
    // Camp spells (0x17B33–0x18376)
    NoMagicHere, NoCondition, CondCast, CondMemorise, CondScribe, NoSpellsMemorised, CanMemorise, ClericSpells,
    MageSpells, MemoriseThese, CannotMemorise, MemoriseTheseAgain, ScribeThese, NoCopyableScrolls, AlreadyKnow,
    AlreadyScribing, CannotScribe, ScribeTheseAgain,
    // Camp display (0x186F1–0x187E4)
    Funky, NoSpellEffects, DisplayTitle,
    // Alter (0x18F71–0x19485)
    PartyOrder, HasBeenSelected, QuitToDosDrop, WillBeGone, DropFromParty, BidsFarewell, DumpedInDitch, SighOfRelief,
    GameSpeedIs, SpeedRange, Faster, Slower, SpeedExit, GameSpeedPrompt, AlterPrompt, MonstersOn, MonstersOff,
    PortraitsOn, PortraitsOff, PicsExit,
    // Rest (0x243AF–0x24A45)
    RestTime, RestMenu, PartyHealed, HasMemorised, HasScribed, StopResting, Interrupted,
    // Save (0x1FCAB–0x1FD11)
    SaveWhich, SaveSlots, Saving,
    // Spell lists and the cast (0x23973–0x27C16)
    SpellsWord, InMemory, InSpellBook, OnScrolls, ToBeMemorised, ToBeScribed, ChooseSpell, CastAction, MemoriseAction,
    ScribeAction, CastOnWhom, CombatOnly, LoseIt, Casts,
    // Character view (0x2103A–0x21AC7)
    Age, Str, Int, Wis, Dex, Con, Cha, Level, Exp, Status, Encumbrance, Movement, ViewPrompt,
    // Items (0x21D5A–0x22F70)
    MustBeUnreadied, ScribeWarning, OkayToLose, IllGiveYou, GoldPiecesForYour, IsItADeal, Sold, SoldOverloaded,
    IdentifyOffer, IdentifyDeal, IdentifyNoMoney, NothingNew,
    // Money (0x24E64–0x25964)
    MoneyToPool, Overloaded, CoinGems, CoinGold, CoinPlatinum, CoinElectrum, CoinSilver, CoinCopper, CoinJewelry,
    SelectCoin, HowMuch, WillYouTake,
    // Appraise (0x26569–0x26623)
    NoGems, OneGem, Gems, OneJewel, Jewels, FineCollection, GemsItem, JewelryItem, AppraisePrompt, GemValued, YouCan,
    JewelValued,
}

/// <summary>
/// The program's own texts, read from the player's GAME.OVR (PoR DOS v1.3: Pascal strings in the overlay units' code
/// segments, at the file offsets below; nothing of the text is in our code). For another GAME.OVR, or an offset that
/// does not hold a string, a short wording of ours is used instead.
/// </summary>
public sealed class GameText
{
    /// <summary>Size of the v1.3 GAME.OVR the offsets belong to.</summary>
    public const int V13OverlaySize = 206105;

    private static readonly Dictionary<Msg, (int Offset, string Ours)> Table = new()
    {
        [Msg.CureAnyway] = (0x4577, "CAST THE CURE ANYWAY:"),
        [Msg.WillOnlyCost] = (0x45ED, " COSTS"),
        [Msg.GoldPieces] = (0x45FE, " GOLD."),
        [Msg.PayForCure] = (0x460C, "PAY:"),
        [Msg.TempleNoMoney] = (0x461A, "NOT ENOUGH MONEY."),
        [Msg.IsCured] = (0x462C, " IS HEALED."),
        [Msg.HowCanWeHelp] = (0x4F2D, ", WHAT SHALL WE DO?"),
        [Msg.NotBlind] = (0x47A3, "IS NOT BLIND."),
        [Msg.NotDiseased] = (0x4846, "IS NOT SICK."),
        [Msg.NotDead] = (0x4A6F, "IS NOT DEAD."),
        [Msg.NotPoisoned] = (0x4CA5, "IS NOT POISONED."),
        [Msg.NotCursed] = (0x4D95, "IS NOT CURSED."),
        [Msg.NotStoned] = (0x4E81, "IS NOT STONE."),
        [Msg.TempleMoneyLeft] = (0x5205, "THERE IS MONEY LEFT IN THE POOL."),
        [Msg.TempleGoBack] = (0x5250, "GO BACK FOR IT?"),
        [Msg.LostDuel] = (0x5DBB, "THE DUEL IS LOST."),
        [Msg.WonDuel] = (0x5DD3, "THE DUEL IS WON."),
        [Msg.PartyWon] = (0x5DEA, "VICTORY."),
        [Msg.PartyFled] = (0x5DFD, "THE PARTY RAN."),
        [Msg.FoundTreasure] = (0x5E11, "THERE IS TREASURE."),
        [Msg.DuelistReceives] = (0x5E2F, "THE DUELIST GETS"),
        [Msg.EachReceives] = (0x5E45, "EACH ONE GETS"),
        [Msg.ExperiencePoints] = (0x5E5E, "XP."),
        [Msg.PressEnter] = (0x5E71, "PRESS ENTER"),
        [Msg.LootTitle] = (0x6080, "TREASURE"),
        [Msg.ItemsHeader] = (0x6085, "ITEMS:"),
        [Msg.TakePrompt] = (0x629E, "TAKE:"),
        [Msg.TreasureLeft] = (0x63FB, "TREASURE REMAINS."),
        [Msg.ClaimTreasure] = (0x641B, "GO BACK FOR IT?"),
        [Msg.TakesShare] = (0x6827, " TAKES A SHARE."),
        [Msg.MonstersRejoice] = (0x6A55, "THE PARTY IS LOST."),
        [Msg.ShopTitle] = (0x6CF3, "STORE"),
        [Msg.ShopOverloaded] = (0x6F02, "TOO HEAVY"),
        [Msg.ShopNoMoney] = (0x7023, "NOT ENOUGH MONEY."),
        [Msg.ShopMoneyLeft] = (0x7194, "THERE IS MONEY LEFT IN THE POOL."),
        [Msg.ShopGoBack] = (0x71E7, "GO BACK FOR IT?"),
        [Msg.MakesCamp] = (0x19654, "CAMPING..."),
        [Msg.CampPrompt] = (0x1968C, "CAMP: "),
        [Msg.QuitToDos] = (0x19693, "Quit the game "),
        [Msg.NoMagicHere] = (0x17B33, "no magic works here"),
        [Msg.NoCondition] = (0x17B53, "is unable to "),
        [Msg.CondCast] = (0x17B6A, "cast"),
        [Msg.CondMemorise] = (0x17B7A, "learn spells"),
        [Msg.CondScribe] = (0x17B8A, "copy scrolls"),
        [Msg.NoSpellsMemorised] = (0x17CBA, "has no spells ready"),
        [Msg.CanMemorise] = (0x17DA1, "free spell slots:"),
        [Msg.ClericSpells] = (0x17DAF, "            Cleric:"),
        [Msg.MageSpells] = (0x17DC2, "              Mage:"),
        [Msg.MemoriseThese] = (0x180F0, "Keep These? "),
        [Msg.CannotMemorise] = (0x18108, "has no spell slots"),
        [Msg.MemoriseTheseAgain] = (0x18123, "Keep These? "),
        [Msg.ScribeThese] = (0x182EA, "Keep These? "),
        [Msg.NoCopyableScrolls] = (0x18300, "has no scrolls to copy"),
        [Msg.AlreadyKnow] = (0x18318, "Already in the book"),
        [Msg.AlreadyScribing] = (0x18334, "Already being copied"),
        [Msg.CannotScribe] = (0x18357, "Cannot copy it."),
        [Msg.ScribeTheseAgain] = (0x18376, "Keep These? "),
        [Msg.Funky] = (0x186F1, "Effect "),
        [Msg.NoSpellEffects] = (0x187D0, " (none)"),
        [Msg.DisplayTitle] = (0x187E4, "Effects"),
        [Msg.PartyOrder] = (0x18F71, "Order: "),
        [Msg.HasBeenSelected] = (0x18F9F, "is picked"),
        [Msg.QuitToDosDrop] = (0x190A8, "Quit the game: "),
        [Msg.WillBeGone] = (0x190B6, "will leave"),
        [Msg.DropFromParty] = (0x190C3, "Remove? "),
        [Msg.BidsFarewell] = (0x190D5, "leaves the party"),
        [Msg.DumpedInDitch] = (0x190E7, "is left behind"),
        [Msg.SighOfRelief] = (0x190FC, "Stays"),
        [Msg.GameSpeedIs] = (0x19261, "Speed "),
        [Msg.SpeedRange] = (0x1926F, " (0 fast, 9 slow)"),
        [Msg.Faster] = (0x19286, " Fast"),
        [Msg.Slower] = (0x1928E, " Slow"),
        [Msg.SpeedExit] = (0x19296, " Exit"),
        [Msg.GameSpeedPrompt] = (0x1929C, "Speed:"),
        [Msg.AlterPrompt] = (0x19441, "Alter: "),
        [Msg.MonstersOn] = (0x19449, "Mon pics on  "),
        [Msg.MonstersOff] = (0x19457, "Mon pics off  "),
        [Msg.PortraitsOn] = (0x19466, "Port on  "),
        [Msg.PortraitsOff] = (0x19475, "Port off  "),
        [Msg.PicsExit] = (0x19485, "Exit"),
        [Msg.RestTime] = (0x243AF, "Resting:"),
        [Msg.RestMenu] = (0x244A5, "Rest daYs Hrs Mins Inc Dec Exit"),
        [Msg.PartyHealed] = (0x24623, "Everyone heals"),
        [Msg.HasMemorised] = (0x246E3, "learned"),
        [Msg.HasScribed] = (0x247A7, "copied"),
        [Msg.StopResting] = (0x24A36, "Stop? "),
        [Msg.Interrupted] = (0x24A45, "The rest is cut short!"),
        [Msg.SaveWhich] = (0x1FCAB, "Save slot: "),
        [Msg.SaveSlots] = (0x1FCBD, "A B C D E F G H I J"),
        [Msg.Saving] = (0x1FD11, "Saving"),
        [Msg.SpellsWord] = (0x239CE, "Spells "),
        [Msg.InMemory] = (0x23973, "ready"),
        [Msg.InSpellBook] = (0x2397D, "in the book"),
        [Msg.OnScrolls] = (0x23995, "on scrolls"),
        [Msg.ToBeMemorised] = (0x239AF, "to learn"),
        [Msg.ToBeScribed] = (0x239BF, "to copy"),
        [Msg.ChooseSpell] = (0x27053, "Spell: "),
        [Msg.CastAction] = (0x27018, "Cast"),
        [Msg.MemoriseAction] = (0x2701D, "Learn"),
        [Msg.ScribeAction] = (0x27026, "Copy"),
        [Msg.CastOnWhom] = (0x27A96, "Target"),
        [Msg.CombatOnly] = (0x27BC6, "works only in a fight"),
        [Msg.LoseIt] = (0x27BE0, "Forget it? "),
        [Msg.Casts] = (0x27C16, "casts"),
        [Msg.Age] = (0x21040, "AGE"),
        [Msg.Str] = (0x21045, "STR"),
        [Msg.Int] = (0x2104A, "INT"),
        [Msg.Wis] = (0x2104F, "WIS"),
        [Msg.Dex] = (0x21054, "DEX"),
        [Msg.Con] = (0x21059, "CON"),
        [Msg.Cha] = (0x2105E, "CHA"),
        [Msg.Level] = (0x21063, "LVL"),
        [Msg.Exp] = (0x2106B, "XP"),
        [Msg.Status] = (0x2107D, "STATE"),
        [Msg.Encumbrance] = (0x21624, "LOAD"),
        [Msg.Movement] = (0x21636, "MOVE"),
        [Msg.ViewPrompt] = (0x21AC7, "VIEW:"),
        [Msg.MustBeUnreadied] = (0x21D5A, "UNREADY IT FIRST."),
        [Msg.ScribeWarning] = (0x21D6C, " MEANT TO SCRIBE FROM IT."),
        [Msg.OkayToLose] = (0x21D92, "LOSE IT?"),
        [Msg.IllGiveYou] = (0x22CC5, "OFFER: "),
        [Msg.GoldPiecesForYour] = (0x22CD4, " GOLD FOR "),
        [Msg.IsItADeal] = (0x22CEB, "AGREED?"),
        [Msg.Sold] = (0x22CFA, "DONE."),
        [Msg.SoldOverloaded] = (0x22D00, "TOO HEAVY: THE REST GOES TO THE POOL."),
        [Msg.IdentifyOffer] = (0x22F02, "IDENTIFY FOR 200 GOLD:"),
        [Msg.IdentifyDeal] = (0x22F2A, "AGREED?"),
        [Msg.IdentifyNoMoney] = (0x22F39, "NOT ENOUGH MONEY."),
        [Msg.NothingNew] = (0x22F4A, "NOTHING MORE TO LEARN ABOUT"),
        [Msg.MoneyToPool] = (0x24E64, "TOO HEAVY: THE REST GOES TO THE POOL."),
        [Msg.Overloaded] = (0x250CA, "TOO HEAVY"),
        [Msg.CoinGems] = (0x257E3, "GEMS"),
        [Msg.CoinGold] = (0x257E9, "GP"),
        [Msg.CoinPlatinum] = (0x257EF, "PP"),
        [Msg.CoinElectrum] = (0x257F9, "EP"),
        [Msg.CoinSilver] = (0x25803, "SP"),
        [Msg.CoinCopper] = (0x2580B, "CP"),
        [Msg.CoinJewelry] = (0x25813, "JEWELS"),
        [Msg.SelectCoin] = (0x25946, "WHICH COINS?"),
        [Msg.HowMuch] = (0x2595A, "HOW MANY"),
        [Msg.WillYouTake] = (0x25964, " TO TAKE?"),
        [Msg.NoGems] = (0x26569, "NOTHING TO APPRAISE"),
        [Msg.OneGem] = (0x2657C, " GEM"),
        [Msg.Gems] = (0x26581, " GEMS"),
        [Msg.OneJewel] = (0x26587, " JEWEL"),
        [Msg.Jewels] = (0x26599, " JEWELS"),
        [Msg.FineCollection] = (0x265AC, "YOU HAVE:"),
        [Msg.GemsItem] = (0x265CB, "GEMS"),
        [Msg.JewelryItem] = (0x265D2, "JEWELS"),
        [Msg.AppraisePrompt] = (0x265E2, "APPRAISE:"),
        [Msg.GemValued] = (0x265EE, "THE GEM IS WORTH"),
        [Msg.YouCan] = (0x26618, "CHOOSE:"),
        [Msg.JewelValued] = (0x26623, "THE JEWEL IS WORTH"),
    };

    private readonly Dictionary<Msg, string> _texts = [];

    /// <summary>Our wordings only (no game files, or another version of GAME.OVR).</summary>
    public GameText()
    {
    }

    public GameText(PorProgram? program)
    {
        if (program?.Overlay is not { Length: V13OverlaySize })
            return;
        foreach (var (id, (offset, _)) in Table)
            if (program.OverlayString(offset) is { } s)
                _texts[id] = s;
        for (int i = 0; i < EffectLabelOffsets.Length; i++)
            _effectLabels[i] = program.OverlayString(EffectLabelOffsets[i]);
    }

    /// <summary>How many of the texts came from the game files.</summary>
    public int FromGame => _texts.Count;

    /// <summary>The text with its original case (menu lines whose capitals are the hotkeys).</summary>
    public string Original(Msg id) => _texts.TryGetValue(id, out var s) ? s : Table[id].Ours;

    /// <summary>
    /// The camp Display list's fixed effect texts (0x187ED: code-segment strings at these offsets, in the order of
    /// <see cref="GoldBox.Core.Game.CampSpells.LabelledEffects"/>); null where the game files do not hold them.
    /// </summary>
    private static readonly int[] EffectLabelOffsets =
        [0x186F9, 0x1871A, 0x18740, 0x1874D, 0x18756, 0x1875D, 0x1876B, 0x18781, 0x1878A, 0x18793, 0x187A0, 0x187B0, 0x187BA, 0x187C6];

    private readonly string?[] _effectLabels = new string?[EffectLabelOffsets.Length];

    /// <summary>The Display list's text for a labelled effect id (upper case), or null when not read.</summary>
    public string? EffectLabel(byte id)
    {
        int i = Array.IndexOf(GoldBox.Core.Game.CampSpells.LabelledEffects, id);
        return i >= 0 ? _effectLabels[i]?.ToUpperInvariant() : null;
    }

    /// <summary>The text, upper case as the font draws it.</summary>
    public string this[Msg id] => (_texts.TryGetValue(id, out var s) ? s : Table[id].Ours).ToUpperInvariant();

    /// <summary>Two texts with one space between them (the program's strings often carry their own).</summary>
    public static string Cat(string a, object b)
    {
        string t = Convert.ToString(b, System.Globalization.CultureInfo.InvariantCulture) ?? "";
        return a.EndsWith(' ') || t.StartsWith(' ') || a.Length == 0 ? a + t : a + " " + t;
    }

    /// <summary>A coin kind's name (pool and money index 0..6: copper, silver, electrum, gold, platinum, gems, jewellery).</summary>
    public string Coin(int kind) => this[kind switch
    {
        0 => Msg.CoinCopper,
        1 => Msg.CoinSilver,
        2 => Msg.CoinElectrum,
        3 => Msg.CoinGold,
        4 => Msg.CoinPlatinum,
        5 => Msg.CoinGems,
        _ => Msg.CoinJewelry,
    }].Trim();
}
