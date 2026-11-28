#!/usr/bin/env python3
"""The Amiga land's recovery (hunk 13 +0x2872) and its tile recolour (hunk 27 +0x7160) run by Pool of Radiance Amiga
v1.0's own m68k code under unicorn, for GoldBox.Core's AmigaOverlandRecoveryTests (docs/re/geo-format.md "The land
recovers", Amiga).

Usage: python3 -I por_amiga_recolour_oracle.py PROGRAM OUT.json [CASES]   (needs unicorn 2.x)

The hunk file is loaded as in por_amiga_sound_oracle.py (hunk i at 0x100000 * (i + 1), RELOC32 applied). The
recolour's random mode calls the generator hunk 27 +0x72C8, which mixes its state byte (hunk 27 +0x7314) with the
beam position VHPOSR ($DFF006 / $DFF007); every read of those two bytes is fed from a 32-bit LCG
(x := x * 1103515245 + 12345, the byte (x >> 16) & 0xFF) whose seed and read count are kept, so the C# port can replay
it.

- "recolour" cases: a tile sheet block in the program's memory layout (a 0x14-byte head, then 4 planes 0x2400 bytes
  apart, two 24-pixel tiles side by side in 6-byte rows: item t at 72 t, + 3 when odd), random 4-bit pixels in one
  item, a random colour map, the random flag and the generator state; the routine is called with (map, item, flag,
  block) and the item's pixels after, the beam bytes read and the state after are kept.
- "recovery" cases: hunk 13 +0x2872 itself with the animated flag 1 and 0, the two sheet pointers (hunk 32 +0xB18,
  +0xB1C) set to blocks with random pixels in every listed tile; its calls to the pan view (hunk 13 +0x6490 → hunk 25
  +0x1AD6), the screen copy (+0x63FA → hunk 25 +0x896) and the pause (+0x6460 → hunk 25 +0xD7A) are trapped and
  recorded with their arguments.

Nothing from the game is stored: the output holds only random inputs and the bytes the routines computed.
"""
import json
import os
import random
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from por_amiga_sound_oracle import Amiga, base, CUSTOM  # noqa: E402

from unicorn import UC_HOOK_MEM_READ, UC_HOOK_CODE  # noqa: E402
from unicorn.m68k_const import UC_M68K_REG_A7, UC_M68K_REG_PC  # noqa: E402

RECOLOUR = (27, 0x7160)
RECOVERY = (13, 0x2872)
STATE = (27, 0x7314)
LOW, HIGH = (32, 0xB18), (32, 0xB1C)
VIEW, COPY, PAUSE = (13, 0x6490), (13, 0x63FA), (13, 0x6460)
PLANE = 0x2400
HEAD = 0x14
BLOCK_A, BLOCK_B, MAPS = 0x10000, 0x20000, 0x30000
CODE = [
    (27, 0x7160, '4e55000048e73f3a'),
    (27, 0x72C8, '48e76080123a0046'),
    (13, 0x2872, '4e55ffac'),
    (14, 0x22B, '65666768696a6b71e0e1e2'),
    (14, 0x248, '000102030401060708090a0b02020a0f'),
]


def addr(hk):
    return base(hk[0]) + hk[1]


class Oracle(Amiga):
    def __init__(self, program, rnd):
        super().__init__(program)
        for h, off, hexs in CODE:
            b = bytes.fromhex(hexs)
            if bytes(self.hunks[h][off:off + len(b)]) != b:
                raise SystemExit('bytes differ at hunk %d +0x%X: not Amiga PoR v1.0' % (h, off))
        self.rnd = rnd
        self.lcg = 0
        self.beam = 0
        self.calls = []
        uc = self.uc
        uc.hook_add(UC_HOOK_MEM_READ, self._read, begin=CUSTOM + 6, end=CUSTOM + 7)
        for name, hk in (('view', VIEW), ('copy', COPY), ('pause', PAUSE)):
            a = addr(hk)
            uc.hook_add(UC_HOOK_CODE, self._stub, name, begin=a, end=a)

    def _read(self, uc, _access, a, size, _value, _ud):
        for k in range(size):
            self.lcg = (self.lcg * 1103515245 + 12345) & 0xFFFFFFFF
            uc.mem_write(a + k, bytes([(self.lcg >> 16) & 0xFF]))
            self.beam += 1

    def _stub(self, uc, _a, _s, name):
        sp = uc.reg_read(UC_M68K_REG_A7)
        ret, a1, a2 = struct.unpack('>III', bytes(uc.mem_read(sp, 12)))
        self.calls.append([name, a1, a2] if name == 'view' else [name])
        uc.reg_write(UC_M68K_REG_A7, sp + 4)
        uc.reg_write(UC_M68K_REG_PC, ret)

    def call(self, a, d0=None, args=()):
        sp = 0x7F000
        for v in reversed(args):
            sp -= 4
            self.uc.mem_write(sp, struct.pack('>I', v))
        sp -= 4
        self.uc.mem_write(sp, struct.pack('>I', 0x80000))
        self.uc.reg_write(UC_M68K_REG_A7, sp)
        self.uc.emu_start(a, 0x80000, count=500_000_000)
        if self.uc.reg_read(UC_M68K_REG_PC) != 0x80000:
            raise SystemExit('the call did not return')

    def state(self):
        return self.uc.mem_read(addr(STATE), 1)[0]

    def set_state(self, s):
        self.uc.mem_write(addr(STATE), bytes([s]))
        self._smc(self.uc, None, addr(STATE), 1, 0, None)

    def put_tile(self, blk, t, px):
        """Writes 24 x 24 pens (row-major) into item t of the block at blk (plane k = bit k, bit 7 the leftmost)."""
        off = 72 * (t & ~1) + (3 if t & 1 else 0)
        for k in range(4):
            for r in range(24):
                for g in range(3):
                    b = 0
                    for x in range(8):
                        if px[r * 24 + g * 8 + x] >> k & 1:
                            b |= 0x80 >> x
                    self.uc.mem_write(blk + HEAD + k * PLANE + off + r * 6 + g, bytes([b]))

    def get_tile(self, blk, t):
        off = 72 * (t & ~1) + (3 if t & 1 else 0)
        planes = [bytes(self.uc.mem_read(blk + HEAD + k * PLANE, PLANE)) for k in range(4)]
        px = []
        for r in range(24):
            for g in range(3):
                for x in range(8):
                    c = 0
                    for k in range(4):
                        if planes[k][off + r * 6 + g] & (0x80 >> x):
                            c |= 1 << k
                    px.append(c)
        return px


