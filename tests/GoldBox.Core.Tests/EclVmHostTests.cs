using GoldBox.Core.Ecl;
using GoldBox.Core.Tests.Support;
using static GoldBox.Core.Tests.Support.Opnd;

namespace GoldBox.Core.Tests;

/// <summary>Host-delegated opcodes, NEWECL and the trace.</summary>
public class EclVmHostTests
{
    private const int F = 0x4A20;

    [Fact]
    public void Every_host_opcode_reaches_the_host_with_its_operands()
    {
        var asm = new EclAsm()
            .Op(EclOpcodes.LoadChar, Imm8(1))
            .Op(EclOpcodes.LoadMon, Imm8(2), Imm8(3), Imm8(4))
            .Op(EclOpcodes.SetupMon, Imm8(5), Imm8(1), Imm8(6))
            .Op(EclOpcodes.Approach)
            .Op(EclOpcodes.Picture, Imm8(7))
            .Op(EclOpcodes.Print, Text("A"))
            .Op(EclOpcodes.PrintClr, Text("B"))
            .Op(EclOpcodes.ClrMon)
            .Op(EclOpcodes.Surprise, Imm8(1), Imm8(2), Imm8(3), Imm8(4))
            .Op(EclOpcodes.Combat)
            .Op(EclOpcodes.Treasure, Imm8(1), Imm8(2), Imm8(3), Imm8(4), Imm8(5), Imm8(6), Imm8(7), Imm8(8))
            .Op(EclOpcodes.Rob, Imm8(1), Imm8(50), Imm8(10))
            .Op(EclOpcodes.Call, Imm16(0x8000))
            .Op(EclOpcodes.Damage, Imm8(1), Imm8(2), Imm8(6), Imm8(0), Imm8(0))
            .Op(EclOpcodes.SpriteOff)
            .Op(EclOpcodes.PrintRet)
            .Op(EclOpcodes.Clock, Imm8(10))
            .Op(EclOpcodes.AddNpc, Imm8(3), Imm8(80))
            .Op(EclOpcodes.LoadFiles, Imm8(18), Imm8(0), Imm8(1))
            .Op(EclOpcodes.LoadPieces, Imm8(1), Imm8(3), Imm8(5))
            .Op(EclOpcodes.Who, Text("WHO"))
            .Op(EclOpcodes.Delay)
            .Op(EclOpcodes.Spell, Imm8(3), Mem(F), Mem(F + 1))
            .Op(EclOpcodes.Protect, Mem(0x9700))
            .Op(EclOpcodes.ClearBox)
            .Op(EclOpcodes.Program, Imm8(9)) // camp, then the run ends like EXIT
            .Op(EclOpcodes.Save, Imm8(1), Mem(F)); // not reached
        var rig = VmRig.Of(asm);
        Assert.Equal(EclStopReason.Exit, rig.Run().Reason);
        Assert.Equal(0, rig[F]);
        Assert.Equal(
        [
            "LoadCharacter 1", "LoadMonster 2 3 4", "SetupMonster 5 1 6", "Approach", "Picture 7", "Print A",
            "PrintClear B", "ClearMonsters", "Random 6", "Random 6", "Combat", "Treasure 1 2 3 4 5 6 7 8", "Rob 1 50 10",
            "Call 8000", "Damage 1 2 6 0 0", "SpriteOff", "PrintReturn", "Clock 10", "AddNpc 3 80", "LoadMap 18",
            "LoadWallSet 1 1", "LoadWallSet 2 3", "LoadWallSet 3 5", "Who WHO", "Delay", "Spell 3", "Protect 9700",
            "ClearBox", "Program 9",
        ], rig.Host.Calls);
        // Host calls are in the trace too.
        Assert.Equal(rig.Host.Calls.Count, rig.Trace.Steps.Sum(s => s.HostCalls.Count));
    }

