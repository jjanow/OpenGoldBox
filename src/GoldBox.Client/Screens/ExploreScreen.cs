using GoldBox.Client.Input;
using GoldBox.Core.Explore;
using GoldBox.Data.Por;

namespace GoldBox.Client.Screens;

/// <summary>
/// The exploring screen of a 3D area, laid out like the original (DOSBox-X, PoR v1.3 EGA): the 88×88 view at pixel
/// (24, 24), the party list on the right, the position line under it, a text window, and the command line at the
/// bottom. Movement as the program's command loop does it (docs/re/view3d.md "Movement"); the area script's step
/// vectors, doors, the clock and the other commands are not in yet.
/// </summary>
public sealed class ExploreScreen(PorArea area, PorViewArt art, IScreenHost host) : IScreen
{
    private const int ViewX = 24, ViewY = 24;
    private const int StatusRow = 15, TextRow = 17, HelpRow = 21, MenuRow = 24;

    /// <summary>
    /// The command line, read from the player's START.EXE (the first word is the area map); our own one-word stand-in
    /// when it is not found. Only the area map works so far; the others answer that they are not in yet.
    /// </summary>
    private readonly string[] Commands =
        art.Program.ExploreCommands is { Count: > 0 } c ? [.. c.Select(w => w.ToUpperInvariant())] : ["MAP"];

    private bool _areaMap;
    private string _message = "";

    public PorArea Area => area;
    public bool ShowingAreaMap => _areaMap;

    public GameAction? MapClick(ScreenClick click)
    {
        if (click.Right)
            return null;
        int col = click.X / 8, row = click.Y / 8;
        // The view: thirds left / right turn; the middle steps forward, its bottom third turns about.
        if (click.X >= ViewX && click.X < ViewX + 88 && click.Y >= ViewY && click.Y < ViewY + 88)
        {
            int vx = click.X - ViewX, vy = click.Y - ViewY;
            if (vx < 29) return GameAction.TurnLeft;
            if (vx >= 59) return GameAction.TurnRight;
            return vy >= 59 ? GameAction.TurnAround : GameAction.Forward;
        }
        if (row == MenuRow && CommandAt(col) is { } cmd)
            return cmd == 0 ? GameAction.AreaMap : NotYet(Commands[cmd]);
        if (row == HelpRow + 1)
            return GameAction.Controls;
        return null;
    }

    private GameAction? NotYet(string command)
    {
        _message = command + ": NOT IN THIS BUILD YET.";
        return null;
    }

    /// <summary>The command under a column of the command line (words separated by one space).</summary>
    private int? CommandAt(int col)
    {
        int c = 0;
        for (int i = 0; i < Commands.Length; i++)
        {
            if (col >= c && col < c + Commands[i].Length)
                return i;
            c += Commands[i].Length + 1;
        }
        return null;
    }

    public void Update(ActionInput input)
    {
        var p = area.Party;
        if (input.Pressed(GameAction.Controls))
        {
            host.Push(new ControlsScreen(art.Font, host));
            return;
        }
        if (input.Pressed(GameAction.AreaMap))
        {
            if (area.AreaMapAllowed)
            {
                _areaMap = !_areaMap;
                _message = "";
            }
            else
                _message = "NO AREA MAP HERE.";
        }
        if (input.Pressed(GameAction.TurnLeft)) Turn(p.TurnLeft);
        if (input.Pressed(GameAction.TurnRight)) Turn(p.TurnRight);
        if (input.Pressed(GameAction.TurnAround)) Turn(p.TurnAround);
        if (input.Pressed(GameAction.Forward))
        {
            var r = p.StepForward(area.Map);
            _message = r.Outcome switch
            {
                StepOutcome.Door => "A DOOR. DOOR MENU NOT IN THIS BUILD YET.",
                StepOutcome.Moved when r.LeftMap => "OFF THE MAP EDGE: WRAPPED (EXITS NOT IN YET).",
                _ => "",
            };
        }
    }

    private void Turn(Action turn)
    {
        turn();
        _message = "";
    }

