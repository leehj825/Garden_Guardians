"""Convert the 3D terrain model (a 100 m square of grass with a hollow oak trunk
and two ponds) into the game's ground: a glb to draw, and a C# height grid to
walk on.

    pip install bpy numpy scipy pillow
    python Tools/convert_terrain.py Terrain.glb Assets/Models/Terrain/terrain.glb
    python Tools/convert_terrain.py Terrain.glb out.glb --tris 150000 --texture 2048

What it does, in order:
  1. imports the model (glb, or fbx), applies its transforms and scales it
     x100 (the model is 1 unit wide, the garden 100 m), Y-up in the output;
  2. deletes the downward-facing faces of the slab's underside (the model is
     a solid block; only its top surface is the ground) but keeps the ones
     high up, on the hollow trunk and the roots, so they are not see-through;
  3. gives both ponds one water level: the model's higher pond (about 7.3 m)
     has its basin lowered to the lower one's (about 3.3 m), and ground that
     would otherwise fall below that level (the map's edges, gullies) is
     lifted just above it, so only the two ponds hold water;
  4. re-centres the height on the mean ground (the oak excluded), so the
     ground sits about y = 0 as it did before, and flattens the hills and
     hollows to RELIEF of their height (the oak, roots, reeds and stones keep
     theirs, standing on the gentler ground);
  5. decimates to --tris triangles, shrinks the texture to --texture px (and stores it as PNG: this raylib build can't read a
     glb's embedded JPEG), and
     splits the mesh into pieces of under 65,536 vertices (raylib's limit);
  6. samples the finished mesh into a height grid (the oak's trunk and roots
     painted out) and writes it, with the pond level and the oak's trunk and
     roots, as C# (Source/World/TerrainData.cs) so headless and Android
     builds need no model to walk on.
"""
import argparse
import math
import os
import sys

import bpy  # noqa: I001 (bpy first: it registers bmesh)
import bmesh
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
from scipy import ndimage as ndi

HALF = 50.0
GRID_STEP = 0.5  # metres between height-grid samples
GRID_N = int(2 * HALF / GRID_STEP) + 1

# Where the two ponds are in the model (metres, x/z of the game; before re-centring).
POND_HIGH = dict(seed=(-12.0, -8.0), box=(-26.0, 18.0, -20.0, 6.0), shore=7.35)
POND_LOW = dict(seed=(29.0, 31.0), box=(14.0, 44.0, 16.0, 46.0), radius=11.5, shore=3.3)
SHARED_LEVEL = 3.3
DISH = 0.5  # extra depth at the lowered pond's centre, so a drought leaves a puddle
BANK = 10.0  # the lowered basin's bank eases back to the old ground over this far
PROP_HEIGHT = 0.4  # a bump this much (m) above the ground round it is a reed clump or a boulder
PROP_MIN_CELLS = 3
RELIEF = 0.65  # the ground's hills and hollows are scaled to this; the oak, reeds and stones keep their height
GUARD = 0.6  # ground is kept at least this far above the water
SLAB_BOTTOM = 1.0  # downward faces below this (m) are the slab's underside; higher ones (the hollow trunk's, the roots') are kept


def smoothstep(t):
    t = np.clip(t, 0.0, 1.0)
    return t * t * (3 - 2 * t)


def sample_grid(arr, x, z):
    """Bilinear sample of a GRID_N x GRID_N array (rows = z, cols = x) at game x, z."""
    fx = np.clip((x + HALF) / GRID_STEP, 0, GRID_N - 1.001)
    fz = np.clip((z + HALF) / GRID_STEP, 0, GRID_N - 1.001)
    ix, iz = fx.astype(int), fz.astype(int)
    tx, tz = fx - ix, fz - iz
    return (arr[iz, ix] * (1 - tx) * (1 - tz) + arr[iz, ix + 1] * tx * (1 - tz) +
            arr[iz + 1, ix] * (1 - tx) * tz + arr[iz + 1, ix + 1] * tx * tz)


