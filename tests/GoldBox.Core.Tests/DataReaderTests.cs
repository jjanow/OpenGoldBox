using GoldBox.Core.Ecl;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Dax;
using GoldBox.Data.Por;
using static GoldBox.Core.Tests.Support.Opnd;

namespace GoldBox.Core.Tests;

public class DaxFileTests
{
    /// <summary>Builds a DOS DAX file with one block per (id, packed bytes, raw size).</summary>
    private static byte[] BuildDax(params (byte Id, byte[] Packed, int Raw)[] blocks)
    {
        var index = new List<byte>();
        var data = new List<byte>();
        foreach (var (id, packed, raw) in blocks)
        {
            int off = data.Count;
            index.Add(id);
            index.AddRange(BitConverter.GetBytes(off));
            index.AddRange(BitConverter.GetBytes((ushort)raw));
            index.AddRange(BitConverter.GetBytes((ushort)packed.Length));
            data.AddRange(packed);
        }
        return [.. BitConverter.GetBytes((ushort)index.Count), .. index, .. data];
    }

    [Fact]
    public void Literal_and_repeat_runs()
    {
        // literal of 3 (control 2), then 4 x 0x55 (control -4 = 0xFC)
        byte[] packed = [2, 1, 2, 3, 0xFC, 0x55];
        var dax = new DaxFile(BuildDax((9, packed, 7), (4, [0, 42], 1)));
        Assert.Equal([1, 2, 3, 0x55, 0x55, 0x55, 0x55], dax.Block(9));
        Assert.Equal([42], dax.Block(4));
        Assert.Null(dax.Block(5));
    }

    [Theory]
    [InlineData(0x7F)]
    [InlineData(0x80)]
    public void Control_bytes_that_hang_the_original_are_rejected(byte c)
    {
        var dax = new DaxFile(BuildDax((1, [c, 0], 1)));
        Assert.Throws<DaxFormatException>(() => dax.Block(1));
    }

    [Fact]
    public void Size_mismatch_is_rejected()
    {
        var dax = new DaxFile(BuildDax((1, [1, 7, 7], 3)));
        Assert.Throws<DaxFormatException>(() => dax.Block(1));
    }

    [Fact]
    public void Bad_index_is_rejected()
    {
        Assert.Throws<DaxFormatException>(() => new DaxFile([10, 0, 1, 2]));
    }
}

public class PorSaveGameTests
{
    [Fact]
    public void Synthetic_save_fills_vm_memory_and_position()
    {
        var script = new EclAsm().Label("init").Op(EclOpcodes.Save, Imm8(1), Mem(0x4A30)).Op(EclOpcodes.Exit)
            .Vector(4, "init").Build();
        var raw = new byte[PorSaveGame.Size];
        raw[PorSaveGame.OffDisk] = 2;
        void Word(int off, int cell, ushort v) => BitConverter.GetBytes(v).CopyTo(raw, off + 2 * cell);
        Word(PorSaveGame.Off4900, 0xC5, 20);          // $49C5 map id
        Word(PorSaveGame.Off4900, 0x100, 7);          // $4A00
        Word(PorSaveGame.Off6B00, 0x312, 2);          // $6E12 disk
        Word(PorSaveGame.Off9700, 0x1FF, 0x1234);     // $98FF
        script.AsSpan(EclLayout.Prefix).CopyTo(raw.AsSpan(PorSaveGame.OffScript));
        raw[PorSaveGame.OffPosition] = 14;
        raw[PorSaveGame.OffPosition + 1] = 5;
        raw[PorSaveGame.OffPosition + 2] = 4;
        raw[PorSaveGame.OffPartySize] = 1;
        raw[PorSaveGame.OffNames] = 8;
        "CHRDATA1"u8.ToArray().CopyTo(raw, PorSaveGame.OffNames + 1);

        var save = new PorSaveGame(raw);
        Assert.Equal(20, save.ScriptId);
        Assert.Equal(["CHRDATA1"], save.PartyFileStems);
        Assert.Equal(0, save.ScriptAreaMismatches(script));

        var vm = new EclVm(new EclMemory(), new ScriptedEclHost());
        save.ApplyTo(vm);
        Assert.Equal(7, vm.Memory.Read(0x4A00)); // no $4A00 reset on restore
        Assert.Equal(2, vm.Memory.Read(0x6E12));
        Assert.Equal(0x1234, vm.Memory.Read(0x98FF));
        Assert.Equal(14, vm.Memory.Read(0xC04B));
        Assert.Equal(2, vm.Memory.Read(0xC04D));
        Assert.Equal(20, vm.CurrentScript);
        Assert.Equal(EclStopReason.Exit, vm.RunVector(EclLayout.VectorInit).Reason);
        Assert.Equal(1, vm.Memory.Read(0x4A30));
    }

    [Fact]
    public void Wrong_size_is_rejected() => Assert.Throws<FormatException>(() => new PorSaveGame(new byte[100]));
}
