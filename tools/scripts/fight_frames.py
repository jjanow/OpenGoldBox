#!/usr/bin/env python3
"""Frame-by-frame screen comparison of DOSBox-X and the Client (Pool of Radiance DOS v1.3; dosbox-trace-method.md).

Run with ``python3 -I``; needs python3-xlib and numpy. Only ever point it at a private Xvfb display.

    fight_frames.py grab  :N SIDE OUT.npz --font 8X8D1.DAX [--rule RE=KEYS]... [--step RE=KEYS]... [--presses N]
                          [--tail S] [--seconds S] [--still S] [--log]
    fight_frames.py ocr   OUT.npz [--font 8X8D1.DAX] [--frames A:B]   row 1 (panel name) and row 24 of each frame
    fight_frames.py compare DOSBOX.npz CLIENT.npz [--report DIR]       look every Client frame up among DOSBox-X's
    fight_frames.py png   OUT.npz INDEX OUT.png [--scale 2]            one frame as a PNG
    fight_frames.py seed  STATE.sav SEED                               write RandSeed DS:4428 into a DOSBox-X state
    fight_frames.py until :N SIDE REGEX --font 8X8D1.DAX [--press KEYS] [--every S] [--timeout S]
                          wait until the state string matches REGEX, pressing KEYS every S seconds meanwhile

SIDE is ``dosbox`` (the DOSBox-X window: 640 x 400 under a 17-pixel menu bar, halved) or ``client`` (the Client's
960 x 600 window, divided by 3). Every grab (python-xlib ``get_image``) is reduced to 320 x 200 EGA colour indices
(nearest of the 16 standard EGA colours); a frame is kept when it differs from the previous one, with its time.

Keys (XTEST, held 0.12 s, the window focused first) are pressed only while the screen has been still for --still
seconds (default 0.4) and the state (panel row 1 + row 24 text with the highlighted words in brackets) has passed
through a different state since the last press, so a menu row that a pause leaves on screen never gets a second
key. The state string is ``NAME|ROW24``; ``--step RE=KEYS`` entries are used once each, in order, when the next one
matches; ``--rule RE=KEYS`` apply whenever no step does. KEYS: xdotool-style names separated by commas (``q``,
``Return``, ``KP_Home``…). The grab ends after --presses presses plus --tail seconds, or after --seconds.

No game data is stored in this file: the font is read from the game's own 8X8D1.DAX at run time.
"""
import re
import struct
import sys
import time
import zipfile
from pathlib import Path

import numpy as np

EGA = np.array([(0, 0, 0), (0, 0, 170), (0, 170, 0), (0, 170, 170), (170, 0, 0), (170, 0, 170), (170, 85, 0),
                (170, 170, 170), (85, 85, 85), (85, 85, 255), (85, 255, 85), (85, 255, 255), (255, 85, 85),
                (255, 85, 255), (255, 255, 85), (255, 255, 255)], dtype=np.int32)


def dax_block(path, block):
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import dax  # noqa: E402  (tools/scripts/dax.py)
    f = dax.DaxFile(str(path))
    for e in f.entries:
        if e.id == block:
            return f.block(e)
    raise SystemExit('%s has no block %d' % (path, block))


