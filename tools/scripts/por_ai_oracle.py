#!/usr/bin/env python3
"""AI casting and item fixtures from the program itself: runs the v1.3 routines (por_emu.py, unicorn) on generated
fights and writes what they produce, for GoldBox.Core's AiCastProgramTests (docs/re/combat-ai.md "Checked under
emulation").

Usage: python3 -I por_ai_oracle.py GAMEDIR OUT.json        (GAMEDIR: the PoR v1.3 files; needs unicorn 2.x)

Cases (fixed seeds, so the output is the same every run):
- cure: the cure-target test 0x143F3 around a caster, with bodies on the map;
- pick: the AI's target pick 0x148BE (ai = 1, which runs the aim 0x14611 and the target search 0x15FDF) for every
  spell, with a remembered target, helpless creatures and item use (DS:6DBF) mixed in; and the should-cast test 0xAEAB;
- use: the item use 0x22AC4 with the cast 0x27C38 trapped (spell, ai, DS:6DBF recorded; the pick's result scripted);
- stack: the Items screen's Halve (0x22827) and Join (0x228D7).
Effect handlers (dispatcher 0x2AEEA) are trapped to do nothing. The output holds no game data besides record bytes
made up here; it goes in tests/fixtures-private all the same.
"""
import json
import os
import random
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from por_combat_oracle import Oracle, REC  # noqa: E402
from por_emu import Emu  # noqa: E402
from unicorn.x86_const import UC_X86_REG_DX, UC_X86_REG_CS, UC_X86_REG_IP  # noqa: E402

CURE, PICK, SHOULD_CAST, USE, CAST, HALVE, JOIN = 0x143F3, 0x148BE, 0xAEAB, 0x22AC4, 0x27C38, 0x22827, 0x228D7
DISPATCH, MARK_SQUARE, SHOW_ITEM, MESSAGE, SCREEN_CLEAR = 0x2AEEA, 0x30A0F, (0xBA, 0x442), (0xBA, 0x16EC), (0xBA, 0x2B88)
GETMEM, FREEMEM = (0xAF8, 0x23F), (0xAF8, 0x254)
FLOOR, BODY = 23, 0x1F
SPELL_LIST = 0x239D6


def ptr_bytes(p):
    return struct.pack('<HH', p[1], p[0]) if p else b'\0\0\0\0'


class AiOracle(Oracle):
    def __init__(self, gamedir):
        super().__init__(gamedir)
        e = self.e
        e.trap_ovr(DISPATCH, Emu.ret(0xC))
        e.trap_ovr(MARK_SQUARE, Emu.ret(4))

        def getmem(em):  # Turbo Pascal GetMem(size): a block from the emulator's arena, returned in DX:AX
            seg, off = em.alloc(em.arg(0))
            em.uc.reg_write(UC_X86_REG_DX, seg)
            em.pop_ret(2, off)
        e.trap_start(*GETMEM, getmem)
        e.trap_start(*FREEMEM, Emu.ret(6))
        self.spell_table = e.S[e.ds(0x31FA):e.ds(0x31FA) + 16 * 68]
        # the base item table DS:5582 is loaded from ITEMS at run time (2-byte header, 128 x 16)
        from por_combat_oracle import find
        with open(find(gamedir, 'ITEMS'), 'rb') as f:
            self.base_items = f.read()[2:2 + 16 * 128]
        e.wb(e.ds(0x5582), self.base_items)

    def spell(self, s):
        return self.spell_table[16 * s:16 * s + 16]

    def creatures(self, specs):
        """make_list plus the bytes these routines read: HP, max HP, levels, effects, auto-combat."""
        e = self.e
        ptrs = self.make_list(specs)
        for p, s in zip(ptrs, specs):
            rl = self.lin(p)
            e.wb(rl + 0x11B, [s.get('hp', 0)])
            e.wb(rl + 0x32, [s.get('maxhp', 0)])
            e.wb(rl + 0x10C, [s.get('status', 0 if s['inplay'] else 4)])
            e.wb(rl + 0x96, [s.get('cleric', 0)])
            e.wb(rl + 0x9B, [s.get('mage', 0)])
            e.wb(rl + 0x10F, [s.get('auto', 1)])
            prev = None
            for eid in reversed(s.get('effects', [])):
                r = e.alloc(9)
                e.wb(self.lin(r), bytes([eid, 0, 0, 0xFF, 0]) + ptr_bytes(prev))
                prev = r
            e.wb(rl + 0x7F, ptr_bytes(prev))
        return ptrs

    def combat_rec(self, p):
        o, s = struct.unpack('<HH', self.e.rb(self.lin(p) + 0x108, 4))
        return s * 16 + o

    def set_other(self, p, other):
        self.e.wb(self.combat_rec(p) + 0x0A, ptr_bytes(other))

    def other(self, p):
        o, s = struct.unpack('<HH', self.e.rb(self.combat_rec(p) + 0x0A, 4))
        return (s, o)

    def set_bodies(self, bodies):
        e = self.e
        for k, (p, x, y) in enumerate(bodies, 1):
            e.wb(e.ds(0x66D6 + 7 * k), ptr_bytes(p) + bytes([x & 0xFF, y & 0xFF, FLOOR]))
        e.set_dsb(0x6715, len(bodies))


