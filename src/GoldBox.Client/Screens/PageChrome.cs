using GoldBox.Client.Play;
using GoldBox.Core.Explore;

namespace GoldBox.Client.Screens;

/// <summary>
/// The character pages of View (0x21AED) as the combat screen and the party pages draw them: a <see cref="CombatPage"/>'s
/// braid, texts, list rows (the selected one lit) and warning lines, and its menu words on row 24.
/// </summary>
public static class PageChrome
{
    /// <summary>The first list row shown (the list scrolls to keep the selected row in its window).</summary>
    public static int PageTop(CombatPage p)
    {
        int visible = p.LastRow - p.FirstRow + 1;
        return Math.Clamp(p.Selected - visible + 1, 0, Math.Max(0, p.Rows.Count - visible));
    }

    /// <summary>The list row under a text row, or null.</summary>
    public static int? RowAt(CombatPage p, int row)
    {
        int i = PageTop(p) + row - p.FirstRow;
        return row >= p.FirstRow && row <= p.LastRow && i >= 0 && i < p.Rows.Count ? i : null;
    }

    /// <summary>The page on a cleared frame (<paramref name="showSelection"/> false: every row green, as the list routine
    /// draws them when it returns).</summary>
    public static void DrawBody(IndexedFrame f, TextFont font, CombatPage p, bool showSelection = true)
    {
        f.Clear(0);
        int top = p.FrameRows.Min(), bottom = p.FrameRows.Max();
        if (Chrome.AmigaFrame is not null)
        {
            // The Amiga stacks whole windows where DOS draws dividers: the Items page (hunk 10 +0x5660) blits the frame
            // of (1, 1)–(38, 22) (+0x5878), then the window (1, 3)–(38, 22) (+0x58C8, blank and frame over row 2); View
            // (+0x48E0) the frame alone. One window between each pair of frame rows, top to bottom, gives the same
            // pixels (FS-UAE s41: frame cells and row 2 equal).
            var rows = p.FrameRows.Distinct().Order().ToList();
            for (int k = 0; k + 1 < rows.Count; k++)
                Chrome.Box(f, 0, rows[k], 39, rows[k + 1]);
        }
        else
        {
            foreach (var r in p.FrameRows)
                Chrome.HLine(f, r, 0, 39);
            Chrome.VLine(f, 0, top, bottom);
            Chrome.VLine(f, 39, top, bottom);
            foreach (var r in p.FrameRows)
            {
                Chrome.BraidCell(f, 0, r, true);
                Chrome.BraidCell(f, 39, r, true);
            }
        }
        foreach (var (col, row, text, colour) in p.Texts)
            Chrome.Text(f, font, col, row, Fit(text, 39 - col), colour);
        int first = PageTop(p);
        for (int k = 0; p.FirstRow + k <= p.LastRow && first + k < p.Rows.Count; k++)
        {
            int i = first + k;
            if (i == p.Selected && p.ShowSelection && showSelection)
                Chrome.Lit(f, font, p.RowCol, p.FirstRow + k, Fit(p.Rows[i], 39 - p.RowCol), Chrome.White, listRow: true);
            else
                Chrome.Text(f, font, p.RowCol, p.FirstRow + k, Fit(p.Rows[i], 39 - p.RowCol), Chrome.Green);
        }
        for (int k = 0; k < p.Warning.Count; k++)
            Chrome.Text(f, font, 1, 21 + k, Fit(p.Warning[k], 38), Chrome.Yellow);
    }

    private static string Fit(string s, int n) => n <= 0 ? "" : s.Length > n ? s[..n] : s;
}
