"""Cuts Tools/terrain_atlas.jpg (a 5x2 sheet of PBR ground textures) into seamless 256px square tiles under
Assets/Textures/Ground/terrain_*.png. The sheet has a preview grid drawn over every tile; it is painted out first.
Needs Pillow and numpy:  python3 Tools/make_ground_tiles.py"""
from PIL import Image
import numpy as np

ATLAS = 'Tools/terrain_atlas.jpg'
OUT = 'Assets/Textures/Ground/'
COLS = [(0, 204), (206, 408), (411, 612), (615, 817), (820, 1024)]
ROWS = [(36, 509), (513, 998)]
# The preview grid drawn over every tile: verticals at these x, horizontals every 42.55 px from these y (per sheet row).
VLINES = [50, 101, 152]
HSTART, HSTEP = [6.5, 41.5], 42.55
# name: (column, row, x, y, side) - the square cut from the tile.
TILES = {
    'grass': (0, 0, 5, 20, 190), 'dirt': (1, 0, 5, 20, 190), 'sand': (2, 0, 0, 150, 150),
    'pondbed': (3, 0, 5, 20, 190), 'path': (4, 0, 5, 20, 190), 'cobble': (0, 1, 5, 20, 190),
    'water': (1, 1, 5, 20, 190), 'flood': (2, 1, 5, 100, 190), 'mud': (3, 1, 5, 150, 190), 'snow': (4, 1, 5, 20, 190),
}
SIZE = 256
# How many pixels each side of a grid line get repaired: smooth textures need a wide repair; a busy one shows a wide
# repair as a smear, so it gets only the line itself. Tiles not listed have no visible grid.
REACH = {'pondbed': 3, 'water': 3, 'flood': 3, 'snow': 3, 'grass': 1, 'dirt': 1, 'sand': 1, 'path': 1}


def bridge(t, centre, axis, reach):
    """Replaces the few pixels round a grid line with a blend of the pixels either side of it."""
    c = int(round(centre))
    lo, hi = c - reach, c + reach + 1
    n = t.shape[axis]
    if lo < 0 or hi >= n:
        return
    for i in range(lo + 1, hi):
        w = (i - lo) / (hi - lo)
        if axis == 1:
            t[:, i] = t[:, lo] * (1 - w) + t[:, hi] * w
        else:
            t[i] = t[lo] * (1 - w) + t[hi] * w


def paint_out(t, row, reach):
    t = t.astype(float)
    for x in VLINES:
        bridge(t, x, 1, reach)
    y = HSTART[row]
    while y < t.shape[0]:
        bridge(t, y, 0, reach)
        y += HSTEP
    return t


def seamless(a):
    """Blends the picture with a half-shifted copy of itself, so its edges meet."""
    n = a.shape[0]
    ramp = 1 - np.abs(np.linspace(-1, 1, n))
    mask = np.clip(np.minimum.outer(ramp, ramp) * 2.0, 0, 1)[..., None]
    shifted = np.roll(np.roll(a, n // 2, axis=0), n // 2, axis=1)
    return a * mask + shifted * (1 - mask)


atlas = np.asarray(Image.open(ATLAS).convert('RGB'))
for name, (c, r, x, y, side) in TILES.items():
    x0, x1 = COLS[c]
    y0, y1 = ROWS[r]
    tile = paint_out(atlas[y0:y1, x0:x1], r, REACH[name]) if name in REACH else atlas[y0:y1, x0:x1].astype(float)
    square = tile[y:y + side, x:x + side]
    out = np.clip(seamless(square), 0, 255).astype(np.uint8)
    Image.fromarray(out).resize((SIZE, SIZE), Image.LANCZOS).save(f'{OUT}terrain_{name}.png', optimize=True)
    print(name)
