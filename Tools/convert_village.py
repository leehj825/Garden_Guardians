"""Split the village asset sheet (one glb holding every building, crop and
accessory laid out on a board) into one mesh per item, for the game.

    pip install bpy numpy scipy
    python Tools/convert_village.py village_assets.glb Assets/Models/Props/Village.glb

Each item is cut out of the sheet (its connected pieces), decimated to its
own triangle budget, stood on y = 0 and centred on x = z = 0, and written as
three meshes (full, a quarter and a fourteenth of the triangles) of a single glb that shares the sheet's texture (stored as PNG: this
raylib build can't read the sheet's webp). The meshes are named 00_0_Name,
00_1_Name, 00_2_Name, 01_0_Name... so their order in the file is item*3+level, the VillageItem order times three, which is
also written, with each item's size, to Source/Engine/VillageItems.cs.

An item faces +Z (its door, or its front). The sheet's board is a
Blender-Z-up scene; the glb is Y-up.
"""
import argparse
import os

import bpy  # noqa: I001 (bpy first: it registers bmesh)
import bmesh
import numpy as np
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import connected_components

# name, (x, y) of the item's centre on the sheet (Blender axes), triangle budget
ITEMS = [
    ("Shield", (-0.445, 0.23), 3000),
    ("Tent", (-0.385, 0.075), 9000),
    ("AphidPen", (-0.31, -0.005), 8000),
    ("Burrow", (-0.28, 0.265), 7000),
    ("Granary", (-0.185, 0.23), 2500),
    ("ConstructionSite", (-0.11, -0.02), 6000),
    ("Palisade", (-0.095, 0.27), 7000),
    ("Twigs", (-0.095, -0.225), 3000),
    ("Cistern", (-0.09, 0.09), 5000),
    ("StoneFooting", (0.0, 0.295), 4000),
    ("Shrine", (0.0, 0.015), 7000),
    ("Hearth", (0.085, 0.295), 5000),
    ("Well", (0.09, 0.13), 8000),
    ("Poultice", (0.16, -0.335), 2500),
    ("WaterCup", (0.175, -0.205), 3000),
    ("CressPlot", (0.225, 0.03), 7000),
    ("GrainPlot", (0.23, 0.29), 7000),
    ("FoodSack", (0.265, -0.2), 3000),
    ("MushroomPlot", (0.39, 0.29), 6000),
    ("FishingRod", (0.39, -0.205), 3000),
]
# Pieces of the sheet that are not part of any item (a magnifying glass drawn beside the rod).
DROP_GREY = {"FishingRod"}
DROP_AFTER = {"FishingRod": lambda centroid, lo, hi: centroid[0] > lo[0] + 0.62 * (hi[0] - lo[0])}
# Flat items lying tilted on the sheet, turned to stand facing +Z (their thinnest direction becomes the front).
FACE_FRONT = {"Shield"}

# Items lying on their side on the sheet, turned upright: degrees about X.
TURN_UP = {"WaterCup": -90.0}


def load_sheet(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=path)
    ob = next(o for o in bpy.data.objects if o.type == "MESH")
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    me = ob.data
    me.calc_loop_triangles()
    co = np.empty(len(me.vertices) * 3, "f4")
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    tri = np.empty(len(me.loop_triangles) * 3, "i4")
    me.loop_triangles.foreach_get("vertices", tri)
    tri = tri.reshape(-1, 3)
    loops = np.empty(len(me.loop_triangles) * 3, "i4")
    me.loop_triangles.foreach_get("loops", loops)
    uvl = me.uv_layers.active.data
    uv = np.empty(len(uvl) * 2, "f4")
    uvl.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)[loops.reshape(-1, 3)]
    sheet = bpy.data.images[0]
    width, height = sheet.size
    pixels = np.empty(width * height * 4, "f4")
    sheet.pixels.foreach_get(pixels)
    image = bpy.data.images.new("sheet", width, height, alpha=False)  # a plain image: the sheet's webp can't be re-saved as PNG
    image.pixels.foreach_set(pixels)
    image.file_format = "PNG"  # this raylib build reads only PNG
    bpy.data.objects.remove(ob)
    return co, tri, uv, image


def split_pieces(co, tri):
    """Face -> piece id, where pieces are connected once vertices at one point are merged."""
    key = np.round(co * 1e5).astype(np.int64)
    _, inv = np.unique(key, axis=0, return_inverse=True)
    inv = inv.ravel()
    t = inv[tri]
    n = inv.max() + 1
    g = coo_matrix((np.ones(len(t) * 2), (np.r_[t[:, 0], t[:, 1]], np.r_[t[:, 1], t[:, 2]])), shape=(n, n))
    _, label = connected_components(g, directed=False)
    return label[t[:, 0]]


