using System.Buffers.Binary;
using GoldBox.Core.Ecl;

namespace GoldBox.Data.Por;

/// <summary>
/// <c>SAVGAMx.DAT</c> of Pool of Radiance DOS (docs/re/save-format.md "SAVGAMx.DAT layout", 13,137 bytes): disk
/// byte, the three 16-bit VM areas, the 7680-byte script area, party position, location type, interface state,
/// party size and the party file stems.
/// </summary>
public sealed class PorSaveGame
{
    public const int Size = 13137;
    public const int OffDisk = 0, Off4900 = 1, Off6B00 = 2049, Off9700 = 4097, OffScript = 5121,
        OffPosition = 12801, OffLocationType = 12806, OffInterface = 12807, OffPartySize = 12808, OffNames = 12809;

    public byte Disk { get; }
    public ushort[] Area4900 { get; } = new ushort[1024];
    public ushort[] Area6B00 { get; } = new ushort[1024];
    public ushort[] Area9700 { get; } = new ushort[512];
    public byte[] ScriptArea { get; } = new byte[EclLayout.AreaSize];
    public byte X { get; }
    public byte Y { get; }
    public byte Facing { get; }
    public byte WallAhead { get; }
    public byte EventByte { get; }
    public byte LocationType { get; }
    public byte InterfaceState { get; }
    public byte PartySize { get; }
    public IReadOnlyList<string> PartyFileStems { get; }

    /// <summary>Current map / ECL script id (VM $49C5).</summary>
    public int ScriptId => Area4900[0x49C5 - 0x4900] & 0xFF;

    /// <summary>
    /// The ECL block the loaded game runs as, DS:84DC: the area entry after a Load sets it from the low byte of $49F2
    /// (0x4067–0x4070), the block current at the last command read (written back at 0x40D6 / 0x40FA; NEWECL writes the
    /// old block there first, 0x1926). Load itself does not write DS:84DC. So a save made in the Valjevo hedge maze
    /// (maps 3–6 in $49C5) runs as block 5. DOSBox-X s39: `save-overland` saves ($49C5 26, save A's $49F2 20) read
    /// DS:84DC = 20 after the load.
    /// </summary>
    public int CurrentBlock => Area4900[0x49F2 - 0x4900] & 0xFF;

    public PorSaveGame(byte[] data)
    {
        if (data.Length != Size)
            throw new FormatException($"save game is {data.Length} bytes, expected {Size}");
        Disk = data[OffDisk];
        ReadWords(data, Off4900, Area4900);
        ReadWords(data, Off6B00, Area6B00);
        ReadWords(data, Off9700, Area9700);
        data.AsSpan(OffScript, EclLayout.AreaSize).CopyTo(ScriptArea);
        X = data[OffPosition];
        Y = data[OffPosition + 1];
        Facing = data[OffPosition + 2];
        WallAhead = data[OffPosition + 3];
        EventByte = data[OffPosition + 4];
        LocationType = data[OffLocationType];
        InterfaceState = data[OffInterface];
        PartySize = data[OffPartySize];
        var stems = new List<string>();
        for (int i = 0; i < Math.Min((int)PartySize, 8); i++)
        {
            int p = OffNames + 41 * i;
            int len = Math.Min((int)data[p], 40);
            stems.Add(System.Text.Encoding.ASCII.GetString(data, p + 1, len));
        }
        PartyFileStems = stems;
    }

    public static PorSaveGame Open(string path) => new(File.ReadAllBytes(path));

    /// <summary>
    /// The save as the program writes it (GAME.OVR 0x1FD3E, docs/re/camp.md "Save"): the disk byte (also stored in
    /// $6E12 first), the three word areas, the script area, the party position, the location type, the interface
    /// state, the party size and the stems <c>CHRDAT&lt;letter&gt;&lt;n&gt;</c> (string[40] each; the bytes after a
    /// stem and the unused slots are written as zero here, leftover memory in the program). The caller puts the game
    /// speed, the picture flags and the wall sets into $49FC, $49FF and $4AFA–$4AFF before (the program copies them
    /// from its variables).
    /// </summary>
    public static byte[] Build(EclMemory memory, int partySize, char letter, byte locationType, byte interfaceState)
        => Build(memory, partySize, letter, locationType, interfaceState, gameSpeed: null);

