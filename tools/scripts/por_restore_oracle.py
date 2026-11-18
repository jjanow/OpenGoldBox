#!/usr/bin/env python3
"""Restoration (spell 56, GAME.OVR 0x29C5B) run by the program itself with the real race level limit (START 0x3AF7)
and level stats (0x2AA87), for GoldBox.Core's RestorationProgramTests (docs/re/spells-effects.md "Restoration at the
race limit").

Usage: python3 -I por_restore_oracle.py GAMEDIR OUT.json        (GAMEDIR: the PoR v1.3 files; needs unicorn 2.x)

por_spell_oracle.py answers START 0x3AF7 with "no limit" and skips 0x2AA87; here both run (neither calls anything
outside) and only the message (START 0x228C) is trapped. The routine takes no arguments: its target is DS:6C95.
The class index [bp-0xC] is uninitialised when no class qualifies; Emu.call's stack filler would put 0xEE there, so a
hook after the prologue (0x29C61) writes 8, the value DOSBox-X showed on every sampled path (fixture
fight_restoration), and the run reproduces the program as sampled. The cases are a grid, not random: START 0x3AF7
tests exact (level, Strength) pairs. The records are made up, but the results hold bytes from the program's tables
(experience thresholds, 0x2AA87's THAC0, saves, slots): the output is kept with the private fixtures.
"""
import json
import os
import random
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from por_emu import Emu  # noqa: E402
from unicorn.x86_const import UC_X86_REG_BP, UC_X86_REG_SS  # noqa: E402

REC = 0x11D
RESTORATION = 0x29C5B
AFTER_PROLOGUE = 0x29C61
MESSAGE = 0x228C            # START far routine (00BA:16EC), 12 bytes of arguments
RESIDUE = 8                 # DOSBox-X's [bp-0xC] (spells-effects.md "Restoration with no class to raise")
FIGHTER, CLERIC, MAGE, THIEF = 2, 0, 5, 6
HUMAN, DWARF, ELF, GNOME, HALFELF, HALFLING = 0, 1, 2, 3, 4, 5


def find(dirpath, name):
    for f in os.listdir(dirpath):
        if f.upper() == name:
            return os.path.join(dirpath, f)
    raise FileNotFoundError(name)


def record(rnd, race, levels, strength, drained, drainedhp, xp):
    """A party-member-like record: random filler where the routine and 0x2AA87 don't decide anything, the fields they
    read set."""
    r = bytearray(rnd.randrange(256) for _ in range(REC))
    r[0] = 4
    r[1:5] = b'TEST'
    r[0x10] = strength
    r[0x11] = rnd.randrange(3, 19)   # Intelligence
    r[0x12] = rnd.randrange(3, 19)   # Wisdom
    r[0x13] = rnd.randrange(3, 19)   # Dexterity
    r[0x2E] = race
    for k in range(8):
        r[0x96 + k] = levels[k]
    r[0x74] = drained
    r[0x75] = drainedhp
    r[0xAC:0xB0] = struct.pack('<i', xp)
    return bytes(r)


