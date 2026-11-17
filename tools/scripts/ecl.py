#!/usr/bin/env python3
"""Pool of Radiance (DOS v1.3) ECL event scripts: list / disassemble / check.

Usage (run with ``python3 -I``; inputs are untrusted data):
    ecl.py list  GAMEDIR                   # every ECL block: size, entry vectors, maps it loads
    ecl.py dis   GAMEDIR ECLn BLOCK [--hex] # disassembly (addresses, decoded strings, labels)
    ecl.py check GAMEDIR [-v]               # decode every block, report coverage / errors / opcode use

GAMEDIR holds ECL1.DAX .. ECL8.DAX (any case).  ECLn may be given as "ECL3",
"ECL3.DAX" or "3".  BLOCK is the DAX block id (= map/script id).

Format: docs/re/ecl-format.md.  Summary of what this tool relies on (all
confirmed in GAME.OVR, see the spec for addresses):

  * Block = u16 prefix (0x1388, not loaded) + script bytes loaded at VM $9900.
  * $9900..$9913: five 4-byte entry vectors (1 ignored byte + 1 operand each).
  * Instruction = 1 opcode byte + operands.  Operand = type byte + payload:
      0 -> 1 byte; 1, 2, 3, 0x81 -> 2 bytes little-endian; 0x80 -> length byte
      + that many bytes of 6-bit packed text; any other type -> 1 byte.
  * Operand counts are fixed per opcode, except VMENU (15), ONGOTO/ONGOSUB
    (25/26) and HMENU (2B) whose last fixed operand is the count of extra ones.
  * An IF that fails skips the next instruction using a *separate* operand
    count table (SKIP below).  For 0C, 15, 25, 26, 2B and 36 that count is
    smaller than the real one, so the game resumes inside the instruction.

Flow analysis starts at the five vectors and follows GOTO/GOSUB/ON targets,
IF-true and IF-skip edges and fall-through.  Edges that the game takes only
in some states (return after GOSUB, fall-through after ON GOTO when the
selector is out of range, IF-skip past a mis-skipped instruction) are marked
speculative; decode failures on those paths are reported separately.  A
speculative path also stops where it reaches a byte that a script operand
addresses (a table).  In ``dis`` output a "?" after the address marks an
instruction reached only on speculative paths; "Dxxxx:" labels mark tables.

Mnemonics are our own.  Nothing from the game files is embedded here.
"""
import collections
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dax  # noqa: E402

BASE = 0x9900          # VM address of block byte 2
PREFIX = 2             # block bytes not loaded
VM_END = 0xB6FF        # last script address (7680 bytes)
NVEC = 5
VECTOR_NAMES = ("move", "search", "precamp", "campint", "init")

