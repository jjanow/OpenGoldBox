#!/usr/bin/env python3
"""The camp unit's spell and party-order routines (GAME.OVR 0x17831-0x1980E) run by the program itself (por_emu.py,
unicorn) on generated characters, for GoldBox.Core's CampProgramTests (docs/re/camp.md).

Usage: python3 -I por_camp_oracle.py GAMEDIR OUT.json        (GAMEDIR: the PoR v1.3 files; needs unicorn 2.x)

Characters are the game's own CHRDATA1-6.SAV records with spell slots, the spell book, memorised and pending slots,
status and items made up here (scroll-type items, whose base slot is 11..13, with spell bytes 0x3C..0x3E, pending bit
7, hidden flag 0x35). The set-up and the traps are por_items_oracle.py's (drawing, naming, GetMem from the arena, the
row-24 message START 0x2233 logged); also trapped: the member message START 0x228C (logged), the screen redraw START
0x3379, the spell list 0x239D6 (logged: type, action; answered from the record as the list would be: empty or not, a
pick or 0) and the yes/no 0x2D9A0 (Y or N, logged). Messages are logged by the code-segment offset of the text the
routine copied (START 0xBA8B / 0xBAA5 from CS), never the text. Kinds:
- prep 0x17831(rec) (AX = minutes), resetMem 0x1798A(rec), resetScribe 0x179CD(rec), sort 0x18055 (current),
  slots 0x17A8D(level, class) (current; AL), check 0x17B9D(activity) (current, VM $49E5 through [DS:49D2]+0x1CA; AL);
- resetParty 0x17A45, moveUp 0x18CDA, moveDown 0x18E32 on a party list (DS:5D96, links +0x104; current DS:5D92);
- memorize 0x1813B and scribe 0x1838C whole (with 0x17B9D, 0x17DD5, 0x17A8D, 0x18055, 0x1798A, 0x179CD, 0x2A255).
Out: the records and items before and after, the list order after, AX, the log. Records are made up here; the output
goes in tests/fixtures-private.
"""
import json
import os
import random
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from por_emu import Emu  # noqa: E402
import por_items_oracle as io  # noqa: E402

PREP, RESET_MEM, RESET_SCRIBE, RESET_PARTY = 0x17831, 0x1798A, 0x179CD, 0x17A45
SLOTS, CHECK, SORT, MEMORIZE, SCRIBE = 0x17A8D, 0x17B9D, 0x18055, 0x1813B, 0x1838C
MOVE_UP, MOVE_DOWN = 0x18CDA, 0x18E32
SPELL_LIST = 0x239D6            # (type, action, var w, var b), retf 0xC
MEMBER_MESSAGE, REDRAW, COPY, COPY_N = (0xBA, 0x16EC), (0xBA, 0x27D9), (0xAF8, 0xB0B), (0xAF8, 0xB25)
REC, ITEM = io.REC, io.ITEM
UNIT_CS = 0x17831               # the camp unit's code starts at its file offset


