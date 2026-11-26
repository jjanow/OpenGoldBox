#!/usr/bin/env python3
"""The list window's highlight and page moves (GAME.OVR 0x2D594 step, 0x2D3D2 label skip, 0x2D502 page) from the
program itself: runs them under unicorn (por_emu.py) on generated windows and writes the highlight and window top they
leave, for GoldBox.Client's ListStepProgramTests (combat-menu.md "Spell lists' Next / Prev").

Usage: python3 -I por_list_oracle.py GAMEDIR OUT.json        (GAMEDIR: the PoR v1.3 files; needs unicorn 2.x)

The three are nested procedures of the list routine 0x2D669 (far, `retf 4`: the direction byte, then the parent's
frame pointer). The oracle builds that parent frame on the stack: its locals -0x59 (window rows) and -0x56 (entry
count), its arguments +0x0A (far pointer to the highlight word) and +0x14 (the list's head, entries linked at +0x2A,
label flag +0x29); the window's top is the global DS:6B29. The row draw 0x2D073 (after a page move) is trapped and
answered at once. Per case: rows, count, labels, top and highlight before, direction, then the highlight and top after
and 'oob' when 0x2C8CF was asked for an entry outside the list (it returns nil; the program then reads the label flag
at 0000:0029, the interrupt table, so the result depends on the machine and the test skips the case).
Nothing from the game is stored.
"""
import json
import os
import random
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from por_emu import Emu, STACKSEG  # noqa: E402

STEP = 0x2D594
SKIP = 0x2D3D2
PAGE = 0x2D502
NODE = 0x2C8CF
DRAWROWS = 0x2D073
TOP = 0x6B29
PARENT_BP = 0xE000
NODE_SIZE = 0x30


def find(dirpath, name):
    for f in os.listdir(dirpath):
        if f.upper() == name:
            return os.path.join(dirpath, f)
    raise FileNotFoundError(name)


class ListOracle:
    def __init__(self, gamedir):
        with open(find(gamedir, 'START.EXE'), 'rb') as f:
            exe = f.read()
        with open(find(gamedir, 'GAME.OVR'), 'rb') as f:
            ovr = f.read()
        self.e = e = Emu(exe, ovr)
        e.trap_ovr(DRAWROWS, self._draw)
        e.trap_ovr(NODE, self._node)
        self.cursor = e.alloc(16)
        self.nodes = e.alloc(NODE_SIZE * 64)
        self.count = 0
        self.oob = False
        self.drawn = []

    def _draw(self, e):
        self.drawn.append(e.arg(2))
        e.pop_ret(4)

    def _node(self, e):
        # 0x2C8CF(list, index) runs as it is; only note an index outside the list.
        idx = e.arg(4)
        if idx >= 0x8000 or idx >= self.count:
            self.oob = True

    def setup(self, rows, count, labels, top, row):
        e = self.e
        self.count = count
        seg, _ = self.nodes
        for i in range(count):
            lin = seg * 16 + i * NODE_SIZE
            e.wb(lin, bytes(NODE_SIZE))
            e.wb(lin + 0x29, [1 if i in labels else 0])
            nxt = (i + 1) * NODE_SIZE if i + 1 < count else 0
            e.wb(lin + 0x2A, struct.pack('<HH', nxt, seg if i + 1 < count else 0))
        pb = STACKSEG * 16 + PARENT_BP
        e.wb(pb - 0x59, struct.pack('<h', rows))
        e.wb(pb - 0x56, struct.pack('<h', count))
        cseg, coff = self.cursor
        e.wb(pb + 0x0A, struct.pack('<HH', coff, cseg))
        e.wb(pb + 0x14, struct.pack('<HH', 0, seg) if count else bytes(4))
        e.wb(cseg * 16 + coff, struct.pack('<h', row))
        e.set_dsw(TOP, top)

    def run(self, kind, rows, count, labels, top, row, down):
        e = self.e
        self.setup(rows, count, labels, top, row)
        self.oob = False
        self.drawn = []
        e.call({'step': STEP, 'skip': SKIP, 'page': PAGE}[kind], (1 if down else 0, PARENT_BP))
        cseg, coff = self.cursor
        after = struct.unpack('<h', e.rb(cseg * 16 + coff, 2))[0]
        return {'kind': kind, 'rows': rows, 'count': count, 'labels': sorted(labels), 'top': top, 'row': row,
                'down': down, 'rowAfter': after, 'topAfter': struct.unpack('<h', struct.pack('<H', e.dsw(TOP)))[0],
                'drawn': self.drawn, 'oob': self.oob}


def main(argv):
    if len(argv) != 3:
        print(__doc__, file=sys.stderr)
        return 2
    o = ListOracle(argv[1])
    rng = random.Random(5252)
    cases = []
    for k in range(3000):
        kind = ('step', 'skip', 'page')[k % 3]
        rows = rng.randint(1, 12)
        count = rng.randint(1, 40)
        if kind == 'page' and rng.random() < 0.8:
            count = max(count, rows)
        nl = rng.choice([0, 0, 1, 2, rng.randint(0, count)])
        labels = set(rng.sample(range(count), min(nl, count)))
        if rng.random() < 0.3:
            labels |= set(range(rng.randint(0, min(3, count))))  # leading labels, as the spell lists' headings
        top = rng.randint(0, max(0, count - rows)) if rng.random() < 0.9 else rng.randint(0, count - 1)
        row = rng.randint(top, min(top + rows, count) - 1)
        cases.append(o.run(kind, rows, count, labels, top, row, rng.random() < 0.5))
    oob = sum(c['oob'] for c in cases)
    with open(argv[2], 'w') as f:
        json.dump({'cases': cases}, f, separators=(',', ':'))
    print('%d cases (%d asked for an entry outside the list)' % (len(cases), oob))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
