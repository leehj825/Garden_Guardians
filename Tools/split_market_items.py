"""Split the generated sheet of village items (a rune stone, a workbench, four market tables and a basket, laid out in a row as one mesh)
into the game's item file: one mesh each, standing on y = 0 centred on its own origin, sized in metres, sharing one 2048 px picture.

    pip install numpy scipy pillow
    python3 Tools/split_market_items.py Tools/kin_src/bench_market_others.glb Assets/Models/Props/Village/Items.glb

The meshes come out in this order (the order of ItemModels.Kind in Source/Engine/ItemModels.cs):
  0 RuneStone (about 1.3 m tall)  1 Workbench (1.3 m long)  2-5 Stalls (0.7 to 1.4 m wide, 0.8 m tall)  6 Basket (0.9 m wide).
"""
import io
import json
import struct
import sys

import numpy as np
from PIL import Image
from scipy import sparse
from scipy.sparse.csgraph import connected_components

Image.MAX_IMAGE_PIXELS = None
# (name, scale) for the pieces in order along the sheet's z axis
# The sheet's pieces are all drawn at their own sizes, so each kind gets one scale (the four stalls the same, to keep their sizes to each other).
ORDER = [("RuneStone", 5.4), ("Workbench", 6.5), ("StallA", 8.0), ("StallB", 8.0), ("StallC", 8.0), ("StallD", 8.0), ("Basket", 8.0)]


def read(path):
    d = open(path, "rb").read()
    n = struct.unpack("<I", d[12:16])[0]
    doc = json.loads(d[20:20 + n])
    binary = d[20 + n + 8:]

    def acc(i):
        a = doc["accessors"][i]; v = doc["bufferViews"][a["bufferView"]]
        w = {"SCALAR": 1, "VEC2": 2, "VEC3": 3}[a["type"]]
        dt = {5126: "<f4", 5125: "<u4", 5123: "<u2"}[a["componentType"]]
        arr = np.frombuffer(binary, dt, a["count"] * w, v.get("byteOffset", 0) + a.get("byteOffset", 0))
        return arr.reshape(-1, w) if w > 1 else arr
    pr = doc["meshes"][0]["primitives"][0]; at = pr["attributes"]
    v = doc["bufferViews"][doc["images"][0]["bufferView"]]
    picture = Image.open(io.BytesIO(binary[v.get("byteOffset", 0):v.get("byteOffset", 0) + v["byteLength"]])).convert("RGB")
    return (acc(at["POSITION"]).astype(np.float64), acc(at["NORMAL"]).astype(np.float32), acc(at["TEXCOORD_0"]).astype(np.float32),
            acc(pr["indices"]).astype(np.int64).reshape(-1, 3), picture)


def components(pos, idx):
    _, inv = np.unique(np.round(pos, 5), axis=0, return_inverse=True)
    n = inv.max() + 1
    a = np.concatenate([inv[idx[:, 0]], inv[idx[:, 1]], inv[idx[:, 2]]]); b = np.concatenate([inv[idx[:, 1]], inv[idx[:, 2]], inv[idx[:, 0]]])
    _, lab = connected_components(sparse.coo_matrix((np.ones(len(a)), (a, b)), shape=(n, n)), directed=False)
    tri = lab[inv[idx[:, 0]]]
    parts = []
    for c in np.unique(tri):
        t = np.where(tri == c)[0]
        if len(t) >= 50:
            p = pos[np.unique(idx[t])]
            parts.append((p[:, 2].min(), t))
    parts.sort(key=lambda x: x[0])  # along the sheet
    return [t for _, t in parts]


def pad(b):
    return b + b"\0" * (-len(b) % 4)


def main():
    src, dst = sys.argv[1], sys.argv[2]
    pos, nrm, uv, idx, picture = read(src)
    parts = components(pos, idx)
    if len(parts) != len(ORDER):
        sys.exit("expected %d pieces, found %d" % (len(ORDER), len(parts)))
    chunks, sizes, meshes, accessors, views = [], [], [], [], []
    offset = 0

    def add(arr):
        nonlocal offset
        raw = pad(arr.tobytes())
        views.append({"buffer": 0, "byteOffset": offset, "byteLength": arr.nbytes})
        chunks.append(raw)
        offset += len(raw)
        return len(views) - 1

    for (name, scale), t in zip(ORDER, parts):
        vs, inverse = np.unique(idx[t], return_inverse=True)
        p = pos[vs]
        lo, hi = p.min(0), p.max(0)
        p = (p - [(lo[0] + hi[0]) / 2, lo[1], (lo[2] + hi[2]) / 2]) * scale
        p32 = p.astype("<f4"); n32 = nrm[vs].astype("<f4"); u32 = uv[vs].astype("<f4"); i32 = inverse.reshape(-1, 3).astype("<u2").reshape(-1)
        first = len(accessors)
        accessors += [
            {"bufferView": add(p32), "componentType": 5126, "count": len(p32), "type": "VEC3", "min": p32.min(0).tolist(), "max": p32.max(0).tolist()},
            {"bufferView": add(n32), "componentType": 5126, "count": len(n32), "type": "VEC3"},
            {"bufferView": add(u32), "componentType": 5126, "count": len(u32), "type": "VEC2"},
            {"bufferView": add(i32), "componentType": 5123, "count": len(i32), "type": "SCALAR"},
        ]
        meshes.append({"name": name, "primitives": [{"attributes": {"POSITION": first, "NORMAL": first + 1, "TEXCOORD_0": first + 2}, "indices": first + 3, "material": 0}]})
        size = (p32.max(0) - p32.min(0))
        print("%-9s %5d triangles, %.2f x %.2f x %.2f m (x, y, z)" % (name, len(t), size[0], size[1], size[2]))

    buf = io.BytesIO()
    picture.resize((2048, 2048), Image.LANCZOS).save(buf, "PNG", optimize=True)
    png = buf.getvalue()
    views.append({"buffer": 0, "byteOffset": offset, "byteLength": len(png)})
    chunks.append(pad(png))
    offset += len(pad(png))
    doc = {
        "asset": {"version": "2.0", "generator": "split_market_items.py"},
        "scene": 0, "scenes": [{"nodes": list(range(len(meshes)))}],
        "nodes": [{"mesh": i, "name": m["name"]} for i, m in enumerate(meshes)],
        "meshes": meshes,
        "materials": [{"name": "items", "doubleSided": True, "pbrMetallicRoughness": {"baseColorTexture": {"index": 0}, "metallicFactor": 0.0, "roughnessFactor": 0.6}}],
        "textures": [{"source": 0, "sampler": 0}], "samplers": [{"magFilter": 9729, "minFilter": 9987, "wrapS": 10497, "wrapT": 10497}],
        "images": [{"bufferView": len(views) - 1, "mimeType": "image/png", "name": "items_basecolor"}],
        "accessors": accessors, "bufferViews": views, "buffers": [{"byteLength": offset}],
    }
    j = pad(json.dumps(doc, separators=(",", ":")).encode()).replace(b"\0", b" ")
    body = b"".join(chunks)
    with open(dst, "wb") as f:
        f.write(struct.pack("<4sII", b"glTF", 2, 12 + 8 + len(j) + 8 + len(body)))
        f.write(struct.pack("<I4s", len(j), b"JSON") + j)
        f.write(struct.pack("<I4s", len(body), b"BIN\0") + body)
    print("wrote", dst, len(body) // 1024, "KB")


if __name__ == "__main__":
    main()
