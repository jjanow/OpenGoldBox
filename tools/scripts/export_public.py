#!/usr/bin/env python3
"""Build the public OpenGoldBox tree from this (private) repo.

    python3 -I tools/scripts/export_public.py TARGET [--manifest FILE] [--max-kb N]

TARGET must not exist or must be an empty folder. The tree is built from an **allow-list** of git-tracked files:

- our code: `src/**`, `tests/**` (minus any `private/` or `fixtures-private/` folder), `tools/scripts/**`
  (the reference extraction tools), `*.sln` / `*.slnx` / `Directory.Build.*` at the root;
- our docs: `docs/module-format.md`, `docs/schemas/**`, `docs/re/**/*.md` (format specs written by us);
- the public `README.md`, `LICENSE` and `.gitignore` from `tools/public/` (copied to the tree root).

Never exported: `reverse-engineering/**` (disk images, extracted files, picture dumps), `docs/manuals/**`, PLAN.md,
AGENTS.md, and anything not on the allow-list.

After writing, the tree is checked; any failure prints the file and line and exits with status 1:
- no file over the size limit (default 512 KB);
- no image / audio / disk-image / archive / game-data file (by extension and by magic bytes);
- no file whose sha256 matches a file under `reverse-engineering/` (masters, extracted files, dumps);
- no hex-dump lines (offset + 8 or more hex bytes) and no disassembly listing lines (seg:off + bytes + mnemonic);
- no private marker, i.e. the word private in square brackets (put it on a doc line that must stay in this repo).

Not checked automatically: quoted original game text in the docs. Format docs describe layouts; they must not quote
the games' text, so read new `docs/re/` files before each refresh.

The manifest (default TARGET/../opengoldbox-manifest.txt) lists every exported file with its size and sha256.
"""

from __future__ import annotations

import argparse
import fnmatch
import hashlib
import re
import shutil
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
PUBLIC = REPO / "tools" / "public"

ALLOW = [
    "src/**",
    "tests/**",
    "tools/scripts/**",
    "*.sln",
    "*.slnx",
    "Directory.Build.props",
    "Directory.Build.targets",
    "docs/module-format.md",
    "docs/schemas/**",
    "docs/re/**.md",
]
DENY = [
    # Both depths: "tests/**/x/**" needs a folder between tests/ and x/, so tests/x/ is listed on its own.
    "tests/private/**",
    "tests/fixtures-private/**",
    "tests/**/private/**",
    "tests/**/fixtures-private/**",
    "reverse-engineering/**",
    "docs/manuals/**",
    "**/__pycache__/**",
    # Hold the Amiga PoR palette values copied from the game program. Export once they are read from the player's
    # program file at import time instead (PLAN phase 1).
    "tools/scripts/gbpic.py",
    "tools/scripts/amiga_por_art.py",
    "tools/scripts/por_art_compare.py",
    "docs/re/por-art-formats.md",
    # Test-rig helper: holds a byte pattern of the game program's code-wheel check (DOSBox test copies only).
    "tools/scripts/por_dosbox_game.py",
]
# tools/public/<name> -> <name in the public tree>
PUBLIC_FILES = {"README.md": "README.md", "LICENSE": "LICENSE", "gitignore": ".gitignore"}

BAD_EXT = {
    # images / audio / video
    ".png", ".gif", ".jpg", ".jpeg", ".bmp", ".pcx", ".lbm", ".iff", ".ilbm", ".tga", ".webp",
    ".wav", ".mp3", ".ogg", ".flac", ".mid", ".midi", ".voc", ".mod",
    ".mp4", ".avi", ".mkv",
    # disk images, archives, documents scanned from paper
    ".img", ".ima", ".dsk", ".adf", ".ipf", ".d64", ".g64", ".nib", ".woz", ".a2r", ".st", ".stx", ".msa",
    ".hdm", ".fdi", ".d88", ".nes", ".gen", ".md", ".bin", ".iso", ".cue", ".zip", ".7z", ".rar", ".lha", ".lzh",
    ".gz", ".tar", ".pdf", ".djvu", ".cbz",
    # Gold Box game files
    ".dax", ".exe", ".ovr", ".com", ".sav", ".dsn", ".glb", ".tlb",
}
BAD_EXT_ALLOWED = {".md"}  # ".md" is also a Genesis ROM extension; Markdown is fine
MAGIC = [
    (b"\x89PNG", "PNG image"), (b"GIF8", "GIF image"), (b"\xff\xd8\xff", "JPEG image"), (b"BM", None),
    (b"FORM", "IFF file"), (b"RIFF", "RIFF file"), (b"PK\x03\x04", "zip archive"), (b"MZ", "DOS/Windows executable"),
    (b"CAPS", "IPF disk image"), (b"%PDF", "PDF"), (b"Rar!", "rar archive"), (b"7z\xbc\xaf", "7z archive"),
]
HEXDUMP = re.compile(r"^\s*(?:[0-9A-Fa-f]{4}:)?[0-9A-Fa-f]{4,8}[:h]?\s+(?:[0-9A-Fa-f]{2}[\s-]+){8,}")
DISASM = re.compile(
    r"^\s*[0-9A-Fa-f]{4}:[0-9A-Fa-f]{4}\s+(?:[0-9A-Fa-f]{2}\s?){1,8}\s+"
    r"(?:mov|push|pop|call|jmp|j[a-z]{1,3}|cmp|add|sub|xor|and|or|lea|ret[fn]?|int|les|lds|inc|dec|shl|shr|test)\b",
    re.IGNORECASE,
)
PRIVATE_MARK = "[" + "private]"


