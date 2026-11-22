#!/usr/bin/env python3
"""The fight's Alt-X debugging switch 0x1638E run by the program itself: the v1.3 routine (por_emu.py, unicorn) on
generated fights, for GoldBox.Core's AltXProgramTests (docs/re/combat-menu.md "Entries", Alt-X).

Usage: python3 -I por_altx_oracle.py GAMEDIR OUT.json        (GAMEDIR: the PoR v1.3 files; needs unicorn 2.x)

Each case: a creature list (head DS:5D96, links +0x104) with made-up combat records (+0x108), placed on an arena in
slots (DS:5F27 + 4k, pointers DS:65B9 + 4k, slot count DS:5F2A, the grid DS:60DB built by 0x30ADA), some hostile
creatures left without a slot; then 0x1638E with ParamStr(1) (START 0xC63C, trapped) answering the program's own word
(read from GAME.OVR 0x16374 at run time, never stored) or a variant of it (lower case, one character more or less,
empty, another word). START 0x31D7 (actions end), 0x31399 (the slot) and the string compare START 0xBB90 run for real;
the row-24 message START 0x2233 and the view scroll 0x310D0 are trapped and logged. Out: the result (AL), the records,
combat records, slot sizes, the slot count, the grid, the message count and 0x310D0's arguments.
The records are made up here; the output goes in tests/fixtures-private.
"""
import json
import os
import random
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from por_emu import Emu  # noqa: E402
import por_spell_oracle as so  # noqa: E402

ALT_X, WORD_AT = 0x1638E, 0x16374
PARAMSTR, MESSAGE, SCROLL, BUILD_CELLS = 0xC63C, 0x2233, 0x310D0, 0x30ADA
COMBAT = 0x16
VARIANTS = ['word', 'lower', 'longer', 'shorter', 'empty', 'other']


class AltXOracle(so.Oracle):
    def __init__(self, gamedir):
        super().__init__(gamedir)
        e = self.e
        with open(so.find(gamedir, 'GAME.OVR'), 'rb') as f:
            ovr = f.read()
        self.word = ovr[WORD_AT + 1:WORD_AT + 1 + ovr[WORD_AT]]
        self.param = b''
        self.messages = 0
        self.scrolls = []
        e.trap_start(0, PARAMSTR, self._paramstr)
        # the base class already hooks these two (a second hook would run the trap twice): replace its handlers
        e.traps[MESSAGE] = self._message
        e.traps[e.unit_linear(SCROLL)[0]] = self._scroll

    def _paramstr(self, e):
        # ParamStr(index): the caller pushed the result pointer, then the index; the pointer stays for the caller
        off, seg = e.arg(2), e.arg(4)
        e.wb(seg * 16 + off, bytes([len(self.param)]) + self.param)
        e.pop_ret(2)

    def _message(self, e):
        self.messages += 1
        e.pop_ret(4)

    def _scroll(self, e):
        # ScrollView(x, y, delta, dir): Pascal pushes x first, so it is the deepest word
        self.scrolls.append([e.arg(6), e.arg(4), e.arg(2) & 0xFF, e.arg(0) & 0xFF])
        e.pop_ret(8)

    def variant(self, v):
        w = self.word
        return {'word': w, 'lower': w.lower(), 'longer': w + b'X', 'shorter': w[:-1], 'empty': b'',
                'other': b'NOTIT'}[v]

    def run_case(self, case):
        e = self.e
        ptrs = self.build([{'record': c, 'effects': [], 'items': []} for c in case['creatures']])
        for p, cb in zip(ptrs, case['combat']):
            o, s = struct.unpack('<HH', e.rb(self.lin(p) + 0x108, 4))
            e.wb(s * 16 + o, bytes.fromhex(cb))
        arena = e.alloc(0x4E9)
        e.set_dsptr(0x6716, arena)
        al = self.lin(arena)
        e.wb(al + 2, [case['view'][0] & 0xFF, case['view'][1] & 0xFF])
        e.wb(al + 7, bytes([23]) * 1250)
        k = 0
        slots = []
        for p, pl in zip(ptrs, case['places']):
            if pl is None:
                slots.append(0)
                continue
            k += 1
            x, y, size = pl
            e.wb(e.ds(0x5F27 + 4 * k), [x & 0xFF, y & 0xFF, k, size])
            e.wb(e.ds(0x65B9 + 4 * k), struct.pack('<HH', p[1], p[0]))
            slots.append(k)
        e.wb(e.ds(0x5F27 + 4 * (k + 1)), [0, 0, 0, 0])
        e.wb(e.ds(0x65B9 + 4 * (k + 1)), b'\0\0\0\0')
        e.set_dsb(0x5F2A, k + 1)
        e.set_dsptr(0x6DBB, (0, 0))
        e.set_dsb(0x6715, 0)
        e.call(BUILD_CELLS)
        self.param = self.variant(case['param'])
        self.messages = 0
        self.scrolls = []
        e.call(ALT_X)
        res = e.uc.reg_read(so.UC_X86_REG_AX) & 0xFF
        combats = []
        for p in ptrs:
            o, s = struct.unpack('<HH', e.rb(self.lin(p) + 0x108, 4))
            combats.append(e.rb(s * 16 + o, COMBAT).hex())
        return {'result': res, 'after': [e.rb(self.lin(p), so.REC).hex() for p in ptrs], 'combat': combats,
                'sizes': [e.dsb(0x5F27 + 4 * j + 3) for j in range(1, k + 1)], 'slotCount': e.dsb(0x5F2A),
                'cells': e.rb(e.ds(0x60DB), 1250).hex(), 'messages': self.messages, 'scrolls': self.scrolls}


