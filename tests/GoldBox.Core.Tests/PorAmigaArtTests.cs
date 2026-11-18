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

    [Fact]
    public void Best_uses_amiga_for_every_category_as_decided()
    {
        var d = PorAmigaArt.BestDefaults();
        Assert.Equal(["combatGround", "icon", "picture", "portrait", "sprite", "wall"], d.Keys);
        Assert.All(d.Values, v => Assert.Equal("amiga", v));
        Assert.Equal(4, PorAmigaArt.TableFor("final", 2));
        Assert.Equal(3, PorAmigaArt.TableFor("final", 7));
        Assert.Null(PorAmigaArt.Category("walldef"));
    }

    // ---- the player's files ----

    private static readonly Lazy<(string Dir, PorImportReport Report)> Imported = new(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "goldbox-tests", $"por-amiga-module-{Guid.NewGuid():N}");
        return (dir, PorImporter.Import(PorData.Dir!, dir, PorAmigaData.Dir));
    });

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
    public void Every_overlay_sidecar_reads_strictly()
    {
        var root = Path.Combine(Imported.Value.Dir, "art", "amiga");
        var files = Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories).ToList();
        Assert.Equal(508, files.Count);
        foreach (var path in files)
        {
            var doc = (ArtDocument)System.Text.Json.JsonSerializer.Deserialize(File.ReadAllText(path), typeof(ArtDocument), ModuleSchemas.Strict)!;
            Assert.NotNull(doc.PaletteTable);
            Assert.NotNull(doc.Frames);
        }
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
}
