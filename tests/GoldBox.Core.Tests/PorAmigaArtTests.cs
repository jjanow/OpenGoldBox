using GoldBox.Core.Tests.Support;
using GoldBox.Data.Dax;
using GoldBox.Data.Module;
using GoldBox.Data.Module.Por;
using GoldBox.Data.Por;
using Xunit.Abstractions;

namespace GoldBox.Core.Tests;

/// <summary>
/// The Amiga art overlay (docs/module-format.md §10, docs/re/por-art-formats.md): the Amiga readers on made-up data,
/// then the import of the player's Amiga disks (skipped without <c>GOLDBOX_POR_DIR</c> and <c>GOLDBOX_POR_AMIGA_DIR</c>).
/// </summary>
public class PorAmigaArtTests(ITestOutputHelper output)
{
    private static DaxPicture Sample(int w, int h, int items, int seed = 1)
    {
        var rnd = new Random(seed);
        return DaxPicture.Create(w, h, 3, -2, Enumerable.Range(0, items).Select(_ =>
        {
            var px = new byte[w * h];
            for (int i = 0; i < px.Length; i++)
                px[i] = (byte)rnd.Next(16);
            return px;
        }));
    }

    [Fact]
    public void Planar_blocks_decode_what_they_encode_including_odd_byte_widths()
    {
        foreach (var (w, h, n) in new[] { (88, 88, 1), (16, 8, 35), (56, 40, 1), (24, 24, 3) })
        {
            var pic = Sample(w, h, n);
            var block = AmigaPicture.Encode(pic);
            Assert.Equal(AmigaPicture.HeaderSize + 4 * h * ((w / 8 + 1) & ~1) * n, block.Length);
            var back = AmigaPicture.DecodeFrames(block);
            Assert.Single(back);
            Assert.Equal((w, h, 3, -2), (back[0].Width, back[0].Height, back[0].X, back[0].Y));
            Assert.Equal(pic.Items, back[0].Items);
        }
    }

    [Fact]
    public void Animations_keep_frames_and_delays()
    {
        var frames = new[] { Sample(40, 48, 1, 1), Sample(32, 40, 1, 2) }
            .Select((f, i) => DaxPicture.Create(f.Width, f.Height, f.X, f.Y, f.Items, delay: (uint)(10 + i))).ToList();
        var block = AmigaPicture.EncodeAnimation(frames);
        Assert.True(AmigaPicture.IsAnimation(block));
        var back = AmigaPicture.DecodeFrames(block);
        Assert.Equal([10u, 11u], back.Select(f => f.Delay!.Value));
        Assert.Equal(frames[1].Items, back[1].Items);
        Assert.False(AmigaPicture.IsAnimation(AmigaPicture.Encode(frames[0])));
    }

    [Fact]
    public void Packed_tiles_split_left_to_right_and_join_back()
    {
        var pic = Sample(16, 8, 3);
        var tiles = AmigaPicture.SplitTiles(pic, 8);
        Assert.Equal((8, 6), (tiles.Width, tiles.Items.Count));
        Assert.Equal(pic.Items[1][8], tiles.Items[3][0]);   // item 1, right half, row 0, column 0
        Assert.Equal(pic.Items[1][16 + 7], tiles.Items[2][8 + 7]);
        Assert.Equal(pic.Items, AmigaPicture.JoinTiles(tiles, 2).Items);
    }

    [Fact]
    public void Twelve_bit_colours_widen_nibble_times_17()
    {
        Assert.Equal(new byte[] { 0x00, 0x66, 0xBB, 0xFF, 0xFF, 0xFF }, AmigaHunks.Rgb12To24([0x00, 0x6B, 0x0F, 0xFF], 2));
    }