def make_cases(rnd):
    cases = []
    for n in range(48):
        count = rnd.randrange(1, 10)
        creatures, combat, places, used = [], [], [], set()
        for i in range(count):
            hostile = rnd.choice([0, 1, 1, 2]) if i else rnd.choice([0, 1])
            c = so.creature(rnd, hostile=hostile, status=rnd.choice([0, 0, 0, 1, 4, 5, 6]),
                            inplay=rnd.choice([1, 1, 1, 0]))
            creatures.append(c.hex())
            cb = bytearray(rnd.randrange(256) for _ in range(COMBAT))
            cb[0x13] = rnd.choice([0, 1])  # not a party member
            combat.append(bytes(cb).hex())
            # a slot for most; the cases from 40 on leave a hostile creature off the slots (0x31399 gives 0)
            if n >= 40 and hostile == 1 and rnd.random() < 0.5:
                places.append(None)
                continue
            while True:
                x, y = rnd.randrange(2, 46), rnd.randrange(2, 22)
                if (x, y) not in used:
                    break
            used.add((x, y))
            places.append([x, y, rnd.choice([1, 1, 2])])
        param = 'word' if n % 3 != 2 else VARIANTS[1 + (n // 3) % (len(VARIANTS) - 1)]
        cases.append({'creatures': creatures, 'combat': combat, 'places': places,
                      'view': [rnd.randrange(0, 44), rnd.randrange(0, 19)], 'param': param})
    return cases


def main(argv):
    if len(argv) != 3:
        print(__doc__)
        return 2
    o = AltXOracle(argv[1])
    rnd = random.Random(20261122)
    cases = make_cases(rnd)
    for c in cases:
        c['out'] = o.run_case(c)
    out = {'source': 'GAME.OVR / START.EXE v1.3 run under unicorn (tools/scripts/por_altx_oracle.py)', 'cases': cases}
    with open(argv[2], 'w') as f:
        json.dump(out, f, indent=0)
    print('%d cases, %d with the word accepted' % (len(cases), sum(c['out']['result'] for c in cases)))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
