#!/usr/bin/env python3
"""Modify Character (GAME.OVR 0x1BA1E, the party menu's and the training hall's Modify) run by the program itself
(por_emu.py, unicorn), for GoldBox.Core's CharacterModifyProgramTests (docs/re/start-menu.md "Modify").

Usage: python3 -I por_modify_oracle.py GAMEDIR OUT.json        (GAMEDIR: the PoR v1.3 files; needs unicorn 2.x)

Two kinds of case:
- "created": a character made by Create (0x19EEB, run as por_chargen_oracle.py runs it: seed, picks, re-rolls) with
  its race effects in memory, then some bytes overwritten ("sets": experience, strength, constitution, hit points),
  then Modify on it as the current character (DS:5D92).
- "made": a record of made-up bytes (race, gender, class, levels, abilities, hit points, experience) with its far
  pointers zeroed (no items, no effects), then Modify on it. For these the HP limits 0x1D932 and 0x1DA0B are also
  called directly on the record before Modify.

The routine's word menu (0x2CC8C) is trapped and answers a scripted list of keys (key, extended byte): cursor keys,
keys it ignores, then K, E or Escape (an exhausted script answers Escape). The message routine START 0x7DF8 (the
refusal) and drawing are trapped; the rest runs (the HP limits, the Wisdom bonus 0x2AC36, the stat recalculation
START 0x1758, the page). The output holds no game text: only records and the keys.
"""
import json
import os
import random
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from por_emu import Emu  # noqa: E402
from por_chargen_oracle import Oracle, REC, retf_bytes  # noqa: E402

MODIFY = 0x1BA1E
MENU = 0x2CC8C
HP_MIN = 0x1D932
HP_MAX = 0x1DA0B
MESSAGE = (0x0709, 0x0D68)   # START 0x7DF8
CUR_CHAR = 0x5D92

DOWN, UP, LEFT, RIGHT = (0x50, 1), (0x48, 1), (0x4B, 1), (0x4D, 1)
EXT_CR = (0x0D, 1)
IGNORED = [(0x0D, 0), (0x20, 0), (0x49, 1), (0x47, 1), (0x41, 0)]
KEEP, EXIT, ESC = (0x4B, 0), (0x45, 0), (0x00, 0)
POINTERS = list(range(0x7F, 0x83)) + list(range(0xC8, 0x100)) + list(range(0x104, 0x10C))


class ModifyOracle(Oracle):
    def __init__(self, gamedir):
        super().__init__(gamedir)
        e = self.e
        self.keys = []
        self.menus = 0
        self.refused = False
        self.record_at = None
        e.trap_ovr(MENU, self._word_menu)
        e.trap_start(*MESSAGE, self._message)
        self.word_ret = retf_bytes(e.O, MENU)
        self.msg_ret = retf_bytes(e.S, MESSAGE[0] * 16 + MESSAGE[1])

    def _save(self, e):
        off, seg = struct.unpack('<HH', e.rb(e.stack() + 4 + self.save_ret - 4, 4))
        self.record_at = (seg, off)
        super()._save(e)

    def _word_menu(self, e):
        # The last pushed argument is the var "extended" byte (a far pointer).
        off, seg = struct.unpack('<HH', e.rb(e.stack() + 4, 4))
        key, ext = self.keys.pop(0) if self.keys else ESC
        self.menus += 1
        e.wb(seg * 16 + off, bytes([ext]))
        e.pop_ret(self.word_ret, key)

    def _message(self, e):
        self.refused = True
        e.pop_ret(self.msg_ret)

    def modify(self, seg, off, keys):
        e = self.e
        e.set_dsptr(CUR_CHAR, (seg, off))
        self.keys = list(keys)
        self.menus = 0
        self.refused = False
        e.call(MODIFY)
        return e.rb(seg * 16 + off, REC)


def walk(rng):
    """A key script: field moves and runs of Left / Right that go past the limits, ignored keys, then an end key."""
    keys = []
    for _ in range(rng.randrange(1, 7)):
        for _ in range(rng.randrange(0, 4)):
            keys.append(rng.choice([DOWN, DOWN, UP, EXT_CR]))
        n = rng.choice([1, 1, 2, 3, 5, 12, 25])
        keys += [rng.choice([LEFT, RIGHT])] * n
        if rng.random() < 0.3:
            keys.append(rng.choice([LEFT, RIGHT]))
        if rng.random() < 0.15:
            keys.append(rng.choice(IGNORED))
    end = rng.choice([KEEP, KEEP, EXIT, ESC, None])
    if end is not None:
        keys.append(end)
    return keys


