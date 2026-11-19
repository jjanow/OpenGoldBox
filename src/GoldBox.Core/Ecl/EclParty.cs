namespace GoldBox.Core.Ecl;

/// <summary>
/// One party member as the ECL VM sees it: the creature record (PoR 285 bytes, docs/re/character-format.md; SotSB
/// 439 bytes, <see cref="Profiles.EclProfile.RecordSize"/>) and the
/// 63-byte item records it carries (docs/re/item-format.md). The VM reads and writes record bytes only through the
/// $6B00-area redirection table and the opcodes the spec reads from the program (PARTYSTR, PSURPRISE, FINDITEM).
/// </summary>
public sealed class EclCharacter
{
    public const int RecordSize = 0x11D;
    /// <summary>SotSB's record (docs/re/survey-sotsb.md §5).</summary>
    public const int SotsbRecordSize = 0x1B7;
    public const int ItemRecordSize = 63;
    /// <summary>Longest name the record holds (Pascal string at offset 0, 16 bytes).</summary>
    public const int MaxNameLength = 15;

    public const int EffectRecordSize = 9;

    public byte[] Record { get; }
    public List<byte[]> Items { get; } = [];
    /// <summary>
    /// The 9-byte effect records (docs/re/item-format.md "Effect record"), in list order. Byte 0 is the effect id;
    /// the encounter menu's speed rule (GAME.OVR 0x9484) tests ids 0x27 and 0x2A.
    /// </summary>
    public List<byte[]> Effects { get; } = [];

    /// <summary>True when an effect with this id is in <see cref="Effects"/> (START.EXE 00BA:2199 walks the list).</summary>
    public bool HasEffect(byte id) => Effects.Any(e => e.Length > 0 && e[0] == id);

    /// <param name="record">Initial bytes (cut or zero-padded to <paramref name="recordSize"/>).</param>
    /// <param name="recordSize">The dialect's record size (<see cref="Profiles.EclProfile.RecordSize"/>).</param>
    public EclCharacter(byte[]? record = null, int recordSize = RecordSize)
    {
        Record = new byte[recordSize];
        if (record is not null)
            record.AsSpan(0, Math.Min(record.Length, recordSize)).CopyTo(Record);
    }

    public byte this[int offset]
    {
        get => Record[offset];
        set => Record[offset] = value;
    }

    public ushort Word(int offset) => (ushort)(Record[offset] | (Record[offset + 1] << 8));

    public void SetWord(int offset, ushort value)
    {
        Record[offset] = (byte)value;
        Record[offset + 1] = (byte)(value >> 8);
    }

    /// <summary>Name: Pascal string at offset 0 (length byte ≤ 15, then the characters, rest zero).</summary>
    public string Name
    {
        get
        {
            int n = Math.Min((int)Record[0], MaxNameLength);
            var c = new char[n];
            for (int i = 0; i < n; i++)
                c[i] = (char)Record[1 + i];
            return new string(c);
        }
        set
        {
            var s = value.Length > MaxNameLength ? value[..MaxNameLength] : value;
            Array.Clear(Record, 0, 1 + MaxNameLength);
            Record[0] = (byte)s.Length;
            for (int i = 0; i < s.Length; i++)
                Record[1 + i] = (byte)s[i];
        }
    }
}

/// <summary>
/// The party list (DS:5D96) and the current character (DS:5D92). Attach to <see cref="EclMemory.Party"/>; without
/// a party, record reads and the party opcodes fall back to cells / host answers and are logged.
/// </summary>
public sealed class EclParty
{
    public List<EclCharacter> Members { get; } = [];

    /// <summary>
    /// 0-based index of the current character, or -1 for none. [?] Which character is current right after a save
    /// is loaded is not read; loaders set 0.
    /// </summary>
    public int CurrentIndex { get; set; } = -1;

    public EclCharacter? Current => CurrentIndex >= 0 && CurrentIndex < Members.Count ? Members[CurrentIndex] : null;

    /// <summary>
    /// DS:4436: the character the player had selected, stored each time the area loop reads a command (GAME.OVR 0x40EA,
    /// 0x4188; <see cref="EclVm.CommandRead"/>). EXIT and PROGRAM restore it when <see cref="ChangedByScript"/> is set.
    /// </summary>
    public EclCharacter? Selected { get; set; }

    /// <summary>DS:4430: set by LOADCHAR; EXIT (0xDAC) and PROGRAM (0x3B5C) then restore <see cref="Selected"/> and clear it.</summary>
    public bool ChangedByScript { get; set; }

    /// <summary>Makes <paramref name="c"/> current (by reference); null or a record no longer listed gives no current character.</summary>
    public void SetCurrent(EclCharacter? c) => CurrentIndex = c is null ? -1 : Members.IndexOf(c);
}
