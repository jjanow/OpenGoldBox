#!/usr/bin/env python3
"""Party member selection, the temple's cure 1 and the shop's Id from the program itself: the v1.3 routines (por_emu.py,
unicorn) on generated parties, for GoldBox.Core's VenueProgramTests (docs/re/shop-format.md "Party selection",
"Temple", "Id").

Usage: python3 -I por_venue_oracle.py GAMEDIR OUT.json        (GAMEDIR: the PoR v1.3 files; needs unicorn 2.x)

Cases (fixed seed, so the output is the same every run):
- members: 0x23819(key) on a party list of 0–6 records (links at +0x104, head DS:5D96) with the current member
  DS:5D92 one of them, nil, or a record outside the list; keys Home 0x47, End 0x4F and others. Out: the index of the
  current member afterwards (-1 nil, -2 the outside record).
- temple: 0x4864 (cure 1) on one character with a made-up effect list (the six disease ids among others, byte 4 set
  or not); the "cure anyway" question 0x458A and the pay question 0x4636 are trapped and answer Y or N (recorded,
  with whether 0x458A was asked). The effect handlers run as they are (table DS:6828 built by 0x11DFD, as
  por_spell_oracle.py does); text routines are trapped. Out: the record, the effect list and DS:681F afterwards.
- identify: 0x22F8C (item, var redraw) on a character DS:5D92 / DS:4716 with made-up money and an item (hidden or
  not), the pool DS:67F4; the yes/no 0x2D9A0 answers Y or N; the name builder START 0x0FE2, the text routine START
  0x77F8, the row-24 message START 0x2233, PauseOnly START 0x7E4E and the box clear START 0x4047 are trapped and
  counted. Out: the record, the item, the pool, the redraw flag and the trap counts.
The records are made up here (zeros with the bytes the routines read); the output goes in tests/fixtures-private.
"""
import json
import os
import random
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from por_emu import Emu  # noqa: E402
from por_spell_oracle import Oracle as SpellOracle  # noqa: E402

SELECT, TEMPLE_CURE1, IDENTIFY = 0x23819, 0x4864, 0x22F8C
CURE_ANYWAY, PAY = 0x458A, 0x4636
YESNO = 0x2D9A0
NAME, TEXT, ROW_MESSAGE, PAUSE, CLEAR_BOX = 0xFE2, 0x77F8, 0x2233, 0x7E4E, 0x4047
REC, ITEM = 0x11D, 0x3F
DISEASE = [31, 34, 43, 44, 50, 57]


