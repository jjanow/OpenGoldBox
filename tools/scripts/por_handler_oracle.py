#!/usr/bin/env python3
"""Effect handlers 40, 88, 90 and 97 run by the program itself (v1.3, por_emu.py / unicorn), for GoldBox.Core's
HandlerProgramTests (docs/re/spells-effects.md "Handlers 40, 84, 88, 90, 97 under emulation").

Usage: python3 -I por_handler_oracle.py GAMEDIR OUT.json        (GAMEDIR: the PoR v1.3 files; needs unicorn 2.x)
Writes OUT.json (the kinds below) and handler_calls.json.gz next to it (the 'call' cases).

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
- call (s52, HandlerCallProgramTests): every other handler of the unit at 0xEC5B (and its helpers 0xEC5B, 0xEC7A,
  0xECB3, 0xFC07, 0xFC78, 0x11570) called through 0x2AEEA, about 40 cases per id: 2-4 generated creatures with effect
  lists, items (weapons for the current character, the handler's own item record for ids 128-139), combat records
  and their +0x0A target, the DS cells 6816-682A, the round counter, team counts and DS:6DBF set per case; a third of
  them on a combat map in a fight (DS:49F3 = 5). Records (equipment slot pointers aside), effect lists, items (names
  aside: START 0x1583, the status panel, writes them and is trapped), combat records, the cells, DS:6826 and RandSeed
  are recorded. Set HANDLER_COVERAGE=FILE to write the unit's executed addresses (a branch check).
The output holds creature bytes made up here and creature names and base item entries read from the program; it
is kept with the private fixtures.
"""
import gzip
import json
import os
import random
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from por_spell_oracle import Oracle, CALL_HANDLER, REMOVE_EFFECT, creature, effect, find, ITEM  # noqa: E402
from por_round_oracle import RoundOracle, weapon_types  # noqa: E402
from por_emu import Emu  # noqa: E402
from unicorn import UC_HOOK_CODE  # noqa: E402

SAVE, GAZE, PUT_ON_SQUARE = 0x2BC4D, 0x10AD2, 0x317E1
FLOOR, BODY, CLOUD = 23, 0x1F, 0x1E
UNIT = (0xEC5B, 0x12508)
CALLS_FILE = 'handler_calls.json.gz'  # the 'call' cases, next to OUT.json
# DS cells the handlers read and write (spells-effects.md "Data-segment cells"), set and compared by the 'call' cases.
CELLS = (0x6816, 0x6817, 0x6818, 0x6819, 0x681A, 0x681B, 0x681C, 0x681D, 0x681E, 0x681F, 0x6820, 0x6821, 0x6822,
         0x6823, 0x6825, 0x682A, 0x49F3)
# Display-only callees trapped for the 'call' cases: 0x30C65 (a cell drawn again), 0x319D2 (the view on a creature);
# START 0x8BBC (a sound), START 0x6192 (the screen copied from the picture buffers, after 0x2BDF0's icon goes), START 0x1583 (the status panel; it also writes the readied items' names into their
# records, a display cache).
DRAW_ONLY = {0x30C65: 6, 0x319D2: 8}
DRAW_ONLY_START = {0x8BBC: 2, 0x1583: 4, 0x6192: 0}


