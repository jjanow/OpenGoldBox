#!/usr/bin/env python3
"""View's Items page (GAME.OVR 0x21F32) from the program itself: runs the v1.3 loop whole (por_emu.py, unicorn) on
generated characters with scripted keys and writes what it does, for GoldBox.Core's ItemsPageProgramTests
(docs/re/camp.md "The Items page's Ready, Drop, Halve and Join").

Usage: python3 -I por_items_oracle.py GAMEDIR OUT.json        (GAMEDIR: the PoR v1.3 files; needs unicorn 2.x)

Each case: a character (the game's own CHRDATA1-6.SAV with class mask, abilities and items made up here) as DS:5D92,
DS:49F3 one of 0 (party menu / hall), 1 (shop / temple), 2 (camp), 3 / 4 (command line), 5 (fight, with a combat
record at +0x108), 6 (loot), VM $49E5 (word [DS:49D2]+0x1CA) 0 or 1. START 0x1758 runs on the character first, as
View's page (0x21084 -> 0x21642) does before Items is chosen. Then 0x21F32 runs as it is, with:
- the item list 0x2D669 (0x03C5:0x0034) scripted: its entry string is recorded and a key among the offered entries'
  first letters (Ready, Drop, Halve, Join; now and then a key no entry has) and a row are answered, until 'E';
- the yes/no 0x2D9A0 (0x03C5:0x0039) scripted (Y or N, recorded in order) and its prompt recorded;
- START 0x1693 (the row-24 message) recorded with its text; the item handler 0x2AEEA (0x00B0:0x0025) recorded (id,
  item, removed) and not run (its handlers are EffectHandlers' and tested elsewhere);
- the drawing routines (START 0x77F8 / 0x7D3B / 0x76B6, the windows 0x3F10 / 0x4047 / 0x41F8, the name START 0x23B6)
  and the item naming START 0xFE2 trapped to do nothing (every item carries its own made-up name "ITEMn");
- START 0x1758 (stat recalculation), 0x20A9 (removal), the scroll test START 0x9DE, the runtime's Move and sets, run
  as they are (GetMem / FreeMem from the emulator's arena).
The records and lists after the loop, the messages, handler calls and entry strings per pass are written to the
output (tests/fixtures-private).
"""
import json
import os
import random
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from por_emu import Emu  # noqa: E402
from capstone import Cs, CS_ARCH_X86, CS_MODE_16  # noqa: E402
from unicorn.x86_const import UC_X86_REG_DX  # noqa: E402

ITEMS_PAGE = 0x21F32
RECALC = (0xBA, 0xBB8)  # START 0x1758
GETMEM, FREEMEM = (0xAF8, 0x23F), (0xAF8, 0x254)
LIST, YESNO = (0x3C5, 0x34), (0x3C5, 0x39)
ROW_MESSAGE, NAMING, HANDLER = (0xBA, 0x1693), (0xBA, 0x442), (0xB0, 0x25)
DRAWS = [(0x709, 0xCAB), (0x709, 0x626), (0x709, 0x768), (0x3F1, 0x0), (0x3F1, 0x137), (0x3F1, 0x2E8), (0xBA, 0x1816)]
REC, ITEM = 0x11D, 0x3F


def find(dirpath, name):
    for f in os.listdir(dirpath):
        if f.upper() == name:
            return os.path.join(dirpath, f)
    raise FileNotFoundError(name)


def ptr_bytes(p):
    return struct.pack('<HH', p[1], p[0]) if p else b'\0\0\0\0'


def pstr(em, seg, off):
    lin = seg * 16 + off
    n = em.rb(lin, 1)[0]
    return em.rb(lin + 1, n).decode('latin-1')