def random_cells(rnd, obstacles):
    terrains = [23] * 12 + [1, 5, 10, 26, 27, 28, 29, 30, 31, 36, 37, 38, 39, 40, 41, 50, 52, 56, 60, 62, 64, 65]
    return bytearray(rnd.choice(terrains) if rnd.random() < obstacles else FLOOR for _ in range(1250))


def place_near(rnd, cx, cy, spread, taken):
    for _ in range(200):
        x, y = cx + rnd.randint(-spread, spread), cy + rnd.randint(-spread, spread)
        if 0 <= x < 50 and 1 <= y < 24 and (x, y) not in taken:
            taken.add((x, y))
            return x, y
    raise RuntimeError('no free square')


def cure_cases(o, rnd, n):
    e = o.e
    out = []
    for _ in range(n):
        o.fresh()
        cells = random_cells(rnd, 0.1)
        cx, cy = rnd.randrange(3, 47), rnd.randrange(3, 22)
        taken = {(cx, cy)}
        k = rnd.randint(1, 9)
        caster_side = rnd.randrange(2)
        specs = [{'size': 1, 'inplay': 1, 'hostile': caster_side}]
        places = [(cx, cy, 1)]
        for _j in range(k):
            x, y = place_near(rnd, cx, cy, 2, taken)
            size = 1 if rnd.random() < 0.85 else 2
            specs.append({'size': size, 'inplay': 1, 'hostile': caster_side if rnd.random() < 0.6 else 1 - caster_side})
            places.append((x, y, size))
        for s in specs:
            s['maxhp'] = rnd.randint(1, 60)
            s['hp'] = s['maxhp'] if rnd.random() < 0.25 else rnd.randint(0, s['maxhp'])
        nb = rnd.choice([0, 0, 1, 2, 3])
        body_specs = []
        for _j in range(nb):
            x, y = place_near(rnd, cx, cy, 1, taken) if rnd.random() < 0.8 else place_near(rnd, cx, cy, 3, taken)
            body_specs.append((x, y))
            cells[y * 50 + x] = BODY
            specs.append({'size': 0, 'inplay': 0, 'hostile': rnd.randrange(2),
                          'status': rnd.choice([4, 5, 5, 6, 7, 8]), 'hp': 0, 'maxhp': 10})
        if rnd.random() < 0.2:  # a body cell with no entry
            x, y = place_near(rnd, cx, cy, 1, taken)
            cells[y * 50 + x] = BODY
        o.set_cells(bytes(cells))
        ptrs = o.creatures(specs)
        on_map = len(places)
        o.set_slots(ptrs[:on_map], places)
        bodies = [(ptrs[on_map + j], x, y) for j, (x, y) in enumerate(body_specs)]
        if bodies and rnd.random() < 0.2:
            bodies[0] = (None, bodies[0][1], bodies[0][2])  # an emptied entry
        o.set_bodies(bodies)
        res = e.alloc(4)
        e.set_dsptr(0x5D92, ptrs[0])
        r = e.call(CURE, [*ptrs[0], *res])[0] & 0xFF
        got = struct.unpack('<HH', e.rb(o.lin(res), 4))
        index = {p: i for i, p in enumerate(ptrs)}
        out.append({'cells': bytes(cells).hex(), 'creatures': specs,
                    'places': [list(p) for p in places],
                    'bodies': [{'creature': index.get(p, -1) if p else -1, 'x': x, 'y': y} for p, x, y in bodies],
                    'result': r, 'target': index.get((got[1], got[0]), -1)})
    return out