class CampOracle(io.ItemsOracle):
    def __init__(self, gamedir):
        super().__init__(gamedir)
        e = self.e
        self.scrolls = [t for t in range(1, 128) if self.base_items[16 * t] in (11, 12, 13)]
        self.last_copy = None
        self.ids = {}
        self.picks = 0

        def copy(em):
            # Pascal string copies: the source far pointer is pushed last (arg 0 / 2) for a CS constant
            if em.arg(2) == self.unit_cs:
                self.last_copy = em.arg(0)
        def copy_n(em):
            # StrCopy(src, dst, max): src pushed first (arg 8 / 6), then dst, then the length
            if em.arg(8) == self.unit_cs:
                self.last_copy = em.arg(6)

        e.trap_start(*COPY, copy)
        e.trap_start(*COPY_N, copy_n)

        def member_message(em):
            self.log.append({'message': self.last_copy})
            em.pop_ret(self.ret_member)

        def row_message(em):
            self.log.append({'row': self.last_copy})
            em.pop_ret(4)

        def spell_list(em):
            kind, action = em.arg(10) & 0xFF, em.arg(8) & 0xFF
            w = em.arg(6) * 16 + em.arg(4)
            b = em.arg(2) * 16 + em.arg(0)
            cands = self.candidates(kind)
            pick = 0
            if cands and action != 0 and self.picks < 7 and self.rnd.random() < 0.8:
                pick = self.rnd.choice(cands + [self.rnd.randrange(1, 56)])
                self.picks += 1
            em.wb(b, bytes([1 if cands else 0]))
            if cands:
                em.wb(w, b'\0\0')
            self.log.append({'list': [kind, action], 'nonEmpty': bool(cands), 'pick': pick})
            em.pop_ret(12, pick)

        def yesno(em):
            a = self.rnd.choice('YN')
            self.log.append({'question': self.last_copy, 'answer': a})
            em.pop_ret(self.ret_yesno, ord(a))

        self.ret_member = self.retf(*MEMBER_MESSAGE)
        e.trap_start(*MEMBER_MESSAGE, member_message)
        e.traps[io.ROW_MESSAGE[0] * 16 + io.ROW_MESSAGE[1]] = row_message  # replaces the base class's
        e.trap_start(*REDRAW, Emu.ret(self.retf(*REDRAW)))
        e.trap_ovr(SPELL_LIST, spell_list)
        # replace the base class's yes/no handler (a second hook would run twice)
        e.traps[e.unit_linear(self.ovr_of(*io.YESNO))[0]] = yesno
        self.unit_cs = e.unit_linear(UNIT_CS)[1]

    # ---- the list as the program would build it (only whether it is empty, and what may be picked) ----
    def cur_record(self):
        return self.e.rb(self.lin(self.current), REC)

    def readable(self, it):
        return self.base_items[16 * it[0x2E]] in (11, 12, 13)

    def candidates(self, kind):
        r = self.cur_record()
        items = [self.e.rb(self.lin(p), ITEM) for p in self.chain()]
        if kind == 5:
            return [b & 0x7F for b in r[0x17:0x2C] if b > 0x7F]
        if kind == 0:
            return [b for b in r[0x17:0x2C] if 0 < b < 0x80]
        if kind == 1:
            return [s for s in range(1, 56) if r[0x32 + s]]
        if kind in (3, 6):
            out = []
            for it in items:
                if not self.readable(it) or it[0x35]:
                    continue
                for b in it[0x3C:0x3F]:
                    if (b > 0x80) if kind == 6 else (b > 0):
                        out.append(b & 0x7F)
            return out
        raise ValueError('list kind %d' % kind)

    # ---- generated characters ----
    def spell_record(self, rnd):
        b = self.record(rnd)
        for k in range(1, 7):
            b[0xB1 + k] = rnd.choice([0, 0, 1, 2, 3, 4])
        for s in range(1, 57):
            b[0x32 + s] = rnd.choice([0, 0, 1])
        for i in range(21):
            s = rnd.randrange(1, 57)
            b[0x17 + i] = rnd.choice([0, 0, 0, s, s, s | 0x80])
        b[0x2C] = rnd.randrange(0, 8)
        b[0x10C] = rnd.choice([0, 0, 0, 0, 1, 4])
        b[0x10D] = rnd.choice([1, 1, 1, 1, 0])
        return b

    def spell_item(self, rnd, k):
        b = self.item(rnd, k)
        if rnd.random() < 0.7:
            b[0x2E] = rnd.choice(self.scrolls)
        for j in range(3):
            s = rnd.randrange(1, 57)
            b[0x3C + j] = rnd.choice([0, 0, s, s, s | 0x80])
        b[0x35] = rnd.choice([0, 0, 0, 1])
        return b

    def snapshot(self, members):
        out = []
        for p in members:
            self.current = p
            out.append({'record': self.cur_record().hex(), 'items': [it['bytes'] for it in self.items_of()]})
        return out

    def party_order(self, members):
        idx = {p: i for i, p in enumerate(members)}
        o, s = struct.unpack('<HH', self.e.rb(self.e.ds(0x5D96), 4))
        p, out = (s, o), []
        while p != (0, 0) and len(out) < 10:
            out.append(idx.get(p, -1))
            o, s = struct.unpack('<HH', self.e.rb(self.lin(p) + 0x104, 4))
            p = (s, o)
        return out

    def run_case(self, rnd, kind):
        e = self.e
        e.reset_heap()
        self.log = []
        self.picks = 0
        n = rnd.choice([1, 2, 3, 4, 5, 6]) if kind in ('resetParty', 'moveUp', 'moveDown') else 1
        members = []
        for _ in range(n):
            rec = self.spell_record(rnd)
            items = [self.spell_item(rnd, k) for k in range(rnd.randint(0, 6))]
            p, _ = self.place(rec, items)
            members.append(p)
        for i, p in enumerate(members):
            nxt = members[i + 1] if i + 1 < n else None
            e.wb(self.lin(p) + 0x104, io.ptr_bytes(nxt))
        e.set_dsptr(0x5D96, members[0])
        cur = rnd.randrange(n)
        self.current = members[cur]
        e.set_dsptr(0x5D92, self.current)
        vm = e.alloc(0x200)
        nomagic = rnd.choice([0, 0, 0, 1, 2])
        e.wb(self.lin(vm) + 0x1CA, struct.pack('<H', nomagic))
        e.set_dsptr(0x49D2, vm)
        e.set_dsb(0x49F3, 2)
        before = self.snapshot(members)
        self.current = members[cur]
        args, ax = [], None
        if kind == 'prep':
            ax, _ = e.call(PREP, list(self.current))
        elif kind == 'resetMem':
            e.call(RESET_MEM, list(self.current))
        elif kind == 'resetScribe':
            e.call(RESET_SCRIBE, list(self.current))
        elif kind == 'sort':
            e.call(SORT)
        elif kind == 'slots':
            args = [rnd.choice([1, 2, 3, 1, 2, 3, 0, 4]), rnd.choice([0, 1, 0, 1, 2])]
            ax, _ = e.call(SLOTS, args)
            ax &= 0xFF
        elif kind == 'check':
            args = [rnd.choice([1, 2, 3])]
            ax, _ = e.call(CHECK, args)
            ax &= 0xFF
        elif kind == 'resetParty':
            e.call(RESET_PARTY)
        elif kind == 'moveUp':
            e.call(MOVE_UP)
        elif kind == 'moveDown':
            e.call(MOVE_DOWN)
        elif kind == 'memorize':
            e.call(MEMORIZE)
        elif kind == 'scribe':
            e.call(SCRIBE)
        after = self.snapshot(members)
        o, s = struct.unpack('<HH', e.rb(e.ds(0x5D92), 4))
        return {'kind': kind, 'current': cur, 'noMagic': nomagic, 'args': args, 'ax': ax, 'members': before,
                'after': after, 'order': self.party_order(members), 'currentAfter': members.index((s, o)),
                'log': self.log}


def main(argv):
    if len(argv) != 3:
        print(__doc__, file=sys.stderr)
        return 2
    o = CampOracle(argv[1])
    rnd = random.Random(52075)
    o.rnd = rnd
    plan = (['prep'] * 3 + ['resetMem', 'resetScribe', 'sort'] + ['slots'] * 2 + ['check'] * 2 + ['resetParty']
            + ['moveUp', 'moveDown'] * 2 + ['memorize'] * 4 + ['scribe'] * 4)
    cases = [o.run_case(rnd, plan[i % len(plan)]) for i in range(600)]
    data = {'source': 'GAME.OVR / START.EXE v1.3 run under unicorn (tools/scripts/por_camp_oracle.py)',
            'items_5582': o.base_items.hex(), 'cases': cases}
    with open(argv[2], 'w') as f:
        json.dump(data, f, indent=0)
    for k in sorted(set(plan)):
        cs = [c for c in cases if c['kind'] == k]
        print('%s: %d cases, %d changed, %d log entries' % (
            k, len(cs), sum(1 for c in cs if c['members'] != c['after'] or c['order'] != list(range(len(c['members'])))),
            sum(len(c['log']) for c in cs)))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
