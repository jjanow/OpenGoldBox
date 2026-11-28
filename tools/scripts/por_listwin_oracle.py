#!/usr/bin/env python3
"""The list routine GAME.OVR 0x2D669 run whole (with the menu routine 0x2CC8C under it) from the program itself: generated
lists and key scripts, for GoldBox.Client's ListWindowProgramTests (combat-menu.md "Spell lists' Next / Prev",
s60-lane-T.md).

Usage: python3 -I por_listwin_oracle.py GAMEDIR OUT.json      (GAMEDIR: the PoR v1.3 files; needs unicorn 2.x)

0x2D669(prompt, word, c24, c22, c20, x0, y0, x1, y1, head, exit, var redraw, var position, var result) is called with
a list of generated entries (Pascal string at +0, label flag +0x29, next +0x2A) and its window. The menu routine
0x2CC8C runs as it is: KeyPressed (START 0xA6FD) answers yes and ReadKey (START 0x89E9) gives the script's bytes
(0, scan for an extended key); GetTime (START 0x7CCF) is 0, so no picture frame and no timeout. The text draws are
trapped and logged ('ms' / 'mg' when drawn inside the menu routine): START 0x76B6 (string at column, row, colour), START 0x726F (glyph run: column, row, colour,
count, character, flag), START 0x4047 (window inside blank). Each script ends with Escape, so every list returns.
Per case: the arguments, the keys, then the position, the window top DS:6B29, the menu cell DS:6B2B, the entry
returned (its index, or -1 for nil), the key returned, 'oob' when 0x2C8CF was asked for an entry outside the list
(it returns nil and the program reads the label flag or the text at 0000:0029 / 0000:0000, the interrupt table: the
result depends on the machine and the test skips the case), the menu lines handed to 0x2CC8C (one per pass) and the draw
log. Nothing from the game is stored: the entries' texts are generated.
"""
import json
import os
import random
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from por_emu import Emu  # noqa: E402

LIST = 0x2D669
MENU = 0x2CC8C
NTH = 0x2C8CF        # entry k of a list (runs as it is; an index outside the list is noted)
MENU_BACK = 0x2D8AF   # 0x2D669's instruction after its call of the menu routine
TOP, CELL = 0x6B29, 0x6B2B
VMPTR = 0x49D2
TIMEOUT = (0x6B2E, 0x6B30)
NODE = 0x30


def find(dirpath, name):
    for f in os.listdir(dirpath):
        if f.upper() == name:
            return os.path.join(dirpath, f)
    raise FileNotFoundError(name)


def s16(v):
    return v - 0x10000 if v >= 0x8000 else v


class ListWindowOracle:
    def __init__(self, gamedir):
        with open(find(gamedir, 'START.EXE'), 'rb') as f:
            exe = f.read()
        with open(find(gamedir, 'GAME.OVR'), 'rb') as f:
            ovr = f.read()
        self.e = e = Emu(exe, ovr)
        e.trap_start(0x709, 0x626, self._string)
        e.trap_start(0x709, 0x1DF, self._glyphs)
        e.trap_start(0x3F1, 0x137, self._blank)
        e.trap_start(0x709, 0xC3F, lambda em: em.pop_ret(0, 0))          # GetTime: 0 (dx too, below)
        e.trap_start(0xA40, 0x2FD, self._pressed)
        e.trap_start(0x899, 0x59, self._readkey)
        e.trap_ovr(MENU, self._menu)
        e.trap_ovr(MENU_BACK, self._menu_back)
        e.trap_ovr(NTH, self._nth)
        self.oob = False
        self.count = 0
        self.inmenu = False
        self.vm = e.alloc(0x800)
        self.nodes = e.alloc(NODE * 80)
        self.strings = e.alloc(0x200)
        self.vars = e.alloc(16)
        self.log = []
        self.keys = []
        self.menus = []

    # ---- traps ----
    def _str(self, seg, off):
        lin = seg * 16 + off
        n = self.e.rb(lin)[0]
        return self.e.rb(lin + 1, n).decode('latin-1')

    def _string(self, e):
        off, seg, colour, row, col = (e.arg(k) for k in (0, 2, 4, 6, 8))
        self.log.append(['ms' if self.inmenu else 's', col & 0xFF, row & 0xFF, colour & 0xFF, self._str(seg, off)])
        e.pop_ret(10)

    def _glyphs(self, e):
        flag, ch, count, colour, row, col = (e.arg(k) & 0xFF for k in (0, 2, 4, 6, 8, 10))
        self.log.append(['mg' if self.inmenu else 'g', col, row, colour, count, ch, flag])
        e.pop_ret(12)

    def _blank(self, e):
        y1, x1, y0, x0 = (e.arg(k) & 0xFF for k in (0, 2, 4, 6))
        self.log.append(['b', x0, y0, x1, y1])
        e.pop_ret(8)

    def _pressed(self, e):
        e.pop_ret(0, 1 if self.keys else 0)

    def _readkey(self, e):
        e.pop_ret(0, self.keys.pop(0) if self.keys else 0x1B)

    def _menu(self, e):
        # 0x2CC8C(prompt, words, colour, colour, colour, keypad, animate, var ext): note its menu line, then run it
        woff, wseg = e.arg(0xE), e.arg(0x10)
        self.menus.append(self._str(wseg, woff))
        self.inmenu = True

    def _nth(self, e):
        idx = e.arg(4)
        if idx >= 0x8000 or idx >= self.count:
            self.oob = True

    def _menu_back(self, e):
        self.inmenu = False

    # ---- one call ----
    def run(self, case):
        e = self.e
        self.log, self.menus = [], []
        self.oob = False
        self.count = len(case['rows'])
        self.keys = list(case['keys'])
        e.wb(self.vm[0] * 16, bytes(0x800))
        e.set_dsptr(VMPTR, self.vm)
        for o in TIMEOUT:
            e.set_dsw(o, 0)
        e.set_dsw(TOP, case['top'])
        e.set_dsb(CELL, case['cell'])
        seg = self.nodes[0]
        texts, labels = case['rows'], set(case['labels'])
        for i, t in enumerate(texts):
            lin = seg * 16 + i * NODE
            raw = t.encode('latin-1')
            e.wb(lin, bytes(NODE))
            e.wb(lin, bytes([len(raw)]) + raw)
            e.wb(lin + 0x29, [1 if i in labels else 0])
            last = i + 1 == len(texts)
            e.wb(lin + 0x2A, struct.pack('<HH', 0 if last else (i + 1) * NODE, 0 if last else seg))
        sseg = self.strings[0]
        e.wb(sseg * 16, bytes([len(case['prompt'])]) + case['prompt'].encode('latin-1'))
        e.wb(sseg * 16 + 0x100, bytes([len(case['word'])]) + case['word'].encode('latin-1'))
        vseg = self.vars[0]
        e.wb(vseg * 16, bytes(16))
        e.wb(vseg * 16 + 0, [case['redraw']])
        e.wb(vseg * 16 + 2, struct.pack('<h', case['position']))
        x0, y0, x1, y1 = case['window']
        c24, c22, c20 = case['colours']
        head = (seg, 0) if texts else (0, 0)
        args = (sseg, 0, sseg, 0x100, c24, c22, c20, x0, y0, x1, y1, head[0], head[1], case['exit'],
                vseg, 0, vseg, 2, vseg, 4)
        ax, _ = e.call(LIST, args)
        roff, rseg = struct.unpack('<HH', e.rb(vseg * 16 + 4, 4))
        res = -1 if (roff, rseg) == (0, 0) else (rseg * 16 + roff - seg * 16) // NODE
        return dict(case, positionAfter=s16(struct.unpack('<H', e.rb(vseg * 16 + 2, 2))[0]),
                    topAfter=s16(e.dsw(TOP)), cellAfter=e.dsb(CELL), redrawAfter=e.rb(vseg * 16)[0],
                    result=res, key=ax & 0xFF, oob=self.oob, keysLeft=len(self.keys), menus=self.menus, draws=self.log)


