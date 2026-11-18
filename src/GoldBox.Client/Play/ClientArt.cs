using GoldBox.Data.Module.Por;
using GoldBox.Data.Por;

namespace GoldBox.Client.Play;

/// <summary>
/// The player's art setting (PLAN §4.3: Original DOS / Best / Amiga) and everything drawn through it: the 3D view's
/// tiles (<see cref="PorViewArt.UseArt"/>), the combat pictures (<see cref="CombatArt"/>), the encounter pictures
/// (<see cref="ViewPictures"/>) and the title painting. Original DOS draws the DOS art exactly as before; Best takes
/// each category from the variant the module's <c>art.defaults</c> names (PoR: Amiga, DOS filling the gaps); Amiga takes
/// the Amiga art wherever the module has it. A module without the Amiga overlay draws DOS under every setting.
/// A change applies at once (the wall slots and caches are loaded again) and is remembered in the settings.
/// </summary>
public sealed class ClientArt
{
    private readonly IPorContent? _content;
    private readonly PorViewArt? _view;

    public ScreenPalette Palette { get; }
    public CombatArt Combat { get; }
    public ViewPictures Pictures { get; }
    public PorArtChoice Choice { get; private set; }

    /// <summary>Called after a change (the Client saves the setting).</summary>
    public Action<PorArtChoice>? Changed { get; set; }

    /// <summary>DOS art only, no content (tests, screens without art).</summary>
    public ClientArt() : this(null, null, new ScreenPalette(), PorArtChoice.Dos)
    {
    }

    public ClientArt(IPorContent? content, PorViewArt? view, ScreenPalette palette, PorArtChoice choice)
    {
        _content = content;
        _view = view;
        Palette = palette;
        Combat = content is null ? new CombatArt() : new CombatArt(content);
        Pictures = new ViewPictures(content);
        Use(choice);
    }

    /// <summary>Switches the art (see the class summary) and reports the change.</summary>
    public void Set(PorArtChoice choice)
    {
        Use(choice);
        Changed?.Invoke(choice);
    }

    private void Use(PorArtChoice choice)
    {
        Choice = choice;
        _view?.UseArt(choice, Palette.Base);
        Combat.UseArt(choice, Palette.Base);
        Pictures.UseArt(choice, Palette.Base);
    }

    /// <summary>
    /// The title painting to show at start-up under this setting, or null: only a variant's own title (the Amiga intro
    /// painting, module path <c>art/amiga/intro/0/1</c>); Original DOS shows none, as before.
    /// </summary>
    public PorArtImage? Title() =>
        Choice != PorArtChoice.Dos
        && _content?.Art(PorAmigaArt.IntroContainer.ToUpperInvariant(), PorAmigaArt.TitleId, Choice) is { Variant: not "dos", Frames: [{ Items.Count: > 0 }, ..] } t
            ? t : null;

    /// <summary>The setting after <paramref name="c"/> in the settings row's cycle.</summary>
    public static PorArtChoice Next(PorArtChoice c) => c switch
    {
        PorArtChoice.Dos => PorArtChoice.Best,
        PorArtChoice.Best => PorArtChoice.Amiga,
        _ => PorArtChoice.Dos,
    };

    /// <summary>The setting before <paramref name="c"/>.</summary>
    public static PorArtChoice Previous(PorArtChoice c) => Next(Next(c));

    /// <summary>The words the settings row shows.</summary>
    public static string Label(PorArtChoice c) => c switch
    {
        PorArtChoice.Dos => "ORIGINAL DOS",
        PorArtChoice.Best => "BEST",
        _ => "AMIGA",
    };

    /// <summary>
    /// The command-line / settings-file word: <c>dos</c>, <c>best</c> or <c>amiga</c> (any case; <c>original</c> also
    /// means DOS), or null for anything else.
    /// </summary>
    public static PorArtChoice? Parse(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "dos" or "original" or "original-dos" => PorArtChoice.Dos,
        "best" => PorArtChoice.Best,
        "amiga" => PorArtChoice.Amiga,
        _ => null,
    };
}