def import_model(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if path.lower().endswith(".fbx"):
        bpy.ops.import_scene.fbx(filepath=path)
    else:
        bpy.ops.import_scene.gltf(filepath=path)
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return ob


def grid_of(bm, top=1000.0):
    """Height of the top surface on the game grid (rows = game z, cols = x); nan where there is none."""
    bvh = BVHTree.FromBMesh(bm)
    out = np.full((GRID_N, GRID_N), np.nan)
    down = Vector((0, 0, -1))
    for j in range(GRID_N):
        z = -HALF + j * GRID_STEP
        for i in range(GRID_N):
            hit = bvh.ray_cast(Vector((-HALF + i * GRID_STEP, -z, top)), down)  # Blender y = -game z
            if hit[0] is not None:
                out[j, i] = hit[0].z
    return out


def fill_nan(h):
    idx = ndi.distance_transform_edt(np.isnan(h), return_distances=False, return_indices=True)
    return h[tuple(idx)]


def oak_mask(h):
    """Cells the oak (trunk and roots) stands on: well above the ground round them."""
    opened = ndi.grey_opening(h, size=(21, 21))
    mask = (h - opened) > 1.5
    labels, n = ndi.label(ndi.binary_dilation(mask, iterations=2))
    if n == 0:
        return mask
    sizes = ndi.sum(labels > 0, labels, range(1, n + 1))
    return labels == (1 + int(np.argmax(sizes)))


def basin(h, pond):
    """The pond's basin: the cells below its shore level, joined to its seed, within its box."""
    x0, x1, z0, z1 = pond["box"]
    xs = -HALF + np.arange(GRID_N) * GRID_STEP
    inbox = (xs[None, :] >= x0) & (xs[None, :] <= x1) & (xs[:, None] >= z0) & (xs[:, None] <= z1)
    sx, sz = pond["seed"]
    if "radius" in pond:  # a round pond: don't follow a gully out of it
        inbox &= (xs[None, :] - sx) ** 2 + (xs[:, None] - sz) ** 2 <= pond["radius"] ** 2
    labels, _ = ndi.label((h < pond["shore"]) & inbox)
    lab = labels[int(round((sz + HALF) / GRID_STEP)), int(round((sx + HALF) / GRID_STEP))]
    if lab == 0:
        sys.exit("pond seed %s is not in a basin" % (pond["seed"],))
    return ndi.binary_fill_holes(labels == lab)


def signed_distance(mask):
    """Metres inside the mask (positive) or outside it (negative)."""
    return (ndi.distance_transform_edt(mask) - ndi.distance_transform_edt(~mask)) * GRID_STEP


def cover_holes(ob):
    """Extend the ground to the whole square. These models are a slab with a ragged rim, so along some edges (and in
    corners) there is nothing under a walker standing inside the 100 m square: fill those cells with a flat-ish
    patch at the nearest ground's height, textured with the nearest ground inside the rim (so not its dark edge)."""
    bm = bmesh_of(ob)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.faces.ensure_lookup_table()
    uv_layer = bm.loops.layers.uv.active
    bvh = BVHTree.FromBMesh(bm)
    height = np.full((GRID_N, GRID_N), np.nan)
    uv = np.zeros((GRID_N, GRID_N, 2))
    down = Vector((0, 0, -1))
    for j in range(GRID_N):
        z = -HALF + j * GRID_STEP
        for i in range(GRID_N):
            hit = bvh.ray_cast(Vector((-HALF + i * GRID_STEP, -z, 1000.0)), down)
            if hit[0] is None:
                continue
            face = bm.faces[hit[2]]
            w = mathutils_interpolate(face, hit[0])
            height[j, i] = hit[0].z
            uv[j, i] = (sum(wt * loop[uv_layer].uv.x for wt, loop in zip(w, face.loops)),
                        sum(wt * loop[uv_layer].uv.y for wt, loop in zip(w, face.loops)))
    hole = np.isnan(height)
    print("ground missing under %d of %d cells (%.0f m2); covering it" % (hole.sum(), hole.size, hole.sum() * GRID_STEP ** 2))
    if not hole.any():
        bm.free()
        return
    inner = ndi.binary_erosion(~hole, iterations=5)
    if not inner.any():
        inner = ~hole
    near_h = ndi.distance_transform_edt(hole, return_distances=False, return_indices=True)
    near_uv = ndi.distance_transform_edt(~inner, return_distances=False, return_indices=True)
    filled_h = height[tuple(near_h)]
    patch = bmesh.new()
    patch_uv = patch.loops.layers.uv.new("UVMap") if not bm.loops.layers.uv.keys() else patch.loops.layers.uv.new(bm.loops.layers.uv.active.name)
    for j in range(GRID_N - 1):
        for i in range(GRID_N - 1):
            if not (hole[j, i] or hole[j, i + 1] or hole[j + 1, i] or hole[j + 1, i + 1]):
                continue
            corners = [(j, i), (j, i + 1), (j + 1, i + 1), (j + 1, i)]
            verts = [patch.verts.new((-HALF + ci * GRID_STEP, -(-HALF + cj * GRID_STEP), filled_h[cj, ci])) for cj, ci in corners]
            face = patch.faces.new(verts)
            if face.normal.z < 0:
                face.normal_flip()
            for loop, (cj, ci) in zip(face.loops, corners):
                u, v = uv[tuple(near_uv[:, cj, ci])]
                loop[patch_uv].uv = (u, v)
    bm.free()
    added = bpy.data.meshes.new("patch")
    patch.to_mesh(added)
    patch.free()
    for mat in ob.data.materials:
        added.materials.append(mat)
    extra = bpy.data.objects.new("patch", added)
    bpy.context.collection.objects.link(extra)
    bpy.ops.object.select_all(action="DESELECT")
    extra.select_set(True)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.join()


def mathutils_interpolate(face, point):
    from mathutils.interpolate import poly_3d_calc
    return poly_3d_calc([v.co for v in face.verts], point)


def reshape_ponds(ob, h, ponds=None, shared=None):
    """Lower every pond above the shared level to it and keep all other ground dry. Returns the new pond masks."""
    ponds = ponds or [POND_HIGH, POND_LOW]
    shared = SHARED_LEVEL if shared is None else shared
    masks = [basin(h, pond) for pond in ponds]
    sds = [signed_distance(mask) for mask in masks]
    drop = np.zeros_like(h)
    for pond, sd in zip(ponds, sds):
        if pond["shore"] > shared + 0.05:  # a higher pond is lowered to the shared level (and dished a little at its centre)
            lower = smoothstep((sd + BANK) / BANK)  # 1 in the basin, easing to 0 over the bank
            drop = drop + (pond["shore"] - shared) * lower + DISH * smoothstep(sd / 5.0)
    near_pond = smoothstep((np.max(sds, axis=0) + 4.0) / 3.0)  # 1 within a metre of a basin

    me = ob.data
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    x, z_game, y = co[:, 0], -co[:, 1], co[:, 2]
    y = y - sample_grid(drop, x, z_game)
    floor = shared + GUARD
    k = 0.5
    soft = floor + 0.5 * ((y - floor) + np.sqrt((y - floor) ** 2 + k * k))  # a smooth max(y, floor)
    keep = sample_grid(near_pond, x, z_game)
    co[:, 2] = y * keep + soft * (1 - keep)
    me.vertices.foreach_set("co", co.ravel())
    me.update()
    return masks


def keep_dry(ob, masks, level, guard=0.5):
    """After flattening, lift any ground away from the ponds (the map's edges, gullies) to at least `guard` above the water, so only the ponds hold water."""
    near_pond = smoothstep((np.max([signed_distance(mask) for mask in masks], axis=0) + 4.0) / 3.0)
    me = ob.data
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    y = co[:, 2]
    floor = level + guard
    soft = floor + 0.5 * ((y - floor) + np.sqrt((y - floor) ** 2 + 0.25))
    keep = sample_grid(near_pond, co[:, 0], -co[:, 1])
    co[:, 2] = y * keep + soft * (1 - keep)
    me.vertices.foreach_set("co", co.ravel())
    me.update()


def flatten_relief(ob, ground_grid, k):
    """Scale the ground's hills and hollows by k, keeping the oak, roots, reeds and stones their full height above it."""
    mask = oak_mask(ground_grid)
    grown = ndi.binary_dilation(mask, iterations=3)
    base = ground_grid.copy()
    base[grown] = np.nan
    base = fill_nan(base)
    for _ in range(40):
        blur = ndi.uniform_filter(base, 3)
        base[grown] = blur[grown]
    smooth = ndi.uniform_filter(ndi.grey_opening(base, size=(9, 9)), 5)  # the ground itself, without the small bumps on it
    me = ob.data
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    co[:, 2] -= (1.0 - k) * sample_grid(smooth, co[:, 0], -co[:, 1])
    me.vertices.foreach_set("co", co.ravel())
    me.update()


def bmesh_of(ob):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    return bm


def decimate(ob, tris):
    bm = bmesh_of(ob)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.to_mesh(ob.data)
    bm.free()
    have = len(ob.data.polygons)
    if have > tris:
        mod = ob.modifiers.new("lod", "DECIMATE")
        mod.ratio = tris / have
        bpy.context.view_layer.objects.active = ob
        bpy.ops.object.modifier_apply(modifier=mod.name)
    for p in ob.data.polygons:
        p.use_smooth = True


def split_pieces(ob, max_verts=45000):
    """Cut the mesh into a square of pieces, each under raylib's 65,536-vertex mesh limit."""
    n = max(1, math.ceil(math.sqrt(len(ob.data.vertices) / max_verts)))
    if n == 1:
        return [ob]
    pieces = []
    for i in range(n):
        for j in range(n):
            copy = ob.copy()
            copy.data = ob.data.copy()
            bpy.context.collection.objects.link(copy)
            bm = bmesh_of(copy)
            cell = 2 * HALF / n
            keep = []
            for f in bm.faces:
                c = f.calc_center_median()
                if min(n - 1, int((c.x + HALF) / cell)) != i or min(n - 1, int((-c.y + HALF) / cell)) != j:
                    keep.append(f)
            bmesh.ops.delete(bm, geom=keep, context="FACES")
            bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
            bm.to_mesh(copy.data)
            bm.free()
            if len(copy.data.polygons):
                pieces.append(copy)
            else:
                bpy.data.objects.remove(copy)
    bpy.data.objects.remove(ob)
    return pieces


def shrink_texture(size):
    for image in bpy.data.images:
        if image.size[0] > size:
            image.scale(size, size)
        image.pack()


def export(pieces, dst):
    bpy.ops.object.select_all(action="DESELECT")
    for o in pieces:
        o.select_set(True)
    bpy.ops.export_scene.gltf(
        filepath=dst, export_format="GLB", use_selection=True, export_yup=True, export_image_format="AUTO",
        export_apply=True)


def oak_circles(ground, mask, min_radius=0.5 * GRID_STEP + 0.2):
    """Circles covering the trunk and roots, largest first: a greedy cover of the mask by its distance transform."""
    dist = ndi.distance_transform_edt(mask) * GRID_STEP
    covered = np.zeros_like(mask)
    xs = -HALF + np.arange(GRID_N) * GRID_STEP
    gx, gz = np.meshgrid(xs, xs)
    circles = []
    while True:
        rest = np.where(covered, 0, dist)
        best = np.unravel_index(np.argmax(rest), rest.shape)
        radius = max(rest[best], 0.0)
        if radius <= min_radius:
            break
        cx, cz = xs[best[1]], xs[best[0]]
        r = radius + GRID_STEP * 0.5
        if min_radius == 0.0:
            r = max(r, 0.6)
        circles.append((cx, cz, r))
        covered |= (gx - cx) ** 2 + (gz - cz) ** 2 <= (r * 0.85) ** 2
    return circles


def build_ground_and_oak(final):
    """Height grid from the finished mesh, with the oak painted out; and the oak's shape."""
    bm = bmesh_of(final)
    raw = fill_nan(grid_of(bm))
    bm.free()
    mask = oak_mask(raw)
    grown = ndi.binary_dilation(mask, iterations=3)
    ground = raw.copy()
    ground[grown] = np.nan
    ground = fill_nan(ground)
    for _ in range(40):  # relax the painted-out patch into a smooth surface
        blur = ndi.uniform_filter(ground, 3)
        ground[grown] = blur[grown]
    # Reed clumps and boulders on the banks: bumps standing well above the ground round them.
    bumps = ((raw - ndi.grey_opening(raw, size=(7, 7))) > PROP_HEIGHT) & ~ndi.binary_dilation(mask, iterations=6)
    bumps = ndi.binary_opening(bumps, iterations=1)
    labels, n = ndi.label(ndi.binary_dilation(bumps, iterations=1))
    sizes = ndi.sum(bumps, labels, range(1, n + 1))
    props = np.isin(labels, [i + 1 for i, size in enumerate(sizes) if size >= PROP_MIN_CELLS]) & ndi.binary_dilation(bumps, iterations=1)
    return raw, ground, mask, props


def ponds_from_features(path):
    """Ponds of another terrain model, from detect_terrain's json: each with a seed, a box round it and a shore level a little above its water; the shared level is the lowest pond's."""
    import json
    with open(path) as f:
        found = json.load(f)["ponds"]
    ponds = []
    for pond in found:
        x0, x1, z0, z1 = pond["bbox"]
        ponds.append(dict(seed=(pond["x"], pond["z"]), box=(x0 - 6.0, x1 + 6.0, z0 - 6.0, z1 + 6.0), shore=pond["level"] + max(0.3, pond["spread"])))
    return ponds, min(p["level"] + max(0.3, p["spread"]) for p in found)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("dst")
    ap.add_argument("--scale", type=float, default=100.0)
    ap.add_argument("--tris", type=int, default=60000)
    ap.add_argument("--texture", type=int, default=2048)
    ap.add_argument("--relief", type=float, default=RELIEF, help="how much of the ground's hills and hollows to keep (0..1)")
    ap.add_argument("--features", help="ponds of a new model, from Tools/detect_terrain.py --json (default: the original model's two ponds)")
    ap.add_argument("--index", type=int, default=1, help="the terrain number this becomes (Terrain<N>.cs)")
    ap.add_argument("--model-file", help="the glb's name under Assets/Models/Terrain (default: the output's)")
    ap.add_argument("--csharp", default=os.path.join(os.path.dirname(__file__), "..", "Source", "World", "Terrains", "Terrain1.cs"))
    args = ap.parse_args()

    ob = import_model(args.src)
    ob.data.transform(Matrix.Scale(args.scale, 4))

    bm = bmesh_of(ob)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.normal.z < -0.1 and f.calc_center_median().z < SLAB_BOTTOM], context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(ob.data)
    bm.free()

    cover_holes(ob)
    ob = bpy.context.view_layer.objects.active

    bm = bmesh_of(ob)
    h0 = fill_nan(grid_of(bm))
    bm.free()
    ponds, shared = None, SHARED_LEVEL
    if args.features:
        ponds, shared = ponds_from_features(args.features)
    pond_masks = reshape_ponds(ob, h0, ponds, shared)

    bm = bmesh_of(ob)
    h1 = fill_nan(grid_of(bm))
    bm.free()
    mean_ground = float(h1[~ndi.binary_dilation(oak_mask(h1), iterations=3)].mean())
    ob.data.transform(Matrix.Translation((0, 0, -mean_ground)))
    level = shared - mean_ground
    flatten_relief(ob, h1 - mean_ground, args.relief)
    level *= args.relief
    keep_dry(ob, pond_masks, level)
    print("mean ground %.3f, shared pond level %.3f (after re-centring and flattening the relief to %.0f%%)" % (mean_ground, level, args.relief * 100))

    decimate(ob, args.tris)
    shrink_texture(args.texture)

    # The grid is sampled from the mesh that ships: one piece, before splitting.
    raw, ground, mask, props = build_ground_and_oak(ob)

    pieces = split_pieces(ob)
    os.makedirs(os.path.dirname(os.path.abspath(args.dst)), exist_ok=True)
    export(pieces, args.dst)
    print("wrote", args.dst, "in", len(pieces), "pieces,", sum(len(p.data.polygons) for p in pieces), "triangles")

    write_csharp(args.csharp, ground, raw, mask, level, props, args.index, args.model_file or os.path.basename(args.dst))


