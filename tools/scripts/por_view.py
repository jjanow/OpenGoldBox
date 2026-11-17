#!/usr/bin/env python3
"""Pool of Radiance (DOS) data viewer: one static HTML page from the player's own game files.

Usage (run with ``python3 -I``; inputs are untrusted data):
    por_view.py GAMEDIR OUT.html

Runs the phase-1 decoders on GAMEDIR and renders what they return:
    Maps      every GEO block (geo.py dump): walls, doors by barrier type, event squares, hover for raw values
    Monsters  every MON?CHA record (gbchar.py monsters) with its carried items (gbitem.py items MON?ITM.DAX)
    Treasure  every ITEM?.DAX block (gbitem.py items)
    Items     the base item table (gbitem.py basetable)
    Saves     every SAVGAM?.DAT (gbchar.py savegame) with each party member's record and items

The page holds the game's own text and numbers, so write it to a private place (the private repo's
reverse-engineering/ tree), never to the public tree. Nothing from the game is embedded in this script.
Format specs: docs/re/geo-format.md, character-format.md, save-format.md, item-format.md.
"""
import html
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))


def run(tool, *args):
    """Run a decoder with --json and return its parsed output (None if it failed)."""
    cmd = [sys.executable, "-I", os.path.join(HERE, tool), *args, "--json"]
    p = subprocess.run(cmd, capture_output=True, text=True)
    if p.returncode != 0:
        sys.stderr.write(f"warning: {' '.join(cmd[2:])} failed: {p.stderr.strip().splitlines()[-1:]}\n")
        return None
    return json.loads(p.stdout)


def files(gamedir, prefix, suffix):
    out = []
    for name in sorted(os.listdir(gamedir)):
        u = name.upper()
        if u.startswith(prefix) and u.endswith(suffix):
            out.append(os.path.join(gamedir, name))
    return out


def esc(x):
    return html.escape(str(x), quote=True)


# ---------------------------------------------------------------- maps

CELL = 22
WALL_COLOURS = {0: "#c33", 1: "#888", 2: "#d90", 3: "#a3d"}  # barrier value -> colour when a wall is present


def map_svg(block):
    sq = {(s["x"], s["y"]): s for s in block["squares"]}
    w, h = block["width"], block["height"]
    pad = 14
    parts = [f'<svg class="map" viewBox="0 0 {w * CELL + pad * 2} {h * CELL + pad * 2}" '
             f'width="{w * CELL + pad * 2}" height="{h * CELL + pad * 2}" role="img">']
    parts.append(f'<g transform="translate({pad},{pad})">')
    for i in range(w):
        parts.append(f'<text class="ax" x="{i * CELL + CELL / 2}" y="-4">{i}</text>')
    for j in range(h):
        parts.append(f'<text class="ax" x="-4" y="{j * CELL + CELL / 2 + 3}" text-anchor="end">{j}</text>')
    for (x, y), s in sq.items():
        x0, y0 = x * CELL, y * CELL
        ev = s["event"]
        low = ev & 0x3F
        tip = (f'({x},{y}) event 0x{ev:02X} walls N{s["walls"][0]} E{s["walls"][1]} S{s["walls"][2]} '
               f'W{s["walls"][3]} barrier {s["barrier"]}')
        cls = "sq ev" if low else "sq"
        parts.append(f'<rect class="{cls}" x="{x0}" y="{y0}" width="{CELL}" height="{CELL}"><title>{esc(tip)}</title></rect>')
        if low:
            parts.append(f'<text class="evn" x="{x0 + CELL / 2}" y="{y0 + CELL / 2 + 3}">{low}</text>')
        edges = [(x0, y0, x0 + CELL, y0), (x0 + CELL, y0, x0 + CELL, y0 + CELL),
                 (x0, y0 + CELL, x0 + CELL, y0 + CELL), (x0, y0, x0, y0 + CELL)]
        for side in range(4):
            if s["walls"][side]:
                b = s["barrier"][side]
                col = WALL_COLOURS.get(b, "#000")
                x1, y1, x2, y2 = edges[side]
                # draw each square's own side slightly inside it, so one-sided walls show
                dx, dy = [(0, 2), (-2, 0), (0, -2), (2, 0)][side]
                parts.append(f'<line x1="{x1 + dx}" y1="{y1 + dy}" x2="{x2 + dx}" y2="{y2 + dy}" '
                             f'stroke="{col}" stroke-width="3"/>')
    parts.append("</g></svg>")
    return "".join(parts)


