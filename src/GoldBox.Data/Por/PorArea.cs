using GoldBox.Core.Ecl;
using GoldBox.Core.Explore;

namespace GoldBox.Data.Por;

/// <summary>
/// A 3D area entered the way the program does it: the area's ECL script runs its entry vector (4) in the VM, and its
/// LOADFILES / LOADPIECES calls pick the map and the wall sets; the sky colours are the VM cells the script set
/// ($49FD, and $49FE for squares whose event byte has bit 7 set). Only the area entry runs: the step, rest and other
/// vectors are not run yet, so events, encounters and exits do not happen.
/// </summary>
public sealed class PorArea
{
    /// <summary>VM cells read after the entry vector (docs/re/view3d.md "Backdrop"): sky index, second sky index, "no area map here".</summary>
    public const ushort SkyCell = 0x49FD, SkyAltCell = 0x49FE, NoAreaMapCell = 0x49FB;

    public int Script { get; }
    public int Disk { get; }
    public AreaMap Map { get; }
    public PartyPosition Party { get; }
    public int SkyIndex { get; }
    public int SkyAltIndex { get; }
    /// <summary>The A command is allowed ($49FB = 0); otherwise the program says "Not Here".</summary>
    public bool AreaMapAllowed { get; }
    /// <summary>The run of the entry vector (for diagnostics: where it stopped).</summary>
    public EclRunResult EntryRun { get; }
    /// <summary>Host calls of the entry vector, as logged by the scripted host.</summary>
    public IReadOnlyList<string> EntryCalls { get; }
    /// <summary>True when the script loaded no map and the map id was taken from the script id [?].</summary>
    public bool MapGuessed { get; }
    /// <summary>The party records the save names (null when entered without a save or the files are missing).</summary>
    public EclParty? Roster { get; }

    private PorArea(int script, int disk, AreaMap map, PartyPosition party, EclMemory mem, EclRunResult run,
        IReadOnlyList<string> calls, bool guessed)
    {
        Script = script;
        Disk = disk;
        Map = map;
        Party = party;
        SkyIndex = mem.Cells4900[SkyCell - EclMemory.Area4900];
        SkyAltIndex = mem.Cells4900[SkyAltCell - EclMemory.Area4900];
        AreaMapAllowed = mem.Cells4900[NoAreaMapCell - EclMemory.Area4900] == 0;
        EntryRun = run;
        EntryCalls = calls;
        MapGuessed = guessed;
        Roster = mem.Party;
        _c059 = (byte)(mem.SpecialWriteValues.TryGetValue(0xC059, out var a) ? a : BackdropRule.InitC059);
        _c05F = (byte)(mem.SpecialWriteValues.TryGetValue(0xC05F, out var b) ? b : BackdropRule.InitC05F);
    }

    private readonly byte _c059, _c05F;

    /// <summary>The EGA sky colour of the party's square: the sky list entry of $49FD, or of $49FE on an event byte ≥ 0x80.</summary>
    public byte SkyColour(IReadOnlyList<byte> skyColours)
    {
        int index = Map.Event(Party.X, Party.Y) >= 0x80 ? SkyAltIndex : SkyIndex;
        return skyColours[index & 15];
    }

    /// <summary>
    /// The four backdrop colours (<see cref="BackdropRule"/>): the sky from the script's cells, the horizon bands and
    /// the ground from the $C05F / $C059 colour bytes as the entry left them.
    /// </summary>
    public Backdrop Backdrop(PorViewArt art) => new(SkyColour(art.Program.SkyColours),
        BackdropRule.Horizon1(_c05F), BackdropRule.Horizon2(_c05F), BackdropRule.Ground(_c059));

    /// <summary>The 88×88 view at the party's square as the program draws it: the 3D view, or the area map.</summary>
    public IndexedFrame RenderView(PorViewArt art, bool areaMap)
    {
        var f = new IndexedFrame(ViewRaster.ViewPixels, ViewRaster.ViewPixels);
        DrawView(f, 0, 0, art, areaMap);
        return f;
    }

