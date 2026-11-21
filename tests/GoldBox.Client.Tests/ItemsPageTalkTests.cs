using GoldBox.Client.Play;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;

namespace GoldBox.Client.Tests;

/// <summary>
/// Sell and Id on the program's Items page (0x22D28, 0x22F8C, read s41): their text in the box (1, 21)–(38, 22) in
/// colour 14 over the page (<see cref="TradeContext.TalkOverItemsPage"/> on meanwhile), the yes/no on row 24, the
/// row-24 messages during the game-speed pause, the box blanked at the end. Our wordings (no game files).
/// </summary>
public class ItemsPageTalkTests
{
    private static (TradeContext T, List<object> Seen, List<bool> Talk, EclCharacter C) Shop(params object?[] answers)
    {
        var w = new TextWindow();
        var seen = new List<object>();
        var talk = new List<bool>();
        var q = new Queue<object?>(answers);
        var c = new EclCharacter { Name = "Anna" };
        c[0x10D] = 1;
        var item = new byte[63];
        item[0x34] = 0;    // not readied
        item[0x3A] = 100;  // value 100: the offer 50
        item[0x35] = 1;    // not identified
        c.Items.Add(item);
        c.SetWord(Money.Gold, 300);
        var p = new EclParty();
        p.Members.Add(c);
        p.CurrentIndex = 0;
        int pages = 0;
        TradeContext? t = null;
        t = new TradeContext
        {
            Dialog = new Dialog(r =>
            {
                switch (r)
                {
                    case ChoiceRequest ch:
                        seen.Add(("ask", ch.MenuPrompt, w.Lines[4], w.Lines[5], w.Cell(21, 1).Colour));
                        return q.Dequeue();
                    case DelayRequest d:
                        seen.Add(("pause", d.BottomLine?.Text, w.Lines[4], w.Lines[5]));
                        return null;
                    default:
                        throw new InvalidOperationException($"unexpected {r}");
                }
            }, w),
            Text = new GameText(),
            Party = p,
            Pool = new uint[7],
            Treasure = [],
            Random = _ => 0,
            Memory = new EclMemory { GameSpeed = 2 },
            TalkOverItemsPage = on => talk.Add(on),
            ItemsPage = (_, entries, row, _) =>
            {
                // First the answer under test on row 0, then Exit.
                if (pages++ == 0 && q.Dequeue() is ItemsEntry e)
                    return new ListAnswer(entries.ToList().IndexOf(e), 0);
                return new ListAnswer(entries.Count - 1, 0);
            },
        };
        return (t, seen, talk, c);
    }

    [Fact]
    public void Sells_offer_is_in_the_box_over_the_page_and_sold_is_on_row_24()
    {
        var (t, seen, talk, c) = Shop(ItemsEntry.Sell, 0); // Sell, Yes
        CharacterView.Items(t, Venue.Shop);
        Assert.Equal([true, false], talk);
        Assert.Empty(c.Items);
        var ask = ((string, string?, string, string, byte))seen[0];
        Assert.Equal("ask", ask.Item1);
        Assert.Contains("50", ask.Item3 + ask.Item4);     // the offer on rows 21–22
        Assert.Equal(14, ask.Item5);                        // colour 14
        var sold = ((string, string?, string, string))seen[1];
        Assert.Equal(new GameText()[Msg.Sold], sold.Item2); // START 0x2233 on row 24 during the pause
        Assert.Equal(2, seen.Count);
        Assert.True(t.Dialog.Text.IsEmpty);                 // the box blanked (03F1:0137 at 0x22EF7)
    }

    [Fact]
    public void Id_shows_the_new_name_in_the_box_with_the_game_speed_pause()
    {
        var (t, seen, talk, c) = Shop(ItemsEntry.Id, 0); // Id, Yes
        CharacterView.Items(t, Venue.Shop);
        Assert.Equal([true, false], talk);
        Assert.Equal(0, c.Items[0][0x35]);
        var after = ((string, string?, string, string))seen[1];
        Assert.Null(after.Item2);                                       // PauseOnly, nothing on row 24
        Assert.StartsWith(new GameText()[Msg.LooksLike].Trim(), after.Item3);
        Assert.True(t.Dialog.Text.IsEmpty);
    }

    [Fact]
    public void Id_without_the_money_says_so_on_row_24()
    {
        var (t, seen, _, c) = Shop(ItemsEntry.Id, 0);
        c.SetWord(Money.Gold, 0);
        CharacterView.Items(t, Venue.Shop);
        Assert.Equal(1, c.Items[0][0x35]);
        var msg = ((string, string?, string, string))seen[1];
        Assert.Equal(new GameText()[Msg.IdentifyNoMoney], msg.Item2);
        Assert.Equal(2, seen.Count);
    }
}
