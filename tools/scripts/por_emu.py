#!/usr/bin/env python3
"""Run Pool of Radiance (DOS v1.3) routines from the player's START.EXE and GAME.OVR under the unicorn CPU emulator.

A test oracle for rules read from the program: a routine is called with memory set up by the caller and its results
are read back. START.EXE is loaded (EXEPACK expanded) at segment 0, so its segment values need no relocation; the data
segment is START.EXE's load segment + its DS (found from the entry point, as GoldBox.Data's PorProgram does). Every
GAME.OVR unit stays resident at its own segment and the overlay stubs (INT 3Fh entries) are resolved on the fly.
Routines that draw, play sounds or wait for keys must be trapped by the caller (``trap_ovr`` / ``trap_start``).

Needs ``pip install unicorn`` (2.x). Nothing from the game is stored by this module.
"""
import struct

from unicorn import Uc, UC_ARCH_X86, UC_MODE_16, UC_HOOK_INTR, UC_HOOK_CODE
from unicorn.x86_const import (UC_X86_REG_AX, UC_X86_REG_CS, UC_X86_REG_DS, UC_X86_REG_DX, UC_X86_REG_ES,
                               UC_X86_REG_IP, UC_X86_REG_SP, UC_X86_REG_SS)

STACKSEG = 0x2000
HEAPSEG = 0x3000
SENT_SEG = 0x3F00
UNITBASE = 0x4000


def load_image(raw):
    """The MZ load module, EXEPACK expanded (documented Microsoft scheme). Returns (image, cs, ip)."""
    cblp, cp, _crlc, cpar = struct.unpack_from('<4H', raw, 2)
    ip, cs = struct.unpack_from('<2H', raw, 0x14)
    size = (cp - 1) * 512 + cblp if cblp else cp * 512
    lm = raw[cpar * 16:size]
    eh = cs * 16
    if lm[eh + 16:eh + 18] != b'RB':
        return bytes(lm), cs, ip
    rip, rcs, _mem, _esz, _rsp, _rss, dlen, skip = struct.unpack_from('<8H', lm, eh)
    src_end = eh - (skip - 1) * 16
    buf = bytearray(max(dlen * 16, src_end))
    buf[:src_end] = lm[:src_end]
    s = src_end - 1
    while buf[s] == 0xFF:
        s -= 1
    d = dlen * 16 - 1
    while True:
        c = buf[s]
        n = buf[s - 1] << 8 | buf[s - 2]
        s -= 3
        if c & 0xFE == 0xB0:
            f = buf[s]
            s -= 1
            for _ in range(n):
                buf[d] = f
                d -= 1
        elif c & 0xFE == 0xB2:
            for _ in range(n):
                buf[d] = buf[s]
                d -= 1
                s -= 1
        else:
            raise ValueError('EXEPACK: bad opcode %x' % c)
        if c & 1:
            break
    return bytes(buf[:dlen * 16]), rcs, rip


def data_segment(img, cs, ip):
    """Turbo Pascal: the entry far-calls System init, which starts with mov dx,DATA / mov ds,dx."""
    e = cs * 16 + ip
    off, seg = struct.unpack_from('<2H', img, e + 1)
    t = seg * 16 + off
    if img[t] != 0xBA or img[t + 3:t + 5] != b'\x8e\xda':
        raise ValueError('data segment not found')
    return struct.unpack_from('<H', img, t + 1)[0]


