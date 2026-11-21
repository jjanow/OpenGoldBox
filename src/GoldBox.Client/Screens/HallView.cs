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

    /// <summary>The hall session and its last menu: the entries stay on the page under the bottom-line questions.</summary>
    private HallState? _hall;
    private HallMenuRequest? _lastMenu;

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
        if (!ReferenceEquals(hall, _hall))
            (_hall, _lastMenu) = (hall, null);
        if (pending is HallMenuRequest current)
            _lastMenu = current;
        if (hall.Page is { } page)
            foreach (var (col, row, text, colour) in page)
                Chrome.Text(f, font, col, row, Fit(text, 39 - col), colour);
        else
        {
            DrawRoster(f, font, party);
            // The menu routine draws only row 24: the entries the loop drew stay under its other questions (Load's and
            // Save's slots, the yes/no, the messages; DOSBox-X s42: Load over an empty party shows the four entries).
            if (_lastMenu is { } m)
            {
                Sync(m);
                for (int i = 0; i < m.Entries.Count; i++)
                {
                    var e = m.Entries[i];
                    int row = MenuRow + i;
                    // The original marks no entry (as the party menu); the pad and the mouse get a pointer.
                    if (i == _selected && pending is HallMenuRequest && input.LastDevice != InputDevice.Keyboard)
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
                    Chrome.MenuPrompt(f, font, 0, BottomRow, mp, PromptColour);
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

    /// <summary>The party list with its heading; none for an empty party, as the party menu (DOSBox-X s42: the hall after
    /// Remove × 6 shows the four entries alone).</summary>
    private static void DrawRoster(IndexedFrame f, TextFont font, EclParty? party)
    {
        if (party is { Members.Count: > 0 })
            Chrome.PartyList(f, font, party, 1);
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
        // The hidden J (0x19E2C), from its binding (the pad's left stick) as from the J key.
        if (input.Pressed(GameAction.HallJ))
            return HallScreens.PasswordKey;
        if (input.NewInput is (InputDevice.Keyboard, { Length: 1 } key))
            return char.ToUpperInvariant(key[0]);
        return null;
    }

    /// <summary>
    /// A click: an entry (answered), a member's name (picked), or the prompt (the hidden J); right click does nothing
    /// (no way out).
    /// </summary>
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
        // The prompt on the bottom line: the hidden J for the mouse (the original shows no entry for it).
        if (row == BottomRow && col < m.Prompt.Length)
            return HallScreens.PasswordKey;
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
    /// The end game's FINAL blocks (<see cref="EndScene"/>) at their cells, clipped to the screen; index 0 is drawn (the
    /// DOS blocks cover the view and its box as on the original screen). DOS art, or for the Amiga sequence
    /// (<see cref="EndScene.Amiga"/>) the chosen art with its indices moved into its palette run (the Amiga sets table 4
    /// for block 2 and table 3 for the others, h13 +0x2612; the importer gives each block that table).
    /// </summary>
    public static void DrawScene(IndexedFrame f, EndScene scene, ViewPictures pictures)
    {
        foreach (var layer in scene.Layers)
        {
            var img = scene.Amiga
                ? pictures.Find("FINAL", layer.Block, scene.Disk, anyDisk: true)
                : pictures.Find("FINAL", layer.Block, scene.Disk, PorArtChoice.Dos, anyDisk: true);
            if (img is not { Frames.Count: > 0 })
                continue;
            int b = scene.Amiga ? pictures.RunBase(img) : 0;
            // Later frames of a FINAL block are XOR deltas against the first (loader 0x2E170, as PIC; DOSBox-X s22: the
            // spirit's frames drawn raw showed speckled colours and a black box).
            int k = Math.Clamp(layer.Frame, 0, img.Frames.Count - 1);
            var p = k == 0 ? img.Frames[0] : ViewPictures.Composed(img.Frames[0], img.Frames[k]);
            if (p.Items.Count == 0)
                continue;
            var px = p.Items[0];
            int x0 = (layer.Column + p.X) * 8, y0 = (layer.Row + p.Y) * 8;
            for (int r = 0; r < p.Height; r++)
                for (int c = 0; c < p.Width; c++)
                {
                    int x = x0 + c, y = y0 + r;
                    if (x >= 0 && y >= 0 && x < f.Width && y < f.Height)
                        f[x, y] = (byte)(b + px[r * p.Width + c]);
                }
        }
    }
}
