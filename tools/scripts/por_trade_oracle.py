#!/usr/bin/env python3
"""View's Trade from the program itself: runs the v1.3 routines (por_emu.py, unicorn) on generated characters and
writes what they produce, for GoldBox.Core's TradeProgramTests (docs/re/camp.md "Trade").

Usage: python3 -I por_trade_oracle.py GAMEDIR OUT.json        (GAMEDIR: the PoR v1.3 files; needs unicorn 2.x)

Cases (fixed seed, so the output is the same every run):
- coins: the money transfer 0x250D5 (kind, amount, the giver DS:5D92 and the picked member), one in ten to the giver
  itself; START 0x1693 (the row-24 message) is trapped and recorded.
- items: the item Trade 0x2276E on an item of DS:5D92, with START 0x3379 (the screen redraw) trapped to do nothing and
  START's member pick 0x37E8 scripted to answer a member (one in ten the giver, one in ten nobody); the carry test
  0x23795, the stat recalculation START 0x1758, the add START 0x2197 and the removal START 0x20A9 run as they are
  (GetMem / FreeMem from the emulator's arena). Then START 0x1758 on the giver, as the Items loop does after every
  command (0x224AF on DS:5D92): 'giverAfterLoop'. Every other giver starts with stale derived bytes (attack bytes
  0x115-0x11A and movement 0x11C := 0xEE, as a weapon's dice left after it was unreadied), so the recalculation's
  output is seen whole.
- copies: the copy step 0x6F0D that the shop's Buy (0x7035) and the loot's Take (0x6184) call, on DS:5D92 and an
  item: the carry test 0x23795 (with its START 0x1758), the copy appended, START 0x1758 again (0x7018). Every other
  character starts with stale derived bytes (0xEE) and a wrong item count.
The records are the game's own CHRDATA1-6.SAV with strength, money, weight and items made up here; the output goes in
tests/fixtures-private all the same.
"""
import json
import os
import random
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from por_emu import Emu  # noqa: E402
from unicorn.x86_const import UC_X86_REG_DX  # noqa: E402

TRANSFER, ITEM_TRADE, COPY = 0x250D5, 0x2276E, 0x6F0D
RECALC = (0xBA, 0xBB8)  # START 0x1758
REDRAW, PICK, ROW_MESSAGE = (0xBA, 0x27D9), (0xBA, 0x2C48), (0xBA, 0x1693)
GETMEM, FREEMEM = (0xAF8, 0x23F), (0xAF8, 0x254)
REC, ITEM = 0x11D, 0x3F


def find(dirpath, name):
    for f in os.listdir(dirpath):
        if f.upper() == name:
            return os.path.join(dirpath, f)
    raise FileNotFoundError(name)


def ptr_bytes(p):
    return struct.pack('<HH', p[1], p[0]) if p else b'\0\0\0\0'


