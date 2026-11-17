#!/usr/bin/env python3
"""Reduce a DOSBox-X EGA screenshot to the 320x200 EGA colour indices `goldbox-tools view-compare` reads.

    python3 -I ega_screen.py SHOT.png OUT.bin [--png CHECK.png]

The screenshot is the DOSBox-X window as `dosbox_xvfb.sh shot` takes it: a menu bar on top (its height is found as
the rows above the 640x400 or 320x200 game picture), then the game picture at 1x or 2x. Each pixel is matched to the
nearest of the 16 standard EGA colours (DOSBox-X's default palette); a pixel further than 24 from every colour stops
the tool (the shot is not an EGA screen).
"""
import sys

from PIL import Image

EGA = [(0, 0, 0), (0, 0, 170), (0, 170, 0), (0, 170, 170), (170, 0, 0), (170, 0, 170), (170, 85, 0), (170, 170, 170),
       (85, 85, 85), (85, 85, 255), (85, 255, 85), (85, 255, 255), (255, 85, 85), (255, 85, 255), (255, 255, 85),
       (255, 255, 255)]


def main(argv):
    if len(argv) < 3:
        print(__doc__, file=sys.stderr)
        return 2
    img = Image.open(argv[1]).convert('RGB')
    w, h = img.size
    scale = 2 if w >= 640 else 1
    top = h - 200 * scale
    if top < 0:
        print(f'{argv[1]}: {w}x{h} is smaller than the game picture', file=sys.stderr)
        return 1
    px = img.load()
    out = bytearray(320 * 200)
    for y in range(200):
        for x in range(320):
            r, g, b = px[x * scale, top + y * scale]
            best = min(range(16), key=lambda i: (EGA[i][0] - r) ** 2 + (EGA[i][1] - g) ** 2 + (EGA[i][2] - b) ** 2)
            d = sum((a - c) ** 2 for a, c in zip(EGA[best], (r, g, b)))
            if d > 24 * 24:
                print(f'{argv[1]}: pixel {x},{y} = {(r, g, b)} is not an EGA colour', file=sys.stderr)
                return 1
            out[y * 320 + x] = best
    with open(argv[2], 'wb') as f:
        f.write(out)
    if '--png' in argv:
        check = Image.new('RGB', (320, 200))
        check.putdata([EGA[i] for i in out])
        check.save(argv[argv.index('--png') + 1])
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
