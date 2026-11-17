#!/usr/bin/env python3
"""Gold Box (Pool of Radiance DOS) item and effect records -> JSON / text.

Usage (run with ``python3 -I``; inputs are untrusted data):
    gbitem.py items     FILE  [--exe START.EXE|GAME.OVR|DIR] [--items ITEMS] [--json]
    gbitem.py effects   FILE  [--json]
    gbitem.py basetable       [--exe ...] [--items ITEMS] [--json]
    gbitem.py random          [--exe ...] [--json]
    gbitem.py words           [--exe ...] [--json]

FILE for ``items``: a character item file (*.ITM), a DAX of item records
(MON?ITM.DAX, ITEM?.DAX) or the base item file ITEMS (then it acts like
``basetable``).  FILE for ``effects``: *.SPC or MON?SPC.DAX.

Item names, spell names and the random-item array are read at run time from
the player's own program files (START.EXE + GAME.OVR); the base item table is
read from the ITEMS data file.  Nothing from the game is embedded here: only
record layouts and the byte patterns used to find the tables in the program.
``--exe`` may name START.EXE, GAME.OVR or the game directory; the other files
are looked up next to it (case-insensitive).  Without --exe, the directory of
FILE is tried.

Format spec: docs/re/item-format.md.  Verified on PoR DOS v1.3
(START.EXE sha1 0640ba4f..., GAME.OVR sha1 31c12ed0...).
"""
import json
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dax  # noqa: E402

ITEM_SIZE = 63
EFFECT_SIZE = 9
BASE_SIZE = 16
RANDOM_REC = 16          # 8 little-endian words per random-item entry


# --------------------------------------------------------------------------
# EXEPACK (Microsoft) unpacker.  Documented scheme; our own implementation.
#   The packed program's CS:0000 holds the EXEPACK header (16 or 18 bytes):
#     real_IP, real_CS, mem_start, exepack_size, real_SP, real_SS,
#     dest_len (paragraphs), [skip_len], 'RB'
#   The packed image (load module up to CS, minus (skip_len-1) paragraphs)
#   is expanded backwards from its end; trailing 0xFF bytes are padding.
#   Commands, read backwards: opcode byte, u16 length (big end first when read
#   backwards = little-endian in file), then for 0xB0 one fill byte (repeat it
#   length times) or for 0xB2 length literal bytes.  Opcode bit 0 = last.
#   Relocations are not needed here (we only read data at fixed offsets).
# --------------------------------------------------------------------------
def mz_load_module(raw):
    if raw[:2] not in (b"MZ", b"ZM"):
        raise ValueError("not an MZ executable")
    (cblp, cp, _crlc, cparhdr, _mina, _maxa, _ss, _sp, _cs_, ip, cs) = struct.unpack_from("<11H", raw, 2)
    size = (cp - 1) * 512 + cblp if cblp else cp * 512
    return raw[cparhdr * 16:size], cs, ip


def unexepack(raw):
    """Return (image, entry_cs, entry_ip, packed).  image = memory image at load segment 0."""
    lm, cs, ip = mz_load_module(raw)
    eh = cs * 16
    if len(lm) < eh + 18:
        return lm, cs, ip, False
    if lm[eh + 16:eh + 18] == b"RB":
        rip, rcs, _mem, _esz, _rsp, _rss, dlen, skip = struct.unpack_from("<8H", lm, eh)
    elif lm[eh + 14:eh + 16] == b"RB":
        rip, rcs, _mem, _esz, _rsp, _rss, dlen = struct.unpack_from("<7H", lm, eh)
        skip = 1
    else:
        return lm, cs, ip, False
    src_end = eh - (skip - 1) * 16
    buf = bytearray(max(dlen * 16, src_end))
    buf[:src_end] = lm[:src_end]
    s = src_end - 1
    pad = 0
    while buf[s] == 0xFF and pad < 16:
        s -= 1
        pad += 1
    d = dlen * 16 - 1
    while True:
        op = buf[s]
        length = buf[s - 1] << 8 | buf[s - 2]
        s -= 3
        if op & 0xFE == 0xB0:
            fill = buf[s]
            s -= 1
            buf[d - length + 1:d + 1] = bytes([fill]) * length
            d -= length
        elif op & 0xFE == 0xB2:
            buf[d - length + 1:d + 1] = buf[s - length + 1:s + 1]
            d -= length
            s -= length
        else:
            raise ValueError("EXEPACK: bad opcode %02X" % op)
        if s < -1 or d < -1:
            raise ValueError("EXEPACK: stream overrun")
        if op & 1:
            break
    return bytes(buf[:dlen * 16]), rcs, rip, True