    /// <summary>Draws the view (<see cref="RenderView"/>) into a frame with its top-left pixel at (x, y).</summary>
    public void DrawView(IndexedFrame f, int x, int y, PorViewArt art, bool areaMap)
    {
        var p = Party;
        if (areaMap)
            ViewRaster.DrawAreaMap(f, x, y, View3D.ComposeAreaMap(Map, p.X, p.Y, p.Facing, art.Symbols.Bases[4]), art.Symbols);
        else
            ViewRaster.Draw3D(f, x, y, Backdrop(art), View3D.Compose(Map, p.X, p.Y, p.Facing, art.Layout, art.Walls), art.Symbols);
    }

    /// <summary>Main menu → Load: the save's memory and script, then vector 4 (as <see cref="PorSaveGame.LoadGame"/>).</summary>
    public static PorArea FromSave(PorGameDirectory dir, PorViewArt art, string savePath, IPorContent? content = null) =>
        FromSave(dir, art, File.ReadAllBytes(savePath), Path.GetDirectoryName(Path.GetFullPath(savePath))!, content);

    /// <summary>As <see cref="FromSave(PorGameDirectory, PorViewArt, string, IPorContent?)"/> from the save's bytes; the party files are looked up in <paramref name="saveDir"/>.</summary>
    /// <param name="content">Scripts and maps (an imported module); null = <paramref name="dir"/>.</param>
    public static PorArea FromSave(PorGameDirectory dir, PorViewArt art, byte[] save, string saveDir, IPorContent? content = null)
    {
        content ??= dir;
        var sg = new PorSaveGame(save);
        var (vm, host, recorder) = NewVm(content, art, sg.Disk);
        try
        {
            vm.Memory.Party = PorParty.Load(sg, saveDir, dir.Root);
        }
        catch (Exception e) when (e is IOException or FormatException)
        {
            // The scripts run without the party (their reads of it get the VM's defaults).
        }
        var run = sg.LoadGame(vm);
        return Finish(content, sg.ScriptId, sg.Disk, new PartyPosition(sg.X, sg.Y, sg.Facing), vm, host, recorder, run);
    }

    /// <summary>
    /// Enters area script <paramref name="script"/> with fresh VM memory (no save) at the given square: script init,
    /// then vector 4.
    /// </summary>
    public static PorArea Enter(PorGameDirectory dir, PorViewArt art, int script, int x, int y, int facing,
        IPorContent? content = null)
    {
        content ??= dir;
        int disk = content.ScriptDisk(script) ?? throw new ArgumentException($"no ECL block {script} in {content.Origin}");
        var (vm, host, recorder) = NewVm(content, art, disk);
        vm.Memory.Disk = (byte)disk;
        vm.Memory.Position.X = (byte)x;
        vm.Memory.Position.Y = (byte)y;
        vm.Memory.Position.Facing = (byte)facing;
        vm.LoadScript(script, disk);
        var run = vm.RunVector(EclLayout.VectorInit);
        return Finish(content, script, disk, new PartyPosition(x, y, facing), vm, host, recorder, run);
    }

    private sealed class Recorder
    {
        public int? Map;
    }

    private static (EclVm, ScriptedEclHost, Recorder) NewVm(IPorContent dir, PorViewArt art, int disk)
    {
        var rec = new Recorder();
        var host = new ScriptedEclHost { StopWhenInputMissing = true };
        var mem = new EclMemory();
        var vm = new EclVm(mem, host) { Scripts = dir };
        host.MapLoaded = m => rec.Map = m;
        host.WallSetLoaded = (slot, set) => art.LoadWallSet(mem.Disk == 0 ? disk : mem.Disk, slot, set);
        return (vm, host, rec);
    }

    private static PorArea Finish(IPorContent content, int script, int disk, PartyPosition party,
        EclVm vm, ScriptedEclHost host, Recorder rec, EclRunResult run)
    {
        bool guessed = rec.Map is null;
        int mapId = rec.Map ?? script;
        int mapDisk = content.MapDisk(mapId)
            ?? throw new InvalidOperationException($"script {script} loads no 3D map (the overland or a script without a map)");
        var map = content.LoadMap(mapDisk, mapId) ?? throw new FormatException($"map {mapId} not readable in {content.Origin}");
        map.Bounded = script is 0 or 10;
        return new PorArea(script, disk, map, party, vm.Memory, run, [.. host.Calls], guessed);
    }
}
