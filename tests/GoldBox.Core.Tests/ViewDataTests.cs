using System.Security.Cryptography;
using GoldBox.Core.Explore;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Dax;
using GoldBox.Data.Por;

namespace GoldBox.Core.Tests;

public class DaxPictureTests
{
    /// <summary>A DaxBlock header + items: height, width in 8-px units, x, y, count, 8-byte CGA map.</summary>
    private static byte[] Block(int h, int w8, int count, Func<int, byte> fill)
    {
        var b = new List<byte>();
        b.AddRange(BitConverter.GetBytes((ushort)h));
        b.AddRange(BitConverter.GetBytes((ushort)w8));
        b.AddRange(BitConverter.GetBytes((short)3));
        b.AddRange(BitConverter.GetBytes((short)-4));
        b.Add((byte)count);
        b.AddRange(new byte[8]);
        for (int i = 0; i < count * h * w8 * 4; i++)
            b.Add(fill(i));
        return [.. b];
    }

    [Fact]
    public void Items_unpack_two_pixels_per_byte_high_nibble_first()
    {
        var pic = DaxPicture.Decode(Block(8, 1, 2, i => (byte)(i < 32 ? 0x1D : 0x2F)));
        Assert.Equal((8, 8, 3, -4), (pic.Width, pic.Height, pic.X, pic.Y));
        Assert.Equal(2, pic.Items.Count);
        Assert.Equal([1, 13], pic.Items[0][..2]);
        Assert.Equal([2, 15], pic.Items[1][..2]);
        var tiles = pic.AsTiles();
        Assert.Equal(128, tiles.Length);
        Assert.Equal(15, tiles[64 + 1]);
    }

    [Fact]
    public void Trailing_bytes_and_short_blocks_are_rejected()
    {
        Assert.Throws<DaxFormatException>(() => DaxPicture.Decode([.. Block(8, 1, 1, _ => 0), 0]));
        Assert.Throws<DaxFormatException>(() => DaxPicture.Decode(Block(8, 1, 1, _ => 0)[..40]));
        Assert.Throws<DaxFormatException>(() => DaxPicture.Decode(Block(8, 2, 1, _ => 0)).AsTiles());
    }

    [Fact]
    public void Animation_wrapper_is_recognised()
    {
        var frame = Block(2, 1, 1, _ => 0x11);
        byte[] anim = [2, 5, 0, 0, 0, .. frame, 7, 0, 0, 0, .. frame];
        Assert.True(DaxPicture.IsAnimation(anim));
        var frames = DaxPicture.DecodeFrames(anim);
        Assert.Equal(2, frames.Count);
        Assert.Equal(7u, frames[1].Delay);
        Assert.False(DaxPicture.IsAnimation(frame));
        Assert.Single(DaxPicture.DecodeFrames(frame));
    }
}

public class PorGeoTests
{
    [Fact]
    public void Blocks_must_be_1026_bytes_with_the_0400_header()
    {
        var block = new byte[PorGeo.BlockSize];
        block[1] = 0x04;
        block[2] = 0x35; // plane 0, square 0,0: N wall 3, E wall 5
        var m = PorGeo.Parse(block, 7);
        Assert.Equal(7, m.Id);
        Assert.Equal(3, m.Wall(0, 0, Facing.North));
        Assert.Equal(5, m.Wall(0, 0, Facing.East));
        Assert.Throws<FormatException>(() => PorGeo.Parse(block[..1025]));
        block[1] = 0x03;
        Assert.Throws<FormatException>(() => PorGeo.Parse(block));
    }
}

public class PorStartExeTests
{
    /// <summary>A made-up program image holding a layout table with the program's structure (not its values).</summary>
    private static byte[] Image(int at, bool twice = false)
    {
        var b = new byte[600];
        new Random(5).NextBytes(b);
        void Put(int p)
        {
            byte[] start = [0, 12, 24, 36, 48, 60, 72, 84, 96, 108];
            byte[] wh = [3, 3, 3, 3, 3, 3, 3, 3, 3, 6, 4, 4, 4, 4, 4, 4, 4, 4, 4, 8];
            start.CopyTo(b, p);
            wh.CopyTo(b, p + 10);
            for (int i = 0; i < 20; i++)
                BitConverter.GetBytes((short)(i % 10)).CopyTo(b, p + 30 + 2 * i);
            for (int i = 0; i < 16; i++)
                b[p - 102 + i] = (byte)(15 - i);
        }
        Put(at);
        if (twice)
            Put(at + 200);
        return b;
    }

    [Fact]
    public void Layout_is_found_by_its_structure()
    {
        var exe = new PorStartExe(Image(150));
        Assert.Equal(150, exe.LayoutOffset);
        Assert.Equal(108, exe.Layout.Start[9]);
        Assert.Equal(15, exe.SkyColours[0]);
    }