    [Fact]
    public void Host_answers_are_written_to_destinations()
    {
        var asm = new EclAsm()
            .Op(EclOpcodes.Random, Imm8(10), Mem(F))
            .Op(EclOpcodes.InputNum, Imm8(3), Mem(F + 1))
            .Op(EclOpcodes.InputStr, Imm8(8), Mem(0x9700))
            .Op(EclOpcodes.PartyStr, Mem(F + 2))
            .Op(EclOpcodes.ChkParty, Imm8(1), Imm8(0), Mem(F + 3), Mem(F + 4), Mem(F + 5), Mem(F + 6))
            .Op(EclOpcodes.Parley, Imm8(1), Imm8(2), Imm8(3), Imm8(4), Imm8(5), Mem(F + 7))
            .Op(EclOpcodes.EncMenu, Imm8(1), Imm8(2), Imm8(3), Mem(F + 8), Imm8(1), Imm8(2), Imm8(3), Imm8(4), Imm8(5),
                Text("FAR"), Text("MID"), Text("NEAR"), Imm8(0), Imm8(0))
            .Op(EclOpcodes.Exit);
        var rig = VmRig.Of(asm);
        rig.Host.RandomValues.Enqueue(7);
        rig.Host.Numbers.Enqueue(123);
        rig.Host.Strings.Enqueue("ABC");
        rig.Host.PartyStrengthValue = 40;
        rig.Host.PartyCheckValue = new EclPartyCheck(1, 9, 5, 1);
        rig.Host.ParleyResults.Enqueue(3);
        rig.Host.EncounterResults.Enqueue(2);
        rig.Run();
        Assert.Equal(7, rig[F]);
        Assert.Equal(123, rig[F + 1]);
        Assert.Equal("ABC", rig.Memory.ReadText(0x9700));
        Assert.Equal(40, rig[F + 2]);
        Assert.Equal([1, 9, 5, 1], new[] { rig[F + 3], rig[F + 4], rig[F + 5], rig[F + 6] }.Select(x => (int)x));
        Assert.Equal(3, rig[F + 7]);
        Assert.Equal(2, rig[F + 8]);
    }

