#!/usr/bin/env python3
"""Secret of the Silver Blades v1.30: the ECL character-view reader (GAME.OVR 0x9830) and writer (0x9D20) run by the
program itself (por_emu.py, unicorn) on made-up 439-byte party records, for GoldBox.Core's SotsbViewProgramTests
(docs/re/survey-sotsb.md §10.2).

Usage: python3 -I sotsb_view_oracle.py GAMEDIR OUT.json        (GAMEDIR: the SotSB v1.30 files; needs unicorn 2.x)

Each trial is a party list of 1–6 records of random bytes (record 0x6B forced to 6 in every third trial, so the level
pair flag of OVR 0x3CBB6 is exercised), a current member, the "member missing" byte DS:A4D1 and the disk byte DS:7420.
``reads``: every offset k = 0 … 0x3FF of $7C00 whose case the reader handles, with the value it returns (cases that
set no value return whatever the emulated stack held; the test skips those). ``writes``: offsets 0 … 0x12F and the
disk / wall-set cells with a few values each: the record bytes the writer changed, the disk bytes, DS:A4B7 and the
wall-set loads (OVR 0x4077E, trapped). por_emu works for SotSB's START.EXE / GAME.OVR as is. Nothing from the game is
stored: the records are random and the output is the program's answers.
"""
import json
import os
import random
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from por_emu import Emu  # noqa: E402

REC = 0x1B7
READER, WRITER, WALL_LOADER = 0x9830, 0x9D20, 0x4077E
TRIALS = 12
WRITE_KS = list(range(0, 0x130)) + [0x312, 0x322, 0x324, 0x326]


def main(game, out):
    e = Emu(open(os.path.join(game, 'START.EXE'), 'rb').read(), open(os.path.join(game, 'GAME.OVR'), 'rb').read())
    rng = random.Random(1)
    walls = []

    def wall_trap(em):
        walls.append([em.arg(2), em.arg(0)])  # (set, slot): Pascal pushes the set first
        em.pop_ret(4)
    e.trap_ovr(WALL_LOADER, wall_trap)

    def setup(recs, pos, disk):
        e.reset_heap()
        ptrs = [e.alloc(REC) for _ in recs]
        for i, (seg, off) in enumerate(ptrs):
            e.wb(seg * 16 + off, recs[i])
            nxt = ptrs[i + 1] if i + 1 < len(ptrs) else (0, 0)
            e.wb(seg * 16 + off + 0x19D, struct.pack('<HH', nxt[1], nxt[0]))
        e.set_dsptr(0x7D3C, ptrs[0])
        e.set_dsptr(0x7D38, ptrs[pos])
        area = e.alloc(0x800)
        e.set_dsptr(0x67CA, area)
        e.wb(area[0] * 16 + 0x67C, struct.pack('<H', 0x1234))  # $7F3E
        e.set_dsb(0x7420, disk)
        e.set_dsb(0x7421, 0)
        e.set_dsb(0xA4B7, 0)
        return ptrs

    trials = []
    for trial in range(TRIALS):
        count = rng.randint(1, 6)
        pos = rng.randrange(count)
        pool = [0, 0, 0, 1, 2, 3, 5, 6, 8, 13, 20, 0x7F, 0x80, 0xFF]
        recs = [bytearray(rng.choice(pool + [rng.randrange(256)]) for _ in range(REC)) for _ in range(count)]
        if trial % 3 == 0:
            recs[pos][0x6B] = 6
        recs = [bytes(r) for r in recs]
        missing, disk = rng.randint(0, 1), rng.randint(1, 3)
        setup(recs, pos, disk)
        e.set_dsb(0xA4D1, missing)
        flag = e.alloc(2)
        reads = []
        for k in range(0x400):
            e.wb(flag[0] * 16, b'\x55')
            ax, _ = e.call(READER, [0x7C00 + k, flag[0], flag[1]])
            if e.rb(flag[0] * 16)[0]:
                reads.append([k, ax])
        writes = []
        for k in WRITE_KS:
            for v in (0, 1, 0x81, 0xB3, rng.randrange(0x10000)):
                ptrs = setup(recs, pos, disk)
                walls.clear()
                e.call(WRITER, [0x7C00 + k, v])
                seg, off = ptrs[pos]
                after = e.rb(seg * 16 + off, REC)
                diff = [[i, after[i]] for i in range(REC) if after[i] != recs[pos][i]]
                writes.append([k, v, diff, e.dsb(0x7420), e.dsb(0x7421), e.dsb(0xA4B7), walls[:]])
        trials.append(dict(recs=[r.hex() for r in recs], pos=pos, missing=missing, disk=disk, reads=reads,
                           writes=writes))
    with open(out, 'w') as f:
        json.dump(dict(program='SotSB v1.30 GAME.OVR', reader=READER, writer=WRITER, trials=trials), f)
    print(TRIALS, 'trials,', sum(len(t['reads']) for t in trials), 'reads,', sum(len(t['writes']) for t in trials),
          'writes')


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
