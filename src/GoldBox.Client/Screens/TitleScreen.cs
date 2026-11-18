using GoldBox.Client.Input;
using GoldBox.Core.Explore;
using GoldBox.Data.Por;

namespace GoldBox.Client.Screens;

/// <summary>
/// The title painting shown under the Best / Amiga art setting (the Amiga intro painting, docs/re/por-art-formats.md §5;
/// <see cref="Play.ClientArt.Title"/>) when the Client starts from a save or an area, over the game screen it hands
/// over to. Any key, pad button or click goes on (the host swallows that input, so it does not reach the game). A start
/// at the party menu shows the introduction instead (<see cref="IntroScreen"/>, which uses <see cref="Paint"/>).
/// </summary>
public sealed class TitleScreen(PorArtImage title, int paletteBase, IScreenHost host) : IScreen
{
    private bool _done;

    public GameAction? MapClick(ScreenClick click)
    {
        Leave();
        return null;
    }

    public void Update(ActionInput input)
    {
        if (input.NewInput is not null || input.Clicks.Count > 0)
            Leave();
    }

    private void Leave()
    {
        if (_done)
            return;
        _done = true;
        host.Pop();
    }

    public void Draw(IndexedFrame f, ActionInput input) => Paint(f, title, paletteBase);

    /// <summary>Frame 0's first item at the frame's top-left, its indices moved into the palette run at <paramref name="b"/>.</summary>
    public static void Paint(IndexedFrame f, PorArtImage title, int b)
    {
        f.Clear((byte)b);
        var p = title.Frames[0];
        var px = p.Items[0];
        for (int y = 0; y < p.Height && y < f.Height; y++)
            for (int x = 0; x < p.Width && x < f.Width; x++)
                f[x, y] = (byte)(b + px[y * p.Width + x]);
    }
}
