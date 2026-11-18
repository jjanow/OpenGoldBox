#!/usr/bin/env python3
"""Sound-driver fixtures from the program itself: runs Pool of Radiance v1.3's PC-speaker / Tandy driver (START.EXE,
docs/re/sound.md) under unicorn (por_emu.py) and records what it writes to the sound ports on every timer tick.

Usage: python3 -I por_sound_oracle.py GAMEDIR OUT.json [TICKS] [--full]   (GAMEDIR: the PoR v1.3 files; unicorn 2.x)

For each device (speaker, tandy) and each tune id 0..20 the oracle runs the driver's own routines: the timer install
(INT 21h 35h/25h served here), the device's tune programmer (far call, the tune id as argument), then the timer
interrupt handler TICKS times (default 600), each as a real interrupt (flags, CS, IP pushed; IRET back). The BIOS
handler the driver chains every 13th tick is an IRET here. Per tick the JSON holds the OUT writes in order as
"port:value" (hex), joined by spaces; the file keeps, per run of BLOCK (25) ticks, the first 16 hex digits of the
SHA-1 of those ticks' lines joined by newlines (GoldBox.Core's SoundDriverProgramTests computes the same), or the lines
themselves with --full. The entry points are found by the same code signatures the importer uses
(GoldBox.Data PorSoundDriver), not fixed offsets. Two more cases check the driver's state across tunes: a second tune
started mid-way (the voices it has no stream for keep running) and output gating (command 0 / 1 path).

The output holds the driver's port writes only (no tables); it goes in tests/fixtures-private all the same.
"""
import hashlib
import json
import os
import re
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from por_emu import Emu, SENT_SEG, STACKSEG  # noqa: E402
from unicorn import UC_HOOK_INSN  # noqa: E402
from unicorn.x86_const import (UC_X86_INS_IN, UC_X86_INS_OUT, UC_X86_REG_AX, UC_X86_REG_BX, UC_X86_REG_CS,  # noqa: E402
                               UC_X86_REG_DX, UC_X86_REG_DS, UC_X86_REG_ES, UC_X86_REG_IP, UC_X86_REG_SP,
                               UC_X86_REG_SS)

BLOCK = 25
IRET_STUB = 0x10  # SENT_SEG:0010 holds an IRET: the chained BIOS handler


def find_driver(img):
    """Return dict of segment and entry offsets, found by code signatures (as PorSoundDriver does)."""
    m = list(re.finditer(re.escape(b'\xb0\xb6\xe6\x43\xb8') + b'..' + re.escape(b'\xe6\x40\x8a\xc4\xe6\x40'), img, re.S))
    if len(m) != 1:
        raise ValueError('timer install signature found %d times' % len(m))
    p = m[0].start()
    # install: push ds; push es; mov ax,CODE; mov ds,ax ... back from the signature
    q = img.rfind(b'\x1e\x06\xb8', 0, p)
    seg = struct.unpack_from('<H', img, q + 3)[0]
    base = seg * 16
    d = {'seg': seg, 'install': q - base, 'divisor': struct.unpack_from('<H', img, p + 5)[0]}
    # the ISR offset: mov dx, isr; mov ax, 2508h; int 21h
    m = re.search(re.escape(b'\xba') + b'..' + re.escape(b'\xb8\x08\x25\xcd\x21'), img[q:p], re.S)
    d['isr'] = struct.unpack_from('<H', img, q + m.start() + 1)[0]
    seg_bytes = img[base:base + 0x10000]
    # tune programmers: push bp; mov bp,sp; push ax; push bx; push di; push si; push ds; mov si,CODE; mov ds,si
    progs = [x.start() for x in re.finditer(re.escape(b'\x55\x8b\xec\x50\x53\x57\x56\x1e\xbe' + struct.pack('<H', seg)
                                                      + b'\x8e\xde'), seg_bytes)]
    if len(progs) != 2:
        raise ValueError('tune programmers found %d times' % len(progs))
    # each: mov di, blocks; mov ax, [si + table]
    blocks = {}
    for o in progs:
        mm = re.search(re.escape(b'\xbf') + b'..' + re.escape(b'\x8b\x84'), seg_bytes[o:o + 64], re.S)
        blocks[o] = struct.unpack_from('<H', seg_bytes, o + mm.start() + 1)[0]
    # the tick routine that writes port 42h drives the speaker: mov si, blocks before it
    t = seg_bytes.find(b'\xe6\x42')
    spk_blocks = None
    for x in re.finditer(re.escape(b'\xbe') + b'..' + re.escape(b'\x89\x36'), seg_bytes[:t], re.S):
        spk_blocks = struct.unpack_from('<H', seg_bytes, x.start() + 1)[0]
    for o, b in blocks.items():
        d['speaker' if b == spk_blocks else 'tandy'] = o
    if 'speaker' not in d or 'tandy' not in d:
        raise ValueError('speaker/tandy programmers not told apart')
    return d


