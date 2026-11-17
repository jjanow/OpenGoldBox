namespace GoldBox.Data.Por.Dosbox;

/// <summary>
/// How to replay each golden capture of <c>tests/fixtures-private/por/dosbox-v1.3/</c> (its README lists the
/// inputs). Holds no game data: only which vectors the program runs for each input (ecl-format.md "Vector roles")
/// and which cells the program changes outside the VM.
/// </summary>
public static class PorGoldenCaptures
{
    private static readonly (ushort, ushort, string) Clock = (0x49C6, 0x49CC, "game clock, advanced by the program");
    private static readonly (ushort, ushort, string) WallSlot = (0x4AFF, 0x4AFF, "wall-set slot, program bookkeeping");
    private static readonly (ushort, ushort, string) Distance =
        (0x6DC0, 0x6DC1, "encounter distance: written inside SETUPMON/APPROACH (host, Lee only; [?] in ecl-format.md)");

    public static IReadOnlyList<CaptureScenario> All { get; } =
    [
        new("load_saveA", "main menu → Load → A", false,
            [new LoadSaveAction("SAVGAMA.DAT")], CaptureParty.SaveA, [], []),
        new("step_west", "Up (one step west)", true,
            [new ForwardKeyAction(), new RunVectorAction(0), new StepAction(), new RunVectorAction(1)], CaptureParty.SaveA, [], [Clock, WallSlot]),
        new("turn_left", "Left (turn)", true,
            [new MoveAction()], CaptureParty.SaveA, [], []),
        new("encamp", "E (encamp)", true,
            [new RunVectorAction(2)], CaptureParty.SaveA, [], [WallSlot]),
        new("rest_interrupted", "in camp: R, H, I×8, R (rest interrupted)", true,
            [new HostDrawsAction(0, 5, "rest loop 1d100 per interval"), new RunVectorAction(3)], CaptureParty.SaveA, [], [Clock, Distance]),
        new("door_pick_fail", "P at the door menu (pick fails), then the search vector", true,
            [new HostDrawsAction(0, 7, "pick-lock roll"), new RunVectorAction(1)], CaptureParty.SaveA, [], [Clock, WallSlot]),
        new("door_bash", "Up, B (bash opens the door, step, event)", true,
            [new ForwardKeyAction(), new RunVectorAction(0), new HostDrawsAction(0, 3, "bash roll"), new StepAction(), new RunVectorAction(1)],
            CaptureParty.SaveA, [], [Clock, WallSlot, Distance]),
        new("combat_win", "Enter, Q per character, N (fight won)", true,
            [], CaptureParty.SaveA, [], [], AllHost: true),
        new("load_saveB", "restart, main menu → Load → B (in-game save after the fight)", false,
            [new LoadSaveAction("SAVGAMB.DAT")], CaptureParty.SaveB, [], []),
        new("edge_wrap_patched", "patched save C: Up off the east edge", true,
            [new ForwardKeyAction(), new RunVectorAction(0), new StepAction(), new RunVectorAction(1)], CaptureParty.SaveA, [], [Clock, WallSlot]),
        new("trace_load_step", "boot, Load A, Up once (E9 trace)", false,
            [new LoadSaveAction("SAVGAMA.DAT"), new ForwardKeyAction(), new RunVectorAction(0), new StepAction("step_west"), new RunVectorAction(1)],
            CaptureParty.SaveA, ["trace_load_step/trace.txt"], []),
        new("trace_camp_rest", "same boot, then E, R, H, I×8, R (E9 trace)", false,
            [new LoadSaveAction("SAVGAMA.DAT"), new ForwardKeyAction(), new RunVectorAction(0), new StepAction("step_west"), new RunVectorAction(1),
             new RunVectorAction(2), new RunVectorAction(3)],
            CaptureParty.SaveA, ["trace_load_step/trace.txt", "trace_camp_rest/trace.txt"], []),
    ];

    public static CaptureScenario? Find(string name) => All.FirstOrDefault(s => s.Name == name);

    /// <summary>Finds <c>tests/fixtures-private/por/dosbox-v1.3</c> above <paramref name="start"/>, or null.</summary>
    public static string? FindFixtures(string start)
    {
        for (var d = new DirectoryInfo(start); d is not null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "tests", "fixtures-private", "por", "dosbox-v1.3");
            if (Directory.Exists(p))
                return p;
        }
        return null;
    }
}