class HandlerOracle(Oracle):
    def __init__(self, gamedir):
        super().__init__(gamedir)
        self.aim = (0, 0)
        seg, off = self.routines[0]
        self.e.trap_start(seg, off, self._pick)
        self.range_word = None
        self.e.trap_ovr(GAZE, self._gaze_entry)
        with open(find(gamedir, 'ITEMS'), 'rb') as f:
            self.base = f.read()[2:2 + 16 * 128]
        self.coverage = None
        if os.environ.get('HANDLER_COVERAGE'):
            self.coverage = set()
            lo = self.e.unit_linear(UNIT[0])[0]
            self.e.uc.hook_add(UC_HOOK_CODE, lambda uc, a, n, d: self.coverage.add(a - lo + UNIT[0]),
                               begin=lo, end=lo + UNIT[1] - UNIT[0])

    set_equipment = RoundOracle.set_equipment
    combat = RoundOracle.combat

    def call_setup(self):
        """The 'call' cases' extra set-up, done once before them (the earlier kinds run without it): the base item
        table DS:5582 from ITEMS, and the display-only routines of the handlers' callees trapped."""
        e = self.e
        e.wb(e.ds(0x5582), self.base)
        for off, n in DRAW_ONLY.items():
            e.trap_ovr(off, Emu.ret(n))
        for lin, n in DRAW_ONLY_START.items():
            e.trap_start(0, lin, Emu.ret(n))

    def names(self):
        """The name lists of handlers 18 (DS:0332), 26 (DS:0372), 47 (DS:03F2) and 48 (two names in its code)."""
        e = self.e

        def ps(lin):
            b = e.rb(lin, 16)
            return bytes(b[:1 + b[0]]) if b[0] < 16 else b''
        out = {18: [ps(e.ds(0x332 + 16 * i)) for i in range(1, 5)],
               26: [ps(e.ds(0x372 + 16 * i)) for i in range(1, 9)],
               47: [ps(e.ds(0x3F2 + 16 * i)) for i in range(1, 9)]}
        lin = e.unit_linear(UNIT[0])[0] - UNIT[0]
        out[48] = [ps(lin + 0xFEC3), ps(lin + 0xFECB)]
        return out

    def run_call(self, case):
        """0x2AEEA(id, creature, record, removed) with the DS cells, combat records and creatures of the case."""
        e = self.e
        ptrs = self.build(case['creatures'])
        for p in ptrs:
            self.set_equipment(p)
        self.map_mode = 'cells' in case
        e.set_dsb(0x49F3, 0)
        if self.map_mode:
            self.setup_map(dict(case, cells=bytes([FLOOR] * 1250).hex()), ptrs)
        else:  # the slot table DS:65B9 alone (handlers 75-77 read it, 0x31399 searches it)
            for k, p in enumerate(ptrs, 1):
                e.wb(e.ds(0x65B9 + 4 * k), struct.pack('<HH', p[1], p[0]))
            e.wb(e.ds(0x65B9 + 4 * (len(ptrs) + 1)), b'\0\0\0\0')
            e.set_dsb(0x5F2A, len(ptrs) + 1)
        e.set_dsb(0x49F3, 5 if case.get('inFight') else 0)
        for i, (p, cb, oi) in enumerate(zip(ptrs, case['combat'], case['others'])):
            b = bytearray(bytes.fromhex(cb))
            b[0x0A:0x0E] = struct.pack('<HH', ptrs[oi][1], ptrs[oi][0]) if oi is not None else b'\0\0\0\0'
            e.wb(self.combat(p), b)
        for k, v in case['cellsIn'].items():
            e.set_dsb(int(k, 16), v)
        o6826 = case.get('ds6826')
        e.set_dsptr(0x6826, ptrs[o6826] if o6826 is not None else (0, 0))
        e.set_dsptr(0x5D92, ptrs[case['current']])
        e.set_randseed(case['seed'])
        c = ptrs[case['creature']]
        how = case['record']
        if how == 'nil':
            node = (0, 0)
        elif how[0] == 'item':
            o, s = struct.unpack('<HH', e.rb(self.lin(c) + 0xC8, 4))
            for _ in range(how[1]):
                o, s = struct.unpack('<HH', e.rb(s * 16 + o + 0x2A, 4))
            node = (s, o)
        else:  # ['first', k]: the first record with the id on creature k (the aura carrier for 21, 45, 46, 49)
            node = self.node(ptrs[how[1]], case['id'])
        if 'ivt3' in case:  # 0000:0003, what a nil record's byte 3 reads (handler 89 from the cloaks' code 133)
            e.wb(3, [case['ivt3']])
        e.call(CALL_HANDLER, [case['id'], c[0], c[1], node[0], node[1], case['removed']])
        index = {p: j for j, p in enumerate(ptrs)}

        def who(lin):
            o, s = struct.unpack('<HH', e.rb(lin, 4))
            return -1 if (s, o) == (0, 0) else index.get((s, o), -2)
        res = {'after': [self.read_creature(p) for p in ptrs], 'seedAfter': e.randseed(),
               'combat': [self.read_combat(p) for p in ptrs],
               'othersOut': [who(self.combat(p) + 0x0A) for p in ptrs],
               'cellsOut': {'%04X' % a: e.dsb(a) for a in CELLS},
               'ds6826': who(e.ds(0x6826))}
        if 'ivt3' in case:
            res['ivt3'] = e.rb(3)[0]
        return res

    def read_combat(self, p):
        b = bytearray(self.e.rb(self.combat(p), 0x16))
        b[0x0A:0x0E] = b'\0\0\0\0'
        return b.hex()

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
        if case['kind'] == 'call':
            return self.run_call(case)
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
        if case['kind'] == 'place':  # s52: 0x317E1(creature, x, y, moving), in a fight or not
            e.set_dsb(0x49F3, 5 if case['fight'] else 0)
            c = ptrs[case['creature']]
            cb = struct.unpack('<HH', e.rb(self.lin(c) + 0x108, 4))
            e.wb(cb[1] * 16 + cb[0] + 0x13, [case['notParty']])
            x, y = case['to']
            al, _ = e.call(PUT_ON_SQUARE, [c[0], c[1], x & 0xFF, y & 0xFF, case['moving']])
            index = {q: j for j, q in enumerate(ptrs)}
            bodies = []
            for k in range(1, e.dsb(0x6715) + 1):
                off, sg, bx, by, t = struct.unpack('<HHBBB', e.rb(e.ds(0x66D6 + 7 * k), 7))
                bodies.append([index.get((sg, off)) if (sg, off) != (0, 0) else None, bx, by, t])
            return {'after': [self.read_creature(q) for q in ptrs], 'seedAfter': e.randseed(), 'put': al & 0xFF,
                    'cells': e.rb(self.lin(arena) + 7, 1250).hex(), 'bodies': bodies,
                    'slots': [list(e.rb(e.ds(0x5F27 + 4 * k), 4)) for k in range(1, len(ptrs) + 1)],
                    'spellCell': e.dsb(0x681B), 'aim': [e.dsb(0x6DB9), e.dsb(0x6DBA)]}
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
    # ---- s52: put a creature on a square, 0x317E1 (the revive / Animate Dead test, the move undo) ----
    for _ in range(80):
        cs, places = placed(rnd.randrange(2, 6))
        cells = arena()
        for x, y, _s in places:
            cells[y * 50 + x] = FLOOR
        for k, (r, ef, it) in enumerate(cs):
            r[0x6C] = rnd.choice([1, 1, 1, 2, 0x81])
        who = rnd.randrange(len(cs))
        bodies = []
        for _b in range(rnd.randrange(0, 5)):
            bx, by = rnd.randrange(2, 48), rnd.randrange(2, 23)
            terr = rnd.choice([FLOOR, FLOOR, 2, BODY, 0x1E])
            bodies.append([rnd.choice([who, who, rnd.randrange(len(cs))]), bx, by, terr])
            cells[by * 50 + bx] = BODY
        if rnd.random() < 0.3:
            bodies.insert(rnd.randrange(len(bodies) + 1), [None, 0, 0, 0])
        how = rnd.random()
        if how < 0.4:
            to = list(places[who][:2])
        elif how < 0.6 and bodies and bodies[-1][0] is not None:
            to = bodies[-1][1:3]
        elif how < 0.75:
            to = list(places[rnd.randrange(len(places))][:2])
        else:
            to = [rnd.randrange(1, 49), rnd.randrange(1, 24)]
        if rnd.random() < 0.15:
            cells[to[1] * 50 + to[0]] = rnd.choice([0, 1, 5])
        add('place', 0, cs, 0, creature=who, cells=bytes(cells).hex(), places=places, bodies=bodies, to=to,
            moving=rnd.choice([1, 1, 0]), notParty=rnd.choice([0, 0, 1]), fight=rnd.random() < 0.9)
    return cases


