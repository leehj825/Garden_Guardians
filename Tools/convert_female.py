"""Rig the static female Bramblekin model to the male's skeleton, so the same
animation clips drive both.

    pip install bpy numpy scipy pillow
    python Tools/convert_female.py female_bramblekin.glb Assets/Models/Bramblekin/Walking.glb \\
        Assets/Models/Bramblekin/Walking_female.glb

The female model has no skeleton. The clips (Walking.glb and the rest) are
baked for the male's, so the female mesh is:
  1. decimated (Blender) to --tris triangles, its texture shrunk to
     --texture px and stored as PNG (this raylib build reads no embedded JPEG);
  2. turned into the male's bind space (Z-up, facing -Y; the glb's nodes and
     inverse-bind matrices stay the male's);
  3. skinned to the male's 33 joints: each vertex is weighted to its four
     nearest bones, by distance to the bone segments (the arms are measured
     against a skeleton fitted to the female's arms, which hang at her sides
     where the male's stick out, so her skirt is not weighted to his hands);
  4. written as the male glb with its mesh, skin weights and texture replaced:
     same nodes, skin and animation, so any clip made for him fits her.
"""
import argparse
import io
import json
import struct
import sys
import tempfile
import os

import bpy  # noqa: I001 (bpy first: it registers the other Blender modules)
import numpy as np
from PIL import Image

# The female's arms hang at her sides; the male's angle out. For weighting only, her arm joints are
# placed where her arms are (bind space, metres): joint name -> position.
FITTED_ARM = {
    "Arm": (0.150, 0.004, 0.445),
    "ForeArm": (0.172, 0.004, 0.350),
    "Hand": (0.190, 0.004, 0.255),
}
FINGER = ("HandIndex",)
NEIGHBOURS = 4
POWER = 4.0
EPSILON = 0.01


def read_glb(path):
    data = open(path, "rb").read()
    length = struct.unpack("<I", data[12:16])[0]
    doc = json.loads(data[20:20 + length])
    binary = data[20 + length + 8:]
    return doc, binary


def accessor(doc, binary, index):
    a = doc["accessors"][index]
    view = doc["bufferViews"][a["bufferView"]]
    width = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}[a["type"]]
    dtype = {5126: "<f4", 5125: "<u4", 5123: "<u2", 5121: "u1"}[a["componentType"]]
    arr = np.frombuffer(binary, dtype, a["count"] * width, view.get("byteOffset", 0) + a.get("byteOffset", 0))
    return arr.reshape(-1, width) if width > 1 else arr


def decimate_female(src, tris, texture, out):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=src)
    ob = next(o for o in bpy.data.objects if o.type == "MESH")
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    have = len(ob.data.polygons)
    if have > tris:
        mod = ob.modifiers.new("lod", "DECIMATE")
        mod.ratio = tris / have
        bpy.ops.object.modifier_apply(modifier=mod.name)
    for image in bpy.data.images:
        if image.size[0] > texture:
            image.scale(texture, texture)
        image.pack()
    bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", export_yup=True, export_image_format="AUTO", use_selection=False)


def bind_joints(doc, binary):
    """Joint name -> bind position (bind space), and each joint's parent name, from the male glb."""
    skin = doc["skins"][0]
    ibm = accessor(doc, binary, skin["inverseBindMatrices"]).reshape(-1, 4, 4)
    names = [doc["nodes"][n]["name"].split(":")[-1] for n in skin["joints"]]
    position = {n: np.linalg.inv(m.T)[:3, 3] for n, m in zip(names, ibm)}
    parent = {}
    for n in skin["joints"]:
        for c in doc["nodes"][n].get("children", []):
            parent[doc["nodes"][c]["name"].split(":")[-1]] = doc["nodes"][n]["name"].split(":")[-1]
    return names, position, parent


