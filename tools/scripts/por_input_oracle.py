#!/usr/bin/env python3
"""DOS's line inputs (START 0x7A73, INPUTSTR and names; START 0x7BF6, INPUTNUM) from the program itself: runs them under
unicorn (por_emu.py) on generated key sequences and writes what they return, for GoldBox.Client's
LineInputProgramTests (ecl-format.md "The ECL line inputs").

Usage: python3 -I por_input_oracle.py GAMEDIR OUT.json        (GAMEDIR: the PoR v1.3 files; needs unicorn 2.x)

The key read START 0x89E9 is trapped and answered from the case's keys (bytes as ReadKey returns them); the drawing
runs as it is (the glyph routine draws nothing with no font loaded). Per case: the routine ('str' with its maximum
length, or 'num'), the keys, then what it returned (the string, or the number) and how many keys it read; 'starved'
when it asked for more keys than the case holds (INPUTNUM asks again after a line Val rejects: the case then holds
the lines it is given in turn). Nothing from the game is stored.
"""
import json
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from por_emu import Emu  # noqa: E402

LINE = (0x709, 0x9E3)       # START 0x7A73
NUMBER = (0x709, 0xB66)     # START 0x7BF6
GETKEY = 0x89E9


def find(dirpath, name):
    for f in os.listdir(dirpath):
        if f.upper() == name:
            return os.path.join(dirpath, f)
    raise FileNotFoundError(name)


class InputOracle:
    def __init__(self, gamedir):
        with open(find(gamedir, 'START.EXE'), 'rb') as f:
            exe = f.read()
        with open(find(gamedir, 'GAME.OVR'), 'rb') as f:
            ovr = f.read()
        self.e = e = Emu(exe, ovr)
        e._trap(GETKEY, self._key)
        self.keys = []
        self.read = 0
        self.starved = False
        self.out = e.alloc(0x100)
        self.prompt = e.alloc(0x100)

    def _key(self, e):
        if self.read >= len(self.keys):
            self.starved = True
            e.uc.emu_stop()
            return
        k = self.keys[self.read]
        self.read += 1
        e.pop_ret(0, ax=k)

    def run(self, kind, keys, maxlen=40, prompt=b''):
        e = self.e
        self.keys = list(keys)
        self.read = 0
        self.starved = False
        ps, po = self.prompt
        e.wb(ps * 16 + po, bytes([len(prompt)]) + prompt)
        os_, oo = self.out
        e.wb(os_ * 16 + oo, bytes(0x100))
        res = {'kind': kind, 'keys': list(keys), 'max': maxlen}
        try:
            if kind == 'str':
                # 0x7A73(var result: string[40]; prompt: string; colour, max: byte): Pascal order, the result first.
                e.call(LINE, (os_, oo, ps, po, 10, maxlen), limit=5_000_000)
                n = e.rb(os_ * 16 + oo)[0]
                res['text'] = e.rb(os_ * 16 + oo + 1, n).hex()
            else:
                # 0x7BF6(prompt: string; colour: byte): word
                ax, _ = e.call(NUMBER, (ps, po, 10), limit=5_000_000)
                res['value'] = ax
        except RuntimeError:
            res['starved'] = True
            return res
        res['read'] = self.read
        res['starved'] = self.starved
        return res


def main(argv):
    if len(argv) != 3:
        print(__doc__, file=sys.stderr)
        return 2
    o = InputOracle(argv[1])
    rng = random.Random(7273)
    cases = []
    ends = [13, 27]
    for k in range(600):
        keys = []
        for _ in range(rng.randint(0, 50)):
            r = rng.random()
            keys.append(8 if r < 0.12 else rng.randint(0, 255) if r < 0.3 else rng.randint(0x20, 0x7A))
        keys = [x for x in keys if x not in ends]
        keys.append(rng.choice(ends))
        cases.append(o.run('str', keys, rng.choice([40, 40, 6, 1, 15, rng.randint(0, 45)])))
    # INPUTNUM: lines of digits (and some other characters), each ended by Return or Escape, until one is accepted.
    def line(rng):
        r = rng.random()
        if r < 0.15:
            s = ''
        elif r < 0.7:
            s = str(rng.choice([0, 1, 9, 255, 256, 999, 32767, 32768, 65535, 65536, 65537, 99999, 100000, 999999,
                                rng.randint(0, 70000)]))
        elif r < 0.85:
            s = rng.choice(['-1', '-0', '+5', ' 12', '12 ', '1A', 'A', '$FF', '0x10', '00012', '-65535', '$10000'])
        else:
            s = ''.join(chr(rng.randint(0x20, 0x7A)) for _ in range(rng.randint(1, 8)))
        keys = list(s.encode('latin-1'))
        if rng.random() < 0.1 and keys:
            keys.insert(rng.randrange(len(keys) + 1), 8)
        return keys + [rng.choice(ends)]
    for k in range(400):
        keys = []
        for _ in range(4):
            keys += line(rng)
        keys += list(b'7') + [13]
        cases.append(o.run('num', keys))
    starved = sum(c['starved'] for c in cases)
    with open(argv[2], 'w') as f:
        json.dump({'cases': cases}, f, separators=(',', ':'))
    print('%d cases (%d starved)' % (len(cases), starved))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