    [Fact]
    public void Menus_pass_texts_and_store_the_choice()
    {
        var asm = new EclAsm()
            .Op(EclOpcodes.VMenu, Mem(F), Text("PICK"), Imm8(2), Text("ONE"), Text("TWO"))
            .Op(EclOpcodes.HMenu, Mem(F + 1), Imm8(3), Text("YES"), Text("NO"), Text("MAYBE"))
            .Op(EclOpcodes.Exit);
        var rig = VmRig.Of(asm);
        rig.Host.MenuChoices.Enqueue(1);
        rig.Host.MenuChoices.Enqueue(2);
        rig.Run();
        Assert.Equal(1, rig[F]);
        Assert.Equal(2, rig[F + 1]);
        Assert.Equal(["VerticalMenu PICK [ONE|TWO]", "HorizontalMenu [YES|NO|MAYBE]"], rig.Host.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FindItem_sets_equality_flags(bool found)
    {
        var asm = new EclAsm().Op(EclOpcodes.FindItem, Imm8(12)).Op(EclOpcodes.Exit);
        var rig = VmRig.Of(asm);
        rig.Host.FindItemResults.Enqueue(found);
        rig.Run();
        Assert.Equal(found, rig.Vm.Flags[EclVm.FlagEq]);
        Assert.Equal(!found, rig.Vm.Flags[EclVm.FlagNe]);
    }

    private sealed class Scripts(Dictionary<(int, int), byte[]> blocks) : IEclScriptSource
    {
        public List<(int Disk, int Script)> Loads { get; } = [];
        public byte[]? LoadBlock(int disk, int script)
        {
            Loads.Add((disk, script));
            return blocks.GetValueOrDefault((disk, script));
        }
    }

    [Fact]
    public void NewEcl_stops_the_run_and_the_switch_runs_init_move_search()
    {
        var first = new EclAsm()
            .Op(EclOpcodes.Save, Imm8(3), Mem(0x6E12))
            .Op(EclOpcodes.NewEcl, Imm8(7))
            .Op(EclOpcodes.Save, Imm8(1), Mem(F)) // never reached
            .Op(EclOpcodes.Exit);
        var second = new EclAsm()
            .Label("move").Op(EclOpcodes.Save, Imm8(10), Mem(F + 1)).Op(EclOpcodes.Exit)
            .Label("search").Op(EclOpcodes.Save, Imm8(11), Mem(F + 2)).Op(EclOpcodes.Exit)
            .Label("init").Op(EclOpcodes.Save, Imm8(14), Mem(F + 3)).Op(EclOpcodes.Exit)
            .Vector(0, "move").Vector(1, "search").Vector(4, "init");
        var src = new Scripts(new() { [(3, 7)] = second.Build() });
        var rig = VmRig.Of(first);
        rig.Vm.Scripts = src;
        var r = rig.Run();
        Assert.Equal(EclStopReason.NewScript, r.Reason);
        Assert.Equal(7, rig.Vm.PendingScript);
        Assert.Equal(0, rig[F]);
        Assert.Contains("ScriptChanging 7", rig.Host.Calls);

        var after = rig.Vm.FollowScriptChanges();
        Assert.Equal(EclStopReason.Exit, after.Reason);
        Assert.Equal([(3, 7)], src.Loads);
        Assert.Equal(7, rig.Vm.CurrentScript);
        Assert.Equal(3, rig.Vm.CurrentDisk);
        Assert.Equal([10, 11, 14], new[] { rig[F + 1], rig[F + 2], rig[F + 3] }.Select(x => (int)x));
        Assert.Equal([4, 0, 1], rig.Trace.Events.Where(e => e.Kind == "run").Skip(1).Select(e => int.Parse(e.Fields["vec"])));
    }

    [Fact]
    public void Missing_script_is_a_fault()
    {
        var rig = VmRig.Of(new EclAsm().Op(EclOpcodes.NewEcl, Imm8(9)));
        rig.Vm.Scripts = new Scripts([]);
        var r = rig.Run();
        Assert.Equal(EclStopReason.Fault, r.Reason);
        Assert.Contains("no script 9", r.Message);
    }

    [Fact]
    public void Jsonl_trace_is_stable()
    {
        var asm = new EclAsm()
            .Op(EclOpcodes.Compare, Mem(F), Imm8(0))
            .Op(EclOpcodes.IfEq)
            .Op(EclOpcodes.PrintClr, Text("HI"))
            .Op(EclOpcodes.Save, Imm8(5), Mem(F))
            .Op(EclOpcodes.Exit);
        string Run()
        {
            var sw = new StringWriter();
            var mem = new EclMemory();
            var vm = new EclVm(mem, new ScriptedEclHost()) { Trace = new JsonlTraceWriter(sw) };
            vm.LoadScriptBlock(asm.Build(), 3, 2);
            vm.RunVector(0);
            return sw.ToString();
        }
        var text = Run();
        Assert.Equal(text, Run());
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("""{"k":"load","script":"3","disk":"2","vectors":"9914,9914,9914,9914,9914"}""", lines[0]);
        Assert.Equal("""{"k":"run","script":"3","vec":"0","pc":"9914"}""", lines[1]);
        Assert.Equal(
            """{"k":"step","seq":0,"script":3,"disk":2,"vec":0,"pc":"9914","op":"03","mn":"COMPARE","ops":[{"t":1,"lo":32,"hi":74,"w":"4A20"},{"t":0,"lo":0}],"rd":[{"a":"4A20","v":0}],"wr":[],"host":[],"fin":"000000","flags":"100011","sp":0,"next":"991A"}""",
            lines[2]);
        Assert.Contains("\"if\":true", lines[3]);
        Assert.Contains("""
            "host":[{"n":"PrintClear","args":["HI"]}]
            """.Trim(), lines[4]);
        Assert.Contains("""
            "wr":[{"a":"4A20","r":"4900","old":0,"new":5}]
            """.Trim(), lines[5]);
        Assert.Contains("\"end\":\"Exit\"", lines[6]);
        Assert.StartsWith("""{"k":"end","reason":"Exit","steps":"5" """.Trim(), lines[7]);
    }
}
