#!/usr/bin/env python3
"""Effect handlers 40, 88, 90 and 97 run by the program itself (v1.3, por_emu.py / unicorn), for GoldBox.Core's
HandlerProgramTests (docs/re/spells-effects.md "Handlers 40, 84, 88, 90, 97 under emulation").

Usage: python3 -I por_handler_oracle.py GAMEDIR OUT.json        (GAMEDIR: the PoR v1.3 files; needs unicorn 2.x)

Same set-up as por_spell_oracle.py (its Oracle class; outside a fight, DS:49F3 = 0), with its own seed and cases:
- cloud: Stinking Cloud cast once or twice on a combat map holding party bodies (DS:66D6 + 7k: far pointer, x, y,
  terrain kept; count DS:6715, an emptied entry all zero), then the first cloud's effect 0x28 removed, which runs
  handler 40 (0xF91E): the cells get back a body's 0x1F or the terrain the cloud covered;
- breath: handler 88 (0x2A0F1) through 0x2AEEA on a combat map, with the round counter DS:6DE3 0 or not and the
  record's param 0xFF / 0xFE / 0xFD / small. The target pick (DS:6B84 entry 0, a procedure variable) is trapped: it
  writes the case's aim into DS:6DB9 / 6DBA (the breather's own square for "no target") and returns true;
- gaze: handler 84 (0x10AD2) on a combat map, its uninitialised range word [bp-2] written at the handler's entry
  (0x3E58 as DOSBox-X showed, small values, 0x8000 + small, 0xEEEE), class-1 / class-0 effects on gazer and target;
- save: the saving throw 0x2BC4D (creature, type, modifier) on carriers of 90 (0x5A), 97 (0x61) or both, with the
  Constitution at every band edge; DS:6816 and the result are recorded. Out of 4..20 the handlers add an
  uninitialised byte (handler BP − 1 = the throw's BP − 0x125): the emulator's stack filler (0xEE) shows there.
The output holds creature bytes made up here (no game data); it is kept with the private fixtures.
"""
import json
import os
import random
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from por_spell_oracle import Oracle, CALL_HANDLER, REMOVE_EFFECT, creature, effect  # noqa: E402
from por_emu import Emu  # noqa: E402

SAVE, GAZE = 0x2BC4D, 0x10AD2
FLOOR, BODY, CLOUD = 23, 0x1F, 0x1E