class VenueOracle(SpellOracle):
    def __init__(self, gamedir):
        super().__init__(gamedir)
        e = self.e
        self.answers = []
        self.counts = {}

        def answer(nbytes, name):
            def f(em):
                self.counts[name] = self.counts.get(name, 0) + 1
                a = self.answers.pop(0) if self.answers else ord('N')
                em.pop_ret(nbytes, a)
            return f

        def count(nbytes, name):
            def f(em):
                self.counts[name] = self.counts.get(name, 0) + 1
                em.pop_ret(nbytes)
            return f

        e.trap_ovr(CURE_ANYWAY, answer(4, 'cureAnyway'))
        e.trap_ovr(PAY, answer(6, 'pay'))
        e.trap_ovr(YESNO, answer(10, 'yesNo'))
        e.trap_start(0, NAME, count(16, 'name'))
        e.trap_start(0, TEXT, count(16, 'text'))
        e.trap_start(0, CLEAR_BOX, count(8, 'clearBox'))
        # START 0x2233 (row 24) and 0x7E4E (PauseOnly) are trapped by the spell oracle already: count them here instead.
        e.traps[ROW_MESSAGE] = count(4, 'rowMessage')
        e.traps[PAUSE] = count(0, 'pause')

    @staticmethod
    def record(rnd):
        b = bytearray(REC)
        b[0:8] = b'\x07MEMBER'
        b[0x10C] = rnd.choice([0, 0, 0, 1, 4, 5])
        b[0x10D] = rnd.choice([0, 1, 1])
        b[0x11B] = rnd.randrange(1, 40)
        b[0x32] = max(b[0x11B], rnd.randrange(1, 60))
        for k in (0x10, 0x11, 0x12, 0x13, 0x14, 0x15):
            b[k] = rnd.randrange(3, 19)
        return b

    def members(self, rnd, n):
        e = self.e
        out = []
        for _ in range(n):
            size = rnd.choice([0, 1, 1, 2, 3, 4, 5, 6, 6])
            ptrs = self.build([{'record': self.record(rnd).hex(), 'effects': [], 'items': []} for _ in range(size)])
            outside = e.alloc(REC)
            r = rnd.random()
            if size and r < 0.8:
                cur = rnd.randrange(size)
            elif r < 0.9:
                cur = -1
            else:
                cur = -2
            e.set_dsptr(0x5D92, ptrs[cur] if cur >= 0 else (0, 0) if cur == -1 else outside)
            key = rnd.choice([0x47, 0x47, 0x4F, 0x4F, 0x48, 0x50, 0x20, 0x49, 0x3B, 0x00])
            if cur == -1 and key == 0x4F and size:
                key = 0x47  # End with no current member reads the link at 0000:0104 (the interrupt table)
            e.call(SELECT, [key])
            got = e.dsptr(0x5D92)
            after = ptrs.index(got) if got in ptrs else -1 if got == (0, 0) else -2
            out.append({'size': size, 'current': cur, 'key': key, 'after': after})
        return out

    def temple(self, rnd, n):
        e = self.e
        out = []
        for _ in range(n):
            ids = rnd.sample(DISEASE + [33, 55, 15, 22, 1, 8], rnd.randrange(0, 7))
            if rnd.random() < 0.3:
                ids.append(rnd.choice(DISEASE))  # a second record of one id
            effects = []
            for i in ids:
                minutes = rnd.choice([0, 1, 10, 60, 14400])
                effects.append(bytes([i, minutes & 0xFF, minutes >> 8, rnd.randrange(256), rnd.choice([0, 1, 1])]).hex())
            rec = self.record(rnd)
            ptrs = self.build([{'record': rec.hex(), 'effects': effects, 'items': []}])
            e.set_dsptr(0x5D92, ptrs[0])
            e.set_dsb(0x49F3, 1)
            e.set_dsb(0x681F, 0)
            answers = [rnd.choice([ord('Y'), ord('Y'), ord('N')]), rnd.choice([ord('Y'), ord('Y'), ord('N')])]
            self.answers = list(answers)
            self.counts = {}
            seed = rnd.getrandbits(32)
            e.set_randseed(seed)
            e.call(TEMPLE_CURE1)
            out.append({'record': rec.hex(), 'effects': effects, 'answers': answers, 'seed': seed,
                        'asked': self.counts.get('cureAnyway', 0), 'paid': self.counts.get('pay', 0),
                        'lock': e.dsb(0x681F), 'seedAfter': e.randseed(),
                        'after': self.read_creature(ptrs[0])})
        return out

    def identify(self, rnd, n):
        e = self.e
        out = []
        for _ in range(n):
            rec = self.record(rnd)
            for k in range(7):
                struct.pack_into('<H', rec, 0x88 + 2 * k, rnd.choice([0, 0, 0, 1, 10, 199, 200, 1000, 40000, 65535]))
            item = bytearray(ITEM)
            item[0x2E] = rnd.randrange(1, 100)
            item[0x35] = rnd.choice([0, 1, 1])
            item[0x3A] = rnd.randrange(256)
            ptrs = self.build([{'record': rec.hex(), 'effects': [], 'items': [item.hex()]}])
            pool = [rnd.choice([0, 0, 1, 50, 199, 200, 5000, 70000, 0x80000000]) for _ in range(7)]
            e.wb(e.ds(0x67F4), struct.pack('<7I', *pool))
            e.set_dsptr(0x5D92, ptrs[0])
            e.set_dsptr(0x4716, ptrs[0])
            o, s = struct.unpack('<HH', e.rb(self.lin(ptrs[0]) + 0xC8, 4))
            flag = e.alloc(2)
            e.wb(self.lin(flag), b'\x00')
            ans = rnd.choice([ord('Y'), ord('Y'), ord('N')])
            self.answers = [ans]
            self.counts = {}
            e.call(IDENTIFY, [s, o, flag[0], flag[1]])
            after = self.read_creature(ptrs[0])
            out.append({'record': rec.hex(), 'item': bytes(item).hex(), 'pool': pool, 'answer': ans,
                        'after': after['record'], 'itemAfter': after['items'][0],
                        'poolAfter': list(struct.unpack('<7I', e.rb(e.ds(0x67F4), 28))),
                        'redraw': e.rb(self.lin(flag), 1)[0], 'counts': dict(self.counts)})
        return out


def main(argv):
    if len(argv) != 3:
        print(__doc__, file=sys.stderr)
        return 2
    o = VenueOracle(argv[1])
    rnd = random.Random(4949)
    data = {'members': o.members(rnd, 400), 'temple': o.temple(rnd, 300), 'identify': o.identify(rnd, 300)}
    with open(argv[2], 'w') as f:
        json.dump(data, f, indent=0)
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
