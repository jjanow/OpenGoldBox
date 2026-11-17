#!/usr/bin/env python3
"""Read Pool of Radiance (DOS v1.3) VM state out of a DOSBox-X save state.

    dosbox_state.py dump  STATE.sav [--json] [--areas OUTDIR]   VM state (DS variables, position, map, areas)
    dosbox_state.py check STATE.sav SAVGAMx.DAT                 compare the four VM areas with a game save
    dosbox_state.py diff  A.sav B.sav                           VM cells / DS variables / map bytes that changed
    dosbox_state.py combat STATE.sav [--json]                   the combat list with each creature's combat record

Run with ``python3 -I``. Method and addresses: docs/re/dosbox-trace-method.md.

A DOSBox-X state (F12+S with ``[dosbox] savefile=…`` and ``compresssaveparts=false``) is a zip. Its ``Memory``
entry is a 0x88-byte header followed by guest RAM from physical address 0. The game's data segment is found from
the DOS memory chain (the block owned by START: DS = PSP + 0x10 + 0xC7C); everything else is read through far
pointers the game keeps in DS, the way the VM reader (GAME.OVR 0x82DF) does:

    DS:49D2  far ptr  VM $4900-$4CFF   1024 words      DS:49DE  far ptr  VM $9900-$B6FF  script bytes
    DS:49D6  far ptr  VM $6B00-$6EFF   1024 words      DS:6A5C  far ptr  current GEO map, 4 planes of 256
    DS:49DA  far ptr  VM $9700-$98FF    512 words

No game data is embedded here.
"""
import json
import os
import struct
import sys
import zipfile

HEADER = 0x88          # bytes before guest RAM in the "Memory" entry (DOSBox-X 2026.01.02)
DS_FROM_LOAD = 0xC7C   # DS = START.EXE load segment + 0xC7C (docs/re/ecl-format.md)

# DS variables (offset, size, name). Sources: docs/re/ecl-format.md "Program references", GAME.OVR 0x82DF.
DS_VARS = [
    (0x49ED, 2, 'pc'),
    (0x49E3, 2, 'vector0_move'), (0x49E5, 2, 'vector1_search'), (0x49E7, 2, 'vector2_precamp'),
    (0x49E9, 2, 'vector3_camp_interrupted'), (0x49EB, 2, 'vector4_init'),
    (0x6F7F, 1, 'opcode'), (0x6F7E, 1, 'prev_opcode'),
    (0x6F78, 1, 'flag_eq'), (0x6F79, 1, 'flag_ne'), (0x6F7A, 1, 'flag_lt'), (0x6F7B, 1, 'flag_gt'),
    (0x6F7C, 1, 'flag_le'), (0x6F7D, 1, 'flag_ge'),
    (0x6F70, 4, 'gosub_stack_head'),
    (0x442E, 1, 'stop_flag'), (0x442F, 1, 'script_changed'),
    (0x49F8, 1, 'ds_49f8'), (0x49FF, 1, 'ds_49ff'),
    (0x5376, 1, 'ecl_disk'),
    (0x4428, 4, 'randseed'),           # Turbo Pascal RandSeed (dword); seed = seed * 0x08088405 + 1
    (0x84DC, 1, 'current_ecl_block'),  # tested by the GEO accessors (0 and 10 = no wrap off the map)
    (0x49FA, 1, 'ds_49fa'),            # 1 while in the 3D view (step/door code at GAME.OVR 0x17443)
    (0x6AAD, 1, 'party_x'), (0x6AAE, 1, 'party_y'), (0x6AAF, 1, 'party_facing'),
    (0x6AB0, 1, 'party_wall'), (0x6AB1, 1, 'party_event'),
    (0x84CE, 2, 'ds_84ce_b8'), (0x84D0, 2, 'ds_84d0_b9'),  # last writes to $00B8 / $00B9 (SPELL's low-byte targets)
    (0x84D2, 1, 'ds_84d2'), (0x84D3, 1, 'ds_84d3'),          # LOADCHAR removal armed by writes of 0 to $6C00 / $6B00
    (0x4430, 1, 'ds_4430'),                                  # LOADCHAR changed the current character
    (0x5D92, 4, 'current_char_ptr'), (0x4436, 4, 'selected_char_ptr'), (0x5D96, 4, 'party_head_ptr'),
]
PARTY_NEXT = 0x104  # far pointer to the next record in the party list (docs/re/ecl-format.md)
ITEM_NEXT = 0x2A
AREAS = [  # name, DS offset of the far pointer, VM base, cells, cell size
    ('area_4900', 0x49D2, 0x4900, 1024, 2),
    ('area_6B00', 0x49D6, 0x6B00, 1024, 2),
    ('area_9700', 0x49DA, 0x9700, 512, 2),
    ('script_9900', 0x49DE, 0x9900, 7680, 1),
]
SAVE_OFFSETS = {'area_4900': 1, 'area_6B00': 2049, 'area_9700': 4097, 'script_9900': 5121}
GEO_PTR = 0x6A5C