def pick_cases(o, rnd, spells):
    e = o.e
    out = []
    for spell in spells:
        for _rep in range(4):
            o.fresh()
            cells = random_cells(rnd, 0.2)
            o.set_cells(bytes(cells))
            k = rnd.randint(2, 12)
            caster_side = rnd.randrange(2)
            specs, places, taken = [], [], set()
            cx, cy = rnd.randrange(5, 45), rnd.randrange(4, 20)
            for j in range(k):
                if j == 0:
                    x, y = cx, cy
                    taken.add((x, y))
                else:
                    x, y = place_near(rnd, cx, cy, rnd.choice([1, 2, 4, 8]), taken)
                size = 1 if rnd.random() < 0.85 else rnd.choice([2, 3, 4])
                side = caster_side if j == 0 else (caster_side if rnd.random() < 0.35 else 1 - caster_side)
                maxhp = rnd.randint(1, 40)
                effects = []
                if rnd.random() < 0.2:
                    effects.append(rnd.choice([0x33, 0x34, 0x35, 0x1F]))
                specs.append({'size': size, 'inplay': 1, 'hostile': side, 'maxhp': maxhp,
                              'hp': rnd.randint(0, maxhp), 'cleric': rnd.randint(0, 9), 'mage': rnd.randint(0, 9),
                              'effects': effects})
                places.append((x, y, size))
            ptrs = o.creatures(specs)
            o.set_slots(ptrs, places)
            other = rnd.choice([None, None] + list(range(k)))
            o.set_other(ptrs[0], ptrs[other] if other is not None else None)
            item = 1 if rnd.random() < 0.25 else 0
            e.set_dsb(0x6DBF, item)
            e.set_dsptr(0x5D92, ptrs[0])
            seed = rnd.getrandbits(32)
            prio = rnd.randrange(8)
            # the should-cast test first (no draws of its own besides the cure test's none)
            e.set_randseed(seed)
            e.set_dsb(0x6DBF, 0)
            should = e.call(SHOULD_CAST, [*ptrs[0], spell, prio])[0] & 0xFF
            should_seed = e.randseed()
            e.set_dsb(0x6DBF, item)
            o.set_other(ptrs[0], ptrs[other] if other is not None else None)
            e.set_randseed(seed)
            e.set_dsb(0x6C94, 0)
            flag = e.alloc(2)
            e.wb(o.lin(flag), b'\xee')
            e.call(PICK, [spell, 1, *flag])
            index = {p: i for i, p in enumerate(ptrs)}
            n = e.dsb(0x6C94)
            lst = []
            for i in range(1, n + 1):
                off, sg = struct.unpack('<HH', e.rb(e.ds(0x6C91 + 4 * i), 4))
                lst.append(index.get((sg, off), -1))
            oth = o.other(ptrs[0])
            out.append({'spell': spell, 'cells': bytes(cells).hex(), 'creatures': specs,
                        'places': [list(p) for p in places], 'other': -1 if other is None else other, 'item': item,
                        'seed': seed, 'priority': prio, 'should': should, 'shouldSeed': should_seed,
                        'picked': e.rb(o.lin(flag), 1)[0], 'targets': lst, 'multi': e.dsb(0x6820),
                        'aimed': [e.dsb(0x6DB9), e.dsb(0x6DBA)],
                        'otherAfter': index.get(oth, -1) if oth != (0, 0) else -1, 'seedAfter': e.randseed()})
    return out


def item_record(rnd, items_slot):
    """A 63-byte item: type, readied, charges / power / kind, count."""
    b = bytearray(63)
    b[0x2E] = rnd.choice(items_slot)
    b[0x30] = rnd.choice([0xD2, 0xD3, 0xD5])
    b[0x34] = rnd.randrange(2)
    b[0x39] = rnd.choice([0, 0, 1, 2, 5])
    b[0x3C] = rnd.choice([0, 1, 1, 2, 7])
    b[0x3D] = rnd.choice([0, 3, 15, 21, 0x39, 0x50, 0x55, 0x5A])
    b[0x3E] = rnd.choice([0, 0, 0x10, 0x81])
    return b