class ItemsOracle:
    def __init__(self, gamedir):
        with open(find(gamedir, 'START.EXE'), 'rb') as f:
            exe = f.read()
        with open(find(gamedir, 'GAME.OVR'), 'rb') as f:
            ovr = f.read()
        self.e = e = Emu(exe, ovr)
        self.md = Cs(CS_ARCH_X86, CS_MODE_16)
        self.log = []
        self.script = []

        def getmem(em):
            seg, off = em.alloc(em.arg(0))
            em.uc.reg_write(UC_X86_REG_DX, seg)
            em.pop_ret(2, off)

        def message(em):
            self.log.append({'message': pstr(em, em.arg(2), em.arg(0))})
            em.pop_ret(4)

        def handler(em):
            # 0x2AEEA(id, creature, item, removed): the last pushed is the removal flag
            removed, item, creature, hid = em.arg(0) & 0xFF, (em.arg(4), em.arg(2)), (em.arg(8), em.arg(6)), em.arg(10) & 0xFF
            self.log.append({'handler': hid, 'item': self.index_of(item), 'removed': removed,
                             'current': creature == self.current})
            em.pop_ret(12)

        def listing(em):
            entries = pstr(em, em.arg(34), em.arg(32))
            var = em.arg(2) * 16 + em.arg(0)
            letters = [w[0] for w in entries.split()]
            self.log.append({'entries': entries})
            key, row = self.next_command(letters)
            item = self.item_at(row)
            em.wb(var, ptr_bytes(item))
            self.log.append({'key': key, 'row': row})
            em.pop_ret(self.ret_list, ord(key))

        def yesno(em):
            a = self.rnd.choice('YN')
            self.log.append({'question': pstr(em, em.arg(8), em.arg(6)), 'answer': a})
            em.pop_ret(self.ret_yesno, ord(a))

        self.ret_list = self.retf(*LIST)
        self.ret_yesno = self.retf(*YESNO)
        e.trap_start(*GETMEM, getmem)
        e.trap_start(*FREEMEM, Emu.ret(6))
        e.trap_start(*ROW_MESSAGE, message)
        e.trap_start(*NAMING, Emu.ret(self.retf(*NAMING)))
        e.trap_ovr(self.ovr_of(*HANDLER), handler)
        assert self.retf(*HANDLER) == 12
        self.trap_far(LIST, listing)
        self.trap_far(YESNO, yesno)
        for d in DRAWS:
            e.trap_start(*d, Emu.ret(self.retf(*d)))
        with open(find(gamedir, 'ITEMS'), 'rb') as f:
            self.base_items = f.read()[2:2 + 16 * 128]
        e.wb(e.ds(0x5582), self.base_items)
        self.records = []
        for k in range(1, 7):
            with open(find(gamedir, 'CHRDATA%d.SAV' % k), 'rb') as f:
                self.records.append(f.read()[:REC])

    # ---- code helpers ----
    def ovr_of(self, seg, off):
        """A far call target: the GAME.OVR offset of a stub entry, or None for START code."""
        e = self.e
        if seg in e.stubs:
            lin = seg * 16 + off
            r = struct.unpack('<H', e.rb(lin + 2, 2))[0]
            return e.stubs[seg][0] + r
        return None

    def trap_far(self, target, fn):
        o = self.ovr_of(*target)
        if o is None:
            self.e.trap_start(*target, fn)
        else:
            self.e.trap_ovr(o, fn)

    def retf(self, seg, off):
        """The argument bytes of the routine's first retf (Pascal: one epilogue)."""
        e = self.e
        o = self.ovr_of(seg, off)
        code, base = (e.O[o:o + 0x2000], o) if o is not None else (e.S[seg * 16 + off:seg * 16 + off + 0x2000], 0)
        for i in self.md.disasm(code, 0):
            if i.mnemonic == 'retf':
                return int(i.op_str, 16) if i.op_str else 0
        raise ValueError('no retf at %x:%x' % (seg, off))

    # ---- data ----
    def lin(self, p):
        return p[0] * 16 + p[1]

    def head(self):
        o, s = struct.unpack('<HH', self.e.rb(self.lin(self.current) + 0xC8, 4))
        return (s, o)

    def chain(self):
        out = []
        p = self.head()
        while p != (0, 0) and len(out) < 40:
            out.append(p)
            o, s = struct.unpack('<HH', self.e.rb(self.lin(p) + 0x2A, 4))
            p = (s, o)
        return out

    def item_at(self, row):
        c = self.chain()
        return c[row] if 0 <= row < len(c) else None

    def index_of(self, p):
        return self.ids.get(p, -1)

    def next_command(self, letters):
        if not self.script:
            return 'E', 0
        key = self.script.pop(0)
        n = len(self.chain())
        offered = [k for k in 'RDHJ' if k in letters]
        if key == '?':
            key = 'X'
        elif key not in offered:
            key = self.rnd.choice(offered) if offered else 'E'
        row = self.rnd.randrange(n) if n else 0
        if self.rnd.random() < 0.03:
            row = n  # past the list: no item
        return key, row

    def record(self, rnd):
        b = bytearray(rnd.choice(self.records))
        b[0x10] = rnd.choice([3, 8, 12, 15, 16, 17, 18, 18, 18, 19])
        b[0x16] = rnd.choice([0, 0, 1, 50, 100])
        b[0xB0] = rnd.choice([0x01, 0x02, 0x04, 0x08, 0x10, 0x20, 0x40, 0x03, 0x06, 0x44, 0xFF])
        for k in range(7):
            struct.pack_into('<H', b, 0x88 + 2 * k, rnd.choice([0, 0, 10, 300]))
        b[0x84] = rnd.choice([0, 0, 0, 0x80])
        b[0x10C] = rnd.choice([0, 0, 0, 1, 4])
        b[0x10D] = rnd.choice([1, 1, 1, 0])
        b[0x10F] = rnd.choice([0, 0, 1])
        b[0x7F:0x83] = b'\0\0\0\0'
        b[0xC8:0x100] = bytes(0x38)
        b[0x104:0x10C] = bytes(8)
        b[0x108:0x10C] = bytes(4)
        return b

    def item(self, rnd, k):
        b = bytearray(ITEM)
        name = ('ITEM%d ' % k).encode()
        b[0] = len(name)
        b[1:1 + len(name)] = name
        types = [t for t in range(1, 128) if self.base_items[16 * t] != 0xFF]
        # weapons, shields, armour, rings, arrows / bolts and their bows more often
        b[0x2E] = rnd.choice(types + [0x49, 0x1C, 0x2F, 0x30, 0x31, 0x1D, 0x49] * 2)
        b[0x2F] = rnd.randrange(1, 200)
        b[0x30] = rnd.choice([0, 0, 0xD2])
        b[0x34] = rnd.choice([0, 0, 1])
        b[0x36] = rnd.choice([0, 0, 0, 1])
        struct.pack_into('<H', b, 0x37, rnd.choice([0, 1, 5, 10, 50]))
        b[0x39] = rnd.choice([0, 1, 1, 2, 3, 10, 40, 200, 255])
        b[0x3C] = rnd.choice([0, 0, 1, 2, 0x81])
        b[0x3D] = rnd.choice([0, 0, 3, 0x26])
        b[0x3E] = rnd.choice([0, 0, 0, 0x81, 0x83, 0x87, 0x89])
        return b

    def place(self, rec_bytes, items):
        e = self.e
        p = e.alloc(REC)
        e.wb(self.lin(p), bytes(rec_bytes))
        ptrs = [e.alloc(ITEM) for _ in items]
        for j, (q, r) in enumerate(zip(ptrs, items)):
            r = bytearray(r)
            r[0x2A:0x2E] = ptr_bytes(ptrs[j + 1] if j + 1 < len(ptrs) else None)
            e.wb(self.lin(q), bytes(r))
        e.wb(self.lin(p) + 0xC8, ptr_bytes(ptrs[0] if ptrs else None))
        return p, ptrs

    def items_of(self):
        out = []
        for p in self.chain():
            r = self.e.rb(self.lin(p), ITEM)
            out.append({'id': self.index_of(p), 'bytes': (bytes(r[:0x2A]) + b'\0\0\0\0' + bytes(r[0x2E:])).hex()})
        return out

    def cases(self, rnd, n):
        e = self.e
        self.rnd = rnd
        out = []
        for i in range(n):
            e.reset_heap()
            context = [0, 1, 2, 3, 4, 5, 6][i % 7]
            rec = self.record(rnd)
            items = [self.item(rnd, k) for k in range(rnd.randint(1, 8))]
            if rnd.random() < 0.5 and len(items) > 1:
                # look-alikes for Join: copies of one item with other counts and readied flags
                src = rnd.randrange(len(items))
                for _ in range(rnd.randint(1, 3)):
                    c = bytearray(items[src])
                    name = ('ITEM%d ' % len(items)).encode()
                    c[0] = len(name)
                    c[1:1 + len(name)] = name
                    c[0x39] = rnd.choice([1, 2, 50, 200, 255])
                    c[0x34] = rnd.choice([0, 0, 1])
                    items.insert(rnd.randrange(len(items) + 1), c)
                for k, it in enumerate(items):
                    name = ('ITEM%d ' % k).encode()
                    it[0] = len(name)
                    it[1:1 + len(name)] = name.ljust(len(name))
            self.current, ptrs = self.place(rec, items)
            self.ids = {p: k for k, p in enumerate(ptrs)}
            vm = e.alloc(0x200)
            nomagic = rnd.choice([0, 0, 0, 1])
            e.wb(self.lin(vm) + 0x1CA, struct.pack('<H', nomagic))
            e.set_dsptr(0x49D2, vm)
            item_use = 0
            if context == 5:
                cr = e.alloc(0x40)
                item_use = rnd.choice([0, 1])
                e.wb(self.lin(cr) + 2, bytes([item_use]))
                e.wb(self.lin(self.current) + 0x108, ptr_bytes(cr))
            e.set_dsb(0x49F3, context)
            e.set_dsb(0x6821, 0)
            e.set_dsptr(0x5D92, self.current)
            e.call(RECALC, list(self.current))
            start = e.rb(self.lin(self.current), REC)
            start_items = self.items_of()
            self.script = [rnd.choice('RRRDDHHJJ?') for _ in range(rnd.randint(1, 7))]
            self.log = []
            done = e.alloc(2)
            e.call(ITEMS_PAGE, list(done))
            out.append({'context': context, 'noMagic': nomagic, 'itemUse': item_use, 'record': start.hex(), 'items': start_items,
                        'log': self.log, 'done': e.rb(self.lin(done), 1)[0],
                        'after': e.rb(self.lin(self.current), REC).hex(), 'itemsAfter': self.items_of()})
        return out


def main(argv):
    if len(argv) != 3:
        print(__doc__, file=sys.stderr)
        return 2
    o = ItemsOracle(argv[1])
    rnd = random.Random(51051)
    data = {'source': 'GAME.OVR / START.EXE v1.3 run under unicorn (tools/scripts/por_items_oracle.py)',
            'items_5582': o.base_items.hex(), 'cases': o.cases(rnd, 420)}
    with open(argv[2], 'w') as f:
        json.dump(data, f, indent=0)
    print('%d cases' % len(data['cases']))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
