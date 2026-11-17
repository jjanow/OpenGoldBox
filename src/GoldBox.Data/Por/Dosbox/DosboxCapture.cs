using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using GoldBox.Core.Ecl;

namespace GoldBox.Data.Por.Dosbox;

/// <summary>
/// One VM state dumped from a DOSBox-X save state by <c>tools/scripts/dosbox_state.py dump --areas</c>
/// (docs/re/dosbox-trace-method.md): the three word areas and the script in the save-file layout, the current map
/// planes and the DS variables of <c>vars.json</c> (PC, vectors, opcodes, flags, disk, RandSeed, party position).
/// </summary>
public sealed class DosboxVmState
{
    public string Folder { get; }
    public ushort[] Area4900 { get; } = new ushort[1024];
    public ushort[] Area6B00 { get; } = new ushort[1024];
    public ushort[] Area9700 { get; } = new ushort[512];
    public byte[] Script { get; } = new byte[EclLayout.AreaSize];
    public byte[]? GeoPlanes { get; }
    public IReadOnlyDictionary<string, long> Vars { get; }

    public DosboxVmState(string folder)
    {
        Folder = folder;
        ReadWords(Path.Combine(folder, "area_4900.bin"), Area4900);
        ReadWords(Path.Combine(folder, "area_6B00.bin"), Area6B00);
        ReadWords(Path.Combine(folder, "area_9700.bin"), Area9700);
        var script = File.ReadAllBytes(Path.Combine(folder, "script_9900.bin"));
        script.AsSpan(0, Math.Min(script.Length, Script.Length)).CopyTo(Script);
        var geo = Path.Combine(folder, "geo_planes.bin");
        GeoPlanes = File.Exists(geo) ? File.ReadAllBytes(geo) : null;
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "vars.json")));
        var vars = new Dictionary<string, long>();
        foreach (var p in doc.RootElement.GetProperty("vars").EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.Number)
                vars[p.Name] = p.Value.GetInt64();
        Vars = vars;
    }

    public static bool Exists(string folder) => File.Exists(Path.Combine(folder, "vars.json"));

    public long Var(string name) => Vars.TryGetValue(name, out var v) ? v : throw new KeyNotFoundException($"{Folder}: no var {name}");

    public ushort Pc => (ushort)Var("pc");
    public byte Opcode => (byte)Var("opcode");
    public byte PreviousOpcode => (byte)Var("prev_opcode");
    public uint RandSeed => (uint)Var("randseed");
    public byte Disk => (byte)Var("ecl_disk");
    public int Script_ => (int)Var("current_ecl_block");
    public bool GosubStackEmpty => Var("gosub_stack_head") == 0;

    /// <summary>The six compare flags in DS:6F78..6F7D order (= ≠ &lt; &gt; ≤ ≥).</summary>
    public bool[] Flags =>
        new[] { "flag_eq", "flag_ne", "flag_lt", "flag_gt", "flag_le", "flag_ge" }.Select(n => Var(n) != 0).ToArray();

    public ushort[] Vectors =>
        new[] { "vector0_move", "vector1_search", "vector2_precamp", "vector3_camp_interrupted", "vector4_init" }
            .Select(n => (ushort)Var(n)).ToArray();

    /// <summary>Party x, y, facing, wall ahead, event byte (DS:6AAD–6AB1 = VM $C04B–$C04F sources).</summary>
    public (byte X, byte Y, byte Facing, byte Wall, byte Event) Position =>
        ((byte)Var("party_x"), (byte)Var("party_y"), (byte)Var("party_facing"), (byte)Var("party_wall"), (byte)Var("party_event"));

    /// <summary>Value of a VM cell in this dump (word areas and script bytes), or null for other addresses.</summary>
    public ushort? Cell(ushort a) => a switch
    {
        >= EclMemory.Area4900 and <= EclMemory.Area4900End => Area4900[a - EclMemory.Area4900],
        >= EclMemory.Area6B00 and <= EclMemory.Area6B00End => Area6B00[a - EclMemory.Area6B00],
        >= EclMemory.Area9700 and <= EclMemory.Area9700End => Area9700[a - EclMemory.Area9700],
        >= EclLayout.Base and <= EclLayout.End => Script[a - EclLayout.Base],
        _ => null,
    };

    /// <summary>All addresses a dump holds, in order.</summary>
    public static IEnumerable<ushort> Addresses()
    {
        for (int a = EclMemory.Area4900; a <= EclMemory.Area4900End; a++) yield return (ushort)a;
        for (int a = EclMemory.Area6B00; a <= EclMemory.Area6B00End; a++) yield return (ushort)a;
        for (int a = EclMemory.Area9700; a <= EclMemory.Area9700End; a++) yield return (ushort)a;
        for (int a = EclLayout.Base; a <= EclLayout.End; a++) yield return (ushort)a;
    }

    /// <summary>
    /// Puts the VM in this state: areas, script, disk byte, position, PC, vectors (read from the script and checked
    /// against the dump), flags, opcodes, current script. The GOSUB stack must be empty (its links are far pointers).
    /// Returns notes about anything that could not be reproduced.
    /// </summary>
    public List<string> ApplyTo(EclVm vm)
    {
        var notes = new List<string>();
        var m = vm.Memory;
        Area4900.CopyTo(m.Cells4900);
        Area6B00.CopyTo(m.Cells6B00);
        Area9700.CopyTo(m.Cells9700);
        Script.CopyTo(m.ScriptArea);
        m.Disk = Disk;
        var p = Position;
        m.Position.X = p.X;
        m.Position.Y = p.Y;
        m.Position.Facing = p.Facing;
        m.Position.WallAhead = p.Wall;
        m.Position.EventByte = p.Event;
        vm.ReadVectors();
        var dumped = Vectors;
        for (int k = 0; k < dumped.Length; k++)
            if (vm.Vectors[k] != dumped[k])
                notes.Add($"vector {k}: script area gives {vm.Vectors[k]:X4}, DS holds {dumped[k]:X4}; DS value used");
        dumped.CopyTo(vm.Vectors, 0);
        Array.Copy(Flags, vm.Flags, 6);
        vm.Pc = Pc;
        vm.Opcode = Opcode;
        vm.PreviousOpcode = PreviousOpcode;
        vm.SetCurrentScript(Script_, Disk);
        vm.LoadingSave = Var("ds_49f8") != 0;
        if (!GosubStackEmpty)
            notes.Add("GOSUB stack not empty in the dump: not reproduced");
        return notes;
    }

    private static void ReadWords(string path, ushort[] dst)
    {
        var b = File.ReadAllBytes(path);
        for (int i = 0; i < dst.Length && 2 * i + 1 < b.Length; i++)
            dst[i] = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(2 * i));
    }
}

