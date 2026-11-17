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
    }

    /// <summary>
    /// Restores the VM from this save: memory as <see cref="ApplyTo(EclMemory)"/>, the script area as saved (its
    /// vectors re-read), current script = $49C5 on disk byte 0. No $4A00 reset (the area comes from the save).
    /// </summary>
    public void ApplyTo(EclVm vm)
    {
        ApplyTo(vm.Memory);
        vm.ReadVectors();
        vm.SetCurrentScript(ScriptId, Disk);
    }

    /// <summary>
    /// The main menu's "Load game" as the program does it: memory from the save, DS:49F8 set, the script init on the
    /// saved script (no file read: the script comes from the save), then vector 4. Attach the party first if the
    /// scripts should see it.
    /// </summary>
    public EclRunResult LoadGame(EclVm vm, int maxSteps = 100_000)
    {
        ApplyTo(vm.Memory);
        vm.SetCurrentScript(ScriptId, Disk);
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
}
