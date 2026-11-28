using System.Buffers.Binary;
using GoldBox.Core.Explore;
using GoldBox.Data.Dax;

namespace GoldBox.Data.Module.Por;

/// <summary>
/// The Amiga land's recovery tables (<see cref="AmigaOverlandRecoveryTables"/>, docs/re/geo-format.md "The land
/// recovers", Amiga; read s60) from the player's <c>program</c> (Pool of Radiance Amiga v1.0), at the places the routine
/// hunk 13 +0x2872 and the generator hunk 27 +0x72C8 name: the code bytes there are checked first and the tables are
/// followed through the routine's relocated <c>lea</c>s, so nothing from the program is kept in our code.
/// </summary>
public static class PorAmigaOverlandRecovery
{
    /// <summary>Code bytes checked (hunk, offset, hex; relocated longs are not compared).</summary>
    private static readonly (int Hunk, int At, string Hex)[] Checks =
    [
        (13, 0x2872, "4e55ffac"),               // link a5,#-$54
        (13, 0x2888, "41f9"), (13, 0x2892, "700a12d851c8fffc"),     // lea list A; moveq #10; copy loop
        (13, 0x289A, "41f9"), (13, 0x28A4, "701112d851c8fffc"),     // lea list B; moveq #17
        (13, 0x28AC, "41f9"), (13, 0x28B6, "700f12d851c8fffc"),     // lea map; moveq #15
        (13, 0x28BE, "41f9"), (13, 0x28D2, "41f9"),                 // lea pan x, pan y
        (13, 0x28E6, "4a076700"),              // tst.b d7 (animated); beq
        (13, 0x28EC, "4878"), (13, 0x28F0, "4878"),                 // pea y, pea x (the first view)
        (13, 0x2900, "7008ba00"),               // the frame loop: moveq #8; cmp.b d0,d5
        (13, 0x290A, "700abc00"), (13, 0x296C, "7011bc00"),         // the random pass's list loops
        (13, 0x29FA, "700abc00"), (13, 0x2A58, "7011bc00"),         // the whole pass's list loops
        (13, 0x2AB4, "4a07671a"), (13, 0x2AB8, "4878"), (13, 0x2ABC, "4878"), // pea y, pea x (the last view)
        (13, 0x63C4, "4ef9"),                   // the recolour stub: jmp
        (27, 0x7160, "4e55000048e73f3a610000c4"),
        (27, 0x71CC, "b2311000"),               // cmp.b (a1,d1.w),d1: the map
        (27, 0x71D4, "4aad0010670000"),         // the random flag
        (27, 0x71DC, "610000ea4a006b000006"),   // bsr generator; tst.b d0; bmi keep
        (27, 0x72C8, "48e76080123a0046"),       // the generator: state at +0x7314
        (27, 0x72D8, "41fa003b"), (27, 0x72EC, "41fa0037"),         // lea the nibble tables (+0x7315, +0x7325)
    ];

    /// <summary>The tables from <paramref name="program"/>, or null when its code is not laid out so.</summary>
    public static AmigaOverlandRecoveryTables? Read(byte[] program)
    {
        try
        {
            var hunks = AmigaHunks.Parse(program);
            var relocs = AmigaHunks.Relocations(program);
            int? Base(int h) => h < hunks.Count && hunks[h].DataOffset is { } at ? at : null;
            bool In(int h, int at, int length) => Base(h) is not null && at >= 0 && at + length <= hunks[h].Size;
            foreach (var (h, at, hex) in Checks)
            {
                var want = Convert.FromHexString(hex);
                if (!In(h, at, want.Length) || !program.AsSpan(Base(h)!.Value + at, want.Length).SequenceEqual(want))
                    return null;
            }
            (int Hunk, int At)? Pointer(int h, int at)
            {
                foreach (var r in relocs)
                    if (r.Hunk == h && r.Offset == at)
                    {
                        int to = (int)BinaryPrimitives.ReadUInt32BigEndian(program.AsSpan(Base(h)!.Value + at));
                        return In(r.Target, to, 1) ? (r.Target, to) : null;
                    }
                return null;
            }
            if (Pointer(13, 0x63C6) != (27, 0x7160))
                return null;
            byte B(int h, int at) => program[Base(h)!.Value + at];
            int W(int h, int at) => BinaryPrimitives.ReadUInt16BigEndian(program.AsSpan(Base(h)!.Value + at));
            byte[]? Bytes((int Hunk, int At)? p, int n) =>
                p is var (h, at) && In(h, at, n) ? program.AsSpan(Base(h)!.Value + at, n).ToArray() : null;
            int countA = B(13, 0x2893) + 1, countB = B(13, 0x28A5) + 1, colours = B(13, 0x28B7) + 1;
            int frames = B(13, 0x2901) + 1;
            // The pan tables are copied as 18 bytes (four longs and a word: hunk 13 +0x28C8–0x28D0, +0x28DC–0x28E4).
            if (colours != OverlandRecovery.Colours || 2 * frames != 18
                || B(13, 0x290B) + 1 != countA || B(13, 0x296D) + 1 != countB
                || B(13, 0x29FB) + 1 != countA || B(13, 0x2A59) + 1 != countB)
                return null;
            if (Bytes(Pointer(13, 0x288A), countA) is not { } listA || Bytes(Pointer(13, 0x289C), countB) is not { } listB
                || Bytes(Pointer(13, 0x28AE), colours) is not { } map
                || Bytes(Pointer(13, 0x28C0), 2 * frames) is not { } panX || Bytes(Pointer(13, 0x28D4), 2 * frames) is not { } panY
                || Bytes((27, 0x7315), 16) is not { } high || Bytes((27, 0x7325), 16) is not { } low)
                return null;
            var pan = new (int X, int Y)[frames];
            for (int f = 0; f < frames; f++)
                pan[f] = (BinaryPrimitives.ReadInt16BigEndian(panX.AsSpan(2 * f)), BinaryPrimitives.ReadInt16BigEndian(panY.AsSpan(2 * f)));
            // pea y, then pea x: x is the first argument (the view routine hunk 25 +0x1AD6 reads it at 8(a5)).
            var tables = new AmigaOverlandRecoveryTables(listA, listB, map, (W(13, 0x28F2), W(13, 0x28EE)),
                (W(13, 0x2ABE), W(13, 0x2ABA)), pan, high, low);
            return tables.Problem() is null ? tables : null;
        }
        catch (DaxFormatException)
        {
            return null;
        }
    }
}
