using System.Globalization;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Client.Play;

/// <summary>Where the money and item screens run (the program's interface state DS:49F3).</summary>
public enum Venue
{
    /// <summary>Camp (DS:49F3 = 2): View without Sell / Id.</summary>
    Camp,
    /// <summary>Shop or temple (DS:49F3 = 1): the items menu adds Sell and Id.</summary>
    Shop,
    /// <summary>The loot step after a fight (DS:49F3 = 6).</summary>
    Loot,
    /// <summary>The party menu and the training hall (0x19AE0 sets DS:49F3 = 0): the items menu has no Use.</summary>
    PartyMenu,
}

/// <summary>
/// What the shop, temple, loot and camp screens work on: the party, the money pool (DS:67F4) and the treasure list
/// (DS:6810: the shop's stock or the loot), the game's random generator, and the program's texts. The rules are
/// Core's (<see cref="ShopRules"/>, <see cref="Money"/>, <see cref="TempleRules"/>, <see cref="Carrying"/>); the
/// menus and their order follow the program (docs/re/shop-format.md, combat-format.md "After combat").
/// </summary>
public sealed class TradeContext
{
    public required Dialog Dialog { get; init; }

    /// <summary>Shows a full-screen window over the screen (null closes it).</summary>
    public Action<IReadOnlyList<WindowLine>?> ShowWindow { get; init; } = _ => { };

    /// <summary>
    /// A full-screen window's text goes through the text routine START 0x77F8 (the NPC share window, 0x69F0): the
    /// window is shown as it grows and types while DS:4A00 is set (<see cref="ClientPlayer"/>'s typing). Unset: at once.
    /// </summary>
    public Action<TextWindow> TypesInto { get; init; } = _ => { };

    /// <summary>
    /// A list page stays on the screen under the text box until the next page (the camp's Cast: 0x17D28 draws the box's
    /// frame over the spell list and blanks its inside; row 24 keeps the list's menu line). Null takes it away.
    /// </summary>
    public Action<ListRequest?> ShowPageUnder { get; init; } = _ => { };
    public required GameText Text { get; init; }
    public required EclParty Party { get; init; }
    /// <summary>The pool: seven dwords (copper … jewellery).</summary>
    public required uint[] Pool { get; init; }
    /// <summary>The treasure list (shop stock or loot), head first.</summary>
    public required List<byte[]> Treasure { get; init; }
    /// <summary>Random(n): 0..n−1 from the game's generator.</summary>
    public required Func<int, int> Random { get; init; }
    /// <summary>Item name word w from the program's table, or null.</summary>
    public Func<int, string?> Word { get; init; } = _ => null;
    /// <summary>Whether an item type is a scroll (base slot 11..13; GAME.OVR 0x2A255), from the ITEMS table.</summary>
    public Func<byte, bool> IsScroll { get; init; } = _ => false;
    /// <summary>The temple's heal list names (program table), or null.</summary>
    public IReadOnlyList<string>? TempleNames { get; init; }
    /// <summary>
    /// Where the original program stops (a run-time error back to DOS): shows it and ends the game. Called on the logic
    /// thread; the exception is thrown on afterwards, so the screen that hit it never goes on.
    /// </summary>
    public Action<ProgramHaltException>? Halt { get; init; }

    /// <summary>The screen has no text window (the party menu): our notices go on the bottom line as a key wait.</summary>
    public bool MessagesOnBottomLine { get; init; }

    public EclCharacter? Current => Party.Current;

    /// <summary>Detect magic (effect 5) on any party member: the naming routine marks magical items with "*".</summary>
    public bool Detect => Party.Members.Any(m => m.HasEffect(5));

    public string ItemName(byte[] item) => ItemNames.Compose(item, Word, Detect).TrimEnd().ToUpperInvariant();

    /// <summary>The pool holds money (0x25C07, first answer).</summary>
    public bool PoolHasMoney => Pool.Any(v => v != 0);

