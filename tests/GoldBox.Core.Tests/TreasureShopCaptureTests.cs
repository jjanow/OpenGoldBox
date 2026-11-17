using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;

namespace GoldBox.Core.Tests;

/// <summary>
/// The <c>treasure_shop</c> captures (fixtures README): a patched save (slot G) whose vector 0 gives the first
/// character mixed coins, waits in an HMENU (s1), runs TREASURE with 8 random items, waits again (s2), then opens the
/// shop; in the shop Buy, Id, Sell, Pool and Share. Each state's JSON holds RandSeed, the pool, the treasure list and
/// the party (records and items), read from the heap of a DOSBox-X save state.
/// </summary>
public class TreasureShopCaptureTests
{
    private static string Dir => Path.Combine(Captures.Dir!, "treasure_shop");

    private static JsonElement Load(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir, name + ".json"))).RootElement;

    private static uint Seed(JsonElement s) => Convert.ToUInt32(s.GetProperty("randseed").GetString(), 16);
    private static byte[] Hex(JsonElement e) => Convert.FromHexString(e.GetString()!);
    private static uint[] Pool(JsonElement s) => s.GetProperty("pool").EnumerateArray().Select(v => v.GetUInt32()).ToArray();

    private static List<EclCharacter> Party(JsonElement s) =>
        s.GetProperty("party").EnumerateArray().Select(p =>
        {
            var c = new EclCharacter(Hex(p.GetProperty("record")));
            c.Items.AddRange(p.GetProperty("items").EnumerateArray().Select(Hex));
            return c;
        }).ToList();

    private static ushort[] Coins(EclCharacter c) => Enumerable.Range(0, 7).Select(k => c.Word(Money.Coins + 2 * k)).ToArray();
    private static ushort[] Coins(JsonElement s, int member) =>
        s.GetProperty("party")[member].GetProperty("money").EnumerateArray().Select(v => (ushort)v.GetInt32()).ToArray();

    [CaptureTheory]
    [InlineData("")]
    [InlineData("run1/")]
    public void Random_treasure_matches_the_program_item_for_item(string run)
    {
        var s1 = Load(run + "s1");
        var s2 = Load(run + "s2");
        var maker = new TreasureMaker(PorProgram.Open(PorData.Open())!.RandomItemTable());
        var rng = new TurboPascalRandom(Seed(s1));
        var made = new List<byte[]>();
        byte type = 0;
        for (int k = 0; k < 8; k++)
        {
            type = EncounterRules.RandomItemType(n => rng.Next((ushort)n), type);
            made.Insert(0, maker.Make(type, n => rng.Next((ushort)n)));
        }
        var game = s2.GetProperty("treasure").EnumerateArray().Select(Hex).ToList();
        Assert.Equal(game.Count, made.Count);
        for (int i = 0; i < made.Count; i++)
            Assert.Equal(Convert.ToHexString(game[i][0x2E..]), Convert.ToHexString(made[i][0x2E..]));
        Assert.Equal(Seed(s2), rng.Seed); // no other draw between the HMENU and the second HMENU
    }

    // The strength allowance (START 0x1F4B) is not modelled; these steps never come near the limit.
    private const int Allowance = 1000;

    [CaptureFact]
    public void Buy_identify_sell_pool_and_share_match_the_program()
    {
        var shop = Load("s3shop");
        var party = Party(shop);
        var pool = Pool(shop);
        Assert.All(pool, v => Assert.Equal(0u, v)); // the shop zeroes the pool on entry
        var c = party[0];
        Assert.Equal([150, 37, 9, 3, 600, 0, 0], Coins(c));

        // Buy the second stock item (Lucern hammer, 2000): 3010 gold → 1010 = 202 platinum.
        var stock = shop.GetProperty("treasure").EnumerateArray().Select(Hex).ToList();
        Assert.Equal(ShopRules.BuyResult.Bought, ShopRules.Buy(c, pool, stock[1], Allowance));
        var buy = Load("s4buy");
        Assert.Equal(Coins(buy, 0), Coins(c));
        Assert.Equal(Convert.ToHexString(stock[1][0x2E..]), Convert.ToHexString(c.Items[^1][0x2E..]));

        // Identify the (already identified) flail: 200 spent anyway.
        Assert.Equal(ShopRules.IdentifyResult.AlreadyIdentified, ShopRules.Identify(c, pool, c.Items[0]));
        Assert.Equal(Coins(Load("s5id"), 0), Coins(c));

        // Sell the flail (value 3 → 1 gold).
        var id = Party(Load("s5id"))[0];
        id.Items[0][0x34] = 0; // unreadied before the sale
        Assert.Equal(false, ShopRules.SellCheck(id.Items[0], false));
        ShopRules.Sell(id, pool, id.Items[0], Allowance);
        var sell = Load("s6sell");
        Assert.Equal(Coins(sell, 0), Coins(id));
        Assert.Equal(sell.GetProperty("party")[0].GetProperty("items").GetArrayLength(), id.Items.Count);

        // Pool everybody's money, then share it.
        var members = Party(sell);
        pool = Pool(sell);
        ShopRules.PoolMoney(members, pool);
        var pooled = Load("s7pool");
        Assert.Equal(Pool(pooled), pool);
        for (int m = 0; m < members.Count; m++)
        {
            Assert.Equal(Coins(pooled, m), Coins(members[m]));
            Assert.Equal(pooled.GetProperty("party")[m].GetProperty("weight").GetInt32(), members[m].Word(Money.Weight));
        }
        bool left = ShopRules.Share(members, pool, _ => Allowance);
        var shared = Load("s8share");
        Assert.Equal(shared.GetProperty("ds_6dc0").GetInt32() != 0, left);
        Assert.Equal(Pool(shared), pool);
        for (int m = 0; m < members.Count; m++)
        {
            Assert.Equal(Coins(shared, m), Coins(members[m]));
            Assert.Equal(shared.GetProperty("party")[m].GetProperty("weight").GetInt32(), members[m].Word(Money.Weight));
        }
    }
}