def use_cases(o, rnd, n):
    e = o.e
    base = o.base_items
    scroll_types = [t for t in range(128) if 11 <= base[16 * t] <= 13]
    other_types = [t for t in range(128) if base[16 * t] not in (11, 12, 13)]
    seen = {}

    def on_cast(em):
        # 0x27C38(spell, ai, b, var done): arguments above the far return address, last pushed first
        seen['spell'] = em.arg(8) & 0xFF
        seen['ai'] = em.arg(6) & 0xFF
        seen['b'] = em.arg(4) & 0xFF
        seen['flag'] = em.dsb(0x6DBF)
        em.wb(em.arg(2) * 16 + em.arg(0), [seen['done']])
        em.pop_ret(10)

    e.trap_ovr(CAST, on_cast)
    e.trap_ovr(SPELL_LIST, lambda em: em.pop_ret(12, seen['scroll']))
    e.trap_start(*SHOW_ITEM, Emu.ret(16))
    e.trap_start(*MESSAGE, Emu.ret(12))
    e.trap_start(*SCREEN_CLEAR, Emu.ret(0))
    e.trap_start(0x709, 0xDBE, Emu.ret(0))
    e.trap_start(0xBA, 0x17E3, Emu.ret(0))
    out = []
    for i in range(n):
        o.fresh()
        specs = [{'size': 1, 'inplay': 1, 'hostile': 0, 'auto': rnd.randrange(2)}]
        ptrs = o.creatures(specs)
        user = ptrs[0]
        recs = []
        nitems = rnd.randint(1, 4)
        for _ in range(nitems):
            recs.append(item_record(rnd, scroll_types if rnd.random() < 0.2 else other_types))
        if any(11 <= base[16 * r[0x2E]] <= 13 for r in recs):
            for r in recs:
                if 11 <= base[16 * r[0x2E]] <= 13:
                    r[0x3C], r[0x3D], r[0x3E] = rnd.choice([(3, 0, 0), (3, 3, 0x83), (0, 21, 3), (21, 21, 0)])
        iptrs = [e.alloc(63) for _ in recs]
        for j, (p, r) in enumerate(zip(iptrs, recs)):
            r[0x2A:0x2E] = ptr_bytes(iptrs[j + 1] if j + 1 < len(iptrs) else None)
            e.wb(o.lin(p), bytes(r))
        e.wb(o.lin(user) + 0xC8, ptr_bytes(iptrs[0]))
        e.set_dsptr(0x5D92, user)
        e.set_dsb(0x49F3, 5)
        which = rnd.randrange(len(recs))
        seen.clear()
        seen['done'] = rnd.randrange(2)
        scripted = seen['done']
        cr = o.combat_rec(user)
        e.wb(cr, bytes([7, 1, 1, 12, 0, 0, 9, 1]))
        done = e.alloc(2)
        e.wb(o.lin(done), b'\xee')
        is_scroll = 11 <= base[16 * recs[which][0x2E]] <= 13
        # the spell list 0x239D6 would ask which scroll spell: the first spell byte of the scroll
        seen['scroll'] = next((b for b in recs[which][0x3C:0x3F] if b), 0) if is_scroll else 0
        e.call(USE, [*iptrs[which], *done])
        after = []
        p = struct.unpack('<HH', e.rb(o.lin(user) + 0xC8, 4))
        index = {q: j for j, q in enumerate(iptrs)}
        while p != (0, 0) and len(after) < 10:
            q = (p[1], p[0])
            after.append({'index': index[q], 'bytes': e.rb(o.lin(q) + 0x2E, 63 - 0x2E).hex()})
            p = struct.unpack('<HH', e.rb(o.lin(q) + 0x2A, 4))
        out.append({'items': [bytes(r).hex() for r in recs], 'which': which, 'auto': specs[0]['auto'],
                    'scripted': scripted, 'scroll': seen['scroll'],
                    'cast': {k: seen[k] for k in ('spell', 'ai', 'b', 'flag')} if 'spell' in seen else None,
                    'done': e.rb(o.lin(done), 1)[0], 'record': e.rb(cr, 8).hex(), 'after': after})
    return out


