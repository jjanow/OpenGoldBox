#!/usr/bin/env python3
"""Pool of Radiance (DOS) creature records, monster files and save games -> JSON.

Usage (run with ``python3 -I``; inputs are untrusted data):
    gbchar.py char     FILE.SAV|FILE.CHA [--json] [--raw]
    gbchar.py monsters MONnCHA.DAX       [--json] [--raw]
    gbchar.py savegame SAVGAMx.DAT       [--json] [--raw] [--no-party]
    gbchar.py check    GAMEDIR            # decode every MON?CHA + CHRDAT* + SAVGAM*, run sanity checks

Formats: docs/re/character-format.md (285-byte creature record, MON?CHA/ITM/SPC)
and docs/re/save-format.md (CHRDATxn.SAV/.ITM/.SPC, SAVGAMx.DAT).  The 63-byte
item record and the 9-byte effect record are specified in docs/re/item-format.md;
this tool only splits .ITM/.SPC files (and MON?ITM/MON?SPC blocks) into records
and passes them on as hex.

Without --json a short text summary is printed.  --raw adds the full 285 record
bytes as hex (and, for savegame, the four GBVM areas).  JSON shape: see the
"JSON" sections of the two specs; top-level key "format" carries a version tag.

Field names are our own.  The enum name maps below are AD&D rules terms, not
game text.  Nothing from the game files is embedded here.
"""
import hashlib
import json
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dax  # noqa: E402

FORMAT_TAG = "gbchar/1"
CHAR_SIZE = 0x11D          # 285
ITEM_SIZE = 63             # docs/re/item-format.md
EFFECT_SIZE = 9            # docs/re/item-format.md
SAVGAM_SIZE = 13137

RACES = ["monster", "dwarf", "elf", "gnome", "half-elf", "halfling", "half-orc", "human"]
CLASSES = ["cleric", "druid", "fighter", "paladin", "ranger", "magic-user", "thief", "monk",
           "cleric/fighter", "cleric/fighter/magic-user", "cleric/ranger", "cleric/magic-user",
           "cleric/thief", "fighter/magic-user", "fighter/thief", "fighter/magic-user/thief",
           "magic-user/thief", "monster"]
BASE_CLASSES = ["cleric", "druid", "fighter", "paladin", "ranger", "magic-user", "thief", "monk"]
GENDERS = ["male", "female"]
ALIGNMENTS = ["lawful good", "lawful neutral", "lawful evil", "neutral good", "true neutral",
              "neutral evil", "chaotic good", "chaotic neutral", "chaotic evil"]
STATUSES = ["okay", "animated", "temporarily_gone", "fled", "unconscious", "dying", "dead", "petrified", "gone"]
# Creature-type categories after Lee 12.2.2.1 (0x9F); gaps are unused codes.
CREATURE_TYPES = {0: "humanoid", 1: "monstrous humanoid", 2: "giant", 3: "dragon", 4: "undead",
                  7: "extra-dimensional", 8: "fire-based", 9: "cold-based", 10: "regenerating",
                  11: "reptile/amphibian", 12: "avian", 14: "snake/scorpion", 15: "quadruped",
                  16: "insect", 17: "generic"}
SAVES = ["paralyzation_poison_death", "petrification_polymorph", "rod_staff_wand", "breath_weapon", "spell"]
THIEF_SKILLS = ["pick_pockets", "open_locks", "find_remove_traps", "move_silently",
                "hide_in_shadows", "hear_noise", "climb_walls", "read_languages"]
MONEY = ["copper", "silver", "electrum", "gold", "platinum", "gems", "jewelry"]
# Equipment pointer slots 0xCC..0xFF (stale far pointers on disk; rebuilt on load).
EQUIP_SLOTS = ["weapon", "shield", "body_armor", "gauntlets", "helm", "belt", "robe", "cloak",
               "boots", "ring1", "ring2", "arrow", "bolt"]
LOCATION_TYPES = {1: "3d_map", 2: "wilderness_west", 3: "wilderness_central", 4: "wilderness_east"}
INTERFACE_STATES = {0: "main_menu", 1: "shop_temple", 2: "camp", 3: "outdoors", 4: "indoors",
                    5: "combat", 6: "loot", 7: "game_won"}


