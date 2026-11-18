#!/usr/bin/env python3
"""The training hall's Train Character routine GAME.OVR 0x1C32D run by the program itself (por_emu.py, unicorn) on
generated characters, for GoldBox.Core's TrainingProgramTests (docs/re/training.md).

Usage: python3 -I por_train_oracle.py GAMEDIR OUT.json        (GAMEDIR: the PoR v1.3 files; needs unicorn 2.x)

Each case is a creature record of made-up bytes (levels, race, abilities, experience near the thresholds, coins,
drain, hit points) put in as the current character (DS:5D92), the hall's class mask in the $6B00 area's word +0x550
(VM $6DA8), the cheat flag DS:470C and RandSeed. Text, windows and the Yes/No question are trapped (the question
answers Y or N as the case says); the spell list 0x239D6 is trapped and answers a spell picked from the list the
program would build (mode 4: spells 1..56 whose class and level have a slot and that are not known), or 0 with the
"list empty" byte when there is none. Everything else (gold total, payment, dice, level stats) runs as in the game.
The output holds no game text: the refusal is stored as the message's code-segment offset.
"""
import json
import os
import random
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from por_emu import Emu  # noqa: E402

REC = 0x11D
TRAIN = 0x1C32D
TRAPS_START = {0x7DF8: 6, 0x76B6: 10, 0x4047: 8, 0x23B6: 10, 0x2233: 4, 0x41F8: 16}
ASK = 0x2D9A0          # Yes/No question, retf 0xA, returns the key
SPELL_LIST = 0x239D6   # (type, action, var w, var b), retf 0xC
XP_TABLE = 0x409B
SPELLS = 0x31FA
MASKS = (113, 114, 116, 120)


def find(dirpath, name):
    for f in os.listdir(dirpath):
        if f.upper() == name:
            return os.path.join(dirpath, f)
    raise FileNotFoundError(name)


def thr(e, cls, lvl):
    off = XP_TABLE + cls * 0x38 + lvl * 4
    return struct.unpack('<i', e.rb(e.ds(off), 4))[0]


def learnable(e, rec):
    out = []
    for s in range(1, 57):
        cls = struct.unpack('b', e.rb(e.ds(SPELLS + 16 * s), 1))[0]
        lvl = struct.unpack('b', e.rb(e.ds(SPELLS + 16 * s + 1), 1))[0]
        if rec[0xB1 + 3 * cls + lvl] > 0 and rec[0x32 + s] == 0:
            out.append(s)
    return out


def creature(e, rng, good=False):
    rec = bytearray(rng.randrange(256) for _ in range(REC))
    for cls in range(8):
        rec[0x96 + cls] = 0
    combos = [[2], [0], [5], [6], [3], [4], [1], [7], [0, 2], [2, 5], [2, 6], [5, 6], [0, 2, 5], [0, 5], [0, 6],
              [2, 5, 6], [0, 4]]
    classes = rng.choice(combos)
    for cls in classes:
        rec[0x96 + cls] = rng.choice([1, 1, 2, 3, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 10, 11])
    rec[0x2E] = rng.choice([1, 2, 3, 4, 5, 6, 7, 7, 7, 0])
    for a in (0x10, 0x11, 0x12, 0x13, 0x14, 0x15):
        rec[a] = rng.randrange(3, 19)
    rec[0x10] = rng.choice([rec[0x10], 17, 18, 16])
    rec[0x2F] = rng.randrange(0, 12)
    rec[0x10C] = 0 if good else rng.choice([0] * 9 + [4, 1, 6])
    # experience: around a class's next threshold, its one after, or anything
    cls = rng.choice(classes)
    lvl = rec[0x96 + cls]
    t1, t2 = thr(e, cls, lvl + 1), thr(e, cls, lvl + 2)
    pick = rng.choice([0, 1, 2]) if good else rng.randrange(6)
    if pick == 0 and t1 > 0:
        xp = t1 + rng.randrange(-2, 3)
    elif pick == 1 and t2 > 0:
        xp = t2 + rng.randrange(-2, 30000)
    elif pick == 2 and t1 > 0:
        xp = rng.randrange(max(0, t1 - 5000), t1 + 5000)
    else:
        xp = rng.choice([rng.randrange(0, 300000), rng.randrange(0, 2 ** 31), 0])
    rec[0xAC:0xB0] = struct.pack('<I', max(0, xp) & 0xFFFFFFFF)
    # coins: copper, silver, electrum, gold, platinum, gems, jewellery
    kind = rng.choice([0, 1, 1, 2, 4]) if good else rng.randrange(5)
    coins = [0] * 7
    if kind == 0:
        coins = [rng.randrange(0, 4000) for _ in range(5)] + [rng.randrange(5), rng.randrange(5)]
    elif kind == 1:
        coins[4] = rng.choice([200, 199, 201, 1000, 30000])
        coins[3] = rng.randrange(5)
    elif kind == 2:   # just around 1000 gold, mostly small coins (the rounded check and the change)
        coins[0] = rng.randrange(0, 200)
        coins[1] = rng.randrange(0, 20)
        coins[2] = rng.randrange(0, 10)
        coins[3] = rng.randrange(990, 1001)
        coins[5], coins[6] = rng.randrange(3), rng.randrange(3)
    elif kind == 3:   # copper only near 200,000
        coins[0] = 0
        coins[1] = rng.randrange(19985, 20005)
        coins[5] = rng.randrange(4)
    else:
        coins = [rng.randrange(0, 65536) for _ in range(5)] + [rng.randrange(20), rng.randrange(20)]
    for k, v in enumerate(coins):
        rec[0x88 + 2 * k:0x8A + 2 * k] = struct.pack('<H', v)
    if rng.random() < 0.25:
        rec[0x74] = rng.randrange(1, 4)
        rec[0x75] = rng.randrange(0, 30)
    else:
        rec[0x74] = rec[0x75] = 0
    mx = rng.randrange(1, 90)
    rec[0x32] = mx
    rec[0x11B] = rng.randrange(0, mx + 1)
    rec[0xB1] = rng.randrange(1, 90)
    for s in range(1, 57):
        rec[0x32 + s] = 1 if rng.random() < 0.3 else 0
    if rng.random() < (0.3 if good and 5 in classes else 0.08):   # a magic-user who knows every spell: the Learn list is empty
        for s in range(1, 57):
            rec[0x32 + s] = 1
    rec[0x73] = max(rec[0x96:0x9E])
    return bytes(rec)