def segments(names, position, parent, fitted):
    """Bone segments per joint: from the joint to each child that its own bone runs to."""
    pos = dict(position)
    for n, p in fitted.items():
        for side in ("Left", "Right"):
            sign = 1.0 if side == "Left" else -1.0
            pos[side + n] = np.array([p[0] * sign, p[1], p[2]])
    pos["LeftHand_tip"] = pos["LeftHand"] + (pos["LeftHand"] - pos["LeftForeArm"]) * 0.5
    pos["RightHand_tip"] = pos["RightHand"] + (pos["RightHand"] - pos["RightForeArm"]) * 0.5
    owned = {n: [] for n in names if not n.startswith(FINGER) and not any(f in n for f in FINGER)}
    for child, par in parent.items():
        if par not in owned:
            continue
        if any(f in child for f in FINGER):
            owned[par].append((pos[par], pos[par + "_tip"] if (par + "_tip") in pos else pos[child]))
        elif par == "Hips" and "UpLeg" in child:
            continue  # the pelvis owns its spine only; the legs own themselves
        elif "Shoulder" in child:
            continue  # the shoulder joint owns the shoulder itself
        else:
            owned[par].append((pos[par], pos[child]))
    for side in ("Left", "Right"):
        owned[side + "Shoulder"].append((pos[side + "Shoulder"], pos[side + "Arm"]))
    return {n: s for n, s in owned.items() if s}


def point_segment_distance(points, a, b):
    ab = b - a
    t = np.clip(((points - a) @ ab) / max(float(ab @ ab), 1e-9), 0.0, 1.0)
    return np.linalg.norm(points - (a + t[:, None] * ab), axis=1)


def skin_weights(points, names, segs):
    """Four nearest joints per point, weighted by 1 / (distance + eps)^p; returns (joint index, weight)."""
    owners = list(segs)
    dist = np.stack([np.min([point_segment_distance(points, a, b) for a, b in segs[n]], axis=0) for n in owners], axis=1)
    order = np.argsort(dist, axis=1)[:, :NEIGHBOURS]
    d = np.take_along_axis(dist, order, axis=1)
    w = 1.0 / (d + EPSILON) ** POWER
    w /= w.sum(axis=1, keepdims=True)
    index = np.array([names.index(owners[k]) for k in range(len(owners))])[order]
    return index.astype("u1"), w.astype("<f4")


def build(doc, binary, positions, normals, uv, indices, joints, weights, png):
    """The male glb with its mesh, weights and texture swapped for the female's."""
    keep = {0, 1, 2, 3, 4, 5, doc["images"][0]["bufferView"]}
    blobs = []

    def add(data, target=None):
        pad = (-sum(len(b) for b in blobs)) % 4
        if pad:
            blobs.append(b"\0" * pad)
        offset = sum(len(b) for b in blobs)
        blobs.append(bytes(data))
        view = {"buffer": 0, "byteOffset": offset, "byteLength": len(data)}
        if target:
            view["target"] = target
        return view

    old_views = doc["bufferViews"]
    new_views = [None] * len(old_views)
    order = []
    for i, v in enumerate(old_views):
        if i in keep:
            continue
        chunk = binary[v.get("byteOffset", 0):v.get("byteOffset", 0) + v["byteLength"]]
        view = add(chunk, v.get("target"))
        new_views[i] = view
    mesh_data = {
        0: positions.astype("<f4").tobytes(), 1: normals.astype("<f4").tobytes(), 2: uv.astype("<f4").tobytes(),
        3: joints.astype("u1").tobytes(), 4: weights.astype("<f4").tobytes(), 5: indices.astype("<u2").tobytes(),
    }
    targets = {0: 34962, 1: 34962, 2: 34962, 3: 34962, 4: 34962, 5: 34963}
    for i, data in mesh_data.items():
        new_views[i] = add(data, targets[i])
    new_views[doc["images"][0]["bufferView"]] = add(png)
    doc["bufferViews"] = new_views
    doc["buffers"] = [{"byteLength": sum(len(b) for b in blobs)}]
    n = len(positions)
    for i, a in enumerate(doc["accessors"][:6]):
        a.pop("max", None)
        a.pop("min", None)
    acc = doc["accessors"]
    for i in range(5):
        acc[i]["count"] = n
    acc[0]["max"] = positions.max(axis=0).tolist()
    acc[0]["min"] = positions.min(axis=0).tolist()
    acc[5]["count"] = len(indices)
    binary_out = b"".join(blobs)
    return doc, binary_out