    public void Draw(IndexedFrame f, ActionInput input)
    {
        var font = art.Font;
        f.Clear(0);
        // Frame cells as on the original screen: outer box, the divider under the view and party list, the divider
        // between view and party list, the view's own box, the box over the command line.
        Chrome.Box(f, 0, 0, 39, 23);
        Chrome.HLine(f, 16, 1, 38);
        Chrome.VLine(f, 16, 1, 15);
        Chrome.Box(f, 2, 2, 14, 14);
        foreach (var (c, r) in new[] { (16, 0), (0, 16), (16, 16), (39, 16) })
            Chrome.BraidCell(f, c, r, true);

        area.DrawView(f, ViewX, ViewY, art, _areaMap);
        DrawRoster(f, font);

        var p = area.Party;
        Chrome.Text(f, font, 17, StatusRow, $"{p.X},{p.Y} {Facing.Letter(p.Facing)}", Chrome.Green);

        if (_message.Length > 0)
            Chrome.Text(f, font, 1, TextRow, _message.Length > 38 ? _message[..38] : _message, Chrome.White);
        DrawHelp(f, font, input);

        int col = 0;
        // As in the original's menu line: the highlighted item (the first) all white, the others with a white hotkey letter.
        for (int i = 0; i < Commands.Length; i++)
        {
            var cmd = Commands[i];
            Chrome.Text(f, font, col, MenuRow, cmd[..1], Chrome.White);
            Chrome.Text(f, font, col + 1, MenuRow, cmd[1..], i == 0 ? Chrome.White : Chrome.Green);
            col += cmd.Length + 1;
        }
    }

    private void DrawRoster(IndexedFrame f, TextFont font)
    {
        Chrome.Text(f, font, 17, 2, "NAME", Chrome.White);
        Chrome.TextRight(f, font, 34, 2, "AC", Chrome.White);
        Chrome.TextRight(f, font, 38, 2, "HP", Chrome.White);
        if (area.Roster is not { } party)
            return;
        for (int i = 0; i < party.Members.Count && i < 8; i++)
        {
            var m = party.Members[i];
            byte colour = i == 0 ? Chrome.White : Chrome.Cyan;
            var name = m.Name.Length > 15 ? m.Name[..15] : m.Name;
            Chrome.Text(f, font, 17, 4 + i, name, colour);
            // [?] AC shown as 60 − record byte 0x111 and HP as record byte 0x11B (PARTYSTR's fields, ecl-format.md);
            // checked only against the six members of save A.
            Chrome.TextRight(f, font, 34, 4 + i, (60 - m[0x111]).ToString(System.Globalization.CultureInfo.InvariantCulture), Chrome.Green);
            Chrome.TextRight(f, font, 38, 4 + i, m[0x11B].ToString(System.Globalization.CultureInfo.InvariantCulture), Chrome.Green);
        }
    }

    /// <summary>Two help lines in the text window, in the device used last (keys, pad buttons, or mouse gestures).</summary>
    private static void DrawHelp(IndexedFrame f, TextFont font, ActionInput input)
    {
        var d = input.LastDevice;
        var b = input.Bindings;
        string L(GameAction a) => PromptLabels.For(b, a, d);
        string move;
        if (d == InputDevice.Mouse)
            move = "MOVE: CLICK THE VIEW";
        else
        {
            var four = new[] { L(GameAction.Forward), L(GameAction.TurnLeft), L(GameAction.TurnRight), L(GameAction.TurnAround) };
            move = four.SequenceEqual(["UP", "LEFT", "RIGHT", "DOWN"]) ? "MOVE: ARROW KEYS"
                : four.SequenceEqual(["DPAD UP", "DPAD LEFT", "DPAD RIGHT", "DPAD DOWN"]) ? "MOVE: DPAD OR LEFT STICK"
                : "MOVE: " + string.Join("/", four);
        }
        Chrome.Text(f, font, 1, HelpRow, move, Chrome.Grey);
        string area = d == InputDevice.Mouse ? "CLICK AREA" : L(GameAction.AreaMap);
        string controls = d == InputDevice.Mouse ? "CLICK HERE" : L(GameAction.Controls);
        Chrome.Text(f, font, 1, HelpRow + 1, $"MAP: {area}  CONTROLS: {controls}", Chrome.Grey);
    }
}