def tile(rnd, favour):
    return [rnd.choice(favour) if rnd.random() < 0.6 else rnd.randrange(16) for _ in range(576)]


def main():
    program, out = sys.argv[1], sys.argv[2]
    n = int(sys.argv[3]) if len(sys.argv) > 3 else 30
    rnd = random.Random(60)
    o = Oracle(program, rnd)
    uc = o.uc
    for blk in (BLOCK_A, BLOCK_B):
        uc.mem_write(blk, bytes(HEAD + 4 * PLANE))
    amiga_map = list(bytes.fromhex(CODE[4][2]))
    cases = []
    for i in range(n):
        to = amiga_map if i < 6 else list(range(16))
        if i >= 6:
            for _ in range(rnd.choice([1, 2, 3, 4])):
                to[rnd.randrange(16)] = rnd.randrange(16)
        t = rnd.randrange(128)
        px = tile(rnd, [5, 12, 13, 14, 1, 2, 10] if i < 6 else [c for c in range(16) if to[c] != c] or [0])
        flag = 0 if i % 4 == 0 else 1
        s = rnd.randrange(256)
        o.set_state(s)
        o.put_tile(BLOCK_A, t, px)
        uc.mem_write(MAPS, bytes(to))
        o.beam, o.lcg = 0, rnd.getrandbits(32)
        lcg = o.lcg
        o.call(addr(RECOLOUR), args=(MAPS, t, flag, BLOCK_A))
        cases.append(dict(item=t, flag=flag, map=bytes(to).hex(), state=s, before=bytes(px).hex(),
                          after=bytes(o.get_tile(BLOCK_A, t)).hex(), beam_seed=lcg, beam_reads=o.beam,
                          state_after=o.state()))
    lists = bytes(o.hunks[14][0x22B:0x22B + 29])
    runs = []
    for animated in (1, 0):
        tiles = {}
        for t in lists:
            px = tile(rnd, [5, 12, 13, 14])
            tiles[t] = px
            o.put_tile(BLOCK_B if t > 0x7F else BLOCK_A, t & 0x7F, px)
        uc.mem_write(addr(LOW), struct.pack('>I', BLOCK_A))
        uc.mem_write(addr(HIGH), struct.pack('>I', BLOCK_B))
        s = rnd.randrange(256)
        o.set_state(s)
        o.beam, o.calls, o.lcg = 0, [], rnd.getrandbits(32)
        lcg = o.lcg
        o.call(addr(RECOVERY), args=(animated,))
        after = {t: o.get_tile(BLOCK_B if t > 0x7F else BLOCK_A, t & 0x7F) for t in lists}
        runs.append(dict(animated=animated, state=s, state_after=o.state(), beam_seed=lcg, beam_reads=o.beam,
                         calls=o.calls,
                         tiles={'%d' % t: bytes(tiles[t]).hex() for t in lists},
                         after={'%d' % t: bytes(after[t]).hex() for t in lists}))
    json.dump(dict(routine='Amiga hunk 27 +0x7160, hunk 13 +0x2872', cases=cases, recovery=runs), open(out, 'w'),
              indent=0)
    print(f'{len(cases)} recolour cases; recovery calls {[len(r["calls"]) for r in runs]}, '
          f'beam reads {[r["beam_reads"] for r in runs]}')


if __name__ == '__main__':
    main()
