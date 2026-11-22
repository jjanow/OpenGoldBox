#!/usr/bin/env python3
"""The cast routine 0x27C38 and the scroll spell lists (0x275B4 → 0x27466 / 0x27534) run from the program itself
(por_emu.py, unicorn), for GoldBox.Core's CastProgramTests (docs/re/spells-effects.md "Cast", item-format.md).

Usage: python3 -I por_spellcast_oracle.py GAMEDIR OUT.json        (GAMEDIR: the PoR v1.3 files; needs unicorn 2.x)

Cases (fixed seeds, the same output every run):
- cast: 0x27C38(spell, ai, announce, var done) in and out of a fight, DS:6DBF 0 / 1, with the target pick (DS:6B84
  entry 0), the spell's routine (DS:6B84 + 4·spell), the yes/no 0x2D9A0, the cast message 0x2A332, the invisibility
  removal 0x2BEBC and the drawing / sound routines trapped and recorded in call order; the pick's results and the
  answers are scripted. START 0x202B (the memorised copy removed) runs for real.
- list: 0x275B4(mode) for modes 2 (DS:6B80 = one scroll), 3 and 6 (every readable item, 0x27534) on random items
  and readers: DS:476A (the spells returned by the list), DS:477F (the row → scroll table), DS:4843, the items'
  hidden byte 0x35 afterwards. The row routine 0x272A5 runs for real (heap blocks from the emulator's arena).
The output holds record bytes made up here and the program's spell numbers; it goes in tests/fixtures-private.
"""
import json
import os
import random
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from por_ai_oracle import AiOracle, ptr_bytes  # noqa: E402
from por_emu import Emu  # noqa: E402

CAST, LIST, YESNO, CASTMSG, INVIS = 0x27C38, 0x275B4, 0x2D9A0, 0x2A332, 0x2BEBC


class CastOracle(AiOracle):
    def __init__(self, gamedir):
        super().__init__(gamedir)
        e = self.e
        self.log = []
        self.picks = []
        self.answers = []
        # stubs for the two procedure-variable targets: a retf each, trapped
        self.pick_stub = e.alloc(16)
        self.routine_stub = e.alloc(16)
        e.wb(self.lin(self.pick_stub), b'\xcb' * 16)
        e.wb(self.lin(self.routine_stub), b'\xcb' * 16)
        e._trap(self.lin(self.pick_stub), self._pick)
        e._trap(self.lin(self.routine_stub), self._routine)
        e.trap_ovr(YESNO, self._yesno)
        e.trap_ovr(CASTMSG, self._rec('castmsg', 0xA, lambda em: em.arg(0) & 0xFF))
        e.trap_ovr(INVIS, self._rec('invis', 4))
        e.trap_start(0x709, 0x626, self._rec('print', 0xA, lambda em: (em.arg(8) & 0xFF, em.arg(6) & 0xFF, em.arg(4) & 0xFF)))
        e.trap_start(0xBA, 0x1693, self._rec('rowmsg', 4))       # START 0x2233
        e.trap_start(0xBA, 0x17E3, self._rec('end', 0))          # START 0x2383
        e.trap_start(0x3F1, 0x137, self._rec('clear', 8, lambda em: (em.arg(6), em.arg(4), em.arg(2), em.arg(0))))
        e.trap_start(0xBA, 0x19DE, self._rec('icon', 2))         # START 0x257E
        e.trap_ovr(0x31321, self._rec('posx', 4, ret=3))
        e.trap_ovr(0x31349, self._rec('posy', 4, ret=4))
        e.trap_ovr(0x300CD, self._rec('cone', 0xA, ret=1))
        e.trap_ovr(0x31232, self._rec('pose', 0xA, lambda em: (em.arg(4) & 0xFF, em.arg(2) & 0xFF, em.arg(0) & 0xFF)))
        e.trap_start(0x8AE, 0x1A5, self._rec('delay', 2, lambda em: em.arg(0)))
        e.trap_start(0x8AE, 0xDC, self._rec('sound', 2))
        e.trap_start(0xBA, 0x1A29, self._rec('flight', 0xC))     # START 0x25C9
        e.trap_ovr(0x30E54, self._rec('visible', 6, ret=1))
        self.base_free = None

    def _rec(self, name, nbytes, detail=None, ret=None):
        def f(em):
            self.log.append([name, detail(em)] if detail else [name])
            em.pop_ret(nbytes, ret)
        return f

    def _pick(self, em):
        # entry 0 of DS:6B84 (0x148BE in a fight, 0x27AA9 outside): (spell, ai, var done)
        got = self.picks.pop(0) if self.picks else 0
        self.log.append(['pick', em.arg(6) & 0xFF, em.arg(4) & 0xFF])
        em.wb(em.arg(2) * 16 + em.arg(0), [got])
        em.pop_ret(8)

    def _routine(self, em):
        self.log.append(['routine', em.dsb(0x681B)])
        em.pop_ret(0)

    def _yesno(self, em):
        n = em.rb(em.arg(8) * 16 + em.arg(6))[0]
        a = self.answers.pop(0) if self.answers else ord('Y')  # out of script: Yes (ends the abort loop)
        self.log.append(['yesno', n, em.arg(4) & 0xFF, em.arg(2) & 0xFF, em.arg(0) & 0xFF, chr(a)])
        em.pop_ret(0xA, a)