    /// <summary>
    /// The save as the program builds it from its own variables: the location type is DS:49FA
    /// (<see cref="EclMemory.ViewMode"/>, written at 0x1FF9D) and $49FC := DS:49E2 (<see cref="EclMemory.GameSpeed"/>,
    /// 0x1FECE).
    /// </summary>
    public static byte[] Build(EclMemory memory, int partySize, char letter, byte interfaceState) =>
        Build(memory, partySize, letter, memory.ViewMode, interfaceState, memory.GameSpeed);

    private static byte[] Build(EclMemory memory, int partySize, char letter, byte locationType, byte interfaceState,
        byte? gameSpeed)
    {
        letter = char.ToUpperInvariant(letter);
        if (letter is < 'A' or > 'J')
            throw new ArgumentOutOfRangeException(nameof(letter), "save slots are A..J");
        if (gameSpeed is { } speed)
            memory.Write(0x49FC, speed);
        memory.Write(0x6E12, memory.Disk);
        var d = new byte[Size];
        d[OffDisk] = memory.Disk;
        WriteWords(d, Off4900, memory.Cells4900);
        WriteWords(d, Off6B00, memory.Cells6B00);
        WriteWords(d, Off9700, memory.Cells9700);
        memory.ScriptArea.CopyTo(d.AsSpan(OffScript, EclLayout.AreaSize));
        var p = memory.Position;
        d[OffPosition] = p.X;
        d[OffPosition + 1] = p.Y;
        d[OffPosition + 2] = p.Facing;
        d[OffPosition + 3] = p.WallAhead;
        d[OffPosition + 4] = p.EventByte;
        d[OffLocationType] = locationType;
        d[OffInterface] = interfaceState;
        int n = Math.Clamp(partySize, 0, 8);
        d[OffPartySize] = (byte)n;
        for (int i = 0; i < n; i++)
        {
            var stem = Stem(letter, i + 1);
            d[OffNames + 41 * i] = (byte)stem.Length;
            System.Text.Encoding.ASCII.GetBytes(stem).CopyTo(d, OffNames + 41 * i + 1);
        }
        return d;
    }

    /// <summary>The file stem of party member <paramref name="n"/> (1-based) in save slot <paramref name="letter"/>.</summary>
    public static string Stem(char letter, int n) => $"CHRDAT{char.ToUpperInvariant(letter)}{n}";

    /// <summary>The save file name of a slot (the program writes it lower case, <c>savgam&lt;letter&gt;.dat</c>).</summary>
    public static string FileName(char letter) => $"SAVGAM{char.ToUpperInvariant(letter)}.DAT";

    private static void WriteWords(byte[] data, int off, ReadOnlySpan<ushort> src)
    {
        for (int i = 0; i < src.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(off + 2 * i), src[i]);
    }