# ---- 'call': one handler through 0x2AEEA with DS cells, combat records and creatures set per case ----

# Handlers the 'call' cases drive (every id of the table but those run elsewhere: 40, 84, 88, 90 and 97 above,
# 81-86 in por_spell_oracle.py, 73 and 74 in the unit at 0x127FB, and the empty ones). Each id gets CALL_N cases.
CALL_IDS = [1, 2, 3, 4, 6, 7, 8, 9, 10, 11, 12, 14, 15, 17, 18, 20, 21, 22, 23, 25, 26, 28, 29, 30, 31, 32, 33, 34,
            35, 36, 37, 38, 39, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 56, 57, 58, 59, 61, 62, 64, 65,
            66, 67, 68, 69, 70, 71, 72, 75, 76, 77, 78, 79, 80, 87, 89, 91, 93, 94, 95, 96, 98, 99, 100, 101, 102,
            103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122, 123,
            124, 125, 126, 128, 129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139]
CALL_N = 40
MAP_IDS = {41, 75, 76, 77, 104, 120, 121}  # distance (START 0x30FB) or combat slots on a real map
ITEM_IDS = {128, 129, 130, 131, 132, 133, 134, 135, 136, 138, 139}
WEAPON_IDS = {41, 94, 96, 104, 115, 116, 117, 119, 120, 122, 123}
AURA_IDS = {21, 45, 46, 49}
REVIVE_IDS = {78, 99, 102}  # 0x2C753: the square test 0x317E1 in a fight
GRANTED = [1, 2, 3, 6, 8, 9, 0x0A, 0x11, 0x12, 0x14, 0x19, 0x1A, 0x1D, 0x21, 0x23, 0x24, 0x25, 0x2A, 0x2D, 0x2E, 0x3A,
           0x3B, 0x47, 0x5D, 0x62, 0x69, 0x6A, 0x6B, 0x6C, 0x6D, 0x6E, 0x6F, 0x70, 0x71, 0x72, 0x73,
           0x74, 0x75, 0x76, 0x7C, 0x7D]
