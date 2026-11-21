#!/usr/bin/env python3
"""Amiga sound fixtures from the program itself: runs Pool of Radiance Amiga v1.0's music player (`program`, hunk 27
+0x78A0 / +0x78B2, docs/re/sound.md §5) and its sound-effect player (hunk 33 +0x14E / +0x172) under unicorn (m68k)
and records what they write to the custom chips (DMACON and the four audio channels) on every vertical blank.

Usage: python3 -I por_amiga_sound_oracle.py PROGRAM OUT.json [TICKS] [--full]   (PROGRAM: disk A's `program`)
       python3 -I por_amiga_sound_oracle.py --timing PROGRAM SONG [TICKS]   per music tick: tick, instructions run
       before the output's channel-on DMACON write (hunk 27 +0x82BE), instructions in the tick, new-note DMA-off writes

The hunk file is loaded with every hunk at 0x100000 * (index + 1) and its RELOC32 entries applied. A run calls the
player's own routines: PlaySong (hunk 27 +0x78A0, the song number in d0), then the tick (+0x78B2) TICKS times (default
13000: past song 1's loop at 12,289); or play effect (hunk 33 +0x14E, the number on the stack), then the effect tick (+0x172). The page at 0xDFF000
is plain memory (DMACONR, INTREQR read 0). Per tick the JSON holds the writes in order as "REG:VALUE" (REG = the
custom-chip offset, hex; VALUE hex; DMACON keeps only bits 15 and 0-3, the audio bits; an AUDxLC long is written as
"hunk:offset" of the address it points to), joined by spaces; the file keeps, per run of BLOCK (25) ticks, the first
16 hex digits of the SHA-1 of those ticks' lines joined by newlines (GoldBox.Core's AmigaSoundProgramTests computes
the same), or the lines themselves with --full. The code bytes at every entry point are checked first.

The player patches immediates in its own code (hunk 27 +0x7E93, +0x80F5, +0x817F); every write into hunk 27 drops
unicorn's translation cache for that range so the patched bytes are what runs.

The output holds custom-chip writes only (no samples or tables); it goes in tests/fixtures-private all the same.
"""
import hashlib
import json
import struct
import sys

from unicorn import Uc, UC_ARCH_M68K, UC_MODE_BIG_ENDIAN, UC_HOOK_CODE, UC_HOOK_MEM_WRITE
from unicorn.m68k_const import UC_CPU_M68K_M68040, UC_M68K_REG_A7, UC_M68K_REG_D0

BLOCK = 25
CUSTOM = 0xDFF000
SENTINEL = 0x80000          # return address: a STOP never reached (emulation ends on it)
STACK_TOP = 0x7F000


def base(i):
    return 0x100000 * (i + 1)


def load_hunks(data):
    L = lambda o: struct.unpack('>I', data[o:o + 4])[0]
    if L(0) != 0x3F3:
        raise SystemExit('not an AmigaDOS hunk executable')
    o = 4
    while L(o):
        o += 4 + 4 * L(o)
    o += 4
    first, last = L(o + 4), L(o + 8)
    o += 12 + 4 * (last - first + 1)
    hunks, relocs = [], []
    while o + 4 <= len(data):
        t = L(o) & 0x3FFFFFFF
        o += 4
        if t in (0x3E9, 0x3EA):
            n = L(o)
            hunks.append(bytearray(data[o + 4:o + 4 + 4 * n]))
            o += 4 + 4 * n
        elif t == 0x3EB:
            hunks.append(bytearray(4 * L(o)))
            o += 4
        elif t == 0x3EC:
            while True:
                n = L(o)
                o += 4
                if n == 0:
                    break
                tgt = L(o)
                o += 4
                relocs += [(len(hunks) - 1, tgt, L(o + 4 * k)) for k in range(n)]
                o += 4 * n
        elif t == 0x3F2:
            pass
        else:
            raise SystemExit('unhandled hunk type %x' % t)
    for h, tgt, off in relocs:
        v = struct.unpack('>I', hunks[h][off:off + 4])[0] + base(tgt)
        hunks[h][off:off + 4] = struct.pack('>I', v)
    return hunks


# Entry points and the code bytes checked there (unrelocated words only).
CHECKS = [
    (27, 0x78A0, '13c0'),               # PlaySong: move.b d0,cmd
    (27, 0x78B2, '48e7fffe6100000a'),   # tick: movem; bsr +0x78C2
    (27, 0x78E2, '7207b200650000c4'),   # n <= 7 test, else the running tick +0x79AC
    (33, 0x14E, '4e55000048e7fffe'),    # play effect
    (33, 0x172, '60000006'),            # effect tick: bra +0x17A
]