def grid(rnd, xp_table):
    cases = []

    def lv(**kw):
        a = [0] * 8
        for k, v in kw.items():
            a[{'c': CLERIC, 'f': FIGHTER, 'm': MAGE, 't': THIEF}[k]] = v
        return a

    def xps(levels):
        """Experience below, at and above the threshold of the class Restoration would pick (or a few fixed values)."""
        out = {0, 9_999_999, 10_000_000, 20_000_000, -5}
        for k in range(8):
            L = levels[k]
            if 0 < L < 14:
                t = xp_table[k][L + 1]
                if t > 0:
                    out |= {t - 1, t, t + 1}
        return sorted(out)

    def drains():
        return [(1, rnd.randrange(0, 12)), (2, rnd.randrange(0, 30)), (3, 7), (255, 255), (1, 250)]

    # Every race-limit row of START 0x3AF7 (fighter: dwarf, elf, gnome, half-elf, halfling; half-elf cleric), at the
    # row's level, one below and one above, with Strength 16, 17, 18.
    rows = [(DWARF, 'f', 8), (ELF, 'f', 7), (GNOME, 'f', 6), (HALFELF, 'f', 8), (HALFLING, 'f', 6), (HALFELF, 'c', 5)]
    for race, cls, top in rows:
        for L in range(top - 4, top + 2):
            if L <= 0:
                continue
            for st in (16, 17, 18):
                levels = lv(**{cls: L})
                for xp in rnd.sample(xps(levels), 3):
                    d, dh = rnd.choice(drains())
                    cases.append(record(rnd, race, levels, st, d, dh, xp))
    # Multi-class half-elves and others with one class at its limit.
    multis = [(HALFELF, lv(c=5, f=4)), (HALFELF, lv(c=5, f=6)), (HALFELF, lv(c=5, f=7)), (HALFELF, lv(c=4, f=8)),
              (HALFELF, lv(c=5, f=8)), (HALFELF, lv(c=5, m=4)), (HALFELF, lv(c=5, f=6, m=5)),
              (HALFELF, lv(f=8, m=6)), (ELF, lv(f=5, m=5)), (ELF, lv(f=6, m=6)), (ELF, lv(f=7, m=3)),
              (DWARF, lv(f=7, t=5)), (DWARF, lv(f=8, t=9)), (GNOME, lv(f=5, t=4)), (GNOME, lv(f=6, t=10)),
              (HALFLING, lv(f=4, t=3)), (HALFLING, lv(f=6, t=9)), (HALFELF, lv(c=5, f=8, m=6))]
    for race, levels in multis:
        for st in (16, 17, 18):
            for xp in rnd.sample(xps(levels), 3):
                d, dh = rnd.choice(drains())
                cases.append(record(rnd, race, levels, st, d, dh, xp))
    # Past the decoded experience rows and the best-13 cap: levels 9..14 and 0xFF, humans and races 6/7 as controls.
    for race in (HUMAN, 6, 7, DWARF, HALFELF):
        for cls in 'cfmt':
            for L in (6, 7, 8, 9, 10, 11, 12, 13, 14, 0xFF):
                levels = lv(**{cls: L})
                d, dh = rnd.choice(drains())
                xp = rnd.choice(xps(levels))
                cases.append(record(rnd, race, levels, rnd.choice((16, 17, 18)), d, dh, xp))
    # Not drained: the routine does nothing.
    for _ in range(5):
        cases.append(record(rnd, rnd.randrange(8), lv(f=rnd.randrange(1, 9)), 17, 0, rnd.randrange(256), 1000))
    return cases


def main():
    if len(sys.argv) != 3:
        print(__doc__)
        return 2
    gamedir, out = sys.argv[1], sys.argv[2]
    e = Emu(open(find(gamedir, 'START.EXE'), 'rb').read(), open(find(gamedir, 'GAME.OVR'), 'rb').read())
    e.trap_start(0, MESSAGE, Emu.ret(12))

    def seed_residue(em):
        bp = em.uc.reg_read(UC_X86_REG_BP)
        ss = em.uc.reg_read(UC_X86_REG_SS)
        em.wb(ss * 16 + ((bp - 0xC) & 0xFFFF), [RESIDUE])
    e.trap_ovr(AFTER_PROLOGUE, seed_residue)
    xp_table = [[struct.unpack('<i', e.rb(e.ds(0x409B + 0x38 * c + 4 * L), 4))[0] for L in range(15)]
                for c in range(8)]
    rnd = random.Random(20261118)
    cases = []
    for before in grid(rnd, xp_table):
        e.reset_heap()
        seg, off = e.alloc(REC)
        lin = seg * 16 + off
        e.wb(lin, before)
        e.set_dsptr(0x6C95, (seg, off))
        e.set_dsptr(0x5D92, (seg, off))
        e.set_dsb(0x6C94, 1)
        e.call(RESTORATION)
        cases.append({'before': before.hex(), 'after': e.rb(lin, REC).hex()})
    with open(out, 'w') as f:
        json.dump({'source': 'GAME.OVR / START.EXE v1.3 run under unicorn (tools/scripts/por_restore_oracle.py)',
                   'residue': RESIDUE, 'cases': cases}, f, indent=0)
    print('%d cases' % len(cases))
    return 0


if __name__ == '__main__':
    sys.exit(main())
