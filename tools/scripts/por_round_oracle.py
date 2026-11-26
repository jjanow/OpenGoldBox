#!/usr/bin/env python3
"""Round start, turn order, one-square move and terrain effects run by the program itself: the v1.3 routines
(por_emu.py, unicorn) on generated creatures, for GoldBox.Core's RoundProgramTests (docs/re/combat-format.md
"Start of round", "Next creature"; combat-map.md "Moves").

Usage: python3 -I por_round_oracle.py GAMEDIR OUT.json        (GAMEDIR: the PoR v1.3 files; needs unicorn 2.x)

Cases (fixed seeds, so the output is the same every run):
- round: the start of round 0x127FB for every creature in list order (with its callees 0x1351E, 0x1364D, 0x1291E and
  the trigger-0x12 effects 39 haste, 42 slow, 58 run for real), surprise $6DCB cleared, then the picks 0x9938 until
  none, each picked creature's initiative set to 0 (as its turn would end); records, combat records, picks, RandSeed;
  then (s52) the highest enemy movement 0x15103 for each creature and the hostile HP percent 0x15074 (DS:6E2F);
- picks: 0x9938 alone on initiative bytes 0x00-0xFF (the signed path), several picks per seed;
- move: one square 0x12F0D on a combat map (cost, slot, grid, flank counters, the guards 0x12E22 with 0x13FF1 run and
  MakeAttack 0x1407F trapped and logged; a scripted guard attack can put the mover out of play or make it helpless);
- terrain: 0x2B9D1 on creatures on stinking-cloud and other cells, the five blocking effects, saves passed and failed
  (the handler dispatcher 0x2AEEA is logged with the record pointer it gets and then runs).
Equipment slots (+0xCC) are written as START 0x1758 builds them (the routine itself is trapped, as in
por_spell_oracle.py). Drawing, text and sound are trapped. The output holds creature bytes made up here and base item
entries read from ITEMS; it belongs in tests/fixtures-private only.
"""
import json
import os
import random
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import por_spell_oracle as so  # noqa: E402
from por_emu import Emu  # noqa: E402

ROUND_START, NEXT_CREATURE, MOVE_ONE, TERRAIN, MAKE_ATTACK, DISPATCH = 0x127FB, 0x9938, 0x12F0D, 0x2B9D1, 0x1407F, 0x2AEEA
FASTEST_ENEMY, HP_PERCENT, FREE_ATTACKS, FLEE, PICTURE = 0x15103, 0x15074, 0x130AD, 0x13465, 0x6192  # s52
REDRAW_CELL, SOUND = 0x30C65, 0x8BBC  # 0x30C65 (draws only), START 08AE:00DC
FLOOR, CLOUD = 23, 0x1E
HELPLESS = [0x33, 0x34, 0x35, 0x1F]
ARROWS, BOLTS = 0x49, 0x1C