def creek_length(g, x, z):
    """How many one-metre steps the game's creek trace (WaterMap.TraceCreek) takes downhill from (x, z) on height grid g."""
    def height(px, pz):
        fx = min(max((px + HALF) / GRID_STEP, 0.0), GRID_N - 1.001)
        fz = min(max((pz + HALF) / GRID_STEP, 0.0), GRID_N - 1.001)
        ix, iz = int(fx), int(fz)
        tx, tz = fx - ix, fz - iz
        return (g[iz, ix] * (1 - tx) * (1 - tz) + g[iz, ix + 1] * tx * (1 - tz) +
                g[iz + 1, ix] * (1 - tx) * tz + g[iz + 1, ix + 1] * tx * tz)

    steps = 0
    for _ in range(60):
        e = 0.05
        sx = (height(x + e, z) - height(x - e, z)) / (2 * e)
        sz = (height(x, z + e) - height(x, z - e)) / (2 * e)
        norm = math.hypot(sx, sz)
        if norm < 0.02:
            break
        nx, nz = x - sx / norm, z - sz / norm
        if height(nx, nz) >= height(x, z) - 0.005 or abs(nx) > 46 or abs(nz) > 46:
            break
        x, z = nx, nz
        steps += 1
    return steps


def pick_spring(ground, level, oak_mask_):
    """Where the creek rises: in the map's outer band, well away from every pond and the oak, where the game's own trace (see creek_length) runs longest downhill."""
    xs = -HALF + np.arange(GRID_N) * GRID_STEP
    gx, gz = np.meshgrid(xs, xs)
    wet = ground < level
    far_water = ndi.distance_transform_edt(~wet) * GRID_STEP
    far_oak = ndi.distance_transform_edt(~oak_mask_) * GRID_STEP
    edge = np.maximum(np.abs(gx), np.abs(gz))
    ok = (edge >= 30.0) & (edge <= 45.0) & (far_water > 20.0) & (far_oak > 14.0)
    if not ok.any():
        ok = (edge >= 30.0) & (edge <= 45.0) & (far_water > 8.0)
    quantized = np.round(ground * 100) / 100  # the game reads the heights in whole centimetres
    best, best_score = None, -1.0
    for j, i in zip(*np.nonzero(ok)):
        if (i + j) % 3:  # every third cell is plenty
            continue
        steps = creek_length(quantized, xs[i], xs[j])
        score = steps + 0.001 * ground[j, i]  # the longer the run, the higher the ground on a tie
        if score > best_score:
            best, best_score = (float(xs[i]), float(xs[j]), steps), score
    print("creek: %d steps downhill from the spring" % best[2])
    return best[0], best[1]