def spell_group(sid):
    """Spell id (1..56) -> (caster, level) after the id ranges in Lee 12.2.2.1."""
    for lo, hi, grp in ((1, 8, ("cleric", 1)), (9, 21, ("magic-user", 1)), (22, 28, ("cleric", 2)),
                        (29, 35, ("magic-user", 2)), (36, 44, ("cleric", 3)), (45, 55, ("magic-user", 3)),
                        (56, 56, ("cleric", 7))):
        if lo <= sid <= hi:
            return grp
    return (None, None)


def _enum(table, v):
    if isinstance(table, dict):
        name = table.get(v)
    else:
        name = table[v] if 0 <= v < len(table) else None
    return {"id": v, "name": name}


def pascal_string(b, maxlen):
    n = min(b[0], maxlen)
    return b[1:1 + n].decode("latin-1")


def _s8(v):
    return v - 256 if v > 127 else v


def decode_creature(r, raw=False):
    """Decode one 285-byte creature record (PC, NPC or monster)."""
    if len(r) != CHAR_SIZE:
        raise ValueError("creature record must be %d bytes, got %d" % (CHAR_SIZE, len(r)))
    u16 = lambda o: struct.unpack_from("<H", r, o)[0]
    u32 = lambda o: struct.unpack_from("<I", r, o)[0]

    mem = []
    for slot, v in enumerate(r[0x17:0x2C]):
        if v:
            sid = v & 0x7F
            caster, lvl = spell_group(sid)
            mem.append({"slot": slot, "spell": sid, "caster": caster, "level": lvl,
                        "pending": bool(v & 0x80)})
    known = [i for i in range(1, 57) if r[0x32 + i]]

    def attack(cnt_x2, nd, ns, bonus):
        return {"attacks_x2": cnt_x2, "dice": nd, "sides": ns, "bonus": _s8(bonus)}

    out = {
        "name": pascal_string(r[0:16], 15),
        "abilities": {"str": r[0x10], "str_exc": r[0x16], "int": r[0x11], "wis": r[0x12],
                      "dex": r[0x13], "con": r[0x14], "cha": r[0x15]},
        "race": _enum(RACES, r[0x2E]),
        "class": _enum(CLASSES, r[0x2F]),
        "gender": _enum(GENDERS, r[0x9E]),
        "alignment": _enum(ALIGNMENTS, r[0xA0]),
        "age": u16(0x30),
        "levels": {BASE_CLASSES[i]: _s8(r[0x96 + i]) for i in range(8)},
        "level_highest": _s8(r[0x73]),
        "xp": u32(0xAC),
        "hp": {"current": r[0x11B], "max": r[0x32], "rolled": r[0xB1]},
        "drain": {"levels": r[0x74], "hp": r[0x75]},
        "thac0_base": 60 - r[0x2D],
        "ac_base": 60 - r[0xA9],
        "saves": {SAVES[i]: r[0x6D + i] for i in range(5)},
        "movement_base": r[0x72],
        "attacks": {"primary": attack(r[0xA1], r[0xA3], r[0xA5], r[0xA7]),
                    "secondary": attack(r[0xA2], r[0xA4], r[0xA6], r[0xA8]),
                    "str_bonus_allowed": r[0xAA]},
        "spells": {
            "memorized": mem,
            "known": known,
            "slots": {"cleric": list(r[0xB2:0xB5]), "magic-user": list(r[0xB5:0xB8])},
            "camp_hours_code": r[0x2C],
        },
        "thief_skills": {THIEF_SKILLS[i]: r[0x77 + i] for i in range(8)},
        "money": {MONEY[i]: u16(0x88 + 2 * i) for i in range(7)},
        "item_usability_mask": r[0xB0],
        "item_count": r[0xC7],
        "npc": {"raw": r[0x84], "is_npc": bool(r[0x84] & 0x80), "morale_bits": r[0x84] & 0x7F,
                "treasure_share": r[0x85] & 7, "share_raw": r[0x85]},
        "monster": {"creature_type": _enum(CREATURE_TYPES, r[0x9F]), "xp_base": u16(0xB8),
                    "xp_per_rolled_hp": r[0xBA], "undead_turn_index": _s8(r[0x76])},
        "size": {"raw": r[0x6C], "icon_dims": r[0x6C] & 0x7F, "large": bool(r[0x6C] & 0x80)},
        "portrait": {"head": r[0xBB], "body": r[0xBC]},
        "icon": {"head": r[0xBD], "body": r[0xBE], "order": r[0xBF], "size": r[0xC0],
                 "colors": list(r[0xC1:0xC7])},
        "combat": {"status": _enum(STATUSES, r[0x10C]), "in_play": r[0x10D], "hostile": r[0x10E],
                   "autocombat": r[0x10F]},
        "derived": {  # recomputed by the game on load / each round; disk values may be stale
            "thac0": 60 - r[0x110], "ac": 60 - r[0x111], "ac_rear": 60 - r[0x112],
            "attacks": {"primary": attack(r[0x113], r[0x115], r[0x117], r[0x119]),
                        "secondary": attack(r[0x114], r[0x116], r[0x118], r[0x11A])},
            "movement": r[0x11C], "sweep_level": r[0x6B], "hands_used": r[0x100],
            "save_bonus": _s8(r[0x101]), "encumbrance": u16(0x102),
        },
        "byte_0xAB": r[0xAB],
        "reserved": {"0x83": r[0x83], "0x86": r[0x86], "0x87": r[0x87]},
    }
    if raw:
        out["raw"] = bytes(r).hex()
    return out


