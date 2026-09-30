"""Split the Tripo wall sheet (Tools/kin_src/Walls.glb: one straight wall and two wall ends, laid out side by side) into
the three meshes the game builds a village wall from, with no Blender.

    pip install numpy pillow
    python3 Tools/convert_walls.py Tools/kin_src/Walls.glb Assets/Models/Props/Walls.glb --texture 2048

Writes one glb with three meshes that share one texture, in metres (the sheet scaled by SCALE), each standing on y = 0:
  0  the straight wall, centred on its origin, its length along x
  1  a wall end, origin at the edge it joins the wall by, the wall's finished tip running out along +x (into a gate)
  2  the other end, turned half a circle about y so that it too runs out along +x from its joining edge
The game puts an end at each side of a gate (where a worn path crosses the wall), facing the gap.
"""
import argparse
import io

import numpy as np
from PIL import Image

from convert_tripo_prop import accessor, pad, read_glb

import json
import struct

Image.MAX_IMAGE_PIXELS = None
SCALE = 3.8  # metres per sheet unit: the straight wall comes out 3.4 m long and a little over a metre high


def components(pos, idx):
    key = np.round(pos, 4)
    _, inverse = np.unique(key, axis=0, return_inverse=True)
    inverse = inverse.reshape(-1)
    parent = list(range(inverse.max() + 1))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    for t in idx.reshape(-1, 3):
        a, b, c = (find(inverse[v]) for v in t)
        parent[b] = a
        parent[find(c)] = a
    roots = np.array([find(inverse[t[0]]) for t in idx.reshape(-1, 3)])
    return [np.where(roots == r)[0] for r in np.unique(roots)]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("dst")
    ap.add_argument("--texture", type=int, default=2048)
    args = ap.parse_args()

    doc, binary = read_glb(args.src)
    prim = doc["meshes"][0]["primitives"][0]
    at = prim["attributes"]
    pos = accessor(doc, binary, at["POSITION"]).astype(np.float64)
    nrm = accessor(doc, binary, at["NORMAL"]).astype(np.float64)
    uv = accessor(doc, binary, at["TEXCOORD_0"]).astype(np.float32)
    idx = accessor(doc, binary, prim["indices"]).astype(np.int64)
    view = doc["bufferViews"][doc["images"][0]["bufferView"]]
    off = view.get("byteOffset", 0)
    picture = Image.open(io.BytesIO(binary[off:off + view["byteLength"]])).convert("RGB")
    buf = io.BytesIO()
    picture.resize((args.texture, args.texture), Image.LANCZOS).save(buf, "PNG", optimize=True)
    png = buf.getvalue()

    parts = components(pos, idx)
    tris = idx.reshape(-1, 3)
    info = []
    for p in parts:
        verts = np.unique(tris[p])
        info.append((p, pos[verts].min(axis=0), pos[verts].max(axis=0)))
    straight = max(info, key=lambda i: i[1][1])            # the one lifted above the others on the sheet
    ends = sorted([i for i in info if i is not straight], key=lambda i: i[1][0])
    left, right = ends[0], ends[1]

    meshes = []
    for which, (face_ids, lo, hi) in (("straight", straight), ("end", left), ("end2", right)):
        t = tris[face_ids]
        used, remap = np.unique(t, return_inverse=True)
        p = pos[used].copy()
        n = nrm[used].copy()
        u = uv[used]
        zc = (lo[2] + hi[2]) / 2
        p[:, 1] -= lo[1]
        p[:, 2] -= zc
        if which == "straight":
            p[:, 0] -= (lo[0] + hi[0]) / 2
        elif which == "end":
            p[:, 0] -= lo[0]                                # joins at its far left edge, tip towards +x
        else:
            p[:, 0] = hi[0] - p[:, 0]                       # turned half a circle about y: join edge at the origin, tip towards +x
            p[:, 2] = -p[:, 2]
            n[:, 0], n[:, 2] = -n[:, 0], -n[:, 2]
        meshes.append((p * SCALE, n.astype(np.float32), u, remap.reshape(-1).astype(np.uint16)))
        ext = (p * SCALE)
        print(which, "tris", len(t), "length %.2f  height %.2f  thickness %.2f" % (np.ptp(ext[:, 0]), np.ptp(ext[:, 1]), np.ptp(ext[:, 2])))

    blobs, views, accessors, prims = [], [], [], []

    def add(data):
        offset = sum(len(b) for b in blobs)
        blobs.append(pad(data))
        views.append({"buffer": 0, "byteOffset": offset, "byteLength": len(data)})
        return len(views) - 1

    for p, n, u, i in meshes:
        p = p.astype(np.float32)
        ids = []
        for data, comp, typ, extra in ((p, 5126, "VEC3", {"min": p.min(axis=0).tolist(), "max": p.max(axis=0).tolist()}), (n, 5126, "VEC3", {}),
                                       (u, 5126, "VEC2", {}), (i, 5123, "SCALAR", {})):
            accessors.append({"bufferView": add(data.tobytes()), "componentType": comp, "count": len(data), "type": typ, **extra})
            ids.append(len(accessors) - 1)
        prims.append({"attributes": {"POSITION": ids[0], "NORMAL": ids[1], "TEXCOORD_0": ids[2]}, "indices": ids[3], "material": 0})
    image = add(png)
    out = {
        "asset": {"version": "2.0", "generator": "convert_walls.py"},
        "scene": 0, "scenes": [{"nodes": [0]}], "nodes": [{"mesh": 0, "name": "stone_wall"}],
        "meshes": [{"name": "stone_wall", "primitives": prims}],
        "materials": [{"name": "stone_wall", "doubleSided": True, "pbrMetallicRoughness": {"baseColorTexture": {"index": 0}, "metallicFactor": 0.0, "roughnessFactor": 0.9}}],
        "textures": [{"source": 0, "sampler": 0}], "samplers": [{"magFilter": 9729, "minFilter": 9987, "wrapS": 10497, "wrapT": 10497}],
        "images": [{"bufferView": image, "mimeType": "image/png", "name": "stone_wall_basecolor"}],
        "accessors": accessors, "bufferViews": views, "buffers": [{"byteLength": sum(len(b) for b in blobs)}],
    }
    j = pad(json.dumps(out, separators=(",", ":")).encode()).replace(b"\0", b" ")
    body = b"".join(blobs)
    with open(args.dst, "wb") as f:
        f.write(struct.pack("<4sII", b"glTF", 2, 12 + 8 + len(j) + 8 + len(body)))
        f.write(struct.pack("<I4s", len(j), b"JSON") + j)
        f.write(struct.pack("<I4s", len(body), b"BIN\0") + body)
    print("wrote", args.dst)


if __name__ == "__main__":
    main()
