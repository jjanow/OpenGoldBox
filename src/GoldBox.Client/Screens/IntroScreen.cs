using System.Diagnostics;
using GoldBox.Client.Input;
using GoldBox.Core.Explore;
using GoldBox.Core.Game;
using GoldBox.Data.Por;

namespace GoldBox.Client.Screens;

/// <summary>
/// The introduction at start-up (GAME.OVR 0x6FB, docs/re/start-menu.md §1a; Core <see cref="Intro"/>), over the party
/// menu it hands over to: the logo (DOS TITLE block 1), the title (DOS TITLE block 2 with the lightning on palette
/// register 1, or under Best / Amiga the Amiga intro painting, without lightning), then the credits page drawn from the
/// program's texts. A key, pad button or click ends the whole intro (DOSBox-X s22); the host swallows that input (the original's code wheel, skipped here, would read it).
/// </summary>
public sealed class IntroScreen : IScreen
{
    private readonly Intro _intro;
    private readonly PorArtImage? _logo, _title;
    private readonly int _logoBase, _titleBase;
    private readonly bool _dosTitle;
    private readonly TextFont? _font;
    private readonly Func<int, string?> _text;
    private readonly IScreenHost _host;
    private readonly Func<TimeSpan> _clock;
    private bool _clicked, _done;

    /// <param name="logo">DOS TITLE block 1, or null (the page is left out).</param>
    /// <param name="title">DOS TITLE block 2 or the Amiga painting, or null.</param>
    /// <param name="text">The program's credit texts by number (<c>Intro.n</c>), null when missing; with any missing the
    /// credits page is left out.</param>
    /// <param name="random">Random(n) for the lightning (null: a generator of its own).</param>
    /// <param name="clock">The time (null: a stopwatch started now).</param>
    public IntroScreen(PorArtImage? logo, int logoBase, PorArtImage? title, int titleBase, TextFont? font,
        Func<int, string?> text, IScreenHost host, Func<int, int>? random = null, Func<TimeSpan>? clock = null)
    {
        _logo = Usable(logo);
        _title = Usable(title);
        _logoBase = logoBase;
        _titleBase = titleBase;
        _dosTitle = _title is { Variant: "dos" };
        _font = font;
        _text = text;
        _host = host;
        if (clock is null)
        {
            var sw = Stopwatch.StartNew();
            clock = () => sw.Elapsed;
        }
        _clock = clock;
        var rng = random ?? new Random().Next;
        bool credits = font is not null && Enumerable.Range(0, Intro.CreditTexts).All(i => text(i) is not null);
        _intro = new Intro(_logo is not null, _title is not null, _dosTitle, credits, rng);
        _intro.Advance(_clock());
    }

    public IntroStep Step => _intro.Step;

    /// <summary>True when the intro has nothing to show (no art, no texts): the caller opens the menu directly.</summary>
    public bool Empty => _intro.Step == IntroStep.Done;

    private static PorArtImage? Usable(PorArtImage? a) =>
        a is { Frames: [{ Items.Count: > 0 }, ..] } ? a : null;

    public GameAction? MapClick(ScreenClick click)
    {
        _clicked = true;
        return null;
    }

    public void Update(ActionInput input)
    {
        var now = _clock();
        if (input.NewInput is not null || input.Clicks.Count > 0 || _clicked)
            _intro.Key(now);
        _clicked = false;
        _intro.Advance(now);
        if (_intro.Step == IntroStep.Done && !_done)
        {
            _done = true;
            _host.Pop();
        }
    }

    public void Draw(IndexedFrame f, ActionInput input)
    {
        var now = _clock();
        switch (_intro.Step)
        {
            case IntroStep.Logo:
                TitleScreen.Paint(f, _logo!, _logoBase);
                break;
            case IntroStep.Title:
                TitleScreen.Paint(f, _title!, _titleBase);
                if (_dosTitle && _intro.FlashColour(now) is { } c)
                    Recolour(f, (byte)(_titleBase + Intro.FlashRegister), (byte)(_titleBase + c));
                break;
            case IntroStep.Credits:
                DrawCredits(f, _font!, _text);
                break;
            default:
                f.Clear(0);
                break;
        }
    }

    /// <summary>Palette register 1 set to another colour: every pixel of index <paramref name="from"/> shows <paramref name="to"/>.</summary>
    private static void Recolour(IndexedFrame f, byte from, byte to)
    {
        for (int y = 0; y < f.Height; y++)
            for (int x = 0; x < f.Width; x++)
                if (f[x, y] == from)
                    f[x, y] = to;
    }

    /// <summary>
    /// The credits page (0x2DA): two windows (START 03F1:02E8 clears the inside and draws the frame one cell around it),
    /// then the lines (START 0x76B6) in their colours.
    /// </summary>
    public static void DrawCredits(IndexedFrame f, TextFont font, Func<int, string?> text)
    {
        f.Clear(0);
        foreach (var (l, t, r, b) in Intro.CreditWindows)
            Chrome.Box(f, l - 1, t - 1, r + 1, b + 1);
        foreach (var line in Intro.CreditLines)
            if (text(line.Text) is { } s)
                Chrome.Text(f, font, line.Column, line.Row, s.Length > 40 - line.Column ? s[..(40 - line.Column)] : s, line.Colour);
    }
}
