#!/usr/bin/env python3
"""Character creation (GAME.OVR 0x19EEB, the party menu's Create) run by the program itself (por_emu.py, unicorn), for
GoldBox.Core's CharacterCreationProgramTests (docs/re/start-menu.md).

Usage: python3 -I por_chargen_oracle.py GAMEDIR OUT.json        (GAMEDIR: the PoR v1.3 files; needs unicorn 2.x)

Each case starts the routine from a set RandSeed (DS:4428) and answers its menus: the race, gender, class and
alignment picks (1-based list positions), "No" to the keep question a number of times, then "Yes". The name prompt,
the portrait and icon editors are answered with nothing (they only take keys; the routine's own defaults stay). The
routine's last call saves the character (0x1E684); the trap there records the 285-byte record, the effect list the
race adds, and RandSeed. A cancelled pick (Esc) is not run: the routine then leaves without saving.

Drawing and key routines are trapped; everything else runs: the dice (0x2BCCD), the effect adds (0x2BD3C), the class
routines (0x2AA87 unit), the stat recalculation the stat display calls (START 0x1758), the menu list builders. Nothing
from the game is stored by this script; the output holds only records it computed.
"""
import json
import os
import random
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from por_emu import Emu  # noqa: E402

from capstone import Cs, CS_ARCH_X86, CS_MODE_16  # noqa: E402

REC = 0x11D
CREATE = 0x19EEB
MENU_VERTICAL = 0x2D669
YES_NO = 0x2D9A0
PORTRAIT = 0x1CD66
ICON = 0x1D1AC
SAVE_CHAR = 0x1E684
GETMEM = (0x0AF8, 0x023F)
FREEMEM = (0x0AF8, 0x0254)
READ_STRING = (0x0709, 0x09E3)
# Drawing / text output (START resident units 0709 and 03F1, and the portrait unit in GAME.OVR).
DRAW_START = [(0x0709, 0x0626), (0x0709, 0x0CAB), (0x03F1, 0x0000), (0x03F1, 0x0137), (0x03F1, 0x02E8)]
DRAW_OVR = [0x2E5C7]
CUR_CHAR = 0x5D92


def find(dirpath, name):
    for f in os.listdir(dirpath):
        if f.upper() == name:
            return os.path.join(dirpath, f)
    raise FileNotFoundError(name)


def retf_bytes(code, start):
    """The argument bytes of a Pascal far routine: the operand of its first retf."""
    md = Cs(CS_ARCH_X86, CS_MODE_16)
    for ins in md.disasm(code[start:start + 0x4000], start):
        if ins.mnemonic == 'retf':
            return int(ins.op_str, 16) if ins.op_str else 0
    raise ValueError('no retf after %#x' % start)