# opcode: (mnemonic, operand names for the fixed part, variadic kind or None)
# variadic kinds: 'menu' (VMENU: count = 3rd operand, extra operands are strings),
#                 'on' (ONGOTO/ONGOSUB: count = 2nd operand, extra operands are targets),
#                 'hmenu' (HMENU: count = 2nd operand, extra operands are strings)
OPS = {
    0x00: ("EXIT", (), None),
    0x01: ("GOTO", ("target",), None),
    0x02: ("GOSUB", ("target",), None),
    0x03: ("COMPARE", ("a", "b"), None),
    0x04: ("ADD", ("a", "b", "dst"), None),
    0x05: ("SUB", ("a", "b", "dst"), None),
    0x06: ("DIV", ("a", "b", "dst"), None),
    0x07: ("MUL", ("a", "b", "dst"), None),
    0x08: ("RANDOM", ("max", "dst"), None),
    0x09: ("SAVE", ("src", "dst"), None),
    0x0A: ("LOADCHAR", ("index",), None),
    0x0B: ("LOADMON", ("monster", "count", "picture"), None),
    0x0C: ("SETUPMON", ("monster", "distance", "picture"), None),
    0x0D: ("APPROACH", (), None),
    0x0E: ("PICTURE", ("picture",), None),
    0x0F: ("INPUTNUM", ("digits", "dst"), None),
    0x10: ("INPUTSTR", ("length", "dst"), None),
    0x11: ("PRINT", ("text",), None),
    0x12: ("PRINTCLR", ("text",), None),
    0x13: ("RETURN", (), None),
    0x14: ("CMPAND", ("a1", "b1", "a2", "b2"), None),
    0x15: ("VMENU", ("dst", "prompt", "count"), "menu"),
    0x16: ("IFEQ", (), None),
    0x17: ("IFNE", (), None),
    0x18: ("IFLT", (), None),
    0x19: ("IFGT", (), None),
    0x1A: ("IFLE", (), None),
    0x1B: ("IFGE", (), None),
    0x1C: ("CLRMON", (), None),
    0x1D: ("PARTYSTR", ("dst",), None),
    0x1E: ("CHKPARTY", ("field", "effect", "dst_min", "dst_max", "dst_avg", "dst_any"), None),
    0x20: ("NEWECL", ("script",), None),
    0x21: ("LOADFILES", ("map", "unused", "flag"), None),
    0x22: ("PSURPRISE", ("dst_party", "dst_mon"), None),
    0x23: ("SURPRISE", ("party1", "party2", "mon1", "mon2"), None),
    0x24: ("COMBAT", (), None),
    0x25: ("ONGOTO", ("select", "count"), "on"),
    0x26: ("ONGOSUB", ("select", "count"), "on"),
    0x27: ("TREASURE", ("cp", "sp", "ep", "gp", "pp", "gems", "jewelry", "items"), None),
    0x28: ("ROB", ("whole_party", "money_pct", "item_chance"), None),
    0x29: ("ENCMENU", ("monster", "distance", "picture", "dst", "r_combat", "r_wait", "r_flee",
                       "r_advance", "r_parley", "msg0", "msg1", "msg2", "move1", "move2"), None),
    0x2A: ("GETTABLE", ("table", "index", "dst"), None),
    0x2B: ("HMENU", ("dst", "count"), "hmenu"),
    0x2C: ("PARLEY", ("haughty", "sly", "nice", "meek", "abusive", "dst"), None),
    0x2D: ("CALL", ("routine",), None),
    0x2E: ("DAMAGE", ("attack", "dice", "sides", "bonus", "extra"), None),
    0x2F: ("AND", ("a", "b", "dst"), None),
    0x30: ("OR", ("a", "b", "dst"), None),
    0x31: ("SPRITEOFF", (), None),
    0x32: ("FINDITEM", ("item",), None),
    0x33: ("PRINTRET", (), None),
    0x34: ("CLOCK", ("minutes",), None),
    0x35: ("SAVETABLE", ("src", "table", "index"), None),
    0x36: ("ADDNPC", ("monster", "morale"), None),
    0x37: ("LOADPIECES", ("walldef1", "walldef2", "walldef3"), None),
    0x38: ("PROGRAM", ("program",), None),
    0x39: ("WHO", ("prompt",), None),
    0x3A: ("DELAY", (), None),
    0x3B: ("SPELL", ("spell", "dst_slot", "dst_char"), None),
    0x3C: ("PROTECT", ("src",), None),
    0x3D: ("CLEARBOX", (), None),
}
# 0x1F has no handler in the dispatcher (the PC is not advanced); >= 0x3E likewise.
UNHANDLED_NAME = {0x1F: "UNDEF1F"}

# Operand count the IF-skip routine uses (GAME.OVR 0x9379).  Opcodes not listed: 0.
SKIP = {}
for _n, _ops in ((1, (0x01, 0x02, 0x0A, 0x0E, 0x11, 0x12, 0x1D, 0x20, 0x2D, 0x32, 0x34, 0x36, 0x38, 0x39, 0x3C)),
                 (2, (0x03, 0x08, 0x09, 0x0C, 0x0F, 0x10, 0x1F, 0x22)),
                 (3, (0x04, 0x05, 0x06, 0x07, 0x0B, 0x21, 0x28, 0x2A, 0x2F, 0x30, 0x35, 0x37, 0x3B)),
                 (4, (0x14, 0x23)), (5, (0x2E,)), (6, (0x1E, 0x2C)), (8, (0x27,)), (14, (0x29,))):
    for _o in _ops:
        SKIP[_o] = _n

