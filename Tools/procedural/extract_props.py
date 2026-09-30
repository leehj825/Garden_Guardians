"""Cut the oak, boulders and plant clumps out of a terrain model as a prop kit.

    python Tools/procedural/extract_props.py terrain3.glb OUT_PREFIX [--texture 1024]

Writes OUT_PREFIX.glb (every prop a separate mesh, origin at its base centre on the ground, all sharing one
shrunken copy of the model's texture) and OUT_PREFIX.json (each prop's name, kind, size and footprint
circles relative to its origin). The props are found the way detect_terrain.py finds what walkers must
avoid: the oak as the tall structure, rocks and plants as bumps on the ground.
"""
import argparse
import json
import math
import os
import sys

import bpy  # noqa: I001 (bpy first: it registers bmesh)
import bmesh
import numpy as np
from mathutils import Matrix
from scipy import ndimage as ndi

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
import convert_terrain as C  # noqa: E402
import detect_terrain as D  # noqa: E402

HALF, STEP, N = C.HALF, C.GRID_STEP, C.GRID_N


def cell_of(x, z):
    return (np.clip(((z + HALF) / STEP).round().astype(int), 0, N - 1), np.clip(((x + HALF) / STEP).round().astype(int), 0, N - 1))


def grassy(colour, loose=False):
    """Lawn green (the ground's texture), as opposed to fern green (darker and bluer) or wood and stone.
    `loose` also takes the olive and yellow-green a lawn skirt shows in some models (still not wood: tan has less green than red)."""
    r, g, b = colour
    if loose:
        return g >= r * 0.93 and b < g * 0.75 and g > 0.30
    return g > r * 1.08 and g > b * 1.25 and g > 0.30


def components(faces):
    """Groups of faces that touch (share a vertex): a prop is one big group; stray shards are small ones."""
    parent = {}

    def find(a):
        while parent.setdefault(a, a) != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    for f in faces:
        ids = [v.index for v in f.verts]
        for other in ids[1:]:
            parent[find(ids[0])] = find(other)
    groups = {}
    for f in faces:
        groups.setdefault(find(f.verts[0].index), []).append(f)
    return list(groups.values())


def classify(colour_mean):
    r, g, b = colour_mean
    if abs(r - g) < 0.06 and abs(g - b) < 0.08:
        return "rock"
    if g > r * 1.05 and g > b * 1.2:
        return "plant"
    return "wood"


def oak_measure(raw, ground, cx, cz):
    """The oak's trunk (centre relative to the prop's origin, radius, height) and where its hive can hang, measured the way convert_terrain.py does."""
    xs = -HALF + np.arange(N) * STEP
    trunk = ndi.binary_fill_holes((raw - ground) > 12.0)
    labels, n = ndi.label(trunk)
    sizes = ndi.sum(trunk, labels, range(1, n + 1))
    trunk = labels == (1 + int(np.argmax(sizes)))
    zz, xx = np.nonzero(trunk)
    tx, tz = float(xs[xx].mean()), float(xs[zz].mean())
    radius = math.sqrt(trunk.sum() * STEP ** 2 / math.pi)
    top = float(raw[trunk].max())
    base = float(ground[int(round((tz + HALF) / STEP)), int(round((tx + HALF) / STEP))])

    def cell(x, z):
        return int(round((z + HALF) / STEP)), int(round((x + HALF) / STEP))

    best = None
    for k in range(72):
        angle = k * math.tau / 72
        dx, dz = math.cos(angle), math.sin(angle)
        surface = 0.0
        for r in np.arange(0.0, 14.0, STEP):
            j, i = cell(tx + dx * r, tz + dz * r)
            if raw[j, i] - ground[j, i] > 2.6:
                surface = r
        j, i = cell(tx + dx * (surface + 1.0), tz + dz * (surface + 1.0))
        if raw[j, i] - ground[j, i] < 0.6 and (best is None or surface < best[0]):  # no root there: the nearest bare trunk
            best = (surface, angle)
    surface, angle = best if best else (radius, 0.0)
    return dict(trunk_dx=tx - cx, trunk_dz=tz - cz, trunk_radius=float(radius), trunk_height=float(top - base), hive_angle=float(angle), hive_surface=float(surface))