class Emu:
    def __init__(self, start_exe, game_ovr):
        self.S, cs, ip = load_image(start_exe)
        self.O = game_ovr
        self.dseg = data_segment(self.S, cs, ip)
        self.stubs = {}
        for p in range(len(self.S) // 16):
            o = p * 16
            if self.S[o:o + 4] == b'\xcd\x3f\x00\x00':
                fo, size = struct.unpack_from('<IH', self.S, o + 4)
                if fo < len(self.O):
                    self.stubs[p] = (fo, size)
        self.units = sorted((fo, size, p) for p, (fo, size) in self.stubs.items())
        self.uc = uc = Uc(UC_ARCH_X86, UC_MODE_16)
        uc.mem_map(0, 0x100000)
        uc.mem_write(0, self.S)
        self.unitseg = {}
        nxt = UNITBASE
        for fo, size, p in self.units:
            self.unitseg[p] = nxt
            uc.mem_write(nxt * 16, self.O[fo:fo + size])
            nxt += (size + 15) // 16 + 1
        if nxt * 16 >= 0x100000:
            raise ValueError('overlay units do not fit')
        uc.mem_write(SENT_SEG * 16, b'\xf4')
        uc.hook_add(UC_HOOK_INTR, self._intr)
        self.heap = HEAPSEG * 16
        self.traps = {}

    # ---- overlay stubs, traps ----
    def unit_linear(self, ovroff):
        for fo, size, p in self.units:
            if fo <= ovroff < fo + size:
                return self.unitseg[p] * 16 + (ovroff - fo), self.unitseg[p], ovroff - fo
        raise KeyError(hex(ovroff))

    def _intr(self, uc, intno, _ud):
        cs = uc.reg_read(UC_X86_REG_CS)
        ip = uc.reg_read(UC_X86_REG_IP)
        if intno == 0x3F and cs in self.stubs:
            r = struct.unpack('<H', uc.mem_read(cs * 16 + ip, 2))[0]
            uc.reg_write(UC_X86_REG_CS, self.unitseg[cs])
            uc.reg_write(UC_X86_REG_IP, r)
            return
        raise RuntimeError('int %#x at %04x:%04x (trap the routine that calls it)' % (intno, cs, ip))

    def _code(self, uc, addr, _size, _ud):
        f = self.traps.get(addr)
        if f:
            f(self)

    def _trap(self, lin, fn):
        # a second trap on the same address replaces the first (one hook, so the trap does not run twice)
        new = lin not in self.traps
        self.traps[lin] = fn
        if new:
            self.uc.hook_add(UC_HOOK_CODE, self._code, begin=lin, end=lin)

    def trap_ovr(self, ovroff, fn):
        self._trap(self.unit_linear(ovroff)[0], fn)

    def trap_start(self, seg, off, fn):
        self._trap(seg * 16 + off, fn)

    def stack(self):
        uc = self.uc
        return uc.reg_read(UC_X86_REG_SS) * 16 + uc.reg_read(UC_X86_REG_SP)

    def arg(self, k):
        """Word k bytes above the far return address (at a trapped routine's entry)."""
        return struct.unpack('<H', self.rb(self.stack() + 4 + k, 2))[0]

    def pop_ret(self, nargs_bytes, ax=None):
        """Return from a trapped far routine (retf nargs_bytes)."""
        uc = self.uc
        sp = uc.reg_read(UC_X86_REG_SP)
        ip, cs = struct.unpack('<HH', self.rb(self.stack(), 4))
        if ax is not None:
            uc.reg_write(UC_X86_REG_AX, ax)
        uc.reg_write(UC_X86_REG_SP, sp + 4 + nargs_bytes)
        uc.reg_write(UC_X86_REG_CS, cs)
        uc.reg_write(UC_X86_REG_IP, ip)

    @staticmethod
    def ret(nbytes, ax=None):
        return lambda e: e.pop_ret(nbytes, ax)

    # ---- memory ----
    def ds(self, off):
        return self.dseg * 16 + off

    def rb(self, lin, n=1):
        return bytes(self.uc.mem_read(lin, n))

    def wb(self, lin, data):
        self.uc.mem_write(lin, bytes(data))

    def dsb(self, off):
        return self.rb(self.ds(off))[0]

    def dsw(self, off):
        return struct.unpack('<H', self.rb(self.ds(off), 2))[0]

    def set_dsb(self, off, v):
        self.wb(self.ds(off), [v & 0xFF])

    def set_dsw(self, off, v):
        self.wb(self.ds(off), struct.pack('<H', v & 0xFFFF))

    def set_dsptr(self, off, ptr):
        seg, o = ptr
        self.wb(self.ds(off), struct.pack('<HH', o, seg))

    def dsptr(self, off):
        o, s = struct.unpack('<HH', self.rb(self.ds(off), 4))
        return s, o

    def alloc(self, n):
        """Zeroed memory from a private arena: (seg, 0)."""
        lin = (self.heap + 15) & ~15
        self.heap = lin + n
        if self.heap >= SENT_SEG * 16:
            raise MemoryError('emulator heap full')
        self.wb(lin, bytes(n))
        return lin >> 4, 0

    def reset_heap(self):
        self.heap = HEAPSEG * 16

    def randseed(self):
        return self.dsw(0x4428) | self.dsw(0x442A) << 16

    def set_randseed(self, v):
        self.set_dsw(0x4428, v & 0xFFFF)
        self.set_dsw(0x442A, v >> 16)

    def call(self, where, args=(), limit=200_000_000):
        """Far call. where = GAME.OVR file offset, or (seg, off) in START.EXE. args: words, pushed in order (Pascal
        pushes the first parameter first; a far pointer is (seg, off) = two words, segment first)."""
        uc = self.uc
        uc.reg_write(UC_X86_REG_DS, self.dseg)
        uc.reg_write(UC_X86_REG_ES, self.dseg)
        uc.reg_write(UC_X86_REG_SS, STACKSEG)
        sp = 0xFFF0
        # The stack below the arguments holds 0xEE, so a value the routine never set reads the same every run.
        self.wb(STACKSEG * 16 + 0xF000, b'\xee' * 0xFF0)
        for a in args:
            sp -= 2
            self.wb(STACKSEG * 16 + sp, struct.pack('<H', a & 0xFFFF))
        sp -= 4
        self.wb(STACKSEG * 16 + sp, struct.pack('<HH', 0, SENT_SEG))
        uc.reg_write(UC_X86_REG_SP, sp)
        if isinstance(where, tuple):
            cs, ip = where
            lin = cs * 16 + ip
        else:
            lin, cs, ip = self.unit_linear(where)
        uc.reg_write(UC_X86_REG_CS, cs)
        uc.reg_write(UC_X86_REG_IP, ip)
        uc.emu_start(lin, SENT_SEG * 16, count=limit)
        if uc.reg_read(UC_X86_REG_CS) != SENT_SEG:
            raise RuntimeError('did not return: %04x:%04x' % (uc.reg_read(UC_X86_REG_CS), uc.reg_read(UC_X86_REG_IP)))
        return uc.reg_read(UC_X86_REG_AX), uc.reg_read(UC_X86_REG_DX)
