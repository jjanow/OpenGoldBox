using System.Buffers.Binary;
using GoldBox.Data.Dax;

namespace GoldBox.Data.Module.Por;

/// <summary>
/// The Amiga v1.0 window frame (docs/re/por-art-formats.md §4 "Window frame"; P, read s39 in the Amiga <c>program</c>,
/// hunks numbered from 0 in file order; no emulator run). The window routine (hunk 25 +0x19F4, the copy of DOS START
/// 0x41F8) blanks its cells and then calls the frame routine (hunk 25 +0x16D4) with (x0, y0, x1, y1), which blits
/// (BltBitMap, minterm 0xC0: a plain copy of all planes) five images held as BitMaps at hunk 27 +0x77EC… (planes in
/// hunk 28), each from its own top-left corner: the top strip over cells x0…x1 of row y0 − 1, the bottom strip over the
/// same cells of row y1 + 1, the left side over rows y0…y1 of column x0 − 1, the right side over the same rows of column
/// x1 + 1, and the corner at the four corner cells. The style argument is not read. Pens 0 and 12–15 only (the same
/// colours in every palette table the program loads); the import draws them with table 3, the explore screen's.
/// <para>The import writes <c>art/amiga/frame/0/1</c>–<c>/5</c> (category <c>frame</c>): one picture each, id
/// <see cref="FirstId"/> + <see cref="Piece"/>. Nothing of the game is in this file: the code bytes below are only checked; the images come
/// from the player's <c>program</c>.</para>
/// </summary>
public static class PorAmigaFrame
{
    public const string Container = "frame";
    public const int FirstId = 1;
    public const string Category = "frame";
    public const int PaletteTable = 3;

    /// <summary>The five images, in the module's frame order.</summary>
    public enum Piece { Top = 0, Bottom = 1, Left = 2, Right = 3, Corner = 4 }

    // (hunk, offset, bytes): the frame routine as the reading relies on it.
    private static readonly (int Hunk, int At, string Hex)[] CodeChecks =
    [
        (25, 0x1A6C, "6100fc66"),                  // the window routine: the frame routine after its blank
        (25, 0x16F6, "5307530652055204"),          // x0 − 1, y0 − 1, x1 + 1, y1 + 1
        (25, 0x172A, "487800ff487800c0"),          // mask 0xFF, minterm 0xC0 (top strip)
        (25, 0x1746, "2f032f034879"),              // source x, y = 0; the top strip's BitMap
        (25, 0x179C, "2f032f034879"),              // the bottom strip's
        (25, 0x17F6, "2f032f034879"),              // the left side's
        (25, 0x184C, "2f032f034879"),              // the right side's
        (25, 0x1892, "2f022f024879"),              // a corner's (four times)
        (25, 0x1750, "4eba324e"),                  // BltBitMap (stub +0x49A0)
    ];

    // (piece, hunk 25 offset of its BitMap's reloc, BitMap at hunk 27).
    private static readonly (Piece Piece, int Reloc, int BitMap)[] Pieces =
    [
        (Piece.Top, 0x174C, 0x77EC),
        (Piece.Bottom, 0x17A2, 0x7810),
        (Piece.Left, 0x17FC, 0x7858),
        (Piece.Right, 0x1852, 0x7834),
        (Piece.Corner, 0x1898, 0x787C),
    ];

    private const int CodeHunk = 25, BitMapHunk = 27, PlaneHunk = 28, Depth = 4;

    /// <summary>
    /// The five images (8-bit pens, row after row) with their sizes, in <see cref="Piece"/> order, or null when the
    /// program is not laid out so. The sides keep the 8 pixels the blit copies of their 16-pixel-wide BitMaps.
    /// </summary>
    public static (int Width, int Height, byte[] Pixels)[]? Read(byte[] program)
    {
        try
        {
            var hunks = AmigaHunks.Parse(program);
            var relocs = AmigaHunks.Relocations(program).ToHashSet();
            int? Base(int h) => h < hunks.Count && hunks[h].DataOffset is { } at ? at : null;
            bool In(int h, int at, int length) => Base(h) is not null && at >= 0 && at + length <= hunks[h].Size;
            foreach (var (h, at, hex) in CodeChecks)
            {
                var want = Convert.FromHexString(hex);
                if (!In(h, at, want.Length) || !program.AsSpan(Base(h)!.Value + at, want.Length).SequenceEqual(want))
                    return null;
            }
            var images = new (int, int, byte[])[Pieces.Length];
            foreach (var (piece, reloc, bitmap) in Pieces)
            {
                if (!In(CodeHunk, reloc, 4) || !relocs.Contains(new AmigaReloc(CodeHunk, BitMapHunk, reloc))
                    || BinaryPrimitives.ReadUInt32BigEndian(program.AsSpan(Base(CodeHunk)!.Value + reloc)) != bitmap
                    || !In(BitMapHunk, bitmap, 8 + 4 * Depth))
                    return null;
                // struct BitMap: BytesPerRow, Rows, Flags, Depth, pad, Planes[8].
                var bm = program.AsSpan(Base(BitMapHunk)!.Value + bitmap);
                int bpr = BinaryPrimitives.ReadUInt16BigEndian(bm), rows = BinaryPrimitives.ReadUInt16BigEndian(bm[2..]);
                if (bm[5] != Depth || bpr == 0 || rows == 0)
                    return null;
                int width = piece is Piece.Top or Piece.Bottom ? 8 * bpr : 8;
                var planes = new int[Depth];
                for (int p = 0; p < Depth; p++)
                {
                    int at = bitmap + 8 + 4 * p;
                    planes[p] = (int)BinaryPrimitives.ReadUInt32BigEndian(bm[(8 + 4 * p)..]);
                    if (!relocs.Contains(new AmigaReloc(BitMapHunk, PlaneHunk, at)) || !In(PlaneHunk, planes[p], bpr * rows))
                        return null;
                }
                var px = new byte[width * rows];
                var plane = program.AsSpan(Base(PlaneHunk)!.Value);
                for (int y = 0; y < rows; y++)
                    for (int x = 0; x < width; x++)
                        for (int p = 0; p < Depth; p++)
                            if ((plane[planes[p] + y * bpr + x / 8] & (0x80 >> (x % 8))) != 0)
                                px[y * width + x] |= (byte)(1 << p);
                images[(int)piece] = (width, rows, px);
            }
            return images;
        }
        catch (DaxFormatException)
        {
            return null;
        }
    }
}