# Effects a handler looks for or adds, put on the creatures now and then.
POOL = [0x01, 0x0B, 0x0C, 0x0F, 0x17, 0x18, 0x19, 0x1F, 0x26, 0x32, 0x34, 0x35, 0x37, 0x39, 0x3A, 0x3B, 0x3E, 0x4A,
        0x4B, 0x4E, 0x50, 0x5F, 0x62, 0x66, 0x79]


def call_creature(rnd, names):
    r = creature(rnd)
    r[0xA0] = rnd.randrange(9)
    r[0x9F] = rnd.choice([0, 0, 1, 1, 1, 4, 9, 0x0A, 0x0C, rnd.randrange(256)])
    r[0x6C] = rnd.choice([1, 1, 2, 2, 0x81, 0x82, 0])
    r[0x10] = rnd.choice([3, 4, 9, 17, 18, 18, 19, 20, rnd.randrange(3, 26)])
    r[0x16] = rnd.choice([0, 0, 50, 99, 100])
    r[0x15] = rnd.choice([0, 1, 2, 3, 4, 10, 18])
    r[0x14] = rnd.choice([3, 10, 14, 15, 16, 17, 18, 19, 20])
    r[0x30], r[0x31] = rnd.randrange(256), rnd.choice([0, 0, 1, 0xFF])
    for o in (0x110, 0x111, 0x112):
        r[o] = rnd.choice([0x32, 0x33, 0x34, 0x35, 0x36, 0x38, 0x39, 0x3A, 0x40, rnd.randrange(256)])
    r[0x72] = rnd.randrange(4, 16)
    r[0x6B] = rnd.randrange(0, 12)
    r[0x76] = rnd.randrange(0, 4)
    r[0x84] = rnd.choice([0, 0, 0x7F, 0x80, 0xB3, 0xFF])
    if rnd.random() < 0.6:
        r[0x10C], r[0x10D] = 0, 1
    else:
        r[0x10C] = rnd.choice([0, 1, 2, 3, 4, 5, 6, 7, 8])
        r[0x10D] = rnd.choice([0, 0, 1])
    r[0x10E] = rnd.choice([0, 1])  # START 0x2F7B counts DS:6814 + hostile: other values write past the two counts
    hpmax = r[0x32]
    r[0x11B] = rnd.choice([0, 1, 2, 3, hpmax, max(hpmax - 1, 0), max(hpmax - 2, 0), max(hpmax - 3, 0),
                           rnd.randrange(hpmax + 1)])
    for o in (0x113, 0x114, 0x115, 0x116, 0x117, 0x118):
        r[o] = rnd.randrange(6)
    r[0xC7] = rnd.choice([0, 1, 3, 15, 16, 17])
    if rnd.random() < 0.35:
        lists = [n for k in (18, 26, 47, 48) for n in names[k] if n]
        if lists:
            nm = rnd.choice(lists)
            r[0:16] = bytes(16)
            r[0:len(nm)] = nm
    return r


