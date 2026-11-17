#!/usr/bin/env python3
"""Gold Box DOS 3D map decoder (GEO?.DAX, Pool of Radiance) - list / dump / ascii.

Usage (run with ``python3 -I``; inputs are untrusted data):
    geo.py list    GEO?.DAX [...]
    geo.py dump    GEO?.DAX [...] [--id N] [--json]
    geo.py ascii   GEO?.DAX [...] [--id N] [--cell hex|low6|none]
    geo.py refs    ECL?.DAX [...]          # heuristic: map / wall-set ids an area script loads
    geo.py special START.EXE --offset N [--count 9]   # wilderness special-location records

Spec: docs/re/geo-format.md.  Every PoR GEO block is 1026 bytes:

    u16   0x0400                    (plane data size; the engine skips these 2 bytes)
    plane 0 [16][16]  walls N/E     high nibble = N wall type, low nibble = E wall type
    plane 1 [16][16]  walls S/W     high nibble = S wall type, low nibble = W wall type
    plane 2 [16][16]  event byte    script-defined; bit 7 also picks the alternate sky colour
    plane 3 [16][16]  barriers      2 bits per side: bits 1-0 N, 3-2 E, 5-4 S, 7-6 W

Planes are row-major, index = y*16 + x; x grows east, y grows south (north = y-1).
Wall type 0 = no wall; 1..15 = wall set slot (t-1)//5 + 1, wall (t-1)%5 within that
slot's WALLDEF block.  Barrier (only used when the wall type is non-zero; the engine
treats a side without a wall as barrier 1): 0 impassable, 1 passable, 2 locked door,
3 barred/held door.  Each side belongs to its own square, so the two faces of one edge
can differ (one-way walls, different textures).

``dump --json`` prints a JSON list with one object per block:
    {"file", "set", "id", "header", "width": 16, "height": 16,
     "squares": [{"x", "y", "walls": [N,E,S,W], "barrier": [N,E,S,W], "event"}, ...]}
``squares`` is row-major (index y*16+x); values are exactly as stored.

``ascii`` draws each square as 3 columns x 2 rows (FAQ style).  An edge is drawn from
both faces: ' ' no wall on either face, '#' impassable from both faces, '+' passable
wall (door, archway, scenery), '=' locked door (barrier 2 on a face), '%' barred door
(barrier 3), '!' one-way (passable from one face only; the engine reads only the face of the square
the party stands on, so the other face never blocks).  The outer frame shows only
the inside face of each border square; the engine wraps most maps there.

Decoder written from our own reading of the data; layout cross-checked against
S. S. Lee's PoR FAQ 12.2.5, coab GeoBlock.cs and the talonius Pascal source (see the spec).
No game data is embedded here.
"""
import json
import os
import re
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dax  # noqa: E402

GEO_SIZE = 0x402
W = H = 16
DIRS = "NESW"


