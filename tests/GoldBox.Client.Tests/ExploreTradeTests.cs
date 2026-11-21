using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Client.Screens;
using GoldBox.Client.Tests.Support;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Data.Por;
using Microsoft.Xna.Framework.Input;

namespace GoldBox.Client.Tests;

/// <summary>
/// View's Trade from the overland's command line (DOSBox-X s42, save A put on script 26 at 8,19 N): the explore
/// screen's party-page overlay (as Program wires it) hands over to the explore screen for the member pick, each START
/// 0x3379 steps the marker on, and the Items page and View come back over it (skipped without GOLDBOX_POR_DIR).
/// </summary>
public class ExploreTradeTests
{
    private sealed class Stack : IScreenHost
    {
        public List<IScreen> Pushed { get; } = [];
        public int Pops { get; private set; }
        public void Push(IScreen screen) => Pushed.Add(screen);
        public void Pop() => Pops++;
        public void Quit() { }
        public void SaveBindings() { }
    }

    private static void Frame(IScreen s, ActionInput input, Keys k = Keys.None, GameAction? a = null)
    {
        input.Update(k == Keys.None ? default : new KeyboardState(k), default, default, TimeSpan.Zero, _ => null);
        if (a is { } act)
            input.Inject(act);
        s.Update(input);
        input.Update(default, default, default, TimeSpan.Zero, _ => null);
    }

    [Fact]
    public void Item_and_money_trade_on_the_overland_by_keys_and_pad()
    {
        if (TestRig.PorDir() is not { } d)
            return;
        var dir = new PorGameDirectory(d);
        var savePath = dir.Find("SAVGAMA.DAT")!;
        var save = PorSaveGame.PlaceOnOverland(File.ReadAllBytes(savePath), dir, 26, 8, 19, Facing.North);
        var runner = new ScriptRunner();
        PorGame? game = null;
        var player = TestRig.Player(runner, () => game!.Memory);
        player.Resources = ClientResources.Load(dir);
        player.Game = () => game;                                // START 0x3379 through the game's area loop
        game = PorGame.FromSave(dir, PorViewArt.Load(dir), save, Path.GetDirectoryName(savePath)!,
            new PorPlayer(player, player, player, player), 7);
        var program = dir.Program()!;
        var folder = Directory.CreateTempSubdirectory("explore-trade-");
        try
        {
            var flow = new PartyMenuFlow(runner.Ask, new PartyMenuTexts(program, player.Resources), program.CreationTables()!,
                PorFight.Kit(dir)!.Items, new PorRoster(folder.FullName), 1);
            player.ViewPage = (c, entries) =>
            {
                flow.GameMemory = game.Memory;
                return flow.ViewPage(c, entries);
            };
            player.ItemsPage = (c, entries, row, name) => flow.ItemsPage(c, entries, row, name);
            player.ItemsTalk = w => flow.TextOverPage = w;
            player.Coins = (c, kinds, words) => flow.Coins(c, kinds, words);
            var host = new Stack();
            var input = new ActionInput(InputBindings.Defaults());
            var art = new ClientArt(dir, game.Art, new ScreenPalette(), PorArtChoice.Dos);
            var screen = new ExploreScreen(game, player, runner, host, 'A', art)
            {
                PartyPages = () => new PartyMenuScreen(flow, runner, host, game.Art.Font, player.Resources, art, _ => null!)
                    { Program = program, Overlay = true },
            };
            var party = game.Memory.Party!;
            party.CurrentIndex = 0;
            var (giver, next) = (party.Members[0], party.Members[1]);
            foreach (var it in giver.Items)
                it[0x34] = 0;                                    // nothing readied: every item may go
            int items = giver.Items.Count, nextItems = next.Items.Count;
            var traded = giver.Items[0];
            // The page on top: the screen the explore screen pushed last (it pops itself when the pick comes).
            IScreen Top() => host.Pushed.Count > host.Pops ? host.Pushed[^1] : screen;

            Frame(screen, input, a: GameAction.View);            // V: View's page over the overland
            Frame(screen, input);
            Assert.IsType<SheetRequest>(runner.Pending);
            int marker = game.Memory.MarkerCounter;              // after the command line's own draws
            Frame(Top(), input, Keys.I);
            Assert.IsType<CharacterPageRequest>(runner.Pending);
            Frame(Top(), input, Keys.T);                         // the item Trade: START 0x3379 case 3, then the pick
            var pick = Assert.IsType<ChoiceRequest>(runner.Pending);
            Assert.Equal(player.Resources.Texts[Msg.TradeWithWhom].TrimEnd(), pick.MenuPrompt);
            Assert.Equal((marker + 1) % 6, game.Memory.MarkerCounter); // the marker steps on (DOSBox-X: frames A, B)
            Assert.Equal(1, host.Pops);                          // the page hands over to the overland for the pick
            Frame(screen, input, Keys.End);                      // the next member
            Frame(screen, input, Keys.Enter);                    // Return: Select
            Assert.Equal(items - 1, giver.Items.Count);
            Assert.Equal(nextItems + 1, next.Items.Count);
            Assert.Equal(traded[0x2F], next.Items[^1][0x2F]);    // appended at the end of the receiver's list
            Assert.IsType<CharacterPageRequest>(runner.Pending); // the giver's Items page again
            Frame(screen, input);                                // the explore screen puts the page up again
            Frame(Top(), input, a: GameAction.Cancel);           // the pad's B: back to View
            var view = Assert.IsType<SheetRequest>(runner.Pending);
            Assert.Same(giver, view.Character);                  // View's page is the viewer's (DS:5D92)

            if (TradeRules.HasMoney(giver))
            {
                int kinds = Enumerable.Range(0, 7).Count(k => FightItemScreens.Coins(giver, k) != 0);
                Frame(Top(), input, Keys.T);                     // the money Trade: the pick starts on the last one
                Assert.IsType<ChoiceRequest>(runner.Pending);
                Assert.Equal((marker + 2) % 6, game.Memory.MarkerCounter);
                Frame(screen, input, a: GameAction.Confirm);     // the pad's A: Select
                Frame(screen, input);
                Assert.IsType<CharacterPageRequest>(runner.Pending);
                Frame(Top(), input, a: GameAction.Confirm);      // the first coin row
                var amount = Assert.IsType<TextRequest>(runner.Pending);
                ushort max = amount.Maximum!.Value;
                Frame(Top(), input, Keys.D1);
                Frame(Top(), input, Keys.Enter);                 // one coin to the next member
                if (kinds > 1 || max > 1)
                {
                    Assert.IsType<CharacterPageRequest>(runner.Pending);
                    Frame(Top(), input, a: GameAction.Cancel);   // leave the coin list: redrawn, the pick again
                    Assert.IsType<ChoiceRequest>(runner.Pending);
                    Assert.Equal((marker + 3) % 6, game.Memory.MarkerCounter);
                    Frame(screen, input, Keys.E);                // Exit: View again
                    Frame(screen, input);
                }
                Assert.IsType<SheetRequest>(runner.Pending);
            }
            Frame(Top(), input, Keys.Escape);                    // View's end: START 0x3379 again
            Frame(screen, input);
            Assert.Null(runner.Pending);
        }
        finally
        {
            runner.Dispose();
            folder.Delete(recursive: true);
        }
    }
}