def main():
    gamedir, out = sys.argv[1], sys.argv[2]
    e = Emu(open(find(gamedir, 'START.EXE'), 'rb').read(), open(find(gamedir, 'GAME.OVR'), 'rb').read())
    state = {}

    def msg(em):
        off = em.arg(2)
        state['message'] = off
        em.pop_ret(6)

    def ask(em):
        state['asked'] = True
        em.pop_ret(10, ord(state['answer']))

    def spell_list(em):
        ptr = e.dsptr(0x5D92)
        rec = em.rb(ptr[0] * 16 + ptr[1], REC)
        lst = learnable(em, rec)
        b_off, b_seg = em.arg(0), em.arg(2)
        state.setdefault('lists', []).append(lst)
        if not lst:
            em.wb(b_seg * 16 + b_off, b'\x00')
            em.pop_ret(12, 0)
            return
        em.wb(b_seg * 16 + b_off, b'\x01')
        s = state['rng'].choice(lst)
        state['pick'] = s
        em.pop_ret(12, s)

    for off, n in TRAPS_START.items():
        if off == 0x7DF8:
            e.trap_start(0, off, msg)
        else:
            e.trap_start(0, off, Emu.ret(n))
    e.trap_ovr(ASK, ask)
    e.trap_ovr(SPELL_LIST, spell_list)

    # The message's string is first copied to a local by START 0xBA8B; the trap gets the local. Record the code
    # offset instead: the routine passes CS:offset to 0xBA8B just before, so watch that copy.
    def copy(em):
        state['last_copy'] = em.arg(0)
    e.trap_start(0, 0xBA8B, copy)

    # A run-time error (the payment's long division by zero, START 0xBA5D) leaves through 0xB062: stop there.
    def halt(em):
        state['halt'] = True
        em.uc.emu_stop()
    e.trap_start(0, 0xB062, halt)

    rng = random.Random(19)
    cases = []
    for n in range(900):
        good = n % 3 != 0   # two in three set up to pass the checks (status 0, money, experience, a matching hall)
        rec = creature(e, rng, good)
        bits = 0
        for cls in range(8):
            if rec[0x96 + cls]:
                bits |= e.dsb(0x5DE + cls)
        fits = [m for m in MASKS if m & bits & 0x0F]
        e.reset_heap()
        seg, off = e.alloc(REC)
        lin = seg * 16 + off
        e.wb(lin, rec)
        e.set_dsptr(0x5D92, (seg, off))
        aseg, aoff = e.alloc(0x800)
        if good and fits:
            mask = rng.choice(fits)
        else:
            mask = rng.choice(MASKS + (rng.randrange(256), 0)) if rng.random() < 0.15 else rng.choice(MASKS)
        e.wb(aseg * 16 + 0x550, struct.pack('<H', mask))
        e.set_dsptr(0x49D6, (aseg, aoff))
        cheat = 1 if rng.random() < 0.08 else 0
        e.set_dsb(0x470C, cheat)
        seed = rng.randrange(2 ** 32)
        e.set_randseed(seed)
        state.clear()
        state['answer'] = 'Y' if rng.random() < 0.85 else 'N'
        state['rng'] = random.Random(n)
        try:
            e.call(TRAIN)
        except RuntimeError:
            if not state.get('halt'):
                raise
        cases.append({
            'halt': state.get('halt', False),
            'before': rec.hex(), 'after': e.rb(lin, REC).hex(), 'mask': mask, 'cheat': cheat,
            'answer': state['answer'], 'seed': seed, 'seedAfter': e.randseed(),
            'refusal': state.get('last_copy') if 'message' in state else None,
            'asked': state.get('asked', False), 'pick': state.get('pick', 0),
            'lists': state.get('lists', []),
        })
    with open(out, 'w') as f:
        json.dump({'cases': cases}, f, indent=0)
    trained = sum(1 for c in cases if c['before'] != c['after'])
    print('%d cases, %d changed, %d asked, %d refusals' % (
        len(cases), trained, sum(c['asked'] for c in cases), sum(c['refusal'] is not None for c in cases)))


if __name__ == '__main__':
    main()
