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
    NpcTag, WeaponLabel, ArmorLabel, AcLabel, HpLabel, Thac0Label, DamageLabel,
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
/// segments, at the offsets of <see cref="PorTextCatalog"/>; nothing of the text is in our code), or from a module's
/// <c>text/en.json</c>. For another GAME.OVR, or an offset that does not hold a string, a short wording of ours is used.
/// </summary>
public sealed class GameText
{
    private static readonly Dictionary<Msg, string> Table = new()
    {
        [Msg.CureAnyway] = "CAST THE CURE ANYWAY:",
        [Msg.WillOnlyCost] = " COSTS",
        [Msg.GoldPieces] = " GOLD.",
        [Msg.PayForCure] = "PAY:",
        [Msg.TempleNoMoney] = "NOT ENOUGH MONEY.",
        [Msg.IsCured] = " IS HEALED.",
        [Msg.HowCanWeHelp] = ", WHAT SHALL WE DO?",
        [Msg.NotBlind] = "IS NOT BLIND.",
        [Msg.NotDiseased] = "IS NOT SICK.",
        [Msg.NotDead] = "IS NOT DEAD.",
        [Msg.NotPoisoned] = "IS NOT POISONED.",
        [Msg.NotCursed] = "IS NOT CURSED.",
        [Msg.NotStoned] = "IS NOT STONE.",
        [Msg.TempleMoneyLeft] = "THERE IS MONEY LEFT IN THE POOL.",
        [Msg.TempleGoBack] = "GO BACK FOR IT?",
        [Msg.LostDuel] = "THE DUEL IS LOST.",
        [Msg.WonDuel] = "THE DUEL IS WON.",
        [Msg.PartyWon] = "VICTORY.",
        [Msg.PartyFled] = "THE PARTY RAN.",
        [Msg.FoundTreasure] = "THERE IS TREASURE.",
        [Msg.DuelistReceives] = "THE DUELIST GETS",
        [Msg.EachReceives] = "EACH ONE GETS",
        [Msg.ExperiencePoints] = "XP.",
        [Msg.PressEnter] = "PRESS ENTER",
        [Msg.LootTitle] = "TREASURE",
        [Msg.ItemsHeader] = "ITEMS:",
        [Msg.TakePrompt] = "TAKE:",
        [Msg.TreasureLeft] = "TREASURE REMAINS.",
        [Msg.ClaimTreasure] = "GO BACK FOR IT?",
        [Msg.TakesShare] = " TAKES A SHARE.",
        [Msg.MonstersRejoice] = "THE PARTY IS LOST.",
        [Msg.ShopTitle] = "STORE",
        [Msg.ShopOverloaded] = "TOO HEAVY",
        [Msg.ShopNoMoney] = "NOT ENOUGH MONEY.",
        [Msg.ShopMoneyLeft] = "THERE IS MONEY LEFT IN THE POOL.",
        [Msg.ShopGoBack] = "GO BACK FOR IT?",
        [Msg.MakesCamp] = "CAMPING...",
        [Msg.CampPrompt] = "CAMP: ",
        [Msg.QuitToDos] = "Quit the game ",
        [Msg.NoMagicHere] = "no magic works here",
        [Msg.NoCondition] = "is unable to ",
        [Msg.CondCast] = "cast",
        [Msg.CondMemorise] = "learn spells",
        [Msg.CondScribe] = "copy scrolls",
        [Msg.NoSpellsMemorised] = "has no spells ready",
        [Msg.CanMemorise] = "free spell slots:",
        [Msg.ClericSpells] = "            Cleric:",
        [Msg.MageSpells] = "              Mage:",
        [Msg.MemoriseThese] = "Keep These? ",
        [Msg.CannotMemorise] = "has no spell slots",
        [Msg.MemoriseTheseAgain] = "Keep These? ",
        [Msg.ScribeThese] = "Keep These? ",
        [Msg.NoCopyableScrolls] = "has no scrolls to copy",
        [Msg.AlreadyKnow] = "Already in the book",
        [Msg.AlreadyScribing] = "Already being copied",
        [Msg.CannotScribe] = "Cannot copy it.",
        [Msg.ScribeTheseAgain] = "Keep These? ",
        [Msg.Funky] = "Effect ",
        [Msg.NoSpellEffects] = " (none)",
        [Msg.DisplayTitle] = "Effects",
        [Msg.PartyOrder] = "Order: ",
        [Msg.HasBeenSelected] = "is picked",
        [Msg.QuitToDosDrop] = "Quit the game: ",
        [Msg.WillBeGone] = "will leave",
        [Msg.DropFromParty] = "Remove? ",
        [Msg.BidsFarewell] = "leaves the party",
        [Msg.DumpedInDitch] = "is left behind",
        [Msg.SighOfRelief] = "Stays",
        [Msg.GameSpeedIs] = "Speed ",
        [Msg.SpeedRange] = " (0 fast, 9 slow)",
        [Msg.Faster] = " Fast",
        [Msg.Slower] = " Slow",
        [Msg.SpeedExit] = " Exit",
        [Msg.GameSpeedPrompt] = "Speed:",
        [Msg.AlterPrompt] = "Alter: ",
        [Msg.MonstersOn] = "Mon pics on  ",
        [Msg.MonstersOff] = "Mon pics off  ",
        [Msg.PortraitsOn] = "Port on  ",
        [Msg.PortraitsOff] = "Port off  ",
        [Msg.PicsExit] = "Exit",
        [Msg.RestTime] = "Resting:",
        [Msg.RestMenu] = "Rest daYs Hrs Mins Inc Dec Exit",
        [Msg.PartyHealed] = "Everyone heals",
        [Msg.HasMemorised] = "learned",
        [Msg.HasScribed] = "copied",
        [Msg.StopResting] = "Stop? ",
        [Msg.Interrupted] = "The rest is cut short!",
        [Msg.SaveWhich] = "Save slot: ",
        [Msg.SaveSlots] = "A B C D E F G H I J",
        [Msg.Saving] = "Saving",
        [Msg.SpellsWord] = "Spells ",
        [Msg.InMemory] = "ready",
        [Msg.InSpellBook] = "in the book",
        [Msg.OnScrolls] = "on scrolls",
        [Msg.ToBeMemorised] = "to learn",
        [Msg.ToBeScribed] = "to copy",
        [Msg.ChooseSpell] = "Spell: ",
        [Msg.CastAction] = "Cast",
        [Msg.MemoriseAction] = "Learn",
        [Msg.ScribeAction] = "Copy",
        [Msg.CastOnWhom] = "Target",
        [Msg.CombatOnly] = "works only in a fight",
        [Msg.LoseIt] = "Forget it? ",
        [Msg.Casts] = "casts",
        [Msg.Age] = "AGE",
        [Msg.Str] = "STR",
        [Msg.Int] = "INT",
        [Msg.Wis] = "WIS",
        [Msg.Dex] = "DEX",
        [Msg.Con] = "CON",
        [Msg.Cha] = "CHA",
        [Msg.Level] = "LVL",
        [Msg.Exp] = "XP",
        [Msg.Status] = "STATE",
        [Msg.Encumbrance] = "LOAD",
        [Msg.Movement] = "MOVE",
        [Msg.ViewPrompt] = "VIEW:",
        [Msg.NpcTag] = "(NPC)",
        [Msg.WeaponLabel] = "WEAPON",
        [Msg.ArmorLabel] = "ARMOUR",
        [Msg.AcLabel] = "AC    ",
        [Msg.HpLabel] = "HP    ",
        [Msg.Thac0Label] = "THAC0   ",
        [Msg.DamageLabel] = "DMG           ",
        [Msg.MustBeUnreadied] = "UNREADY IT FIRST.",
        [Msg.ScribeWarning] = " MEANT TO SCRIBE FROM IT.",
        [Msg.OkayToLose] = "LOSE IT?",
        [Msg.IllGiveYou] = "OFFER: ",
        [Msg.GoldPiecesForYour] = " GOLD FOR ",
        [Msg.IsItADeal] = "AGREED?",
        [Msg.Sold] = "DONE.",
        [Msg.SoldOverloaded] = "TOO HEAVY: THE REST GOES TO THE POOL.",
        [Msg.IdentifyOffer] = "IDENTIFY FOR 200 GOLD:",
        [Msg.IdentifyDeal] = "AGREED?",
        [Msg.IdentifyNoMoney] = "NOT ENOUGH MONEY.",
        [Msg.NothingNew] = "NOTHING MORE TO LEARN ABOUT",
        [Msg.MoneyToPool] = "TOO HEAVY: THE REST GOES TO THE POOL.",
        [Msg.Overloaded] = "TOO HEAVY",
        [Msg.CoinGems] = "GEMS",
        [Msg.CoinGold] = "GP",
        [Msg.CoinPlatinum] = "PP",
        [Msg.CoinElectrum] = "EP",
        [Msg.CoinSilver] = "SP",
        [Msg.CoinCopper] = "CP",
        [Msg.CoinJewelry] = "JEWELS",
        [Msg.SelectCoin] = "WHICH COINS?",
        [Msg.HowMuch] = "HOW MANY",
        [Msg.WillYouTake] = " TO TAKE?",
        [Msg.NoGems] = "NOTHING TO APPRAISE",
        [Msg.OneGem] = " GEM",
        [Msg.Gems] = " GEMS",
        [Msg.OneJewel] = " JEWEL",
        [Msg.Jewels] = " JEWELS",
        [Msg.FineCollection] = "YOU HAVE:",
        [Msg.GemsItem] = "GEMS",
        [Msg.JewelryItem] = "JEWELS",
        [Msg.AppraisePrompt] = "APPRAISE:",
        [Msg.GemValued] = "THE GEM IS WORTH",
        [Msg.YouCan] = "CHOOSE:",
        [Msg.JewelValued] = "THE JEWEL IS WORTH",
    };