def stack_cases(o, rnd, n):
    """Halve (0x22827) and Join (0x228D7) on lists of look-alike items."""
    e = o.e
    e.trap_start(0xBA, 0x1693, Emu.ret(4))
    out = []
    for i in range(n):
        o.fresh()
        ptrs = o.creatures([{'size': 1, 'inplay': 1, 'hostile': 0}])
        user = ptrs[0]
        proto = bytearray(63)
        proto[0x2E], proto[0x2F], proto[0x30], proto[0x31] = 0x49, 0, 0xD2, 0
        proto[0x37] = 1
        recs = []
        for _ in range(rnd.randint(1, 7)):
            r = bytearray(proto)
            r[0x39] = rnd.choice([0, 1, 2, 3, 20, 100, 200, 255])
            r[0x34] = rnd.randrange(2)
            r[0x35] = rnd.randrange(2)
            r[0x3A] = rnd.randrange(3)
            r[0x3C] = rnd.choice([0, 0, 1, 2, 3])
            r[0x3D] = rnd.choice([0, 0, 5])
            r[0x3E] = rnd.choice([0, 0, 0x81])
            if rnd.random() < 0.25:  # differs in a compared byte
                r[rnd.choice([0x2E, 0x2F, 0x30, 0x31, 0x32, 0x33, 0x36, 0x37, 0x38])] ^= 1
            recs.append(r)
        iptrs = [e.alloc(63) for _ in recs]
        for j, (p, r) in enumerate(zip(iptrs, recs)):
            r[0x2A:0x2E] = ptr_bytes(iptrs[j + 1] if j + 1 < len(iptrs) else None)
            e.wb(o.lin(p), bytes(r))
        e.wb(o.lin(user) + 0xC8, ptr_bytes(iptrs[0]))
        e.set_dsptr(0x5D92, user)
        which = rnd.randrange(len(recs))
        op = 'halve' if i % 2 == 0 else 'join'
        e.call(HALVE if op == 'halve' else JOIN, [*iptrs[which]])
        index = {q: j for j, q in enumerate(iptrs)}
        after = []
        p = struct.unpack('<HH', e.rb(o.lin(user) + 0xC8, 4))
        while p != (0, 0) and len(after) < 20:
            q = (p[1], p[0])
            after.append({'index': index.get(q, -1), 'bytes': e.rb(o.lin(q) + 0x2E, 63 - 0x2E).hex()})
            p = struct.unpack('<HH', e.rb(o.lin(q) + 0x2A, 4))
        out.append({'op': op, 'items': [bytes(r).hex() for r in recs], 'which': which, 'after': after})
    return out


# ---- s52: the AI unit's own routines (0xBE4D, 0xB235, 0xBC97, 0xBB0F, 0xAFA4, 0xB105) ---------------------------------

WORTH, CAN_STEP, MORALE, FINISH, ITEM_PICK, SPELL_PICK = 0xBE4D, 0xB235, 0xBC97, 0xBB0F, 0xAFA4, 0xB105
STATUS_CHANGE, CAST_AUTO, GUARD_MSG = 0x2BDF0, 0x14C0F, (0xBA, 0x1693)


def set_bytes(o, p, bts):
    for off, v in bts.items():
        o.e.wb(o.lin(p) + int(off), [v & 0xFF])


def worth_cases(o, rnd, n):
    """0xBE4D(creature, item): a weapon's worth."""
    e = o.e
    weapons = [t for t in range(128) if o.base_items[16 * t] == 0]
    out = []
    for _ in range(n):
        o.fresh()
        specs = [{'size': 1, 'inplay': 1, 'hostile': 1,
                  'bytes': {0x100: rnd.choice([0, 1, 2, 3]), 0xA0: rnd.randrange(9)}},
                 {'size': 1, 'inplay': 1, 'hostile': 0, 'bytes': {0x76: rnd.choice([0, 0, 1, 0x80, 5])}}]
        ptrs = o.creatures(specs)
        for p, s in zip(ptrs, specs):
            set_bytes(o, p, s['bytes'])
        other = rnd.random() < 0.7
        o.set_other(ptrs[0], ptrs[1] if other else None)
        b = bytearray(63)
        b[0x2E] = rnd.choice(weapons + [0x55] * 4)
        b[0x32] = rnd.choice([0, 0, 1, 2, 3, 0xFF, 0x80, 0x7F])
        b[0x36] = rnd.choice([0] * 6 + [1])
        b[0x3D] = rnd.choice([0, 0, 0x53, 0x13, 0x21, 5])
        b[0x3E] = rnd.choice([0, 0, 0x84])
        it = e.alloc(63)
        e.wb(o.lin(it), bytes(b))
        r = e.call(WORTH, [*ptrs[0], *it])[0] & 0xFF
        out.append({'creatures': specs, 'other': int(other), 'item': bytes(b).hex(), 'worth': r})
    return out


