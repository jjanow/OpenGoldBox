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
            [new HostDrawsAction(0, 5, "rest loop 1d100 per interval"), new RunVectorAction(3)], CaptureParty.SaveA, [], [Clock]),
        new("door_pick_fail", "P at the door menu (pick fails), then the search vector", true,
            [new HostDrawsAction(0, 7, "pick-lock roll"), new RunVectorAction(1)], CaptureParty.SaveA, [], [Clock, WallSlot]),
        new("door_bash", "Up, B (bash opens the door, step, event)", true,
            [new ForwardKeyAction(), new RunVectorAction(0), new HostDrawsAction(0, 3, "bash roll"), new StepAction(), new RunVectorAction(1)],
            CaptureParty.SaveA, [], [Clock, WallSlot]),
        new("combat_win", "Enter, Q per character, N (fight won)", true,
            [], CaptureParty.SaveA, [], [], AllHost: true),
        new("load_saveB", "restart, main menu → Load → B (in-game save after the fight)", false,
            [new LoadSaveAction("SAVGAMB.DAT")], CaptureParty.SaveB, [], []),
        new("edge_wrap_patched", "patched save C: Up off the east edge", true,
            [new ForwardKeyAction(), new RunVectorAction(0), new StepAction(), new RunVectorAction(1)], CaptureParty.SaveA, [], [Clock, WallSlot]),
        new("encmenu_clamp", "in camp: R, H, I×8, R (rest interrupted, encounter menu at distance 2, view 1)", true,
            [new HostDrawsAction(0, 5, "rest loop 1d100 per interval"), new RunVectorAction(3)], CaptureParty.SaveA, [], [Clock]),
        new("encmenu_advance", "same, then A (advance) at the encounter menu", true,
            [new HostDrawsAction(0, 5, "rest loop 1d100 per interval"), new AnswersAction(3), new RunVectorAction(3)],
            CaptureParty.SaveA, [], [Clock]),
        new("trace_encmenu", "boot, Load A, E, R, H, I×8, R, A (advance), F (flee) (E9 trace)", false,
            [new LoadSaveAction("SAVGAMA.DAT"), new RunVectorAction(2), new AnswersAction(3, 2), new RunVectorAction(3)],
            CaptureParty.SaveA, ["trace_encmenu/trace.txt"], []),
        new("trace_load_step", "boot, Load A, Up once (E9 trace)", false,
            [new LoadSaveAction("SAVGAMA.DAT"), new ForwardKeyAction(), new RunVectorAction(0), new StepAction("step_west"), new RunVectorAction(1)],
            CaptureParty.SaveA, ["trace_load_step/trace.txt"], []),
        new("trace_camp_rest", "same boot, then E, R, H, I×8, R (E9 trace)", false,
            [new LoadSaveAction("SAVGAMA.DAT"), new ForwardKeyAction(), new RunVectorAction(0), new StepAction("step_west"), new RunVectorAction(1),
             new RunVectorAction(2), new RunVectorAction(3)],
            CaptureParty.SaveA, ["trace_load_step/trace.txt", "trace_camp_rest/trace.txt"], []),
        // Patched save D: vector 4 runs test code (CLOCK after GOSUB, special reads, PRINT of numbers) and waits in an
        // HMENU; vector 0 points at an 0x1F byte, which hangs (the trace keeps only the first of its lines).
        new("ecl_open_items", "patched save D: Load D, state at the HMENU of vector 4", false,
            [new LoadSaveAction("SAVGAMD.DAT")], CaptureParty.SaveA, [], [Clock]),
        new("trace_ecl_open_items", "boot, Load D, Enter at the HMENU, Up (0x1F hang; E9 trace)", false,
            [new AnswersAction(0), new LoadSaveAction("SAVGAMD.DAT"), new ForwardKeyAction(), new RunVectorAction(0)],
            CaptureParty.SaveA, ["trace_ecl_open_items/trace.txt"], []),
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
