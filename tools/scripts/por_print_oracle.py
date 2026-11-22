#!/usr/bin/env python3
"""The text routine START 0x77F8 (PRINT / PRINTCLR, venue and message texts) from the program itself: runs it under
unicorn (por_emu.py) on generated strings, boxes and cursors and writes every draw it makes, for GoldBox.Client's
PrintProgramTests (docs/re/view3d.md "Text window").

Usage: python3 -I por_print_oracle.py GAMEDIR OUT.json        (GAMEDIR: the PoR v1.3 files; needs unicorn 2.x)

Per case: the box (x0, y0, x1, y1), colour, clear flag, the cursor DS:5E24 / DS:5E25 and the typing flag DS:4A00
before, the string (a Pascal string, 0-255 characters), then the events in order: the glyph routine START 0x726F
(trapped: column, row, character, colour, upper-case flag), the box clear START 0x7E63 (trapped), the page prompt's key
wait START 0x7C85 (trapped, answered at once) and Delay START 0xA69C (trapped); the keyboard flush 0899:0104 (START
0x8A94) runs as it is. Then the cursor after, or 'hung' when the routine had drawn the string's length + 600 glyphs
without returning (a 255-character string: the 0x7737 loop's byte index wraps) or after 20 million instructions (a
box with x1 < x0 and a one-character last word: the wrap branch counts the row on for ever and draws nothing).
Also: 'decode', the ECL text unpacker GAME.OVR 0x8626 on texts of 200-340 codes (the slot's length byte after it).
The strings are made up here (letters, digits, spaces, the word-cut delimiters, a few other bytes); nothing from the game
is stored.
"""
import json
import os
import random
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from por_emu import Emu  # noqa: E402

PRINT = (0x709, 0x768)            # START 0x77F8
GLYPH = 0x726F                    # linear (0709:01DF)
CLEARBOX = 0x7E63
PAGEKEY = 0x7C85
DELAY = 0xA69C
DECODE = 0x8626                   # GAME.OVR
LIMIT_EXTRA = 600


class Stop(Exception):
    pass


def find(dirpath, name):
    for f in os.listdir(dirpath):
        if f.upper() == name:
            return os.path.join(dirpath, f)
    raise FileNotFoundError(name)


class PrintOracle:
    def __init__(self, gamedir):
        with open(find(gamedir, 'START.EXE'), 'rb') as f:
            exe = f.read()
        with open(find(gamedir, 'GAME.OVR'), 'rb') as f:
            ovr = f.read()
        self.e = e = Emu(exe, ovr)
        self.events = []
        self.glyphs = 0
        self.glyph_limit = 0
        e._trap(GLYPH, self._glyph)
        e._trap(CLEARBOX, self._clear)
        e._trap(PAGEKEY, self._page)
        e._trap(DELAY, self._delay)
        self.buf = e.alloc(0x200)
        self.script = e.alloc(0x6700 + 0x300)

    def _glyph(self, e):
        # 0x726F(column, row, colour, count, character, upcase): Pascal order, so the last argument is nearest.
        flag, ch, _count, colour, row, col = (e.arg(2 * k) & 0xFF for k in range(6))
        self.events.append(['g', col, row, ch, colour, flag])
        self.glyphs += 1
        if self.glyphs >= self.glyph_limit:
            e.uc.emu_stop()
            self.hung = True
            return
        e.pop_ret(12)

    def _clear(self, e):
        y1, x1, y0, x0 = (e.arg(2 * k) & 0xFF for k in range(4))
        self.events.append(['c', x0, y0, x1, y1])
        e.pop_ret(8)

    def _page(self, e):
        colour = e.arg(4) & 0xFF
        self.events.append(['p', colour])
        e.pop_ret(6)

    def _delay(self, e):
        self.events.append(['d', e.arg(0)])
        e.pop_ret(2)

    def run(self, box, colour, clear, cursor, typing, s):
        e = self.e
        seg, off = self.buf
        e.wb(seg * 16 + off, bytes([len(s)]) + s)
        e.set_dsb(0x5E24, cursor[0])
        e.set_dsb(0x5E25, cursor[1])
        e.set_dsb(0x4A00, typing)
        e.set_dsb(0x49E2, 3)
        self.events = []
        self.glyphs = 0
        self.glyph_limit = len(s) + LIMIT_EXTRA
        self.hung = False
        x0, y0, x1, y1 = box
        try:
            e.call(PRINT, (x0, y0, x1, y1, colour, clear, seg, off), limit=20_000_000)
        except RuntimeError:
            # The glyph limit, or the instruction limit: a loop that draws nothing (a box with x1 < x0 whose last
            # word is one character: the row counts on for ever).
            self.hung = True
        return {
            'box': list(box), 'colour': colour, 'clear': clear, 'cursor': list(cursor), 'typing': typing,
            'text': s.hex(), 'events': self.events, 'hung': self.hung,
            'after': None if self.hung else [e.dsb(0x5E24), e.dsb(0x5E25)],
        }

    def decode(self, codes, slot=1):
        """GAME.OVR 0x8626 on the packed codes: the string slot's length byte and text after it."""
        e = self.e
        bits = ''.join(format(c, '06b') for c in codes)
        bits += '0' * (-len(bits) % 8)
        packed = bytes(int(bits[i:i + 8], 2) for i in range(0, len(bits), 8))
        assert len(packed) <= 255
        seg, _ = self.script
        pc = 0x100
        e.set_dsptr(0x49DE, (seg, 0))
        e.set_dsw(0x49ED, pc)
        # The unpacker increments the PC, then reads es:[ptr + 0x6700 + PC].
        e.wb(seg * 16 + 0x6700 + pc + 1, packed)
        e.call(DECODE, (len(packed), slot))
        at = e.ds(0x6FC8 + 256 * slot)
        n = e.rb(at)[0]
        return packed, n, e.rb(at + 1, n)