class Oracle:
    def __init__(self, gamedir):
        self.e = e = Emu(open(find(gamedir, 'START.EXE'), 'rb').read(), open(find(gamedir, 'GAME.OVR'), 'rb').read())
        self.saved = None
        self.menu = []
        self.keeps = []
        self.effects = []
        e.trap_start(*GETMEM, self._getmem)
        e.trap_start(*FREEMEM, Emu.ret(6))
        e.trap_start(*READ_STRING, self._read_string)
        for seg, off in DRAW_START:
            e.trap_start(seg, off, Emu.ret(retf_bytes(e.S, seg * 16 + off)))
        for o in DRAW_OVR:
            e.trap_ovr(o, Emu.ret(retf_bytes(e.O, o)))
        e.trap_ovr(MENU_VERTICAL, self._menu)
        e.trap_ovr(YES_NO, self._yes_no)
        e.trap_ovr(PORTRAIT, Emu.ret(0))
        e.trap_ovr(ICON, Emu.ret(0))
        e.trap_ovr(SAVE_CHAR, self._save)
        self.menu_ret = retf_bytes(e.O, MENU_VERTICAL)
        self.yes_no_ret = retf_bytes(e.O, YES_NO)
        self.read_ret = retf_bytes(e.S, READ_STRING[0] * 16 + READ_STRING[1])
        self.save_ret = retf_bytes(e.O, SAVE_CHAR)

    def _getmem(self, e):
        size = e.arg(0)
        seg, off = e.alloc(size)
        from unicorn.x86_const import UC_X86_REG_DX
        e.uc.reg_write(UC_X86_REG_DX, seg)
        e.pop_ret(2, off)

    def _menu(self, e):
        # The last pushed argument is the list cursor (var), before it the selection (var word).
        sel_off, sel_seg = struct.unpack('<HH', e.rb(e.stack() + 8, 4))
        pick = self.menu.pop(0)
        e.wb(sel_seg * 16 + sel_off, struct.pack('<H', pick))
        e.pop_ret(self.menu_ret, 0x0D)

    def _yes_no(self, e):
        e.pop_ret(self.yes_no_ret, ord('Y') if self.keeps.pop(0) else ord('N'))

    def _read_string(self, e):
        # Function result (a string): the caller's buffer is the first pushed argument, under the others.
        off, seg = struct.unpack('<HH', e.rb(e.stack() + 4 + self.read_ret, 4))
        name = b'ORACLE'
        e.wb(seg * 16 + off, bytes([len(name)]) + name)
        e.pop_ret(self.read_ret)

    def _save(self, e):
        # SaveChar(creature: far pointer, name: string): the creature is pushed first.
        off, seg = struct.unpack('<HH', e.rb(e.stack() + 4 + self.save_ret - 4, 4))
        lin = seg * 16 + off
        rec = e.rb(lin, REC)
        effects = []
        eoff, eseg = struct.unpack('<HH', rec[0x7F:0x83])
        n = 0
        while (eoff or eseg) and n < 32:
            node = e.rb(eseg * 16 + eoff, 9)
            # The 9 bytes as written to .SPC: 5 data bytes, then the far pointer to the next effect (stale on disk).
            effects.append(node[:5].hex())
            eoff, eseg = struct.unpack('<HH', node[5:9])
            n += 1
        self.saved = {'record': rec.hex(), 'effects': effects, 'seedAfter': e.randseed()}
        e.pop_ret(self.save_ret)

    def run(self, seed, picks, rerolls):
        e = self.e
        e.reset_heap()
        e.set_dsptr(CUR_CHAR, (0, 0))
        e.set_randseed(seed)
        self.menu = list(picks)
        self.keeps = [False] * rerolls + [True]
        self.saved = None
        e.call(CREATE)
        if self.saved is None:
            raise RuntimeError('the routine did not save a character')
        return self.saved


def main():
    gamedir, out = sys.argv[1], sys.argv[2]
    o = Oracle(gamedir)
    e = o.e
    rng = random.Random(19)

    def race_of(pick):  # the race list skips half-orc: position 6 is human (7)
        return pick + 1 if pick == 6 else pick

    def classes(pick):  # class list of a race (count at DS:3E17 + 12 * race, codes after it)
        r = race_of(pick)
        return [e.dsb(0x3E17 + 12 * r + k) for k in range(1, e.dsb(0x3E17 + 12 * r) + 1)]

    def alignments(cls):  # alignment list of a class (count at DS:3FE7 + 10 * class)
        return e.dsb(0x3FE7 + 10 * cls)

    plan = []
    # Every race × gender × class, each alignment in turn, then random picks.
    for race in range(1, 7):
        for gender in (1, 2):
            for c, code in enumerate(classes(race), 1):
                for a in range(1, alignments(code) + 1):
                    plan.append((race, gender, c, a, rng.randrange(3)))
    for _ in range(300):
        race = rng.randrange(1, 7)
        cl = classes(race)
        c = rng.randrange(1, len(cl) + 1)
        plan.append((race, rng.randrange(1, 3), c, rng.randrange(1, alignments(cl[c - 1]) + 1), rng.randrange(5)))
    cases = []
    for race, gender, cls, align, rerolls in plan:
        seed = rng.randrange(1 << 32)
        r = o.run(seed, [race, gender, cls, align], rerolls)
        r.update({'seed': seed, 'picks': [race, gender, cls, align], 'rerolls': rerolls})
        cases.append(r)
    with open(out, 'w') as f:
        json.dump({'cases': cases}, f, indent=0)
    print('%d cases' % len(cases))


if __name__ == '__main__':
    main()
