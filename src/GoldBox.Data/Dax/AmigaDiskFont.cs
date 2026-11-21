using System.Buffers.Binary;
using System.Text;

namespace GoldBox.Data.Dax;

/// <summary>
/// An AmigaOS disk font (<c>FONTS:&lt;name&gt;/&lt;size&gt;</c>): a hunk load file whose one hunk holds a
/// DiskFontHeader (file id 0x0F80) and the TextFont it describes (<c>diskfont/diskfont.h</c>, <c>graphics/text.h</c>).
/// Read as graphics.library's Text draws it for a font without proportional spacing: every character of
/// <see cref="LoChar"/>..<see cref="HiChar"/> takes its bits from the shared bitmap (<c>tf_CharData</c>, one row of
/// <c>tf_Modulo</c> bytes per line) at its <c>tf_CharLoc</c> bit offset and width; any other character takes the slot
/// after <see cref="HiChar"/> (the font's own "no such character" glyph). Own code from the AmigaOS include files'
/// layout. Used for the PoR Amiga rune font (docs/re/ecl-format.md "PROTECT", docs/re/por-art-formats.md).
/// </summary>
public sealed class AmigaDiskFont
{
    /// <summary>dfh_FileID of a disk font file.</summary>
    public const int FileId = 0x0F80;

    /// <summary>fch_FileID of a <c>.font</c> contents file (no tag lists).</summary>
    public const int ContentsId = 0x0F00;

    /// <summary>tf_Flags bit: proportional spacing (tf_CharSpace / tf_CharKern used).</summary>
    public const int Proportional = 0x20;

    // Offsets in the hunk: moveq/rts (4), the DiskFontHeader's node (14), file id, revision, segment, name[32],
    // then the TextFont (its Message is 20 bytes).
    private const int HeaderAt = 4, FileIdAt = HeaderAt + 14, NameAt = FileIdAt + 8, TextFontAt = NameAt + 32,
        MetricsAt = TextFontAt + 20;

    private readonly byte[] _hunk;
    private readonly int _charData, _modulo, _charLoc;

    public int YSize { get; }
    public int XSize { get; }
    public int Baseline { get; }
    public int Style { get; }
    public int Flags { get; }
    public int LoChar { get; }
    public int HiChar { get; }

    private AmigaDiskFont(byte[] hunk)
    {
        _hunk = hunk;
        ushort W(int at) => BinaryPrimitives.ReadUInt16BigEndian(Span(at, 2));
        uint L(int at) => BinaryPrimitives.ReadUInt32BigEndian(Span(at, 4));
        if (W(FileIdAt) != FileId)
            throw new DaxFormatException("not an Amiga disk font (file id is not 0x0F80)");
        int m = MetricsAt;
        YSize = W(m);
        Style = hunk[m + 2];
        Flags = hunk[m + 3];
        XSize = W(m + 4);
        Baseline = W(m + 6);
        LoChar = hunk[m + 12];
        HiChar = hunk[m + 13];
        _charData = (int)L(m + 14);
        _modulo = W(m + 18);
        _charLoc = (int)L(m + 20);
        if (HiChar < LoChar || YSize == 0)
            throw new DaxFormatException("Amiga disk font: no characters");
        Span(_charLoc, 4 * (HiChar - LoChar + 2));
        Span(_charData, _modulo * YSize);
    }

    private ReadOnlySpan<byte> Span(int at, int length) =>
        at >= 0 && length >= 0 && at + length <= _hunk.Length
            ? _hunk.AsSpan(at, length)
            : throw new DaxFormatException("Amiga disk font: a table runs past the hunk");

    /// <summary>Reads a disk font file (its first hunk; the TextFont's pointers are offsets from the hunk start).</summary>
    public static AmigaDiskFont Parse(byte[] file)
    {
        var hunks = AmigaHunks.Parse(file);
        if (hunks.Count == 0 || hunks[0].DataOffset is not { } at || at + hunks[0].Size > file.Length)
            throw new DaxFormatException("Amiga disk font: no whole code hunk");
        return new AmigaDiskFont(file.AsSpan(at, hunks[0].Size).ToArray());
    }

    /// <summary>The name in the DiskFontHeader (often empty).</summary>
    public string Name
    {
        get
        {
            var n = _hunk.AsSpan(NameAt, 32);
            int z = n.IndexOf((byte)0);
            return Encoding.Latin1.GetString(z < 0 ? n : n[..z]);
        }
    }

    /// <summary>The CharLoc slot character <paramref name="c"/> is drawn from (the slot after HiChar for any other character).</summary>
    public int Slot(int c) => c >= LoChar && c <= HiChar ? c - LoChar : HiChar - LoChar + 1;

    /// <summary>Character <paramref name="c"/>'s bit offset in the bitmap rows and its width.</summary>
    public (int Offset, int Width) Location(int c)
    {
        int at = _charLoc + 4 * Slot(c);
        return (BinaryPrimitives.ReadUInt16BigEndian(_hunk.AsSpan(at)), BinaryPrimitives.ReadUInt16BigEndian(_hunk.AsSpan(at + 2)));
    }

    /// <summary>Whether bit (<paramref name="x"/>, <paramref name="y"/>) of character <paramref name="c"/>'s image is set.</summary>
    public bool Pixel(int c, int x, int y)
    {
        var (off, width) = Location(c);
        if (x < 0 || y < 0 || x >= width || y >= YSize)
            return false;
        int bit = off + x;
        int at = _charData + y * _modulo + (bit >> 3);
        return at < _hunk.Length && (_hunk[at] & (0x80 >> (bit & 7))) != 0;
    }

    /// <summary>
    /// The entries of a <c>.font</c> contents file (fch_FileID 0x0F00): each font file's name relative to the fonts
    /// folder (e.g. <c>protect/8</c>) and its height.
    /// </summary>
    public static IReadOnlyList<(string File, int YSize)> Contents(byte[] file)
    {
        if (file.Length < 4 || BinaryPrimitives.ReadUInt16BigEndian(file) != ContentsId)
            throw new DaxFormatException("not an Amiga .font contents file (file id is not 0x0F00)");
        int n = BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(2));
        const int Entry = 260; // fc_FileName[256], fc_YSize, fc_Style, fc_Flags
        if (4 + n * Entry > file.Length)
            throw new DaxFormatException("Amiga .font contents file: entries past the end");
        var o = new List<(string, int)>();
        for (int i = 0; i < n; i++)
        {
            var e = file.AsSpan(4 + i * Entry, Entry);
            int z = e[..256].IndexOf((byte)0);
            o.Add((Encoding.Latin1.GetString(z < 0 ? e[..256] : e[..z]), BinaryPrimitives.ReadUInt16BigEndian(e[256..])));
        }
        return o;
    }
}