IFS = range(0x16, 0x1C)
TERMINATORS = (0x00, 0x01, 0x13, 0x20)      # no fall-through
JUMP_EDGES = ("vector", "goto", "gosub", "on-goto", "on-gosub")


def opname(op):
    if op in OPS:
        return OPS[op][0]
    return UNHANDLED_NAME.get(op, "OP_%02X" % op)


# --------------------------------------------------------------------------
# Strings: 6-bit packing (GAME.OVR 0x8626, character map 0x7BA6)
# --------------------------------------------------------------------------
def unpack_text(data):
    """Packed bytes -> text.  4 characters per 3 bytes, MSB first; code 0 is skipped,
    1..31 -> chr(code + 64), 32..63 -> chr(code)."""
    out = []
    acc = nb = 0
    for x in data:
        acc = (acc << 8) | x
        nb += 8
        while nb >= 6:
            nb -= 6
            c = (acc >> nb) & 63
            if c:
                out.append(chr(c + 64) if c < 32 else chr(c))
        acc &= (1 << nb) - 1
    return "".join(out)


# --------------------------------------------------------------------------
# Decoding
# --------------------------------------------------------------------------
class DecodeError(Exception):
    pass


class Operand:
    __slots__ = ("type", "lo", "hi", "data", "size")

    def __init__(self, type_, lo, hi, data, size):
        self.type, self.lo, self.hi, self.data, self.size = type_, lo, hi, data, size

    @property
    def word(self):
        return self.lo | (self.hi << 8) if self.hi is not None else None

    def is_mem(self):
        return self.type in (1, 3)

    def is_imm(self):
        return self.type in (0, 2)

    def value(self):
        """Immediate value (types 0/2); None otherwise."""
        if self.type == 0:
            return self.lo
        if self.type == 2:
            return self.word
        return None

    def text(self):
        return unpack_text(self.data) if self.type == 0x80 else None


def decode_operand(b, p):
    if p + 1 >= len(b):
        raise DecodeError("operand runs past end of block")
    t = b[p]
    if t in (1, 2, 3, 0x81):
        if p + 2 >= len(b):
            raise DecodeError("operand runs past end of block")
        return Operand(t, b[p + 1], b[p + 2], None, 3)
    if t == 0x80:
        n = b[p + 1]
        if p + 2 + n > len(b):
            raise DecodeError("string runs past end of block")
        return Operand(t, n, None, bytes(b[p + 2:p + 2 + n]), 2 + n)
    # type 0, or an unknown type: the loader takes one value byte
    return Operand(t, b[p + 1], None, None, 2)


class Insn:
    __slots__ = ("off", "op", "operands", "size", "skip_size", "notes")

    def __init__(self, off, op):
        self.off, self.op = off, op
        self.operands = []
        self.size = 1
        self.skip_size = 1
        self.notes = []

    @property
    def addr(self):
        return BASE + self.off - PREFIX

    def handled(self):
        return self.op in OPS

    def role(self, i):
        spec = OPS.get(self.op)
        if not spec:
            return "?"
        fixed = spec[1]
        if i < len(fixed):
            return fixed[i]
        return {"menu": "choice", "hmenu": "choice", "on": "target"}[spec[2]]


def decode_insn(b, off):
    """Decode one instruction at block offset ``off`` (>= PREFIX)."""
    if off >= len(b):
        raise DecodeError("past end of block")
    ins = Insn(off, b[off])
    op = ins.op
    if op not in OPS:
        return ins            # no handler: size 1, flow stops
    _name, fixed, var = OPS[op]
    p = off + 1
    sizes = []
    for _ in fixed:
        o = decode_operand(b, p)
        ins.operands.append(o)
        sizes.append(o.size)
        p += o.size
    if var:
        cnt_op = ins.operands[2 if var == "menu" else 1]
        if cnt_op.type != 0:
            # The handler reads the count through the value getter; a memory/16-bit count
            # cannot be decoded statically.
            raise DecodeError("variadic count operand has type %d" % cnt_op.type)
        for _ in range(cnt_op.lo):
            o = decode_operand(b, p)
            ins.operands.append(o)
            sizes.append(o.size)
            p += o.size
    for o in ins.operands:
        if o.type not in (0, 1, 2, 3, 0x80, 0x81):
            ins.notes.append("operand type %02X (value undefined)" % o.type)
    ins.size = p - off
    ins.skip_size = 1 + sum(sizes[:SKIP.get(op, 0)])
    return ins