    [Fact]
    public void Command_line_is_the_string_followed_by_itself_without_its_first_word()
    {
        byte[] Pascal(string s) => [(byte)s.Length, .. System.Text.Encoding.ASCII.GetBytes(s)];
        byte[] b = [1, 2, .. Pascal("Alpha Beta"), 0, .. Pascal("Map Walk Rest Peek"), 0, 0, 0, .. Pascal("Walk Rest Peek"), 9];
        Assert.Equal(["Map", "Walk", "Rest", "Peek"], PorStartExe.FindCommandLine(b));
        Assert.Null(PorStartExe.FindCommandLine(Pascal("Map Walk Rest Peek")));
    }

    [Fact]
    public void A_missing_or_doubled_table_is_an_error()
    {
        Assert.Throws<FormatException>(() => new PorStartExe(new byte[600]));
        Assert.Throws<FormatException>(() => new PorStartExe(Image(150, twice: true)));
    }
}

/// <summary>Checks against the player's PoR v1.3 files (skipped without <c>GOLDBOX_POR_DIR</c>).</summary>
public class PorViewRealDataTests
{
    [PorFact]
    public void Every_geo_block_parses()
    {
        var dir = PorData.Open();
        int n = 0;
        for (int d = 1; d <= 8; d++)
            foreach (var e in dir.Dax($"GEO{d}.DAX")!.Entries)
            {
                PorGeo.Load(dir, d, e.Id);
                Assert.Equal(d, PorGeo.DiskOf(dir, e.Id));
                n++;
            }
        Assert.Equal(29, n);
    }

    [PorFact]
    public void Program_tables_and_symbol_blocks_load()
    {
        var art = PorViewArt.Load(PorData.Open());
        Assert.Equal(156, Enumerable.Range(0, 10).Sum(p => art.Layout.Width[p] * art.Layout.Height[p]));
        Assert.All(art.Program.SkyColours, c => Assert.InRange(c, (byte)0, (byte)15));
        // Derived from the tile counts; equal to the DS:2722/272C tables read from v1.1 and v1.2's START.EXE.
        Assert.Equal([1, 46, 116, 186, 256], art.Symbols.Bases);
        Assert.Equal(45, art.Symbols.RebaseThreshold);
        Assert.False(art.Symbols.Tile(45).IsEmpty);
        Assert.False(art.Symbols.Tile(256 + 19).IsEmpty); // the last area-map square tile
        Assert.Equal(177, art.Font.GlyphCount);
        Assert.Equal(6, art.Program.ExploreCommands?.Count); // the 3D command line: area map first, five more
    }

    [PorFact]
    public void Every_wall_set_loads_with_tiles_for_all_its_symbols()
    {
        var dir = PorData.Open();
        var sets = Enumerable.Range(1, 8).SelectMany(d => dir.Dax($"WALLDEF{d}.DAX")!.Entries.Select(e => (Disk: d, Id: (int)e.Id))).ToList();
        Assert.Equal(38, sets.Count);
        foreach (var (disk, id) in sets)
        {
            var art = PorViewArt.Load(dir);
            art.LoadWallSet(disk, 1, id);
            Assert.Equal(id, art.Walls.SetIds[0]);
            // Every non-zero symbol of the slots now has a tile.
            for (int slot = 0; slot < 3; slot++)
                if (art.Walls.SetIds[slot] is not null)
                    foreach (var s in art.Walls.Data.Slice(slot * WallBuffer.SlotSize, WallBuffer.SlotSize))
                        if (s != 0)
                            Assert.False(art.Symbols.Tile(s).IsEmpty, $"disk {disk} set {id}: symbol {s} has no tile");
        }
    }

    [PorFact]
    public void Slums_entry_from_save_A()
    {
        var dir = PorData.Open();
        var art = PorViewArt.Load(dir);
        var area = PorArea.FromSave(dir, art, dir.Find("SAVGAMA.DAT")!);
        Assert.Equal((20, 2, 20), (area.Script, area.Disk, area.Map.Id));
        Assert.False(area.MapGuessed);
        Assert.Equal([2, 4, 1], art.Walls.SetIds.Select(i => i ?? -1));
        Assert.Equal((11, 9), (area.SkyIndex, area.SkyAltIndex)); // trace_load_step: SAVE #11 → $49FD, #9 → $49FE
        Assert.True(area.AreaMapAllowed);
        Assert.Equal("15,4 W", area.Party.ToString());
        Assert.False(area.Map.Bounded);
    }