def write_glb(path, doc, binary):
    text = json.dumps(doc, separators=(",", ":")).encode()
    text += b" " * ((-len(text)) % 4)
    binary += b"\0" * ((-len(binary)) % 4)
    total = 12 + 8 + len(text) + 8 + len(binary)
    with open(path, "wb") as f:
        f.write(struct.pack("<4sII", b"glTF", 2, total))
        f.write(struct.pack("<I4s", len(text), b"JSON") + text)
        f.write(struct.pack("<I4s", len(binary), b"BIN\0") + binary)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("female")
    ap.add_argument("male")
    ap.add_argument("dst")
    ap.add_argument("--tris", type=int, default=50000)
    ap.add_argument("--texture", type=int, default=2048)
    ap.add_argument("--dump", help="also write the skinned bind pose and weights here (.npz), for checking")
    args = ap.parse_args()

    tmp = os.path.join(tempfile.mkdtemp(), "female_lod.glb")
    decimate_female(args.female, args.tris, args.texture, tmp)

    fdoc, fbin = read_glb(tmp)
    prim = fdoc["meshes"][0]["primitives"][0]
    at = prim["attributes"]
    pos_y = accessor(fdoc, fbin, at["POSITION"]).astype(np.float64)
    nrm_y = accessor(fdoc, fbin, at["NORMAL"]).astype(np.float64)
    uv = accessor(fdoc, fbin, at["TEXCOORD_0"]).astype(np.float32)
    idx = accessor(fdoc, fbin, prim["indices"]).astype(np.uint32)
    if len(pos_y) > 65535:
        sys.exit("%d vertices: raise --tris pressure (raylib meshes take 16-bit indices)" % len(pos_y))
    img_view = fdoc["bufferViews"][fdoc["images"][0]["bufferView"]]
    png = fbin[img_view["byteOffset"]:img_view["byteOffset"] + img_view["byteLength"]]
    if not png.startswith(b"\x89PNG"):
        buf = io.BytesIO()
        Image.open(io.BytesIO(png)).convert("RGBA").save(buf, "PNG")
        png = buf.getvalue()

    # Y-up, facing +Z  ->  the male's bind space: Z-up, facing -Y (a quarter turn about X).
    def to_bind(a):
        return np.stack([a[:, 0], -a[:, 2], a[:, 1]], axis=1)

    pos = to_bind(pos_y)
    nrm = to_bind(nrm_y)

    doc, binary = read_glb(args.male)
    names, position, parent = bind_joints(doc, binary)
    segs = segments(names, position, parent, FITTED_ARM)
    joints_idx, w = skin_weights(pos, names, segs)
    joints4 = np.zeros((len(pos), 4), "u1")
    weights4 = np.zeros((len(pos), 4), "<f4")
    joints4[:, :NEIGHBOURS] = joints_idx
    weights4[:, :NEIGHBOURS] = w

    doc, out = build(doc, binary, pos.astype("<f4"), nrm.astype("<f4"), uv, idx.reshape(-1), joints4, weights4, png)
    doc["images"][0]["name"] = "Bramblekin_female_basecolor"
    write_glb(args.dst, doc, out)
    print("wrote", args.dst, len(pos), "vertices,", len(idx) // 3, "triangles")
    if args.dump:
        np.savez(args.dump, pos=pos, joints=joints4, weights=weights4, names=names)


if __name__ == "__main__":
    main()