def split_records(data, size):
    if data is None:
        return None
    return [data[i:i + size].hex() for i in range(0, len(data) - len(data) % size, size)]


def _sidecar(path, ext):
    """Find FILE.<ext> next to path with any case (DOS names are case-insensitive)."""
    d = os.path.dirname(path) or "."
    stem = os.path.splitext(os.path.basename(path))[0].lower()
    for name in os.listdir(d):
        s, e = os.path.splitext(name)
        if s.lower() == stem and e.lower() == "." + ext.lower():
            return os.path.join(d, name)
    return None


def _read(path):
    with open(path, "rb") as f:
        return f.read()


def load_character(path, raw=False):
    data = _read(path)
    rec = decode_creature(data[:CHAR_SIZE], raw)
    out = {"format": FORMAT_TAG, "kind": "character", "file": os.path.basename(path),
           "size": len(data), "record": rec}
    for ext, size, key in (("ITM", ITEM_SIZE, "items"), ("SPC", EFFECT_SIZE, "effects")):
        p = _sidecar(path, ext)
        blob = _read(p) if p else None
        out[key] = {"file": os.path.basename(p) if p else None,
                    "size": len(blob) if blob is not None else 0,
                    "records": split_records(blob, size) or []}
    return out


def _dax_blocks(path):
    if path is None:
        return {}
    return {i: b for i, b in dax.DaxFile(path).blocks()}


def load_monsters(path, raw=False):
    base = os.path.basename(path)
    stem = os.path.splitext(base)[0]
    if not (stem.upper().startswith("MON") and stem.upper().endswith("CHA")):
        raise ValueError("expected MONnCHA.DAX, got %s" % base)
    setno = stem[3:-3]
    d = os.path.dirname(path) or "."
    sib = {}
    for kind in ("ITM", "SPC"):
        want = ("MON%s%s.DAX" % (setno, kind)).lower()
        sib[kind] = next((os.path.join(d, n) for n in os.listdir(d) if n.lower() == want), None)
    cha = dax.DaxFile(path)
    itm, spc = _dax_blocks(sib["ITM"]), _dax_blocks(sib["SPC"])
    mons = []
    for e in sorted(cha.entries, key=lambda e: e.id):
        blk = cha.block(e)
        mons.append({"id": e.id, "block_size": len(blk),
                     "record": decode_creature(blk[:CHAR_SIZE], raw) if len(blk) >= CHAR_SIZE else None,
                     "items": split_records(itm.get(e.id), ITEM_SIZE) or [],
                     "effects": split_records(spc.get(e.id), EFFECT_SIZE) or []})
    orphans = sorted((set(itm) | set(spc)) - {m["id"] for m in mons})
    return {"format": FORMAT_TAG, "kind": "monsters", "file": base,
            "set": int(setno) if setno.isdigit() else setno,
            "item_file": os.path.basename(sib["ITM"]) if sib["ITM"] else None,
            "effect_file": os.path.basename(sib["SPC"]) if sib["SPC"] else None,
            "orphan_item_or_effect_ids": orphans, "monsters": mons}