def step_cases(o, rnd, n):
    """0xB235(creature, attempt, direction, var edge): can the AI step there."""
    e = o.e
    out = []
    for _ in range(n):
        o.fresh()
        cells = random_cells(rnd, 0.35)
        for k in range(rnd.randrange(0, 40)):
            cells[rnd.randrange(1250)] = rnd.choice([0, 0x1E, 0x1E])
        o.set_cells(bytes(cells))
        cx, cy = rnd.randrange(0, 50), rnd.randrange(0, 24)  # footprints stay on the arena
        taken = {(cx, cy)}
        specs = [{'size': 1 if rnd.random() < 0.8 else 2, 'inplay': 1, 'hostile': rnd.randrange(2),
                  'effects': [rnd.choice([0x20, 0x1E, 0x6F, 0x7D, 0x33, 9])] if rnd.random() < 0.3 else []}]
        places = [(cx, cy, specs[0]['size'])]
        for _j in range(rnd.randint(0, 5)):
            x, y = place_near(rnd, cx, cy, 2, taken)
            specs.append({'size': 1, 'inplay': 1, 'hostile': rnd.randrange(2)})
            places.append((x, y, 1))
        ptrs = o.creatures(specs)
        o.set_slots(ptrs, places)
        row, move = rnd.randint(1, 6), rnd.choice([0, 1, 2, 3, 4, 6, 8, 12, 24])
        cr = o.combat_rec(ptrs[0])
        e.wb(cr + 0x15, [row])
        e.wb(cr + 6, [move])
        attempt, base = rnd.randint(1, 5), rnd.randrange(8)
        edge = e.alloc(2)
        e.wb(o.lin(edge), b'\xee')
        r = e.call(CAN_STEP, [*ptrs[0], attempt, base, *edge])[0] & 0xFF
        out.append({'cells': bytes(cells).hex(), 'creatures': specs, 'places': [list(p) for p in places], 'row': row,
                    'move': move, 'attempt': attempt, 'base': base, 'result': r, 'edge': e.rb(o.lin(edge), 1)[0]})
    return out


def morale_cases(o, rnd, n):
    """0xBC97(creature): morale. The status change 0x2BDF0 (surrender) is trapped and logged."""
    e = o.e
    seen = []

    def status_change(em):
        seen.append(em.arg(4) & 0xFF)
        em.pop_ret(0xA)
    e.trap_ovr(STATUS_CHANGE, status_change)
    out = []
    for _ in range(n):
        o.fresh()
        k = rnd.randint(1, 6)
        specs = []
        for j in range(k):
            mx = rnd.randint(1, 80)
            specs.append({'size': 1, 'inplay': 1 if j == 0 or rnd.random() < 0.8 else 0, 'hostile': rnd.randrange(2),
                          'maxhp': mx, 'hp': rnd.randint(0, mx),
                          'effects': [rnd.choice([0x4A, 0x4B, 0x4A, 9, 0x2D]) for _x in range(rnd.choice([0, 0, 1, 2, 3]))],
                          'bytes': {0x84: rnd.choice([0, 0x10, 0x7F, 0x80, 0x85, 0xA0, 0xB2, 0xB4, 0xFF]),
                                    0x11: rnd.choice([3, 5, 6, 10, 18]), 0x11C: rnd.choice([0, 3, 6, 9, 12, 15, 30, 100])}})
        ptrs = o.creatures(specs)
        for p, s in zip(ptrs, specs):
            set_bytes(o, p, s['bytes'])
        forced = 1 if rnd.random() < 0.15 else 0
        e.wb(o.combat_rec(ptrs[0]) + 0x10, [forced])
        e.wb(o.combat_rec(ptrs[0]), bytes([1, 1, 1, 7, 0, 0, 6, 0]))
        side_pct = rnd.choice([0, 10, 50, 60, 90, 100])
        e.set_dsb(0x6E2F, side_pct)
        thr = rnd.choice([0, 10, 40, 50, 100, 120])
        e.wb(o.lin(o.zone1) + 0x58C, struct.pack('<H', thr))
        e.set_dsb(0x6825, 0xEE)
        seen.clear()
        r = e.call(MORALE, [*ptrs[0]])[0] & 0xFF
        eff = []
        p = struct.unpack('<HH', e.rb(o.lin(ptrs[0]) + 0x7F, 4))
        while p != (0, 0):
            q = (p[1], p[0])
            eff.append(e.rb(o.lin(q), 1)[0])
            p = struct.unpack('<HH', e.rb(o.lin(q) + 5, 4))
        out.append({'creatures': specs, 'forced': forced, 'sidePct': side_pct, 'threshold': thr, 'result': r,
                    'combat': e.rb(o.combat_rec(ptrs[0]), 22).hex(), 'morale': e.dsb(0x6825), 'effects': eff,
                    'surrender': list(seen)})
    return out