def call_item(rnd, t, readied, eid=None, words=None):
    b = bytearray(ITEM)
    b[0x2E] = t
    if words is None:
        words = [rnd.choice([0, 0, 0x76, 0x98, 0xB1, 0xF3, 0xFC, rnd.randrange(256)]) for _ in range(3)]
    b[0x2F], b[0x30], b[0x31] = words
    b[0x32] = rnd.choice([0, 0, 1, 2, 0xFF, 0xFE])
    b[0x34] = 1 if readied else 0
    b[0x39] = rnd.choice([0, 1, 5])
    b[0x3D] = eid if eid is not None else rnd.randrange(256)
    b[0x3E] = rnd.randrange(256)
    return b.hex()


def tune(hid, rnd, recs, effects, items, cur, names):
    """Inputs a handler's branches need more often than the general draw gives them."""
    if hid == 7:
        recs[0][0x14] = rnd.choice([10, 13, 14, 15, 16, 17, 18, 19])
        if rnd.random() < 0.25:
            recs[0][0x96:0x9E] = bytes(8)
        elif rnd.random() < 0.5:
            recs[0][0x98] = rnd.randrange(1, 10)
    if hid in (18, 26) and rnd.random() < 0.7:
        t = recs[rnd.randrange(len(recs))]
        t[0x9F], t[0x6C] = 1, 1
        for r in recs:
            if rnd.random() < 0.6:
                nm = rnd.choice([n for n in names[hid] if n] or [b'\x01A'])
                r[0:16] = bytes(16)
                r[0:len(nm)] = nm
    if hid in (47, 48) and rnd.random() < 0.7:
        recs[cur][0x9F] = 1
        if hid == 47:
            recs[cur][0x6C] = rnd.choice([2, 0x82])
        for r in recs:
            if rnd.random() < 0.6:
                nm = rnd.choice([n for n in names[hid] if n] or [b'\x01A'])
                r[0:16] = bytes(16)
                r[0:len(nm)] = nm
    if hid == 23 and rnd.random() < 0.5:  # the hammer already there
        items[0].insert(rnd.randrange(len(items[0]) + 1),
                        call_item(rnd, 0x14, rnd.random() < 0.5, words=[0x14, rnd.choice([0, 0x14]), 0xF3]))
    if hid == 28 and rnd.random() < 0.5:  # one image left, a single-target spell
        effects[0][:] = [e for e in effects[0] if int(e[:2], 16) != 28]
        effects[0].insert(0, effect(28, 5, rnd.choice([1, 2])))
    if hid == 123 and rnd.random() < 0.5:
        items[cur] = [call_item(rnd, rnd.choice([0x14, 0x22]), True, words=[0, 0, 0xB1])]
        items[cur][0] = items[cur][0][:0x32 * 2] + '00' + items[cur][0][0x33 * 2:]
    if hid == 126:
        t = rnd.randrange(len(recs))
        items[t] = items[t] + [call_item(rnd, rnd.randrange(1, 0x60), True,
                                         words=rnd.choice([[0x98, 0, 0xFC], [0, 0x76, 0], [0x98, 0, 0], [0, 0, 0x76]]))]
    if hid == 131 and rnd.random() < 0.5:
        recs[0][0x10], recs[0][0x16] = 18, 100
    if hid == 131 and rnd.random() < 0.5:  # a Strength record holding 18/00 (code 0x65)
        effects[0].insert(0, effect(0x26, 0, rnd.choice([0x65, 0xE5]), rnd.choice([0, 1])))
    if hid in REVIVE_IDS and rnd.random() < 0.7:  # dying or unconscious, out of play
        recs[0][0x10C], recs[0][0x10D] = rnd.choice([4, 5, 5]), 0
    if hid == 22 and rnd.random() < 0.5:
        effects[0].append(effect(0x37))
    if hid in (34, 43):
        recs[0][0x10] = rnd.choice([2, 3, 3, 4, 10])
        if rnd.random() < 0.3:
            effects[0].append(effect(0x1F))