def section_maps(gamedir):
    out = ['<p class="note">Each square draws its own four sides just inside its edge (so one-sided walls show). '
           'Colour = barrier: <b style="color:#c33">impassable</b>, <b style="color:#888">passable wall</b>, '
           '<b style="color:#d90">locked door</b>, <b style="color:#a3d">harder door</b>. '
           'Number = event (low 6 bits). Hover a square for raw values.</p>']
    for f in files(gamedir, "GEO", ".DAX"):
        blocks = run("geo.py", "dump", f) or []
        for b in blocks:
            out.append(f'<details><summary>{esc(b["file"])} block {b["id"]}</summary>{map_svg(b)}</details>')
    return "\n".join(out)


# ---------------------------------------------------------------- items

def item_rows(records):
    rows = []
    for r in records:
        base = r.get("base") or {}
        dmg = base.get("damage_small_medium") or []
        dmg_s = f"{dmg[0]}d{dmg[1]}{'+' + str(dmg[2]) if len(dmg) > 2 and dmg[2] else ''}" if dmg and dmg[0] else ""
        rows.append("<tr>" + "".join(f"<td>{esc(v)}</td>" for v in (
            r.get("name", ""), r.get("name_identified", ""), r.get("type"), r.get("plus"), r.get("weight"),
            r.get("value"), r.get("count"), "yes" if r.get("cursed") else "", base.get("slot_name", ""), dmg_s,
        )) + "</tr>")
    return rows


ITEM_HEAD = ("<tr><th>Name (as shown)</th><th>Identified name</th><th>Type</th><th>+</th><th>Weight</th>"
             "<th>Value</th><th>Count</th><th>Cursed</th><th>Slot</th><th>Damage S/M</th></tr>")


def item_table(records):
    if not records:
        return '<p class="note">no items</p>'
    return f'<table class="t">{ITEM_HEAD}{"".join(item_rows(records))}</table>'


def section_treasure(gamedir):
    out = []
    for f in files(gamedir, "ITEM", ".DAX"):
        d = run("gbitem.py", "items", f, "--exe", gamedir)
        if not d:
            continue
        for b in d["blocks"]:
            out.append(f'<details><summary>{esc(os.path.basename(f))} block {b["block"]} '
                       f'({len(b["records"])} items)</summary>{item_table(b["records"])}</details>')
    return "\n".join(out)


def section_basetable(gamedir):
    d = run("gbitem.py", "basetable", "--exe", gamedir)
    if not d:
        return "<p>failed</p>"
    rows = []
    for t in d["types"]:
        if not int(t.get("raw") or "0", 16):
            continue
        dmg = t.get("damage_small_medium") or [0, 0, 0]
        dl = t.get("damage_large") or [0, 0, 0]
        rows.append("<tr>" + "".join(f"<td>{esc(v)}</td>" for v in (
            t["type"], t.get("slot_name", ""), t.get("hands"),
            f"{dmg[0]}d{dmg[1]}+{dmg[2]}" if dmg[0] else "", f"{dl[0]}d{dl[1]}+{dl[2]}" if dl[0] else "",
            t.get("rate_of_fire_x2"), t.get("range"), t.get("ac") if t.get("ac") is not None else "",
            ", ".join(t.get("usable_by") or []),
        )) + "</tr>")
    head = ("<tr><th>Type</th><th>Slot</th><th>Hands</th><th>Dmg S/M</th><th>Dmg L</th>"
            "<th>RoF×2</th><th>Range</th><th>AC</th><th>Usable by</th></tr>")
    return f'<table class="t">{head}{"".join(rows)}</table>'