def match(path: str, patterns: list[str]) -> bool:
    for p in patterns:
        if fnmatch.fnmatchcase(path, p):
            return True
        # "a/**" also matches files directly in "a/"
        if p.endswith("/**") and path.startswith(p[:-2]):
            return True
        if "/**." in p:  # "docs/re/**.md"
            base, ext = p.split("/**", 1)
            if path.startswith(base + "/") and path.endswith(ext):
                return True
    return False


def tracked_files() -> list[str]:
    out = subprocess.run(["git", "-C", str(REPO), "ls-files", "-z"], check=True, capture_output=True).stdout
    return [p for p in out.decode().split("\0") if p]


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def private_hashes() -> set[str]:
    """sha256 of every file under reverse-engineering/ (tracked or not; masters over 50 MB are skipped).

    Our own scripts and notes kept there (*.py, *.md) are skipped: copies of tools/scripts live beside the images."""
    hashes = set()
    root = REPO / "reverse-engineering"
    if root.is_dir():
        for p in root.rglob("*"):
            if (p.is_file() and not p.is_symlink() and p.suffix.lower() not in {".py", ".md"}
                    and p.stat().st_size < 50 << 20):
                hashes.add(sha256(p))
    return hashes


def check(target: Path, max_kb: int) -> list[str]:
    problems = []
    bad_hashes = private_hashes()
    for p in sorted(target.rglob("*")):
        if not p.is_file():
            continue
        rel = p.relative_to(target).as_posix()
        size = p.stat().st_size
        if size > max_kb * 1024:
            problems.append(f"{rel}: {size} bytes, over the {max_kb} KB limit")
        ext = p.suffix.lower()
        if ext in BAD_EXT and ext not in BAD_EXT_ALLOWED:
            problems.append(f"{rel}: blocked file type {ext}")
        head = p.read_bytes()[:8]
        for magic, what in MAGIC:
            if what and head.startswith(magic):
                problems.append(f"{rel}: looks like a {what}")
        if size and sha256(p) in bad_hashes:
            problems.append(f"{rel}: identical to a file under reverse-engineering/")
        try:
            text = p.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            problems.append(f"{rel}: binary (not UTF-8) file")
            continue
        for n, line in enumerate(text.splitlines(), 1):
            if HEXDUMP.search(line):
                problems.append(f"{rel}:{n}: hex dump line")
            elif DISASM.search(line):
                problems.append(f"{rel}:{n}: disassembly line")
            if PRIVATE_MARK in line:
                problems.append(f"{rel}:{n}: {PRIVATE_MARK} marker")
    return problems


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("target", type=Path)
    ap.add_argument("--manifest", type=Path)
    ap.add_argument("--max-kb", type=int, default=512)
    args = ap.parse_args()

    target = args.target.resolve()
    if target.exists() and (not target.is_dir() or any(target.iterdir())):
        print(f"error: {target} exists and is not an empty folder", file=sys.stderr)
        return 2
    if REPO in target.parents or target == REPO:
        print("error: TARGET must be outside this repo", file=sys.stderr)
        return 2
    target.mkdir(parents=True, exist_ok=True)

    rows = []
    for rel in tracked_files():
        if not match(rel, ALLOW) or match(rel, DENY):
            continue
        src = REPO / rel
        if not src.is_file():  # deleted in the working tree
            continue
        dst = target / rel
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, dst)
        rows.append((rel, rel))
    for src_name, dst_name in PUBLIC_FILES.items():
        src = PUBLIC / src_name
        if not src.is_file():
            print(f"error: missing {src}", file=sys.stderr)
            return 2
        shutil.copy2(src, target / dst_name)
        rows.append((dst_name, f"tools/public/{src_name}"))

    manifest = args.manifest or target.parent / "opengoldbox-manifest.txt"
    with manifest.open("w", encoding="utf-8") as f:
        for dst_name, src_name in sorted(rows):
            p = target / dst_name
            f.write(f"{p.stat().st_size:>9}  {sha256(p)}  {dst_name}  <- {src_name}\n")
    print(f"exported {len(rows)} files to {target}; manifest {manifest}")

    problems = check(target, args.max_kb)
    for line in problems:
        print("LEAK CHECK:", line, file=sys.stderr)
    if problems:
        print(f"{len(problems)} problem(s); do not publish this tree", file=sys.stderr)
        return 1
    print("leak check passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
