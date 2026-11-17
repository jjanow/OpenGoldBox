#!/usr/bin/env python3
"""Combat-map fixtures from the program itself: runs the v1.3 routines (por_emu.py, unicorn) on generated inputs and
writes what they produce, for GoldBox.Core's CombatMapProgramTests (docs/re/combat-map.md "Checked under emulation").

Usage: python3 -I por_combat_oracle.py GAMEDIR OUT.json        (GAMEDIR: the PoR v1.3 files; needs unicorn 2.x)

Cases (fixed seeds, so the output is the same every run):
- dungeon: the arena built from a GEO map around a party square (0xCDE1, rare features included), RandSeed after;
- wild: the wilderness arena (0xD785) and the terrain class of every code (0xCE75);
- place: placement of a party and monsters (0xDFC6) on a dungeon or wilderness arena: slots, removals, bodies;
- lines: random arenas and creatures: target lists (0x3045B), adjacent / visible enemies (START 0x2FD3), distance
  (START 0x30FB), direction (0x14E38), the step probe (0x313F3) and the cone test (0x300CD).
The output holds map planes and arena cells (game data): it belongs in tests/fixtures-private only.
"""
import json
import os
import random
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import geo  # noqa: E402
from por_emu import Emu  # noqa: E402

# Routines (GAME.OVR file offsets; START.EXE seg:off as the far calls name them).
BUILD_DUNGEON, BUILD_WILD, TERRAIN_CLASS, PLACE_ALL = 0xCDE1, 0xD785, 0xCE75, 0xDFC6
BUILD_CELLS, PROBE, IS_DIR_TOWARD, TARGET_LIST, DIR_TOWARD = 0x30ADA, 0x313F3, 0x300CD, 0x3045B, 0x14E38
COUNT_TARGETS, DISTANCE = (0xBA, 0x2433), (0xBA, 0x255B)
FREE_CREATURE, POLL_KEYS = 0x1997F, (0x899, 0x122)
REC, COMBAT = 0x11D, 0x16


def find(dirpath, name):
    for f in os.listdir(dirpath):
        if f.upper() == name:
            return os.path.join(dirpath, f)
    raise FileNotFoundError(name)


