using GoldBox.Client.Play;
using Xunit;

namespace GoldBox.Client.Tests;

/// <summary>
/// The Amiga input requester's typed text and the amount prompts that size it (docs/re/por-art-formats.md "Input
/// requester", FS-UAE s47). Static <see cref="AmigaView.On"/> is not touched (tests run in parallel).
/// </summary>
public class AmigaRequesterTests
{
    [Theory]
    [InlineData(128, 0, 64)]    // empty: the cursor cell at the gadget's middle
    [InlineData(128, 2, 56)]    // Rename's 16-cell gadget, two letters: 7 cells in
    [InlineData(128, 13, 8)]    // thirteen letters: 1 cell in
    [InlineData(128, 15, 0)]    // fifteen fill the gadget with the cursor
    [InlineData(48, 2, 16)]     // Take's 6-cell number gadget, "30": 2 cells in
    [InlineData(120, 14, 0)]    // creation's name gadget, fourteen letters at its left edge (s44)
    [InlineData(56, 0, 24)]     // the code word's 7-cell gadget, empty: 3 cells in
    [InlineData(56, 2, 16)]     // two letters: 2 cells in
    public void The_typed_text_starts_whole_cells_in(int width, int shown, int offset) =>
        Assert.Equal(offset, AmigaGadget.TextOffset(width, shown));

    [Fact]
    public void An_amount_prompt_keeps_its_last_space_for_the_Amiga_requester_only()
    {
        const string prompt = "HOW MANY COINS? ";
        Assert.Equal(prompt, AmigaView.AmountPrompt(prompt, amiga: true));
        Assert.Equal("HOW MANY COINS?", AmigaView.AmountPrompt(prompt, amiga: false));
    }
}