    private static void ReadWords(byte[] data, int off, ushort[] dst)
    {
        for (int i = 0; i < dst.Length; i++)
            dst[i] = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(off + 2 * i));
    }

    /// <summary>Fills the VM memory: the four areas, the party position cells $C04B–$C04F and the disk byte (DS:5376).</summary>
    public void ApplyTo(EclMemory memory)
    {
        memory.Disk = Disk;
        Area4900.CopyTo(memory.Cells4900);
        Area6B00.CopyTo(memory.Cells6B00);
        Area9700.CopyTo(memory.Cells9700);
        ScriptArea.CopyTo(memory.ScriptArea);
        memory.Position.X = X;
        memory.Position.Y = Y;
        memory.Position.Facing = Facing;
        memory.Position.WallAhead = WallAhead;
        memory.Position.EventByte = EventByte;
        memory.ViewMode = LocationType;                        // the location type byte is DS:49FA (0x1F862 reads it back)
        memory.GameSpeed = (byte)memory.Read(0x49FC);          // DS:49E2 := low byte of $49FC (0x1F8F1)
    }

    /// <summary>
    /// Restores the VM from this save: memory as <see cref="ApplyTo(EclMemory)"/>, the script area as saved (its
    /// vectors re-read), current block = $49F2 (<see cref="CurrentBlock"/>) on disk byte 0. No $4A00 reset (the area comes from the save).
    /// </summary>
    public void ApplyTo(EclVm vm)
    {
        ApplyTo(vm.Memory);
        vm.ReadVectors();
        vm.SetCurrentScript(CurrentBlock, Disk);
    }

    /// <summary>
    /// The main menu's "Load game" as the program does it: memory from the save, DS:49F8 set, the script init on the
    /// saved script (no file read: the script comes from the save), then vector 4. Attach the party first if the
    /// scripts should see it.
    /// </summary>
    public EclRunResult LoadGame(EclVm vm, int maxSteps = 100_000)
    {
        ApplyTo(vm.Memory);
        vm.SetCurrentScript(CurrentBlock, Disk);
        return vm.RunLoadedGame(maxSteps);
    }

    /// <summary>
    /// Bytes of the saved script area that differ from <paramref name="block"/> (minus its prefix); the area after the
    /// block must be zero. 0 = the save holds exactly that block (save-format.md check 3).
    /// </summary>
    public int ScriptAreaMismatches(ReadOnlySpan<byte> block)
    {
        var body = block[EclLayout.Prefix..];
        int bad = 0;
        for (int i = 0; i < ScriptArea.Length; i++)
            if (ScriptArea[i] != (i < body.Length ? body[i] : 0))
                bad++;
        return bad;
    }

    /// <summary>
    /// A copy of a save with the party put in another area, for side-by-side checks in DOSBox-X (tool
    /// <c>save-area</c>): the script area holds ECL block <paramref name="script"/> from its disk, $49C5 = the script,
    /// the disk byte and the cell $6E12 (the program's load takes the disk from there), the position, and the
    /// script's flags $4A00–$4A1F (kept on a load) zero except $4A00 = <paramref name="flag0"/>. The load re-reads the
    /// wall sets named at $4AFA–$4AFC before vector 4 and stops ("Unable to load wallset") when the area's disk has no
    /// such block, so <paramref name="walls"/> gives the area's own ids (-1 = none; slots 1, 2, 3).
    /// </summary>
    public static byte[] PlaceInArea(byte[] save, PorGameDirectory dir, int script, int x, int y, int facing, int flag0 = 0,
        int[]? walls = null)
    {
        var s = (byte[])save.Clone();
        int disk = dir.DiskOf(script) ?? throw new ArgumentException($"no ECL block {script}");
        var block = dir.LoadBlock(disk, script)!;
        s[OffDisk] = (byte)disk;
        var area = s.AsSpan(OffScript, EclLayout.AreaSize);
        area.Clear();
        block.AsSpan(EclLayout.Prefix).CopyTo(area);
        void Word(int off, int v)
        {
            s[off] = (byte)v;
            s[off + 1] = (byte)(v >> 8);
        }
        void Cell(int addr, int v) => Word(Off4900 + 2 * (addr - 0x4900), v);
        Cell(0x49C5, script);
        Word(Off6B00 + 2 * (0x6E12 - 0x6B00), disk);
        for (int c = 0x4A00; c <= 0x4A1F; c++)
            Cell(c, 0);
        Cell(0x4A00, flag0);
        if (walls is not null)
            for (int k = 0; k < 3; k++)
            {
                Cell(0x4AFA + k, k < walls.Length ? walls[k] & 0xFFFF : 0xFFFF);
                Cell(0x4AFD + k, k + 1);
            }
        s[OffPosition] = (byte)x;
        s[OffPosition + 1] = (byte)y;
        s[OffPosition + 2] = (byte)facing;
        return s;
    }

    /// <summary>
    /// A copy of <paramref name="save"/> with the party on the overland of wilderness script <paramref name="script"/>
    /// (25–27): <see cref="PlaceInArea"/> (3D position 0,0), then the location type byte (DS:49FA) = the script's view
    /// mode 2–4, $49E6 = 0 and the overland square $49C3 / $49C4 = <paramref name="x"/>, <paramref name="y"/>. Main menu
    /// → Load then keeps the view mode (no map is read for a mode ≥ 2) and runs the script's vector 4.
    /// </summary>
    public static byte[] PlaceOnOverland(byte[] save, PorGameDirectory dir, int script, int x, int y, int facing)
    {
        int mode = PorOverland.ViewMode(script) ?? throw new ArgumentException($"script {script} is not a wilderness script");
        var s = PlaceInArea(save, dir, script, 0, 0, facing);
        void Cell(int addr, int v)
        {
            int o = Off4900 + 2 * (addr - 0x4900);
            s[o] = (byte)v;
            s[o + 1] = (byte)(v >> 8);
        }
        Cell(0x49E6, 0);
        Cell(Core.Explore.Overland.CellX, x);
        Cell(Core.Explore.Overland.CellY, y);
        s[OffLocationType] = (byte)mode;
        return s;
    }
}