# ---------------------------------------------------------------- creatures

def creature_card(rec, items=None, title=None):
    a = rec["abilities"]
    st = f'{a["str"]}' + (f'/{a["str_exc"]:02d}' if a.get("str_exc") else "")
    lv = ", ".join(f"{k} {v}" for k, v in rec["levels"].items() if v)
    sv = rec["saves"]
    att = rec["attacks"]["primary"]
    money = rec.get("money") or {}
    money_s = ", ".join(f"{k} {v}" for k, v in money.items() if v) if isinstance(money, dict) else esc(money)
    head = title or rec["name"]
    body = (
        f'<div class="kv"><span>Race/class</span>{esc(rec["race"]["name"])} / {esc(rec["class"]["name"])}'
        f' ({esc(rec["alignment"]["name"])})</div>'
        f'<div class="kv"><span>Levels</span>{esc(lv or "-")} (XP {rec["xp"]})</div>'
        f'<div class="kv"><span>Abilities</span>STR {st} INT {a["int"]} WIS {a["wis"]} DEX {a["dex"]} '
        f'CON {a["con"]} CHA {a["cha"]}</div>'
        f'<div class="kv"><span>HP</span>{rec["hp"]["current"]}/{rec["hp"]["max"]} (rolled {rec["hp"]["rolled"]})</div>'
        f'<div class="kv"><span>THAC0 / AC</span>{rec["thac0_base"]} / {rec["ac_base"]} (base), '
        f'move {rec["movement_base"]}</div>'
        f'<div class="kv"><span>Attack</span>{att["attacks_x2"] / 2:g}× {att["dice"]}d{att["sides"]}'
        f'{"+" + str(att["bonus"]) if att["bonus"] else ""}</div>'
        f'<div class="kv"><span>Saves</span>PPD {sv["paralyzation_poison_death"]} PP {sv["petrification_polymorph"]} '
        f'RSW {sv["rod_staff_wand"]} BW {sv["breath_weapon"]} Sp {sv["spell"]}</div>'
        f'<div class="kv"><span>Money</span>{esc(money_s or "-")}</div>'
    )
    if items is not None:
        body += item_table(items)
    raw = esc(json.dumps(rec, indent=1))
    return (f'<details class="card"><summary>{esc(head)}</summary>{body}'
            f'<details><summary class="small">full record (JSON)</summary><pre>{raw}</pre></details></details>')


def section_monsters(gamedir):
    out = []
    for f in files(gamedir, "MON", "CHA.DAX"):
        d = run("gbchar.py", "monsters", f)
        if not d:
            continue
        itm = {}
        if d.get("item_file") and os.path.exists(os.path.join(gamedir, d["item_file"])):
            di = run("gbitem.py", "items", os.path.join(gamedir, d["item_file"]), "--exe", gamedir)
            for b in (di or {}).get("blocks", []):
                itm[b["block"]] = b["records"]
        out.append(f'<h3>{esc(d["file"])} ({len(d["monsters"])} records)</h3>')
        for m in d["monsters"]:
            out.append(creature_card(m["record"], itm.get(m["id"], []), f'#{m["id"]} {m["record"]["name"]}'))
    return "\n".join(out)