class HandlerOracle(Oracle):
    def __init__(self, gamedir):
        super().__init__(gamedir)
        self.aim = (0, 0)
        seg, off = self.routines[0]
        self.e.trap_start(seg, off, self._pick)
        self.range_word = None
        self.e.trap_ovr(GAZE, self._gaze_entry)

    def _gaze_entry(self, e):
        """Handler 84's entry: its [bp-2] (SP - 4 before push bp) gets the case's stack residue."""
        if self.range_word is not None:
            e.wb(e.stack() - 4, struct.pack('<H', self.range_word))

    def _pick(self, e):
        """DS:6B84 entry 0 (spell, ai, var picked): the case's aim, picked = 1, true."""
        o, s = e.arg(0), e.arg(2)
        e.wb(s * 16 + o, [1])
        e.set_dsb(0x6DB9, self.aim[0])
        e.set_dsb(0x6DBA, self.aim[1])
        e.pop_ret(8, 1)

    def bodies(self, case, ptrs):
        e = self.e
        bl = case.get('bodies', [])
        for k, (who, x, y, terr) in enumerate(bl, 1):
            p = ptrs[who] if who is not None else (0, 0)
            e.wb(e.ds(0x66D6 + 7 * k), struct.pack('<HH', p[1], p[0]) + bytes([x, y, terr]))
        e.set_dsb(0x6715, len(bl))

    def node(self, c, eid):
        e = self.e
        o, s = struct.unpack('<HH', e.rb(self.lin(c) + 0x7F, 4))
        while (o, s) != (0, 0):
            if e.rb(s * 16 + o, 1)[0] == eid:
                return s, o
            o, s = struct.unpack('<HH', e.rb(s * 16 + o + 5, 4))
        return 0, 0

    def run(self, case):
        e = self.e
        if case['kind'] == 'save':
            ptrs = self.build(case['creatures'])
            self.map_mode = False
            e.set_dsb(0x49F3, 0)
            e.set_dsptr(0x5D92, ptrs[0])
            e.set_randseed(case['seed'])
            c = ptrs[0]
            al, _ = e.call(SAVE, [c[0], c[1], case['type'], case['mod'] & 0xFF])
            return {'after': [self.read_creature(p) for p in ptrs], 'seedAfter': e.randseed(),
                    'saved': al & 0xFF, 'roll': e.dsb(0x6816)}
        ptrs = self.build(case['creatures'])
        self.map_mode = True
        arena = self.setup_map(case, ptrs)
        self.bodies(case, ptrs)
        e.set_dsb(0x49F3, 0)
        e.set_dsb(0x681F, 0)
        e.set_dsb(0x6DBF, 0)
        e.set_dsb(0x6819, 0)
        e.set_dsb(0x6823, 0)
        e.set_dsb(0x6DE3, case.get('round', 0))
        e.set_dsptr(0x5D92, ptrs[case['current']])
        e.set_randseed(case['seed'])
        if case['kind'] == 'cloud':
            for cur, (ax, ay) in case['casts']:
                e.set_dsptr(0x5D92, ptrs[cur])
                e.set_dsb(0x681B, 0x22)
                e.set_dsb(0x6DB9, ax)
                e.set_dsb(0x6DBA, ay)
                e.call(self.routines[0x22])
            if 'removeFrom' in case:
                c = ptrs[case['removeFrom']]
                e.call(REMOVE_EFFECT, [c[0], c[1], 0x28, 0, 0])
        else:  # breath, gaze
            c = ptrs[case['creature']]
            hid = case['id']
            self.aim = tuple(case.get('pick', (0, 0)))
            self.range_word = case.get('range')
            if 'other' in case:
                cb = struct.unpack('<HH', e.rb(self.lin(c) + 0x108, 4))
                tp = ptrs[case['other']]
                e.wb(cb[1] * 16 + cb[0] + 0x0A, struct.pack('<HH', tp[1], tp[0]))
            e.set_dsb(0x681B, 0)
            e.set_dsb(0x6DB9, 0)
            e.set_dsb(0x6DBA, 0)
            n = self.node(c, hid)
            e.call(CALL_HANDLER, [hid, c[0], c[1], n[0], n[1], 0])
            self.range_word = None
        return {'after': [self.read_creature(p) for p in ptrs], 'seedAfter': e.randseed(),
                'spellCell': e.dsb(0x681B), 'cells': e.rb(self.lin(arena) + 7, 1250).hex(),
                'aim': [e.dsb(0x6DB9), e.dsb(0x6DBA)]}


