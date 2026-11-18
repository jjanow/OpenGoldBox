using System.Diagnostics;
using GoldBox.Client.Input;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Data.Por;

namespace GoldBox.Client.Screens;

/// <summary>
/// The Amiga intro at start-up under Best / Amiga (Core <see cref="AmigaIntroSequence"/>, docs/re/start-menu.md §1a),
/// over the party menu it hands over to: the eight credits panels and the title painting from the module's Amiga art
/// (<see cref="Play.ClientArt.AmigaIntroPages"/>), each faded in and out through a palette run of its own that is
/// rewritten every frame. The Amiga reads only the left mouse button, in its waits; here a key, pad button or click
/// counts too (full controller support), kept until the next wait if it comes during a fade. The host swallows it.
/// </summary>
public sealed class AmigaIntroScreen : IScreen
{
    private readonly IReadOnlyList<PorArtImage> _pages;
    private readonly AmigaIntroSequence _seq;
    private readonly ScreenPalette _palette;
    private readonly int _run;
    private readonly IScreenHost _host;
    private readonly Func<TimeSpan> _clock;
    private bool _clicked, _done;

    /// <param name="pages">The panels in order, the title last (every page 320×200, one frame).</param>
    /// <param name="clock">The time (null: a stopwatch started now).</param>
    public AmigaIntroScreen(IReadOnlyList<PorArtImage> pages, ScreenPalette palette, IScreenHost host, Func<TimeSpan>? clock = null)
    {
        _pages = pages;
        _seq = new AmigaIntroSequence(pages.Count);
        _palette = palette;
        _run = palette.Reserve("amiga-intro");
        _host = host;
        if (clock is null)
        {
            var sw = Stopwatch.StartNew();
            clock = () => sw.Elapsed;
        }
        _clock = clock;
        _seq.Advance(_clock());
    }

    public AmigaIntroSequence Sequence => _seq;

    /// <summary>The first index of the run the pages are drawn in.</summary>
    public int Run => _run;

    public GameAction? MapClick(ScreenClick click)
    {
        _clicked = true;
        return null;
    }

    public void Update(ActionInput input)
    {
        var now = _clock();
        if (input.NewInput is not null || input.Clicks.Count > 0 || _clicked)
            _seq.Key(now);
        _clicked = false;
        _seq.Advance(now);
        if (_seq.Phase == AmigaIntroPhase.Done && !_done)
        {
            _done = true;
            _host.Pop();
        }
    }

    public void Draw(IndexedFrame f, ActionInput input)
    {
        _seq.Advance(_clock());
        if (_seq.Phase == AmigaIntroPhase.Done)
        {
            f.Clear(0);
            return;
        }
        var page = _pages[_seq.Page];
        // In the gap the colours are black (the last fade out; the screen is cleared after the wait).
        _palette.SetRun(_run, AmigaIntroSequence.FadedRgb(page.PaletteRgb, _seq.Divisor));
        TitleScreen.Paint(f, page, _run);
    }
}
