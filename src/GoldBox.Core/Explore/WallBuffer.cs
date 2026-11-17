namespace GoldBox.Core.Explore;

/// <summary>
/// The three wall slots as the program holds them (docs/re/view3d.md "Wall sets"): 3 × 780 bytes, five walls of 156
/// symbol numbers per slot, so wall type t (1..15) starts at (t − 1) × 156. Filled from WALLDEF records by
/// <see cref="Load"/>, which applies the program's rebasing.
/// </summary>
public sealed class WallBuffer
{
    public const int Slots = 3;
    public const int WallsPerSlot = 5;
    public const int SlotSize = WallsPerSlot * ViewLayout.WallRecordSize; // 780

    private readonly byte[] _data = new byte[Slots * SlotSize];

    /// <summary>The WALLDEF id loaded in slots 1..3 (index 0..2), or null when the slot is empty.</summary>
    public int?[] SetIds { get; } = new int?[Slots];

    public ReadOnlySpan<byte> Data => _data;

    /// <summary>Symbol numbers of wall type <paramref name="wallType"/> (1..15): 156 bytes.</summary>
    public ReadOnlySpan<byte> Wall(int wallType) =>
        wallType is >= 1 and <= Slots * WallsPerSlot
            ? _data.AsSpan((wallType - 1) * ViewLayout.WallRecordSize, ViewLayout.WallRecordSize)
            : throw new ArgumentOutOfRangeException(nameof(wallType), wallType, "wall types are 1..15");

    /// <summary>
    /// LoadWallSet (GAME.OVR, routine named by its error text "Unable to load wallset"): copies a WALLDEF record of k
    /// blocks (k × 780 bytes) into slots slot..slot+k−1, then adds base(slot) − base(1) to every byte ≥ the threshold
    /// (DS:272C, so the threshold value itself is shifted too). Every block of a multi-block record uses the delta of
    /// the first slot. Returns the slots filled, so the caller loads the matching 8X8D tile blocks.
    /// </summary>
    public int Load(int slot, int setId, ReadOnlySpan<byte> record, SymbolSet symbols)
    {
        if (slot is < 1 or > Slots)
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "wall slots are 1..3");
        if (record.Length == 0 || record.Length % SlotSize != 0)
            throw new FormatException($"WALLDEF record of {record.Length} bytes is not a multiple of {SlotSize}");
        int blocks = record.Length / SlotSize;
        if (slot + blocks - 1 > Slots)
            throw new FormatException($"WALLDEF record of {blocks} blocks does not fit from slot {slot}");
        int delta = symbols.SlotDelta(slot);
        for (int f = 0; f < blocks; f++)
        {
            var dst = _data.AsSpan((slot - 1 + f) * SlotSize, SlotSize);
            record.Slice(f * SlotSize, SlotSize).CopyTo(dst);
            for (int i = 0; i < SlotSize; i++)
                if (dst[i] >= symbols.RebaseThreshold)
                    dst[i] = (byte)(dst[i] + delta);
            SetIds[slot - 1 + f] = setId;
        }
        return blocks;
    }

    /// <summary>LOADPIECES with 255: the slot is emptied. [?] whether the program clears the bytes; here they are zeroed.</summary>
    public void Clear(int slot)
    {
        if (slot is < 1 or > Slots)
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "wall slots are 1..3");
        _data.AsSpan((slot - 1) * SlotSize, SlotSize).Clear();
        SetIds[slot - 1] = null;
    }
}
