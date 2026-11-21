using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Core.Tests.Support;
using GoldBox.Data.Por;

namespace GoldBox.Core.Tests;

/// <summary>
/// The command line's C and V (0x17054: 3D 0x170F8 / 0x1710F, overland 0x1731C / 0x17327; P + D s41) and DS:49F3
/// (<see cref="EclMemory.InterfaceState"/>): no vector, no time; the overland interface once per routine entry; START
/// 0x3379 (<see cref="AreaLoop.DrawInterface"/>) by the state.
/// </summary>
public class CommandLineMenuTests
{
    private static (PorGame Game, ScriptedAreaUi Ui) Load(byte[] save, string dir)
    {
        var content = PorData.Open();
        var ui = new ScriptedAreaUi();
        var player = new PorPlayer(new ScriptedEclHost { StopWhenInputMissing = true },
            new ScriptedEncounterUi { StopWhenInputMissing = true }, new RecordingCombatRunner(), ui);
        return (PorGame.FromSave(content, PorViewArt.Load(content), save, dir, player, 1), ui);
    }

    private static (PorGame Game, ScriptedAreaUi Ui) SaveA()
    {
        var content = PorData.Open();
        var path = content.Find("SAVGAMA.DAT")!;
        return Load(File.ReadAllBytes(path), Path.GetDirectoryName(path)!);
    }

    [PorFact]
    public void View_and_cast_run_no_vector_and_take_no_time()
    {
        var (g, ui) = SaveA();
        Assert.Equal(4, g.Memory.InterfaceState);            // the loaded game's first pass (0x405D)
        Assert.True(g.Memory.MenuAnimates);                   // save A's $4AFF (3): no menu has run since the Load
        long t0 = GameClock.TotalMinutes(g.Memory);
        ushort pc = g.Vm.Pc;
        var at = (g.Memory.Position.X, g.Memory.Position.Y);
        g.Loop.Execute(AreaCommand.View);
        g.Loop.MenuCommand(AreaCommand.Cast);
        Assert.Equal(["View", "Cast"], ui.Calls);
        Assert.Equal(t0, GameClock.TotalMinutes(g.Memory));
        Assert.Equal(pc, g.Vm.Pc);
        Assert.Equal(at, (g.Memory.Position.X, g.Memory.Position.Y));
        // 3D: Cast only with the current member's status 0x10C = 0 (0x170FC); the overland branch has no check.
        g.Memory.Party!.Current![0x10C] = 1;
        g.Loop.MenuCommand(AreaCommand.Cast);
        Assert.Equal(["View", "Cast"], ui.Calls);
        // The command line's menu clears VM $4AFF at its end (0x2CFAD).
        g.Memory.MenuAnimates = true;                         // as after a Load with $4AFF set
        g.Loop.MenuCommand(AreaCommand.View);
        Assert.False(g.Memory.MenuAnimates);
        Assert.Equal(3, g.Memory.Read(AreaLoop.MenuAnimateCell)); // the cell a later save writes keeps its value
    }

    [PorFact]
    public void On_the_overland_the_interface_is_drawn_once_per_routine_entry()
    {
        var content = PorData.Open();
        var path = content.Find("SAVGAMA.DAT")!;
        // A save made on the overland ($49F2 = 26): the load draws nothing, the first key draws the interface (+2).
        var save = PorSaveGame.PlaceOnOverland(File.ReadAllBytes(path), content, 26, 8, 19, 0);
        save[PorSaveGame.Off4900 + 2 * (0x49F2 - 0x4900)] = 26;
        var (g, ui) = Load(save, Path.GetDirectoryName(path)!);
        Assert.Equal(3, g.Memory.InterfaceState);
        int c0 = g.Loop.MarkerCounter;
        Assert.True(g.Loop.OverlandInterfacePending);
        ui.OnView = g.Loop.DrawInterface;                    // View's exit 0x21D4F: START 0x3379 case 3 (+1)
        g.Loop.Execute(AreaCommand.View);
        Assert.Equal((c0 + 3) % 6, g.Loop.MarkerCounter);
        Assert.False(g.Loop.OverlandInterfacePending);       // V loops inside the routine: DS:49F9 not set yet
        Assert.True(g.Memory.PreviousViewMode < 2);
        g.Loop.Execute(AreaCommand.TravelEast);              // no second interface; the step's redraw (+1)
        Assert.Equal((c0 + 4) % 6, g.Loop.MarkerCounter);
        Assert.Equal(g.Memory.ViewMode, g.Memory.PreviousViewMode);
    }

    [PorFact]
    public void Draw_interface_raises_the_state_and_redraws_the_view_for_3_and_4()
    {
        var (g, _) = SaveA();
        var seen = new List<byte>();
        int redraws = 0;
        g.Loop.InterfaceDrawn += seen.Add;
        g.Loop.Redrawn += () => redraws++;
        g.Loop.DrawInterface();
        g.Memory.InterfaceState = 2;
        g.Loop.DrawInterface();
        Assert.Equal([4, 2], seen);
        Assert.Equal(1, redraws);                             // case 2 draws the camp picture's frames, not the view
    }
}
