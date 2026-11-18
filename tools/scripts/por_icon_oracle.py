#!/usr/bin/env python3
"""The icon editor (GAME.OVR 0x1D1AC, Create's last step) run by the program itself (por_emu.py, unicorn), for
GoldBox.Core's IconEditorProgramTests (docs/re/start-menu.md §3).

Usage: python3 -I por_icon_oracle.py GAMEDIR OUT.json        (GAMEDIR: the PoR v1.3 files; needs unicorn 2.x)

Each case puts a record with random icon bytes (head 0xBD, weapon 0xBE, size 0xC0, colours 0xC1..0xC6) at DS:5D92 and
answers the editor's menus (the horizontal menu 0x2CC8C) with a random walk of keys the shown line offers (its
hotkeys, or 0 for Escape), read from the routine's state byte [bp-8]; after a number of keys it walks out (Keep or
Exit in the sub-menus, Exit on the main menu) and answers the IconOk question (0x2D9A0) No a few times, then Yes. The
keys, the answers and the icon bytes the routine leaves in the record are written out.

Drawing, the icon picture builders (0x1E468, 0x1CF3D, 0x1CE5E) and the palette calls are trapped; the menu logic and
the run-time library calls (strings, sets, Move) run. Nothing from the game is stored by this script; the output holds
only bytes it computed.
"""
import json
import os
import random
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from por_emu import Emu  # noqa: E402
from por_chargen_oracle import find, retf_bytes  # noqa: E402

from unicorn.x86_const import UC_X86_REG_BP  # noqa: E402

REC = 0x11D
ICON = 0x1D1AC
MENU = 0x2CC8C
YES_NO = 0x2D9A0
STUB_OVR = [0x1E468, 0x1CF3D, 0x1CE5E, 0x3081D, 0x3083F, 0x2CFCA, 0x31BBA, 0x31C34]
STUB_START = [(0x03F1, 0x02E8), (0x0709, 0x0626)]
CUR_CHAR = 0x5D92
FIRST = 0xBD

# The hotkeys of each menu line (DS:07BF + 41·state): the routine only ever gets these, or 0 (Escape).
KEYS = {1: 'P12SE', 2: 'HWE', 5: 'NPKE'}


class Oracle:
    def __init__(self, gamedir):
        self.e = e = Emu(open(find(gamedir, 'START.EXE'), 'rb').read(), open(find(gamedir, 'GAME.OVR'), 'rb').read())
        for o in STUB_OVR:
            e.trap_ovr(o, Emu.ret(retf_bytes(e.O, o)))
        for seg, off in STUB_START:
            e.trap_start(seg, off, Emu.ret(retf_bytes(e.S, seg * 16 + off)))
        e.trap_ovr(MENU, self._menu)
        e.trap_ovr(YES_NO, self._yes_no)
        self.menu_ret = retf_bytes(e.O, MENU)
        self.yes_no_ret = retf_bytes(e.O, YES_NO)

    def _menu(self, e):
        bp = e.uc.reg_read(UC_X86_REG_BP)
        frame = 0x2000 * 16 + bp  # the editor's frame (SS = the emulator's stack segment)
        state = e.rb(frame - 8)[0]
        high = e.rb(frame - 0x17)[0]
        key = self.choose(state, high)
        self.keys.append(key)
        # The last pushed argument is the var "extended" byte: not extended.
        off, seg = struct.unpack('<HH', e.rb(e.stack() + 4, 4))
        e.wb(seg * 16 + off, b'\x00')
        e.pop_ret(self.menu_ret, ord(key) if key != '0' else 0)

    def choose(self, state, high):
        size = self.e.rb(self.rec + 0xC0)[0]
        if state == 3:
            keys = 'WB' + ('F' if high else 'H') + 'SALE'
        elif state == 4:
            keys = ('S' if size == 2 else 'L') + 'KE'
        else:
            keys = KEYS[state]
        if self.budget > 0:
            self.budget -= 1
            if self.rng.random() < 0.08:
                return '0'
            k = self.rng.choice(keys)
            # Leave the main menu only at the end of the walk.
            return self.rng.choice('P12S') if state == 1 and k == 'E' else k
        if state == 5:
            return self.rng.choice('KE0')
        return self.rng.choice('E0')

    def _yes_no(self, e):
        yes = self.noes == 0
        self.answers.append('Y' if yes else 'N')
        if not yes:
            self.noes -= 1
            self.budget = self.rng.randrange(0, 30)
        e.pop_ret(self.yes_no_ret, ord('Y') if yes else ord('N'))

    def run(self, rng, icon, steps, noes):
        e = self.e
        e.reset_heap()
        seg, off = e.alloc(REC)
        self.rec = seg * 16 + off
        e.wb(self.rec + FIRST, icon)
        e.set_dsptr(CUR_CHAR, (seg, off))
        e.set_dsb(0x49FA, 1)
        self.rng = rng
        self.budget = steps
        self.noes = noes
        self.keys = []
        self.answers = []
        e.call(ICON)
        return {'icon': bytes(icon).hex(), 'keys': ''.join(self.keys), 'answers': ''.join(self.answers),
                'after': e.rb(self.rec + FIRST, 10).hex()}


def main():
    gamedir, out = sys.argv[1], sys.argv[2]
    o = Oracle(gamedir)
    rng = random.Random(1997)
    cases = []
    for n in range(600):
        icon = [rng.randrange(14), rng.randrange(32), 0, rng.choice((1, 2))] + [rng.randrange(256) for _ in range(6)]
        if n % 7 == 0:
            icon[4:] = [0] * 6  # zero colour bytes: the nibbles wrap below 0
        steps = rng.choice((0, 1, 3, 8, 20, 60))
        cases.append(o.run(rng, bytes(icon), steps, rng.choice((0, 0, 1, 2))))
    with open(out, 'w') as f:
        json.dump({'cases': cases}, f, indent=0)
    print('%d cases, %d keys' % (len(cases), sum(len(c['keys']) for c in cases)))


if __name__ == '__main__':
    main()