def a2o(addr):
    return addr - BASE + PREFIX


def o2a(off):
    return BASE + off - PREFIX


def vectors(b):
    """The five entry vectors: list of (ignored byte, operand)."""
    out = []
    for k in range(NVEC):
        p = PREFIX + 4 * k
        out.append((b[p], decode_operand(b, p + 1)))
    return out


# --------------------------------------------------------------------------
# Flow analysis
# --------------------------------------------------------------------------
HARD = "hard"
SPEC = "spec"


class Analysis:
    """Static flow analysis of one script block.

    Pass 1 follows only hard edges from the five vectors.  Pass 2 adds the speculative
    edges; a speculative path stops where it reaches a byte that some script operand
    addresses (a table), and pass 2 is repeated until that set of addresses is stable.
    """

    def __init__(self, b):
        self.b = b
        roots = [(a2o(o.word), "vector", -1) for _i, o in vectors(b)]
        self.vector_offs = [r[0] for r in roots]
        hard = self._walk(roots, False, set(), None)
        stop = set(hard["refs"])
        for _ in range(20):
            full = self._walk(roots, True, stop, hard["insns"])
            new = stop | set(full["refs"])
            if new == stop:
                break
            stop = new
        self.insns = full["insns"]
        self.reach = dict((o, HARD if o in hard["insns"] else SPEC) for o in self.insns)
        self.labels = full["labels"]
        self.data_refs = full["refs"]
        self.errors = [(HARD,) + e for e in hard["errors"]] + \
                      [(SPEC,) + e for e in full["errors"] if e not in hard["errors"]]
        self.hazards = full["hazards"]
        self.undefined_targets = full["undefined"]
        self.unhandled = [(o, self.reach[o]) for o in full["unhandled"]]
        self.spec_into_data = full["into_data"]
        self.overlaps = []
        last_end, last = -1, None
        for off in sorted(self.insns):
            if off < last_end:
                self.overlaps.append((last, off))
            end = off + self.insns[off].size
            if end > last_end:
                last_end, last = end, off

    def _edges(self, ins, st):
        """Successors of a decoded instruction: list of (off, edge kind, is_hard)."""
        op = ins.op
        out = []
        for i, o in enumerate(ins.operands):
            if ins.role(i) == "target":
                if o.type == 0:
                    st["undefined"].append((ins.off, i))
                    continue
                kind = {0x01: "goto", 0x02: "gosub", 0x25: "on-goto", 0x26: "on-gosub"}[op]
                out.append((a2o(o.word), kind, True))
        nxt = ins.off + ins.size
        if op in IFS:
            out.append((nxt, "if-true", True))
            try:
                n = decode_insn(self.b, nxt)
            except DecodeError:
                n = None
            if n is not None and n.handled():
                if n.skip_size == n.size:
                    out.append((nxt + n.size, "if-skip", True))
                else:
                    st["hazards"].append((ins.off, nxt, n.op))
                    out.append((nxt + n.size, "if-skip-intended", False))
            return out
        if op in TERMINATORS:
            return out
        if op == 0x02:
            out.append((nxt, "after-gosub", True))
        elif op in (0x25, 0x26):
            out.append((nxt, "on-fallthrough", False))
        else:
            out.append((nxt, "fall", True))
        return out

    def _walk(self, roots, spec, stop, hard_insns):
        st = {"insns": {}, "labels": {}, "refs": collections.defaultdict(list), "errors": [],
              "hazards": [], "undefined": [], "unhandled": [], "into_data": []}
        work = list(roots)
        seen = set()
        n = len(self.b)
        while work:
            off, edge, src = work.pop()
            if edge in JUMP_EDGES:
                st["labels"].setdefault(off, set()).add(edge)
            if off in seen:
                continue
            seen.add(off)
            if not (PREFIX + 4 * NVEC <= off < n):
                st["errors"].append((off, src, edge, "target outside script"))
                continue
            try:
                ins = decode_insn(self.b, off)
            except DecodeError as e:
                st["errors"].append((off, src, edge, str(e)))
                continue
            if spec and off not in hard_insns and any(r in stop for r in range(off, off + ins.size)):
                st["into_data"].append((off, src, edge))
                continue
            st["insns"][off] = ins
            if not ins.handled():
                st["unhandled"].append(off)
                continue
            for i, o in enumerate(ins.operands):
                if o.is_mem() and ins.role(i) != "target" and BASE <= o.word <= VM_END:
                    st["refs"][a2o(o.word)].append((ins.off, ins.role(i)))
            for t, kind, is_hard in self._edges(ins, st):
                if is_hard or spec:
                    work.append((t, kind, off))
        return st

    def coverage(self):
        cov = bytearray(len(self.b))
        for off, ins in self.insns.items():
            for k in range(off, off + ins.size):
                cov[k] = 1 if self.reach[off] == HARD else 2
        for k in range(PREFIX, PREFIX + 4 * NVEC):
            cov[k] = 3
        return cov

    def gaps(self):
        """Unreached byte runs: list of (start, end)."""
        cov = self.coverage()
        out = []
        k = PREFIX
        n = len(self.b)
        while k < n:
            if cov[k]:
                k += 1
                continue
            s = k
            while k < n and not cov[k]:
                k += 1
            out.append((s, k))
        return out