class RoundOracle(so.Oracle):
    def __init__(self, gamedir):
        super().__init__(gamedir)
        e = self.e
        with open(so.find(gamedir, 'ITEMS'), 'rb') as f:
            self.base = f.read()[2:2 + 16 * 128]
        e.wb(e.ds(0x5582), self.base)
        e.trap_ovr(REDRAW_CELL, Emu.ret(6))
        e.trap_start(0, SOUND, Emu.ret(2))
        e.trap_start(0, PICTURE, Emu.ret(0))  # s52: START 0x6192 (screen) after 0x2BDF0's icon removal
        e.trap_ovr(MAKE_ATTACK, self._make_attack)
        e.trap_ovr(DISPATCH, self._dispatch)
        self.zone1 = None
        self.attacks = []
        self.attack_state = []
        self.handlers = []
        self.knock = None

    def _make_attack(self, e):
        # MakeAttack(attacker, defender, fromBehind, ammo, out): Pascal pushes the first parameter first, so the
        # attacker's pointer is the deepest: words above the return address: out(2), ammo(2), byte, defender(2), attacker(2)
        att = (e.arg(16), e.arg(14))
        dfn = (e.arg(12), e.arg(10))
        frm = e.arg(8) & 0xFF
        ammo = (e.arg(6), e.arg(4))
        self.attacks.append((att, dfn, frm, ammo != (0, 0)))
        rl = self.lin(att)
        self.attack_state.append([e.rb(self.combat(att) + 4, 1)[0]] + list(e.rb(rl + 0x113, 2)))
        if self.knock is not None and len(self.attacks) == self.knock[0]:
            rl = self.lin(dfn)
            if self.knock[1] == 'out':
                e.wb(rl + 0x10D, [0])
            else:  # helpless: a sleep record (0x35) at the head of the effect list
                n = e.alloc(so.EFFECT)
                head = e.rb(rl + 0x7F, 4)
                e.wb(self.lin(n), bytes([0x35, 0, 0, 0xFF, 0]) + head)
                e.wb(rl + 0x7F, struct.pack('<HH', n[1], n[0]))
        e.pop_ret(0x12)

    def _dispatch(self, e):
        # 0x2AEEA(id, creature, record, removed): logged, then the handler runs
        self.handlers.append((e.arg(10) & 0xFF, (e.arg(8), e.arg(6)), (e.arg(4), e.arg(2)), e.arg(0) & 0xFF))

    def fresh_zone1(self, surprise, partymove):
        e = self.e
        z = e.alloc(0x800)
        e.set_dsptr(0x49D6, z)
        e.wb(self.lin(z) + 0x596, struct.pack('<H', surprise))
        e.wb(self.lin(z) + 0x6E4, struct.pack('<H', partymove))

    def combat(self, p):
        o, s = struct.unpack('<HH', self.e.rb(self.lin(p) + 0x108, 4))
        return s * 16 + o

    def set_equipment(self, p):
        """START 0x1758-0x18D7: the 13 slot pointers from the readied items (0x34), in list order."""
        e = self.e
        rl = self.lin(p)
        slots = [(0, 0)] * 13
        o, s = struct.unpack('<HH', e.rb(rl + 0xC8, 4))
        while (o, s) != (0, 0):
            it = e.rb(s * 16 + o, so.ITEM)
            if it[0x34]:
                t = it[0x2E]
                sl = self.base[16 * t]
                if sl <= 8:
                    slots[sl] = (s, o)
                elif sl == 9:
                    if slots[9] == (0, 0):
                        slots[9] = (s, o)
                    elif slots[10] == (0, 0):
                        slots[10] = (s, o)
                if t == ARROWS:
                    slots[11] = (s, o)
                if t == BOLTS:
                    slots[12] = (s, o)
            o, s = struct.unpack('<HH', it[0x2A:0x2E])
        e.wb(rl + 0xCC, b''.join(struct.pack('<HH', so_[1], so_[0]) for so_ in slots))

    def read_combat(self, p):
        return self.e.rb(self.combat(p), 0x16).hex()


def weapon_types(base):
    """Base item types by kind, from the ITEMS table (slot 0 weapons)."""
    kinds = {'melee': [], 'bow': [], 'xbow': [], 'thrown': [], 'flags0a': []}
    for t in range(128):
        b = base[16 * t:16 * t + 16]
        if b[0] != 0:
            continue
        rng, fl = b[12], b[14]
        if rng <= 1:
            kinds['melee'].append(t)
        elif fl == 0x0A:
            kinds['flags0a'].append(t)
        elif fl & 0x10:
            kinds['thrown'].append(t)
        elif fl & 8 and fl & 1:
            kinds['bow'].append(t)
        elif fl & 8 and fl & 0x80:
            kinds['xbow'].append(t)
    return {k: v for k, v in kinds.items() if v}


def item(t, readied, count=1):
    b = bytearray(so.ITEM)
    b[0x2E] = t
    b[0x34] = 1 if readied else 0
    b[0x39] = count
    return b.hex()


