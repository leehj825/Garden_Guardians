"""Rig a static Tripo Bramblekin (about 1,000 triangles) to the game's skeleton, with no Blender.

    pip install numpy pillow
    python3 Tools/convert_tripo_kin.py Tools/kin_src/Male.glb   Tools/kin_src/Walking_skeleton.glb Assets/Models/Bramblekin/Walking.glb
    python3 Tools/convert_tripo_kin.py Tools/kin_src/Female.glb Tools/kin_src/Walking_skeleton.glb Assets/Models/Bramblekin/Walking_female.glb --female

What convert_female.py does for a decimated mesh, done directly: the model (Y-up, facing +Z, about 0.95-0.98 tall) is scaled to the
skeleton's height of 1, turned into its bind space (Z-up, facing -Y), skinned to its 33 joints by distance to the bones, and
written as the skeleton's glb with the mesh, weights and texture replaced, so every animation clip fits it. The texture (a
4096 px picture) is shrunk to --texture px and stored as PNG (this raylib build reads no embedded JPEG).

  --female   her arms hang at her sides where the skeleton's stick out (FITTED_ARM), and her long hair is weighted to the head and
             spine only, so it does not swing with her arms or legs when she walks.
  --lods     also write <name>_lod1.glb and <name>_lod2.glb: the same mesh with a 1024 px and a 512 px texture.
"""
import argparse
import io
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(__file__))
from convert_female import (EPSILON, FITTED_ARM, NEIGHBOURS, POWER, accessor, bind_joints, build, point_segment_distance,  # noqa: E402
                            read_glb, segments, write_glb)

Image.MAX_IMAGE_PIXELS = None
ARM_RADIUS = 0.07      # beyond this far from every arm bone a vertex is not arm (hair, skirt)
LEG_TOP = 0.28         # above this height (bind space) nothing follows the legs (hair, dress)


def load_static(path):
    doc, binary = read_glb(path)
    prim = doc["meshes"][0]["primitives"][0]
    at = prim["attributes"]
    pos = accessor(doc, binary, at["POSITION"]).astype(np.float64)
    nrm = accessor(doc, binary, at["NORMAL"]).astype(np.float64)
    uv = accessor(doc, binary, at["TEXCOORD_0"]).astype(np.float32)
    idx = accessor(doc, binary, prim["indices"]).astype(np.uint32)
    view = doc["bufferViews"][doc["images"][0]["bufferView"]]
    offset = view.get("byteOffset", 0)
    picture = Image.open(io.BytesIO(binary[offset:offset + view["byteLength"]])).convert("RGB")
    return pos, nrm, uv, idx, picture


def png_of(picture, size):
    buf = io.BytesIO()
    picture.resize((size, size), Image.LANCZOS).save(buf, "PNG", optimize=True)
    return buf.getvalue()


def skin(points, names, segs, female):
    owners = list(segs)
    dist = np.stack([np.min([point_segment_distance(points, a, b) for a, b in segs[n]], axis=0) for n in owners], axis=1)
    if female:
        arm = np.array([any(k in n for k in ("Shoulder", "Arm", "Hand")) for n in owners])
        leg = np.array([any(k in n for k in ("UpLeg", "Leg", "Foot", "Toe")) for n in owners])
        not_arm = dist[:, arm].min(axis=1) > ARM_RADIUS
        dist[np.ix_(not_arm, np.where(arm)[0])] = np.inf
        high = points[:, 2] > LEG_TOP
        dist[np.ix_(high, np.where(leg)[0])] = np.inf
    order = np.argsort(dist, axis=1)[:, :NEIGHBOURS]
    d = np.take_along_axis(dist, order, axis=1)
    w = 1.0 / (d + EPSILON) ** POWER
    w[~np.isfinite(d)] = 0.0
    w /= w.sum(axis=1, keepdims=True)
    index = np.array([names.index(owners[k]) for k in range(len(owners))])[order]
    return index.astype("u1"), w.astype("<f4")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("model")
    ap.add_argument("skeleton")
    ap.add_argument("dst")
    ap.add_argument("--female", action="store_true")
    ap.add_argument("--texture", type=int, default=2048)
    ap.add_argument("--lods", action="store_true")
    args = ap.parse_args()

    pos_y, nrm_y, uv, idx, picture = load_static(args.model)
    if len(pos_y) > 65535:
        sys.exit("%d vertices: raylib meshes take 16-bit indices" % len(pos_y))
    scale = 1.0 / pos_y[:, 1].max()  # to the skeleton's height of 1 (feet at 0)

    def to_bind(a, s=1.0):  # Y-up facing +Z  ->  Z-up facing -Y
        return np.stack([a[:, 0], -a[:, 2], a[:, 1]], axis=1) * s

    pos = to_bind(pos_y, scale)
    nrm = to_bind(nrm_y)

    doc, binary = read_glb(args.skeleton)
    names, position, parent = bind_joints(doc, binary)
    segs = segments(names, position, parent, FITTED_ARM if args.female else {})
    joints_idx, w = skin(pos, names, segs, args.female)
    joints4 = np.zeros((len(pos), 4), "u1")
    weights4 = np.zeros((len(pos), 4), "<f4")
    joints4[:, :NEIGHBOURS] = joints_idx
    weights4[:, :NEIGHBOURS] = w

    outputs = [(args.dst, args.texture)]
    if args.lods:
        stem = args.dst[:-4]
        outputs += [(stem + "_lod1.glb", 1024), (stem + "_lod2.glb", 512)]
    for path, size in outputs:
        d, b = read_glb(args.skeleton)
        d, out = build(d, b, pos.astype("<f4"), nrm.astype("<f4"), uv, idx.reshape(-1), joints4, weights4, png_of(picture, size))
        d["images"][0]["name"] = "Bramblekin_" + ("female" if args.female else "male") + "_basecolor"
        write_glb(path, d, out)
        print("wrote", path, len(pos), "vertices,", len(idx) // 3, "triangles,", size, "px texture")


if __name__ == "__main__":
    main()