def classify_gap(an, s, e):
    """'data' if a script operand points into the gap, 'dead-code' if it decodes cleanly as
    instructions that nothing jumps to, else 'unknown'."""
    refs = [r for r in an.data_refs if s <= r < e]
    if refs:
        return "data"
    p = s
    try:
        while p < e:
            ins = decode_insn(an.b, p)
            if not ins.handled():
                raise DecodeError("op")
            p += ins.size
        if p == e:
            return "dead-code"
    except DecodeError:
        pass
    return "unknown"


# --------------------------------------------------------------------------
# Formatting
# --------------------------------------------------------------------------
def label(off):
    return "L%04X" % o2a(off)


def fmt_operand(ins, i, o, an=None):
    role = ins.role(i)
    t = o.type
    if role == "target":
        if t == 0:
            return "#%d ;target undefined (type 0)" % o.lo
        return label(a2o(o.word))
    if t == 0:
        return "#%d" % o.lo
    if t == 2:
        return "#$%04X.w" % o.word
    if t == 1:
        return "[$%04X]" % o.word
    if t == 3:
        return "[$%04X].w" % o.word
    if t == 0x80:
        return '"%s"' % o.text().replace('"', '\\"')
    if t == 0x81:
        return "str[$%04X]" % o.word
    return "?%02X:%d" % (t, o.lo)


def fmt_insn(ins, an=None, show_hex=False):
    name = opname(ins.op)
    ops = ", ".join(fmt_operand(ins, i, o, an) for i, o in enumerate(ins.operands))
    txt = "%-10s %s" % (name, ops) if ops else name
    if show_hex:
        raw = an.b[ins.off:ins.off + ins.size].hex(" ") if an else ""
        if len(raw) > 41:
            raw = raw[:38] + "..."
        txt = "%-42s %s" % (raw, txt)
    notes = list(ins.notes)
    if not ins.handled():
        notes.append("no handler in the program")
    if an is not None:
        if ins.op in IFS:
            for h in an.hazards:
                if h[0] == ins.off:
                    notes.append("IF-skip lands inside the next instruction")
    if notes:
        txt += "    ; " + "; ".join(notes)
    return txt


def find_ecl(gamedir, name):
    n = name.upper()
    if n.isdigit():
        n = "ECL" + n
    if not n.endswith(".DAX"):
        n += ".DAX"
    for f in os.listdir(gamedir):
        if f.upper() == n:
            return os.path.join(gamedir, f)
    raise SystemExit("%s not found in %s" % (n, gamedir))


def ecl_files(gamedir):
    out = []
    for f in sorted(os.listdir(gamedir), key=str.upper):
        u = f.upper()
        if u.startswith("ECL") and u.endswith(".DAX") and u[3:-4].isdigit():
            out.append(os.path.join(gamedir, f))
    return out


