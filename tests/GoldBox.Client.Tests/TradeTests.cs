using GoldBox.Client.Play;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Client.Tests;

/// <summary>
/// View's Trade (the Items page's 0x2276E, View's money 0x23190; docs/re/camp.md "Trade"): START 0x3379 before the
/// member pick, the pick's prompt and its start on DS:471A, the overloaded refusals on row 24, the item and coin moves,
/// and the money Trade's loop. Our wordings (no game files).
/// </summary>
public class TradeTests
{
    private sealed class Rig
    {
        public readonly List<object> Seen = [];
        public readonly Queue<object?> Answers = new();
        public readonly EclParty Party = new();
        public int Redraws;
        public readonly List<EclCharacter?> Lit = [];
        public TradeContext T = null!;
        public readonly Queue<int> ViewAnswers = new();
        public readonly Queue<ItemsEntry> ItemsAnswers = new();
        public readonly Queue<(int Kind, ushort Amount)?> CoinAnswers = new();
        public readonly List<CoinTexts?> CoinWords = [];
        public readonly List<EclCharacter> ViewPages = [];

        public Rig(params EclCharacter[] members)
        {
            Party.Members.AddRange(members);
            Party.CurrentIndex = 0;
            var w = new TextWindow();
            T = new TradeContext
            {
                Dialog = new Dialog(r =>
                {
                    switch (r)
                    {
                        case ChoiceRequest ch:
                            Seen.Add(("pick", ch.MenuPrompt));
                            return Answers.Dequeue();
                        case DelayRequest d:
                            Seen.Add(("pause", d.BottomLine?.Text));
                            return null;
                        default:
                            throw new InvalidOperationException($"unexpected {r}");
                    }
                }, w),
                Text = new GameText(),
                Party = Party,
                Pool = new uint[7],
                Treasure = [],
                Random = _ => 0,
                Memory = new EclMemory { GameSpeed = 2 },
                InterfaceRedraw = () => Redraws++,
                RosterDrawnLit = who => Lit.Add(who),
                ViewPage = (c, entries) =>
                {
                    ViewPages.Add(c);
                    return ViewAnswers.Count > 0 ? entries.ToList().IndexOf((ViewEntry)ViewAnswers.Dequeue()) : entries.Count - 1;
                },
                ItemsPage = (_, entries, row, _) => ItemsAnswers.Count > 0
                    ? new ListAnswer(entries.ToList().IndexOf(ItemsAnswers.Dequeue()), 0)
                    : new ListAnswer(entries.Count - 1, 0),
                Coins = (c, kinds, words) =>
                {
                    CoinWords.Add(words);
                    return CoinAnswers.Dequeue();
                },
            };
        }
    }

    private static EclCharacter Member(string name, ushort gold = 0)
    {
        var c = new EclCharacter { Name = name };
        c.SetWord(Money.Gold, gold);
        return c;
    }

    private static byte[] Item(byte tag, ushort weight = 10)
    {
        var r = new byte[EclCharacter.ItemRecordSize];
        r[0x2F] = tag;
        r[0x37] = (byte)weight;
        r[0x38] = (byte)(weight >> 8);
        return r;
    }

    [Fact]
    public void An_item_goes_to_the_member_picked_after_the_redraw()
    {
        var (a, b, c) = (Member("ANNA"), Member("BRAN"), Member("CARA"));
        a.Items.AddRange([Item(1), Item(2)]);
        var rig = new Rig(a, b, c);
        rig.ItemsAnswers.Enqueue(ItemsEntry.Trade);
        rig.Answers.Enqueue(ChoiceRequest.NextMember);       // End: from ANNA (DS:471A, the viewed member) to BRAN
        rig.Answers.Enqueue(0);                              // Select
        rig.T.TradeTarget = a;
        CharacterView.Items(rig.T, Venue.Explore);
        Assert.Equal(1, rig.Redraws);                        // START 0x3379 before the pick
        Assert.Equal([a, b], rig.Lit);                       // the list lit on DS:471A, then on the next member
        Assert.Equal(("pick", new GameText()[Msg.TradeWithWhom].TrimEnd()), rig.Seen[0]);
        Assert.Equal([2], a.Items.Select(i => i[0x2F]));     // the lit row (0) went
        Assert.Equal([1], b.Items.Select(i => i[0x2F]));
        Assert.Same(b, rig.T.TradeTarget);                   // DS:471A := the pick
        Assert.Equal(10, b.Word(Money.Weight));              // the pick recalculated
    }