def cast_cases(o, rnd, n):
    e = o.e
    table = [o.spell(s) for s in range(68)]
    combat_only = [s for s in range(1, 68) if table[s][7] == 0]
    outside = [s for s in range(1, 68) if table[s][7] != 0]
    out = []
    for i in range(n):
        o.fresh()
        fight = rnd.random() < 0.5
        spell = rnd.choice(combat_only if (not fight and rnd.random() < 0.5) else outside if not fight else range(1, 68))
        ai = rnd.randrange(2)
        announce = rnd.randrange(2)
        item = 1 if rnd.random() < 0.35 else 0
        picks = [rnd.choice([0, 1]) for _ in range(3)]
        answers = [ord(rnd.choice('YN')) for _ in range(3)]
        mem = [rnd.choice([0, spell, spell, rnd.randrange(1, 56), spell | 0x80]) for _ in range(21)]
        ptrs = o.creatures([{'size': 1, 'inplay': 1, 'hostile': 0}])
        caster = ptrs[0]
        e.wb(o.lin(caster) + 0x17, bytes(mem))
        cr = o.combat_rec(caster)
        e.wb(cr + 9, [rnd.randrange(8)])
        e.set_dsptr(0x5D92, caster)
        e.set_dsb(0x49F3, 5 if fight else rnd.choice([2, 3, 4]))
        e.set_dsb(0x6DBF, item)
        e.set_dsb(0x681B, 0x77)
        e.set_dsb(0x6820, 0x77)
        e.set_dsptr(0x6B84, o.pick_stub)
        e.set_dsptr(0x6B84 + 4 * spell, o.routine_stub)
        o.log.clear()
        o.picks = list(picks)
        o.answers = list(answers)
        done = e.alloc(2)
        start_done = rnd.randrange(2)
        e.wb(o.lin(done), [start_done])
        e.call(CAST, [spell, ai, announce, *done])
        out.append({
            'spell': spell, 'ai': ai, 'announce': announce, 'item': item, 'fight': int(fight),
            'target': table[spell][7], 'picks': picks, 'answers': ''.join(map(chr, answers)), 'done0': start_done,
            'memorised': bytes(mem).hex(),
            'done': e.rb(o.lin(done))[0], 'after': e.rb(o.lin(caster) + 0x17, 21).hex(),
            'ds681b': e.dsb(0x681B), 'ds6820': e.dsb(0x6820), 'log': list(o.log),
        })
    return out