class Amiga:
    def __init__(self, program):
        self.hunks = load_hunks(open(program, 'rb').read())
        for h, off, hexs in CHECKS:
            b = bytes.fromhex(hexs)
            if bytes(self.hunks[h][off:off + len(b)]) != b:
                raise SystemExit('code bytes differ at hunk %d +0x%X: not Amiga PoR v1.0' % (h, off))
        uc = Uc(UC_ARCH_M68K, UC_MODE_BIG_ENDIAN)
        uc.ctl_set_cpu_model(UC_CPU_M68K_M68040)
        uc.mem_map(0, 0x100000)
        for i, b in enumerate(self.hunks):
            uc.mem_map(base(i), max(0x1000, (len(b) + 0xFFF) & ~0xFFF))
            uc.mem_write(base(i), bytes(b))
        uc.mem_map(CUSTOM, 0x1000)
        uc.mem_write(SENTINEL, b'\x4e\x72\x27\x00')
        self.uc = uc
        self.line = []
        self.count = None       # --timing: instructions run in the current call, and where each write came
        self.writes_at = []
        uc.hook_add(UC_HOOK_MEM_WRITE, self._custom, begin=CUSTOM, end=CUSTOM + 0x1FF)
        c27 = base(27)
        uc.hook_add(UC_HOOK_MEM_WRITE, self._smc, begin=c27, end=c27 + len(self.hunks[27]) - 1)

    def _smc(self, uc, _access, addr, size, _value, _ud):
        uc.ctl_remove_cache(addr & ~0xFFF, (addr & ~0xFFF) + 0x1000)

    def where(self, v):
        i = v // 0x100000 - 1
        if 0 <= i < len(self.hunks) and v % 0x100000 < len(self.hunks[i]):
            return '%d:%x' % (i, v % 0x100000)
        return '%x' % v

    def _custom(self, uc, _access, addr, size, value, _ud):
        reg = addr - CUSTOM
        value &= (1 << (8 * size)) - 1
        if self.count is not None:
            self.writes_at.append((self.count[0], reg, value))
        if reg == 0x96:
            self.line.append('96:%x' % (value & 0x800F))
        elif 0xA0 <= reg < 0xE0:
            if size == 4:
                self.line.append('%x:%s' % (reg, self.where(value)))
            else:
                self.line.append('%x:%x' % (reg, value))

    def call(self, addr, d0=None, args=()):
        uc = self.uc
        sp = STACK_TOP
        for a in reversed(args):
            sp -= 4
            uc.mem_write(sp, struct.pack('>I', a))
        sp -= 4
        uc.mem_write(sp, struct.pack('>I', SENTINEL))
        uc.reg_write(UC_M68K_REG_A7, sp)
        if d0 is not None:
            uc.reg_write(UC_M68K_REG_D0, d0)
        uc.emu_start(addr, SENTINEL, count=2_000_000)

    def play_song(self, n):
        self.call(base(27) + 0x78A0, d0=n)

    def song_tick(self):
        self.line = []
        self.call(base(27) + 0x78B2)
        return ' '.join(self.line)

    def play_effect(self, n):
        self.call(base(33) + 0x14E, args=(n,))

    def effect_tick(self):
        self.line = []
        self.call(base(33) + 0x172)
        return ' '.join(self.line)

    def counted_song_tick(self):
        """--timing: a music tick with the instruction count at each custom-chip write."""
        if self.count is None:
            self.count = [0]
            self.uc.hook_add(UC_HOOK_CODE, lambda _uc, _a, _s, c: c.__setitem__(0, c[0] + 1), self.count)
        self.count[0] = 0
        self.writes_at = []
        self.song_tick()
        return self.count[0], self.writes_at


def run(program, ticks):
    res = {'block': BLOCK, 'ticks': ticks, 'songs': {}, 'effects': {}, 'cases': {}}
    for n in range(0, 7):
        a = Amiga(program)
        a.play_song(n)
        res['songs'][str(n)] = [a.song_tick() for _ in range(ticks)]
    for n in range(0, 15):
        a = Amiga(program)
        a.play_effect(n)
        res['effects'][str(n)] = [a.effect_tick() for _ in range(min(ticks, 600))]
    # a song started over a running one, and a stop mid-song
    a = Amiga(program)
    a.play_song(1)
    t = [a.song_tick() for _ in range(300)]
    a.play_song(3)
    t += [a.song_tick() for _ in range(300)]
    a.play_song(0)
    t += [a.song_tick() for _ in range(50)]
    res['cases']['1-3-0'] = t
    # an effect over a running song (s43): PlayEffect (hunk 33 +0x14E) clears "music on", so the vertical-blank
    # handler (+0x82) runs the effect tick from the next frame on instead of the music tick; then a new PlaySong
    a = Amiga(program)
    a.play_song(2)
    t = [a.song_tick() for _ in range(100)]
    a.play_effect(4)
    t += [a.effect_tick() for _ in range(100)]
    a.play_song(2)
    t += [a.song_tick() for _ in range(100)]
    res['cases']['2-e4-2'] = t
    return res


def timing(program, song, ticks):
    """Per music tick: instructions before the output's DMACON write (+0x82BE, the channels switched on)."""
    a = Amiga(program)
    a.play_song(song)
    rows = []
    for t in range(ticks):
        total, writes = a.counted_song_tick()
        on = [n for n, reg, v in writes if reg == 0x96 and v & 0x8000]
        starts = sum(1 for n, reg, v in writes if reg == 0x96 and not v & 0x8000 and n < (on[0] if on else 0))
        rows.append((t, on[0] if on else None, total, starts))
    return rows


def blocks(trace):
    return [hashlib.sha1('\n'.join(trace[i:i + BLOCK]).encode()).hexdigest()[:16] for i in range(0, len(trace), BLOCK)]


def main(argv):
    if len(argv) > 2 and argv[1] == '--timing':
        # --timing PROGRAM SONG [TICKS]: instructions from the tick's start to the output's DMACON write
        rows = timing(argv[2], int(argv[3]), int(argv[4]) if len(argv) > 4 else 385)
        for t, on, total, starts in rows:
            print('%5d %5s %5d %d' % (t, on, total, starts))
        return 0
    full = '--full' in argv
    argv = [a for a in argv if a != '--full']
    if len(argv) < 3:
        print(__doc__)
        return 2
    ticks = int(argv[3]) if len(argv) > 3 else 13000
    res = run(argv[1], ticks)
    if not full:
        for k in ('songs', 'effects', 'cases'):
            res[k] = {n: blocks(t) for n, t in res[k].items()}
    with open(argv[2], 'w') as f:
        json.dump(res, f, indent=0, sort_keys=True)
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
