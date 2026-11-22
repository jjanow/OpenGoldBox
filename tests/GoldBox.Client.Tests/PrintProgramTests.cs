using System.Text.Json;
using GoldBox.Client.Play;
using GoldBox.Core.Ecl;
using GoldBox.Data.Por.Dosbox;

namespace GoldBox.Client.Tests;

/// <summary>
/// DOS's text routine against the program itself: the fixture <c>program-v1.3/print.json</c> (private, from
/// <c>tools/scripts/por_print_oracle.py</c>) holds START 0x77F8 run under emulation on made-up strings, boxes and cursors
/// (every glyph, box clear, page prompt and Delay it made, the cursor after, or that it never returned) and the ECL text
/// unpacker GAME.OVR 0x8626 on long texts. <see cref="TextWindow"/>'s DOS print must leave the same screen cells, cursor,
/// prompts and typing waits, and hang where the program hangs.
/// </summary>
public class PrintProgramTests
{
    private static readonly string? File_ = PorGoldenCaptures.FindFixtures(AppContext.BaseDirectory) is { } d
        ? Path.Combine(d, "..", "program-v1.3", "print.json") : null;

    public sealed class OracleFactAttribute : FactAttribute
    {
        public OracleFactAttribute()
        {
            if (File_ is null || !File.Exists(File_))
                Skip = "needs the private fixture program-v1.3/print.json";
        }
    }

    private static int[] Ints(JsonElement e) => [.. e.EnumerateArray().Select(x => x.GetInt32())];