# --- SAVGAMx.DAT -----------------------------------------------------------------
# File = disk byte, then GBVM areas.  Areas 0-2 store one 16-bit word per GBVM
# address, area 3 (the loaded ECL script) one byte per address.
AREAS = [("area_4900", 0x4900, 0x400, 2), ("area_6B00", 0x6B00, 0x400, 2),
         ("area_9700", 0x9700, 0x200, 2), ("ecl_9900", 0x9900, 0x1E00, 1)]


class SavGam:
    def __init__(self, data):
        if len(data) != SAVGAM_SIZE:
            raise ValueError("SAVGAM must be %d bytes, got %d" % (SAVGAM_SIZE, len(data)))
        self.data = data
        self.disk = data[0]
        off = 1
        self.areas = {}
        for name, base, count, width in AREAS:
            n = count * width
            self.areas[name] = (base, width, data[off:off + n], off)
            off += n
        self.pos = data[off:off + 5]
        self.loc_type = data[off + 5]
        self.ifc_state = data[off + 6]
        self.party_count = data[off + 7]
        self.names_off = off + 8
        assert self.names_off + 8 * 41 == SAVGAM_SIZE

    def file_offset(self, addr):
        for name, (base, width, blob, off) in self.areas.items():
            if base <= addr < base + len(blob) // width:
                return off + (addr - base) * width
        raise KeyError(hex(addr))

    def word(self, addr):
        for name, (base, width, blob, off) in self.areas.items():
            if base <= addr < base + len(blob) // width:
                i = (addr - base) * width
                return struct.unpack_from("<H", blob, i)[0] if width == 2 else blob[i]
        raise KeyError(hex(addr))

    def party_names(self):
        out = []
        for i in range(8):
            o = self.names_off + 41 * i
            out.append(pascal_string(self.data[o:o + 41], 40))
        return out


def load_savegame(path, raw=False, party=True):
    data = _read(path)
    g = SavGam(data)
    w = g.word
    stem = os.path.splitext(os.path.basename(path))[0]
    letter = stem[-1].upper() if stem.upper().startswith("SAVGAM") else None
    pos = g.pos
    ecl_blob = g.areas["ecl_9900"][2]
    out = {
        "format": FORMAT_TAG, "kind": "savegame", "file": os.path.basename(path), "letter": letter,
        "disk": g.disk, "disk_gbvm": w(0x6E12),
        "location": {
            "type": _enum(LOCATION_TYPES, g.loc_type),
            "map_id": w(0x49C5),
            "x": _s8(pos[0]), "y": _s8(pos[1]), "facing": pos[2], "wall": pos[3], "event": pos[4],
            "view_3d": w(0x49E6),
            "outdoor_x": w(0x49C3), "outdoor_y": w(0x49C4),
            "previous": {"x": w(0x49F0), "y": w(0x49F1), "script": w(0x49F2)},
        },
        "interface_state": _enum(INTERFACE_STATES, g.ifc_state),
        "time": {"minute_segment": w(0x49C6), "minute_ones": w(0x49C7), "minute_tens": w(0x49C8),
                 "hour": w(0x49C9), "day": w(0x49CA), "month": w(0x49CB), "year": w(0x49CC)},
        "settings": {"game_speed": w(0x49FC) & 0xFF, "portraits": w(0x49FF) & 1,
                     "monster_pics": (w(0x49FF) >> 1) & 1},
        "wall_sets": [{"walldef_id": _s16(w(0x4AFA + i)), "slot": w(0x4AFD + i)} for i in range(3)],
        "flags": [w(0x4A00 + i) for i in range(256)],
        "ecl": {"size": len(ecl_blob), "sha256": hashlib.sha256(ecl_blob).hexdigest()},
        "party_count": g.party_count,
        "party_size_gbvm": w(0x6E3E),
        "party": [],
    }
    names = g.party_names()
    d = os.path.dirname(path) or "."
    for i in range(min(g.party_count, 8)):
        entry = {"index": i + 1, "file_stem": names[i]}
        sav = None
        for name in os.listdir(d):
            s, e = os.path.splitext(name)
            if s.upper() == names[i].upper() and e.upper() == ".SAV":
                sav = os.path.join(d, name)
        entry["sav"] = os.path.basename(sav) if sav else None
        if party and sav:
            c = load_character(sav, raw)
            entry["items_file"] = c["items"]["file"]
            entry["effects_file"] = c["effects"]["file"]
            r = c["record"]
            entry["summary"] = {"name": r["name"], "race": r["race"]["name"], "class": r["class"]["name"],
                                "hp": r["hp"]["current"], "hp_max": r["hp"]["max"],
                                "status": r["combat"]["status"]["name"]}
        out["party"].append(entry)
    if raw:
        out["raw_areas"] = {k: v[2].hex() for k, v in g.areas.items()}
    return out