    [Fact]
    public void A_full_member_refuses_the_item_on_row_24_and_exit_keeps_it()
    {
        var (a, b) = (Member("ANNA"), Member("BRAN"));
        a.Items.Add(Item(1));
        for (int i = 0; i < 16; i++)
            b.Items.Add(Item(9, 0));
        var rig = new Rig(a, b);
        rig.T.TradeTarget = a;
        rig.ItemsAnswers.Enqueue(ItemsEntry.Trade);
        rig.Answers.Enqueue(ChoiceRequest.NextMember);
        rig.Answers.Enqueue(0);
        rig.ItemsAnswers.Enqueue(ItemsEntry.Trade);
        rig.Answers.Enqueue(1);                              // Exit: nobody, nothing moves, DS:471A kept
        CharacterView.Items(rig.T, Venue.Camp);
        Assert.Contains(("pause", (string?)new GameText()[Msg.TradeOverloaded]), rig.Seen);
        Assert.Single(a.Items);
        Assert.Equal(16, b.Items.Count);
        Assert.Same(b, rig.T.TradeTarget);                   // set before the carry test
        Assert.Equal(b, rig.Lit[2]);                         // the second Trade's pick starts on the last one
        Assert.Equal(2, rig.Redraws);
    }

    [Fact]
    public void A_readied_item_is_refused_before_any_redraw()
    {
        var (a, b) = (Member("ANNA"), Member("BRAN"));
        var it = Item(1);
        it[0x34] = 1;
        a.Items.Add(it);
        var rig = new Rig(a, b);
        rig.ItemsAnswers.Enqueue(ItemsEntry.Trade);
        CharacterView.Items(rig.T, Venue.Explore);
        Assert.Equal(0, rig.Redraws);
        Assert.Equal(("pause", (string?)new GameText()[Msg.MustBeUnreadied]), rig.Seen.Single());
        Assert.Single(a.Items);
    }

    [Fact]
    public void Money_goes_until_the_coin_list_is_left_then_the_pick_asks_again()
    {
        var (a, b) = (Member("ANNA", gold: 300), Member("BRAN"));
        var rig = new Rig(a, b);
        rig.ViewAnswers.Enqueue((int)ViewEntry.Trade);
        rig.Answers.Enqueue(ChoiceRequest.NextMember);       // the pick starts on ANNA (View set DS:471A)
        rig.Answers.Enqueue(0);
        rig.CoinAnswers.Enqueue((3, 100));                   // 100 gold to BRAN
        rig.CoinAnswers.Enqueue((3, 500));                   // more than held: the input's maximum (200)
        rig.Answers.Enqueue(1);                              // never asked: ANNA has no money left
        CharacterView.Run(rig.T, Venue.Explore);
        Assert.Equal(0, a.Word(Money.Gold));
        Assert.Equal(300, b.Word(Money.Gold));
        Assert.Equal(1, rig.Redraws);
        Assert.All(rig.CoinWords, w => Assert.Equal(new GameText()[Msg.TradeSelectCoin], w!.Title));
        Assert.Equal(2, rig.ViewPages.Count);                 // T redraws View's page: the viewed member's
        Assert.All(rig.ViewPages, p => Assert.Same(a, p));
        Assert.Single(rig.Answers);
    }

    [Fact]
    public void Leaving_the_coin_list_after_a_transfer_redraws_and_asks_whom_again()
    {
        var (a, b) = (Member("ANNA", gold: 300), Member("BRAN"));
        b.SetWord(Money.Weight, 1450);                        // allowance 0: limit 1500
        var rig = new Rig(a, b);
        rig.ViewAnswers.Enqueue((int)ViewEntry.Trade);
        rig.Answers.Enqueue(ChoiceRequest.NextMember);
        rig.Answers.Enqueue(0);
        rig.CoinAnswers.Enqueue((3, 51));                     // refused: 1450 + 51 > 1500
        rig.CoinAnswers.Enqueue((3, 50));                     // goes
        rig.CoinAnswers.Enqueue((3, 50));                     // refused: BRAN's weight word went up (bug fixed, s42)
        rig.CoinAnswers.Enqueue(null);                        // Escape
        rig.Answers.Enqueue(0);                               // the pick again, on BRAN: Select
        rig.CoinAnswers.Enqueue(null);                        // Escape at once: Trade ends [?]
        CharacterView.Run(rig.T, Venue.Explore);
        Assert.Contains(("pause", (string?)new GameText()[Msg.Overloaded]), rig.Seen);
        Assert.Equal(250, a.Word(Money.Gold));
        Assert.Equal(50, b.Word(Money.Gold));
        Assert.Equal(1500, b.Word(Money.Weight));
        Assert.Equal(2, rig.Redraws);
        Assert.Equal([a, b, b], rig.Lit);                     // the second pick starts on BRAN (DS:471A)
    }
}