def made_record(rng):
    rec = bytearray(rng.randrange(256) for _ in range(REC))
    for i in POINTERS:
        rec[i] = 0
    rec[0x2E] = rng.randrange(0, 8)
    rec[0x9E] = rng.randrange(2)
    for k in range(8):
        rec[0x96 + k] = 0
    combos = [[2], [0], [5], [6], [3], [4], [1], [7], [0, 2], [2, 5], [2, 6], [5, 6], [0, 2, 5], [0, 5], [0, 6],
              [2, 5, 6], [0, 4], [4, 7], [3, 4, 7]]
    combo = rng.choice(combos)
    for cls in combo:
        rec[0x96 + cls] = rng.choice([1, 1, 1, 2, 3, 5, 8, 12, 20, 30, 40])
    # A negative level byte on a class outside the combination (no class at all divides by zero in the program).
    if rng.random() < 0.1:
        rec[0x96 + rng.choice([k for k in range(8) if k not in combo])] = rng.randrange(0x80, 0x100)
    rec[0x2F] = rng.randrange(0, 17)
    for a in range(0x10, 0x16):
        rec[a] = rng.choice([rng.randrange(3, 19), rng.randrange(3, 19), rng.randrange(0, 26)])
    rec[0x14] = rng.choice([rec[0x14], 3, 4, 5, 6, 7, 15, 16, 17, 18, 19])
    if rng.random() < 0.4:
        rec[0x10] = 18
    rec[0x16] = rng.choice([0, 0, rng.randrange(1, 101), 99, 100])
    rec[0x32] = rng.choice([rng.randrange(1, 30), rng.randrange(0, 256), 1, 255, 0])
    rec[0x11B] = rng.randrange(0, rec[0x32] + 1)
    rec[0xB2] = rng.choice([0, 0, 1, rng.randrange(0, 5)])
    rec[0xB3] = rng.choice([0, 0, 1])
    rec[0xB4] = rng.choice([0, 0, 1])
    rec[0xAC:0xB0] = struct.pack('<I', rng.choice([0, 0, 0, 0, 1, 0x10000, 0x80000000, 0xFFFFFFFF, 0xFFFF0000,
                                                   rng.randrange(1 << 32)]))
    return bytes(rec)


def main():
    gamedir, out = sys.argv[1], sys.argv[2]
    o = ModifyOracle(gamedir)
    e = o.e
    rng = random.Random(23)

    def race_of(pick):
        return pick + 1 if pick == 6 else pick

    def classes(pick):
        r = race_of(pick)
        return [e.dsb(0x3E17 + 12 * r + k) for k in range(1, e.dsb(0x3E17 + 12 * r) + 1)]

    cases = []
    # Created characters: every race × class at least twice, then random picks.
    plan = []
    for race in range(1, 7):
        for c in range(1, len(classes(race)) + 1):
            for _ in range(3):
                plan.append((race, rng.randrange(1, 3), c))
    for _ in range(250):
        race = rng.randrange(1, 7)
        plan.append((race, rng.randrange(1, 3), rng.randrange(1, len(classes(race)) + 1)))
    for race, gender, cls in plan:
        code = classes(race)[cls - 1]
        aligns = e.dsb(0x3FE7 + 10 * code)
        picks = [race, gender, cls, rng.randrange(1, aligns + 1)]
        seed = rng.randrange(1 << 32)
        rerolls = rng.randrange(3)
        o.run(seed, picks, rerolls)
        seg, off = o.record_at
        lin = seg * 16 + off
        sets = []
        if rng.random() < 0.15:
            sets.append([0xAC, rng.choice([1, 0x10000, 0x80000000, 0xFFFFFFFF, 0xFFFF])])
        if rng.random() < 0.3:
            sets.append([0x10, 18])
            sets.append([0x16, rng.choice([0, 1, 50, 99, 100])])
        if rng.random() < 0.3:
            sets.append([0x14, rng.choice([3, 4, 5, 6, 7, 15, 16, 17, 18])])
        if rng.random() < 0.3:
            sets.append([0x11B, rng.randrange(0, 5)])
        for at, v in sets:
            n = 4 if at == 0xAC else 1
            e.wb(lin + at, struct.pack('<I', v)[:n])
        before = e.rb(lin, REC)
        keys = walk(rng)
        after = o.modify(seg, off, keys)
        cases.append({'kind': 'created', 'seed': seed, 'picks': picks, 'rerolls': rerolls, 'sets': sets,
                      'before': before.hex(), 'keys': [list(k) for k in keys], 'after': after.hex(),
                      'refused': o.refused, 'menus': o.menus})
    # Made-up records.
    for _ in range(700):
        e.reset_heap()
        rec = made_record(rng)
        seg, off = e.alloc(REC)
        e.wb(seg * 16 + off, rec)
        e.set_dsptr(CUR_CHAR, (seg, off))
        lo, _ = e.call(HP_MIN, (seg, off))
        hi, _ = e.call(HP_MAX, (seg, off))
        keys = walk(rng)
        after = o.modify(seg, off, keys)
        cases.append({'kind': 'made', 'before': rec.hex(), 'keys': [list(k) for k in keys], 'after': after.hex(),
                      'refused': o.refused, 'menus': o.menus, 'hpMin': lo & 0xFF, 'hpMax': hi & 0xFF})
    with open(out, 'w') as f:
        json.dump({'cases': cases}, f, indent=0)
    print('%d cases, %d refused, %d changed' % (len(cases), sum(c['refused'] for c in cases),
                                               sum(c['before'] != c['after'] for c in cases)))


if __name__ == '__main__':
    main()