def finish_cases(o, rnd, n):
    """0xBB0F(creature): end the actions or guard."""
    e = o.e
    e.trap_start(*GUARD_MSG, Emu.ret(4))
    weapons = [t for t in range(128) if o.base_items[16 * t] == 0]
    out = []
    for _ in range(n):
        o.fresh()
        specs = [{'size': 1, 'inplay': 1, 'hostile': rnd.randrange(2), 'status': rnd.choice([0, 0, 0, 1, 2]),
                  'effects': [rnd.choice([0x33, 0x34, 0x35, 0x1F, 9])] if rnd.random() < 0.3 else []}]
        ptrs = o.creatures(specs)
        c = ptrs[0]
        item = None
        if rnd.random() < 0.6:
            b = bytearray(63)
            b[0x2E] = rnd.choice(weapons)
            b[0x34] = 1
            it = e.alloc(63)
            e.wb(o.lin(it), bytes(b))
            e.wb(o.lin(c) + 0xC8, ptr_bytes(it))
            e.wb(o.lin(c) + 0xCC, ptr_bytes(it))
            item = bytes(b).hex()
        init = rnd.choice([0, 0, 1, 5, 20])
        e.wb(o.combat_rec(c), bytes([2, 1, 1, init, 0, 0, 6, 0]))
        r = e.call(FINISH, [*c])[0] & 0xFF
        out.append({'creatures': specs, 'item': item, 'initiative': init, 'result': r,
                    'combat': e.rb(o.combat_rec(c), 22).hex()})
    return out