WORDCHARS = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789'
DELIMS = '!,-.:;?'


def make_text(rng, n):
    out = []
    while len(out) < n:
        r = rng.random()
        if r < 0.6:
            out.extend(rng.choice(WORDCHARS) for _ in range(rng.randint(1, 12)))
        elif r < 0.8:
            out.extend(' ' * rng.randint(1, 3))
        elif r < 0.95:
            out.extend(rng.choice(DELIMS) for _ in range(rng.randint(1, 3)))
        else:
            out.append(chr(rng.choice([0x40, 0x5F, 0x60, 0x7B, 0x7E, 0x80, 0xE9, 0xFF, 0x01])))
    return ''.join(out[:n]).encode('latin-1')


def main(argv):
    if len(argv) != 3:
        print(__doc__, file=sys.stderr)
        return 2
    o = PrintOracle(argv[1])
    rng = random.Random(4777)
    cases = []
    boxes = [(1, 17, 38, 22), (23, 1, 38, 21), (1, 21, 38, 22), (2, 5, 37, 22), (1, 1, 38, 22)]
    for k in range(400):
        if k % 10 == 0:
            box = tuple(rng.randint(0, 45) for _ in range(4))
        else:
            box = rng.choice(boxes)
        x0, y0, x1, y1 = box
        cursor = (rng.randint(x0, max(x0, x1) + 1), rng.randint(y0, max(y0, y1) + 1)) if rng.random() < 0.8 else \
            (rng.randint(0, 60), rng.randint(0, 30))
        cursor = (cursor[0] & 0xFF, cursor[1] & 0xFF)
        n = rng.choice([0, 1, 2, rng.randint(1, 40), rng.randint(40, 254), rng.randint(200, 255), 255, 254])
        cases.append(o.run(box, rng.choice([10, 13, 14, 15]), rng.randint(0, 1), cursor, rng.randint(0, 1),
                           make_text(rng, n)))
    # Fixed cases: a one-character last word after a full box (drawn on the row below), a 255-character text.
    cases.append(o.run((1, 17, 38, 22), 10, 1, (1, 17), 1, (b'A' * 37 + b' ') * 6 + b'X'))
    cases.append(o.run((1, 17, 38, 22), 10, 1, (1, 17), 1, (b'LONG TEST TEXT FOR THE PRINT ' * 9)[:255]))
    cases.append(o.run((1, 17, 38, 22), 10, 1, (1, 17), 1, (b'LONG TEST TEXT FOR THE PRINT ' * 9)[:254]))
    decode = []
    for n in (200, 254, 255, 256, 300, 340):
        codes = [rng.randint(1, 63) for _ in range(n)]
        packed, length, text = o.decode(codes)
        decode.append({'codes': n, 'packed': packed.hex(), 'length': length, 'text': text.hex()})
    hung = sum(c['hung'] for c in cases)
    with open(argv[2], 'w') as f:
        json.dump({'cases': cases, 'decode': decode}, f, separators=(',', ':'))
    print('%d cases (%d hung), decode lengths %s -> %s' % (len(cases), hung, [d['codes'] for d in decode],
                                                         [d['length'] for d in decode]))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