    [Fact]
    public void Hunk_files_list_code_data_and_bss_with_their_data_offsets()
    {
        static byte[] L(params uint[] v) => [.. v.SelectMany(x => new[] { (byte)(x >> 24), (byte)(x >> 16), (byte)(x >> 8), (byte)x })];
        var file = L(0x3F3, 0, 3, 0, 2, 1, 1, 2)                     // header: no libraries, 3 hunks 0..2, sizes
            .Concat(L(0x3E9, 1, 0x4E754E75)).Concat(L(0x3F2))       // CODE, 1 long
            .Concat(L(0x3EA, 1, 0x0FFF0000)).Concat(L(0x3EC, 1, 0, 0, 0)).Concat(L(0x3F2))   // DATA + one reloc
            .Concat(L(0x3EB, 2)).Concat(L(0x3F2)).ToArray();
        var h = AmigaHunks.Parse(file);
        Assert.Equal([AmigaHunks.Code, AmigaHunks.Data, AmigaHunks.Bss], h.Select(x => x.Type));
        Assert.Equal(0x0F, file[h[1].DataOffset!.Value]);
        Assert.Null(h[2].DataOffset);
        Assert.Equal(8, h[2].Size);
    }

    /// <summary>Packs PowerPacker bits (in the order the decruncher reads them) into a stream: longs from the end, LSB first.</summary>
    private static byte[] PowerPackerStream(IReadOnlyList<int> bits, int length)
    {
        var longs = new List<uint>();
        for (int i = 0; i < bits.Count; i += 32)
        {
            uint v = 0;
            for (int k = 0; k < 32 && i + k < bits.Count; k++)
                v |= (uint)bits[i + k] << k;
            longs.Add(v);
        }
        longs.Add(0); // the decruncher loads the next long as soon as one is used up
        longs.Reverse();
        longs.Add((uint)length << 8); // skip 0
        return [.. longs.SelectMany(l => new[] { (byte)(l >> 24), (byte)(l >> 16), (byte)(l >> 8), (byte)l })];
    }

    private static IEnumerable<int> Msb(int value, int n) => Enumerable.Range(0, n).Select(i => value >> (n - 1 - i) & 1);

    [Fact]
    public void PowerPacker_decrunches_literals_and_matches_from_the_end()
    {
        // A literal run of 2 bytes ('B' then 'A', written from the end), then a match: index 0 (length 2), offset 1.
        var bits = new List<int> { 0 };
        bits.AddRange(Msb(1, 2));            // count 1 → 2 literals
        bits.AddRange(Msb('B', 8));
        bits.AddRange(Msb('A', 8));
        bits.AddRange(Msb(0, 2));            // match index 0
        bits.AddRange(Msb(1, 9));            // offset 1 (9 bits for index 0)
        var o = AmigaIntro.PowerPacker(PowerPackerStream(bits, 4), [9, 9, 9, 9]);
        Assert.Equal("ABAB"u8.ToArray(), o);
        Assert.Throws<DaxFormatException>(() => AmigaIntro.PowerPacker(new byte[4], [9, 9, 9, 9]));
    }

    [Fact]
    public void Planar_pictures_take_plane_k_as_bit_k_left_pixel_first()
    {
        // 16×1, 4 planes of 2 bytes: pixel x has plane bits from x.
        byte[] planes = [0b1010_1010, 0x00, 0b1100_1100, 0x00, 0b1111_0000, 0x00, 0x00, 0xFF];
        var px = AmigaIntro.Planar(planes, 16, 1);
        Assert.Equal(new byte[] { 7, 6, 5, 4, 3, 2, 1, 0, 8, 8, 8, 8, 8, 8, 8, 8 }, px);
        Assert.Throws<DaxFormatException>(() => AmigaIntro.Planar(planes, 16, 2));
        Assert.Throws<DaxFormatException>(() => AmigaIntro.Title(new byte[16]));
    }

    [Fact]
    public void Best_uses_amiga_for_every_category_as_decided()
    {
        var d = PorAmigaArt.BestDefaults();
        Assert.Equal(["combatGround", "font", "icon", "picture", "portrait", "sprite", "title", "wall"], d.Keys);
        Assert.All(d.Values, v => Assert.Equal("amiga", v));
        Assert.Equal(4, PorAmigaArt.TableFor("final", 2));
        Assert.Equal(3, PorAmigaArt.TableFor("final", 7));
        Assert.Null(PorAmigaArt.Category("walldef"));
    }