def _s16(v):
    return v - 0x10000 if v & 0x8000 else v


# --- text output ----------------------------------------------------------------
def _fmt_str(a):
    s = "%d" % a["str"]
    if a["str"] == 18 and a["str_exc"]:
        s += "/%02d" % (a["str_exc"] % 100)
    return s


def char_line(r):
    a = r["abilities"]
    lv = "/".join(str(v) for k, v in r["levels"].items() if v)
    return ("%-15s %-9s %-26s L%-7s HP %3d/%-3d bAC %3d bTHAC0 %2d  S%s I%d W%d D%d C%d Ch%d  %s"
            % (r["name"], r["race"]["name"], r["class"]["name"], lv or "-", r["hp"]["current"], r["hp"]["max"],
               r["ac_base"], r["thac0_base"], _fmt_str(a), a["int"], a["wis"], a["dex"], a["con"],
               a["cha"], r["combat"]["status"]["name"]))


def print_text(obj):
    k = obj["kind"]
    if k == "character":
        print(char_line(obj["record"]))
        print("  items %d, effects %d" % (len(obj["items"]["records"]), len(obj["effects"]["records"])))
    elif k == "monsters":
        print("%s: %d monsters" % (obj["file"], len(obj["monsters"])))
        for m in obj["monsters"]:
            print("  %3d %s  items %d effects %d" % (m["id"], char_line(m["record"]), len(m["items"]),
                                                    len(m["effects"])))
    else:
        loc = obj["location"]
        print("%s: disk %d, map %d at (%d,%d) facing %d, %s, %s; party %d"
              % (obj["file"], obj["disk"], loc["map_id"], loc["x"], loc["y"], loc["facing"],
                 loc["type"]["name"], obj["interface_state"]["name"], obj["party_count"]))
        for p in obj["party"]:
            s = p.get("summary")
            print("  %d %-10s %s" % (p["index"], p["file_stem"], s and "%s (%s %s) HP %d/%d"
                                     % (s["name"], s["race"], s["class"], s["hp"], s["hp_max"])))


# --- sanity checks --------------------------------------------------------------
def check_creature(r, is_monster):
    """Return a list of problem strings for one decoded record."""
    p = []
    n = r["name"]
    if not n or not all(32 <= ord(c) < 127 for c in n):
        p.append("name not printable: %r" % n)
    a = r["abilities"]
    hi = 25 if is_monster else 18
    lo = 0 if is_monster else 3
    for k in ("str", "int", "wis", "dex", "con", "cha"):
        if not lo <= a[k] <= hi:
            p.append("%s out of range: %d" % (k, a[k]))
    if a["str_exc"] and a["str"] != 18:
        p.append("str_exc %d with str %d" % (a["str_exc"], a["str"]))
    if a["str_exc"] > 100:
        p.append("str_exc > 100")
    for key, tbl in (("race", RACES), ("class", CLASSES), ("gender", GENDERS), ("alignment", ALIGNMENTS)):
        if r[key]["name"] is None:
            p.append("%s code %d unknown" % (key, r[key]["id"]))
    if r["combat"]["status"]["name"] is None:
        p.append("status %d unknown" % r["combat"]["status"]["id"])
    if not is_monster:
        if r["hp"]["current"] > r["hp"]["max"]:
            p.append("hp %d > max %d" % (r["hp"]["current"], r["hp"]["max"]))
        if r["hp"]["max"] == 0:
            p.append("hp max 0")
        if r["race"]["id"] == 0:
            p.append("PC with race 0")
    if r["monster"]["creature_type"]["name"] is None:
        p.append("creature type %d unknown" % r["monster"]["creature_type"]["id"])
    return p