def export_prop(obj, full, path, max_side):
    """Write `obj` as its own glb with just the part of the model's texture it uses (padded, and shrunk to max_side), UVs remapped to it."""
    from PIL import Image
    mesh = obj.data
    layer = mesh.uv_layers.active
    uvs = np.array([loop.uv[:] for loop in layer.data])
    height, width = full.shape[:2]
    pad = 6
    x0, x1 = max(0, int(math.floor(uvs[:, 0].min() * width)) - pad), min(width, int(math.ceil(uvs[:, 0].max() * width)) + pad)
    y0, y1 = max(0, int(math.floor(uvs[:, 1].min() * height)) - pad), min(height, int(math.ceil(uvs[:, 1].max() * height)) + pad)
    crop = (np.clip(full[y0:y1, x0:x1, :3], 0, 1) * 255).astype(np.uint8)[::-1]  # Blender's rows run bottom-up, a PNG's top-down
    img = Image.fromarray(crop)
    scale = min(1.0, max_side / max(img.size))
    if scale < 1.0:
        img = img.resize((max(4, round(img.width * scale)), max(4, round(img.height * scale))), Image.LANCZOS)
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    tmp = path + ".png"
    img.save(tmp)
    cw, ch = x1 - x0, y1 - y0
    for loop in layer.data:
        loop.uv = ((loop.uv.x * width - x0) / cw, (loop.uv.y * height - y0) / ch)
    material = bpy.data.materials.new(obj.name)
    material.use_nodes = True
    texture = material.node_tree.nodes.new("ShaderNodeTexImage")
    texture.image = bpy.data.images.load(tmp)
    texture.image.pack()
    material.node_tree.links.new(texture.outputs["Color"], material.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
    mesh.materials.clear()
    mesh.materials.append(material)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_yup=True, export_image_format="AUTO", export_apply=True)
    os.remove(tmp)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("prefix")
    ap.add_argument("--texture", type=int, default=1024)
    ap.add_argument("--glb-dir", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "Models", "Procedural", "props"),
                    help="where each prop's own glb (with its own cropped texture) is written")
    args = ap.parse_args()

    ob = D.load(args.src)
    raw, colour = D.sample(ob)
    raw = C.fill_nan(raw)
    oak = C.oak_mask(raw)
    oak_grown = ndi.binary_dilation(oak, iterations=3)
    ground = raw.copy()
    ground[oak_grown] = np.nan
    ground = C.fill_nan(ground)
    for _ in range(40):
        blur = ndi.uniform_filter(ground, 3)
        ground[oak_grown] = blur[oak_grown]
    base = ndi.uniform_filter(ndi.grey_opening(ground, size=(9, 9)), 5)  # the ground itself, without the small bumps on it

    water = D.water_mask(colour, raw)
    bumps = ((raw - ndi.grey_opening(raw, size=(7, 7))) > C.PROP_HEIGHT) & ~ndi.binary_dilation(oak, iterations=6) & ~ndi.binary_erosion(water, iterations=2)
    bumps = ndi.binary_opening(bumps, iterations=1)
    lab, n = ndi.label(ndi.binary_dilation(bumps, iterations=1))
    sizes = ndi.sum(bumps, lab, range(1, n + 1))
    edge = np.zeros_like(bumps)
    m = int(D.EDGE_MARGIN / STEP)
    edge[:m, :] = edge[-m:, :] = edge[:, :m] = edge[:, -m:] = True
    clumps = []
    for k in range(1, n + 1):
        region = ndi.binary_dilation(lab == k, iterations=2)
        if sizes[k - 1] >= C.PROP_MIN_CELLS and not (region & edge).any():
            clumps.append(region)

    regions = [("oak", ndi.binary_dilation(oak, iterations=2), 0.15, ground)] + [("clump", c, 0.08, base) for c in clumps]

    # Which faces belong to which prop: the face's centre is inside its region and stands above the ground there.
    me = ob.data
    bm = C.bmesh_of(ob)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.faces.ensure_lookup_table()
    centres = np.array([f.calc_center_median()[:] for f in bm.faces])
    jj, ii = cell_of(centres[:, 0], -centres[:, 1])
    owner = np.full(len(centres), -1)
    for index, (kind, region, above, floor) in enumerate(regions):
        inside = region[jj, ii] & (centres[:, 2] - floor[jj, ii] > above) & (owner < 0)
        owner[inside] = index
    print("%d props: %s" % (len(regions), ", ".join("%d faces" % (owner == i).sum() for i in range(len(regions)))))

    image = next(i for i in bpy.data.images if i.size[0] > 0)
    full = np.array(image.pixels[:], dtype=np.float32).reshape(image.size[1], image.size[0], 4)  # full resolution, kept for each prop's own crop
    if image.size[0] > args.texture:
        image.scale(args.texture, args.texture)
    image.pack()
    pixels = np.array(image.pixels[:], dtype=np.float32).reshape(image.size[1], image.size[0], 4)

    items = []
    objects = []
    uv_layer = bm.loops.layers.uv.active
    xs = -HALF + np.arange(N) * STEP
    def face_colour(face):
        u, w = face.loops[0][uv_layer].uv
        return pixels[min(pixels.shape[0] - 1, int(w * pixels.shape[0])), min(pixels.shape[1] - 1, int(u * pixels.shape[1])), :3]

    bm.normal_update()
    for index, (kind, region, above, floor) in enumerate(regions):
        picked = [f for f, o in zip(bm.faces, owner) if o == index]
        # The skirt of lawn round a prop's foot (up-facing, low, lawn-coloured) is ground, not prop.
        picked = [f for f in picked
                  if not (f.normal.z > 0.7 and f.calc_center_median().z - floor[cell_of(np.array([f.calc_center_median().x]), np.array([-f.calc_center_median().y]))][0] < 0.8
                          and grassy(face_colour(f), loose=(kind == "oak")))]
        # Drop stray shards: pieces not joined to the prop that are small (under 0.8 m across) or a sliver of it.
        groups = components(picked)
        if not groups:
            continue
        largest = max(len(g) for g in groups)
        kept = []
        for group in groups:
            pts = np.array([v.co[:] for f in group for v in f.verts])
            extent = float((pts.max(axis=0) - pts.min(axis=0)).max())
            if len(group) == largest or (extent >= 0.8 and len(group) >= max(12, 0.04 * largest)):
                kept.extend(group)
        picked = kept
        if len(picked) < 40:
            continue
        zz, xx = np.nonzero(region & (np.abs(raw - floor) > above))
        cx, cz = float(xs[xx].mean()), float(xs[zz].mean())
        cj, ci = cell_of(np.array([cx]), np.array([cz]))
        base_y = float(floor[cj[0], ci[0]])
        # A new mesh from those faces, moved so its origin sits at the base centre.
        piece = bmesh.new()
        piece_uv = piece.loops.layers.uv.new(uv_layer.name)
        mapping = {}
        colours = []
        for f in picked:
            verts = []
            for v in f.verts:
                if v.index not in mapping:
                    mapping[v.index] = piece.verts.new((v.co.x - cx, v.co.y + cz, v.co.z - base_y))
                verts.append(mapping[v.index])
            face = piece.faces.new(verts)
            for loop, src in zip(face.loops, f.loops):
                loop[piece_uv].uv = src[uv_layer].uv
            u, w = f.loops[0][uv_layer].uv
            colours.append(pixels[min(pixels.shape[0] - 1, int(w * pixels.shape[0])), min(pixels.shape[1] - 1, int(u * pixels.shape[1])), :3])
        name = "oak" if kind == "oak" else None
        cls = "oak" if kind == "oak" else classify(np.mean(colours, axis=0))
        name = "%s_%d" % (cls, len([i for i in items if i["kind"] == cls]))
        mesh = bpy.data.meshes.new(name)
        piece.to_mesh(mesh)
        for material in me.materials:
            mesh.materials.append(material)
        piece.free()
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(obj)
        objects.append(obj)
        pts = np.array([v.co[:] for v in mesh.vertices])
        footprint = (np.abs(raw - floor) > above) & region
        circles = [(x - cx, z - cz, r) for x, z, r in C.oak_circles(ground, footprint, min_radius=(0.5 * STEP + 0.2) if cls == "oak" else 0.0)]
        item = dict(name=name, kind=cls, source=os.path.basename(args.src), x=cx, z=cz, base=base_y,
                    height=float(pts[:, 2].max()), radius=float(np.hypot(pts[:, 0], pts[:, 1]).max()), circles=[list(map(float, c)) for c in circles])
        if cls == "oak":
            item.update(oak_measure(raw, ground, cx, cz))
        if cls == "oak" or (cls in ("rock", "plant") and item["height"] >= 1.5):
            file = "%s_%s.glb" % (os.path.splitext(os.path.basename(args.src))[0], name)
            export_prop(obj, full, os.path.join(args.glb_dir, file), 1024 if cls == "oak" else 512)
            item["file"] = "props/" + file
        items.append(item)
    bm.free()

    os.makedirs(os.path.dirname(os.path.abspath(args.prefix)), exist_ok=True)
    with open(args.prefix + ".json", "w") as f:
        json.dump(items, f, indent=1)
    print("wrote", args.prefix + ".json;", len([i for i in items if "file" in i]), "prop models in", os.path.abspath(args.glb_dir), ";",
          ", ".join("%d %s" % (sum(1 for i in items if i["kind"] == k and "file" in i), k) for k in ("oak", "rock", "plant")))


if __name__ == "__main__":
    main()