def make_cases(rnd):
    cases = []

    def add(kind, cid, creatures, current, **kw):
        c = {'kind': kind, 'id': cid, 'creatures': [{'record': bytes(r).hex(), 'effects': ef, 'items': it}
                                                     for r, ef, it in creatures],
             'current': current, 'seed': rnd.getrandbits(32)}
        c.update(kw)
        cases.append(c)

    def arena():
        return bytearray(1 if rnd.random() < 0.06 else FLOOR for _ in range(1250))

    def placed(n, hostile0=0, lo=2):
        cs, places, used = [], [], set()
        while len(places) < n:
            x, y = rnd.randrange(lo, 50 - lo), rnd.randrange(lo, 25 - lo)
            if (x, y) in used:
                continue
            used.add((x, y))
            places.append((x, y, 1))
            lv = [0, 0, 0, 0, 0, rnd.randrange(3, 13), 0, 0]
            cs.append((creature(rnd, levels=lv, hostile=hostile0 if not cs else rnd.randrange(2),
                                hp=rnd.randrange(5, 60), hpmax=rnd.randrange(20, 90)), [], []))
        return cs, places

    # ---- handler 40 with bodies under the clouds ----
    for _ in range(40):
        cs, places = placed(rnd.randrange(3, 7))
        cells = arena()
        for x, y, _s in places:
            cells[y * 50 + x] = FLOOR
        a1 = places[rnd.randrange(1, len(places))][:2]
        a2 = (a1[0] + rnd.choice([-1, 0, 1]), a1[1] + rnd.choice([-1, 0, 1]))
        bodies = []
        for _b in range(rnd.randrange(1, 5)):
            dx, dy = rnd.choice([(0, 0), (1, 0), (1, 1), (0, 1), (2, 1), (-1, 0)])
            x, y = a1[0] + dx, a1[1] + dy
            if (x, y) in [p[:2] for p in places]:
                continue
            terr = rnd.choice([FLOOR, FLOOR, 2, BODY])
            bodies.append([rnd.randrange(len(cs)), x, y, terr])
            cells[y * 50 + x] = BODY
        if rnd.random() < 0.4:  # an emptied entry (0x317E1 fills the 7 bytes with 0)
            bodies.insert(rnd.randrange(len(bodies) + 1), [None, 0, 0, 0])
        casts = [(0, list(a1))]
        if rnd.random() < 0.7:
            casts.append((rnd.choice([0, 1]), list(a2)))
        c = {'cells': bytes(cells).hex(), 'places': places, 'casts': casts, 'targets': [], 'bodies': bodies}
        if rnd.random() < 0.85:
            c['removeFrom'] = 0
        add('cloud', 40, cs, 0, **c)

    # ---- handler 88 ----
    for _ in range(60):
        cs, places = placed(rnd.randrange(2, 7), hostile0=1, lo=3)
        cells = arena()
        for x, y, _s in places:
            cells[y * 50 + x] = FLOOR
        param = rnd.choice([0xFF, 0xFF, 0xFE, 0xFD, rnd.randrange(0, 0x10)])
        r, ef, it = cs[0]
        cs[0] = (r, [effect(88, 0, param, 0)] + ([effect(0x19, 5)] if rnd.random() < 0.3 else []), it)
        px, py = places[0][:2]
        how = rnd.random()
        if how < 0.15:
            pick = [px, py]  # no target: the aim stays on the breather
        elif how < 0.6:
            pick = list(places[rnd.randrange(1, len(places))][:2])
        else:
            pick = [px + rnd.randrange(-6, 7), py + rnd.randrange(-6, 7)]
        add('breath', 88, cs, 0, creature=0, cells=bytes(cells).hex(), places=places, pick=pick,
            round=rnd.choice([0, 0, 1, 3]))

    # ---- handler 84: the range word, the reversed target check ----
    for _ in range(60):
        cs, places = placed(rnd.randrange(2, 6), hostile0=1, lo=2)
        cells = arena()
        for x, y, _s in places:
            cells[y * 50 + x] = FLOOR
        r, ef, it = cs[0]
        gef = [effect(84, 0, 0xFF, 0)]
        if rnd.random() < 0.3:
            gef.append(effect(rnd.choice([25, 37, 63, 71]), 5, rnd.randrange(0, 0x40)))
        cs[0] = (r, gef, it)
        if rnd.random() < 0.3:
            r1, _e1, it1 = cs[1]
            cs[1] = (r1, [effect(rnd.choice([25, 37, 63, 71, 126]), 5, rnd.randrange(0, 0x40))], it1)
        rw = rnd.choice([0x3E58, 0x3E58, rnd.randrange(0, 0x19), rnd.randrange(0, 0x19), 0x8000 + rnd.randrange(0, 0x19),
                         0xEEEE])
        add('gaze', 84, cs, 0, creature=0, other=1, cells=bytes(cells).hex(), places=places, range=rw)

    # ---- handlers 90 / 97 in the saving throw ----
    for con in (3, 4, 6, 7, 10, 11, 13, 14, 17, 18, 20, 21, 0, 255):
        for ids in ([90], [97], [90, 97], [97, 90]):
            for _ in range(2):
                t = creature(rnd, race=rnd.choice([1, 3, 5]))
                t[0x14] = con
                t[0x101] = rnd.choice([0, 0, 1, 0xFF, 2])
                for i in range(5):
                    t[0x6D + i] = rnd.randrange(4, 20)
                add('save', 0, [(t, [effect(i) for i in ids], [])], 0, type=rnd.randrange(0, 5),
                    mod=rnd.choice([0, 0, -2, 2, 4, -4]))
    return cases


def main(argv):
    if len(argv) != 3:
        print(__doc__)
        return 2
    o = HandlerOracle(argv[1])
    rnd = random.Random(20261122)
    cases = make_cases(rnd)
    for c in cases:
        try:
            c['result'] = o.run(c)
        except RuntimeError as x:
            raise RuntimeError('%s %d: %s' % (c['kind'], c['id'], x)) from x
    out = {'source': 'GAME.OVR / START.EXE v1.3 run under unicorn (tools/scripts/por_handler_oracle.py)',
           'cases': cases}
    with open(argv[2], 'w') as f:
        json.dump(out, f, indent=0)
    print('%d cases' % len(cases))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