class State:
    def __init__(self, path):
        z = zipfile.ZipFile(path)
        mem = z.read('Memory')
        self.program = z.read('Program_Name').decode('latin-1')
        self.ram = mem[HEADER:HEADER + 0x100000]
        self.ds = self._find_ds() << 4

    def _find_ds(self):
        r = self.ram
        for a in range(0, 0xA0000, 16):  # MCB: 'M'/'Z', owner PSP, size, name at +8
            if r[a] not in (0x4D, 0x5A):
                continue
            owner, size = struct.unpack_from('<HH', r, a + 1)
            if owner == a // 16 + 1 and r[a + 8:a + 13] == b'START' and r[a + 13] in (0, 0x2E):
                return owner + 0x10 + DS_FROM_LOAD
        raise ValueError('START.EXE not found in the DOS memory chain')

    def u(self, off, size):
        return int.from_bytes(self.ram[self.ds + off:self.ds + off + size], 'little')

    def farptr(self, off):
        o, s = struct.unpack_from('<HH', self.ram, self.ds + off)
        return s * 16 + o

    def area(self, name):
        _, ptr, _, cells, size = next(a for a in AREAS if a[0] == name)
        p = self.farptr(ptr)
        return self.ram[p:p + cells * size]

    def combat_list(self):
        """The combat list DS:5D96 (next pointer at record +0x104), with the 22-byte combat record at +0x108
        (docs/re/combat-format.md) and the record bytes the fight reads."""
        out = []
        p = self.farptr(0x5D96)
        while p and len(out) < 80:
            rec = self.ram[p:p + 0x11D]
            o, sg = struct.unpack_from('<HH', rec, 0x108)
            cr = self.ram[sg * 16 + o:sg * 16 + o + 22] if (o or sg) else b''
            out.append({'name': rec[1:1 + min(rec[0], 15)].decode('latin-1'), 'record': rec.hex(),
                        'combat': cr.hex(), 'initiative': (cr[3] ^ 0x80) - 0x80 if cr else None,
                        'movement': cr[6] if cr else None, 'dex': rec[0x13], 'status': rec[0x10C],
                        'in_play': rec[0x10D], 'hostile': rec[0x10E], 'auto': rec[0x10F],
                        'attacks_x2': [rec[0xA1], rec[0xA2]], 'attacks_left': [rec[0x113], rec[0x114]],
                        'move_cur': rec[0x11C], 'hp': rec[0x11B]})
            o, sg = struct.unpack_from('<HH', rec, 0x104)
            p = sg * 16 + o
        return {'creatures': out, 'current': self.farptr(0x5D92), 'list_head': self.farptr(0x5D96),
                'round_counter_ds6de3': self.u(0x6DE3, 1), 'team_counts_ds6814': [self.u(0x6814, 1), self.u(0x6815, 1)],
                'randseed': self.u(0x4428, 4), 'ds_49f3': self.u(0x49F3, 1)}

    def geo(self):
        p = self.farptr(GEO_PTR)
        return self.ram[p:p + 1024]

    def linear(self, seg_off):
        return (seg_off >> 16) * 16 + (seg_off & 0xFFFF)

    def party(self):
        """The party list DS:5D96 (party plus appended creatures): records with a few fields, in list order."""
        out = []
        cur = self.u(0x5D92, 4)
        p = self.u(0x5D96, 4)
        while p and len(out) < 64:
            a = self.linear(p)
            r = self.ram[a:a + 0x11D]
            items = 0
            ip = int.from_bytes(r[0xC8:0xCC], 'little')
            while ip and items < 64:
                items += 1
                ia = self.linear(ip)
                ip = int.from_bytes(self.ram[ia + ITEM_NEXT:ia + ITEM_NEXT + 4], 'little')
            out.append({'name': r[1:1 + min(r[0], 15)].decode('latin-1'), 'current': p == cur,
                        'hp': r[0x11B], 'status': r[0x10C], 'in_play': r[0x10D], 'hostile': r[0x10E],
                        'npc_0x84': r[0x84], 'icon_0xbf': r[0xBF],
                        'money': list(struct.unpack_from('<7H', r, 0x88)), 'items': items,
                        'record_hex': bytes(r).hex()})
            p = int.from_bytes(self.ram[a + PARTY_NEXT:a + PARTY_NEXT + 4], 'little')
        return out

    def vars(self):
        return {name: self.u(off, size) for off, size, name in DS_VARS}