    private readonly Dictionary<Msg, string> _texts = [];

    /// <summary>Our wordings only (no game files, or another version of GAME.OVR).</summary>
    public GameText()
    {
    }

    public GameText(PorProgramData? program)
    {
        if (program is null)
            return;
        foreach (var id in Table.Keys)
            if (program.Text("Msg." + id) is { } s)
                _texts[id] = s;
        for (int i = 0; i < _effectLabels.Length; i++)
            _effectLabels[i] = program.Text("EffectLabel." + i);
    }

    /// <summary>How many of the texts came from the game files.</summary>
    public int FromGame => _texts.Count;

    /// <summary>The text with its original case (menu lines whose capitals are the hotkeys).</summary>
    public string Original(Msg id) => _texts.TryGetValue(id, out var s) ? s : Table[id];

    /// <summary>
    /// The camp Display list's fixed effect texts (0x187ED: code-segment strings, catalogue keys EffectLabel.0..13, in
    /// the order of <see cref="GoldBox.Core.Game.CampSpells.LabelledEffects"/>); null where the game files do not hold them.
    /// </summary>
    private readonly string?[] _effectLabels = new string?[GoldBox.Core.Game.CampSpells.LabelledEffects.Length];

    /// <summary>The Display list's text for a labelled effect id (upper case), or null when not read.</summary>
    public string? EffectLabel(byte id)
    {
        int i = Array.IndexOf(GoldBox.Core.Game.CampSpells.LabelledEffects, id);
        return i >= 0 ? _effectLabels[i]?.ToUpperInvariant() : null;
    }

    /// <summary>The text, upper case as the font draws it.</summary>
    public string this[Msg id] => (_texts.TryGetValue(id, out var s) ? s : Table[id]).ToUpperInvariant();

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
