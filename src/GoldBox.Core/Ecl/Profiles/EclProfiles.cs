using System.Collections.Frozen;

namespace GoldBox.Core.Ecl.Profiles;

/// <summary>A module names a VM profile the engine does not have.</summary>
public sealed class UnknownEclProfileException(string name)
    : Exception($"unknown VM profile \"{name}\" (this engine has: {string.Join(", ", EclProfiles.Names)})")
{
    public string Name { get; } = name;
}

/// <summary>
/// The VM profiles that ship with the engine, by the name a module's manifest gives as <c>vm</c>
/// (docs/module-format.md §5.1). Names are compared exactly (ordinal, case-sensitive).
/// </summary>
public static class EclProfiles
{
    public const string Por13Name = "ecl-por-1.3";

    /// <summary>
    /// Pool of Radiance DOS v1.3 (docs/re/ecl-format.md). The values are the constants the VM was written with
    /// (<see cref="EclLayout"/>, <see cref="EclMemory"/>, <see cref="EclOpcodes"/>).
    /// </summary>
    public static EclProfile Por13 { get; } = new()
    {
        Name = Por13Name,
        ScriptBase = EclLayout.Base,
        ScriptPrefix = EclLayout.Prefix,
        ScriptAreaSize = EclLayout.AreaSize,
        VectorCount = EclLayout.VectorCount,
        InitVector = EclLayout.VectorInit,
        // ecl-format.md "VM memory map": all four regions are in the save (offsets 1, 2049, 4097, 5121).
        Regions =
        [
            new("4900", EclMemory.Area4900, EclMemory.Area4900End, 16, Saved: true),
            new("6B00", EclMemory.Area6B00, EclMemory.Area6B00End, 16, Saved: true),
            new("9700", EclMemory.Area9700, EclMemory.Area9700End, 16, Saved: true),
            new("script", EclLayout.Base, EclLayout.End, 8, Saved: true),
        ],
        OperandWidth = EclOperandWidthRule.FromAddressRegion,
        UnmappedRead = EclUnmappedRead.StackResidue,
        Opcodes = EclOpcodes.Por13,
        ReturnOnEmptyStack = EclReturnOnEmptyStack.FallsThrough,
        // combat-format.md "CALL ids" (handler 0x3A15); the effects are in EncounterHost and the hosts behind it.
        CallIds = new Dictionary<ushort, string>
        {
            [0x2C90] = "event byte read again, redraw",
            [0x8000] = "mirror duel",
            [0x8001] = "solo fight",
            [0xBA03] = "sound",
            [0xC018] = "wall ahead read again",
            [0xC01E] = "one square forward",
        }.ToFrozenDictionary(),
        // ecl-format.md opcode 0x38; the effects are the host's (IEclHost.Program).
        ProgramIds = new Dictionary<ushort, string>
        {
            [0] = "training hall",
            [8] = "end game",
            [9] = "camp",
        }.ToFrozenDictionary(),
        ProgramIdsThatExit = new HashSet<ushort> { 9 }.ToFrozenSet(), // camp (0x3B56), then EXIT (0x3BBC)
        CharacterView = EclCharacterView.Por6B00Redirection,
        Handlers = EclHandlerSet.Por13,
        ScriptBytesSaved = true,
        DiskCell = 0x6E12, // DS:5376
        BankedKinds = new HashSet<string> { "wallset", "monster", "itemList", "treasureList", "effectList" }.ToFrozenSet(),
        DiskCheckedKinds = new HashSet<string> { "script", "map" }.ToFrozenSet(),
    };

    private static readonly Dictionary<string, EclProfile> All = new(StringComparer.Ordinal)
    {
        [Por13.Name] = Por13,
    };

    /// <summary>The profile used when none is named (the engine runs PoR v1.3 only so far).</summary>
    public static EclProfile Default => Por13;

    public static IEnumerable<string> Names => All.Keys;

    public static bool TryGet(string name, out EclProfile profile) => All.TryGetValue(name, out profile!);

    /// <summary>The profile named <paramref name="name"/>; throws <see cref="UnknownEclProfileException"/> when there is none.</summary>
    public static EclProfile Get(string name) =>
        All.TryGetValue(name, out var p) ? p : throw new UnknownEclProfileException(name);

    /// <summary>
    /// The profile for a manifest's <c>vm</c>: <see cref="Default"/> when it is absent `[?]` (module-format.md §18),
    /// else <see cref="Get"/>.
    /// </summary>
    public static EclProfile ForVm(string? vm) => vm is null ? Default : Get(vm);
}