    [OracleFact]
    public void Print_matches_0x77F8_under_emulation()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(File_!));
        var fails = new List<string>();
        int n = 0, hung = 0, below = 0;
        foreach (var k in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var box = Ints(k.GetProperty("box"));
            var cursor = Ints(k.GetProperty("cursor"));
            byte colour = (byte)k.GetProperty("colour").GetInt32();
            bool clear = k.GetProperty("clear").GetInt32() != 0;
            bool typing = k.GetProperty("typing").GetInt32() != 0;
            var raw = Convert.FromHexString(k.GetProperty("text").GetString()!);
            string text = new([.. raw.Select(b => (char)b)]);
            bool wantHung = k.GetProperty("hung").GetBoolean();

            // The program's screen after the events: glyph UpCase(c) mod 64 at (column, row); a box clear blanks.
            var want = new (char, byte)?[25, 40];
            int glyphs = 0, delays = 0, prompts = 0;
            foreach (var ev in k.GetProperty("events").EnumerateArray())
            {
                switch (ev[0].GetString())
                {
                    case "g":
                        glyphs++;
                        int col = ev[1].GetInt32(), row = ev[2].GetInt32();
                        if (col <= 39 && row <= 24) // 0x726F draws nothing past the screen (0x7275–0x728A)
                            want[row, col] = (TextWindow.DosGlyph((char)ev[3].GetInt32()), (byte)ev[4].GetInt32());
                        break;
                    case "c":
                        for (int r = ev[2].GetInt32(); r <= Math.Min(24, ev[4].GetInt32()); r++)
                            for (int c = ev[1].GetInt32(); c <= Math.Min(39, ev[3].GetInt32()); c++)
                                want[r, c] = null;
                        break;
                    case "p":
                        prompts++;
                        Assert.Equal(13, ev[1].GetInt32());
                        break;
                    case "d":
                        delays++;
                        break;
                }
            }

            // The area: the box when it is a real one, else the whole screen.
            var (x0, y0, x1, y1) = (box[0], box[1], box[2], box[3]);
            var t = x0 <= x1 && y0 <= y1 && x1 <= 39 && y1 <= 24 ? new TextWindow(x0, y0, x1, y1) : new TextWindow(0, 0, 39, 24);
            t.Amiga = false;
            t.OriginalWordEndHang = true; // the program's own hang (fixed in play, s48)
            t.MoveTo(cursor[0], cursor[1]);
            int typed = 0, paused = 0;
            bool hang = false;
            t.Typed = () => typed++;
            t.Pause = () => paused++;
            t.Hang = () => hang = true;
            t.PrintIn(x0, y0, x1, y1, text, clear, colour);

            string id = $"case {n} box ({x0},{y0})-({x1},{y1}) cursor ({cursor[0]},{cursor[1]}) length {text.Length}";
            if (hang != wantHung)
                fails.Add($"{id}: hung {hang}, program {wantHung}");
            if (paused != prompts)
                fails.Add($"{id}: {paused} page prompts, program {prompts}");
            if (!wantHung)
            {
                if (typing && typed != delays)
                    fails.Add($"{id}: {typed} typing waits, program {delays}");
                if (typed != glyphs)
                    fails.Add($"{id}: {typed} glyphs, program {glyphs}");
                var after = Ints(k.GetProperty("after"));
                if (t.Column != after[0] || t.Row != after[1])
                    fails.Add($"{id}: cursor ({t.Column},{t.Row}), program ({after[0]},{after[1]})");
            }
            else
                hung++;
            for (int r = 0; r <= 24; r++)
                for (int c = 0; c <= 39; c++)
                {
                    var (ch, co) = t.Cell(r, c);
                    var got = ch == '\0' ? ((char, byte)?)null : (ch, co);
                    if (got != want[r, c])
                        fails.Add($"{id}: cell ({c},{r}) {got}, program {want[r, c]}");
                    if (got is not null && r == y1 + 1 && y1 == TextWindow.Bottom)
                        below++;
                }
            n++;
        }
        Assert.True(n >= 400 && hung > 40 && below > 0, $"{n} cases, {hung} hung, {below} cells below the box");
        Assert.True(fails.Count == 0, string.Join("\n", fails.Take(30)));
    }

    /// <summary>
    /// The original bug fixed (user, s48; view3d.md "Text window" step 4): a text of 255 characters or more no longer
    /// hangs after its last word. It is cut to 255 (string[255]) and printed whole, the cursor after it; with
    /// <see cref="TextWindow.OriginalWordEndHang"/> the program's hang is still there.
    /// </summary>
    [Theory]
    [InlineData(254)]
    [InlineData(255)]
    [InlineData(300)]
    public void A_255_character_text_prints_and_returns(int length)
    {
        // Words of four letters and a space, the text's last character an E (a word ending at the text's end).
        string text = string.Concat(Enumerable.Repeat("ABCD ", 80))[..(length - 1)] + "E";
        int glyphs = Math.Min(length, TextWindow.MaxText);
        foreach (bool original in new[] { false, true })
        {
            var t = new TextWindow(0, 0, 39, 24) { Amiga = false, OriginalWordEndHang = original };
            bool hang = false;
            int typed = 0;
            t.Hang = () => hang = true;
            t.Typed = () => typed++;
            t.PrintIn(0, 0, 39, 24, text, true, 10);
            if (original && length >= 255)
            {
                Assert.True(hang);
                continue;
            }
            Assert.False(hang);
            // Every character drawn once (spaces at a wrap are skipped, so a few fewer than the text's length), and
            // the last cell drawn is the cut text's last character.
            Assert.InRange(typed, glyphs - 10, glyphs);
            char lastWanted = text[glyphs - 1];
            Assert.Equal(lastWanted, t.Cell(t.Row, t.Column - 1).Char);
        }
    }

    [OracleFact]
    public void Ecl_text_slots_keep_255_characters_as_0x8626()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(File_!));
        foreach (var k in doc.RootElement.GetProperty("decode").EnumerateArray())
        {
            var packed = Convert.FromHexString(k.GetProperty("packed").GetString()!);
            var want = Convert.FromHexString(k.GetProperty("text").GetString()!);
            Assert.Equal(k.GetProperty("length").GetInt32(), want.Length);
            string got = EclText.Slot(EclText.Unpack(packed));
            Assert.Equal(new string([.. want.Select(b => (char)b)]), got);
        }
    }
}
