using System.Globalization;
using GoldBox.Client.Input;
using GoldBox.Client.Play;
using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;
using GoldBox.Data.Por;

namespace GoldBox.Client.Screens;

/// <summary>
/// The training hall's screen (GAME.OVR 0x19AE0, DOSBox-X s19): the outer frame, the party list across the top (NAME at
/// column 1 row 2, members from row 4, AC and HP right-aligned at columns 34 / 38, the current member white), the
/// menu's shown entries from row 12 at column 2 (hotkey letter white, the rest green), the prompt in colour 13 on the
/// bottom line. Train's page replaces the list and the menu. The highlight (a yellow marker) is ours, for pad and mouse.
/// Bottom-line menus (the yes/no, the save slots), messages and key waits are drawn here too while the hall is open.
/// </summary>
public sealed class HallView
{
    public const int MenuRow = 12, MenuColumn = 2, BottomRow = 24;

    private HallMenuRequest? _shown;
    private int _selected;

    public int Selected => _selected;

    private void Sync(HallMenuRequest r)
    {
        if (ReferenceEquals(r, _shown))
            return;
        _shown = r;
        _selected = 0;
    }

    public void Draw(IndexedFrame f, TextFont font, HallState hall, EclParty? party, PlayerRequest? pending,
        int choiceSelected, ActionInput input)
    {
        f.Clear(0);
        Chrome.Box(f, 0, 0, 39, 23);
        if (hall.Page is { } page)
            foreach (var (col, row, text, colour) in page)
                Chrome.Text(f, font, col, row, Fit(text, 39 - col), colour);
        else
        {
            DrawRoster(f, font, party);
            if (pending is HallMenuRequest m)
            {
                Sync(m);
                for (int i = 0; i < m.Entries.Count; i++)
                {
                    var e = m.Entries[i];
                    int row = MenuRow + i;
                    if (i == _selected)
                        Chrome.Text(f, font, MenuColumn - 1, row, ">", Chrome.Yellow);
                    Chrome.Text(f, font, MenuColumn, row, Fit(e.Text, 37), Chrome.Green);
                    if (e.Text.Length > 0)
                        Chrome.Text(f, font, MenuColumn, row, e.Text[..1], Chrome.White);
                }
            }
        }
        switch (pending)
        {
            case HallMenuRequest m:
                Chrome.Text(f, font, 0, BottomRow, Fit(m.Prompt, 40), PromptColour);
                break;
            case ChoiceRequest { Vertical: false } c:
            {
                int col = 0;
                if (c.MenuPrompt is { Length: > 0 } mp)
                {
                    Chrome.Text(f, font, 0, BottomRow, mp, PromptColour);
                    col = mp.Length + 1;
                }
                for (int i = 0; i < c.Items.Count; i++)
                {
                    var w = c.Items[i];
                    if (col + w.Length > 40)
                        break;
                    int k = Math.Max(0, w.IndexOf(c.HotkeyOf(i)));
                    Chrome.Text(f, font, col, BottomRow, w, i == choiceSelected ? Chrome.White : Chrome.Green);
                    Chrome.Text(f, font, col + k, BottomRow, w.Substring(k, 1), Chrome.White);
                    col += w.Length + 1;
                }
                break;
            }
            case KeyRequest k:
                Chrome.Text(f, font, 0, BottomRow, Fit(k.Prompt ?? "PRESS A KEY", 40), PromptColour);
                break;
        }
        if (hall.Message is { } msg)
            Chrome.Text(f, font, 0, BottomRow, Fit(msg.Text, 40), msg.Colour);
    }

    private const byte PromptColour = 13;