    // ---- party icons (por-art-formats.md §4 "Party icons") ----

    /// <summary>Pairs in the program's shape: pair k holds the pens of DOS pair k, the dark one low when <paramref name="darkLow"/>.</summary>
    private static byte[] Pairs(Func<int, bool> darkLow) =>
    [
        .. PorAmigaArt.DosIconPairs.Select((c, k) =>
        {
            byte dark = PorAmigaArt.IconPen(c)!.Value, light = PorAmigaArt.IconPen(c + 8)!.Value;
            return darkLow(k) ? (byte)(light << 4 | dark) : (byte)(dark << 4 | light);
        }),
    ];

    [Fact]
    public void Dos_colour_bytes_move_to_the_amiga_nibbles_of_each_pair()
    {
        byte[] defaults = [0x91, 0xA2, 0xB3, 0xC4, 0xE6, 0xF7];   // DOS Create: (c + 8)·16 + c
        foreach (var pairs in new[] { Pairs(_ => true), Pairs(_ => false), Pairs(k => k % 2 == 0) })
        {
            Assert.True(PorAmigaArt.IconPairsAgree(pairs));
            // The DOS defaults become the pair bytes themselves: the Amiga's own defaults, the art's colours.
            Assert.Equal(pairs, PorAmigaArt.AmigaIconColours(defaults, pairs));
            Assert.Equal(Enumerable.Range(0, 16).Select(i => (byte)i), PorAmigaArt.IconColourMap(pairs, pairs));
            // The dark part of pair 1 (DOS index 1) in EGA red: the Amiga index of DOS 1 takes the pen of red.
            var map = PorAmigaArt.IconColourMap(PorAmigaArt.AmigaIconColours([0x94, 0xA2, 0xB3, 0xC4, 0xE6, 0xF7], pairs)!, pairs);
            Assert.Equal(PorAmigaArt.IconPen(4), map[PorAmigaArt.IconPen(1)!.Value]);
            Assert.Equal(PorAmigaArt.IconPen(9), map[PorAmigaArt.IconPen(9)!.Value]);
        }
        // EGA 5 and 13: the nearest table-3 pens, 6 and 7 (user, s28).
        var p = Pairs(_ => true);
        var m5 = PorAmigaArt.IconColourMap(PorAmigaArt.AmigaIconColours([0x95, 0xA2, 0xB3, 0xC4, 0xE6, 0xF7], p)!, p);
        Assert.Equal(6, m5[PorAmigaArt.IconPen(1)!.Value]);
        var m13 = PorAmigaArt.IconColourMap(PorAmigaArt.AmigaIconColours([0xD1, 0xA2, 0xB3, 0xC4, 0xE6, 0xF7], p)!, p);
        Assert.Equal(7, m13[PorAmigaArt.IconPen(9)!.Value]);
        var wrong = Pairs(_ => true);
        (wrong[0], wrong[1]) = (wrong[1], wrong[0]);
        Assert.False(PorAmigaArt.IconPairsAgree(wrong));
        Assert.Null(PorAmigaArt.AmigaIconColours(defaults, wrong));
    }

    [PorAmigaFact]
    public void The_programs_colour_pairs_agree_and_reach_the_module()
    {
        var (dir, report) = Imported.Value;
        var pairs = PorAmigaArt.IconPairs(File.ReadAllBytes(PorAmigaArt.Locate(PorAmigaData.Dir!).Program));
        Assert.NotNull(pairs);
        Assert.True(PorAmigaArt.IconPairsAgree(pairs));
        Assert.True(report.Amiga!.IconColours);
        Assert.Equal(pairs, PorModule.Open(dir).AmigaIconPairs());
        var r = PorAmigaArt.Check(PorAmigaData.Dir!, PorModule.Open(dir));
        Assert.Equal(1, r.Checked["AMIGA ICON COLOURS"]);
        Assert.Empty(r.Mismatches);
        // Not the program: no pairs.
        Assert.Null(PorAmigaArt.IconPairs(File.ReadAllBytes(PorAmigaArt.LocateIntro(PorAmigaData.Dir!) ?? PorAmigaArt.Locate(PorAmigaData.Dir!).Program)[..64]));
    }