def list_cases(o, rnd, n):
    e = o.e
    base = o.base_items
    scroll_types = [t for t in range(128) if 11 <= base[16 * t] <= 13]
    other_types = [t for t in range(128) if base[16 * t] not in (11, 12, 13)]
    out = []
    for i in range(n):
        o.fresh()
        reader = {'size': 1, 'inplay': 1, 'hostile': 0, 'cleric': rnd.choice([0, 0, 1, 3, 0xFF, 0x80]),
                  'effects': [0x10] if rnd.random() < 0.25 else rnd.choice([[], [5]])}
        ptrs = o.creatures([reader])
        user = ptrs[0]
        recs = []
        for _ in range(rnd.randint(1, 5)):
            b = bytearray(63)
            b[0x2E] = rnd.choice(scroll_types if rnd.random() < 0.7 else other_types)
            b[0x35] = rnd.choice([0, 0, 1])
            for k in range(3):
                b[0x3C + k] = rnd.choice([0, 0, rnd.randrange(1, 56), rnd.randrange(1, 56) | 0x80, 0x80])
            recs.append(b)
        iptrs = [e.alloc(63) for _ in recs]
        for j, (p, r) in enumerate(zip(iptrs, recs)):
            r[0x2A:0x2E] = ptr_bytes(iptrs[j + 1] if j + 1 < len(iptrs) else None)
            e.wb(o.lin(p), bytes(r))
        e.wb(o.lin(user) + 0xC8, ptr_bytes(iptrs[0]))
        e.set_dsptr(0x5D92, user)
        mode = rnd.choice([2, 2, 3, 6])
        which = rnd.randrange(len(recs))
        e.set_dsptr(0x6B80, iptrs[which] if mode == 2 else (0, 0))
        e.set_dsptr(0x4766, (0, 0))
        e.wb(e.ds(0x476A), b'\xee' * 21)
        e.wb(e.ds(0x477F), b'\xee' * 4 * 49)
        start_count = rnd.randrange(4)
        e.set_dsb(0x4843, start_count)
        ax, _ = e.call(LIST, [mode])
        # count the rows of the built list (DS:4766, next at +0x2A, +0x29 = heading)
        rows = []
        p = e.dsptr(0x4766)
        while p != (0, 0) and len(rows) < 80:
            rows.append(e.rb(o.lin(p) + 0x29)[0])
            o2, s2 = struct.unpack('<HH', e.rb(o.lin(p) + 0x2A, 4))
            p = (s2, o2)
        spells = sum(1 for h in rows if h == 0)
        index = {p: j for j, p in enumerate(iptrs)}
        table = []
        for k in range(e.dsb(0x4843) if mode != 2 else 0):
            o3, s3 = struct.unpack('<HH', e.rb(e.ds(0x477F + 4 * k), 4))
            table.append(index.get((s3, o3), -1))
        out.append({
            'mode': mode, 'which': which, 'cleric': reader['cleric'], 'effects': reader['effects'],
            'items': [bytes(r).hex() for r in recs], 'slots': [base[16 * r[0x2E]] for r in recs],
            'count0': start_count, 'found': ax & 0xFF, 'headings': rows,
            'spells': list(e.rb(e.ds(0x476A), min(spells, 21))), 'count': e.dsb(0x4843), 'table': table,
            'hidden': [e.rb(o.lin(p) + 0x35)[0] for p in iptrs], 'b6b80': list(e.dsptr(0x6B80)) != [0, 0],
        })
    return out


def main(argv):
    if len(argv) != 3:
        print(__doc__)
        return 2
    o = CastOracle(argv[1])
    rnd = random.Random(20261122)
    out = {'source': 'GAME.OVR / START.EXE v1.3 run under unicorn (tools/scripts/por_spellcast_oracle.py)'}
    out['cast'] = cast_cases(o, rnd, 300)
    out['list'] = list_cases(o, rnd, 200)
    with open(argv[2], 'w') as f:
        json.dump(out, f, separators=(',', ':'))
    print('cast %d, list %d' % (len(out['cast']), len(out['list'])))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