class Font:
    """8X8D1 block 201: glyphs 1-31 = letters 0x41-0x5F, 32-63 = ASCII 0x20-0x3F (TextFont.cs)."""

    def __init__(self, path):
        data = dax_block(path, 201)
        self.table = {}
        for g in range(1, min(64, len(data) // 8)):
            bits = bytes(data[g * 8:g * 8 + 8])
            ch = chr(g + 0x40) if g < 32 else chr(g)
            self.table.setdefault(bits, ch)

    def row(self, frame, row, col0=0, col1=40):
        """Text of a text row; highlighted words (every glyph white = 15) in brackets."""
        cells = []
        # the background of a text row is the row's most common colour
        strip = frame[row * 8:row * 8 + 8, col0 * 8:col1 * 8]
        bg = np.bincount(strip.reshape(-1), minlength=16).argmax()
        for c in range(col0, col1):
            cell = frame[row * 8:row * 8 + 8, c * 8:c * 8 + 8]
            fgmask = cell != bg
            if not fgmask.any():
                cells.append((' ', None))
                continue
            bits = bytes(int(''.join('1' if v else '0' for v in fgmask[r]), 2) for r in range(8))
            ch = self.table.get(bits)
            if ch is None:
                ch = '?'
            fg = cell[fgmask]
            cells.append((ch, int(np.bincount(fg).argmax())))
        out, word = [], []

        def flush():
            if word:
                txt = ''.join(ch for ch, _ in word)
                lit = all(col == 15 for _, col in word) and len(word) > 1
                out.append('[%s]' % txt if lit else txt)
                word.clear()
        for ch, col in cells:
            if ch == ' ':
                flush()
                out.append(' ')
            else:
                word.append((ch, col))
        flush()
        return re.sub(' +', ' ', ''.join(out)).strip()


_LUT = {}


def to_indices(rgb):
    """H x W x 3 uint8 -> H x W EGA indices (nearest colour; colours seen are cached)."""
    key = (rgb[..., 0].astype(np.uint32) << 16) | (rgb[..., 1].astype(np.uint32) << 8) | rgb[..., 2]
    uniq, inv = np.unique(key.reshape(-1), return_inverse=True)
    idx = np.empty(len(uniq), dtype=np.uint8)
    for k, u in enumerate(uniq.tolist()):
        v = _LUT.get(u)
        if v is None:
            c = np.array([(u >> 16) & 255, (u >> 8) & 255, u & 255], dtype=np.int32)
            v = _LUT[u] = int(((EGA - c) ** 2).sum(axis=1).argmin())
        idx[k] = v
    return idx[inv].reshape(rgb.shape[:2])


class Window:
    def __init__(self, disp, side):
        from Xlib import display, X
        self.X = X
        self.d = display.Display(disp)
        self.side = side
        self.win = self._find()
        self._raw, self._last, self._geom = None, None, None
        self.grabs = 0

    def _find(self):
        want = 'DOSBox' if self.side == 'dosbox' else 'Gold Box'
        root = self.d.screen().root
        best = None
        stack = [root]
        while stack:
            w = stack.pop()
            try:
                name = w.get_wm_name()
                attrs = w.get_attributes()
                geom = w.get_geometry()
            except Exception:
                continue
            if name and want in name and attrs.map_state == self.X.IsViewable and geom.width >= 320:
                best = w
            try:
                stack.extend(w.query_tree().children)
            except Exception:
                pass
        if best is None:
            raise SystemExit('no %s window' % want)
        return best

    def grab(self):
        self.grabs += 1
        if self._geom is None or self.grabs % 200 == 0:
            self._geom = self.win.get_geometry()
        g = self._geom
        if self.side == 'dosbox':
            x, y, w, h, step = 0, g.height - 400, 640, 400, 2
        else:
            # the Client draws 320 x 200 scaled by an integer, centred in its window
            s = min(g.width // 320, g.height // 200)
            x, y = (g.width - 320 * s) // 2, (g.height - 200 * s) // 2
            w, h, step = 320 * s, 200 * s, s
        try:
            img = self.win.get_image(x, y, w, h, self.X.ZPixmap, 0xFFFFFFFF)
        except Exception:
            self._geom = None  # resized or not yet mapped: measure again
            time.sleep(0.05)
            return self.grab()
        raw = img.data
        if raw == self._raw:
            return self._last
        a = np.frombuffer(raw, dtype=np.uint8).reshape(h, w, 4)[::step, ::step, 2::-1]
        self._raw, self._last = raw, to_indices(np.ascontiguousarray(a))
        return self._last

    def press(self, keys):
        from Xlib import XK
        from Xlib.ext import xtest
        self.win.set_input_focus(self.X.RevertToParent, self.X.CurrentTime)
        self.d.sync()
        for k in keys:
            code = self.d.keysym_to_keycode(XK.string_to_keysym(k))
            if not code:
                raise SystemExit('unknown key %s' % k)
            xtest.fake_input(self.d, self.X.KeyPress, code)
            self.d.sync()
            time.sleep(0.12)
            xtest.fake_input(self.d, self.X.KeyRelease, code)
            self.d.sync()
            time.sleep(0.15)


def parse_rules(items):
    out = []
    for it in items or []:
        pat, _, keys = it.rpartition('=')
        out.append((re.compile(pat), [k for k in keys.split(',') if k]))
    return out


def cmd_grab(args):
    import argparse
    p = argparse.ArgumentParser(prog='fight_frames.py grab')
    p.add_argument('display')
    p.add_argument('side', choices=['dosbox', 'client'])
    p.add_argument('out')
    p.add_argument('--font', required=True)
    p.add_argument('--rule', action='append')
    p.add_argument('--step', action='append')
    p.add_argument('--presses', type=int, default=0)
    p.add_argument('--tail', type=float, default=3.0)
    p.add_argument('--seconds', type=float, default=600)
    p.add_argument('--still', type=float, default=0.4)
    p.add_argument('--log', action='store_true')
    a = p.parse_args(args)
    font = Font(a.font)
    rules, steps = parse_rules(a.rule), parse_rules(a.step)
    win = Window(a.display, a.side)
    frames, times, presses = [], [], []
    t0 = time.monotonic()
    last = None
    last_change = t0
    pressed_state = None
    seen_other = True
    n_press = 0
    end_at = t0 + a.seconds
    try:
        while time.monotonic() < end_at:
            f = win.grab()
            now = time.monotonic()
            if last is None or (f is not last and not np.array_equal(f, last)):
                frames.append(f)
                times.append(now - t0)
                last = f
                last_change = now
                continue
            if now - last_change < a.still:
                continue
            state = '%s|%s' % (font.row(f, 1, 23, 39), font.row(f, 24))
            if state != pressed_state:
                seen_other = True
            if not seen_other or (a.presses and n_press >= a.presses):
                continue
            keys = None
            if steps and steps[0][0].search(state):
                keys = steps.pop(0)[1]
            else:
                for rx, k in rules:
                    if rx.search(state):
                        keys = k
                        break
            if keys is None:
                continue
            if a.log:
                print('%8.2f press %s at %s' % (now - t0, ','.join(keys), state), flush=True)
            presses.append((now - t0, ','.join(keys), state))
            win.press(keys)
            n_press += 1
            pressed_state, seen_other = state, False
            if a.presses and n_press >= a.presses:
                end_at = min(end_at, time.monotonic() + a.tail)
    except KeyboardInterrupt:
        pass
    np.savez_compressed(a.out, frames=np.array(frames, dtype=np.uint8), times=np.array(times),
                        presses=np.array([repr(x) for x in presses]))
    el = time.monotonic() - t0
    print('%d frames, %d presses, %d grabs in %.1f s (%.0f/s) -> %s'
          % (len(frames), n_press, win.grabs, el, win.grabs / el, a.out))


def load(path):
    z = np.load(path)
    return z['frames'], z['times'], list(z['presses']) if 'presses' in z else []


def cmd_ocr(args):
    import argparse
    p = argparse.ArgumentParser(prog='fight_frames.py ocr')
    p.add_argument('npz')
    p.add_argument('--font', required=True)
    p.add_argument('--frames', default=':')
    p.add_argument('--rows', default='1,24')
    a = p.parse_args(args)
    font = Font(a.font)
    fr, tm, pr = load(a.npz)
    s, _, e = a.frames.partition(':')
    rows = [int(r) for r in a.rows.split(',')]
    prev = None
    for i in range(int(s or 0), int(e or len(fr))):
        txt = ' | '.join(font.row(fr[i], r, 23 if r <= 21 and r != 24 else 0, 39 if r <= 21 else 40) for r in rows)
        if txt != prev:
            print('%5d %8.3f %s' % (i, tm[i], txt))
            prev = txt
    for x in pr:
        print('press', x)


def cmd_compare(args):
    import argparse
    p = argparse.ArgumentParser(prog='fight_frames.py compare')
    p.add_argument('dosbox')
    p.add_argument('client')
    p.add_argument('--report')
    p.add_argument('--font')
    a = p.parse_args(args)
    dfr, dtm, _ = load(a.dosbox)
    cfr, ctm, _ = load(a.client)
    index = {}
    for i, f in enumerate(dfr):
        index.setdefault(f.tobytes(), i)
    font = Font(a.font) if a.font else None
    miss = []
    for i, f in enumerate(cfr):
        if f.tobytes() not in index:
            miss.append(i)
    print('Client frames %d, DOSBox-X frames %d, Client frames equal to a DOSBox-X frame: %d (%.1f %%)'
          % (len(cfr), len(dfr), len(cfr) - len(miss), 100.0 * (len(cfr) - len(miss)) / max(1, len(cfr))))
    for i in miss:
        f = cfr[i]
        # nearest DOSBox-X frame by differing pixels
        diffs = [(int((f != d).sum()), j) for j, d in enumerate(dfr)]
        n, j = min(diffs)
        ys, xs = np.nonzero(f != dfr[j])
        box = (xs.min(), ys.min(), xs.max(), ys.max()) if len(xs) else None
        extra = ''
        if font:
            extra = ' | ' + font.row(f, 24)
        print('  client %4d t=%7.3f: nearest dosbox %4d (%d px differ, box %s)%s' % (i, ctm[i], j, n, box, extra))
        if a.report:
            Path(a.report).mkdir(parents=True, exist_ok=True)
            write_png(f, Path(a.report) / ('c%04d.png' % i))
            write_png(dfr[j], Path(a.report) / ('c%04d_d%04d.png' % (i, j)))


def write_png(frame, path, scale=2):
    import zlib
    rgb = EGA[frame].astype(np.uint8)
    rgb = rgb.repeat(scale, 0).repeat(scale, 1)
    h, w, _ = rgb.shape
    raw = b''.join(b'\x00' + rgb[y].tobytes() for y in range(h))

    def chunk(t, d):
        return struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d) & 0xFFFFFFFF)
    Path(path).write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 2, 0, 0, 0))
                           + chunk(b'IDAT', zlib.compress(raw, 6)) + chunk(b'IEND', b''))


