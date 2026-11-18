using System.Text.Json;
using GoldBox.Core.Ecl;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Core.Tests;

/// <summary>
/// Modify Character (GAME.OVR 0x1BA1E) against the program itself: the private fixture <c>program-v1.3/modify.json</c>
/// (<c>tools/scripts/por_modify_oracle.py</c>) holds, per case, a character (made by Create from a seed and picks, then
/// some bytes overwritten; or a record of made-up bytes), the keys given to the routine's word menu, and the record the
/// routine leaves; for made-up records also the HP limits 0x1D932 / 0x1DA0B. <see cref="CharacterModify"/> must give
/// the same bytes.
/// </summary>
public class CharacterModifyProgramTests
{
    private static readonly string? File_ = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } d
        ? Path.Combine(d, "..", "program-v1.3", "modify.json") : null;

    public sealed class OracleFactAttribute : FactAttribute
    {
        public OracleFactAttribute()
        {
            if (!PorData.Present)
                Skip = PorData.SkipReason;
            else if (File_ is null || !File.Exists(File_))
                Skip = "needs the private fixture program-v1.3/modify.json";
        }
    }

    private static bool Stale(int i) => i is >= 0x7F and <= 0x82 or >= 0xC8 and <= 0xFF or >= 0x104 and <= 0x10B;

    /// <summary>A key the oracle gave (key, extended byte) as Modify tells it apart.</summary>
    internal static ModifyKey KeyOf(int key, int extended) => (key, extended) switch
    {
        (0x50, not 0) or (0x0D, not 0) => ModifyKey.Down,
        (0x48, not 0) => ModifyKey.Up,
        (0x4B, not 0) => ModifyKey.Left,
        (0x4D, not 0) => ModifyKey.Right,
        (0x4B, 0) => ModifyKey.Keep,
        (0x45, 0) or (0, 0) => ModifyKey.Exit,
        _ => ModifyKey.Other,
    };

    [OracleFact]
    public void Matches_0x1BA1E_under_emulation()
    {
        var content = PorData.Open();
        var tables = content.Program()!.CreationTables();
        Assert.NotNull(tables);
        var items = PorFight.Kit(content)!.Items;
        using var doc = JsonDocument.Parse(File.ReadAllText(File_!));
        var fails = new List<string>();
        int n = 0, refused = 0, kept = 0, exits = 0;
        foreach (var k in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var before = Convert.FromHexString(k.GetProperty("before").GetString()!);
            EclCharacter c;
            if (k.GetProperty("kind").GetString() == "created")
            {
                var picks = k.GetProperty("picks").EnumerateArray().Select(x => x.GetInt32()).ToList();
                (c, _) = CharacterCreationProgramTests.Create(tables!, items, k.GetProperty("seed").GetUInt32(), picks,
                    k.GetProperty("rerolls").GetInt32());
                foreach (var s in k.GetProperty("sets").EnumerateArray())
                {
                    int at = s[0].GetInt32();
                    uint v = s[1].GetUInt32();
                    if (at == CharacterModify.Experience)
                    {
                        c.SetWord(at, (ushort)v);
                        c.SetWord(at + 2, (ushort)(v >> 16));
                    }
                    else
                        c[at] = (byte)v;
                }
                for (int i = 0; i < before.Length; i++)
                    if (!Stale(i) && c.Record[i] != before[i])
                        fails.Add($"case {n}: created record +{i:X2} {c.Record[i]:X2} vs {before[i]:X2}");
            }
            else
            {
                c = new EclCharacter(before);
                if (CharacterModify.HpMinimum(tables!, c) != k.GetProperty("hpMin").GetInt32())
                    fails.Add($"case {n}: 0x1D932 {CharacterModify.HpMinimum(tables!, c)} vs {k.GetProperty("hpMin").GetInt32()}");
                if (CharacterModify.HpMaximum(tables!, c) != k.GetProperty("hpMax").GetInt32())
                    fails.Add($"case {n}: 0x1DA0B {CharacterModify.HpMaximum(tables!, c)} vs {k.GetProperty("hpMax").GetInt32()}");
            }

            bool can = CharacterModify.CanModify(c);
            if (can == k.GetProperty("refused").GetBoolean())
                fails.Add($"case {n}: refused {!can} vs {k.GetProperty("refused").GetBoolean()}");
            int menus = 0;
            if (can)
            {
                var m = new CharacterModify(tables!, items, c);
                var keys = k.GetProperty("keys").EnumerateArray().Select(x => KeyOf(x[0].GetInt32(), x[1].GetInt32()))
                    .ToList();
                int at = 0;
                while (true)
                {
                    var key = at < keys.Count ? keys[at++] : ModifyKey.Exit;
                    menus++;
                    if (m.Press(key))
                    {
                        if (key == ModifyKey.Keep) kept++;
                        else exits++;
                        break;
                    }
                }
            }
            else
                refused++;
            if (menus != k.GetProperty("menus").GetInt32())
                fails.Add($"case {n}: {menus} menus vs {k.GetProperty("menus").GetInt32()}");
            var want = Convert.FromHexString(k.GetProperty("after").GetString()!);
            for (int i = 0; i < want.Length; i++)
                if (!Stale(i) && c.Record[i] != want[i])
                    fails.Add($"case {n} ({k.GetProperty("kind").GetString()}) +{i:X2}: {c.Record[i]:X2} vs {want[i]:X2}");
            n++;
        }
        Assert.True(n > 900 && refused > 50 && kept > 200 && exits > 200, $"{n} cases, {refused} refused, {kept} kept, {exits} exits");
        Assert.True(fails.Count == 0, $"{fails.Count} differences: " + string.Join("; ", fails.Take(40)));
    }
}
