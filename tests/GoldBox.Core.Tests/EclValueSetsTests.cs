using GoldBox.Core.Ecl;
using GoldBox.Core.Tests.Support;
using static GoldBox.Core.Tests.Support.Opnd;

namespace GoldBox.Core.Tests;

/// <summary>The static value sets behind computed LOADFILES operands (<see cref="EclValueSets"/>), on assembled scripts.</summary>
public class EclValueSetsTests
{
    private const int T1 = 0x6E79, T2 = 0x6E82, Facing = 0xC04D, Disk = 0x6E12, Map = 0x49C5;

    /// <summary>The cell <paramref name="cell"/> at the script's only LOADFILES.</summary>
    private static (IReadOnlySet<int>? Values, IReadOnlySet<int> Disks) AtLoad(EclAsm a, int cell)
    {
        var an = new EclAnalysis(a.Build());
        var load = an.Instructions.Values.Single(i => i.Op == EclOpcodes.LoadFiles);
        return (EclValueSets.Before(an, load.Offset, (ushort)cell), EclValueSets.ImmediateWritesBefore(an, load.Offset, Disk));
    }

    private static EclAsm Start() => new EclAsm().Vector(0, "v").Op(EclOpcodes.Exit).Label("v");

    [Fact]
    public void A_table_indexed_by_facing_gives_its_four_bytes()
    {
        var a = Start()
            .Op(EclOpcodes.GetTable, L("tab"), Mem(Facing), Mem(T2))
            .Op(EclOpcodes.LoadFiles, Mem(T2), Imm8(2), Imm8(255))
            .Op(EclOpcodes.Exit)
            .Label("tab").Bytes(3, 5, 5, 3);
        Assert.Equal([3, 5], AtLoad(a, T2).Values!.Order());
    }

    [Fact]
    public void Random_plus_a_constant()
    {
        var a = Start()
            .Op(EclOpcodes.Random, Imm8(3), Mem(T2))
            .Op(EclOpcodes.Add, Imm8(3), Mem(T2), Mem(T1))
            .Op(EclOpcodes.LoadFiles, Mem(T1), Imm8(2), Imm8(255))
            .Op(EclOpcodes.Exit);
        Assert.Equal([3, 4, 5, 6], AtLoad(a, T1).Values!.Order());
    }

    [Fact]
    public void A_write_behind_an_if_adds_to_the_values_before_it()
    {
        var a = Start()
            .Op(EclOpcodes.Save, Mem(Facing), Mem(T2))
            .Op(EclOpcodes.Compare, Imm8(31), Mem(Map))
            .Op(EclOpcodes.IfEq)
            .Op(EclOpcodes.Add, Imm8(4), Mem(T2), Mem(T2))
            .Op(EclOpcodes.GetTable, L("tab"), Mem(T2), Mem(T1))
            .Op(EclOpcodes.Compare, Mem(T1), Imm8(26))
            .Op(EclOpcodes.IfEq)
            .Op(EclOpcodes.Save, Imm8(7), Mem(Disk))
            .Op(EclOpcodes.LoadFiles, Mem(T1), Imm8(2), Imm8(255))
            .Op(EclOpcodes.Exit)
            .Label("tab").Bytes(10, 11, 12, 13, 14, 15, 16, 17);
        var (values, disks) = AtLoad(a, T1);
        Assert.Equal(Enumerable.Range(10, 8), values!.Order());
        Assert.Equal([7], disks);
    }

    [Fact]
    public void A_value_that_can_come_from_elsewhere_is_unknown()
    {
        // Behind an IF with nothing before it but the vector: the cell may hold anything.
        var guarded = Start()
            .Op(EclOpcodes.Compare, Mem(Map), Imm8(3))
            .Op(EclOpcodes.IfEq)
            .Op(EclOpcodes.Save, Imm8(3), Mem(T2))
            .Op(EclOpcodes.LoadFiles, Mem(T2), Imm8(2), Imm8(255))
            .Op(EclOpcodes.Exit);
        Assert.Null(AtLoad(guarded, T2).Values);
        // A GOSUB on the way may write it.
        var gosub = Start()
            .Op(EclOpcodes.Save, Imm8(3), Mem(T2))
            .Op(EclOpcodes.Gosub, L("sub"))
            .Op(EclOpcodes.LoadFiles, Mem(T2), Imm8(2), Imm8(255))
            .Op(EclOpcodes.Exit)
            .Label("sub").Op(EclOpcodes.Return);
        Assert.Null(AtLoad(gosub, T2).Values);
        // A jump target between the write and the read.
        var joined = Start()
            .Op(EclOpcodes.Save, Imm8(3), Mem(T2))
            .Label("j")
            .Op(EclOpcodes.LoadFiles, Mem(T2), Imm8(2), Imm8(255))
            .Op(EclOpcodes.Goto, L("j"));
        Assert.Null(AtLoad(joined, T2).Values);
        // Engine-state cells are not followed.
        var engine = Start()
            .Op(EclOpcodes.Save, Imm8(3), Mem(Map))
            .Op(EclOpcodes.LoadFiles, Mem(Map), Imm8(2), Imm8(255))
            .Op(EclOpcodes.Exit);
        Assert.Null(AtLoad(engine, Map).Values);
    }
}
