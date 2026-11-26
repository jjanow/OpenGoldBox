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
- lootTake (s52): Take 0x62B6 and the item take 0x6184 for real, the menu, coin take, list routine and copy step
  answered by the script (see loot_take).
- award (s52): the after-combat award 0x58D3 on the list DS:5D96 with a longint share.
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
# The heal list's entries 0..8 (0x4978 takes the dice count 1..3 as a byte argument).
TEMPLE_ENTRIES = [(0x47C0, None), (0x4864, None), (0x4978, 1), (0x4978, 2), (0x4978, 3), (0x4CC8, None),
                  (0x4A87, None), (0x4DB1, None), (0x4E9F, None)]
MESSAGE, EFFECT_MESSAGE = 0x228C, 0x2B8B   # START 0x16EC, 0x2B8B as linear addresses (seg 0)
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
        e.traps[MESSAGE] = count(12, 'message')
        e.traps[EFFECT_MESSAGE] = count(10, 'effectMessage')

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

    def temple_entries(self, rnd, n):
        """Every heal-list entry on a made-up character: statuses, Con, levels and rolled HP spread over the raise
        dead branches, effect records (55, 22, 15, 32 among them, doubles, byte 4 set or not), items cursed or not."""
        e = self.e
        out = []
        pool_ids = [33, 55, 55, 22, 15, 15, 32, 36, 31, 34, 43, 44, 50, 57, 1, 8]
        for i in range(n):
            entry = [0, 1, 2, 3, 4, 5, 5, 5, 6, 6, 6, 7, 8][i % 13]
            rec = self.record(rnd)
            rec[0x10C] = rnd.choice([0, 0, 1, 4, 5, 6, 6, 7])
            if rec[0x10C] in (4, 5, 6):
                rec[0x10D] = rnd.choice([0, 0, 1])
                rec[0x11B] = rnd.choice([0, 0, 1, 5])
            rec[0x14] = rnd.choice([0, 3, 12, 13, 14, 15, 16, 17, 18, 19])
            rec[0xB1] = rnd.choice([0, rnd.randrange(1, 40), rec[0x32], min(255, rec[0x32] + 5)])
            for k in range(8):
                rec[0x96 + k] = rnd.choice([0, 0, 0, 0, 1, 2, 5, 9, 0xFF, 0x80]) if rnd.random() < 0.5 else 0
            ids = [rnd.choice(pool_ids) for _ in range(rnd.randrange(0, 6))]
            if entry == 5 and i % 39 == 5:
                ids = rnd.choice([[55, 22, 15, 15], [55, 55, 22, 15], [22, 15, 15, 55]])  # handler 22's own lock
            effects = []
            for x in ids:
                minutes = rnd.choice([0, 1, 10, 60, 14400])
                on_remove = 1 if entry == 5 and i % 39 == 5 else rnd.choice([0, 1, 1])
                effects.append(bytes([x, minutes & 0xFF, minutes >> 8, rnd.randrange(256), on_remove]).hex())
            items = []
            for _ in range(rnd.randrange(0, 4)):
                it = bytearray(ITEM)
                it[0x2E] = rnd.randrange(1, 100)
                it[0x36] = rnd.choice([0, 0, 1, 0x80])
                items.append(it.hex())
            ptrs = self.build([{'record': rec.hex(), 'effects': effects, 'items': items}])
            e.set_dsptr(0x5D92, ptrs[0])
            e.set_dsb(0x49F3, 1)
            e.set_dsb(0x681F, 0)
            e.set_dsptr(0x6C95, (0, 0))
            answers = [rnd.choice([ord('Y'), ord('Y'), ord('Y'), ord('N')]) for _ in range(2)]
            self.answers = list(answers)
            self.counts = {}
            seed = rnd.getrandbits(32)
            e.set_randseed(seed)
            where, arg = TEMPLE_ENTRIES[entry]
            e.call(where, [arg] if arg is not None else [])
            out.append({'entry': entry, 'record': rec.hex(), 'effects': effects, 'items': items, 'answers': answers,
                        'seed': seed, 'counts': dict(self.counts), 'lock': e.dsb(0x681F), 'seedAfter': e.randseed(),
                        'after': self.read_creature(ptrs[0])})
        return out

    def loot(self, rnd, n):
        """The loot menu 0x644B with the menu routine 0x2CC8C answered by the script: each pass records the item line
        the routine was given and the key returned (a hotkey of that line, or Home / End as the keypad's extended
        0x47 / 0x4F); D runs the cast 0x27C38 for real (its arguments recorded at entry). View, Take, Pool, Share, the
        redraws and the exit question's texts are trapped; the exit question (0x8B57) answers 1 (No), so E ends."""
        e = self.e
        out = []
        state = {}

        def menu(em):
            s, o = em.arg(16), em.arg(14)
            line = em.rb(s * 16 + o + 1, em.rb(s * 16 + o)[0]).decode('latin-1')
            words = line.split()
            passes = state['passes']
            if passes >= state['limit']:
                key, ext = ord('E'), 0
            else:
                r = state['rnd'].random()
                if r < 0.15:
                    key, ext = state['rnd'].choice([0x47, 0x4F]), 1
                else:
                    key, ext = ord(state['rnd'].choice(words)[0].upper()), 0
            state['passes'] = passes + 1
            state['log'].append({'line': line, 'key': key, 'ext': ext})
            em.wb(em.arg(2) * 16 + em.arg(0), [ext])
            em.pop_ret(22, key)

        def cast(em):
            state['log'].append({'cast': [em.arg(8) & 0xFF, em.arg(6) & 0xFF, em.arg(4) & 0xFF]})

        def noop(nbytes, name):
            def f(em):
                state['log'].append({'call': name})
                em.pop_ret(nbytes)
            return f

        e.trap_ovr(0x2CC8C, menu)
        e.trap_ovr(0x27C38, cast)
        e.trap_ovr(0x21AED, noop(4, 'view'))
        e.trap_ovr(0x62B6, noop(8, 'take'))
        e.trap_ovr(0x25191, noop(0, 'pool'))
        e.trap_ovr(0x252C3, noop(0, 'share'))
        e.trap_ovr(0x8B57, lambda em: em.pop_ret(18, 1))
        e.trap_start(0x709, 0x768, Emu.ret(16))
        e.trap_start(0x709, 0xDD3, Emu.ret(8))
        e.trap_start(0xBA, 0x27D9, Emu.ret(0))
        e.trap_start(0xBA, 0x767, Emu.ret(4))
        spells = [5, 11, 5, 11, 0x85, 0x8B, 1, 3, 15, 0, 0, 0]
        for _ in range(n):
            size = rnd.randint(1, 4)
            members = []
            for _ in range(size):
                rec = self.record(rnd)
                rec[0x10C], rec[0x10D] = 0, 1
                rec[0x17:0x2C] = bytes(rnd.choice(spells) if rnd.random() < 0.4 else 0 for _ in range(21))
                for k in range(8):
                    rec[0x96 + k] = rnd.choice([0, 0, 1, 3, 7, 12])
                effects = []
                if rnd.random() < 0.2:
                    effects.append(bytes([5, 10, 0, 1, 0]).hex())
                members.append({'record': rec.hex(), 'effects': effects, 'items': []})
            ptrs = self.build(members)
            cur = rnd.randrange(size)
            e.set_dsptr(0x5D92, ptrs[cur])
            pool = [0] * 7 if rnd.random() < 0.4 else [rnd.choice([0, 0, 0, 5, 100]) for _ in range(7)]
            e.wb(e.ds(0x67F4), struct.pack('<7I', *pool))
            items = rnd.random() < 0.8
            e.set_dsptr(0x6810, e.alloc(ITEM) if items else (0, 0))
            e.set_dsb(0x49F3, 6)
            e.set_dsb(0x6DBF, 0)
            e.set_dsb(0x681F, 0)
            e.set_dsptr(0x6DB5, (0, 0))
            seed = rnd.getrandbits(32)
            e.set_randseed(seed)
            state.update(passes=0, limit=rnd.randint(1, 8), rnd=random.Random(rnd.getrandbits(32)), log=[])
            self.counts = {}
            e.call(0x644B)
            out.append({'members': members, 'current': cur, 'pool': pool, 'items': int(items), 'seed': seed,
                        'log': list(state['log']), 'seedAfter': e.randseed(), 'counts': dict(self.counts),
                        'currentAfter': ptrs.index(e.dsptr(0x5D92)) if e.dsptr(0x5D92) in ptrs else -1,
                        'after': [self.read_creature(p) for p in ptrs]})
        return out

    def loot_take(self, rnd, n):
        """Take 0x62B6 and the item take 0x6184 (with its page 0x6092) run for real on made-up pools and treasure
        lists (0–4 items, linked at +0x2A from DS:6810). Answered by the script and logged in order: the Take: menu
        0x2CC8C (M, I, E, 0, Home / End, others), the coin take 0x25974 (it may empty the pool), the list routine
        0x2D669 on a non-empty list (a key T, Return or another, and a row; on the empty list it runs as it is), the
        copy step 0x6F0D (refused or not). Logged as well: the window START 0x41F8, the redraw START 0x3379, the party
        list START 0x1307, the selection 0x23819 and FreeMem. Out: the log, the list left (indices) and DS:6B2B."""
        e = self.e
        state = {}
        log = lambda x: state['log'].append(x)  # noqa: E731

        def menu(em):
            s, o = em.arg(16), em.arg(14)
            line = em.rb(s * 16 + o + 1, em.rb(s * 16 + o)[0]).decode('latin-1')
            r = state['rnd']
            if state['menus'] >= 6:
                key, ext = ord('E'), 0
            else:
                key, ext = r.choice([(ord('M'), 0), (ord('I'), 0), (ord('I'), 0), (ord('M'), 0), (ord('E'), 0),
                                     (0, 0), (0x47, 1), (0x4F, 1), (0x50, 1), (0x20, 0)])
            state['menus'] += 1
            log({'menu': line, 'key': key, 'ext': ext})
            em.wb(em.arg(2) * 16 + em.arg(0), [ext])
            em.pop_ret(22, key)

        def coins(em):
            r = state['rnd']
            empty = r.random() < 0.5
            log({'coins': int(empty)})
            if empty:
                em.wb(em.ds(0x67F4), bytes(28))
            em.pop_ret(0)

        def items_list():
            out = []
            p = e.dsptr(0x6810)
            while p != (0, 0) and len(out) < 16:
                out.append(state['items'].index(p) if p in state['items'] else -1)
                o, s = struct.unpack('<HH', e.rb(self.lin(p) + 0x2A, 4))
                p = (s, o)
            return out

        def page(em):
            # 0x2D669 (retf 0x28): [bp+0x14] the list head (args 14 / 16), [bp+0xA] var position (4 / 6), [bp+6] var
            # item (0 / 2), from 0x6092's push list.
            o, s = em.arg(14), em.arg(16)
            if (s, o) == (0, 0):
                log({'page': 'empty', 'b6b2b': em.dsb(0x6B2B)})
                return  # runs as it is
            r = state['rnd']
            rows = items_list()
            row = r.randrange(len(rows))
            key = r.choice([ord('T'), ord('T'), 13, ord('T'), 0, ord('E'), 0x20])
            p = e.dsptr(0x6810)
            for _ in range(row):
                lo, ls = struct.unpack('<HH', e.rb(self.lin(p) + 0x2A, 4))
                p = (ls, lo)
            log({'page': rows, 'key': key, 'row': row})
            # var item: [bp+6] = args 0 (offset) and 2 (segment); var position: [bp+0xA] = args 4 / 6.
            em.wb(em.arg(2) * 16 + em.arg(0), struct.pack('<HH', p[1], p[0]))
            em.wb(em.arg(6) * 16 + em.arg(4), struct.pack('<H', row))
            em.pop_ret(0x28, key)

        def copy(em):
            item = (em.arg(6), em.arg(4))
            refused = int(state['rnd'].random() < 0.3)
            log({'copy': state['items'].index(item) if item in state['items'] else -1, 'refused': refused})
            em.wb(em.arg(2) * 16 + em.arg(0), [refused])
            em.pop_ret(8)

        def select(em):
            log({'select': em.arg(0) & 0xFF})
            em.pop_ret(2)

        def free(em):
            p = (em.arg(4), em.arg(2))
            log({'free': state['items'].index(p) if p in state['items'] else -1, 'size': em.arg(0)})
            em.pop_ret(6)

        def note(nbytes, name):
            def f(em):
                log({'call': name})
                em.pop_ret(nbytes)
            return f

        e.trap_ovr(0x2CC8C, menu)
        e.trap_ovr(0x25974, coins)
        e.trap_ovr(0x2D669, page)
        e.trap_ovr(0x6F0D, copy)
        e.trap_ovr(0x23819, select)
        e.traps[0xB1D4] = free
        e.trap_start(0, 0x41F8, note(16, 'window'))
        e.trap_start(0, 0x3379, note(0, 'redraw'))
        e.trap_start(0, 0x1307, note(4, 'party'))
        out = []
        for i in range(n):
            e.reset_heap()
            pool = [0] * 7 if rnd.random() < 0.4 else [rnd.choice([0, 0, 0, 5, 100]) for _ in range(7)]
            e.wb(e.ds(0x67F4), struct.pack('<7I', *pool))
            count = 0 if i % 5 == 0 else rnd.randint(0, 4)
            items = [e.alloc(ITEM) for _ in range(count)]
            for a, b in zip(items, items[1:] + [(0, 0)]):
                e.wb(self.lin(a) + 0x2A, struct.pack('<HH', b[1], b[0]))
            e.set_dsptr(0x6810, items[0] if items else (0, 0))
            e.set_dsptr(0x5D92, e.alloc(REC))
            b6b2b = rnd.choice([0, 1, 2, 5])
            e.set_dsb(0x6B2B, b6b2b)
            flags = e.alloc(4)
            money, have = int(any(pool)), int(bool(items))
            e.wb(self.lin(flags), bytes([money, 0, have, 0]))
            state.update(log=[], menus=0, items=items, rnd=random.Random(rnd.getrandbits(32)))
            self.counts = {}
            fs, fo = flags
            e.call(0x62B6, [fs, fo, fs, fo + 2])
            out.append({'pool': pool, 'items': count, 'b6b2b': b6b2b, 'log': list(state['log']),
                        'left': items_list(), 'b6b2bAfter': e.dsb(0x6B2B),
                        'poolAfter': list(struct.unpack('<7I', e.rb(e.ds(0x67F4), 28))),
                        'counts': dict(self.counts)})
        return out

    def award(self, rnd, n):
        """The after-combat award 0x58D3(share: longint) over the list DS:5D96 of 1–8 made-up records: class byte 0x2F
        (0..17, sometimes 0x80+), the four prime scores, in-play byte 0x10D, status 0x10C and experience 0xAC varied.
        Out: the records, the share and the records after."""
        e = self.e
        out = []
        for _ in range(n):
            recs = []
            for _ in range(rnd.randint(1, 8)):
                rec = self.record(rnd)
                rec[0x2F] = rnd.choice(list(range(18)) + [0x80, 0xFF])
                for k in (0x10, 0x11, 0x12, 0x13):
                    rec[k] = rnd.choice([3, 9, 15, 16, 18, 25])
                rec[0x10D] = rnd.choice([0, 1, 1, 2])
                rec[0x10C] = rnd.choice([0, 0, 1, 4, 6])
                struct.pack_into('<I', rec, 0xAC, rnd.choice([0, 1, 999, 250000, 0xFFFFFF00]))
                recs.append({'record': rec.hex(), 'effects': [], 'items': []})
            ptrs = self.build(recs)
            share = rnd.choice([0, 1, 9, 10, 37, 1000, 65535, 70001, 0x7FFFFFF0, -5 & 0xFFFFFFFF])
            e.call(0x58D3, [share >> 16, share & 0xFFFF])
            out.append({'records': [r['record'] for r in recs], 'share': share,
                        'after': [self.read_creature(p)['record'] for p in ptrs]})
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
    data['templeEntries'] = o.temple_entries(random.Random(5151), 1300)
    data['loot'] = o.loot(random.Random(6605), 400)
    data['lootTake'] = VenueOracle(argv[1]).loot_take(random.Random(6184), 400)  # its own traps: a fresh program
    data['award'] = o.award(random.Random(0x58D3), 400)
    with open(argv[2], 'w') as f:
        json.dump(data, f, indent=0)
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