class Oracle:
    def __init__(self, gamedir):
        with open(find(gamedir, 'START.EXE'), 'rb') as f:
            exe = f.read()
        with open(find(gamedir, 'GAME.OVR'), 'rb') as f:
            ovr = f.read()
        self.e = Emu(exe, ovr)
        self.e.trap_ovr(FREE_CREATURE, Emu.ret(4))
        self.e.trap_start(*POLL_KEYS, Emu.ret(0))
        self.maps = []
        for disk in range(1, 9):
            try:
                self.maps += [(disk, m) for m in geo.load(find(gamedir, 'GEO%d.DAX' % disk))]
            except FileNotFoundError:
                pass

    def fresh(self):
        e = self.e
        e.reset_heap()
        self.arena = e.alloc(0x4E9)
        e.set_dsptr(0x6716, self.arena)
        self.planes = e.alloc(1024)
        e.set_dsptr(0x6A5C, self.planes)
        self.zone0 = e.alloc(0x800)
        e.set_dsptr(0x49D2, self.zone0)
        self.zone1 = e.alloc(0x800)
        e.set_dsptr(0x49D6, self.zone1)

    def lin(self, ptr):
        return ptr[0] * 16 + ptr[1]

    def cells(self):
        return self.e.rb(self.lin(self.arena) + 7, 1250)

    def set_cells(self, data):
        self.e.wb(self.lin(self.arena) + 7, data)

    def build_dungeon(self, m, x, y, facing, block, seed):
        e = self.e
        e.wb(self.lin(self.planes), b''.join(m.planes))
        e.set_dsb(0x6AAD, x)
        e.set_dsb(0x6AAE, y)
        e.set_dsb(0x6AAF, facing)
        e.set_dsb(0x84DC, block)
        e.set_dsb(0x49FA, 1)
        e.set_randseed(seed)
        e.call(BUILD_DUNGEON)

    def build_wild(self, mode, wx, wy, yarash, block, seed):
        e = self.e
        z = self.lin(self.zone0)
        e.wb(z + 0x186, [wx])
        e.wb(z + 0x188, [wy])
        e.wb(z + 0x366, struct.pack('<H', 255 if yarash else 0))
        e.set_dsb(0x49FA, mode)
        e.set_dsb(0x84DC, block)
        e.set_randseed(seed)
        e.call(BUILD_WILD)

    def terrain_class(self, code, yarash):
        e = self.e
        e.set_dsb(0x465A, code)
        e.wb(self.lin(self.zone0) + 0x366, struct.pack('<H', 255 if yarash else 0))
        return e.call(TERRAIN_CLASS)[0] & 0xFF

    # ---- creatures ----
    def make_list(self, specs):
        """specs: dicts with size, inplay, hostile, notparty. Returns record pointers in list order."""
        e = self.e
        ptrs = []
        for s in specs:
            r = e.alloc(REC)
            c = e.alloc(COMBAT)
            rl = self.lin(r)
            e.wb(rl + 0x6C, [s['size']])
            e.wb(rl + 0x10C, [0 if s['inplay'] else 4])
            e.wb(rl + 0x10D, [s['inplay']])
            e.wb(rl + 0x10E, [s['hostile']])
            e.wb(rl + 0x108, struct.pack('<HH', c[1], c[0]))
            e.wb(self.lin(c) + 0x13, [s.get('notparty', 0)])
            ptrs.append(r)
        for a, b in zip(ptrs, ptrs[1:] + [None]):
            e.wb(self.lin(a) + 0x104, struct.pack('<HH', b[1], b[0]) if b else b'\0\0\0\0')
        e.set_dsptr(0x5D96, ptrs[0] if ptrs else (0, 0))
        return ptrs

    def slot_table(self, count):
        e = self.e
        out = []
        for k in range(1, count + 1):
            x, y, s, size = e.rb(e.ds(0x5F27 + 4 * k), 4)
            o, sg = struct.unpack('<HH', e.rb(e.ds(0x65B9 + 4 * k), 4))
            out.append({'x': x, 'y': y, 'slot': s, 'size': size, 'ptr': (sg, o)})
        return out

    def set_slots(self, ptrs, places):
        e = self.e
        for k, (p, (x, y, size)) in enumerate(zip(ptrs, places), 1):
            e.wb(e.ds(0x5F27 + 4 * k), [x & 0xFF, y & 0xFF, k, size])
            e.wb(e.ds(0x65B9 + 4 * k), struct.pack('<HH', p[1], p[0]))
        e.set_dsb(0x5F2A, len(ptrs) + 1)
        e.wb(e.ds(0x5F27 + 4 * (len(ptrs) + 1)), [0, 0, 0, 0])
        e.wb(e.ds(0x65B9 + 4 * (len(ptrs) + 1)), b'\0\0\0\0')
        e.call(BUILD_CELLS)

    def target_list(self):
        e = self.e
        n = e.dsb(0x671A)
        return [list(e.rb(e.ds(0x6718 + 3 * i), 3)) for i in range(1, n + 1)]


def hexs(b):
    return bytes(b).hex()