# --------------------------------------------------------------------------
# Locating tables in the program (v1.3; patterns are code, not game data)
# --------------------------------------------------------------------------
def _find_all(hay, needle):
    out = []
    i = hay.find(needle)
    while i >= 0:
        out.append(i)
        i = hay.find(needle, i + 1)
    return out


def _imm_after(hay, prefix):
    """All u16 immediates that directly follow `prefix` in `hay`, most common first."""
    counts = {}
    for i in _find_all(hay, prefix):
        if i + len(prefix) + 2 <= len(hay):
            v = struct.unpack_from("<H", hay, i + len(prefix))[0]
            counts[v] = counts.get(v, 0) + 1
    return sorted(counts, key=lambda v: -counts[v])


def _stride_index_pattern(stride):
    # mov dx,stride / mul dx / mov di,ax / add di,imm16   (Turbo Pascal array[i] of string)
    return b"\xBA" + struct.pack("<H", stride) + b"\xF7\xE2\x8B\xF8\x81\xC7"


def _pstr(buf, off, maxlen):
    if off < 0 or off >= len(buf):
        return None
    n = buf[off]
    if n > maxlen or off + 1 + n > len(buf):
        return None
    b = buf[off + 1:off + 1 + n]
    if any(c < 0x20 or c > 0x7E for c in b):
        return None
    return b.decode("latin-1")


def _ci_find(directory, name):
    try:
        for f in os.listdir(directory):
            if f.lower() == name.lower():
                return os.path.join(directory, f)
    except OSError:
        pass
    return None