class SoundOracle(Emu):
    def __init__(self, gamedir):
        rd = lambda n: open(os.path.join(gamedir, n), 'rb').read()  # noqa: E731
        super().__init__(rd('START.EXE'), rd('GAME.OVR'))
        self.drv = find_driver(self.S)
        self.ports = []
        self.p61 = 0
        self.vector = (0, 0)
        self.wb(SENT_SEG * 16 + IRET_STUB, b'\xcf')
        self.uc.hook_add(UC_HOOK_INSN, self._out, None, 1, 0, UC_X86_INS_OUT)
        self.uc.hook_add(UC_HOOK_INSN, self._in, None, 1, 0, UC_X86_INS_IN)

    def _out(self, uc, port, size, value, _ud):
        if port == 0x61:
            self.p61 = value & 0xFF
        if port != 0x20:  # the EOI is not sound
            self.ports.append('%02X:%02X' % (port, value & 0xFF))

    def _in(self, uc, port, size, _ud):
        return self.p61 if port == 0x61 else 0

    def _intr(self, uc, intno, ud):
        if intno == 0x21:
            ah = uc.reg_read(UC_X86_REG_AX) >> 8
            if ah == 0x35:  # get vector: the BIOS handler is the IRET stub
                uc.reg_write(UC_X86_REG_ES, SENT_SEG)
                uc.reg_write(UC_X86_REG_BX, IRET_STUB)
                return
            if ah == 0x25:
                self.vector = (uc.reg_read(UC_X86_REG_DS), uc.reg_read(UC_X86_REG_DX))
                return
        super()._intr(uc, intno, ud)

    def far(self, off, args=()):
        self.call((self.drv['seg'], off), args)

    def tick(self):
        """One timer interrupt: push flags, CS, IP and enter the installed handler."""
        uc = self.uc
        seg, off = self.vector
        uc.reg_write(UC_X86_REG_SS, STACKSEG)
        sp = 0xFFF0 - 6
        self.wb(STACKSEG * 16 + sp, struct.pack('<HHH', 0, SENT_SEG, 0x0202))
        uc.reg_write(UC_X86_REG_SP, sp)
        uc.reg_write(UC_X86_REG_CS, seg)
        uc.reg_write(UC_X86_REG_IP, off)
        self.ports = []
        uc.emu_start(seg * 16 + off, SENT_SEG * 16, count=2_000_000)
        if uc.reg_read(UC_X86_REG_CS) != SENT_SEG or uc.reg_read(UC_X86_REG_IP) != 0:
            raise RuntimeError('tick did not return')
        return ' '.join(self.ports)


def run(gamedir, ticks):
    out = {'ticks': ticks, 'tunes': {}, 'cases': {}}
    for dev in ('speaker', 'tandy'):
        for tune in range(21):
            o = SoundOracle(gamedir)
            o.far(o.drv['install'])
            o.far(o.drv[dev], [tune])
            out['tunes']['%s/%d' % (dev, tune)] = [o.tick() for _ in range(ticks)]
    # a tune started while another runs (no reset of the driver between them)
    for dev, a, b in (('speaker', 1, 9), ('tandy', 1, 2), ('tandy', 13, 6)):
        o = SoundOracle(gamedir)
        o.far(o.drv['install'])
        o.far(o.drv[dev], [a])
        tr = [o.tick() for _ in range(40)]
        o.far(o.drv[dev], [b])
        tr += [o.tick() for _ in range(ticks - 40)]
        out['cases']['%s/%d+40/%d' % (dev, a, b)] = tr
    d = SoundOracle(gamedir).drv
    out['driver'] = {k: v for k, v in d.items()}
    return out


def blocks(trace):
    return [hashlib.sha1('\n'.join(trace[i:i + BLOCK]).encode()).hexdigest()[:16] for i in range(0, len(trace), BLOCK)]


def main(argv):
    full = '--full' in argv
    argv = [a for a in argv if a != '--full']
    if len(argv) < 3:
        print(__doc__)
        return 2
    ticks = int(argv[3]) if len(argv) > 3 else 600
    res = run(argv[1], ticks)
    if not full:
        res['block'] = BLOCK
        for group in ('tunes', 'cases'):
            res[group] = {k: blocks(v) for k, v in res[group].items()}
    with open(argv[2], 'w') as f:
        json.dump(res, f, indent=0, sort_keys=True)
    print('driver', {k: hex(v) for k, v in res['driver'].items()})
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
