using GoldBox.Core.Game;

namespace GoldBox.Data.Por;

/// <summary>
/// The training hall's tables and menu texts in the program's data segment (PoR DOS v1.3 offsets, as
/// <see cref="PorCombatTables"/>): read from <see cref="PorProgramData.Ds"/>, so a module's <c>rules/program.json</c>
/// gives the same as the player's START.EXE. docs/re/training.md.
/// </summary>
public static class PorTrainingTables
{
    /// <summary>DS:0D38: class names, string[26] per class 0..7 (27 bytes each).</summary>
    public const int ClassNames = 0x0D38;

    /// <summary>The hall's tables, or null when the program's level tables were not found.</summary>
    public static TrainingTables? From(PorProgramData? p) =>
        p?.LevelTables() is { } levels ? new TrainingTables { Data = p.Ds, Levels = levels } : null;

    /// <summary>The text of a hall menu entry (DS:0619 + 42·i, string[40]), or null.</summary>
    public static string? MenuEntry(PorProgramData? p, TrainingHall.Entry e) => p?.PascalString(TrainingHall.EntryString(e), 40);

    /// <summary>The default "shown" flag of a menu entry (DS byte after its string; Create, Add and Exit keep theirs).</summary>
    public static bool? MenuDefault(PorProgramData? p, TrainingHall.Entry e)
    {
        int at = TrainingHall.EntryString(e) + 41;
        return p is not null && at < p.Ds.Length ? p.Ds[at] != 0 : null;
    }

    /// <summary>A class's name (0 cleric … 7 monk), or null.</summary>
    public static string? ClassName(PorProgramData? p, int cls) =>
        cls is >= 0 and <= 7 ? p?.PascalString(ClassNames + 27 * cls, 26) : null;
}