/// <summary>One line of the port-E9 trace: <c>E pppp oo ff</c> (PC, opcode, flags before the instruction, bit k = DS:6F78+k).</summary>
public readonly record struct E9TraceLine(ushort Pc, byte Op, byte Flags)
{
    public override string ToString() => $"E {Pc:X4} {Op:X2} {Flags:X2}";

    /// <summary>The flags as the VM trace writes them: six 0/1 digits in = ≠ &lt; &gt; ≤ ≥ order.</summary>
    public string FlagString
    {
        get
        {
            var c = new char[6];
            for (int k = 0; k < 6; k++)
                c[k] = (Flags >> k & 1) != 0 ? '1' : '0';
            return new string(c);
        }
    }

    public static byte PackFlags(bool[] f)
    {
        int b = 0;
        for (int k = 0; k < 6; k++)
            if (f[k])
                b |= 1 << k;
        return (byte)b;
    }

    /// <summary>Reads a stripped E9 log (lines starting with "E "; other lines are ignored).</summary>
    public static List<E9TraceLine> ReadFile(string path)
    {
        var list = new List<E9TraceLine>();
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            int at = line.IndexOf("E ", StringComparison.Ordinal);
            if (at < 0)
                continue;
            var f = line[(at + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 3)
                continue;
            list.Add(new E9TraceLine(
                ushort.Parse(f[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(f[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(f[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
        }
        return list;
    }
}
