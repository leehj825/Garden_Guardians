"""Find what walkers and builders must avoid on a terrain model: its ponds, the oak (trunk and roots),
and the rocks and reed/fern clumps — and draw them over the model's top view so they can be checked by eye.

    python Tools/detect_terrain.py terrain2.glb overlay2.png [--json features2.json]

Ponds are found by their water colour (blue, on cells whose ground is flat); the oak as the tall
structure standing well above the ground round it (see convert_terrain.oak_mask); rocks and
plants as smaller bumps on the ground (see convert_terrain.build_ground_and_oak). Output overlay:
blue = pond, red = oak trunk and roots, yellow = rocks and plants.
"""
import argparse
import json
import math
import os
import sys

import bpy  # noqa: I001 (bpy first: it registers bmesh)
import bmesh
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
from mathutils.interpolate import poly_3d_calc
from PIL import Image
from scipy import ndimage as ndi

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import convert_terrain as C  # noqa: E402

HALF, STEP, N = C.HALF, C.GRID_STEP, C.GRID_N
EDGE_MARGIN = 3.0  # m


def load(path):
    ob = C.import_model(path)
    ob.data.transform(Matrix.Scale(100.0, 4))
    bm = C.bmesh_of(ob)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.normal.z < -0.1 and f.calc_center_median().z < C.SLAB_BOTTOM], context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(ob.data)
    bm.free()
    return ob


def sample(ob):
    """Height and colour of the top surface on the game grid (rows = z, cols = x)."""
    bm = C.bmesh_of(ob)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.faces.ensure_lookup_table()
    uv = bm.loops.layers.uv.active
    image = next(i for i in bpy.data.images if i.size[0] > 0)
    w, h = image.size
    pixels = np.array(image.pixels[:]).reshape(h, w, 4)
    bvh = BVHTree.FromBMesh(bm)
    heights = np.full((N, N), np.nan)
    colour = np.zeros((N, N, 3))
    for j in range(N):
        z = -HALF + j * STEP
        for i in range(N):
            hit = bvh.ray_cast(Vector((-HALF + i * STEP, -z, 1000.0)), Vector((0, 0, -1)))
            if hit[0] is None:
                continue
            face = bm.faces[hit[2]]
            weights = poly_3d_calc([v.co for v in face.verts], hit[0])
            u = sum(wt * loop[uv].uv.x for wt, loop in zip(weights, face.loops))
            v = sum(wt * loop[uv].uv.y for wt, loop in zip(weights, face.loops))
            colour[j, i] = pixels[min(h - 1, int(v * h)), min(w - 1, int(u * w))][:3]
            heights[j, i] = hit[0].z
    bm.free()
    return heights, colour


def water_mask(colour, heights):
    """Blue-ish cells (water), grown a little to take in the wet sand at the rim, kept if they belong to a sizeable flat patch."""
    r, g, b = colour[..., 0], colour[..., 1], colour[..., 2]
    blue = (b > r + 0.06) & (b > g - 0.02) & (b > 0.25)
    blue = ndi.binary_opening(blue, iterations=1)
    labels, n = ndi.label(ndi.binary_closing(blue, iterations=2))
    keep = np.zeros_like(blue)
    for k in range(1, n + 1):
        cells = labels == k
        if cells.sum() >= 60:  # 15 m² at least
            keep |= cells
    return ndi.binary_fill_holes(keep)


def circles(mask, min_radius):
    return C.oak_circles(None, mask, min_radius=min_radius)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("overlay")
    ap.add_argument("--json")
    args = ap.parse_args()

    ob = load(args.src)
    raw, colour = sample(ob)
    holes = np.isnan(raw)
    raw = C.fill_nan(raw)
    colour_filled = colour.copy()

    oak = C.oak_mask(raw)
    oak_grown = ndi.binary_dilation(oak, iterations=3)
    ground = raw.copy()
    ground[oak_grown] = np.nan
    ground = C.fill_nan(ground)
    water = water_mask(colour_filled, raw)
    water &= ~oak_grown

    bumps = ((raw - ndi.grey_opening(raw, size=(7, 7))) > C.PROP_HEIGHT) & ~ndi.binary_dilation(oak, iterations=6) & ~ndi.binary_erosion(water, iterations=2)
    bumps = ndi.binary_opening(bumps, iterations=1)
    lab, n = ndi.label(ndi.binary_dilation(bumps, iterations=1))
    sizes = ndi.sum(bumps, lab, range(1, n + 1))
    props = np.isin(lab, [i + 1 for i, s in enumerate(sizes) if s >= C.PROP_MIN_CELLS]) & ndi.binary_dilation(bumps, iterations=1)
    # The slab's ragged rim is not a prop: ignore anything within EDGE_MARGIN of the edge or of a hole in the model.
    edge = np.zeros_like(props)
    m = int(EDGE_MARGIN / STEP)
    edge[:m, :] = edge[-m:, :] = edge[:, :m] = edge[:, -m:] = True
    props &= ~(edge | ndi.binary_dilation(holes, iterations=m))

    xs = -HALF + np.arange(N) * STEP
    ponds = []
    plab, pn = ndi.label(water)
    for k in range(1, pn + 1):
        cells = plab == k
        zz, xx = np.nonzero(cells)
        level = float(np.median(raw[cells]))
        cx_, cz_ = xs[xx].mean(), xs[zz].mean()
        near = np.argmin((xs[xx] - cx_) ** 2 + (xs[zz] - cz_) ** 2)  # the water cell nearest the centre, so a curved pond still seeds inside itself
        ponds.append(dict(x=float(xs[xx][near]), z=float(xs[zz][near]), area=float(cells.sum() * STEP * STEP),
                          bbox=[float(xs[xx].min()), float(xs[xx].max()), float(xs[zz].min()), float(xs[zz].max())],
                          level=level, spread=float(np.percentile(raw[cells], 90) - np.percentile(raw[cells], 10))))
    zz, xx = np.nonzero(oak)
    oak_info = dict(x=float(xs[xx].mean()), z=float(xs[zz].mean()), cells=int(oak.sum()), top=float(raw[oak].max()))
    ground_mean = float(ground[~oak_grown & ~water].mean())
    print("ground mean %.2f (relief 5-95%%: %.2f..%.2f)" % (ground_mean, *np.percentile(ground[~water], [5, 95])))
    print("ponds:", json.dumps(ponds, indent=1))
    print("oak:", oak_info, "footprint circles:", len(circles(oak, 0.5 * STEP + 0.2)))
    print("props: %d cells in %d clumps, %d circles" % (props.sum(), ndi.label(props)[1], len(circles(props, 0.0))))

    if args.json:
        with open(args.json, "w") as f:
            json.dump(dict(ponds=ponds, oak=oak_info, ground_mean=ground_mean,
                           oak_circles=[list(map(float, c)) for c in circles(oak, 0.5 * STEP + 0.2)],
                           prop_circles=[list(map(float, c)) for c in circles(props, 0.0)]), f)

    img = (np.clip(colour_filled, 0, 1) * 255).astype(np.uint8)
    for mask, rgb, alpha in ((water, (0, 90, 255), 0.55), (oak, (255, 30, 30), 0.6), (props, (255, 230, 0), 0.6)):
        for c in range(3):
            img[..., c] = np.where(mask, img[..., c] * (1 - alpha) + rgb[c] * alpha, img[..., c]).astype(np.uint8)
    Image.fromarray(img).resize((N * 4, N * 4), Image.NEAREST).save(args.overlay)


if __name__ == "__main__":
    main()
