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

/// <summary>
/// Areas other than the Slums (s11): save A's party placed in another area with <see cref="PorSaveGame.PlaceInArea"/>
/// (tool <c>save-area</c>), loaded from the main menu in DOSBox-X (PoR v1.3, EGA, 2026-11-17), screenshot of the view,
/// then A (area map, or refused where $49FB ≠ 0), then one Up step. Hashes are of DOSBox-X's 88×88 view.
/// New Phlan: the three-block wall set (127 → WALLDEF3 0), a bounded map. Valhingen Graveyard: a two-block set with
/// the two-flag rule ($49E7/$49E8, slot 2's 22 absent), the backdrop bytes $C05F = 8 / $C059 = 11 (horizon 0 / 8,
/// ground 8), and after the step the black sky vector 0 sets ($49FD = 8). Kobold Caves: no area map, no coordinates.
/// </summary>
public class OtherAreaViewTests
{
    public static TheoryData<int, int, int, int, int, int[], string, string, string> Cases() => new()
    {
        { 0, 2, 4, Facing.East, 0, [0, -1, -1], "2fefae327565357ea24b001fd61bae4b2944854e79cde72157b4b73ae6df7da2",
            "08ebed323991e99beaaed62c038604d658e2a6349fe23fdf8ce2cfd1c27dc439", "" },
        { 10, 0, 4, Facing.East, 1, [21, -1, 20], "c35d62eda83a0392157ff7d0036cc381422d29d29c1aaaea5cb8797e3256d2a3",
            "9e70590534027c9e9aac251209135eceaea4cfa1e260a658d2860f5405ebfd7b", "369660dcc9f8cd2e14babfddab39770d02d32b475f3f248ca19aff240d3c3fd7" },
        { 10, 9, 9, Facing.East, 1, [21, -1, 20], "437106d0012dbcd28ce4b7ad1887b87cb80019bc966caa72405d50ee560bcc96",
            "71e4e65fdefd03682b719eb9789d437760fae977cd5a5942b39835a15ea13163", "5901e42a3c6beaa03194a13e56e46247aa1881f01fbeef9c5623016c54510bc7" },
        // Area map refused here: the view stays.
        { 13, 14, 14, Facing.North, 0, [17, 18, -1], "d66b311b833045f0c5482c563de28e07c1d6386b2087f796edbf639bebad0657",
            "d66b311b833045f0c5482c563de28e07c1d6386b2087f796edbf639bebad0657", "bdc2333309097bf72d76f7385aa657ad66d510b835002f17656061ac15a330b0" },
    };

    private static string Hash(IndexedFrame f) => Convert.ToHexStringLower(SHA256.HashData(f.Pixels));

    private sealed class NoDoors : IAreaUi
    {
        public GoldBox.Core.Doors.DoorAction? ChooseDoorAction(GoldBox.Core.Doors.DoorBarrier b, IReadOnlyList<GoldBox.Core.Doors.DoorAction> c) => null;
        public void Bump() { }
        public void Message(AreaMessage m) { }
        public bool Camp() => false;
    }

    [PorTheory]
    [MemberData(nameof(Cases))]
    public void View_matches_dosbox(int script, int x, int y, int facing, int flag0, int[] walls, string view, string map, string step)
    {
        var dir = PorData.Open();
        var savePath = dir.Find("SAVGAMA.DAT")!;
        var save = PorSaveGame.PlaceInArea(File.ReadAllBytes(savePath), dir, script, x, y, facing, flag0, walls);
        var art = PorViewArt.Load(dir);
        var area = PorArea.FromSave(dir, art, save, Path.GetDirectoryName(savePath)!);
        Assert.Equal(view, Hash(area.RenderView(art, false)));
        Assert.Equal(map, Hash(area.RenderView(art, area.AreaMapAllowed)));
        if (step.Length == 0)
            return;
        var player = new PorPlayer(new GoldBox.Core.Ecl.ScriptedEclHost { StopWhenInputMissing = true },
            new GoldBox.Core.Game.ScriptedEncounterUi { StopWhenInputMissing = true }, new GoldBox.Core.Game.RecordingCombatRunner(), new NoDoors());
        var game = PorGame.FromSave(dir, PorViewArt.Load(dir), save, Path.GetDirectoryName(savePath)!, player, 0);
        game.Loop.Execute(AreaCommand.Forward);
        Assert.Equal(step, Hash(game.RenderView(false)));
    }

