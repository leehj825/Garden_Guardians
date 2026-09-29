"""Cut seamless ground tiles (grass, dirt, sand) out of the terrain models' baked textures.

    python Tools/procedural/extract_tiles.py OUT_DIR terrain2.glb terrain3.glb ...

Each model has one big texture laid out as loose patches ("islands") of grass, dirt, water edge, ferns
and so on. This finds the largest clean square of each ground kind in each texture (by colour), then
makes it tile by cross-fading it with a copy of itself shifted by half a tile. Writes
grass_<n>.png, dirt_<n>.png and sand_<n>.png (256 px) into OUT_DIR.
"""
import os
import sys

import bpy
import numpy as np
from PIL import Image
from scipy import ndimage as ndi

TILE = 256


def atlas_of(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=path)
    image = next(i for i in bpy.data.images if i.size[0] > 0)
    w, h = image.size
    pixels = np.array(image.pixels[:], dtype=np.float32).reshape(h, w, 4)[::-1, :, :3]  # Blender stores rows bottom-up
    return (np.clip(pixels, 0, 1) * 255).astype(np.uint8)


def hsv(rgb):
    import matplotlib.colors as mc
    a = mc.rgb_to_hsv(rgb / 255.0)
    return a[..., 0] * 360, a[..., 1], a[..., 2]


KINDS = {
    "grass": lambda h, s, v: (h > 55) & (h < 95) & (s > 0.35) & (v > 0.40),
    "dirt": lambda h, s, v: (h > 20) & (h < 42) & (s > 0.30) & (v > 0.45),
    "sand": lambda h, s, v: (h > 22) & (h < 46) & (s > 0.15) & (s < 0.42) & (v > 0.55),
}


def largest_squares(mask, size, want):
    """Up to `want` non-overlapping size x size windows lying entirely inside `mask`, best (roomiest) first."""
    room = ndi.distance_transform_edt(mask)
    found = []
    for _ in range(want):
        y, x = np.unravel_index(np.argmax(room), room.shape)
        if room[y, x] < size / 2:
            break
        y0, x0 = int(y - size // 2), int(x - size // 2)
        if y0 < 0 or x0 < 0 or y0 + size > mask.shape[0] or x0 + size > mask.shape[1]:
            room[max(0, y - 8):y + 8, max(0, x - 8):x + 8] = 0
            continue
        found.append((y0, x0))
        room[max(0, y0 - size // 2):y0 + size * 3 // 2, max(0, x0 - size // 2):x0 + size * 3 // 2] = 0
    return found


def despeckle(crop):
    """Replace pixels that stand out from their surroundings (stray light or dark dots) by the local median."""
    median = ndi.median_filter(crop, size=(5, 5, 1))
    off = np.abs(crop.astype(int) - median.astype(int)).sum(axis=2) > 45
    off = ndi.binary_dilation(off, iterations=1)
    out = crop.copy()
    out[off] = median[off]
    return out


def tileable(crop):
    """Blend the crop with a copy shifted by half a tile, weighting the middle of the crop, so opposite edges match."""
    a = crop.astype(np.float32)
    n = a.shape[0]
    shifted = np.roll(np.roll(a, n // 2, 0), n // 2, 1)
    y, x = np.mgrid[0:n, 0:n] / (n - 1)
    d = np.minimum(np.minimum(x, 1 - x), np.minimum(y, 1 - y)) * 2  # 0 at the edges, 1 in the middle
    w = (d * d * (3 - 2 * d))[..., None]
    out = a * w + shifted * (1 - w)
    # Keep the tile's average colour: blending toward the shifted copy flattens contrast a little.
    out = (out - out.mean((0, 1))) * (a.std((0, 1)) / np.maximum(out.std((0, 1)), 1e-3)) + a.mean((0, 1))
    return np.clip(out, 0, 255).astype(np.uint8)


def main():
    out_dir, models = sys.argv[1], sys.argv[2:]
    os.makedirs(out_dir, exist_ok=True)
    counts = {kind: 0 for kind in KINDS}
    for model in models:
        atlas = atlas_of(model)
        h, s, v = hsv(atlas)
        for kind, rule in KINDS.items():
            # Stay well inside the patch: its rim is bleed from the next patch (light and dark specks).
            mask = ndi.binary_erosion(ndi.binary_opening(rule(h, s, v), iterations=2), iterations=8)
            for size in (384, 256, 192):
                spots = largest_squares(mask, size, 2)
                if spots:
                    break
            for y0, x0 in spots:
                crop = despeckle(atlas[y0:y0 + size, x0:x0 + size])
                crop = Image.fromarray(crop).resize((TILE, TILE), Image.LANCZOS)
                counts[kind] += 1
                name = "%s_%d.png" % (kind, counts[kind])
                Image.fromarray(tileable(np.array(crop))).save(os.path.join(out_dir, name))
                print(os.path.basename(model), kind, "from", (x0, y0), size, "->", name)


if __name__ == "__main__":
    main()