def build_object(name, co, tri, uv, image, tris):
    used, local = np.unique(tri, return_inverse=True)
    verts = co[used].astype(np.float64)
    if name.split("_", 1)[1] in FACE_FRONT:
        centred = verts - verts.mean(0)
        axis = np.linalg.eigh(np.cov(centred.T))[1][:, 0]  # thinnest direction
        target = np.array([0.0, -1.0, 0.0])  # Blender -Y is glTF +Z
        if axis @ target < 0:
            axis = -axis
        v = np.cross(axis, target)
        c = axis @ target
        k = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]])
        rot = np.eye(3) + k + k @ k / (1 + c)
        verts = centred @ rot.T
    turn = np.radians(TURN_UP.get(name.split("_", 1)[1], 0.0))
    if turn:
        c, s = np.cos(turn), np.sin(turn)
        verts = verts @ np.array([[1, 0, 0], [0, c, s], [0, -s, c]])
    faces = local.reshape(-1, 3)
    lo, hi = verts.min(0), verts.max(0)
    verts[:, 0] -= (lo[0] + hi[0]) / 2
    verts[:, 1] -= (lo[1] + hi[1]) / 2
    verts[:, 2] -= lo[2]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts.tolist(), [], faces.tolist())
    layer = mesh.uv_layers.new(name="UVMap")
    layer.data.foreach_set("uv", uv.reshape(-1))
    ob = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(ob)
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    if len(faces) > tris:
        mod = ob.modifiers.new("lod", "DECIMATE")
        mod.ratio = tris / len(faces)
        bpy.ops.object.modifier_apply(modifier=mod.name)
    for p in mesh.polygons:
        p.use_smooth = True
    mat = bpy.data.materials.get("sheet")
    if mat is None:
        mat = bpy.data.materials.new("sheet")
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes["Principled BSDF"]
        tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
        tex.image = image
        mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        bsdf.inputs["Roughness"].default_value = 0.9
    mesh.materials.append(mat)
    size = np.array(ob.dimensions)
    return ob, size


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("dst")
    ap.add_argument("--csharp", default=os.path.join(os.path.dirname(__file__), "..", "Source", "Engine", "VillageItems.cs"))
    args = ap.parse_args()

    co, tri, uv, image = load_sheet(args.src)
    piece = split_pieces(co, tri)
    ids = [p for p in np.unique(piece) if (piece == p).sum() > 2000]
    centres = {}
    for p in ids:
        v = co[np.unique(tri[piece == p])]
        centres[p] = (v.min(0) + v.max(0)) / 2
    taken = set()
    objects = []
    lods = []
    for index, (name, (cx, cy), budget) in enumerate(ITEMS):
        p = min((q for q in ids if q not in taken), key=lambda q: (centres[q][0] - cx) ** 2 + (centres[q][1] - cy) ** 2)
        taken.add(p)
        sel = piece == p
        t, u = tri[sel], uv[sel]
        drop = DROP_AFTER.get(name)
        if drop:
            v = co[np.unique(t)]
            lo, hi = v.min(0), v.max(0)
            keep = ~np.array([drop(co[f].mean(0), lo, hi) for f in t])
            t, u = t[keep], u[keep]
        if name in DROP_GREY:
            # Grey-white metal left over from a picture drawn beside the item (the magnifying glass by the rod).
            w, h = image.size
            pix = np.empty(w * h * 4, "f4")
            image.pixels.foreach_get(pix)
            pix = pix.reshape(h, w, 4)
            centre = u.mean(1)
            rgb = pix[np.clip((centre[:, 1] * h).astype(int), 0, h - 1), np.clip((centre[:, 0] * w).astype(int), 0, w - 1), :3]
            grey = (rgb.max(1) - rgb.min(1) < 0.09) & (rgb.mean(1) > 0.55)
            t, u = t[~grey], u[~grey]
            # …and keep only the biggest connected piece that is left.
            leftover = split_pieces(co, t)
            biggest = np.bincount(leftover).argmax()
            t, u = t[leftover == biggest], u[leftover == biggest]
        ob, size = build_object("%02d_%s" % (index, name), co, t, u, image, budget)
        objects.append((name, ob, size))
        ob.name = "%02d_0_%s" % (index, name)
        for lod, share in ((1, 4), (2, 14)):
            # Cheaper copies for when the item is far away or small on screen.
            copy = ob.copy()
            copy.data = ob.data.copy()
            copy.name = "%02d_%d_%s" % (index, lod, name)
            bpy.context.collection.objects.link(copy)
            bpy.context.view_layer.objects.active = copy
            target = max(300, budget // share)
            if len(copy.data.polygons) > target:
                mod = copy.modifiers.new("lod", "DECIMATE")
                mod.ratio = target / len(copy.data.polygons)
                bpy.ops.object.modifier_apply(modifier=mod.name)
            lods.append(copy)
        print(name, "piece", p, "size", size.round(3), "tris", len(ob.data.polygons))

    if image.size[0] > 1024:
        image.scale(1024, 1024)
    image.pack()
    bpy.ops.object.select_all(action="DESELECT")
    for _, ob, _ in objects:
        ob.select_set(True)
    for ob in lods:
        ob.select_set(True)
    bpy.ops.export_scene.gltf(filepath=args.dst, export_format="GLB", use_selection=True, export_yup=True, export_image_format="AUTO", export_apply=True)

    lines = [
        "// <auto-generated> by Tools/convert_village.py from the village asset sheet. Do not edit by hand.",
        "namespace GardenGuardians;",
        "",
        "/// <summary>The items of Village.glb, in file order (see <see cref=\"VillageModels\"/>).</summary>",
        "public enum VillageItem",
        "{",
    ] + ["    %s," % n for n, _, _ in objects] + [
        "}",
        "",
        "public static class VillageItems",
        "{",
        "    /// <summary>Each item's width (the larger of its x and z extents) and height, in the glb's own units.</summary>",
        "    public static readonly (float Width, float Height)[] Sizes =",
        "    {",
    ] + ["        (%.4ff, %.4ff), // %s" % (max(s[0], s[1]), s[2], n) for n, _, s in objects] + [
        "    };",
        "}",
        "",
    ]
    with open(args.csharp, "w") as f:
        f.write("\n".join(lines))
    print("wrote", args.dst, args.csharp)


if __name__ == "__main__":
    main()