def dis_block(b, show_hex=False, out=sys.stdout):
    an = Analysis(b)
    w = out.write
    w("; prefix u16 = 0x%04X (not loaded), %d script bytes at $%04X..$%04X\n"
      % (b[0] | b[1] << 8, len(b) - PREFIX, BASE, BASE + len(b) - PREFIX - 1))
    for k, (ign, o) in enumerate(vectors(b)):
        w("%04X  VECTOR     %-8s -> %s%s\n" % (o2a(PREFIX + 4 * k), VECTOR_NAMES[k], label(a2o(o.word)),
                                              "" if ign == 1 and o.type == 1 else
                                              "    ; ignored byte %02X, type %d" % (ign, o.type)))
    cov = an.coverage()
    gaps = dict((s, e) for s, e in an.gaps())
    off = PREFIX + 4 * NVEC
    n = len(b)
    while off < n:
        if off in an.labels or off in an.data_refs:
            tags = []
            if off in an.labels:
                tags.append(",".join(sorted(an.labels[off])))
            if off in an.data_refs:
                tags.append("data ref by " + ", ".join("%s(%s)" % (label(r)[1:], role)
                                                       for r, role in an.data_refs[off][:4]))
            w("%s:%s\n" % (label(off) if off in an.insns else "D%04X" % o2a(off),
                           "    ; " + "; ".join(tags) if tags else ""))
        if off in an.insns:
            ins = an.insns[off]
            mark = " " if an.reach[off] == HARD else "?"
            w("%04X %s %s\n" % (o2a(off), mark, fmt_insn(ins, an, show_hex)))
            off += ins.size
            continue
        if off in gaps:
            e = gaps[off]
        else:   # inside a gap that started at a data ref
            e = off + 1
            while e < n and not cov[e] and e not in an.data_refs and e not in an.insns:
                e += 1
        if off in gaps:
            kind = classify_gap(an, off, e)
            w("; unreached %d bytes (%s)\n" % (e - off, kind))
        # dump bytes up to e, breaking at data refs
        p = off
        while p < e:
            q = min(e, p + 16)
            for r in range(p + 1, q):
                if r in an.data_refs:
                    q = r
                    break
            w("%04X   db %s\n" % (o2a(p), b[p:q].hex(" ")))
            p = q
            if p < e and p in an.data_refs:
                break
        off = p
    for kind, eoff, src, edge, msg in an.errors:
        w("; %s ERROR at $%04X (from $%04X via %s): %s\n"
          % ("hard" if kind == HARD else "speculative", o2a(eoff), o2a(src) if src >= 0 else 0, edge, msg))
    return an


# --------------------------------------------------------------------------
# Commands
# --------------------------------------------------------------------------
def cmd_list(gamedir):
    for path in ecl_files(gamedir):
        d = dax.DaxFile(path)
        for bid, b in d.blocks():
            an = Analysis(b)
            vec = " ".join("%04X" % o.word for _i, o in vectors(b))
            def first_ops(op):
                vals = set()
                for ins in an.insns.values():
                    if ins.op == op:
                        o = ins.operands[0]
                        vals.add(str(o.lo) if o.type == 0 else "[$%04X]" % o.word)
                return sorted(vals, key=lambda v: (v.startswith("["), int(v) if v.isdigit() else 0, v))
            maps = first_ops(0x21)
            newecl = first_ops(0x20)
            pieces = sorted({tuple(o.lo for o in ins.operands) for ins in an.insns.values()
                             if ins.op == 0x37 and all(o.type == 0 for o in ins.operands)})
            print("%-9s block %2d  %5d bytes  vectors %s  LOADFILES maps %s  NEWECL %s  LOADPIECES %s"
                  % (os.path.basename(path), bid, len(b), vec, ",".join(maps) or "-", ",".join(newecl) or "-",
                     " ".join("%d/%d/%d" % p for p in pieces) or "-"))


def cmd_dis(gamedir, name, bid, show_hex):
    d = dax.DaxFile(find_ecl(gamedir, name))
    for i, b in d.blocks():
        if i == bid:
            print("; %s block %d" % (os.path.basename(d.path), bid))
            dis_block(b, show_hex)
            return
    raise SystemExit("block %d not in %s" % (bid, d.path))