def write_csharp(path, ground, raw, mask, level, props, index, model_file):
    import base64
    cm = np.round(ground * 100).astype("<i2")
    blob = base64.b64encode(cm.tobytes()).decode()

    # The trunk: cells more than 12 m above the ground round it, holes filled.
    trunk = ndi.binary_fill_holes((raw - ground) > 12.0)
    labels, n = ndi.label(trunk)
    sizes = ndi.sum(trunk, labels, range(1, n + 1))
    trunk = labels == (1 + int(np.argmax(sizes)))
    xs = -HALF + np.arange(GRID_N) * GRID_STEP
    zz, xx = np.nonzero(trunk)
    cx, cz = xs[xx].mean(), xs[zz].mean()
    radius = math.sqrt(trunk.sum() * GRID_STEP ** 2 / math.pi)
    top = float(raw[trunk].max())
    base = float(ground[int(round((cz + HALF) / GRID_STEP)), int(round((cx + HALF) / GRID_STEP))])
    print("oak at (%.2f, %.2f) radius %.2f, ground %.2f, top %.2f" % (cx, cz, radius, base, top))

    circles = oak_circles(ground, mask)

    # The hive hangs on the trunk 2.6 m up, where the trunk is bare of roots and
    # the ground beside it is dry: the direction whose trunk surface is nearest.
    def cell(x, z):
        return int(round((z + HALF) / GRID_STEP)), int(round((x + HALF) / GRID_STEP))

    best = None
    for k in range(72):
        angle = k * math.tau / 72
        dx, dz = math.cos(angle), math.sin(angle)
        surface = 0.0
        for r in np.arange(0.0, 14.0, GRID_STEP):
            j, i = cell(cx + dx * r, cz + dz * r)
            if raw[j, i] - ground[j, i] > 2.6:
                surface = r
        j, i = cell(cx + dx * (surface + 1.0), cz + dz * (surface + 1.0))
        clear = raw[j, i] - ground[j, i] < 0.6  # no root there
        if ground[j, i] > level + 0.8 and clear and (best is None or surface < best[0]):
            best = (surface, angle)
    hive_surface, hive_angle = best
    print("hive at angle %.2f rad, trunk surface %.2f m out" % (hive_angle, hive_surface))
    print(len(circles), "footprint circles")
    prop_circles = oak_circles(ground, props, min_radius=0.0)
    print(len(prop_circles), "prop circles")
    spring = pick_spring(ground, level, mask)
    print("creek spring at (%.1f, %.1f)" % spring)
    lines = [
        "// <auto-generated> by Tools/convert_terrain.py from the terrain model. Do not edit by hand.",
        "namespace GardenGuardians;",
        "",
        "/// <summary>Terrain %d (Assets/Models/Terrain/%s): its ground sampled onto a grid so headless and Android builds need no model to walk on (see <see cref=\"TerrainData\"/>).</summary>" % (index, model_file),
        "internal static class Terrain%d" % index,
        "{",
        "    public static TerrainSet Make() => new()",
        "    {",
        "        ModelFile = \"%s\"," % model_file,
        "        PondLevel = %.3ff," % level,
        "        OakX = %.2ff, OakZ = %.2ff," % (cx, cz),
        "        HiveAngle = %.3ff, HiveSurface = %.2ff," % (hive_angle, hive_surface),
        "        OakTrunkRadius = %.2ff, OakTrunkHeight = %.2ff," % (radius, top - base),
        "        SpringX = %.1ff, SpringZ = %.1ff," % spring,
        "        PropCircles = new[]",
        "        {",
    ] + ["            %.2ff, %.2ff, %.2ff," % c for c in prop_circles] + [
        "        },",
        "        OakCircles = new[]",
        "        {",
    ]
    for c in circles:
        lines.append("            %.2ff, %.2ff, %.2ff," % c)
    lines += [
        "        },",
        "        Encoded =",
    ]
    for i in range(0, len(blob), 120):
        lines.append('            "%s"%s' % (blob[i:i + 120], "," if i + 120 >= len(blob) else " +"))
    lines += [
        "    };",
        "}",
        "",
    ]
    with open(path, "w") as f:
        f.write("\n".join(lines))
    print("wrote", os.path.abspath(path))
    debug = os.environ.get("TERRAIN_DEBUG_DIR")
    if debug:
        np.save(os.path.join(debug, "ground.npy"), ground)
        np.save(os.path.join(debug, "raw.npy"), raw)
        np.save(os.path.join(debug, "oak.npy"), mask)
        np.save(os.path.join(debug, "props.npy"), props)


if __name__ == "__main__":
    main()