def cells(name, data):
    base, size = next((a[2], a[4]) for a in AREAS if a[0] == name)
    if size == 1:
        return {base + i: b for i, b in enumerate(data)}
    return {base + i: v for i, (v,) in enumerate(struct.iter_unpack('<H', data))}


def cmd_dump(st, as_json, outdir):
    v = st.vars()
    out = {'program': st.program, 'ds_segment': st.ds >> 4, 'vars': v,
           'vm': {'4900_cells_nonzero': sum(1 for x in cells('area_4900', st.area('area_4900')).values() if x)},
           'geo_event_here': st.geo()[512 + v['party_y'] * 16 + v['party_x']] if v['party_x'] < 16 else None,
           'party': st.party()}
    if outdir:
        os.makedirs(outdir, exist_ok=True)
        for a in AREAS:
            open(os.path.join(outdir, a[0] + '.bin'), 'wb').write(st.area(a[0]))
        open(os.path.join(outdir, 'geo_planes.bin'), 'wb').write(st.geo())
        open(os.path.join(outdir, 'vars.json'), 'w').write(json.dumps(out, indent=1) + '\n')
    if as_json:
        print(json.dumps(out, indent=1))
    else:
        for k, x in v.items():
            print('%-26s %s' % (k, hex(x)))


def cmd_check(st, save):
    s = open(save, 'rb').read()
    bad = 0
    for a in AREAS:
        mine, theirs = st.area(a[0]), s[SAVE_OFFSETS[a[0]]:SAVE_OFFSETS[a[0]] + len(st.area(a[0]))]
        cm, ct = cells(a[0], mine), cells(a[0], theirs)
        d = [(hex(k), cm[k], ct[k]) for k in cm if cm[k] != ct[k]]
        bad += len(d)
        print('%-12s %4d cells differ %s' % (a[0], len(d), d[:16]))
    pos = s[12801:12806]
    v = st.vars()
    print('position save', list(pos), 'ram', [v['party_x'], v['party_y'], v['party_facing'], v['party_wall'],
                                              v['party_event']])
    return bad


def cmd_diff(a, b):
    va, vb = a.vars(), b.vars()
    for k in va:
        if va[k] != vb[k]:
            print('var  %-26s %s -> %s' % (k, hex(va[k]), hex(vb[k])))
    for ar in AREAS:
        ca, cb = cells(ar[0], a.area(ar[0])), cells(ar[0], b.area(ar[0]))
        for k in ca:
            if ca[k] != cb[k]:
                print('vm   $%04X  %d -> %d' % (k, ca[k], cb[k]))
    ga, gb = a.geo(), b.geo()
    for i in range(1024):
        if ga[i] != gb[i]:
            p, sq = divmod(i, 256)
            print('geo  plane %d (%d,%d)  0x%02X -> 0x%02X' % (p, sq % 16, sq // 16, ga[i], gb[i]))


def main(argv):
    if len(argv) < 3:
        sys.exit(__doc__)
    cmd = argv[1]
    if cmd == 'dump':
        outdir = argv[argv.index('--areas') + 1] if '--areas' in argv else None
        cmd_dump(State(argv[2]), '--json' in argv, outdir)
    elif cmd == 'check':
        sys.exit(1 if cmd_check(State(argv[2]), argv[3]) else 0)
    elif cmd == 'combat':
        c = State(argv[2]).combat_list()
        if '--json' in argv:
            print(json.dumps(c, indent=1))
        else:
            print('randseed %08x  round DS:6DE3 %d  teams %s  DS:49F3 %d' % (
                c['randseed'], c['round_counter_ds6de3'], c['team_counts_ds6814'], c['ds_49f3']))
            for k in c['creatures']:
                print('%-16s st %d play %d host %d auto %d dex %2d init %s move %s hp %3d  combat %s' % (
                    k['name'], k['status'], k['in_play'], k['hostile'], k['auto'], k['dex'], k['initiative'],
                    k['movement'], k['hp'], k['combat']))
    elif cmd == 'diff':
        cmd_diff(State(argv[2]), State(argv[3]))
    else:
        sys.exit(__doc__)


if __name__ == '__main__':
    main(sys.argv)