def main(argv):
    if len(argv) != 3:
        print(__doc__)
        return 2
    o = Oracle(argv[1])
    e = o.e
    rnd = random.Random(20261117)
    out = {'source': 'GAME.OVR / START.EXE v1.3 run under unicorn (tools/scripts/por_combat_oracle.py)'}

    # ---- dungeon arenas ----
    dungeon = []
    picks = o.maps[::max(1, len(o.maps) // 20)][:20]
    extra = [(0, 0, 0, 20), (15, 15, 4, 10), (0, 8, 6, 10), (15, 3, 2, 0)]
    for i, (disk, m) in enumerate(picks):
        o.fresh()
        x, y, facing, block = extra[i] if i < len(extra) else (rnd.randrange(16), rnd.randrange(16),
                                                               rnd.choice([0, 2, 4, 6]), rnd.choice([0, 5, 10, 20]))
        seed = rnd.getrandbits(32)
        o.build_dungeon(m, x, y, facing, block, seed)
        dungeon.append({'disk': disk, 'map': m.id, 'planes': hexs(b''.join(m.planes)), 'x': x, 'y': y,
                        'facing': facing, 'block': block, 'seed': seed, 'cells': hexs(o.cells()),
                        'seedAfter': e.randseed()})
    out['dungeon'] = dungeon

    # ---- wilderness ----
    o.fresh()
    out['terrainClass'] = [[o.terrain_class(c, y) for c in range(256)] for y in (False, True)]
    wild = []
    wanted = [0x10, 0x20, 0x40, 0x80, 0x08, 0x04, 0x02, 0x01, 0x81, 0x80, 0x10, 0x20]
    S = e.S
    wildmap = S[e.ds(0x3648):e.ds(0x3648) + 36 * 44]
    for i, bit in enumerate(wanted):
        o.fresh()
        yarash = i == 9
        cands = []
        for mode, off in ((2, 0), (3, 13), (4, 26)):
            for wy in range(36):
                for wx in range(13):
                    code = wildmap[wy * 44 + wx + off]
                    cls = out['terrainClass'][1 if yarash else 0][code]
                    if cls & bit == bit and cls not in (0xEE, 0x6F):  # 0xEE / 0x6F: no arm names the code
                        cands.append((mode, wx, wy))
        if not cands:
            continue
        mode, wx, wy = rnd.choice(cands)
        block = 10 if i == 5 else rnd.choice([0, 5, 20])
        seed = rnd.getrandbits(32)
        o.build_wild(mode, wx, wy, yarash, block, seed)
        wild.append({'mode': mode, 'wx': wx, 'wy': wy, 'yarash': yarash, 'block': block, 'seed': seed,
                     'cells': hexs(o.cells()), 'seedAfter': e.randseed()})
    out['wild'] = wild

    # ---- placement ----
    place = []
    for i in range(40):
        o.fresh()
        outdoors = i % 5 == 4
        seed = rnd.getrandbits(32)
        facing = rnd.choice([0, 2, 4, 6])
        case = {'facing': facing, 'seed': seed, 'outdoors': outdoors}
        if outdoors:
            mode, wx, wy = rnd.choice([2, 3, 4]), rnd.randrange(13), rnd.randrange(36)
            o.build_wild(mode, wx, wy, False, 20, seed)
            case.update(mode=mode, wx=wx, wy=wy, block=20)
            x = y = 0
            disk, m = o.maps[0]
        else:
            disk, m = rnd.choice(o.maps)
            x, y = rnd.randrange(16), rnd.randrange(16)
            o.build_dungeon(m, x, y, facing, 20, seed)
            e.set_dsb(0x49FA, 1)
            case.update(disk=disk, map=m.id, planes=hexs(b''.join(m.planes)), x=x, y=y, block=20)
        e.set_dsb(0x6AAD, x)
        e.set_dsb(0x6AAE, y)
        e.set_dsb(0x6AAF, facing)
        duel = i % 13 == 7
        e.set_dsb(0x84D4, 1 if duel else 0)
        e.set_dsb(0x6715, 0)
        e.wb(e.ds(0x66DD), bytes(0x38))
        dist = rnd.choice([0, 1, 2, 2])
        npar = rnd.randint(1, 8)
        nmon = rnd.choice([0, 1, 3, 6, 10, 16, 25, 40])
        specs = []
        for k in range(npar):
            specs.append({'size': 1, 'inplay': 0 if rnd.random() < 0.15 else 1, 'hostile': 0, 'notparty': 0})
        for k in range(nmon):
            specs.append({'size': rnd.choice([1, 1, 1, 1, 2, 3, 4]), 'inplay': 0 if rnd.random() < 0.05 else 1,
                          'hostile': 0 if rnd.random() < 0.1 else 1, 'notparty': 1})
        z1 = o.lin(o.zone1)
        e.wb(z1 + 0x582, struct.pack('<H', dist))
        e.wb(z1 + 0x67C, struct.pack('<H', npar))
        ptrs = o.make_list(specs)
        e.call(PLACE_ALL)
        count = e.dsb(0x5F2A)
        slots = o.slot_table(count)
        index = {p: k for k, p in enumerate(ptrs)}
        # list after placement
        after = []
        p = e.dsptr(0x5D96)
        while p != (0, 0) and len(after) < 100:
            after.append(index[p])
            off, sg = struct.unpack('<HH', e.rb(o.lin(p) + 0x104, 4))
            p = (sg, off)
        bodies = []
        for k in range(1, e.dsb(0x6715) + 1):
            off, sg, bx, by, t = struct.unpack('<HHBBB', e.rb(e.ds(0x66D6 + 7 * k), 7))
            bodies.append({'creature': index.get((sg, off), -1), 'x': bx, 'y': by, 'terrain': t})
        case.update(distance=dist, duel=duel, creatures=specs, slotCount=count,
                    slots=[{'creature': index.get(s['ptr'], -1), 'x': s['x'], 'y': s['y'], 'size': s['size']}
                           for s in slots],
                    listAfter=after, bodies=bodies, cells=hexs(o.cells()), teamCounts=[e.dsb(0x6814), e.dsb(0x6815)])
        place.append(case)
    out['place'] = place

    # ---- lines ----
    lines = []
    terrains = [23] * 12 + [1, 5, 10, 26, 27, 28, 29, 30, 31, 36, 37, 38, 39, 40, 41, 50, 52, 56, 60, 62, 64, 65]
    for i in range(30):
        o.fresh()
        cells = bytes(rnd.choice(terrains) if rnd.random() < 0.35 else 23 for _ in range(1250))
        o.set_cells(cells)
        k = rnd.randint(2, 9)
        specs = [{'size': rnd.choice([1, 1, 1, 2, 3, 4]), 'inplay': 1, 'hostile': rnd.randrange(2)} for _ in range(k)]
        ptrs = o.make_list(specs)
        # Footprints stay inside the 25 x 50 cell grid DS:60DB (x may be -1 or 50: the cell lands on the next or
        # previous row); a cell past it would overwrite the creature table after the grid.
        places = [(rnd.randrange(-1, 51) if rnd.random() < 0.05 else rnd.randrange(50), rnd.randrange(1, 24),
                   specs[j]['size'] if rnd.random() < 0.9 else 0) for j in range(k)]
        o.set_slots(ptrs, places)
        case = {'cells': hexs(cells), 'creatures': [{'x': p[0], 'y': p[1], 'size': p[2], 'hostile': s['hostile']}
                                                     for p, s in zip(places, specs)]}
        tl = []
        for a in range(k):
            x, y, size = places[a]
            for rng, flt in ((1, 255), (3, 255), (255, 255), (255, rnd.randrange(8)), (2, rnd.randrange(8))):
                e.set_dsb(0x671A, 0)
                e.call(TARGET_LIST, [x & 0xFF, y & 0xFF, rng, flt, size, *o.arena])
                tl.append({'from': a, 'range': rng, 'filter': flt, 'list': o.target_list()})
        case['targetLists'] = tl
        ct = []
        for a in range(k):
            for rng in (1, 255):
                n = e.call(COUNT_TARGETS, [*ptrs[a], rng])[0] & 0xFF
                ct.append({'from': a, 'range': rng, 'count': n, 'list': o.target_list(),
                           'potential': list(e.rb(e.ds(0x6DE5), n))})
        case['countTargets'] = ct
        dist, dirs = [], []
        e.set_dsb(0x671A, 0)
        e.call(TARGET_LIST, [places[0][0] & 0xFF, places[0][1], 1, 255, places[0][2], *o.arena])
        for a in range(k):
            for b in range(k):
                before = o.target_list()
                d = e.call(DISTANCE, [*ptrs[a], *ptrs[b]])[0] & 0xFF
                dist.append({'a': a, 'b': b, 'd': d, 'countAfter': e.dsb(0x671A),
                             'entriesKept': list(e.rb(e.ds(0x671B), 3 * len(before))) == sum(before, [])})
                if all(0 <= places[j][0] < 50 for j in (a, b)):  # off the arena the program's loop never ends
                    dirs.append({'a': a, 'b': b, 'dir': e.call(DIR_TOWARD, [*ptrs[a], *ptrs[b]])[0] & 0xFF})
        case['distance'] = dist
        case['direction'] = dirs
        probes = []
        for a in range(k):
            if places[a][2] == 0:
                continue
            for d in range(9):
                cr = o.e.alloc(2)
                e.call(PROBE, [*ptrs[a], d, cr[0], 0, cr[0], 1])
                c, t = e.rb(o.lin(cr), 2)
                probes.append({'a': a, 'dir': d, 'slot': c, 'terrain': t})
        case['probes'] = probes
        lines.append(case)
    cone = []
    for _ in range(400):
        x, y = rnd.randrange(-2, 52), rnd.randrange(-2, 27)
        tx, ty = x + rnd.randrange(-6, 7), y + rnd.randrange(-6, 7)
        d = rnd.choice(list(range(9)) + [255])
        r = e.call(IS_DIR_TOWARD, [x & 0xFF, y & 0xFF, tx & 0xFF, ty & 0xFF, d])[0] & 0xFF
        cone.append([x, y, tx, ty, d, r])
    out['lines'] = lines
    out['cone'] = cone
    with open(argv[2], 'w') as f:
        json.dump(out, f, separators=(',', ':'))
    print('dungeon %d, wild %d, place %d, lines %d' % (len(dungeon), len(wild), len(place), len(lines)))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