    private static void DrawRoster(IndexedFrame f, TextFont font, EclParty? party)
    {
        Chrome.Text(f, font, 1, 2, "NAME", Chrome.White);
        Chrome.TextRight(f, font, 34, 2, "AC", Chrome.White);
        Chrome.TextRight(f, font, 38, 2, "HP", Chrome.White);
        if (party is null)
            return;
        for (int i = 0; i < party.Members.Count && i < 8; i++)
        {
            var m = party.Members[i];
            var name = m.Name.Length > 15 ? m.Name[..15] : m.Name;
            Chrome.Text(f, font, 1, 4 + i, name.ToUpperInvariant(), i == party.CurrentIndex ? Chrome.White : Chrome.Cyan);
            Chrome.TextRight(f, font, 34, 4 + i, (60 - m[0x111]).ToString(CultureInfo.InvariantCulture), Chrome.Green);
            // DOSBox-X: a member below its maximum shows the HP in yellow [?] (the rule seen, the routine START 0x1307 not read).
            Chrome.TextRight(f, font, 38, 4 + i, m[0x11B].ToString(CultureInfo.InvariantCulture),
                m[0x11B] < m[0x32] ? Chrome.Yellow : Chrome.Green);
        }
    }

    /// <summary>The menu's answer this frame: a hotkey letter, Confirm on the highlighted entry, a member switch.</summary>
    public object? Answer(HallMenuRequest m, ActionInput input)
    {
        Sync(m);
        int n = m.Entries.Count;
        if (n == 0)
            return null;
        if (input.Pressed(GameAction.MenuUp))
            _selected = (_selected + n - 1) % n;
        if (input.Pressed(GameAction.MenuDown))
            _selected = (_selected + 1) % n;
        if (input.Pressed(GameAction.Confirm))
            return m.Entries[_selected].Key;
        if (input.Pressed(GameAction.PreviousMember))
            return ChoiceRequest.PreviousMember;
        if (input.Pressed(GameAction.NextMember))
            return ChoiceRequest.NextMember;
        if (input.NewInput is (InputDevice.Keyboard, { Length: 1 } key))
            return char.ToUpperInvariant(key[0]);
        return null;
    }

    /// <summary>A click: an entry (answered), or a member's name (picked); right click does nothing (no way out).</summary>
    public object? Click(HallMenuRequest m, int col, int row, int partySize)
    {
        Sync(m);
        if (EntryAt(m, col, row) is { } i)
        {
            _selected = i;
            return m.Entries[i].Key;
        }
        if (col is >= 1 and <= 38 && row - 4 is var p and >= 0 && p < Math.Min(partySize, 8))
            return TradeContext.MemberPick - p;
        return null;
    }

    public void Hover(HallMenuRequest m, int col, int row)
    {
        Sync(m);
        if (EntryAt(m, col, row) is { } i)
            _selected = i;
    }

    private static int? EntryAt(HallMenuRequest m, int col, int row)
    {
        int i = row - MenuRow;
        return i >= 0 && i < m.Entries.Count && col >= MenuColumn - 1 && col < MenuColumn + m.Entries[i].Text.Length ? i : null;
    }

    private static string Fit(string s, int n) => s.Length > n ? s[..n] : s;

    // ---- End game ------------------------------------------------------------------------------------------------

    /// <summary>
    /// The end game's FINAL blocks (<see cref="EndScene"/>, DOS art) at their cells, clipped to the screen; index 0 is
    /// drawn (the blocks cover the view and its box as on the original screen).
    /// </summary>
    public static void DrawScene(IndexedFrame f, EndScene scene, ViewPictures pictures)
    {
        foreach (var layer in scene.Layers)
        {
            if (pictures.Find("FINAL", layer.Block, scene.Disk, PorArtChoice.Dos) is not { Frames.Count: > 0 } img)
                continue;
            var p = img.Frames[Math.Clamp(layer.Frame, 0, img.Frames.Count - 1)];
            if (p.Items.Count == 0)
                continue;
            var px = p.Items[0];
            int x0 = (layer.Column + p.X) * 8, y0 = (layer.Row + p.Y) * 8;
            for (int r = 0; r < p.Height; r++)
                for (int c = 0; c < p.Width; c++)
                {
                    int x = x0 + c, y = y0 + r;
                    if (x >= 0 && y >= 0 && x < f.Width && y < f.Height)
                        f[x, y] = px[r * p.Width + c];
                }
        }
    }
}
