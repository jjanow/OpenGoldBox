using GoldBox.Client.Input;
using GoldBox.Core.Explore;

namespace GoldBox.Client.Screens;

/// <summary>One screen of the Client. Screens read actions only (never keys or buttons) and draw into the 320×200 frame.</summary>
public interface IScreen
{
    /// <summary>The action a mouse click on this screen stands for, or null.</summary>
    GameAction? MapClick(ScreenClick click);

    /// <summary>Called when the mouse points at a frame pixel (menus highlight what is under it).</summary>
    void Hover(int x, int y) { }

    void Update(ActionInput input);

    void Draw(IndexedFrame frame, ActionInput input);
}

/// <summary>What screens may ask of the game: open and close screens, quit.</summary>
public interface IScreenHost
{
    void Push(IScreen screen);
    void Pop();
    void Quit();
    void SaveBindings();
}

/// <summary>Text and frame drawing in the original's 40 × 25 text-cell grid.</summary>
public static class Chrome
{
    public const int Columns = 40, Rows = 25;

    /// <summary>EGA colours the original screens use for text (measured in DOSBox-X: hotkey letter white, rest green).</summary>
    public const byte White = 15, Green = 10, Cyan = 11, Yellow = 14, Red = 4, LightRed = 12, Grey = 7;

    public static void Text(IndexedFrame f, TextFont font, int col, int row, string text, byte colour) =>
        font.Draw(f, col * 8, row * 8, text, colour);

    /// <summary>Blank <paramref name="cells"/> cells from (col, row).</summary>
    public static void Clear(IndexedFrame f, int col, int row, int cells)
    {
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < cells * 8; x++)
                f[col * 8 + x, row * 8 + y] = 0;
    }

    /// <summary>Right-aligned text ending at column <paramref name="endCol"/> (inclusive).</summary>
    public static void TextRight(IndexedFrame f, TextFont font, int endCol, int row, string text, byte colour) =>
        Text(f, font, endCol - text.Length + 1, row, text, colour);

    /// <summary>
    /// A frame cell. [?] The original's braided frame picture is not decoded yet: this is a stand-in in the same
    /// colours (red and light red, yellow corners) on the same cells.
    /// </summary>
    public static void BraidCell(IndexedFrame f, int col, int row, bool corner)
    {
        int x0 = col * 8, y0 = row * 8;
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                byte c;
                if (corner)
                    c = x is 0 or 7 || y is 0 or 7 || x is 2 or 5 || y is 2 or 5 ? Yellow : (byte)0;
                else
                    c = ((x + y) % 4) switch { 0 => LightRed, 1 => Red, _ => (byte)0 };
                f[x0 + x, y0 + y] = c;
            }
    }

    public static void HLine(IndexedFrame f, int row, int col0, int col1)
    {
        for (int c = col0; c <= col1; c++)
            BraidCell(f, c, row, false);
    }

    public static void VLine(IndexedFrame f, int col, int row0, int row1)
    {
        for (int r = row0; r <= row1; r++)
            BraidCell(f, col, r, false);
    }

    public static void Box(IndexedFrame f, int col0, int row0, int col1, int row1)
    {
        HLine(f, row0, col0, col1);
        HLine(f, row1, col0, col1);
        VLine(f, col0, row0, row1);
        VLine(f, col1, row0, row1);
        foreach (var (c, r) in new[] { (col0, row0), (col1, row0), (col0, row1), (col1, row1) })
            BraidCell(f, c, r, true);
    }
}
