#!/usr/bin/env python3
"""The tile recolour START 0x464:0x1E5C (linear 0x649C, the land's recovery 0x20D50) run by the program itself
(por_emu.py, unicorn), for GoldBox.Core's OverlandRecoveryProgramTests (docs/re/geo-format.md "The land recovers").

Usage: python3 -I por_recolour_oracle.py GAMEDIR OUT.json [CASES]   (needs unicorn 2.x)

Each case builds a one-item 24 x 24 block in EGA mode (DS:4A1E = 1: per row, per 8-pixel group, four plane bytes;
the plane of each colour bit from the program's table DS:25F6; bit 7 the leftmost pixel) from random 4-bit pixels,
picks a random RandSeed, a colour map (identity "from" with a few random changes) and the random flag, calls the
routine, and records the pixels before and after (decoded with the same plane table) and RandSeed after. GetMem
(0xAF8:0x23F) comes from the emulator's arena; the free (0x6BD5) is trapped. Nothing from the game is stored: the
output holds only random inputs and the bytes the routine computed.
"""
import json
import os
import random
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from por_emu import Emu  # noqa: E402

from unicorn.x86_const import UC_X86_REG_AX, UC_X86_REG_DX  # noqa: E402

SEG = 0x464
RECOLOUR = 0x1E5C
FREE = 0x6BD5 - SEG * 16
GETMEM = (0xAF8, 0x23F)
VIDEO = 0x4A1E
PLANES = 0x25F6
SIZE, GROUPS = 24, 3


def main():
    game, out = sys.argv[1], sys.argv[2]
    n = int(sys.argv[3]) if len(sys.argv) > 3 else 40
    e = Emu(open(os.path.join(game, 'START.EXE'), 'rb').read(), open(os.path.join(game, 'GAME.OVR'), 'rb').read())

    def getmem(em):
        size = em.arg(0)
        seg, off = em.alloc(size + 16)
        em.uc.reg_write(UC_X86_REG_DX, seg)
        em.pop_ret(2, ax=off)

    e.trap_start(*GETMEM, getmem)
    e.trap_start(SEG, FREE, e.ret(4))
    e.set_dsb(VIDEO, 1)
    planes = [e.dsb(PLANES + k) for k in range(4)]
    rnd = random.Random(58)
    cases = []
    from_ = list(range(16))
    for i in range(n):
        px = [rnd.randrange(16) if rnd.random() < 0.5 else rnd.choice([0, 7]) for _ in range(SIZE * SIZE)]
        to = list(range(16))
        for _ in range(rnd.choice([1, 2, 3])):
            to[rnd.randrange(16)] = rnd.randrange(16)
        if i < 4:
            to = [1, 1, 2, 3, 4, 5, 6, 2, 8, 9, 10, 11, 12, 13, 14, 15] if i % 2 == 0 else \
                [0, 1, 2, 3, 4, 5, 6, 2, 8, 9, 10, 11, 12, 13, 14, 15]
        flag = 1 if i % 3 else 0
        seed = rnd.getrandbits(32)
        data = bytearray(SIZE * GROUPS * 4)
        for r in range(SIZE):
            for g in range(GROUPS):
                for k in range(4):
                    b = 0
                    for x in range(8):
                        if px[r * SIZE + g * 8 + x] & planes[k]:
                            b |= 0x80 >> x
                    data[(r * GROUPS + g) * 4 + k] = b
        e.reset_heap()
        bseg, _ = e.alloc(0x17 + len(data) + 16)
        hdr = bytearray(0x17)
        struct.pack_into('<HH', hdr, 0, SIZE, GROUPS)
        hdr[8] = 1
        struct.pack_into('<H', hdr, 0x11, len(data))
        e.wb(bseg * 16, bytes(hdr) + bytes(data))
        fseg, _ = e.alloc(32)
        e.wb(fseg * 16, bytes(from_) + bytes(to))
        e.set_randseed(seed)
        # Pascal order: from (far), to (far), index (word), flag (byte), block (far).
        e.call((SEG, RECOLOUR), [fseg, 0, fseg, 16, 0, flag, bseg, 0])
        res = e.rb(bseg * 16 + 0x17, len(data))
        after = []
        for r in range(SIZE):
            for g in range(GROUPS):
                for x in range(8):
                    c = 0
                    for k in range(4):
                        if res[(r * GROUPS + g) * 4 + k] & (0x80 >> x):
                            c |= planes[k]
                    after.append(c)
        cases.append(dict(seed=seed, flag=flag, to=bytes(to).hex(), before=bytes(px).hex(), after=bytes(after).hex(),
                          seed_after=e.randseed()))
    json.dump(dict(routine='START 0x464:0x1E5C', planes=planes, cases=cases), open(out, 'w'), indent=0)
    print(f'{len(cases)} cases, planes {planes}')


if __name__ == '__main__':
    main()