def call_cases(o, rnd):
    names = o.names()
    kinds = weapon_types(o.base)
    weapons = sorted({t for v in kinds.values() for t in v} | {0x55, 0x56, 0x57, 0x58, 0x14})
    cases = []
    for hid in CALL_IDS:
        for _n in range(CALL_N):
            ivt3 = None
            k = rnd.randint(2, 4)
            recs = [call_creature(rnd, names) for _ in range(k)]
            effects = [[] for _ in range(k)]
            items = [[] for _ in range(k)]
            for j in range(k):
                for _ in range(rnd.choice([0, 0, 1, 2, 3])):
                    eid = rnd.choice([x for x in POOL if x != hid])
                    prm = rnd.choice([0xFF, 0x0C, 0, 0x10, 0x15, 0x20, 0x65, 0x70, 0x80, 0x90, rnd.randrange(256)])
                    if eid in (0x4A, 0x4B):  # the hold / blood drain records keep the victim's combat slot
                        prm = rnd.randint(1, k)
                    # (0x4A / 0x4B without the removal call: their handlers need the combat map's slots)
                    effects[j].append(effect(eid, rnd.choice([0, 0, 1, 3, 10, 60, 0x1F0, rnd.randrange(65536)]), prm,
                                             0 if eid in (0x4A, 0x4B) else rnd.choice([0, 0, 1])))
            # the handler's own record on creature 0 (or on an aura carrier)
            param = rnd.choice([0, 1, 2, 3, 5, 0x0C, 0x10, 0x11, 0x15, 0x1F, 0x20, 0x21, 0x2F, 0x30, 0x40, 0x60, 0x65,
                                0x66, 0x70, 0x7F, 0x80, 0xA0, 0xC0, 0xE5, 0xFF, rnd.randrange(256)])
            minutes = rnd.choice([0, 1, 5, 0x10, 0x31, 0x50, 0xCF, 0x3840, rnd.randrange(65536)])
            if hid == 75:  # 76 stores the victim's combat slot (0x31399)
                param = rnd.randint(1, k)
            own = effect(hid, minutes, param, rnd.choice([0, 1, 1]))
            record = ['first', 0]
            if hid in AURA_IDS and rnd.random() < 0.4:
                record = ['first', 1]
                effects[1].insert(rnd.randrange(len(effects[1]) + 1), own)
            elif hid not in ITEM_IDS:
                effects[0].insert(rnd.randrange(len(effects[0]) + 1), own)
                if hid != 75 and rnd.random() < 0.15:  # a second record of the same id (75: a slot number)
                    effects[0].append(effect(hid, rnd.randrange(100), rnd.randrange(256), rnd.randrange(2)))
            if hid in (12, 38, 131):  # more Strength records (0x0C / 0x26)
                for _ in range(rnd.choice([0, 1, 2, 3])):
                    effects[0].insert(rnd.randrange(len(effects[0]) + 1),
                                      effect(rnd.choice([0x0C, 0x26]), rnd.randrange(100),
                                             rnd.choice([0x13, 0x30, 0x60, 0x65, 0x66, 0x70, 0x76, 0x80, 0xE5, 0xF0,
                                                         rnd.randrange(256)]), rnd.choice([0, 1])))
            if hid == 131 and rnd.random() < 0.5:
                recs[0][0x10], recs[0][0x16] = 18, rnd.choice([100, 100, 50, 0])
            cur = rnd.randrange(k)
            if hid in WEAPON_IDS:
                own_w = {117: 0x55, 122: 0x56, 120: rnd.choice([0x57, 0x58])}.get(hid)
                pick = [own_w] * 3 if own_w and rnd.random() < 0.5 else weapons + [0x49, 0x1C]
                items[cur if rnd.random() < 0.7 else rnd.randrange(k)] = [
                    call_item(rnd, rnd.choice(pick), rnd.random() < 0.8) for _ in range(rnd.choice([0, 1, 1, 2, 3]))]
            if hid in (23, 126) or rnd.random() < 0.15:
                for j in range(k):
                    if rnd.random() < 0.5:
                        items[j] = items[j] + [call_item(rnd, rnd.choice([0x14, 0x14, rnd.randrange(1, 0x60)]),
                                                         rnd.random() < 0.7) for _ in range(rnd.randrange(1, 4))]
            if hid in ITEM_IDS:
                # 0x11B35 runs the granted effect's handler with a nil record: only handlers that do not read the
                # record (a nil one reads the interrupt table at 0000:0000 on a real machine)
                eid = rnd.choice(GRANTED)
                if hid == 132:
                    eid = rnd.choice([recs[0][0xA0], rnd.randrange(9)]) + 16 * rnd.randrange(16)
                at = rnd.randrange(len(items[0]) + 1)
                items[0].insert(at, call_item(rnd, rnd.randrange(1, 0x60), rnd.random() < 0.8, eid))
                if rnd.random() < 0.4:  # the granted effect already there
                    effects[0].append(effect(eid if hid != 132 else 0x11, rnd.randrange(50), rnd.randrange(256),
                                             rnd.randrange(2)))
                record = ['item', at]
                if hid == 133 and rnd.random() < 0.6:  # the cloaks: Displacement (89), run with a nil record
                    items[0][at] = items[0][at][:0x3D * 2] + '59' + items[0][at][0x3E * 2:]
                    ivt3 = rnd.choice([0, 0x10, 0x12, 0x1F, 0x9F, rnd.randrange(256)])
            tune(hid, rnd, recs, effects, items, cur, names)
            others = [rnd.randrange(k) for _ in range(k)]
            if others[0] == 0 and rnd.random() < 0.8:
                others[0] = rnd.randrange(1, k)
            combat = [bytes(rnd.choice([0, 0, 1, 2, 0xFF, rnd.randrange(256)]) for _ in range(0x16)).hex()
                      for _ in range(k)]
            cells = {}
            for a in CELLS:
                if a != 0x49F3:
                    cells['%04X' % a] = rnd.choice([0, 0, 1, 2, 3, 4, 5, 8, 9, 0x0A, 0x0F, 0x10, 0x14, 0x1F, 0x50,
                                                    0x64, 0x7F, 0x80, 0xFE, 0xFF, rnd.randrange(256)])
            cells['681D'] = rnd.choice([0, 1])
            cells['681B'] = rnd.choice([0, 0, 0x0A, 0x0F, rnd.randrange(1, 0x44)])  # a spell id (START 0x3262 reads its table row)
            cells['6817'] = rnd.choice([0, 0, 0x0B, 0x34, 0x35, 0x37, rnd.randrange(256)])
            cells['682A'] = rnd.choice([0, 0, 1, 2, 3, 4])
            cells['6DE3'] = rnd.choice([0, 0, 1, 2, 7])
            cells['6DBF'] = rnd.choice([0, 0, 0, 1])
            cells['6814'] = rnd.randrange(10)
            cells['6815'] = rnd.randrange(10)
            if hid == 89 and rnd.random() < 0.5:
                cells['6822'] = 0
            if hid in (34, 43) and rnd.random() < 0.6:
                cells['681F'] = 0
            if hid == 28 and rnd.random() < 0.5:
                cells['6820'], cells['681B'] = 0, rnd.randrange(1, 0x44)
            if rnd.random() < 0.3:
                cells['681F'] = rnd.choice([0, 1])
            c = {'kind': 'call', 'id': hid,
                 'creatures': [{'record': bytes(r).hex(), 'effects': ef, 'items': it}
                               for r, ef, it in zip(recs, effects, items)],
                 'current': cur, 'creature': 0, 'combat': combat, 'others': others,
                 'cellsIn': cells, 'ds6826': rnd.choice([None, rnd.randrange(k)]), 'record': record,
                 'removed': rnd.choice([0, 0, 1]), 'seed': rnd.getrandbits(32)}
            if ivt3 is not None:
                c['ivt3'] = ivt3
            if hid in MAP_IDS or rnd.random() < (0.6 if hid in REVIVE_IDS else 0.35):
                if hid not in MAP_IDS or rnd.random() < 0.5:
                    c['inFight'] = True  # DS:49F3 = 5
                places, used = [], set()
                while len(places) < k:
                    x, y = rnd.randrange(1, 49), rnd.randrange(1, 24)
                    if (x, y) in used or (places and rnd.random() < 0.7 and max(abs(x - places[0][0]), abs(y - places[0][1])) > 4):
                        continue
                    used.add((x, y))
                    places.append((x, y, 1))
                if hid in REVIVE_IDS and rnd.random() < 0.5:  # another creature on the reviving one's square
                    places[1] = places[0]
                if hid == 121 and rnd.random() < 0.6:  # the target next to the breather
                    places[others[0]] = (places[0][0] + rnd.choice([1, 2, 3]), places[0][1], 1)
                c['cells'] = 'floor'  # every cell 23 (setup_map gets the bytes)
                c['places'] = places
            cases.append(c)
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
    o.call_setup()
    calls = call_cases(o, random.Random(20261125))
    for c in calls:
        try:
            c['result'] = o.run(c)
        except RuntimeError as x:
            raise RuntimeError('%s %d: %s' % (c['kind'], c['id'], x)) from x
    if o.coverage is not None:
        with open(os.environ['HANDLER_COVERAGE'], 'w') as f:
            json.dump(sorted(o.coverage), f)
    src = 'GAME.OVR / START.EXE v1.3 run under unicorn (tools/scripts/por_handler_oracle.py)'
    with open(argv[2], 'w') as f:
        json.dump({'source': src, 'cases': cases}, f, indent=0)
    with gzip.open(os.path.join(os.path.dirname(os.path.abspath(argv[2])), CALLS_FILE), 'wt') as f:
        json.dump({'source': src, 'items_5582': o.base.hex(), 'cases': calls}, f, separators=(',', ':'))
    print('%d cases, %d calls' % (len(cases), len(calls)))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