    /// <summary>
    /// The pens of <see cref="PorAmigaArt.IconPen"/> are the art's: over every CHEAD / CBODY block both versions have,
    /// pixel by pixel, the most common non-zero Amiga index under each DOS colour (with at least 200 pixels) is its pen.
    /// </summary>
    [PorAmigaFact]
    public void The_icon_pens_are_what_the_amiga_icon_art_uses()
    {
        var module = PorModule.Open(Imported.Value.Dir);
        var counts = new int[16, 16];
        foreach (var file in new[] { "CHEAD", "CBODY" })
            for (int id = 0; id < 256; id++)
            {
                if (module.Picture(file, id) is not [{ Items: [var dos] } d]
                    || module.Art(file, id, PorArtChoice.Amiga) is not { Variant: "amiga", Frames: [{ Items: [var am] } a] }
                    || (d.Width, d.Height) != (a.Width, a.Height))
                    continue;
                for (int i = 0; i < dos.Length; i++)
                    counts[dos[i], am[i]]++;
            }
        int seen = 0;
        for (int e = 1; e < 16; e++)
        {
            int total = Enumerable.Range(0, 16).Sum(p => counts[e, p]);
            if (total < 200)
                continue;
            int best = Enumerable.Range(1, 15).MaxBy(p => counts[e, p]);
            output.WriteLine($"DOS {e}: Amiga {best} ({counts[e, best]} of {total})");
            Assert.Equal(PorAmigaArt.IconPen(e), (byte)best);
            seen++;
        }
        Assert.Equal(13, seen);                                   // 1–4, 6–12, 14, 15
    }

    /// <summary>EGA 5 and 13 (no pen in the art, table 3 has no magenta): the nearest table-3 pen by RGB (user, s28).</summary>
    [PorAmigaFact]
    public void Ega_5_and_13_take_the_nearest_table_3_pen()
    {
        var table3 = PorAmigaArt.Palettes(File.ReadAllBytes(PorAmigaArt.Locate(PorAmigaData.Dir!).Program))[3];
        var ega = GoldBox.Data.Module.IndexedPng.EgaPalette;
        foreach (int e in new[] { 5, 13 })
            Assert.Equal(PorAmigaArt.IconPen(e), PorAmigaArt.NearestIconPen(table3, ega[3 * e], ega[3 * e + 1], ega[3 * e + 2]));
    }

    // ---- the player's files ----

    private static readonly Lazy<(string Dir, PorImportReport Report)> Imported =
        new(() => Support.TestImportCache.Import("por-amiga-module", PorData.Dir!, PorAmigaData.Dir));

    [PorAmigaFact]
    public void The_overlay_holds_every_amiga_picture_and_rebuilds_byte_for_byte()
    {
        var (dir, report) = Imported.Value;
        var amiga = report.Amiga!;
        Assert.Equal(508, amiga.Pictures);
        Assert.Equal(["sprit 1", "sprit 9", "sprit 120"], amiga.Skipped);
        Assert.Empty(amiga.NotPictures);
        var r = PorAmigaArt.Check(PorAmigaData.Dir!, PorModule.Open(dir));
        output.WriteLine(string.Join(", ", r.Checked.Select(kv => $"{kv.Key} {kv.Value}")));
        Assert.Empty(r.Mismatches);
        Assert.Equal(508, r.Checked["AMIGA ART"]);
        var m = ModuleManifest.Load(dir);
        Assert.Equal(["dos", "amiga"], m.Art!.Variants);
        Assert.Contains(m.Source!.Inputs, i => i.File == "amiga/program");
    }

