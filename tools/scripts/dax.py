#!/usr/bin/env python3
"""Gold Box DAX container reader (DOS and Amiga Pool of Radiance) - list / extract.

Usage (run with ``python3 -I``; inputs are untrusted data):
    dax.py list    FILE.DAX [...]
    dax.py extract FILE.DAX OUTDIR      # writes <stem>_<id>.bin, decompressed

A DAX file is a small index followed by independently compressed blocks, each
addressed by a block id (picture number, monster number, ...).  Two variants:

DOS (little-endian, SSI 1988-89; verified on every PoR v1.2 *.DAX)
    u16   index_bytes               (= 9 * entry count)
    entry[n], 9 bytes each:
        u8    id
        i32   offset                (from the end of the index)
        u16   raw_size              (decompressed size)
        u16   comp_size
    data: blocks, RLE compressed ("PackBits"-like, signed control byte):
        c >= 0   : copy the next c+1 bytes literally
        c <  0   : repeat the next byte -c times
    Matches Simeon Pilgrim's coab C# port (Classes/DaxFiles/DaxFileCache.cs,
    https://github.com/simeonpilgrim/coab) and decodes every PoR block to exactly raw_size.

Amiga (big-endian, Ubi Soft port 1990; verified on every PoR v1.0 *.dax)
    u16   index_bytes               (= 10 * entry count)
    entry[n], 10 bytes each:
        u16   id
        u32   offset                (from the end of the index)
        u16   comp_size
        u16   raw_size
    data: each block is a ByteKiller (Jek) crunched stream, decoded backwards:
        last u32 = raw size, previous u32 = checksum, previous u32 = first bit
        buffer (its highest set bit is a sentinel).  Longs are read from the end
        towards the start; bits are taken LSB first; a refilled long gets
        bit 31 set as the new sentinel.  Output is written from the end.
          %00 nnn          : n+1 literal bytes (8 bits each)
          %01 oooooooo     : copy 2 bytes from out+o
          %1 00 o*9        : copy 3 bytes        %1 01 o*10 : copy 4 bytes
          %1 10 n*8 o*12   : copy n+1 bytes      %1 11 n*8  : n+9 literal bytes
        XOR of every consumed long (including the first) equals the checksum;
        every PoR block checks out to 0 and consumes the stream exactly.
"""
import os
import struct
import sys


class DaxEntry:
    __slots__ = ("id", "offset", "raw_size", "comp_size")

    def __init__(self, id_, offset, raw_size, comp_size):
        self.id, self.offset, self.raw_size, self.comp_size = id_, offset, raw_size, comp_size


def _parse_index(data, big_endian):
    if len(data) < 2:
        return None
    if big_endian:
        n = struct.unpack(">H", data[:2])[0]
        esz = 10
    else:
        n = struct.unpack("<H", data[:2])[0]
        esz = 9
    if n == 0 or n % esz or 2 + n > len(data):
        return None
    ents = []
    for k in range(2, 2 + n, esz):
        if big_endian:
            id_, off, comp, raw = struct.unpack(">HIHH", data[k:k + 10])
        else:
            id_, off, raw, comp = struct.unpack("<BiHH", data[k:k + 9])
        ents.append(DaxEntry(id_, off, raw, comp))
    base = 2 + n
    # Consistency: every block must lie inside the file.
    for e in ents:
        if e.offset < 0 or base + e.offset + e.comp_size > len(data):
            return None
    return ents