def cmd_png(args):
    fr, _, _ = load(args[0])
    write_png(fr[int(args[1])], args[2], int(args[3]) if len(args) > 3 else 2)


def cmd_seed(args):
    """RandSeed DS:4428 (dword) in the state's Memory entry (0x88-byte header, then RAM from 0)."""
    path, seed = args[0], int(args[1], 0)
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import dosbox_state  # noqa: E402
    st = dosbox_state.State(path)
    off = 0x88 + st.ds + 0x4428  # st.ds is already DS * 16
    tmp = path + '.new'
    with zipfile.ZipFile(path) as zin, zipfile.ZipFile(tmp, 'w', zipfile.ZIP_STORED) as zout:
        for info in zin.infolist():
            data = zin.read(info.filename)
            if info.filename == 'Memory':
                old = struct.unpack_from('<I', data, off)[0]
                data = data[:off] + struct.pack('<I', seed & 0xFFFFFFFF) + data[off + 4:]
                print('RandSeed 0x%08X -> 0x%08X' % (old, seed & 0xFFFFFFFF))
            zout.writestr(info, data)
    Path(tmp).replace(path)


def cmd_until(args):
    import argparse
    p = argparse.ArgumentParser(prog='fight_frames.py until')
    p.add_argument('display')
    p.add_argument('side', choices=['dosbox', 'client'])
    p.add_argument('regex')
    p.add_argument('--font', required=True)
    p.add_argument('--press', default='')
    p.add_argument('--every', type=float, default=1.5)
    p.add_argument('--timeout', type=float, default=60)
    a = p.parse_args(args)
    font = Font(a.font)
    win = Window(a.display, a.side)
    rx = re.compile(a.regex)
    t0 = time.monotonic()
    last_press = t0
    state = ''
    while time.monotonic() - t0 < a.timeout:
        f = win.grab()
        state = '%s|%s' % (font.row(f, 1, 23, 39), font.row(f, 24))
        if rx.search(state):
            print('ok: %s' % state)
            return
        if a.press and time.monotonic() - last_press > a.every:
            win.press([k for k in a.press.split(',') if k])
            last_press = time.monotonic()
        time.sleep(0.1)
    raise SystemExit('timeout; last state: %s' % state)


def main(argv):
    if len(argv) < 2 or argv[1] not in ('grab', 'ocr', 'compare', 'png', 'seed', 'until'):
        print(__doc__, file=sys.stderr)
        return 2
    globals()['cmd_' + argv[1]](argv[2:])
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