class GeoMap:
    """One decoded GEO block."""

    def __init__(self, data, id_=None, file=None):
        if len(data) != GEO_SIZE:
            raise ValueError("GEO block %s: %d bytes, expected %d" % (id_, len(data), GEO_SIZE))
        self.id = id_
        self.file = file
        self.header = struct.unpack_from("<H", data, 0)[0]
        self.planes = [data[2 + k * 256: 2 + (k + 1) * 256] for k in range(4)]

    @staticmethod
    def index(x, y):
        return (y % H) * W + (x % W)

    def walls(self, x, y):
        """Wall types [N, E, S, W] of square (x, y); coordinates wrap like the engine."""
        c = self.index(x, y)
        ne, sw = self.planes[0][c], self.planes[1][c]
        return [ne >> 4, ne & 15, sw >> 4, sw & 15]

    def barrier(self, x, y):
        """Stored barrier values [N, E, S, W] of square (x, y)."""
        b = self.planes[3][self.index(x, y)]
        return [b & 3, (b >> 2) & 3, (b >> 4) & 3, (b >> 6) & 3]

    def passage(self, x, y):
        """Effective barrier per side as the engine sees it: 1 where there is no wall."""
        return [b if w else 1 for w, b in zip(self.walls(x, y), self.barrier(x, y))]

    def can_step(self, x, y, facing):
        """Forward step test of the engine (GAME.OVR 0x17469 -> barrier accessor 0x2EC47): only the face of the
        party's own square is read, so the other side of the edge never matters (one-way walls).  facing is the
        side index 0 N, 1 E, 2 S, 3 W.  True = the party walks; 2/3 open the door menu instead."""
        return self.passage(x, y)[facing] == 1

    def one_way_edges(self):
        """Interior edges passable from exactly one side: (x, y, side) of the face that lets the party through."""
        out = []
        for y in range(H):
            for x in range(W):
                for k, (nx, ny, back) in ((1, (x + 1, y, 3)), (2, (x, y + 1, 0))):
                    if nx >= W or ny >= H:
                        continue
                    a, b = self.can_step(x, y, k), self.can_step(nx, ny, back)
                    if a != b and 0 in (self.passage(x, y)[k], self.passage(nx, ny)[back]):
                        out.append((x, y, k) if a else (nx, ny, back))
        return out

    def event(self, x, y):
        return self.planes[2][self.index(x, y)]

    def to_dict(self):
        sq = []
        for y in range(H):
            for x in range(W):
                sq.append({"x": x, "y": y, "walls": self.walls(x, y),
                           "barrier": self.barrier(x, y), "event": self.event(x, y)})
        m = re.search(r"(\d)\.DAX$", self.file or "", re.I)
        return {"file": os.path.basename(self.file) if self.file else None,
                "set": int(m.group(1)) if m else None, "id": self.id,
                "header": self.header, "width": W, "height": H, "squares": sq}


def wall_slot(t):
    """Wall type 1..15 -> (wall-set slot 1..3, wall 0..4 inside the slot's WALLDEF block)."""
    if not 1 <= t <= 15:
        raise ValueError("wall type %d" % t)
    return (t - 1) // 5 + 1, (t - 1) % 5


def load(path):
    d = dax.DaxFile(path)
    if d.variant != "dos":
        raise ValueError("%s: %s DAX; only DOS GEO blocks are covered" % (path, d.variant))
    return [GeoMap(blk, id_, path) for id_, blk in d.blocks()]


def edge_glyph(a, b):
    """Glyph for an edge from the (wall, stored barrier) pair of each face (b None = map border)."""
    def cls(face):
        w, s = face
        return None if w == 0 else s  # None = no wall (passable)
    if b is None:  # map border: only the inside face exists
        b = a
    ca, cb = cls(a), cls(b)
    if ca is None and cb is None:
        return " "
    if 3 in (ca, cb):
        return "%"
    if 2 in (ca, cb):
        return "="
    pa, pb = ca != 0, cb != 0
    if pa and pb:
        return "+"
    if not pa and not pb:
        return "#"
    return "!"


def render_ascii(g, cell="hex"):
    def face(x, y, k):
        return g.walls(x, y)[k], g.barrier(x, y)[k]

    def hedge(x, y):  # edge between (x, y-1) and (x, y); y in 0..16
        if y == 0:
            return edge_glyph(face(x, 0, 0), None)
        if y == H:
            return edge_glyph(face(x, H - 1, 2), None)
        return edge_glyph(face(x, y - 1, 2), face(x, y, 0))

    def vedge(x, y):  # edge between (x-1, y) and (x, y); x in 0..16
        if x == 0:
            return edge_glyph(face(0, y, 3), None)
        if x == W:
            return edge_glyph(face(W - 1, y, 1), None)
        return edge_glyph(face(x - 1, y, 1), face(x, y, 3))

    def corner(x, y):
        around = []
        if y > 0:
            around.append(vedge(x, y - 1))
        if y < H:
            around.append(vedge(x, y))
        if x > 0:
            around.append(hedge(x - 1, y))
        if x < W:
            around.append(hedge(x, y))
        return "#" if any(c != " " for c in around) else " "

    out = ["   " + "".join("%3d" % x for x in range(W))]
    for y in range(H + 1):
        line = "   " + "".join(corner(x, y) + hedge(x, y) * 2 for x in range(W)) + corner(W, y)
        out.append(line.rstrip())
        if y == H:
            break
        row = "%2d " % y
        for x in range(W):
            ev = g.event(x, y)
            if cell == "hex":
                txt = "%02X" % ev if ev else "  "
            elif cell == "low6":
                txt = "%2d" % (ev & 0x3F) if ev & 0x3F else "  "
            else:
                txt = "  "
            row += vedge(x, y) + txt
        row += vedge(W, y)
        out.append(row + " %2d" % y)
    out.append(out[0])
    return "\n".join(out)