    /// <summary>
    /// SHA-256 of the 88×88 view (EGA indices) in DOSBox-X screenshots of PoR v1.3 (EGA, 2026-11-17, s10): save A's
    /// party put at each square of the Slums and loaded from the main menu; "map" = after the A key. Only the hashes are
    /// kept here, not the pictures.
    /// </summary>
    [PorTheory]
    [InlineData(15, 4, 6, false, "189563d445d98fe6c805d70fd62d2effc82e7b2d344908859a2f46da52d3d577")]
    [InlineData(15, 4, 6, true, "cf1fb777c356b92da88bf3cbbff7f80a851bca4dc93290cc5a71fe9a358ac353")]
    [InlineData(15, 4, 4, false, "3bb4d18f4943b5491af728a0c95027ecce07a51cbe251f1fc222fd7dd1dc3e3a")]
    [InlineData(14, 4, 6, false, "72241b3b5e78d1b31aa30ca6184d46fd95ccdecfc7794eec29201ec3b46bbad2")]
    [InlineData(14, 4, 0, false, "1e7efeb065bb84b9a81d905eeadaae80976d0b3e378cac0cfb561ebf9acf6422")]
    [InlineData(14, 4, 2, false, "e2404e92066d744aaf6f67bb7c45e3d153e07fdc6df38ca8499872e50ce21cb4")] // looks across the east edge (wrap)
    [InlineData(9, 8, 2, false, "a320207adee4a10fd44c2cda040075be019fcfd9093c9a5848b17ec8ecf74938")]
    [InlineData(9, 8, 2, true, "e056d4ade172b75d13d1799f28e7d6bfdb10540f6ce045b3e4d81a18e1f42999")]
    [InlineData(6, 2, 6, false, "0c142ed9a470fdbb30fa7df71c455760a23136448b1c85e5b62550e133d50ec9")]
    [InlineData(6, 2, 6, true, "08d23f697f3da2185d9c66a6fddcc2564f8e48bb1ec3efb72412617143849a57")]
    [InlineData(4, 12, 2, false, "73d487a07ec145c16691cee78fa1636c9b7c898f22c016183de278661cc2b796")] // second sky colour (event ≥ 0x80)
    [InlineData(4, 12, 2, true, "712b9dbd43932e846c1b5260ba569e59402c7ff5e8f06bf5426bd55a5c54994a")]
    [InlineData(3, 8, 4, false, "f26e8c5a8722519c14be5b0851474162793b406f68da2d1a8422013d3b2f4a27")]
    [InlineData(3, 8, 4, true, "2b2669fe4674ec0f4db898a6d17b822d66c5d52461ff1a0ef9abd633aa37a988")]
    public void View_matches_dosbox(int x, int y, int facing, bool areaMap, string sha256)
    {
        var dir = PorData.Open();
        var art = PorViewArt.Load(dir);
        var path = dir.Find("SAVGAMA.DAT")!;
        var save = File.ReadAllBytes(path);
        save[PorSaveGame.OffPosition] = (byte)x;
        save[PorSaveGame.OffPosition + 1] = (byte)y;
        save[PorSaveGame.OffPosition + 2] = (byte)facing;
        var area = PorArea.FromSave(dir, art, save, Path.GetDirectoryName(path)!);
        var frame = area.RenderView(art, areaMap);
        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(frame.Pixels)));
    }
}

/// <summary>Entering every area script with a fresh VM (the Client's --area start), on the player's files.</summary>
public class PorAreaEntryTests
{
    public static TheoryData<int> Scripts()
    {
        var d = new TheoryData<int>();
        for (int s = 0; s <= 32; s++)
            d.Add(s);
        return d;
    }

    [PorTheory]
    [MemberData(nameof(Scripts))]
    public void Every_area_enters_and_renders(int script)
    {
        var dir = PorData.Open();
        if (dir.DiskOf(script) is null)
            return; // no such ECL block
        var art = PorViewArt.Load(dir);
        if (script == 19)
        {
            // The overland script loads no 3D map.
            Assert.Throws<InvalidOperationException>(() => PorArea.Enter(dir, art, script, 8, 8, Facing.North));
            return;
        }
        var area = PorArea.Enter(dir, art, script, 8, 8, Facing.North);
        Assert.Equal(script is 0 or 10, area.Map.Bounded);
        // Only the castle quadrants ask for a slot-2 set after a two-block set with fresh memory (two-flag rule).
        Assert.True(art.Diagnostics.Count == 0 || script is >= 3 and <= 7, string.Join("; ", art.Diagnostics));
        for (int f = 0; f < 8; f += 2)
            foreach (var s in View3D.Compose(area.Map, 8, 8, f, art.Layout, art.Walls))
                Assert.False(art.Symbols.Tile(s.Symbol).IsEmpty, $"script {script} facing {f}: symbol {s.Symbol} has no tile");
        area.RenderView(art, areaMap: true);
    }
}
