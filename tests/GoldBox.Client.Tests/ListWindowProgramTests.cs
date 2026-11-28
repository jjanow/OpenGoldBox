using System.Text.Json;
using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Client.Screens;
using Microsoft.Xna.Framework.Input;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Client.Tests;

/// <summary>
/// The list routine GAME.OVR 0x2D669 run whole against the program: the fixture <c>program-v1.3/list_window.json</c>
/// (private, from <c>tools/scripts/por_listwin_oracle.py</c>) holds generated lists, windows, colours and key scripts
/// run through 0x2D669 and the menu routine 0x2CC8C under emulation, with the program's draw log. <see cref="ListWindowRoutine"/>
/// (the routine, the steps of <see cref="ListWindowSteps"/>, the menu routine's key layer) must end on the same
/// position, window top, menu cell, entry and key, hand the menu routine the same lines, and draw the same rows and
/// highlights (the menu routine's own drawing left out).
/// </summary>
public class ListWindowProgramTests
{
    private static readonly string? File_ = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } d
        ? Path.Combine(d, "..", "program-v1.3", "list_window.json") : null;

    public sealed class OracleFactAttribute : FactAttribute
    {
        public OracleFactAttribute()
        {
            if (File_ is null || !File.Exists(File_))
                Skip = "needs the private fixture program-v1.3/list_window.json";
        }
    }

    private static string Show(ListWindowRoutine.Draw d) => d.Kind switch
    {
        's' => $"s {d.Col},{d.Row} c{d.Colour} '{d.Text}'",
        'g' => $"g {d.Col},{d.Row} c{d.Colour} x{d.Count} {d.Char}",
        _ => $"b {d.Col},{d.Row}-{d.Colour},{d.Count}",
    };

    private static ListWindowRoutine.Draw Parse(JsonElement e)
    {
        var a = e.EnumerateArray().ToArray();
        return a[0].GetString() switch
        {
            "s" => new('s', a[1].GetInt32(), a[2].GetInt32(), a[3].GetInt32(), a[4].GetString()!),
            "g" => new('g', a[1].GetInt32(), a[2].GetInt32(), a[3].GetInt32(), Count: a[4].GetInt32(), Char: a[5].GetInt32()),
            _ => new('b', a[1].GetInt32(), a[2].GetInt32(), a[3].GetInt32(), Count: a[4].GetInt32()),
        };
    }

    [OracleFact]
    public void The_list_routine_0x2D669_matches_the_program_under_emulation()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(File_!));
        var fails = new List<string>();
        int n = 0, paged = 0, digits = 0;
        int skipped = 0;
        foreach (var k in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            if (k.GetProperty("oob").GetBoolean())
            {
                // 0x2C8CF was asked for an entry outside the list: the program read the interrupt table.
                skipped++;
                continue;
            }
            var rows = k.GetProperty("rows").EnumerateArray().Select(x => x.GetString()!).ToList();
            var labels = k.GetProperty("labels").EnumerateArray().Select(x => x.GetInt32()).ToHashSet();
            var w = k.GetProperty("window").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            var c = k.GetProperty("colours").EnumerateArray().Select(x => (byte)x.GetInt32()).ToArray();
            var keys = new Queue<byte>(k.GetProperty("keys").EnumerateArray().Select(x => (byte)x.GetInt32()));
            var r = new ListWindowRoutine(rows, labels, (w[0], w[1], w[2], w[3]), (c[0], c[1], c[2]),
                k.GetProperty("exit").GetInt32() != 0, k.GetProperty("word").GetString()!);
            int cell = k.GetProperty("cell").GetInt32();
            var menus = new List<string>();
            (int Entry, byte Key) result = (-1, 0);
            if (r.Enter(k.GetProperty("top").GetInt32(), k.GetProperty("position").GetInt32(),
                    k.GetProperty("redraw").GetInt32() != 0, ref cell))
            {
                while (true)
                {
                    r.DrawLit(true);
                    string line = r.MenuLine();
                    menus.Add(line);
                    (byte Key, bool Ext)? got = null;
                    while (got is null)
                    {
                        byte ch = keys.Dequeue();
                        byte scan = ch == 0 ? keys.Dequeue() : (byte)0;
                        if (ch is >= (byte)'1' and <= (byte)'9')
                            digits++;
                        got = ListWindowRoutine.MenuKey(ch, scan, line, keypad: true, enterGives13: true, ref cell);
                    }
                    r.DrawLit(false);
                    if (r.Key(got.Value.Key, got.Value.Ext) is { } done)
                    {
                        result = done;
                        break;
                    }
                }
            }
            n++;
            string id = $"case {n - 1}";
            var want = (k.GetProperty("positionAfter").GetInt32(), k.GetProperty("topAfter").GetInt32(),
                k.GetProperty("cellAfter").GetInt32(), k.GetProperty("result").GetInt32(), k.GetProperty("key").GetInt32());
            var got2 = (r.Position, r.Top, cell, result.Entry, (int)result.Key);
            if (want != got2)
                fails.Add($"{id}: program {want}, port {got2}");
            var wantMenus = k.GetProperty("menus").EnumerateArray().Select(x => x.GetString()!).ToList();
            if (!wantMenus.SequenceEqual(menus))
                fails.Add($"{id}: menus program [{string.Join("|", wantMenus)}] port [{string.Join("|", menus)}]");
            if (wantMenus.Any(m => m.Contains("Next", StringComparison.Ordinal) || m.Contains("Prev", StringComparison.Ordinal)))
                paged++;
            var wantDraws = k.GetProperty("draws").EnumerateArray()
                .Where(e => e[0].GetString() is "s" or "g" or "b").Select(Parse).Select(Show).ToList();
            var gotDraws = r.Draws.Select(Show).ToList();
            if (!wantDraws.SequenceEqual(gotDraws))
            {
                int i = 0;
                while (i < wantDraws.Count && i < gotDraws.Count && wantDraws[i] == gotDraws[i])
                    i++;
                fails.Add($"{id}: draw {i}: program {wantDraws.ElementAtOrDefault(i)}, port {gotDraws.ElementAtOrDefault(i)}");
            }
        }
        Assert.True(n > 1200 && paged > 300 && digits > 100, $"{n} cases ({skipped} skipped), {paged} paged, {digits} digits");
        Assert.True(fails.Count == 0, $"{fails.Count} differ:\n" + string.Join("\n", fails.Take(25)));
    }

    private static ActionInput Key(params Keys[] keys)
    {
        var input = new ActionInput(InputBindings.Defaults());
        input.Update(new KeyboardState(keys), default, default, TimeSpan.Zero, _ => null);
        return input;
    }

    [Fact]
    public void The_keypad_flag_makes_the_digits_cursor_keys()
    {
        // DS:288C through the menu routine's key layer: '7' is Home's code, extended; '5' a space; '\' 0x37.
        int cell = 1;
        Assert.Equal(((byte)0x47, true), ListWindowRoutine.MenuKey((byte)'7', 0, " Next Exit", keypad: true, enterGives13: true, ref cell));
        Assert.Equal(((byte)0x20, true), ListWindowRoutine.MenuKey((byte)'5', 0, " Next Exit", keypad: true, enterGives13: true, ref cell));
        Assert.Equal(((byte)0x37, true), ListWindowRoutine.MenuKey((byte)'\\', 0, "", keypad: true, enterGives13: true, ref cell));
        Assert.Null(ListWindowRoutine.MenuKey((byte)'7', 0, " Next Exit", keypad: false, enterGives13: true, ref cell));
        // A letter is a hotkey only when the menu line holds it as a capital; it lights its word.
        Assert.Equal(((byte)'E', false), ListWindowRoutine.MenuKey((byte)'e', 0, " Next Exit", keypad: true, enterGives13: true, ref cell));
        Assert.Equal(2, cell);
        Assert.Null(ListWindowRoutine.MenuKey((byte)'x', 0, " Next Exit", keypad: true, enterGives13: true, ref cell));
        Assert.Equal(ListWindowRoutine.HomeCode, ListWindowRoutine.DigitRowKey(Key(Keys.D7)));
        Assert.Equal(ListWindowRoutine.PgDnCode, ListWindowRoutine.DigitRowKey(Key(Keys.D3)));
        Assert.Null(ListWindowRoutine.DigitRowKey(Key(Keys.LeftShift, Keys.D7)));
        Assert.Null(ListWindowRoutine.DigitRowKey(Key(Keys.D8)));
        // Both rows through the keypad flag (Speed, the rest time, the party menu, the member pick).
        Assert.Equal((byte)0x48, DosKeys.KeypadFlagCode(Key(Keys.D8)));
        Assert.Equal((byte)0x50, DosKeys.KeypadFlagCode(Key(Keys.NumPad2)));
        Assert.Equal((byte)0x47, DosKeys.KeypadFlagCode(Key(Keys.NumPad7)));
        Assert.Null(DosKeys.KeypadFlagCode(Key(Keys.D0)));
        Assert.Null(DosKeys.KeypadFlagCode(Key(Keys.A)));
    }

    [Fact]
    public void The_digits_row_moves_and_pages_a_list_window_as_the_keypad()
    {
        var rows = Enumerable.Range(0, 30).Select(i => $"ROW {i}").ToList();
        var r = new ListRequest([], rows, ["EXIT"], 0, FirstRow: 2) { Paging = new ListPaging("NEXT", "PREV", 10, 0) };
        var page = new ListPage();
        page.Sync(r);
        Assert.Equal(0, page.SelectedRow);
        page.Update(r, Key(Keys.D1));          // End: a row down
        Assert.Equal(1, page.SelectedRow);
        page.Update(r, Key(Keys.D7));          // Home: a row up
        page.Update(r, Key(Keys.D7));          // up from the window's top wraps to its last row
        Assert.Equal(9, page.SelectedRow);
        page.Update(r, Key(Keys.D3));          // PgDn while Next shows
        Assert.Equal(10, page.Top);
        Assert.Equal(19, page.SelectedRow);
        page.Update(r, Key(Keys.D9));          // PgUp while Prev shows
        Assert.Equal(0, page.Top);
    }
}