def scan_ecl_refs(blk):
    """Heuristic byte scan of an ECL block for LOAD FILES (0x21) / LOAD PIECES (0x37)
    with three immediate (type 0) operands.  Not a disassembly: misses computed
    operands and can false-match data."""
    found = []
    for op, name in ((0x21, "LOAD_FILES"), (0x37, "LOAD_PIECES")):
        for m in re.finditer(bytes([op]) + b"\x00(.)\x00(.)\x00(.)", blk, re.S):
            found.append({"op": name, "offset": m.start(),
                          "args": [m.group(1)[0], m.group(2)[0], m.group(3)[0]]})
    return sorted(found, key=lambda f: f["offset"])


def _opt(args, name, default=None, conv=str):
    if name in args:
        i = args.index(name)
        if i + 1 >= len(args):
            raise SystemExit("%s needs a value" % name)
        v = conv(args[i + 1])
        del args[i:i + 2]
        return v
    return default


def main(argv):
    if len(argv) < 3 or argv[1] not in ("list", "dump", "ascii", "refs", "special"):
        print(__doc__.split("\n\n")[1])
        return 2
    cmd, args = argv[1], argv[2:]
    want = _opt(args, "--id", None, int)
    cell = _opt(args, "--cell", "hex")
    offset = _opt(args, "--offset", None, lambda s: int(s, 0))
    count = _opt(args, "--count", 9, int)
    as_json = "--json" in args
    files = [a for a in args if not a.startswith("--")]
    if not files:
        print(__doc__.split("\n\n")[1])
        return 2

    if cmd == "special":
        if offset is None:
            print("special needs --offset (file offset of the table; see docs/re/geo-format.md)")
            return 2
        with open(files[0], "rb") as f:
            data = f.read()
        recs = [dict(zip(("x", "y", "tile"), struct.unpack_from("<3H", data, offset + 6 * i)))
                for i in range(count)]
        if as_json:
            print(json.dumps(recs))
        else:
            for i, r in enumerate(recs, 1):
                print("%2d  x %3d  y %3d  tile %3d" % (i, r["x"], r["y"], r["tile"]))
        return 0

    if cmd == "refs":
        res = []
        for p in files:
            for id_, blk in dax.DaxFile(p).blocks():
                res.append({"file": os.path.basename(p), "id": id_, "refs": scan_ecl_refs(blk)})
        if as_json:
            print(json.dumps(res, indent=1))
        else:
            for r in res:
                print("%s %3d  %s" % (r["file"], r["id"], "  ".join(
                    "%s(%s)" % (f["op"], ",".join(map(str, f["args"]))) for f in r["refs"])))
        return 0

    maps = []
    for p in files:
        maps += [g for g in load(p) if want is None or g.id == want]
    if want is not None and not maps:
        print("no block %d" % want)
        return 1

    if cmd == "list":
        for g in maps:
            sides = [(w, b) for y in range(H) for x in range(W)
                     for w, b in zip(g.walls(x, y), g.barrier(x, y))]
            nwall = sum(1 for w, _ in sides if w)
            doors = sum(1 for w, b in sides if w and b >= 2)
            evs = sum(1 for c in range(256) if g.planes[2][c] & 0x7F)
            print("%s id %3d  header %04X  wall sides %4d  locked/barred sides %3d  one-way edges %3d  event squares %3d"
                  % (os.path.basename(g.file), g.id, g.header, nwall, doors, len(g.one_way_edges()), evs))
        return 0
    if cmd == "dump":
        if as_json:
            print(json.dumps([g.to_dict() for g in maps]))
        else:
            for g in maps:
                print("%s id %d" % (os.path.basename(g.file), g.id))
                for y in range(H):
                    for x in range(W):
                        print("  (%2d,%2d) walls %s barrier %s event %02X" % (
                            x, y, "".join("%X" % w for w in g.walls(x, y)),
                            "".join(str(b) for b in g.barrier(x, y)), g.event(x, y)))
        return 0
    for g in maps:
        print("%s id %d" % (os.path.basename(g.file), g.id))
        print(render_ascii(g, cell))
        print()
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
