using System.Text.Json.Nodes;

namespace GoldBox.Data.Module;

// The engine save (docs/module-format.md §15): one JSON file per saved game. byte[] properties are base64.
// Raw is authoritative, as in the module's records (§11): the VM regions and the record bytes are what a load uses;
// `clock` and the records' `fields` are read-only views for tools and people.

/// <summary>The module a save was made with (§15): the manifest's id and version, and its content hash.</summary>
public sealed record SaveModuleRef
{
    public required string Id { get; init; }
    public string? Version { get; init; }
    /// <summary><see cref="ModuleContentHash"/> of the module chain the save was made with (null: not known, e.g. a game folder).</summary>
    public string? ContentHash { get; init; }
}

/// <summary>
/// One VM memory region the VM profile marks as saved (§5.1 <c>Regions</c>): its name, first address, cell width and
/// cells. 16-bit cells are little-endian words (2 bytes per address); 8-bit cells one byte per address.
/// </summary>
public sealed record SavedRegion
{
    public required string Name { get; init; }
    public int Start { get; init; }
    public int CellBits { get; init; }
    public required byte[] Cells { get; init; }
}

/// <summary>The party's square (PoR: DS:6AAD–6AB1, read by scripts at $C04B–$C04F).</summary>
public sealed record SavedPosition
{
    public int X { get; init; }
    public int Y { get; init; }
    /// <summary>8-way code: 0 N, 2 E, 4 S, 6 W.</summary>
    public int Facing { get; init; }
    /// <summary>Wall type ahead on the party's square ($C04E).</summary>
    public int WallAhead { get; init; }
    /// <summary>Event byte of the party's square ($C04F).</summary>
    public int EventByte { get; init; }
}

/// <summary>
/// The current script: its key and the bank it was loaded from. Under a profile that saves the script bytes (PoR)
/// the bytes are the <c>script</c> memory region, and they win over the module's copy on load (a script may have
/// changed itself); <see cref="MatchesModule"/> only records whether they equalled the module's copy when the save
/// was written.
/// </summary>
public sealed record SavedScript
{
    public required KeyRef Key { get; init; }
    /// <summary>The bank (PoR: the disk) the script was loaded from.</summary>
    public int Disk { get; init; }
    /// <summary>Whether the saved bytes equalled the module's script then (null: not checked). Informational.</summary>
    public bool? MatchesModule { get; init; }
}

/// <summary>A read-only view of the clock cells (PoR $49C6–$49CC); the memory region is what a load uses.</summary>
public sealed record SaveClock
{
    public int Year { get; init; }
    public int Month { get; init; }
    public int Day { get; init; }
    public int Hour { get; init; }
    public int Minute { get; init; }
}

/// <summary>One party member: the character record, its item and effect records (§11 shapes).</summary>
public sealed record SavedCharacter
{
    /// <summary>The file stem the member was stored under in the original save (PoR <c>CHRDATA1</c>), kept for the round trip.</summary>
    public string? Stem { get; init; }
    public JsonObject? Fields { get; init; }
    public required byte[] Raw { get; init; }
    /// <summary>The item records in inventory order; null = no item file (PoR writes none for an empty list).</summary>
    public List<RecordDocument>? Items { get; init; }
    /// <summary>The effect records in list order; null = no effect file.</summary>
    public List<RecordDocument>? Effects { get; init; }
    /// <summary>Bytes after the last whole item record of the file (none in the shipped saves; the game's load ignores them).</summary>
    public byte[]? ItemsTrailing { get; init; }
    /// <summary>Bytes after the last whole effect record of the file.</summary>
    public byte[]? EffectsTrailing { get; init; }
}

/// <summary>
/// What PoR's <c>SAVGAMx.DAT</c> holds besides the VM regions and the position (save-format.md): the bytes an engine
/// save keeps so the original file can be written again byte for byte.
/// </summary>
public sealed record PorSaveLayout
{
    /// <summary>The save slot letter A..J the files were (or would be) named after.</summary>
    public string? Slot { get; init; }
    /// <summary>Byte 0: the current disk (DS:5376), kept apart from the VM cell $6E12.</summary>
    public int Disk { get; init; }
    /// <summary>Byte 12806: the location type (DS:49FA, the view mode).</summary>
    public int LocationType { get; init; }
    /// <summary>Byte 12807: the interface state when saved (2 = camp).</summary>
    public int InterfaceState { get; init; }
    /// <summary>Byte 12808: the party size as written (normally the party's count).</summary>
    public int PartySize { get; init; }
    /// <summary>
    /// Bytes 12809–13136: the eight string[40] file-stem slots as written, leftover memory included (the program
    /// writes uninitialised bytes after each stem and in the unused slots). Null = zeros.
    /// </summary>
    public byte[]? NameSlots { get; init; }
}

/// <summary>
/// An engine save (docs/module-format.md §15): the module it belongs to, the VM profile, the saved VM regions, the
/// position, the current script and the party. <see cref="Por"/> holds the rest of PoR's original layout.
/// </summary>
public sealed record EngineSaveDocument
{
    /// <summary>The save format this code writes; it reads any 0.x.</summary>
    public const string CurrentFormatVersion = "0.1";

    public required string FormatVersion { get; init; }
    public required SaveModuleRef Module { get; init; }
    /// <summary>The VM profile the memory belongs to (§5.1); a load refuses another.</summary>
    public required string Vm { get; init; }
    public required List<SavedRegion> Memory { get; init; }
    public required SavedPosition Position { get; init; }
    public required SavedScript Script { get; init; }
    /// <summary>The party in order; null when the original's party files could not be read (the game then runs without one).</summary>
    public List<SavedCharacter>? Party { get; init; }
    public SaveClock? Clock { get; init; }
    public PorSaveLayout? Por { get; init; }
    /// <summary>Where the save was converted from (an original <c>SAVGAMx.DAT</c>), if it was.</summary>
    public SourceRef? Source { get; init; }
}