    /// <summary>
    /// Lairs on disks 6–8 (s24, the residue runs' load screens): the 3D maps of the wilderness scripts 25 and 27
    /// ($4A9E = 255) and the dungeons 13 and 23 (whose vector 4 loads no wall set: the saved ones show).
    /// </summary>
    public static TheoryData<int, int, int, int, int[], int, string> Lairs() => new()
    {
        { 25, 2, 9, Facing.North, [17, 17, 17], 0x4A9E, "d4c79eb4c85f2eea5d6a10ee88cd9c4bda69777c7e3ed761876f4f792d8eac69" },
        { 27, 3, 11, Facing.West, [17, 17, 17], 0x4A9E, "8c3b1f291eb08d40a92e1fb60b9960014662fd3cae6549b8c48303ba3b55fea9" },
        { 27, 0, 7, Facing.South, [17, 17, 17], 0x4A9E, "2eaadc14738de89a8ef9b601a3c66ff5cf6871650ac691dc57faf5ba52453f80" },
        { 13, 14, 7, Facing.North, [17, 18, -1], 0, "a1d881a41b360b784233aa741f8472ab516ec7da9030b5c1c9d71d0b8eb68487" },
        { 23, 7, 0, Facing.East, [1, 3, -1], 0, "0e828d4f0a8c8fcae4eb68a32e2d615c2ff6840a30a16c9afb7847668be17128" },
        { 23, 4, 9, Facing.South, [1, 3, -1], 0, "c72dd6078ad91022aeea5064e74676ca84e84c9242a768ec90938e855cb4be0a" },
        { 23, 13, 13, Facing.South, [1, 3, -1], 0, "4904a8f516cffb429e524fe464c4b465bf4e49d5f371fe25a4fab5fa4453670c" },
    };

    [PorTheory]
    [MemberData(nameof(Lairs))]
    public void Lair_views_match_dosbox(int script, int x, int y, int facing, int[] walls, int cell255, string view)
    {
        var dir = PorData.Open();
        var savePath = dir.Find("SAVGAMA.DAT")!;
        var save = PorSaveGame.PlaceInArea(File.ReadAllBytes(savePath), dir, script, x, y, facing, 0, walls);
        if (cell255 != 0)
            save[PorSaveGame.Off4900 + 2 * (cell255 - 0x4900)] = 255;
        var art = PorViewArt.Load(dir);
        var area = PorArea.FromSave(dir, art, save, Path.GetDirectoryName(savePath)!);
        Assert.Equal(view, Hash(area.RenderView(art, false)));
        var player = new PorPlayer(new GoldBox.Core.Ecl.ScriptedEclHost { StopWhenInputMissing = true },
            new GoldBox.Core.Game.ScriptedEncounterUi { StopWhenInputMissing = true }, new GoldBox.Core.Game.RecordingCombatRunner(), new NoDoors());
        var game = PorGame.FromSave(dir, PorViewArt.Load(dir), save, Path.GetDirectoryName(savePath)!, player, 0);
        Assert.Equal(view, Hash(game.RenderView(false)));
    }

    /// <summary>
    /// Valjevo Castle (ECL5 blocks 3–7, GEO5 maps 3–7; s28): save A placed with <c>save-area</c>, saved wall sets
    /// 24/–/1 and $49E7 = $49E8 = 1 (block 6's init sets both before its LOADPIECES 24, 25, 1, so slots 1–2 get the
    /// two-block set 24 and slot 3 set 1; without them the game would ask for WALLDEF5 block 25, which does not exist).
    /// Block 3's vector 4 picks map 3 or 5 by the facing ($9C24); blocks 4, 5 and 7 load no wall set (the saved ones
    /// show) and block 5 loads no map (Load re-reads map $49C5, here 3–6: the hedge maze). Each loaded from the main
    /// menu in DOSBox-X (MACHINE=ega): 0 differing pixels in all 13 views and after one step west at 4: 8,7 W.
    /// </summary>
    public static TheoryData<int, int, int, int, int, string, string> Valjevo() => new()
    {
        { 6, 0, 1, 8, Facing.North, "f5b42ed4986d828a2994a3a2af4f2d43ca4b517f3a1348aece1ec3f86424092b", "" },
        { 6, 0, 5, 8, Facing.East, "f3b1cfcce1d245d7fa61845da466e047891db8c595cc04df446ec0de6bd2bc29", "" },
        { 4, 0, 14, 10, Facing.North, "20f321f883cb6fd4a466ce96c2bf0b8677cc35c8c0bd94cc1a4370f3495b97c5", "" },
        { 4, 0, 8, 7, Facing.West, "180c81ec68ff3bdcfd7fdf467135c7ac5147a05011013c7c5c0ac6e7754475a0",
            "8512e79ddaedd6dcf8f27edc46fc0d8a8c4f62923fcb208a6f035a677b59bd9a" },
        { 3, 0, 1, 9, Facing.West, "2df21bf318e4147c2950a1579121885e5a904bc130fd5f4458841518250b1523", "" },
        { 3, 0, 13, 7, Facing.East, "ca0d9f6d9d1b5608df9c3f34cac1af834dbf28bde66dbb5ecaafc6e79f1509bc", "" },
        { 5, 3, 8, 8, Facing.North, "e643305ed447d89866d4f7b2e46ea008575b3f667480f75da5a4e1ee912e5806", "" },
        { 5, 4, 6, 10, Facing.South, "af67db3f0633a78228498bcf39fd0c785b2a5544683d30b473053c88ea4da84a", "" },
        { 5, 5, 5, 6, Facing.East, "4c578fe395bae93daa3ba8b5f18885b2050774711471e0f12e6a8ffb8f906ee6", "" },
        { 5, 6, 9, 7, Facing.West, "206a569c2170ec424bb6ba0f2f3cb05bfc8c1508d6cf60bb10867ff5862e1996", "" },
        { 7, 0, 2, 5, Facing.South, "7c9b144182a42938c96838c624878042265a3915f7268c9e879b769459bdfbdd", "" },
        { 7, 0, 7, 9, Facing.North, "52d6f4ecb2612fc3a5d96b7c62b20a63361b3519064027b7b94648dd7d211cac", "" },
        { 7, 0, 10, 5, Facing.West, "d1462fe9e40c03dafd2d86ec513cd42df1c6e56996362e45b943d521c04ad4b7", "" },
    };