def run_check(gamedir):
    names = sorted(os.listdir(gamedir))
    up = {n.upper(): n for n in names}
    total = bad = 0
    report = []
    for n in sorted(up):
        if n.startswith("MON") and n.endswith("CHA.DAX"):
            m = load_monsters(os.path.join(gamedir, up[n]))
            for mon in m["monsters"]:
                total += 1
                if mon["block_size"] != CHAR_SIZE:
                    report.append("%s id %d: block size %d" % (n, mon["id"], mon["block_size"]))
                    bad += 1
                    continue
                pr = check_creature(mon["record"], True)
                if mon["record"]["item_count"] not in (0, len(mon["items"])):
                    pr.append("item_count %d vs %d ITM records" % (mon["record"]["item_count"], len(mon["items"])))
                if pr:
                    bad += 1
                    report.append("%s id %d %s: %s" % (n, mon["id"], mon["record"]["name"], "; ".join(pr)))
            if m["orphan_item_or_effect_ids"]:
                report.append("%s: ITM/SPC ids without CHA: %s" % (n, m["orphan_item_or_effect_ids"]))
        elif n.startswith("CHRDAT") and (n.endswith(".SAV") or n.endswith(".CHA")):
            c = load_character(os.path.join(gamedir, up[n]))
            total += 1
            pr = []
            if c["size"] != CHAR_SIZE:
                pr.append("file size %d" % c["size"])
            pr += check_creature(c["record"], False)
            if c["record"]["item_count"] != len(c["items"]["records"]):
                pr.append("item_count %d vs %d ITM records" % (c["record"]["item_count"], len(c["items"]["records"])))
            for k, sz in (("items", ITEM_SIZE), ("effects", EFFECT_SIZE)):
                if c[k]["size"] % sz:
                    pr.append("%s size %d not a multiple of %d" % (k, c[k]["size"], sz))
            if pr:
                bad += 1
                report.append("%s: %s" % (n, "; ".join(pr)))
        elif n.startswith("SAVGAM") and n.endswith(".DAT"):
            g = load_savegame(os.path.join(gamedir, up[n]))
            total += 1
            pr = []
            if g["disk"] != g["disk_gbvm"]:
                pr.append("disk %d != GBVM $6E12 %d" % (g["disk"], g["disk_gbvm"]))
            if g["party_count"] != g["party_size_gbvm"]:
                pr.append("party %d != GBVM $6E3E %d" % (g["party_count"], g["party_size_gbvm"]))
            for p in g["party"]:
                if not p["sav"]:
                    pr.append("party file %s.SAV missing" % p["file_stem"])
            # The ECL area should hold the script block of the current map from ECL<disk>.DAX.
            ecl = up.get("ECL%d.DAX" % g["disk"])
            if ecl:
                blocks = _dax_blocks(os.path.join(gamedir, ecl))
                sg = SavGam(_read(os.path.join(gamedir, up[n])))
                buf = sg.areas["ecl_9900"][2]
                blk = blocks.get(g["location"]["map_id"])
                if blk is None:
                    pr.append("no ECL block %d in %s" % (g["location"]["map_id"], ecl))
                else:
                    body = blk[2:]
                    same = sum(1 for i in range(len(body)) if buf[i] == body[i])
                    report.append("%s: ECL area vs %s block %d (minus 2-byte prefix): %d/%d bytes equal"
                                  % (n, ecl, g["location"]["map_id"], same, len(body)))
            if pr:
                bad += 1
                report.append("%s: %s" % (n, "; ".join(pr)))
    for line in report:
        print(line)
    print("checked %d records/files, %d with problems" % (total, bad))
    return 1 if bad else 0


def main(argv):
    args = [a for a in argv[1:] if not a.startswith("--")]
    flags = {a for a in argv[1:] if a.startswith("--")}
    if len(args) != 2 or args[0] not in ("char", "monsters", "savegame", "check"):
        print(__doc__.split("\n\n")[1])
        return 2
    cmd, path = args
    raw = "--raw" in flags
    if cmd == "check":
        return run_check(path)
    if cmd == "char":
        obj = load_character(path, raw)
    elif cmd == "monsters":
        obj = load_monsters(path, raw)
    else:
        obj = load_savegame(path, raw, party="--no-party" not in flags)
    if "--json" in flags:
        json.dump(obj, sys.stdout, indent=1)
        print()
    else:
        print_text(obj)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