    [PorAmigaFact]
    public void The_overlay_holds_the_amiga_players_data()
    {
        var (dir, report) = Imported.Value;
        Assert.True(report.Amiga!.Sound);
        var path = Path.Combine(dir, AmigaSoundDocument.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var doc = (AmigaSoundDocument)System.Text.Json.JsonSerializer.Deserialize(File.ReadAllText(path), typeof(AmigaSoundDocument), ModuleSchemas.Strict)!;
        Assert.Equal(AmigaSoundDocument.DriverPor10, doc.Driver);
        var fromModule = PorModule.Open(dir).AmigaSound()!;
        var direct = PorAmigaSound.Read(File.ReadAllBytes(PorAmigaArt.Locate(PorAmigaData.Dir!).Program))!;
        Assert.Equal(direct.Music, fromModule.Music);
        Assert.Equal(direct.MusicRelocs, fromModule.MusicRelocs);
        Assert.Equal(direct.Effects, fromModule.Effects);
        Assert.Equal(direct.EffectRelocs, fromModule.EffectRelocs);
        Assert.Equal(direct.EffectSamples, fromModule.EffectSamples);
        Assert.Equal([5, 9, 8, 10, 12, 11, 6, 11, 4, 7, 0, 2, 2], fromModule.Cues);
        // A module without the overlay has none.
        Assert.Null(PorModule.Open(PorModuleData.Dir).AmigaSound());
    }

    [PorAmigaFact]
    public void A_program_that_is_not_amiga_por_has_no_players()
    {
        var program = File.ReadAllBytes(PorAmigaArt.Locate(PorAmigaData.Dir!).Program);
        Assert.NotNull(PorAmigaSound.Read(program));
        // One operand moved: the song table's lea no longer points at hunk 28 +0x197C.
        var hunks = AmigaHunks.Parse(program);
        int at = hunks[PorAmigaSound.CodeHunk].DataOffset!.Value + 0x78F2;
        var changed = (byte[])program.Clone();
        changed[at + 3] ^= 0x10;
        Assert.Null(PorAmigaSound.Read(changed));
        Assert.Null(PorAmigaSound.Read(new byte[64]));
    }

    [PorAmigaFact]
    public void Every_overlay_sidecar_reads_strictly()
    {
        var root = Path.Combine(Imported.Value.Dir, "art", "amiga");
        var files = Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Where(p => !p.EndsWith(Path.GetFileName(AmigaIconColoursDocument.RelativePath), StringComparison.Ordinal)).ToList();
        bool title = Imported.Value.Report.Amiga!.Title;
        Assert.Equal(PorAmigaArt.LocateIntro(PorAmigaData.Dir!) is not null, title);
        Assert.Equal(title ? PorAmigaArt.PanelCount : 0, Imported.Value.Report.Amiga!.IntroPanels);
        bool runes = Imported.Value.Report.Amiga!.RuneFont;
        Assert.Equal(PorAmigaData.RuneFont is not null, runes);
        Assert.Equal(508 + (title ? 1 + PorAmigaArt.PanelCount : 0) + (runes ? 1 : 0), files.Count);
        foreach (var path in files)
        {
            var doc = (ArtDocument)System.Text.Json.JsonSerializer.Deserialize(File.ReadAllText(path), typeof(ArtDocument), ModuleSchemas.Strict)!;
            // The title painting carries the intro's own palette, the rune font the interface colours 16–31, not one
            // of the program's five tables.
            Assert.Equal(doc.Source!.Container is not (PorAmigaArt.IntroContainer or PorAmigaRunes.Container + ".font"),
                doc.PaletteTable is not null);
            Assert.NotNull(doc.Frames);
        }
    }

    [PorAmigaIntroFact]                                           // skipped, naming the file, without disk A's intro
    public void The_intro_panels_are_read_from_its_code()
    {
        var intro = File.ReadAllBytes(PorAmigaData.Intro!);
        // hunk 4 +0x1A … +0x1E8: sheet row, screen row, rows, and the second fade for the logo panel only
        Assert.Equal([new AmigaIntro.Panel(93, 30, 48, false), new AmigaIntro.Panel(455, 60, 16, false),
            new AmigaIntro.Panel(398, 30, 56, false), new AmigaIntro.Panel(474, 60, 19, false),
            new AmigaIntro.Panel(141, 20, 146, false), new AmigaIntro.Panel(0, 20, 92, true),
            new AmigaIntro.Panel(288, 20, 110, false), new AmigaIntro.Panel(499, 20, 100, false)], AmigaIntro.Panels(intro));
        var screens = AmigaIntro.PanelScreens(intro);
        var (_, titlePalette) = AmigaIntro.Title(intro);
        foreach (var (panel, px, pal) in screens)
        {
            Assert.Equal(AmigaIntro.Width * AmigaIntro.Height, px.Length);
            // black (index 0) outside the panel's rows, something drawn inside
            Assert.All(px.Take(panel.ScreenRow * AmigaIntro.Width), v => Assert.Equal(0, v));
            Assert.All(px.Skip((panel.ScreenRow + panel.Rows) * AmigaIntro.Width), v => Assert.Equal(0, v));
            Assert.Contains(px.Skip(panel.ScreenRow * AmigaIntro.Width).Take(panel.Rows * AmigaIntro.Width), v => v != 0);
            Assert.Equal(!panel.Second, pal.AsSpan().SequenceEqual(titlePalette));
        }
        var module = PorModule.Open(Imported.Value.Dir);
        for (int i = 0; i < PorAmigaArt.PanelCount; i++)
        {
            var a = module.Art("INTRO", PorAmigaArt.FirstPanelId + i, PorArtChoice.Best)!;
            Assert.Equal("amiga", a.Variant);
            Assert.Equal(screens[i].Pixels, a.Frames[0].Items[0]);
            Assert.Equal(screens[i].PaletteRgb, a.PaletteRgb);
        }
        Assert.Null(module.Art("INTRO", PorAmigaArt.FirstPanelId, PorArtChoice.Dos));
    }

    [PorAmigaFact]
    public void Art_picks_the_variant_and_falls_back_to_dos()
    {
        var (dir, _) = Imported.Value;
        var module = PorModule.Open(dir);
        var palettes = PorAmigaArt.Palettes(File.ReadAllBytes(PorAmigaArt.Locate(PorAmigaData.Dir!).Program));

        var dosPic = module.Art("PIC1", 1, PorArtChoice.Dos)!;
        Assert.Equal("dos", dosPic.Variant);
        Assert.Equal(IndexedPng.EgaPalette, dosPic.PaletteRgb);
        Assert.Equal(module.Picture("PIC1", 1)!.Count, dosPic.Frames.Count);

        foreach (var choice in new[] { PorArtChoice.Best, PorArtChoice.Amiga })
        {
            var a = module.Art("PIC1", 1, choice)!;
            Assert.Equal("amiga", a.Variant);
            Assert.Equal(palettes[4], a.PaletteRgb);
            // the same Amiga asset serves every disk's bank
            Assert.Equal(a.Frames[0].Items, module.Art("PIC5", 1, choice)!.Frames[0].Items);
        }
        // 8X8D: split to DOS's 70 8×8 tiles, drawn with table 3
        var walls = module.Art("8X8D1", 1, PorArtChoice.Amiga)!;
        Assert.Equal((8, 8, 70), (walls.Frames[0].Width, walls.Frames[0].Height, walls.Frames[0].Items.Count));
        Assert.Equal(palettes[3], walls.PaletteRgb);
        // DOS-only assets and left-out placeholders fall back to DOS
        Assert.Equal("dos", module.Art(DosFile(module, "PIC", 118), 118, PorArtChoice.Amiga)!.Variant);
        Assert.Equal("dos", module.Art(DosFile(module, "SPRIT", 1), 1, PorArtChoice.Amiga)!.Variant);
        Assert.Equal(3, module.Art(DosFile(module, "SPRIT", 120), 120, PorArtChoice.Best)!.Frames.Count(f => f.Height > 1));
        // the parity readers stay DOS
        Assert.Equal(module.Picture(DosFile(module, "SPRIT", 0), 0)!.Count,
            module.Art(DosFile(module, "SPRIT", 0), 0, PorArtChoice.Dos)!.Frames.Count);
    }

    private static string DosFile(PorModule module, string container, int id) =>
        Enumerable.Range(1, 8).Select(d => container + d).First(f => module.Picture(f, id) is not null);

    // ---- the rune font (docs/re/ecl-format.md "PROTECT", PorAmigaRunes) ----

    /// <summary>
    /// A made-up disk font: characters 0x41 (8 wide, bit y of row y), 0x42 (4 wide, every row full) and the slot after
    /// them (row 0 full), in one bitmap of 3 bytes a row, as the AmigaOS layout stores them.
    /// </summary>
    private static byte[] FontFile(int baseline = 6, int flags = 0x42)
    {
        var h = new byte[148];
        void W(int at, int v) { h[at] = (byte)(v >> 8); h[at + 1] = (byte)v; }
        void L(int at, int v) { W(at, v >> 16); W(at + 2, v); }
        L(0, 0x70004E75);                       // moveq #0, d0; rts
        W(18, AmigaDiskFont.FileId);
        int m = 78;                             // the TextFont's metrics
        W(m, 8); h[m + 3] = (byte)flags; W(m + 4, 8); W(m + 6, baseline);
        h[m + 12] = 0x41; h[m + 13] = 0x42;
        L(m + 14, 110); W(m + 18, 3); L(m + 20, 134);
        for (int y = 0; y < 8; y++)
        {
            h[110 + 3 * y] = (byte)(0x80 >> y);
            h[110 + 3 * y + 1] = 0xF0;
            h[110 + 3 * y + 2] = (byte)(y == 0 ? 0xFF : 0);
        }
        W(134, 0); W(136, 8); W(138, 8); W(140, 4); W(142, 16); W(144, 8);
        static byte[] B(params uint[] v) => [.. v.SelectMany(x => new[] { (byte)(x >> 24), (byte)(x >> 16), (byte)(x >> 8), (byte)x })];
        return [.. B(0x3F3, 0, 1, 0, 0, (uint)h.Length / 4, 0x3E9, (uint)h.Length / 4), .. h, .. B(0x3F2)];
    }

    [Fact]
    public void Disk_fonts_draw_each_character_from_its_slot_and_others_from_the_one_after_hichar()
    {
        var f = AmigaDiskFont.Parse(FontFile());
        Assert.Equal((8, 8, 6, 0x41, 0x42), (f.YSize, f.XSize, f.Baseline, f.LoChar, f.HiChar));
        Assert.True(f.Pixel(0x41, 3, 3));
        Assert.False(f.Pixel(0x41, 4, 3));
        Assert.True(f.Pixel(0x42, 3, 5));
        Assert.False(f.Pixel(0x42, 4, 5));      // past its width: the next character's bits are not drawn
        Assert.Equal(2, f.Slot(0x20));
        Assert.Equal(2, f.Slot(0x43));
        Assert.True(f.Pixel(0, 7, 0));
        Assert.False(f.Pixel(0, 7, 1));
        Assert.Throws<DaxFormatException>(() => AmigaDiskFont.Parse(FontFile()[..40]));

        // The cells as the glyph routine draws them: pen 28 is index 12 of the interface run, the glyph's top line at
        // row · 8 + 6 − baseline; a proportional font is not drawn this way.
        var cells = PorAmigaRunes.Cells(f, 28)!;
        Assert.Equal(256, cells.Length);
        Assert.Equal(12, cells[0x41][3 * 8 + 3]);
        Assert.Equal(0, cells[0x41][3 * 8 + 4]);
        Assert.Equal(cells[0], cells[0xFF]);
        var lower = PorAmigaRunes.Cells(AmigaDiskFont.Parse(FontFile(baseline: 5)), 28)!;
        Assert.Equal(12, lower[0x41][4 * 8 + 3]);
        Assert.All(lower[0x41][..8], v => Assert.Equal(0, v));
        Assert.Null(PorAmigaRunes.Cells(AmigaDiskFont.Parse(FontFile(flags: 0x62)), 28));

        // The glyph routine's colour → pen: below 16 the even pen of the interface pair.
        Assert.Equal([16, 16, 30, 30, 26, 28], new[] { 0, 1, 14, 15, 10, 28 }.Select(PorAmigaRunes.ScreenPen));
    }

    [Fact]
    public void Font_contents_files_list_each_size_s_file()
    {
        var c = new byte[4 + 260];
        c[0] = 0x0F; c[3] = 1;
        "runes/8"u8.CopyTo(c.AsSpan(4));
        c[4 + 257] = 8;
        Assert.Equal([("runes/8", 8)], AmigaDiskFont.Contents(c));
        Assert.Throws<DaxFormatException>(() => AmigaDiskFont.Contents(new byte[8]));
    }

    [PorAmigaFact]
    public void The_program_opens_the_rune_font_and_draws_in_pen_28()
    {
        var program = File.ReadAllBytes(PorAmigaArt.Locate(PorAmigaData.Dir!).Program);
        var s = PorAmigaRunes.Read(program)!;
        Assert.Equal(("protect.font", 8, 28), (s.FontName, s.YSize, s.Pen));
        // Registers 16–31 as start-up loads them (hunk 2 words 16–31).
        var hunks = AmigaHunks.Parse(program);
        Assert.Equal(AmigaHunks.Rgb12To24(program.AsSpan(hunks[2].DataOffset!.Value + 32), 16), s.InterfaceRgb);
        // PROTECT's colour push moved: nothing is taken.
        var changed = (byte[])program.Clone();
        changed[hunks[16].DataOffset!.Value + 0x4CCE] ^= 1;
        Assert.Null(PorAmigaRunes.Read(changed));
        Assert.Null(PorAmigaRunes.Read(new byte[64]));
    }

    [PorAmigaRuneFontFact]                                        // skipped, naming the files, without disk A's fonts
    public void The_rune_font_is_the_dos_runes_redrawn_and_reaches_the_module()
    {
        var (dir, report) = Imported.Value;
        Assert.True(report.Amiga!.RuneFont);
        var module = PorModule.Open(dir);
        var r = PorAmigaArt.Check(PorAmigaData.Dir!, module);
        Assert.Empty(r.Mismatches);
        Assert.Equal(1, r.Checked["AMIGA RUNE FONT"]);
        var setup = PorAmigaRunes.Read(File.ReadAllBytes(PorAmigaArt.Locate(PorAmigaData.Dir!).Program))!;
        foreach (var choice in new[] { PorArtChoice.Best, PorArtChoice.Amiga })
        {
            var a = module.Art(PorAmigaRunes.Container, PorAmigaRunes.Id, choice)!;
            Assert.Equal("amiga", a.Variant);
            Assert.Equal(setup.InterfaceRgb, a.PaletteRgb);
        }
        Assert.Null(module.Art(PorAmigaRunes.Container, PorAmigaRunes.Id, PorArtChoice.Dos));
        var cells = module.Art(PorAmigaRunes.Container, PorAmigaRunes.Id, PorArtChoice.Amiga)!.Frames.Single();
        Assert.Equal((8, 8, 256), (cells.Width, cells.Height, cells.Items.Count));
        Assert.All(cells.Items, c => Assert.All(c, v => Assert.True(v is 0 or 12)));
        // Against the DOS text font (8X8D1 block 201): the glyphs the rune lines use (65–139 in v1.3) are the same
        // bits but four, which the Amiga artist redrew (two of them, 79 and 80, are in ECL4 block 21 and ECL5 block 4).
        var dos = module.ArtData("8X8D1", 201)!;
        var differ = Enumerable.Range(65, 75).Where(g => Enumerable.Range(0, 64)
            .Any(i => (cells.Items[g][i] != 0) != ((dos[g * 8 + i / 8] & (0x80 >> (i % 8))) != 0))).ToList();
        Assert.Equal([75, 79, 80, 117], differ);
    }
}
