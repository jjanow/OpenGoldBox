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
    MakesCamp, CampPrompt,
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
    }

    /// <summary>How many of the texts came from the game files.</summary>
    public int FromGame => _texts.Count;

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