def section_saves(gamedir):
    out = []
    for f in files(gamedir, "SAVGAM", ".DAT"):
        d = run("gbchar.py", "savegame", f)
        if not d:
            continue
        loc = d["location"]
        t = d["time"]
        out.append(f'<h3>{esc(d["file"])} (slot {esc(d["letter"])})</h3>'
                   f'<div class="kv"><span>Where</span>{esc(loc["type"]["name"])}, map {loc["map_id"]} at '
                   f'({loc["x"]},{loc["y"]}) facing {loc["facing"]}; disk {d["disk"]}; '
                   f'state {esc(d["interface_state"]["name"])}</div>'
                   f'<div class="kv"><span>Time</span>year {t["year"]} month {t["month"]} day {t["day"]} '
                   f'{t["hour"]:02d}:{t["minute_tens"]}{t["minute_ones"]}</div>'
                   f'<div class="kv"><span>Wall sets</span>{esc(d["wall_sets"])}</div>')
        for p in d["party"]:
            sav = os.path.join(gamedir, p["sav"])
            c = run("gbchar.py", "char", sav) if os.path.exists(sav) else None
            items = []
            itf = os.path.join(gamedir, p["items_file"])
            if os.path.exists(itf):
                di = run("gbitem.py", "items", itf, "--exe", gamedir)
                for b in (di or {}).get("blocks", []):
                    items += b["records"]
            if c:
                rec = c.get("record", c)
                out.append(creature_card(rec, items, f'{p["index"]}. {p["summary"]["name"]} ({p["sav"]})'))
    return "\n".join(out)


# ---------------------------------------------------------------- page

CSS = """
:root{--bg:#fbfaf7;--fg:#1d1d1f;--muted:#666;--line:#ddd;--card:#fff;--ev:#fff3c4;--sq:#f4f2ec}
@media (prefers-color-scheme: dark){:root{--bg:#16171a;--fg:#e6e6e6;--muted:#999;--line:#333;--card:#1f2125;--ev:#4a3f10;--sq:#24262b}}
body{background:var(--bg);color:var(--fg);font:14px/1.45 system-ui,sans-serif;margin:0;padding:16px;max-width:1200px}
h1{font-size:20px}h2{margin-top:28px;border-bottom:1px solid var(--line)}h3{font-size:15px;margin:18px 0 6px}
nav a{margin-right:14px}.note{color:var(--muted)}
details{margin:4px 0}summary{cursor:pointer}.small{font-size:12px;color:var(--muted)}
.card{background:var(--card);border:1px solid var(--line);border-radius:6px;padding:4px 10px;margin:4px 0}
.kv span{display:inline-block;width:110px;color:var(--muted)}
table.t{border-collapse:collapse;margin:6px 0;font-size:13px;display:block;overflow-x:auto}
.t td,.t th{border:1px solid var(--line);padding:2px 6px;text-align:left;white-space:nowrap}
pre{font-size:11px;max-height:300px;overflow:auto}
svg.map{max-width:100%;height:auto}.sq{fill:var(--sq);stroke:var(--line);stroke-width:.5}.sq.ev{fill:var(--ev)}
.evn{font-size:9px;text-anchor:middle;fill:var(--muted)}.ax{font-size:8px;text-anchor:middle;fill:var(--muted)}
"""


def main(argv):
    if len(argv) != 3:
        sys.stderr.write(__doc__)
        return 2
    gamedir, out = argv[1], argv[2]
    sections = [
        ("maps", "Maps (GEO)", section_maps(gamedir)),
        ("monsters", "Monsters (MON?CHA + MON?ITM)", section_monsters(gamedir)),
        ("treasure", "Treasure (ITEM?.DAX)", section_treasure(gamedir)),
        ("items", "Base item table (ITEMS)", section_basetable(gamedir)),
        ("saves", "Save games (SAVGAM + CHRDAT)", section_saves(gamedir)),
    ]
    nav = "".join(f'<a href="#{k}">{esc(t)}</a>' for k, t, _ in sections)
    body = "".join(f'<h2 id="{k}">{esc(t)}</h2>{s}' for k, t, s in sections)
    page = (f'<!doctype html><html lang="en"><head><meta charset="utf-8">'
            f'<meta name="viewport" content="width=device-width,initial-scale=1">'
            f'<title>PoR Data Viewer</title><style>{CSS}</style></head><body>'
            f'<h1>Pool of Radiance data viewer</h1><p class="note">Decoded from {esc(os.path.abspath(gamedir))} '
            f'by tools/scripts/por_view.py (geo.py, gbchar.py, gbitem.py).</p><nav>{nav}</nav>{body}</body></html>')
    with open(out, "w", encoding="utf-8") as fh:
        fh.write(page)
    print(f"wrote {out} ({len(page) // 1024} KB)")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