def effect(eid, param=0xFF, minutes=0):
    return bytes([eid, minutes & 0xFF, minutes >> 8, param, 0]).hex()


def round_creature(rnd, kinds, hostile):
    r = bytearray(so.creature(rnd))
    r[0x13] = rnd.choice(list(range(0, 26)) + [rnd.randrange(256)])
    r[0x10D] = 0 if rnd.random() < 0.12 else 1
    r[0x10C] = 0 if r[0x10D] else rnd.choice([3, 4, 5, 6])
    r[0x10E] = hostile
    r[0x10F] = rnd.randrange(2)
    r[0x11C] = rnd.choice([0, 1, 6, 9, 12, 96, 97, 255, rnd.randrange(256)])
    r[0xA1] = rnd.choice([0, 1, 2, 3, 4, 5, 6])
    r[0xA2] = rnd.choice([0, 0, 1, 2, 3, 4])
    r[0x6B] = rnd.randrange(0, 12)
    r[0x113] = rnd.choice([0, 1, 2, 3, 5])
    r[0x114] = rnd.randrange(4)
    items = []
    k = rnd.choice(list(kinds) + ['none'])
    if k != 'none':
        t = rnd.choice(kinds[k])
        items.append(item(t, rnd.random() < 0.9, rnd.choice([0, 1, 1, 3, 12])))
        if k in ('bow', 'xbow') and rnd.random() < 0.8:
            items.append(item(ARROWS if k == 'bow' else BOLTS, rnd.random() < 0.85, rnd.choice([0, 1, 2, 5, 20])))
    if rnd.random() < 0.15:
        items.append(item(rnd.choice([ARROWS, BOLTS]), True, rnd.randrange(30)))
    effects = []
    for eid in (0x27, 0x2A, 0x3A):
        if rnd.random() < 0.18:
            effects.append(effect(eid, rnd.choice([0, 0x10, 0x15, 0xFF]), rnd.randrange(30)))
    if rnd.random() < 0.3:
        effects.insert(rnd.randrange(len(effects) + 1), effect(rnd.choice([0x19, 0x0B, 0x21, 0x31])))
    if rnd.random() < 0.08 and effects:
        effects.append(effects[0])  # a second record of the same id
    return {'record': r.hex(), 'effects': effects, 'items': items}