WORDS = ['', 'Cast', 'Take', 'Heal Exit', 'Add Exit', 'Buy', 'Memorize', 'Learn']
PLAIN = [ord(c) for c in 'NPEXAZCHTML01234567898 ,.\\'] + [13, 13, 13]
EXT = [0x47, 0x4F, 0x49, 0x51, 0x48, 0x50, 0x4B, 0x4D, 0x52, 0x53, 0x3B]


def gen_case(rng):
    rows = rng.randint(1, 12)
    count = rng.randint(1, 30) if rng.random() < 0.97 else 0
    nl = rng.choice([0, 0, 1, 2, rng.randint(0, max(0, count))])
    labels = set(rng.sample(range(count), min(nl, count))) if count else set()
    if count and rng.random() < 0.4:
        labels |= set(range(rng.randint(1, min(3, count))))
    if labels and len(labels) == count:
        labels.discard(count - 1)
    width = rng.randint(6, 38)
    x0 = rng.randint(1, 39 - width)
    y0 = rng.randint(1, 23 - rows)
    texts = []
    for i in range(count):
        n = rng.randint(0, min(width + 2, 30))
        t = ''.join(rng.choice('ABCDEFGHIJKLMNOPQRSTUVWXYZ abcdef') for _ in range(n))
        if rng.random() < 0.2:
            t = ' ' * rng.randint(1, 3) + t
        if rng.random() < 0.2:
            t = t + ' ' * rng.randint(1, 3)
        texts.append(t)
    keys = []
    for _ in range(rng.randint(0, 8)):
        if rng.random() < 0.45:
            keys += [0, rng.choice(EXT)]
        else:
            keys.append(rng.choice(PLAIN))
    keys.append(0x1B)
    top = rng.randint(0, max(0, count + 1)) if rng.random() < 0.7 else rng.randint(0, 3)
    position = rng.randint(0, max(0, count - 1)) if rng.random() < 0.9 else rng.randint(0, count + 2)
    return {'rows': texts, 'labels': sorted(labels), 'window': [x0, y0, x0 + width - 1, y0 + rows - 1],
            'colours': [13, 10, 15] if rng.random() < 0.8 else [11, 10, 15], 'exit': rng.choice([0, 1, 1]),
            'redraw': rng.choice([0, 1]), 'position': position, 'top': top, 'cell': rng.randint(0, 4),
            'prompt': rng.choice(['', '', 'Items: ', 'Choose: ']), 'word': rng.choice(WORDS), 'keys': keys}


def main(argv):
    if len(argv) != 3:
        print(__doc__, file=sys.stderr)
        return 2
    o = ListWindowOracle(argv[1])
    rng = random.Random(6060)
    cases = []
    for _ in range(1500):
        c = gen_case(rng)
        cases.append(o.run(c))
    with open(argv[2], 'w') as f:
        json.dump({'cases': cases}, f, separators=(',', ':'))
    print('%d cases; keys left over in %d; %d asked for an entry outside the list' %
          (len(cases), sum(1 for c in cases if c['keysLeft']), sum(1 for c in cases if c['oob'])))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
