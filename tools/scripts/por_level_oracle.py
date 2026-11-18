#!/usr/bin/env python3
"""The level-stats routine GAME.OVR 0x2AA87 and the race level limit START 0x3AF7 (00BA:2F57) run by the program
itself (por_emu.py, unicorn) on generated creatures, for GoldBox.Core's LevelStatsProgramTests
(docs/re/spells-effects.md "Level stats").

Usage: python3 -I por_level_oracle.py GAMEDIR OUT.json        (GAMEDIR: the PoR v1.3 files; needs unicorn 2.x)

Each case is a creature record of made-up bytes (levels, race, Strength, Wisdom, Dexterity, slots and the bytes the
routine writes filled at random) with the record after 0x2AA87, and the answer of START 0x3AF7 for each class. Neither
routine calls anything outside. The output holds no game data but is kept with the private fixtures.
"""
import json
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from por_emu import Emu  # noqa: E402

REC = 0x11D
LEVEL_STATS = 0x2AA87
RACE_LIMIT = (0x00BA, 0x2F57)


def find(dirpath, name):
    for f in os.listdir(dirpath):
        if f.upper() == name:
            return os.path.join(dirpath, f)
    raise FileNotFoundError(name)


def creature(rng):
    rec = bytearray(rng.randrange(256) for _ in range(REC))
    for cls in range(8):
        rec[0x96 + cls] = rng.choice([0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 0xFF])
    rec[0x2E] = rng.randrange(8)
    rec[0x10] = rng.randrange(3, 19)
    rec[0x12] = rng.randrange(3, 19)
    rec[0x13] = rng.randrange(3, 19)
    rec[0x73] = rng.randrange(0, 12)
    for i in range(0xB2, 0xB8):
        rec[i] = rng.choice([0, 0, 1, 2, 3])
    return bytes(rec)


def main():
    gamedir, out = sys.argv[1], sys.argv[2]
    e = Emu(open(find(gamedir, 'START.EXE'), 'rb').read(), open(find(gamedir, 'GAME.OVR'), 'rb').read())
    rng = random.Random(15)
    cases = []
    for _ in range(400):
        rec = creature(rng)
        e.reset_heap()
        seg, off = e.alloc(REC)
        lin = seg * 16 + off
        limits = []
        for cls in range(8):
            e.wb(lin, rec)
            ax, _ = e.call(RACE_LIMIT, (seg, off, cls))
            limits.append(ax & 0xFF)
        e.wb(lin, rec)
        e.call(LEVEL_STATS, (seg, off))
        cases.append({'before': rec.hex(), 'after': e.rb(lin, REC).hex(), 'limits': limits})
    with open(out, 'w') as f:
        json.dump({'cases': cases}, f, indent=0)
    print('%d cases' % len(cases))


if __name__ == '__main__':
    main()