class TradeOracle:
    def __init__(self, gamedir):
        with open(find(gamedir, 'START.EXE'), 'rb') as f:
            exe = f.read()
        with open(find(gamedir, 'GAME.OVR'), 'rb') as f:
            ovr = f.read()
        self.e = e = Emu(exe, ovr)
        self.messages = 0
        self.pick_answer = None
        self.pick_start = None

        def getmem(em):
            seg, off = em.alloc(em.arg(0))
            em.uc.reg_write(UC_X86_REG_DX, seg)
            em.pop_ret(2, off)

        def message(em):
            self.messages += 1
            em.pop_ret(4)

        def pick(em):
            # 0x37E8(prompt, keypad, var who): the var is the last argument pushed
            var = em.arg(2) * 16 + em.arg(0)
            o, s = struct.unpack('<HH', em.rb(var, 4))
            self.pick_start = (s, o)
            em.wb(var, ptr_bytes(self.pick_answer))
            em.pop_ret(10)

        e.trap_start(*GETMEM, getmem)
        e.trap_start(*FREEMEM, Emu.ret(6))
        e.trap_start(*ROW_MESSAGE, message)
        e.trap_start(*REDRAW, Emu.ret(0))
        e.trap_start(*PICK, pick)
        with open(find(gamedir, 'ITEMS'), 'rb') as f:
            self.base_items = f.read()[2:2 + 16 * 128]
        e.wb(e.ds(0x5582), self.base_items)
        self.records = []
        for k in range(1, 7):
            with open(find(gamedir, 'CHRDATA%d.SAV' % k), 'rb') as f:
                self.records.append(f.read()[:REC])

    def lin(self, p):
        return p[0] * 16 + p[1]

    def record(self, rnd):
        b = bytearray(rnd.choice(self.records))
        b[0x10] = rnd.choice([3, 8, 12, 15, 16, 17, 18, 18, 18, 19])
        b[0x16] = rnd.choice([0, 0, 1, 50, 76, 91, 99, 100])
        for k in range(7):
            struct.pack_into('<H', b, 0x88 + 2 * k, rnd.choice([0, 0, 1, 50, 300, 2000, 65500]))
        struct.pack_into('<H', b, 0x102, rnd.choice([0, 100, 900, 1400, 1700, 1990, 65500]))
        b[0x34] = 0
        b[0x7F:0x83] = b'\0\0\0\0'
        b[0xC8:0x100] = bytes(0x38)
        b[0x104:0x10C] = bytes(8)
        return b

    def item(self, rnd):
        b = bytearray(ITEM)
        b[0x2E] = rnd.choice([t for t in range(1, 128) if self.base_items[16 * t] not in (0xFF,)])
        b[0x2F] = rnd.randrange(1, 200)
        b[0x34] = rnd.choice([0, 0, 0, 1])
        struct.pack_into('<H', b, 0x37, rnd.choice([0, 1, 5, 10, 30, 50, 150, 400, 1000]))
        b[0x39] = rnd.choice([0, 0, 1, 2, 10, 40])
        return b

    def place(self, rec_bytes, items):
        e = self.e
        p = e.alloc(REC)
        e.wb(self.lin(p), bytes(rec_bytes))
        ptrs = [e.alloc(ITEM) for _ in items]
        for j, (q, r) in enumerate(zip(ptrs, items)):
            r = bytearray(r)
            r[0x2A:0x2E] = ptr_bytes(ptrs[j + 1] if j + 1 < len(ptrs) else None)
            e.wb(self.lin(q), bytes(r))
        e.wb(self.lin(p) + 0xC8, ptr_bytes(ptrs[0] if ptrs else None))
        return p, ptrs

    def items_of(self, p):
        e = self.e
        out = []
        o, s = struct.unpack('<HH', e.rb(self.lin(p) + 0xC8, 4))
        while (s, o) != (0, 0) and len(out) < 40:
            r = e.rb(s * 16 + o, ITEM)
            out.append(bytes(r[:0x2A]) + b'\0\0\0\0' + bytes(r[0x2E:]))
            o, s = struct.unpack('<HH', r[0x2A:0x2E])
        return out

    def coins(self, rnd, n):
        e = self.e
        out = []
        for _ in range(n):
            e.reset_heap()
            g = self.record(rnd)
            same = rnd.random() < 0.1
            pk = g if same else self.record(rnd)
            gp, _ = self.place(g, [])
            pp = gp if same else self.place(pk, [])[0]
            kind = rnd.randrange(7)
            held = struct.unpack_from('<H', g, 0x88 + 2 * kind)[0]
            amount = rnd.choice([0, 1, held, held // 2, min(held, 1500), 65535])
            var = e.alloc(4)
            e.wb(self.lin(var), ptr_bytes(pp))
            e.set_dsptr(0x5D92, gp)
            self.messages = 0
            e.call(TRANSFER, [e.dseg, 0x5D92, var[0], var[1], amount, kind])
            out.append({'giver': g.hex(), 'pick': None if same else pk.hex(), 'kind': kind, 'amount': amount,
                        'giverAfter': e.rb(self.lin(gp), REC).hex(),
                        'pickAfter': None if same else e.rb(self.lin(pp), REC).hex(), 'messages': self.messages})
        return out

    def items(self, rnd, n):
        e = self.e
        out = []
        for _ in range(n):
            e.reset_heap()
            g = self.record(rnd)
            if len(out) % 2 == 0:
                g[0x115:0x11B] = b'\xee' * 6  # attack dice, sides, bonus (0x11B is the HP)
                g[0x11C] = 0xEE  # movement
            gi = [self.item(rnd) for _ in range(rnd.randint(1, 5))]
            r = rnd.random()
            who = 'self' if r < 0.1 else 'none' if r < 0.2 else 'other'
            pk = self.record(rnd)
            if rnd.random() < 0.7:
                # the carry test recalculates the weight from the items and coins: mostly light pickers
                for k in range(7):
                    struct.pack_into('<H', pk, 0x88 + 2 * k, rnd.choice([0, 0, 10, 300]))
            pi = [self.item(rnd) for _ in range(rnd.choice([0, 1, 3, 15, 16, 17]))]
            gp, gptrs = self.place(g, gi)
            pp, _ = self.place(pk, pi)
            # DS:471A: the member the pick starts on (View sets it to DS:5D92; a former pick later)
            start = gp if rnd.random() < 0.5 else pp
            e.set_dsptr(0x471A, start)
            e.set_dsptr(0x5D92, gp)
            self.pick_answer = {'self': gp, 'none': None, 'other': pp}[who]
            self.messages = 0
            which = rnd.randrange(len(gi))
            e.call(ITEM_TRADE, [*gptrs[which]])
            target = e.dsptr(0x471A)
            after = e.rb(self.lin(gp), REC)
            e.call(RECALC, [gp[0], gp[1]])
            out.append({'giver': g.hex(), 'giverItems': [bytes(x).hex() for x in gi], 'pick': pk.hex(),
                        'pickItems': [bytes(x).hex() for x in pi], 'item': which, 'who': who,
                        'startIsGiver': start == gp, 'pickStartedOnStart': self.pick_start == start,
                        'messages': self.messages,
                        'target': 'giver' if target == gp else 'pick' if target == pp else 'other',
                        'giverAfter': after.hex(),
                        'giverAfterLoop': e.rb(self.lin(gp), REC).hex(),
                        'giverItemsAfter': [x.hex() for x in self.items_of(gp)],
                        'pickAfter': e.rb(self.lin(pp), REC).hex(),
                        'pickItemsAfter': [x.hex() for x in self.items_of(pp)]})
        return out

    def copies(self, rnd, n):
        e = self.e
        out = []
        for _ in range(n):
            e.reset_heap()
            c = self.record(rnd)
            if rnd.random() < 0.7:
                for k in range(7):
                    struct.pack_into('<H', c, 0x88 + 2 * k, rnd.choice([0, 0, 10, 300]))
            if len(out) % 2 == 0:
                c[0x115:0x11B] = b'\xee' * 6
                c[0x11C] = 0xEE
                c[0xC7] = rnd.choice([0, 3, 20])
            ci = [self.item(rnd) for _ in range(rnd.choice([0, 1, 3, 15, 16, 17]))]
            item = self.item(rnd)
            cp, _ = self.place(c, ci)
            ip = e.alloc(ITEM)
            e.wb(self.lin(ip), bytes(item))
            res = e.alloc(2)
            e.set_dsptr(0x5D92, cp)
            self.messages = 0
            e.call(COPY, [ip[0], ip[1], res[0], res[1]])
            out.append({'record': c.hex(), 'items': [bytes(x).hex() for x in ci], 'item': bytes(item).hex(),
                        'refused': e.rb(self.lin(res), 1)[0], 'messages': self.messages,
                        'after': e.rb(self.lin(cp), REC).hex(), 'itemsAfter': [x.hex() for x in self.items_of(cp)]})
        return out


def main(argv):
    if len(argv) != 3:
        print(__doc__, file=sys.stderr)
        return 2
    o = TradeOracle(argv[1])
    rnd = random.Random(4242)
    data = {'items_5582': o.base_items.hex(), 'coins': o.coins(rnd, 300), 'items': o.items(rnd, 300)}
    data['copies'] = o.copies(rnd, 300)
    with open(argv[2], 'w') as f:
        json.dump(data, f, indent=0)
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
