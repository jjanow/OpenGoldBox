using System.Globalization;

namespace GoldBox.Client.Play;

/// <summary>
/// The questions the Client's screens ask on the logic thread, on top of <see cref="ScriptRunner.Ask"/> (or a
/// scripted answer function in tests): bottom-line menus, yes/no, key waits, numbers and full-screen lists. Texts go
/// to the program's text window (<see cref="TextWindow"/>).
/// </summary>
public sealed class Dialog(Func<PlayerRequest, object?> ask, TextWindow text)
{
    public TextWindow Text { get; } = text;

    public object? Ask(PlayerRequest request) => ask(request);

    /// <summary>A menu on the bottom line; the answer is the item index, or a <see cref="ChoiceRequest"/> member answer.</summary>
    public int Menu(IReadOnlyList<string> items, string? prompt = null, int? cancel = null, bool partySelect = false,
        string? memberLetters = null, bool animates = false) =>
        ask(new ChoiceRequest(null, items, false, cancel)
        {
            MenuPrompt = prompt?.TrimEnd(), // the program's prompts carry a trailing space; the screen adds one
            PartySelect = partySelect,
            MemberLetters = memberLetters,
            Animates = animates, // the menu routine's flag [bp+0xA]: the picture window's animation runs while it waits
        }) is int i ? i : cancel ?? 0;

    /// <summary>
    /// The menu helper GAME.OVR 0x8B57 (002B:0061) as the venue exit questions call it (Return flag [bp+8] = 0): the
    /// menu routine 0x2CC8C on the shared highlight with colours 13 / 10 / 15 and the keypad flag, until it returns a
    /// letter or digit (the set at 0x8B57's CS:1664); an item's hotkey answers its index. Return (13 with the flag 0)
    /// and Escape (0) loop; an extended key (Home / End) runs the party selection 0x23819 and the party list, then the
    /// menu again (answered here as <see cref="ChoiceRequest.PreviousMember"/> / <see cref="ChoiceRequest.NextMember"/>
    /// for the caller to apply). A letter that is no hotkey would answer 0xFF, but the menu routine ignores such keys
    /// (DOSBox-X s47: X, Escape and Return at the loot's question changed nothing).
    /// </summary>
    public int MenuHelper(IReadOnlyList<string> items) =>
        ask(new ChoiceRequest(null, items, false) { PartySelect = true, KeyboardEnterIgnored = true }) is int i ? i : 0;

    /// <summary>A vertical list in the text window under <paramref name="prompt"/>; the answer is the index.</summary>
    public int List(string? prompt, IReadOnlyList<string> items, int? cancel = null) =>
        ask(new ChoiceRequest(prompt, items, true, cancel)) is int i ? i : cancel ?? 0;

    /// <summary>
    /// The program's yes/no menu (0x2D9A0: "~Yes ~No" with the question as its prompt): true on Yes. Cancel = No. It
    /// sets the shared highlight to No before its menu (<see cref="ChoiceRequest.Selected"/> = 1).
    /// </summary>
    /// <paramref name="promptColour"/>: the caller's prompt colour (13 when null; 14 for the camp's and hall's warnings,
    /// the Amiga's pen 24 there, read s46). <paramref name="colours"/>: the words' and lit word's colours when the caller
    /// passes them otherwise (<see cref="ChoiceRequest.MenuColours"/>); the Amiga's copies of those callers pass words 16,
    /// capitals 28 (hunk 25 +0x3A72, hunk 22 +0x29CE, +0x2A28; read s46).
    public bool YesNo(string? prompt = null, byte? promptColour = null, (byte Words, byte Lit)? colours = null) =>
        ask(new ChoiceRequest(null, ["YES", "NO"], false, 1)
        {
            MenuPrompt = prompt?.TrimEnd(),
            MenuPromptColour = promptColour is byte pc ? AmigaView.YesNoPrompt(pc) : null,
            MenuColours = colours,
            Selected = 1,
            AmigaPens = colours is not null ? AmigaView.YesNoSwappedPens : null,
        }) is int i ? i == 0 : false;

    /// <summary>Print into the text window.</summary>
    public void Show(string message, bool clear = true, byte colour = TextWindow.ScriptColour) =>
        Text.Print(message, clear, colour);

    /// <summary>Print, then wait for a key. [?] Where the program pauses after its messages is not read for each one.</summary>
    public void Say(string message, bool clear = true, byte colour = TextWindow.ScriptColour)
    {
        Show(message, clear, colour);
        WaitKey();
    }

    public void WaitKey(string? prompt = null) => ask(new KeyRequest(prompt));

    /// <summary>A key wait whose prompt is a white menu of the menu routine (<see cref="KeyRequest.AsMenu"/>);
    /// <paramref name="original"/> is the text in its original case, for the entry count.</summary>
    public void MenuKey(string prompt, string original) =>
        ask(new KeyRequest(prompt) { AsMenu = true, MenuEntries = KeyRequest.CountEntries(original), Original = original });

    /// <summary>A number typed on the bottom line (digits only); null when nothing was typed.</summary>
    public int? Number(string prompt)
    {
        var s = ask(new TextRequest(prompt.TrimEnd(), true, 6)) as string ?? "";
        return int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    /// <summary>A full-screen list; the answer's action is −1 when nothing came back.</summary>
    public ListAnswer Page(ListRequest request) => ask(request) as ListAnswer ?? new ListAnswer(-1, 0);
}