class Program:
    """Tables read from START.EXE (data segment) and GAME.OVR (code that indexes them)."""

    def __init__(self, exe=None, items=None, near=None):
        self.notes = []
        self.start_path = self.ovr_path = None
        self.items_path = items
        d = None
        if exe:
            if os.path.isdir(exe):
                d = exe
            else:
                d = os.path.dirname(os.path.abspath(exe))
                if os.path.basename(exe).lower().endswith(".ovr"):
                    self.ovr_path = exe
                else:
                    self.start_path = exe
        elif near:
            d = near
        if d:
            self.start_path = self.start_path or _ci_find(d, "START.EXE") or _ci_find(d, "ST.EXE")
            self.ovr_path = self.ovr_path or _ci_find(d, "GAME.OVR")
            self.items_path = self.items_path or _ci_find(d, "ITEMS")
        self.image = self.ovr = None
        self.ds = None
        self.words_base = self.spells_base = self.random_base = None
        self.spell_count = 0
        if self.start_path:
            with open(self.start_path, "rb") as f:
                raw = f.read()
            self.image, ecs, eip, packed = unexepack(raw)
            self.packed = packed
            self.ds = self._find_ds(ecs, eip)
            if self.ds is None:
                self.notes.append("data segment not found in %s" % self.start_path)
        if self.ovr_path:
            with open(self.ovr_path, "rb") as f:
                self.ovr = f.read()
        if self.ds is not None:
            self._find_tables()
        self.base = self._load_base()

    # Turbo Pascal: the entry point first far-calls System init, which starts with
    # mov dx,DATA / mov ds,dx  (BA lo hi 8E DA).
    def _find_ds(self, ecs, eip):
        img = self.image
        e = ecs * 16 + eip
        if img[e:e + 1] == b"\x9A":
            off, seg = struct.unpack_from("<HH", img, e + 1)
            t = seg * 16 + off
            for k in range(t, t + 16):
                if img[k] == 0xBA and img[k + 3:k + 5] == b"\x8E\xDA":
                    return struct.unpack_from("<H", img, k + 1)[0] * 16
        return None

    def _ds_valid_table(self, base, stride, first, count, need):
        ok = 0
        for k in range(first, first + count):
            s = _pstr(self.image, self.ds + base + k * stride, stride - 1)
            if s is not None:
                ok += 1
        return ok >= need

    def _find_tables(self):
        code = self.image + (self.ovr or b"")
        # Item name words: array[1..255] of string[20] (21 bytes), indexed DS:base + w*21.
        for base in _imm_after(code, _stride_index_pattern(21)):
            if self._ds_valid_table(base, 21, 1, 255, 250):
                self.words_base = base
                break
        if self.words_base is None:
            self.notes.append("item name word table not found")
        # Spell names: array[1..] of string[40] (41 bytes), indexed DS:base + id*41.
        for base in _imm_after(code, _stride_index_pattern(41)):
            if self._ds_valid_table(base, 41, 1, 56, 56):
                self.spells_base = base
                n = 0
                while n < 127 and _pstr(self.image, self.ds + base + (n + 1) * 41, 40):
                    n += 1
                self.spell_count = n       # 56 in v1.3; followed by non-string data
                break
        # Random item array (GAME.OVR generator):
        #   mov di,ax / shl di,1 / mov dl,[di+base]   (base + 2*(magic + field))
        if self.ovr:
            c = _imm_after(self.ovr, b"\x8B\xF8\xD1\xE7\x8A\x95")
            if c:
                self.random_base = c[0]
        if self.random_base is None:
            self.notes.append("random item array not found (needs GAME.OVR)")

    def _load_base(self):
        if not self.items_path:
            self.notes.append("ITEMS file not found")
            return None
        with open(self.items_path, "rb") as f:
            b = f.read()
        # The program seeks to offset 2 and block-reads the rest (2 header bytes unused).
        return [b[2 + t * BASE_SIZE:2 + (t + 1) * BASE_SIZE] for t in range((len(b) - 2) // BASE_SIZE)]

    def word(self, w):
        if not w or self.words_base is None:
            return None
        return _pstr(self.image, self.ds + self.words_base + w * 21, 20)

    def spell(self, sid):
        if not sid or self.spells_base is None or sid > self.spell_count:
            return None
        return _pstr(self.image, self.ds + self.spells_base + sid * 41, 40)

    def random_entries(self, count=19):
        if self.random_base is None or self.ds is None:
            return []
        out = []
        for k in range(count):
            magic = 1 + 8 * k
            o = self.ds + self.random_base + magic * 2
            w = struct.unpack_from("<8H", self.image, o)
            out.append({"magic": magic, "ds_offset": self.random_base + magic * 2,
                        "word_2f": w[0], "word_30": w[1], "word_31": w[2],
                        "weight": w[3], "value": w[4], "props": [w[5], w[6], w[7]],
                        "name": (compose_name(self, (w[0], w[1], w[2]), 0, 0, 0) or "").rstrip()})
        return out

    def info(self):
        return {"start_exe": self.start_path, "game_ovr": self.ovr_path, "items_file": self.items_path,
                "ds_image_offset": self.ds, "words_ds": self.words_base, "spells_ds": self.spells_base, "spell_count": self.spell_count,
                "random_ds": self.random_base, "notes": self.notes}


# --------------------------------------------------------------------------
# Name composition (reimplementation of the program's item-name routine)
# --------------------------------------------------------------------------
def compose_name(prog, words, hidden, count, itype, detect=False, magic=False):
    """words = (byte 2F, byte 30, byte 31).  Returns the name the game shows.

    Word slots k = 1..3 are bytes 2F, 30, 31; slot k is visible if non-zero and bit (3-k)
    of `hidden` is clear.  Words are written k = 3, 2, 1 (byte 31 first), each followed by
    a space; with count >= 2 one word gets an 's' instead (see item-format.md)."""
    if prog is None or prog.words_base is None:
        return None
    parts = []
    if detect and magic:
        parts.append("* ")
    if count > 0:
        parts.append("%d " % count)
    vis = 0
    for k in (1, 2, 3):
        if words[k - 1] and not (hidden >> (3 - k)) & 1:
            vis |= 1 << (k - 1)
    plural_done = False
    for k in (3, 2, 1):
        if not (vis >> (k - 1)) & 1:
            continue
        w = words[k - 1]
        txt = prog.word(w)
        parts.append(txt if txt is not None else "#%d" % w)
        plural = False
        if count >= 2 and not plural_done:
            if (1 << (k - 1)) == vis:
                plural = True
            elif k == 1 and vis > 4 and itype != 0x56:
                plural = True
            elif k == 2 and not vis & 1:
                plural = True
            elif k == 3 and itype == 0x56:
                plural = True
            elif itype in (0x49, 0x1C) and words[2] != 0xB1:
                plural = True
        if plural:
            parts.append("s ")
            plural_done = True
        else:
            parts.append(" ")
    s = "".join(parts)
    return s[:40]          # stored in a string[40]


# --------------------------------------------------------------------------
# Records
# --------------------------------------------------------------------------
# Labels follow Lee 12.2.4.2 (class bits also checked against the program's per-class
# bit array); slots 3-8/10 and the flag bits are Lee only.  See item-format.md.
SLOT = {0: "main_hand", 1: "off_hand", 2: "body_armor", 3: "gauntlets", 4: "helm", 5: "belt_or_necklace",
        6: "robe", 7: "cloak", 8: "boots", 9: "ring", 10: "misc", 11: "mu_scroll", 12: "cleric_scroll"}
CLASS_BITS = ["magic_user", "cleric", "thief", "fighter", "druid", "monk", "paladin", "ranger"]
WFLAG_BITS = {0: "shoots_arrows", 1: "dex_missile_bonus", 2: "str_bonus", 3: "ai_ranged", 4: "thrown",
              7: "shoots_bolts"}
SCROLL_SLOTS = (11, 12)


def _bits(v, names):
    if isinstance(names, dict):
        return [names.get(b, "bit%d" % b) for b in range(8) if v >> b & 1]
    return [names[b] for b in range(8) if v >> b & 1]


def decode_base(t, e):
    sb = lambda x: x - 256 if x > 127 else x  # noqa: E731
    ac = e[6]
    if ac >= 178:
        ac_d = {"kind": "armor", "bonus": ac - 178}
    elif ac >= 128:
        ac_d = {"kind": "other", "bonus": ac - 128}
    else:
        ac_d = None
    return {"type": t, "raw": e.hex(), "slot": e[0], "slot_name": SLOT.get(e[0]), "hands": e[1],
            "damage_large": [e[2], e[3], sb(e[4])], "rate_of_fire_x2": e[5], "ac_code": ac, "ac": ac_d,
            "weapon_kind": e[7], "byte8": e[8], "damage_small_medium": [e[9], e[10], sb(e[11])],
            "range": e[12], "usable_mask": e[13], "usable_by": _bits(e[13], CLASS_BITS),
            "weapon_flags": e[14], "weapon_flag_names": _bits(e[14], WFLAG_BITS), "byte15": e[15]}


def decode_item(r, prog=None, index=None):
    sb = lambda x: x - 256 if x > 127 else x  # noqa: E731
    words = (r[0x2F], r[0x30], r[0x31])
    itype, hidden, count = r[0x2E], r[0x35], r[0x39]
    plus, plus2, cursed = sb(r[0x32]), sb(r[0x33]), r[0x36]
    props = list(r[0x3C:0x3F])
    stored = _pstr(r, 0, 40)
    d = {"index": index, "type": itype, "words": {"2f": words[0], "30": words[1], "31": words[2]},
         "plus": plus, "save_bonus": plus2, "equipped": r[0x34], "hidden_mask": hidden, "cursed": cursed,
         "weight": struct.unpack_from("<H", r, 0x37)[0], "count": count,
         "value": struct.unpack_from("<H", r, 0x3A)[0], "props": props,
         "stored_name": stored, "byte29": r[0x29], "next_ptr": r[0x2A:0x2E].hex()}
    if prog is not None:
        d["name"] = compose_name(prog, words, hidden, count, itype)
        d["name_identified"] = compose_name(prog, words, 0, count, itype)
        d["detect_magic_marker"] = plus > 0 or plus2 > 0 or cursed != 0
        d["word_text"] = {k: prog.word(w) for k, w in d["words"].items() if w}
        base = prog.base[itype] if prog.base and itype < len(prog.base) else None
        if base is not None:
            d["base"] = decode_base(itype, base)
            if base[0] in SCROLL_SLOTS:
                d["scroll_spells"] = [{"spell": p & 0x7F, "scribe_flag": bool(p & 0x80),
                                       "name": prog.spell(p & 0x7F)} for p in props if p]
        if "scroll_spells" not in d:
            d["props_hint"] = props_hint(props, prog)
    if d.get("name") is not None:
        d["name_raw"] = d["name"]                  # exactly as the game stores it (trailing space)
        d["name"] = d["name"].rstrip()
        d["name_identified"] = d["name_identified"].rstrip()
    return d


# Handlers of worn items (props 3E >= 0x80), item-format.md "props".
WORN_ADD_EFFECT = {128, 129, 130, 133, 134, 136, 138, 139}
WORN_HANDLERS = {131: "strength_18_00", 132: "alignment_check", 135: "needs_strength_19", 137: "remove_effect_23"}


def props_hint(props, prog=None):
    """What the program does with props 3C-3E of a non-scroll item (item-format.md "props"): byte 3E decides.

    - 3E < 0x80 and 3D > 0: usable; Use casts power 3D (spell table entry 3D if <= 56, else 3D - 23);
      3C = charges (0 = unlimited; with count > 1 the stack is used up first).
    - 3E >= 0x80: worn; readying runs handler 3E (apply), unreadying runs it with remove.
    - otherwise: no use, no handler.
    """
    c, power, h = props
    if h >= 0x80:
        p = {"kind": "worn", "handler": h}
        if h in WORN_ADD_EFFECT:
            p["action"] = "add_effect"
            p["effect_id"] = power
        elif h in WORN_HANDLERS:
            p["action"] = WORN_HANDLERS[h]
            if h == 132:
                p["alignment"] = power & 0x0F
                p["damage"] = power >> 4
        else:
            p["action"] = "unknown"          # handler codes above 139 are not in the table
        return p
    if power > 0:
        spell_id = power if power <= 56 else power - 23
        p = {"kind": "usable", "charges": c, "unlimited": c == 0, "power": power, "power_spell_id": spell_id}
        if prog is not None and 1 <= spell_id <= prog.spell_count:
            p["power_spell_name"] = prog.spell(spell_id)
        return p
    return {"kind": "none"}


def decode_effect(r, index=None):
    return {"index": index, "id": r[0], "minutes": struct.unpack_from("<H", r, 1)[0], "param": r[3],
            "param_lo": r[3] & 15, "param_hi": r[3] >> 4, "remove_calls_handler": r[4],
            "next_ptr": r[5:9].hex()}


def _blocks(path):
    """[(block_id or None, bytes)] for a DAX or a plain record file."""
    try:
        dx = dax.DaxFile(path)
        if dx.variant == "dos":
            return [(i, b) for i, b in dx.blocks()]
    except ValueError:
        pass
    with open(path, "rb") as f:
        return [(None, f.read())]


def _records(path, size, decode):
    out = []
    for bid, b in _blocks(path):
        recs = [decode(b[k * size:(k + 1) * size], k) for k in range(len(b) // size)]
        out.append({"block": bid, "bytes": len(b), "trailing_bytes": len(b) % size, "records": recs})
    return out


def _is_items_file(path):
    return os.path.basename(path).lower() == "items"


# --------------------------------------------------------------------------
# CLI
# --------------------------------------------------------------------------
def _opt(argv, name):
    if name in argv:
        i = argv.index(name)
        if i + 1 < len(argv):
            return argv[i + 1]
    return None


def _text_items(blocks):
    for blk in blocks:
        print("block %s (%d bytes)" % (blk["block"], blk["bytes"]))
        for it in blk["records"]:
            print("  %2d type %3d  %-36s  [%s]  +%d/%d w%d v%d props %s" % (
                it["index"], it["type"], it.get("name") or "", it.get("name_identified") or "",
                it["plus"], it["save_bonus"], it["weight"], it["value"], it["props"]))


def main(argv):
    if len(argv) < 2 or argv[1] not in ("items", "effects", "basetable", "random", "words"):
        print(__doc__)
        return 2
    cmd = argv[1]
    as_json = "--json" in argv
    pos = [a for i, a in enumerate(argv[2:], 2) if not a.startswith("--") and argv[i - 1] not in ("--exe", "--items")]
    f = pos[0] if pos else None
    exe, items = _opt(argv, "--exe"), _opt(argv, "--items")

    if cmd == "effects":
        if not f:
            print(__doc__)
            return 2
        res = {"file": f, "blocks": _records(f, EFFECT_SIZE, decode_effect)}
        if as_json:
            print(json.dumps(res, indent=1))
        else:
            for blk in res["blocks"]:
                print("block %s" % blk["block"])
                for e in blk["records"]:
                    print("  %(index)2d id %(id)3d  %(minutes)5d min  param %(param)3d  handler %(remove_calls_handler)d" % e)
        return 0

    near = os.path.dirname(os.path.abspath(f)) if f else os.getcwd()
    if cmd == "items" and f and _is_items_file(f):
        cmd, items = "basetable", f
    prog = Program(exe=exe, items=items, near=None if exe else near)

    if cmd == "basetable":
        if prog.base is None:
            print("ITEMS file not found; pass --items or --exe", file=sys.stderr)
            return 1
        res = {"program": prog.info(), "types": [decode_base(t, e) for t, e in enumerate(prog.base)]}
        if as_json:
            print(json.dumps(res, indent=1))
        else:
            for b in res["types"]:
                print("%3d slot %2d hands %d  L %dd%d%+d  SM %dd%d%+d  rof %d  ac %3d  range %2d  use %02x  flags %02x" % (
                    b["type"], b["slot"], b["hands"], *b["damage_large"], *b["damage_small_medium"],
                    b["rate_of_fire_x2"], b["ac_code"], b["range"], b["usable_mask"], b["weapon_flags"]))
        return 0
    if cmd == "random":
        res = {"program": prog.info(), "entries": prog.random_entries()}
        if as_json:
            print(json.dumps(res, indent=1))
        else:
            for e in res["entries"]:
                print("%3d  %-32s w%-4d v%-6d props %s" % (e["magic"], (e["name"] or "").rstrip(), e["weight"],
                                                         e["value"], e["props"]))
        return 0
    if cmd == "words":
        res = {"program": prog.info(), "words": {w: prog.word(w) for w in range(1, 256)},
               "spells": {s: prog.spell(s) for s in range(1, 128) if prog.spell(s) is not None}}
        if as_json:
            print(json.dumps(res, indent=1))
        else:
            for w, t in res["words"].items():
                print("%3d %s" % (w, t))
        return 0
    if not f:
        print(__doc__)
        return 2
    res = {"file": f, "program": prog.info(),
           "blocks": _records(f, ITEM_SIZE, lambda r, k: decode_item(r, prog, k))}
    if as_json:
        print(json.dumps(res, indent=1))
    else:
        _text_items(res["blocks"])
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
