#!/usr/bin/env python3
"""The door menu's Bash, Pick and Knock routines run by the program itself (por_emu.py, unicorn) on generated parties,
for GoldBox.Core's DoorProgramTests (docs/re/geo-format.md "Door menu").

Usage: python3 -I por_door_oracle.py GAMEDIR OUT.json        (GAMEDIR: the PoR v1.3 files; needs unicorn 2.x)

Each case: a party of 1..8 made-up creature records (list head DS:5D96, links +0x104) with Strength, exceptional
Strength, open locks, status, class levels and memorised slots chosen to reach every branch; the party's square and
facing (DS:6AAD x, DS:6AAE y, DS:6AAF facing 0/2/4/6, edges included); a made-up 16x16 map (four planes, the pointer
DS:6A5C); the menu flags DS:6DDE / 6DDF; RandSeed. Then one of:
- Bash 0x1695F (with 0x167D4, the face set, and the dice 0x2BCCD run as they are; the barrier accessor 0x2EC47 is
  trapped and answers the case's barrier 2 or 3, its arguments logged);
- Pick 0x16BAA (0x167D4 and the dice as they are);
- Knock 0x16D13 (0x16CB9 and 0x16C76 as they are);
- the class test 0x16901(k) and the spell test 0x16CB9(n) (their results only).
Out: AL, the records that changed, the map's barrier plane, the flags and RandSeed after. The records are made up here; the output
goes in tests/fixtures-private.
"""
import json
import os
import random
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from por_emu import Emu  # noqa: E402

REC = 0x11D
BASH, PICK, KNOCK, HAS_CLASS, HAS_SPELL = 0x1695F, 0x16BAA, 0x16D13, 0x16901, 0x16CB9
BARRIER = 0x2EC47   # (x, y, facing), retf 6


def find(dirpath, name):
    for f in os.listdir(dirpath):
        if f.upper() == name:
            return os.path.join(dirpath, f)
    raise FileNotFoundError(name)


def member(rng):
    rec = bytearray(rng.randrange(256) for _ in range(REC))
    rec[0x10] = rng.choice([rng.randrange(0, 26), 18, 18, rng.randrange(3, 19), rng.randrange(19, 26), rng.randrange(26, 256)])
    rec[0x16] = rng.choice([0, rng.randrange(1, 51), 50, 51, rng.randrange(51, 100), 90, 91, 99, 100, 101, rng.randrange(256)])
    rec[0x78] = rng.choice([rng.randrange(0, 101), 0, 100, rng.randrange(256)])
    rec[0x10C] = rng.choice([0, 0, 0, rng.randrange(1, 9), rng.randrange(256)])
    for k in range(8):
        rec[0x96 + k] = rng.choice([0, 0, 0, rng.randrange(1, 12), rng.randrange(128, 256)])
    for s in range(21):
        rec[0x17 + s] = rng.choice([0] * 12 + [31, 31 + 0x80, rng.randrange(1, 60), rng.randrange(256)])
    return bytes(rec)


def main():
    gamedir, out = sys.argv[1], sys.argv[2]
    with open(find(gamedir, 'START.EXE'), 'rb') as f:
        exe = f.read()
    with open(find(gamedir, 'GAME.OVR'), 'rb') as f:
        ovr = f.read()
    e = Emu(exe, ovr)
    state = {}

    def barrier(em):
        state.setdefault('barrierCalls', []).append([em.arg(4) & 0xFF, em.arg(2) & 0xFF, em.arg(0) & 0xFF])
        em.pop_ret(6, state['barrier'])
    e.trap_ovr(BARRIER, barrier)

    rng = random.Random(52)
    cases = []
    kinds = ['bash'] * 6 + ['pick'] * 4 + ['knock'] * 3 + ['class', 'spell']
    for n in range(900):
        kind = kinds[n % len(kinds)]
        e.reset_heap()
        size = rng.choice([1, 1, 2, 3, 4, 5, 6, 6, 8])
        recs = [member(rng) for _ in range(size)]
        ptrs = [e.alloc(REC) for _ in range(size)]
        for i, (seg, off) in enumerate(ptrs):
            nxt = ptrs[i + 1] if i + 1 < size else (0, 0)
            r = bytearray(recs[i])
            r[0x104:0x108] = struct.pack('<HH', nxt[1], nxt[0])
            recs[i] = bytes(r)
            e.wb(seg * 16 + off, r)
        e.set_dsptr(0x5D96, ptrs[0])
        mseg, moff = e.alloc(0x400)
        planes = bytes(rng.randrange(256) for _ in range(0x400))
        e.wb(mseg * 16, planes)
        e.set_dsptr(0x6A5C, (mseg, moff))
        x = rng.choice([0, 15, rng.randrange(16)])
        y = rng.choice([0, 15, rng.randrange(16)])
        facing = rng.choice([0, 2, 4, 6])
        e.set_dsb(0x6AAD, x)
        e.set_dsb(0x6AAE, y)
        e.set_dsb(0x6AAF, facing)
        flags = [rng.choice([0, 1, 1]), rng.choice([0, 1, 1]), rng.choice([0, 1, 1])]
        for k, v in enumerate(flags):
            e.set_dsb(0x6DDE + k, v)
        seed = rng.randrange(2 ** 32)
        e.set_randseed(seed)
        state.clear()
        state['barrier'] = rng.choice([2, 2, 3])
        arg = None
        if kind == 'bash':
            al, _ = e.call(BASH)
        elif kind == 'pick':
            al, _ = e.call(PICK)
        elif kind == 'knock':
            al, _ = e.call(KNOCK)
        elif kind == 'class':
            arg = rng.randrange(8)
            al, _ = e.call(HAS_CLASS, [arg])
        else:
            arg = rng.choice([31, 31, rng.randrange(1, 60), 31 + 0x80])
            ax, dx = e.call(HAS_SPELL, [arg])
            # the result is the record pointer: give its party index (or -1)
            ptr = (dx, ax)
            al = ptrs.index(ptr) if ptr in ptrs else (0xFF if ptr == (0, 0) else 0xFE)
        cases.append({
            'kind': kind, 'arg': arg, 'x': x, 'y': y, 'facing': facing, 'barrier': state['barrier'],
            'flags': flags, 'seed': seed, 'planes': planes[0x300:].hex(),
            'records': [r.hex() for r in recs],
            'result': al & 0xFF,
            # only the records that changed (index -> bytes)
            'recordsAfter': {str(i): e.rb(s * 16 + o, REC).hex() for i, (s, o) in enumerate(ptrs)
                             if e.rb(s * 16 + o, REC) != recs[i]},
            'planesAfter': e.rb(mseg * 16 + 0x300, 0x100).hex(),
            'flagsAfter': [e.dsb(0x6DDE + k) for k in range(3)],
            'seedAfter': e.randseed(),
            'barrierCalls': state.get('barrierCalls', []),
        })
    with open(out, 'w') as f:
        json.dump({'cases': cases}, f, indent=0)
    for k in ('bash', 'pick', 'knock', 'class', 'spell'):
        cs = [c for c in cases if c['kind'] == k]
        print('%s: %d cases, %d result 1' % (k, len(cs), sum(1 for c in cs if c['result'] == 1)))


if __name__ == '__main__':
    main()