def round_cases(o, rnd, n):
    e = o.e
    kinds = weapon_types(o.base)
    out = []
    for i in range(n):
        k = rnd.randint(1, 14)
        cs = [round_creature(rnd, kinds, 0 if j < k // 2 else rnd.choice([1, 1, 1, 0xFF, 2])) for j in range(k)]
        ptrs = o.build(cs)
        for p in ptrs:
            o.set_equipment(p)
        combats = []
        for p in ptrs:
            cb = bytes(rnd.randrange(256) for _ in range(0x16))
            e.wb(o.combat(p), cb)
            combats.append(cb.hex())
        surprise = rnd.choice([0, 0, 1, 2, 3, rnd.randrange(0x10000)])
        partymove = rnd.choice([0, 0, 1, 3, 0xFE, rnd.randrange(0x10000)])
        o.fresh_zone1(surprise, partymove)
        counter = rnd.randrange(256)
        e.set_dsb(0x6DE3, counter)
        e.set_dsb(0x49F3, 0)
        seed = rnd.getrandbits(32)
        e.set_randseed(seed)
        o.map_mode = False
        cases_in = [{'record': o.read_creature(p)['record'], 'effects': c['effects'], 'items': o.read_creature(p)['items']}
                    for p, c in zip(ptrs, cs)]
        for p in ptrs:
            e.call(ROUND_START, [p[0], p[1]])
        started = [o.read_creature(p) for p in ptrs]
        start_combat = [o.read_combat(p) for p in ptrs]
        seed_start = e.randseed()
        e.wb(o.lin(o.zone1_ptr()) + 0x596, b'\0\0')
        picks = []
        outp = e.alloc(4)
        index = {p: j for j, p in enumerate(ptrs)}
        for _ in range(40):
            e.call(NEXT_CREATURE, [outp[0], outp[1]])
            off, sg = struct.unpack('<HH', e.rb(o.lin(outp), 4))
            if (sg, off) == (0, 0):
                picks.append(-1)
                break
            j = index[(sg, off)]
            picks.append(j)
            e.wb(o.combat(ptrs[j]) + 3, [0])
        seed_after = e.randseed()
        # s52: after the picks, the highest enemy movement 0x15103 for each creature and the hostile HP percent 0x15074
        fastest = [e.call(FASTEST_ENEMY, [p[0], p[1]])[0] & 0xFF for p in ptrs]
        e.set_dsb(0x6E2F, 0xEE)
        e.call(HP_PERCENT)
        out.append({'creatures': cases_in, 'combat': combats, 'surprise': surprise, 'partyMove': partymove,
                    'roundCounter': counter, 'seed': seed, 'started': started, 'startedCombat': start_combat,
                    'seedStarted': seed_start, 'picks': picks, 'seedAfter': seed_after,
                    'fastest': fastest, 'hpPercent': e.dsb(0x6E2F), 'seedEnd': e.randseed()})
    return out


def pick_cases(o, rnd, n):
    e = o.e
    out = []
    for i in range(n):
        k = rnd.randint(0, 16)
        cs = [{'record': bytes(so.REC).hex(), 'effects': [], 'items': []} for _ in range(k)]
        ptrs = o.build(cs)
        inits = [rnd.choice([0, 0, 1, 20, 7, 7, 0x80, 0xFF, 0xF0, 0x7F, rnd.randrange(256), rnd.randrange(21)])
                 for _ in range(k)]
        for p, v in zip(ptrs, inits):
            e.wb(o.combat(p) + 3, [v])
        seed = rnd.getrandbits(32)
        e.set_randseed(seed)
        outp = e.alloc(4)
        index = {p: j for j, p in enumerate(ptrs)}
        picks, zeroed = [], []
        for _ in range(rnd.randint(1, 4)):
            e.wb(o.lin(outp), struct.pack('<HH', 0x1234, 0x5678))  # the routine leaves it alone unless it picks
            e.call(NEXT_CREATURE, [outp[0], outp[1]])
            off, sg = struct.unpack('<HH', e.rb(o.lin(outp), 4))
            picks.append(-1 if (sg, off) == (0, 0) else index[(sg, off)])
            zeroed.append(picks[-1] >= 0 and rnd.random() < 0.7)
            if zeroed[-1]:
                e.wb(o.combat(ptrs[picks[-1]]) + 3, [0])
        out.append({'initiatives': inits, 'seed': seed, 'picks': picks, 'zeroed': zeroed, 'seedAfter': e.randseed(),
                    'after': [e.rb(o.combat(p) + 3, 1)[0] for p in ptrs]})
    return out


def map_creature(rnd, hostile, **kw):
    r = bytearray(so.creature(rnd))
    r[0x10D] = kw.get('inplay', 1)
    r[0x10C] = 0
    r[0x10E] = hostile
    r[0x10F] = kw.get('auto', rnd.randrange(2))
    r[0x6C] = 1
    effects = []
    if rnd.random() < 0.15:
        effects.append(effect(rnd.choice(HELPLESS)))
    if rnd.random() < 0.1:
        effects.append(effect(rnd.choice([0x1E, 0x20, 0x6F, 0x7D, 0x19])))
    return {'record': r.hex(), 'effects': effects, 'items': []}


def place(rnd, cx, cy, spread, taken):
    for _ in range(300):
        x, y = cx + rnd.randint(-spread, spread), cy + rnd.randint(-spread, spread)
        if 1 <= x < 49 and 1 <= y < 24 and (x, y) not in taken:
            taken.add((x, y))
            return x, y
    raise RuntimeError('no free square')


def move_cases(o, rnd, n):
    e = o.e
    out = []
    for i in range(n):
        cells = bytearray(FLOOR if rnd.random() > 0.12 else rnd.choice([1, 5, 26, 29, CLOUD]) for _ in range(1250))
        cx, cy = rnd.randrange(4, 46), rnd.randrange(4, 20)
        mover_hostile = rnd.randrange(2)
        taken = {(cx, cy)}
        cs = [map_creature(rnd, mover_hostile, inplay=1)]
        places = [(cx, cy, 1)]
        for _ in range(rnd.randint(0, 9)):
            h = (1 - mover_hostile) if rnd.random() < 0.75 else mover_hostile
            cs.append(map_creature(rnd, h, inplay=0 if rnd.random() < 0.08 else 1))
            x, y = place(rnd, cx, cy, 2, taken)
            places.append((x, y, 1))
        if rnd.random() < 0.3:  # the mover in a stinking cloud or helpless before the step
            cs[0]['effects'].append(effect(rnd.choice(HELPLESS)))
        ptrs = o.build(cs)
        o.map_mode = True
        o.setup_map({'cells': cells.hex(), 'places': places}, ptrs)
        e.set_dsb(0x49F3, 5)
        e.set_dsb(0x6DE3, rnd.randrange(2))
        o.fresh_zone1(0, 0)
        combats = []
        for j, p in enumerate(ptrs):
            cb = bytearray(rnd.randrange(256) for _ in range(0x16))
            cb[0x0A:0x0E] = b'\0\0\0\0'
            if j == 0:
                cb[6] = rnd.choice([0, 1, 2, 3, 4, 5, 12, 0x18, 255])
            cb[7] = rnd.choice([0, 0, 1, 2])
            cb[9] = rnd.randrange(8)
            e.wb(o.combat(p), cb)
            combats.append(cb.hex())
        d = rnd.randrange(8)
        o.attacks = []
        o.handlers = []
        o.knock = None
        if rnd.random() < 0.35:
            o.knock = (rnd.randint(1, 2), rnd.choice(['out', 'helpless']))
        seed = rnd.getrandbits(32)
        e.set_randseed(seed)
        e.call(MOVE_ONE, [ptrs[0][0], ptrs[0][1], d])
        index = {p: j for j, p in enumerate(ptrs)}
        count = e.dsb(0x5F2A)
        out.append({'creatures': cs, 'combat': combats, 'cells': cells.hex(), 'places': places, 'dir': d,
                    'knock': list(o.knock) if o.knock else None, 'seed': seed,
                    'after': [o.read_creature(p) for p in ptrs], 'afterCombat': [o.read_combat(p) for p in ptrs],
                    'slots': [list(e.rb(e.ds(0x5F27 + 4 * k), 4)) for k in range(1, count)],
                    'grid': e.rb(e.ds(0x60DB), 25 * 50).hex(),
                    'attacks': [[index[a], index[b], f, m] for a, b, f, m in o.attacks],
                    'seedAfter': e.randseed()})
    return out


def terrain_cases(o, rnd, n):
    e = o.e
    out = []
    for i in range(n):
        cells = bytearray(FLOOR for _ in range(1250))
        k = rnd.randint(1, 6)
        taken = set()
        cs, places = [], []
        for j in range(k):
            c = map_creature(rnd, rnd.randrange(2), inplay=0 if rnd.random() < 0.1 else 1)
            r = bytearray.fromhex(c['record'])
            for s in range(5):
                r[0x6D + s] = rnd.randrange(2, 21)
            c['record'] = r.hex()
            if rnd.random() < 0.2:
                c['effects'].append(effect(rnd.choice([0x1E, 0x1F, 0x20, 0x6F, 0x7D])))
            cs.append(c)
            x, y = place(rnd, 25, 12, 4, taken)
            places.append((x, y, 1))
            cells[y * 50 + x] = CLOUD if rnd.random() < 0.8 else rnd.choice([FLOOR, 1, 26])
        ptrs = o.build(cs)
        o.map_mode = True
        o.setup_map({'cells': cells.hex(), 'places': places}, ptrs)
        e.set_dsb(0x49F3, 5)
        o.fresh_zone1(0, 0)
        o.handlers = []
        seed = rnd.getrandbits(32)
        e.set_randseed(seed)
        index = {p: j for j, p in enumerate(ptrs)}
        for p in ptrs:
            e.call(TERRAIN, [p[0], p[1]])
        out.append({'creatures': cs, 'cells': cells.hex(), 'places': places, 'seed': seed,
                    'after': [o.read_creature(p) for p in ptrs], 'afterCombat': [o.read_combat(p) for p in ptrs],
                    'handlers': [[hid, index.get(c, -1), rec != (0, 0), rm] for hid, c, rec, rm in o.handlers],
                    'seedAfter': e.randseed()})
    return out


def free_cases(o, rnd, n):
    """s52: the free attacks 0x130AD before a step (MakeAttack trapped and logged with the attacker's phase +4 and
    attacks 0x113 / 0x114 as 0x130AD left them); the mover cannot be knocked out (the trap does nothing)."""
    e = o.e
    out = []
    for i in range(n):
        cells = bytearray(FLOOR for _ in range(1250))
        cx, cy = rnd.randrange(4, 46), rnd.randrange(4, 20)
        mover_hostile = rnd.randrange(2)
        taken = {(cx, cy)}
        cs = [map_creature(rnd, mover_hostile, inplay=1 if rnd.random() < 0.95 else 0)]
        r0 = bytearray.fromhex(cs[0]['record'])
        r0[0x11B] = r0[0x32] = 255  # the Core test runs MakeAttack whole: the mover must stay in play
        cs[0]['record'] = r0.hex()
        places = [(cx, cy, 1)]
        for _ in range(rnd.randint(1, 9)):
            h = (1 - mover_hostile) if rnd.random() < 0.8 else mover_hostile
            c = map_creature(rnd, h, inplay=0 if rnd.random() < 0.08 else 1)
            r = bytearray.fromhex(c['record'])
            r[0xA1] = rnd.choice([0, 0, 1, 2])
            r[0x113] = rnd.choice([0, 0, 1, 2])
            r[0x114] = rnd.choice([0, 0, 1])
            c['record'] = r.hex()
            if rnd.random() < 0.12:
                c['effects'].append(effect(rnd.choice([0x4A, 0x4B])))
            cs.append(c)
            x, y = place(rnd, cx, cy, 1 if rnd.random() < 0.7 else 2, taken)
            places.append((x, y, 1))
        ptrs = o.build(cs)
        o.map_mode = True
        o.setup_map({'cells': cells.hex(), 'places': places}, ptrs)
        e.set_dsb(0x49F3, 5)
        o.fresh_zone1(0, 0)
        combats = []
        for j, p in enumerate(ptrs):
            cb = bytearray(rnd.randrange(256) for _ in range(0x16))
            cb[0x0A:0x0E] = b'\0\0\0\0'
            cb[3] = rnd.choice([0, 0, 1, 5, 0x80, 0xFF])
            cb[0x0F] = rnd.choice([0, 0, 1, 2])
            cb[9] = rnd.choice([rnd.randrange(8), rnd.randrange(8), 250, 255])
            e.wb(o.combat(p), cb)
            combats.append(cb.hex())
        d = rnd.randrange(8)
        o.attacks = []
        o.attack_state = []
        o.handlers = []
        o.knock = None
        seed = rnd.getrandbits(32)
        e.set_randseed(seed)
        e.call(FREE_ATTACKS, [ptrs[0][0], ptrs[0][1], d])
        index = {p: j for j, p in enumerate(ptrs)}
        out.append({'creatures': cs, 'combat': combats, 'cells': cells.hex(), 'places': places, 'dir': d,
                    'seed': seed, 'after': [o.read_creature(p) for p in ptrs],
                    'afterCombat': [o.read_combat(p) for p in ptrs],
                    'attacks': [[index[a], index[b], f, m] + st for (a, b, f, m), st in zip(o.attacks, o.attack_state)],
                    'seedAfter': e.randseed()})
    return out


def flee_cases(o, rnd, n):
    """s52: the flee 0x13465 (enemies in sight or not, speeds, the 1d2, 0x2BDF0 with status 3)."""
    e = o.e
    out = []
    for i in range(n):
        cells = bytearray(FLOOR for _ in range(1250))
        cx, cy = rnd.randrange(4, 46), rnd.randrange(4, 20)
        mover_hostile = rnd.randrange(2)
        taken = {(cx, cy)}
        cs = [map_creature(rnd, mover_hostile, inplay=1 if rnd.random() < 0.95 else 0)]
        places = [(cx, cy, 1)]
        for _ in range(rnd.randint(0, 6)):
            h = (1 - mover_hostile) if rnd.random() < 0.7 else mover_hostile
            cs.append(map_creature(rnd, h, inplay=0 if rnd.random() < 0.15 else 1))
            x, y = place(rnd, cx, cy, rnd.choice([2, 8, 15]), taken)
            places.append((x, y, 1))
        for c in cs:
            r = bytearray.fromhex(c['record'])
            r[0x6B] = rnd.choice([3, 6, 6, 9, 12, 12, rnd.randrange(16)])
            c['record'] = r.hex()
            if rnd.random() < 0.15:
                c['effects'].append(effect(rnd.choice([0x27, 0x2A]), 0xFF, 5))
        ptrs = o.build(cs)
        o.map_mode = True
        o.setup_map({'cells': cells.hex(), 'places': places}, ptrs)
        e.set_dsb(0x49F3, 5)
        o.fresh_zone1(0, rnd.choice([0, 0, 1, 3]))
        combats = []
        for j, p in enumerate(ptrs):
            cb = bytearray(rnd.randrange(256) for _ in range(0x16))
            cb[0x0A:0x0E] = b'\0\0\0\0'
            e.wb(o.combat(p), cb)
            combats.append(cb.hex())
        o.attacks = []
        o.attack_state = []
        o.handlers = []
        o.knock = None
        seed = rnd.getrandbits(32)
        e.set_randseed(seed)
        ax, _dx = e.call(FLEE, [ptrs[0][0], ptrs[0][1]])
        count = e.dsb(0x5F2A)
        out.append({'creatures': cs, 'combat': combats, 'cells': cells.hex(), 'places': places,
                    'partyMove': o.e.rb(o.lin(o.zone1_ptr()) + 0x6E4, 2).hex(), 'seed': seed, 'result': ax & 0xFF,
                    'after': [o.read_creature(p) for p in ptrs], 'afterCombat': [o.read_combat(p) for p in ptrs],
                    'slots': [list(e.rb(e.ds(0x5F27 + 4 * k), 4)) for k in range(1, len(ptrs) + 1)],
                    'seedAfter': e.randseed()})
    return out


def main(argv):
    if len(argv) != 3:
        print(__doc__)
        return 2
    o = RoundOracle(argv[1])
    o.zone1_ptr = lambda: o.e.dsptr(0x49D6)
    rnd = random.Random(20261122)
    out = {'source': 'GAME.OVR / START.EXE v1.3 run under unicorn (tools/scripts/por_round_oracle.py)',
           'items_5582': o.base.hex()}
    out['round'] = round_cases(o, rnd, 160)
    out['picks'] = pick_cases(o, rnd, 200)
    out['move'] = move_cases(o, rnd, 200)
    out['terrain'] = terrain_cases(o, rnd, 80)
    out['free'] = free_cases(o, rnd, 200)
    out['flee'] = flee_cases(o, rnd, 200)
    with open(argv[2], 'w') as f:
        json.dump(out, f, separators=(',', ':'))
    print('round %d, picks %d, move %d, terrain %d, free %d, flee %d' % tuple(len(out[k]) for k in ('round', 'picks', 'move', 'terrain', 'free', 'flee')))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
