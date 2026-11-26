namespace GoldBox.Client.Screens;

/// <summary>
/// The list routine 0x2D669's window moves (P, s30; combat-menu.md "Spell lists' Next / Prev"), shared by the list
/// pages (<see cref="ListPage"/>) and VMENU (<see cref="ExploreScreen"/>): a window of <c>rows</c> rows from
/// <c>top</c> over <c>n</c> entries, the highlight on <c>row</c>. Label rows are the caller's.
/// </summary>
public static class ListWindowSteps
{
    /// <summary>0x2D594: the highlight one row down (up), wrapping inside the window (down: past the window's last row
    /// or the list's → the top; up: above the top → the window's last row, past the list's last → the last).</summary>
    public static int Step(int row, int top, int rows, int n, bool down)
    {
        if (down)
        {
            row++;
            if (top + rows - 1 < row) row = top;
            if (n - 1 < row) row = top;
        }
        else
        {
            row--;
            if (row < top) row = top + rows - 1;
            if (n - 1 < row) row = n - 1;
        }
        return row;
    }

    /// <summary>0x2D3D2: past label rows in the direction, at most <paramref name="rows"/> moves, each the step of
    /// <see cref="Step"/> (the routine inlines the same wrapping).</summary>
    public static int SkipLabels(int row, int top, int rows, int n, bool down, Func<int, bool> isLabel)
    {
        for (int k = 0; k < rows && isLabel(row); k++)
            row = Step(row, top, rows, n, down);
        return row;
    }

    /// <summary>0x2D502: the window a page down (up) by its rows, at most n − rows (at least 0), the highlight at the same
    /// place in it.</summary>
    public static (int Row, int Top) Page(int row, int top, int rows, int n, bool down)
    {
        int offset = row - top;
        if (down)
        {
            top += rows;
            if (n - rows < top) top = n - rows;
        }
        else
        {
            top -= rows;
            if (top < 0) top = 0;
        }
        return (top + offset, top);
    }

    /// <summary>The menu line's Next (count − rows &gt; top) and Prev (top &gt; the leading labels) words (0x2D7E6–0x2D854).</summary>
    public static (bool Next, bool Prev) Words(int top, int rows, int n, int leadingLabels = 0) =>
        (n - rows > top, top > leadingLabels);
}