    /// <summary>Apply a member answer of a menu (previous / next, 0x23819; or a clicked name). Returns false for other answers.</summary>
    public bool SelectMember(int answer)
    {
        int n = Party.Members.Count;
        if (n == 0)
            return false;
        int cur = Math.Max(0, Party.CurrentIndex);
        switch (answer)
        {
            case ChoiceRequest.PreviousMember:
                Party.CurrentIndex = (cur + n - 1) % n; // the head wraps to the last member
                return true;
            case ChoiceRequest.NextMember:
                Party.CurrentIndex = (cur + 1) % n;     // the last wraps to the head
                return true;
            case <= MemberPick when MemberPick - answer < n:
                Party.CurrentIndex = MemberPick - answer;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Answers at or below this pick a member directly (a click on the party list): <c>MemberPick − index</c>.</summary>
    public const int MemberPick = -100;
}

/// <summary>The money routines the shop, temple and loot menus share (GAME.OVR 0x25191, 0x252C3, 0x25974, 0x2663B).</summary>
public static class MoneyScreens
{
    /// <summary>Pool (0x25191).</summary>
    public static void Pool(TradeContext t) => ShopRules.PoolMoney(t.Party.Members, t.Pool);

    /// <summary>
    /// Share (0x252C3). With money in the pool and no member that counts (0x84 = 0 or 0xB3) the program divides by
    /// zero and quits to DOS with run-time error 200 (<see cref="ShopRules.Share"/>): the halt goes to
    /// <see cref="TradeContext.Halt"/> (the Client ends the game there), then on up so the menu never resumes.
    /// </summary>
    public static void Share(TradeContext t)
    {
        try
        {
            ShopRules.Share(t.Party.Members, t.Pool, Carrying.StrengthAllowance);
        }
        catch (ProgramHaltException e)
        {
            t.Halt?.Invoke(e);
            throw;
        }
    }

    /// <summary>
    /// Take (0x25974 → 0x25711): pick a kind the pool holds (kinds 6 down to 0), type an amount; overloaded → a message
    /// and nothing moves.
    /// </summary>
    public static void Take(TradeContext t)
    {
        if (t.Current is not { } c)
            return;
        var kinds = Enumerable.Range(0, 7).Reverse().Where(k => t.Pool[k] != 0).ToList();
        if (kinds.Count == 0)
            return;
        var labels = kinds.Select(k => $"{t.Text.Coin(k)} {t.Pool[k].ToString(CultureInfo.InvariantCulture)}").Append("EXIT").ToList();
        int i = t.Dialog.List(t.Text[Msg.SelectCoin], labels, cancel: labels.Count - 1);
        if (i < 0 || i >= kinds.Count)
            return;
        int kind = kinds[i];
        int? n = t.Dialog.Number(t.Text[Msg.HowMuch] + t.Text[Msg.WillYouTake]);
        if (n is not { } amount || amount <= 0)
            return;
        if (!ShopRules.Take(c, t.Pool, kind, (ushort)Math.Min(amount, ushort.MaxValue), Carrying.StrengthAllowance(c)))
            t.Dialog.Say(t.Text[Msg.Overloaded]);
    }

    /// <summary>
    /// Appraise (0x2663B): needs gems or jewellery on the current character; Gems / Jewelry / Exit until Exit or
    /// nothing is left; each appraisal rolls a value, then Sell, or Sell / Keep when the character can carry one more
    /// item (<see cref="ShopRules.Appraise"/>).
    /// </summary>
    public static void Appraise(TradeContext t)
    {
        if (t.Current is not { } c)
            return;
        var d = t.Dialog;
        while (true)
        {
            int gems = c.Word(Money.Gems), jewels = c.Word(Money.Jewellery);
            if (gems == 0 && jewels == 0)
            {
                d.Say(t.Text[Msg.NoGems]);
                return;
            }
            d.Show(t.Text[Msg.FineCollection]);
            if (gems > 0)
            {
                d.Text.NewLine();
                d.Show(gems + t.Text[gems == 1 ? Msg.OneGem : Msg.Gems], clear: false);
            }
            if (jewels > 0)
            {
                d.Text.NewLine();
                d.Show(jewels + t.Text[jewels == 1 ? Msg.OneJewel : Msg.Jewels], clear: false);
            }
            var items = new List<string>();
            if (gems > 0) items.Add(t.Text[Msg.GemsItem].Trim());
            if (jewels > 0) items.Add(t.Text[Msg.JewelryItem].Trim());
            items.Add("EXIT");
            int pick = d.Menu(items, t.Text[Msg.AppraisePrompt], cancel: items.Count - 1);
            if (pick < 0 || pick >= items.Count - 1)
                return;
            bool jewellery = gems == 0 || pick == 1;
            ShopRules.Appraise(c, t.Pool, jewellery, t.Random, (value, canKeep) =>
            {
                d.Show(GameText.Cat(t.Text[jewellery ? Msg.JewelValued : Msg.GemValued], value));
                string[] choices = canKeep ? ["SELL", "KEEP"] : ["SELL"];
                // K only when offered; any other key sells (0x2663B), so backing out sells too.
                return d.Menu(choices, t.Text[Msg.YouCan], cancel: 0) == 1;
            }, Carrying.StrengthAllowance(c));
            Carrying.RecalculateWeight(c); // START 0x1758 after each appraisal
        }
    }

    /// <summary>
    /// The exit question of the shop and temple (0x7350 / the temple's): with money in the pool, a message and "go back?";
    /// returns true to stay. Pool items alone do not ask.
    /// </summary>
    public static bool StayForMoney(TradeContext t, Msg leaving, Msg goBack)
    {
        if (!t.PoolHasMoney)
            return false;
        t.Dialog.Show(t.Text[leaving]);
        t.Dialog.Text.NewLine();
        t.Dialog.Show(t.Text[goBack], clear: false, colour: 15);
        return t.Dialog.YesNo();
    }

    /// <summary>The money of a character as "N KIND" parts, highest kind first (for the view and lists).</summary>
    public static string MoneyLine(TradeContext t, EclCharacter c) => MoneyLine(t.Text, c);

    /// <summary>As <see cref="MoneyLine(TradeContext, EclCharacter)"/> from the texts alone.</summary>
    public static string MoneyLine(GameText text, EclCharacter c)
    {
        var parts = new List<string>();
        for (int k = 6; k >= 0; k--)
            if (c.Word(Money.Coins + 2 * k) is var v and > 0)
                parts.Add($"{v} {text.Coin(k)}");
        return string.Join(" ", parts);
    }
}
