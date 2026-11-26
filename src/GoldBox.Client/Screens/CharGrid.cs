using GoldBox.Client.Input;
using GoldBox.Core.Explore;

namespace GoldBox.Client.Screens;

/// <summary>
/// Text entry for every device: typing on the keyboard, or a grid of characters for the pad and the mouse (13 to a row
/// from column 2, two columns apart, then DEL and OK under them). The explore screen's and the party menu's text
/// entries use its layout; <see cref="Edit"/> is the whole entry for a screen of our own (the controls screen).
/// </summary>
public sealed class CharGrid(string charset, int firstRow, int maxLength)
{
    public const int Columns = 13;

    /// <summary>The letters, digits, space, '-' and '.' (names and lines).</summary>
    public const string Letters = "ABCDEFGHIJKLM" + "NOPQRSTUVWXYZ" + "0123456789 -.";

    public string Charset => charset;
    public int FirstRow => firstRow;

    /// <summary>The typed text.</summary>
    public string Typed { get; set; } = "";

    /// <summary>The highlighted cell.</summary>
    public int Selected { get; set; }

    /// <summary>Cells: the characters, then DEL and OK.</summary>
    public static int CellCount(string charset) => charset.Length + 2;

    /// <summary>Cell <paramref name="k"/> at (column, row) with its label.</summary>
    public static (int Col, int Row, string Label) Cell(string charset, int firstRow, int k)
    {
        if (k < charset.Length)
            return (2 + 2 * (k % Columns), firstRow + k / Columns, charset[k] == ' ' ? "_" : charset[k].ToString());
        int lastRow = firstRow + (charset.Length + Columns - 1) / Columns;
        return k == charset.Length ? (2, lastRow, "DEL") : (8, lastRow, "OK");
    }

    /// <summary>The cell under a screen cell, or null.</summary>
    public static int? CellAt(string charset, int firstRow, int col, int row)
    {
        for (int k = 0; k < CellCount(charset); k++)
        {
            var (c, r, label) = Cell(charset, firstRow, k);
            if (row == r && col >= c && col < c + label.Length)
                return k;
        }
        return null;
    }

    /// <summary>The character a key types (letters, digits, keypad digits, space, '-' and '.'), or null.</summary>
    public static char? KeyChar(string name) => name switch
    {
        { Length: 1 } when char.IsAsciiLetterUpper(name[0]) => name[0],
        { Length: 2 } when name[0] == 'D' && char.IsAsciiDigit(name[1]) => name[1],
        _ when name.StartsWith("NumPad", StringComparison.Ordinal) && name.Length == 7 => name[6],
        "Space" => ' ',
        "OemMinus" or "Subtract" => '-',
        "OemPeriod" or "Decimal" => '.',
        _ => null,
    };

    /// <summary>
    /// The character this frame's key types on a line input: any printable key as the keyboard gives it with Shift held
    /// or not (<see cref="DosKeys.Printable"/>: Shift-1 '!', the punctuation keys; the request filters them, as START
    /// 0x7A73 takes 0x20–0x7A, <see cref="GoldBox.Client.Play.TextRequest.Takes"/>), a letter as a capital, or with
    /// <paramref name="keepsCase"/> (the Amiga's requester, <see cref="GoldBox.Client.Play.TextRequest.KeepsCase"/>)
    /// lower case unless <see cref="ActionInput.NewShifted"/>.
    /// </summary>
    public static char? KeyChar(string name, ActionInput input, bool keepsCase) =>
        DosKeys.Printable(name, input.NewShiftHeld) is { } ch && keepsCase && char.IsAsciiLetter(ch) && !input.NewShifted
            ? char.ToLowerInvariant(ch)
            : DosKeys.Printable(name, input.NewShiftHeld);

    /// <summary>Whether <paramref name="charset"/> holds <paramref name="ch"/>, a lower-case letter by its capital.</summary>
    public static bool Holds(string charset, char ch) => charset.Contains(char.ToUpperInvariant(ch), StringComparison.Ordinal);

    /// <summary>Letters typed lower case unless Shift (<see cref="KeyChar(string, ActionInput, bool)"/>).</summary>
    public bool KeepsCase { get; init; }

    /// <summary>The characters the entry takes (a line input's rule, <see cref="GoldBox.Client.Play.TextRequest.Takes"/>);
    /// null: the grid's own characters.</summary>
    public Func<char, bool>? Takes { get; init; }

    private void Type(char ch)
    {
        if (Typed.Length < maxLength && (Takes?.Invoke(ch) ?? Holds(charset, ch)))
            Typed += ch;
    }

    private void Backspace()
    {
        if (Typed.Length > 0)
            Typed = Typed[..^1];
    }

    /// <summary>
    /// One frame of input: keys type, Backspace deletes, Enter finishes; the arrows / pad move over the grid, Confirm
    /// types the cell (DEL deletes, OK finishes), Cancel deletes the last character or, with nothing typed, gives up.
    /// True when finished (OK / Enter), false when given up, null while editing.
    /// </summary>
    public bool? Edit(ActionInput input)
    {
        int cells = CellCount(charset);
        if (input.NewInput is (InputDevice.Keyboard, var name))
        {
            if (name == "Enter")
                return true;
            if (name == "Back")
            {
                Backspace();
                return null;
            }
            if (KeyChar(name, input, KeepsCase) is { } ch)
            {
                Type(ch);
                return null;
            }
            if (name is not ("Up" or "Down" or "Left" or "Right" or "Escape"))
                return null;
        }
        if (input.Pressed(GameAction.MenuLeft)) Selected = (Selected + cells - 1) % cells;
        if (input.Pressed(GameAction.MenuRight)) Selected = (Selected + 1) % cells;
        if (input.Pressed(GameAction.MenuUp)) Selected = Math.Max(0, Selected - Columns);
        if (input.Pressed(GameAction.MenuDown)) Selected = Math.Min(cells - 1, Selected + Columns);
        if (input.Pressed(GameAction.Cancel))
        {
            if (Typed.Length == 0)
                return false;
            Backspace();
        }
        if (input.Pressed(GameAction.Confirm))
        {
            if (Selected < charset.Length)
                Type(charset[Selected]);
            else if (Selected == charset.Length)
                Backspace();
            else
                return true;
        }
        return null;
    }

    /// <summary>A click on the grid: the cell is highlighted and pressed (true: OK was clicked).</summary>
    public bool? Click(ScreenClick click)
    {
        if (click.Right)
        {
            Backspace();
            return null;
        }
        if (CellAt(charset, firstRow, click.X / 8, click.Y / 8) is not { } k)
            return null;
        Selected = k;
        if (k < charset.Length)
            Type(charset[k]);
        else if (k == charset.Length)
            Backspace();
        else
            return true;
        return null;
    }

    public void Hover(int x, int y)
    {
        if (CellAt(charset, firstRow, x / 8, y / 8) is { } k)
            Selected = k;
    }

    public void Draw(IndexedFrame f, TextFont font)
    {
        for (int k = 0; k < CellCount(charset); k++)
        {
            var (c, r, label) = Cell(charset, firstRow, k);
            Chrome.Text(f, font, c, r, label, k == Selected ? Chrome.White : Chrome.Green);
            if (k == Selected)
                Chrome.Text(f, font, c - 1, r, ">", Chrome.Yellow);
        }
    }
}