class DaxFile:
    """Parsed DAX file. ``variant`` is 'dos' or 'amiga' (auto-detected)."""

    def __init__(self, path):
        with open(path, "rb") as f:
            self.data = f.read()
        self.path = path
        ents = _parse_index(self.data, big_endian=False)
        self.variant = "dos"
        if ents is None:
            ents = _parse_index(self.data, big_endian=True)
            self.variant = "amiga"
        if ents is None:
            raise ValueError("%s: not a DAX file" % path)
        self.entries = ents
        self.base = 2 + struct.unpack("<H" if self.variant == "dos" else ">H", self.data[:2])[0]

    def raw_block(self, e):
        return self.data[self.base + e.offset:self.base + e.offset + e.comp_size]

    def block(self, e):
        comp = self.raw_block(e)
        if self.variant == "dos":
            out = unrle(comp, e.raw_size)
        else:
            out = unbytekiller(comp)
        if len(out) != e.raw_size:
            raise ValueError("block %d: got %d bytes, index says %d" % (e.id, len(out), e.raw_size))
        return out

    def blocks(self):
        for e in self.entries:
            yield e.id, self.block(e)


def unrle(comp, raw_size):
    """DOS DAX RLE (see module docstring)."""
    out = bytearray()
    i = 0
    n = len(comp)
    while i < n:
        c = comp[i]
        if c < 0x80:
            out += comp[i + 1:i + 2 + c]
            i += c + 2
        else:
            out += bytes([comp[i + 1]]) * (256 - c)
            i += 2
    # [?] a few encoders pad past raw_size; the game only reads raw_size bytes.
    return bytes(out[:raw_size])


def unbytekiller(comp):
    """Amiga DAX ByteKiller decrunch (see module docstring)."""
    if len(comp) % 4 or len(comp) < 12:
        raise ValueError("ByteKiller stream length %d not a multiple of 4" % len(comp))
    longs = struct.unpack(">%dI" % (len(comp) // 4), comp)
    pos = len(longs) - 1
    size = longs[pos]; pos -= 1
    checksum = longs[pos]; pos -= 1
    cur = longs[pos]; pos -= 1
    x = checksum ^ cur
    out = bytearray(size)
    o = size

    def bit():
        nonlocal cur, pos, x
        b = cur & 1
        cur >>= 1
        if cur == 0:
            if pos < 0:
                raise ValueError("ByteKiller stream underrun")
            v = longs[pos]; pos -= 1
            x ^= v
            b = v & 1
            cur = (v >> 1) | 0x80000000
        return b

    def bits(k):
        v = 0
        for _ in range(k):
            v = (v << 1) | bit()
        return v

    def literal(k):
        nonlocal o
        for _ in range(k):
            o -= 1
            out[o] = bits(8)

    def copy(k, off):
        nonlocal o
        for _ in range(k):
            o -= 1
            out[o] = out[o + off]

    while o > 0:
        if not bit():
            if not bit():
                literal(bits(3) + 1)
            else:
                copy(2, bits(8))
        else:
            t = bits(2)
            if t == 3:
                literal(bits(8) + 9)
            elif t == 2:
                k = bits(8) + 1
                copy(k, bits(12))
            elif t == 1:
                copy(4, bits(10))
            else:
                copy(3, bits(9))
    if x != 0:
        raise ValueError("ByteKiller checksum mismatch")
    return bytes(out)


def main(argv):
    if len(argv) < 3 or argv[1] not in ("list", "extract"):
        print(__doc__.split("\n\n")[1])
        return 2
    if argv[1] == "list":
        for p in argv[2:]:
            d = DaxFile(p)
            print("%s: %s, %d blocks" % (p, d.variant, len(d.entries)))
            for e in d.entries:
                print("  id %5d  off %7d  comp %6d  raw %6d" % (e.id, e.offset, e.comp_size, e.raw_size))
        return 0
    d = DaxFile(argv[2])
    outdir = argv[3]
    os.makedirs(outdir, exist_ok=True)
    stem = os.path.splitext(os.path.basename(argv[2]))[0]
    for id_, blk in d.blocks():
        with open(os.path.join(outdir, "%s_%03d.bin" % (stem, id_)), "wb") as f:
            f.write(blk)
    print("%s: %d blocks -> %s" % (argv[2], len(d.entries), outdir))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