def pick_item_spell_cases(o, rnd, spells, n):
    """0xAFA4 (item) and 0xB105 (spell) with the should-cast test 0xAEAB run; the use 0x22AC4 and the cast 0x14C0F
    trapped and logged (the cast's result scripted)."""
    e = o.e
    seen = {}

    def use(em):
        seen['use'] = (em.arg(6), em.arg(4))
        em.pop_ret(8)

    def cast(em):
        seen['cast'] = em.arg(6) & 0xFF
        em.wb(em.arg(2) * 16 + em.arg(0), [seen['done']])
        em.pop_ret(8)
    e.trap_ovr(USE, use)
    e.trap_ovr(CAST_AUTO, cast)
    base = o.base_items
    scroll_types = [t for t in range(128) if 11 <= base[16 * t] <= 13]
    other_types = [t for t in range(128) if not 11 <= base[16 * t] <= 13]
    out = []
    for i in range(n):
        o.fresh()
        cells = random_cells(rnd, 0.15)
        o.set_cells(bytes(cells))
        cx, cy = rnd.randrange(5, 45), rnd.randrange(4, 20)
        taken = {(cx, cy)}
        side = rnd.randrange(2)
        specs, places = [], []
        for j in range(rnd.randint(2, 8)):
            x, y = (cx, cy) if j == 0 else place_near(rnd, cx, cy, rnd.choice([1, 2, 4, 8]), taken)
            mx = rnd.randint(1, 40)
            specs.append({'size': 1, 'inplay': 1, 'hostile': side if j == 0 else (side if rnd.random() < 0.3 else 1 - side),
                          'maxhp': mx, 'hp': rnd.randint(0, mx), 'cleric': rnd.randint(0, 9), 'mage': rnd.randint(0, 9)})
            places.append((x, y, 1))
        mem = [0] * 21
        for k in range(21):
            if rnd.random() < 0.2:
                mem[k] = rnd.choice(spells)  # no pending ids (+0x80): Core leaves them out by decision (s50)
        specs[0]['bytes'] = {0x84: rnd.choice([0, 0x80, 0xFF])}
        for k in range(21):
            specs[0]['bytes'][0x17 + k] = mem[k]
        ptrs = o.creatures(specs)
        set_bytes(o, ptrs[0], specs[0]['bytes'])
        o.set_slots(ptrs, places)
        enemies = sum(1 for s in specs if s['hostile'] != side and s['inplay'])
        e.set_dsb(0x6814, enemies if side == 1 else rnd.choice([0, 3]))
        e.set_dsb(0x6815, enemies if side == 0 else rnd.choice([0, 3]))
        recs = []
        for _k in range(rnd.choice([0, 1, 2, 3])):
            b = bytearray(63)
            b[0x2E] = rnd.choice(scroll_types if rnd.random() < 0.2 else other_types)
            b[0x34] = rnd.choice([0, 1, 1])
            b[0x3D] = rnd.choice([0, rnd.choice(spells), rnd.choice(spells) + 0x17, 0x39, 0x50])
            b[0x3E] = rnd.choice([0, 0, 1, 0x80, 0x84])
            recs.append(b)
        iptrs = [e.alloc(63) for _ in recs]
        for j, (p, r) in enumerate(zip(iptrs, recs)):
            r[0x2A:0x2E] = ptr_bytes(iptrs[j + 1] if j + 1 < len(iptrs) else None)
            e.wb(o.lin(p), bytes(r))
        e.wb(o.lin(ptrs[0]) + 0xC8, ptr_bytes(iptrs[0] if iptrs else None))
        e.wb(o.combat_rec(ptrs[0]), bytes([0, rnd.choice([0, 1, 1]), rnd.choice([0, 1, 1]), 7, 0, 0, 6, 0]))
        quick = rnd.randrange(2)
        e.set_dsb(0x6E30, quick)
        anti = 1 if rnd.random() < 0.1 else 0
        e.wb(o.lin(o.zone0) + 0x1CA, struct.pack('<H', anti))
        e.set_dsptr(0x5D92, ptrs[0])
        seed = rnd.getrandbits(32)
        e.set_randseed(seed)
        seen.clear()
        seen['done'] = rnd.randrange(2)
        op = 'item' if i % 2 == 0 else 'spell'
        r = e.call(ITEM_PICK if op == 'item' else SPELL_PICK, [*ptrs[0]])[0] & 0xFF
        out.append({'op': op, 'cells': bytes(cells).hex(), 'creatures': specs, 'places': [list(p) for p in places],
                    'teams': [e.dsb(0x6814), e.dsb(0x6815)], 'items': [bytes(r).hex() for r in recs],
                    'combat': e.rb(o.combat_rec(ptrs[0]), 8).hex(), 'quick': quick, 'anti': anti, 'seed': seed,
                    'scripted': seen['done'], 'result': r, 'seedAfter': e.randseed(),
                    'used': iptrs.index(seen['use']) if 'use' in seen else -1, 'cast': seen.get('cast', 0)})
    return out


def main(argv):
    if len(argv) != 3:
        print(__doc__)
        return 2
    o = AiOracle(argv[1])
    rnd = random.Random(20261117)
    out = {'source': 'GAME.OVR / START.EXE v1.3 run under unicorn (tools/scripts/por_ai_oracle.py)'}
    out['cure'] = cure_cases(o, rnd, 200)
    spells = [s for s in range(1, 68) if o.spell(s)[11] != 0]
    out['pick'] = pick_cases(o, rnd, spells)
    out['use'] = use_cases(o, rnd, 120)
    out['stack'] = stack_cases(o, rnd, 160)
    # s52 (appended so the sections above keep their draws)
    out['worth'] = worth_cases(o, rnd, 300)
    out['step'] = step_cases(o, rnd, 400)
    out['morale'] = morale_cases(o, rnd, 300)
    out['finish'] = finish_cases(o, rnd, 150)
    out['choose'] = pick_item_spell_cases(o, rnd, spells, 400)
    with open(argv[2], 'w') as f:
        json.dump(out, f, separators=(',', ':'))
    print(' '.join('%s %d' % (k, len(v)) for k, v in out.items() if isinstance(v, list)))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