    [PorTheory]
    [MemberData(nameof(Valjevo))]
    public void Valjevo_views_match_dosbox(int script, int map, int x, int y, int facing, string view, string step)
    {
        var dir = PorData.Open();
        var savePath = dir.Find("SAVGAMA.DAT")!;
        var save = PorSaveGame.PlaceInArea(File.ReadAllBytes(savePath), dir, script, x, y, facing, 0, [24, -1, 1]);
        void Cell(int addr, int v) => save[PorSaveGame.Off4900 + 2 * (addr - 0x4900)] = (byte)v;
        Cell(0x49E7, 1);
        Cell(0x49E8, 1);
        if (map != 0)
            Cell(0x49C5, map);
        var art = PorViewArt.Load(dir);
        var area = PorArea.FromSave(dir, art, save, Path.GetDirectoryName(savePath)!);
        Assert.Empty(art.Diagnostics);
        Assert.Equal(view, Hash(area.RenderView(art, false)));
        var player = new PorPlayer(new GoldBox.Core.Ecl.ScriptedEclHost { StopWhenInputMissing = true },
            new GoldBox.Core.Game.ScriptedEncounterUi { StopWhenInputMissing = true }, new GoldBox.Core.Game.RecordingCombatRunner(), new NoDoors());
        var game = PorGame.FromSave(dir, PorViewArt.Load(dir), save, Path.GetDirectoryName(savePath)!, player, 0);
        Assert.Equal(view, Hash(game.RenderView(false)));
        if (step.Length == 0)
            return;
        game.Loop.Execute(AreaCommand.Forward);
        Assert.Equal(step, Hash(game.RenderView(false)));
    }
}

/// <summary>Load (0x1FBEE–0x1FC86) re-reads the saved wall sets before vector 4 (s23).</summary>
public class LoadWallSetTests
{
    private sealed class NoDoors : IAreaUi
    {
        public GoldBox.Core.Doors.DoorAction? ChooseDoorAction(GoldBox.Core.Doors.DoorBarrier b, IReadOnlyList<GoldBox.Core.Doors.DoorAction> c) => null;
        public void Bump() { }
        public void Message(AreaMessage m) { }
        public bool Camp() => false;
    }

    [PorFact]
    public void A_load_whose_vector_4_loads_no_walls_keeps_the_saved_ones()
    {
        // SAVGAMA's script area with every vector at an EXIT (as tools/scripts/por_test_saves.py writes slots B–I):
        // DOSBox-X shows the saved walls (2, 4, 1 at $4AFA–$4AFC), not an open view.
        var dir = PorData.Open();
        var savePath = dir.Find("SAVGAMA.DAT")!;
        var save = File.ReadAllBytes(savePath);
        var area = save.AsSpan(PorSaveGame.OffScript, GoldBox.Core.Ecl.EclLayout.AreaSize);
        area.Clear();
        for (int v = 0; v < 5; v++)
        {
            area[4 * v] = 1;
            area[4 * v + 1] = 1;
            area[4 * v + 2] = 0x1F;
            area[4 * v + 3] = 0x99;
        }
        var art = PorViewArt.Load(dir);
        var player = new PorPlayer(new GoldBox.Core.Ecl.ScriptedEclHost { StopWhenInputMissing = true },
            new GoldBox.Core.Game.ScriptedEncounterUi { StopWhenInputMissing = true }, new GoldBox.Core.Game.RecordingCombatRunner(), new NoDoors());
        PorGame.FromSave(dir, art, save, Path.GetDirectoryName(savePath)!, player, 0);
        Assert.Equal([2, 4, 1], art.Walls.SetIds.Select(i => i ?? -1));
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
