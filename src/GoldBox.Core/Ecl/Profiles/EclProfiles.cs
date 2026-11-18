using System.Collections.Frozen;

namespace GoldBox.Core.Ecl.Profiles;

/// <summary>A module names a VM profile the engine does not have.</summary>
public sealed class UnknownEclProfileException(string name)
    : Exception($"unknown VM profile \"{name}\" (this engine has: {string.Join(", ", EclProfiles.Names)})")
{
    public string Name { get; } = name;
}

/// <summary>
/// A VM profile the engine knows but cannot run: its handler set is not written (SotSB: decoding only).
/// </summary>
public sealed class EclProfileNotRunnableException(EclProfile profile)
    : NotSupportedException($"VM profile {profile.Name}: handler set {profile.Handlers} is not implemented " +
                            "(this engine decodes its scripts but does not run them)")
{
    public EclProfile Profile { get; } = profile;
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

    public const string Sotsb13Name = "ecl-sotsb-1.3";

    /// <summary>
    /// Secret of the Silver Blades DOS v1.30 (docs/re/survey-sotsb.md; the same VM in v1.00, v1.10, v1.20 and the 1992
    /// WizardWorks "v1.3"). PoR's VM with the regions moved, its own opcode and IF-skip tables, RETURN-exits and other
    /// CALL / PROGRAM ids. Decoding only: its handler set is not implemented, so <see cref="EclMemory"/> refuses it.
    /// Offsets are SotSB v1.30 GAME.OVR file offsets.
    /// </summary>
    public static EclProfile Sotsb13 { get; } = new()
    {
        Name = Sotsb13Name,
        // Script loader OVR 0x939F: clears 0x1E00 bytes, copies the block from offset 2 (prefix 0x1388 dropped);
        // classifier OVR 0x9774: script area $8000–$9DFF; init OVR 0x922C reads five vectors, init = vector 4.
        ScriptBase = 0x8000,
        ScriptPrefix = 2,
        ScriptAreaSize = 0x1E00,
        VectorCount = 5,
        InitVector = 4,
        // Classifier OVR 0x9774, reader OVR 0xA0F1, writer OVR 0x9F3A: PoR's regions moved (survey §3.3). The save
        // (writer ≈OVR 0x26AE0) holds the three word areas, not the script (survey §6).
        Regions =
        [
            new("4B00", 0x4B00, 0x4EFF, 16, Saved: true),
            new("7A00", 0x7A00, 0x7BFF, 16, Saved: true),
            new("7C00", 0x7C00, 0x7FFF, 16, Saved: true),
            new("script", 0x8000, 0x9DFF, 8, Saved: false),
        ],
        OperandWidth = EclOperandWidthRule.FromAddressRegion,  // value fetch OVR 0x91AB, as PoR
        UnmappedRead = EclUnmappedRead.StackResidue,           // reader OVR 0xA1DD sets nothing, as PoR
        Opcodes = EclOpcodes.Sotsb,
        ReturnOnEmptyStack = EclReturnOnEmptyStack.Exits,      // OVR 0x170A → EXIT 0x0C0B
        // CALL OVR 0x3FD1 compares field − 0x7FFF; the raw field values are listed.
        CallIds = new Dictionary<ushort, string>
        {
            [0x2DCB] = "view refresh",
            [0x6803] = "[?]",
            [0xB203] = "sound",
            [0xC018] = "wall ahead read again [?] (PoR's meaning, not re-read)",
            [0xC01E] = "one square forward",
        }.ToFrozenDictionary(),
        // PROGRAM OVR 0x41A1.
        ProgramIds = new Dictionary<ushort, string>
        {
            [0] = "program routine OVR 0x41E5 (training hall as in PoR [?])",
            [9] = "camp",
        }.ToFrozenDictionary(),
        ProgramIdsThatExit = new HashSet<ushort> { 9 }.ToFrozenSet(),
        ProgramExitGuardCell = 0x4C38, // OVR 0x422E: EXIT only when $4C38 = 0
        CharacterView = EclCharacterView.Sotsb7C00Redirection,
        Handlers = EclHandlerSet.Sotsb,
        ScriptBytesSaved = false, // reloaded by id from $4BF2
        DiskCell = 0x7F12,        // case 0x312 of the character view (PoR $6E12)
        // ECL, GEO, WALLDEF, 8X8D and ITEM keep the disk digit (DS:7420); monsters are always MON1 (OVR 0x25BDA).
        // [?] MON1ITM / MON1SPC: their name builders were not read (presumably fixed like MON1CHA).
        BankedKinds = new HashSet<string> { "wallset", "tile", "treasureList" }.ToFrozenSet(),
        DiskCheckedKinds = new HashSet<string> { "script", "map" }.ToFrozenSet(),
    };

    private static readonly Dictionary<string, EclProfile> All = new(StringComparer.Ordinal)
    {
        [Por13.Name] = Por13,
        [Sotsb13.Name] = Sotsb13,
    };

    /// <summary>
    /// The profile of code that names none (a DOS game folder, <c>new EclMemory()</c>): the engine runs PoR v1.3 only so
    /// far (<see cref="Sotsb13"/> decodes but does not run). A module without <c>vm</c> does not get it (<see cref="ForVm"/>).
    /// </summary>
    public static EclProfile Default => Por13;

    public static IEnumerable<string> Names => All.Keys;

    public static bool TryGet(string name, out EclProfile profile) => All.TryGetValue(name, out profile!);

    /// <summary>The profile named <paramref name="name"/>; throws <see cref="UnknownEclProfileException"/> when there is none.</summary>
    public static EclProfile Get(string name) =>
        All.TryGetValue(name, out var p) ? p : throw new UnknownEclProfileException(name);

    /// <summary>
    /// The profile for a manifest's <c>vm</c>: null when it is absent (no ECL VM: an event-only module,
    /// docs/module-format.md §4), else <see cref="Get"/>.
    /// </summary>
    public static EclProfile? ForVm(string? vm) => vm is null ? null : Get(vm);
}