def cmd_check(gamedir, verbose):
    tot = collections.Counter()
    usage = collections.Counter()
    types = collections.Counter()
    hazards_by_op = collections.Counter()
    gap_kinds = collections.Counter()
    gap_bytes = collections.Counter()
    hard_err_list = []
    spec_cut_list = []
    spec_err_list = []
    hazard_list = []
    undefined_list = []
    unhandled_list = []
    data_into_code = []
    blocks = 0
    headers = collections.Counter()
    strings = collections.Counter()
    print("%-9s %5s %6s %6s %6s %6s %5s %5s %5s %4s" % ("file", "block", "bytes", "hard", "spec", "unrch",
                                                       "ins", "herr", "serr", "haz"))
    for path in ecl_files(gamedir):
        fn = os.path.basename(path)
        for bid, b in dax.DaxFile(path).blocks():
            blocks += 1
            headers[(b[0] | b[1] << 8, tuple((ign, o.type) for ign, o in vectors(b)))] += 1
            an = Analysis(b)
            cov = an.coverage()
            hard = sum(1 for c in cov[PREFIX:] if c in (1, 3))
            spec = sum(1 for c in cov[PREFIX:] if c == 2)
            unr = sum(1 for c in cov[PREFIX:] if c == 0)
            herr = [e for e in an.errors if e[0] == HARD]
            serr = [e for e in an.errors if e[0] == SPEC]
            print("%-9s %5d %6d %6d %6d %6d %5d %5d %5d %4d" % (fn, bid, len(b) - PREFIX, hard, spec, unr,
                                                                len(an.insns), len(herr), len(serr),
                                                                len(an.hazards)))
            tot["bytes"] += len(b) - PREFIX
            tot["hard"] += hard
            tot["spec"] += spec
            tot["unreached"] += unr
            tot["insns"] += len(an.insns)
            tot["overlaps"] += len(an.overlaps)
            for ins in an.insns.values():
                usage[ins.op] += 1
                for o in ins.operands:
                    types[o.type] += 1
                    if o.type == 0x80:
                        strings["count"] += 1
                        strings["packed bytes"] += o.lo
                        strings["chars"] += len(o.text())
                        if o.lo == 0:
                            strings["empty"] += 1
                        if any(not (" " <= ch <= "_") for ch in o.text()):
                            strings["odd chars"] += 1
            for e in herr:
                hard_err_list.append((fn, bid, e))
            for e in serr:
                spec_err_list.append((fn, bid, e))
            for c in an.spec_into_data:
                spec_cut_list.append((fn, bid, c))
            for h in an.hazards:
                hazards_by_op[h[2]] += 1
                hazard_list.append((fn, bid, h))
            for u in an.undefined_targets:
                undefined_list.append((fn, bid, u))
            for u in an.unhandled:
                unhandled_list.append((fn, bid, u))
            for ref in an.data_refs:
                if ref in an.insns:
                    data_into_code.append((fn, bid, ref, an.data_refs[ref]))
            for s, e in an.gaps():
                k = classify_gap(an, s, e)
                gap_kinds[k] += 1
                gap_bytes[k] += e - s
                if verbose:
                    print("    gap $%04X..$%04X %5d bytes  %s" % (o2a(s), o2a(e) - 1, e - s, k))
    print()
    print("blocks: %d   script bytes: %d   instructions decoded: %d" % (blocks, tot["bytes"], tot["insns"]))
    print("bytes reached on hard paths (incl. 20-byte vector table): %d (%.1f%%)"
          % (tot["hard"], 100.0 * tot["hard"] / tot["bytes"]))
    print("bytes reached only on speculative paths: %d (%.1f%%)" % (tot["spec"], 100.0 * tot["spec"] / tot["bytes"]))
    print("bytes not reached: %d (%.1f%%)" % (tot["unreached"], 100.0 * tot["unreached"] / tot["bytes"]))
    print("  unreached runs by kind: " + ", ".join("%s %d runs / %d bytes" % (k, gap_kinds[k], gap_bytes[k])
                                                 for k in sorted(gap_kinds)))
    print("overlapping instructions (jump/call target not on an instruction boundary): %d" % tot["overlaps"])
    print("decode errors on hard paths: %d" % len(hard_err_list))
    for fn, bid, (_k, off, src, edge, msg) in hard_err_list:
        print("    %s %d $%04X (from $%04X via %s): %s" % (fn, bid, o2a(off), o2a(src) if src >= 0 else 0, edge, msg))
    print("decode errors on speculative paths only: %d" % len(spec_err_list))
    for fn, bid, (_k, off, src, edge, msg) in spec_err_list:
        print("    %s %d $%04X (from $%04X via %s): %s" % (fn, bid, o2a(off), o2a(src) if src >= 0 else 0, edge, msg))
    print("speculative paths stopped at a table (an operand addresses one of their bytes): %d"
          % len(spec_cut_list))
    if verbose:
        for fn, bid, (off, src, edge) in spec_cut_list:
            print("    %s %d $%04X (from $%04X via %s)" % (fn, bid, o2a(off), o2a(src), edge))
    print("reachable opcodes with no handler: %d" % len(unhandled_list))
    for fn, bid, (off, st) in unhandled_list:
        print("    %s %d $%04X (%s)" % (fn, bid, o2a(off), st))
    print("jump targets given as type-0 operand (target undefined): %d" % len(undefined_list))
    for fn, bid, (off, i) in undefined_list:
        print("    %s %d $%04X operand %d" % (fn, bid, o2a(off), i))
    print("IF followed by an opcode the IF-skip mis-sizes: %d  (%s)"
          % (len(hazard_list), ", ".join("%s %d" % (opname(k), v) for k, v in sorted(hazards_by_op.items()))))
    if verbose:
        for fn, bid, (ifo, nxt, op) in hazard_list:
            print("    %s %d IF $%04X -> %s $%04X" % (fn, bid, o2a(ifo), opname(op), o2a(nxt)))
    print("script-address operands that point at a decoded instruction (self-modifying / data in code): %d"
          % len(data_into_code))
    for fn, bid, ref, users in data_into_code[: (None if verbose else 10)]:
        print("    %s %d $%04X used by %s" % (fn, bid, o2a(ref),
                                              ", ".join("$%04X(%s)" % (o2a(u), r) for u, r in users[:4])))
    for (prefix, vec), n in sorted(headers.items()):
        print("header: prefix 0x%04X, vector (ignored byte, operand type) %s: %d blocks"
              % (prefix, "same for all five: %s" % (vec[0],) if len(set(vec)) == 1 else vec, n))
    print("inline strings: %d, %d packed bytes -> %d characters, %d empty, %d with characters outside $20-$5F"
          % (strings["count"], strings["packed bytes"], strings["chars"], strings["empty"], strings["odd chars"]))
    print("operand types: " + ", ".join("%02X:%d" % (k, v) for k, v in sorted(types.items())))
    print("opcode usage (decoded instructions, hard + speculative):")
    line = []
    for op in range(0x40):
        if usage[op] or op in OPS:
            line.append("%02X %-10s %5d" % (op, opname(op), usage[op]))
    for k in range(0, len(line), 4):
        print("    " + "   ".join(line[k:k + 4]))
    unused = [opname(op) for op in OPS if not usage[op]]
    print("opcodes never decoded: %s" % (", ".join(unused) or "none"))
    ok = not hard_err_list and not tot["overlaps"] and not unhandled_list
    print("RESULT: %s" % ("hard paths decode cleanly, no overlaps" if ok else "PROBLEMS (see above)"))
    return 0 if ok else 1


def main(argv):
    if len(argv) < 3:
        print(__doc__)
        return 2
    cmd, gamedir = argv[1], argv[2]
    if cmd == "list":
        cmd_list(gamedir)
        return 0
    if cmd == "dis" and len(argv) >= 5:
        bid = argv[4]
        cmd_dis(gamedir, argv[3], int(bid, 16) if bid.lower().startswith("0x") else int(bid), "--hex" in argv)
        return 0
    if cmd == "check":
        return cmd_check(gamedir, "-v" in argv)
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
