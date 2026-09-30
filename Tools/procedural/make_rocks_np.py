"""Remakes the kit's boulders without Blender (same shapes as make_rocks.py, but chunkier).

    python3 Tools/procedural/make_rocks_np.py     (needs Pillow and numpy)

Each rock prop's glb is replaced by a faceted, flat-bottomed boulder of the same size: a 320-face icosphere pushed out
of round by big lumps and a little per-vertex jitter, flat-shaded. It wears the game's plain stone (Tools/procedural/stone.png)
shrunk to a coarse 64 px, punched up in contrast and laid over each face along its dominant axis at the same scale on every
rock, so a small boulder shows the same blocky texels a big one does. Origin at the base centre, like every other prop.
"""
import io
import json
import math
import os
import re
import struct

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
PROPS = os.path.join(HERE, "..", "..", "Assets", "Models", "Procedural", "props")
KIT = os.path.join(HERE, "..", "..", "Source", "World", "Procedural", "ProceduralKit.cs")
TEXELS = 64
METRES_PER_TEXTURE = 2.2  # one repeat of the texture covers this much rock, whatever its size


def texture_png():
    img = Image.open(os.path.join(HERE, "stone.png")).convert("L")
    img = img.crop((14, 14, img.width - 14, img.height - 14)).resize((TEXELS, TEXELS), Image.LANCZOS)
    t = np.asarray(img).astype(float)
    t = (t - t.mean()) / (t.std() + 1e-6)          # grain only
    base = np.array([150.0, 146.0, 138.0])
    rgb = base[None, None, :] * (1 + 0.20 * t[..., None])
    out = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8))
    buf = io.BytesIO()
    out.save(buf, "PNG")
    return buf.getvalue()


def icosphere(subdivisions):
    p = (1 + 5 ** 0.5) / 2
    v = [(-1, p, 0), (1, p, 0), (-1, -p, 0), (1, -p, 0), (0, -1, p), (0, 1, p), (0, -1, -p), (0, 1, -p), (p, 0, -1), (p, 0, 1), (-p, 0, -1), (-p, 0, 1)]
    f = [(0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
         (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1)]
    v = [np.array(x, float) / np.linalg.norm(x) for x in v]
    for _ in range(subdivisions):
        cache, nf = {}, []

        def mid(a, b):
            key = (min(a, b), max(a, b))
            if key not in cache:
                m = v[a] + v[b]
                v.append(m / np.linalg.norm(m))
                cache[key] = len(v) - 1
            return cache[key]

        for a, b, c in f:
            ab, bc, ca = mid(a, b), mid(b, c), mid(c, a)
            nf += [(a, ab, ca), (b, bc, ab), (c, ca, bc), (ab, bc, ca)]
        f = nf
    return np.array(v), f


def rock(name, height, reach, seed, png):
    rng = np.random.default_rng(seed)
    verts, faces = icosphere(3)
    rx = 0.55 * reach
    rz = rx * rng.uniform(0.7, 0.9)
    hz = min(height * 0.8, rx * rng.uniform(0.75, 1.0))
    ph = rng.uniform(0, math.tau, 6)
    out = []
    for d in verts:
        bump = (1.0 + 0.26 * math.sin(3 * d[0] + ph[0]) * math.cos(2 * d[1] + ph[1])
                + 0.16 * math.sin(5 * d[2] + 4 * d[0] + ph[2]) + 0.08 * math.sin(7 * d[1] - 3 * d[2] + ph[3])
                + rng.uniform(-0.07, 0.07))
        p = np.array([d[0] * rx * bump, d[1] * rz * bump, d[2] * hz * bump])
        p[2] = max(p[2], -0.12 * hz) + 0.12 * hz  # flat underside, sunk a little
        out.append(p)
    out = np.array(out)

    cube = METRES_PER_TEXTURE
    pos, nor, uv = [], [], []
    for a, b, c in faces:
        tri = out[[a, b, c]]
        n = np.cross(tri[1] - tri[0], tri[2] - tri[0])
        length = np.linalg.norm(n)
        n = n / length if length > 1e-9 else np.array([0, 0, 1.0])
        if np.dot(n, tri.mean(axis=0) - np.array([0, 0, 0.12 * hz])) < 0:
            tri = tri[[0, 2, 1]]
            n = -n
        axis = int(np.argmax(np.abs(n)))
        for p in tri:
            u, w = {0: (p[1], p[2]), 1: (p[0], p[2]), 2: (p[0], p[1])}[axis]
            pos.append((p[0], p[2], -p[1]))          # Blender z-up -> glTF y-up
            nor.append((n[0], n[2], -n[1]))
            uv.append((u / cube, w / cube))
    pos = np.array(pos, np.float32)
    nor = np.array(nor, np.float32)
    uv = np.array(uv, np.float32)
    idx = np.arange(len(pos), dtype=np.uint16)
    write_glb(os.path.join(PROPS, name), pos, nor, uv, idx, png)


def pad(b):
    return b + b"\0" * (-len(b) % 4)


def write_glb(path, pos, nor, uv, idx, png):
    parts = [pad(pos.tobytes()), pad(nor.tobytes()), pad(uv.tobytes()), pad(idx.tobytes()), pad(png)]
    offsets = np.cumsum([0] + [len(p) for p in parts])
    views = [{"buffer": 0, "byteOffset": int(offsets[i]), "byteLength": len(parts[i])} for i in range(5)]
    views[0]["byteLength"] = pos.nbytes
    views[1]["byteLength"] = nor.nbytes
    views[2]["byteLength"] = uv.nbytes
    views[3]["byteLength"] = idx.nbytes
    views[4]["byteLength"] = len(png)
    js = {
        "asset": {"version": "2.0", "generator": "make_rocks_np.py"},
        "scene": 0, "scenes": [{"nodes": [0]}], "nodes": [{"mesh": 0}],
        "meshes": [{"primitives": [{"attributes": {"POSITION": 0, "NORMAL": 1, "TEXCOORD_0": 2}, "indices": 3, "material": 0}]}],
        "materials": [{"pbrMetallicRoughness": {"baseColorTexture": {"index": 0}, "metallicFactor": 0.0, "roughnessFactor": 1.0}}],
        "textures": [{"source": 0, "sampler": 0}],
        "images": [{"bufferView": 4, "mimeType": "image/png"}],
        "samplers": [{"magFilter": 9728, "minFilter": 9728, "wrapS": 10497, "wrapT": 10497}],
        "accessors": [
            {"bufferView": 0, "componentType": 5126, "count": len(pos), "type": "VEC3", "min": pos.min(axis=0).tolist(), "max": pos.max(axis=0).tolist()},
            {"bufferView": 1, "componentType": 5126, "count": len(nor), "type": "VEC3"},
            {"bufferView": 2, "componentType": 5126, "count": len(uv), "type": "VEC2"},
            {"bufferView": 3, "componentType": 5123, "count": len(idx), "type": "SCALAR"},
        ],
        "bufferViews": views, "buffers": [{"byteLength": int(offsets[-1])}],
    }
    j = pad(json.dumps(js, separators=(",", ":")).encode()).replace(b"\0", b" ")
    binary = b"".join(parts)
    total = 12 + 8 + len(j) + 8 + len(binary)
    with open(path, "wb") as f:
        f.write(struct.pack("<4sII", b"glTF", 2, total))
        f.write(struct.pack("<I4s", len(j), b"JSON") + j)
        f.write(struct.pack("<I4s", len(binary), b"BIN\0") + binary)


def main():
    png = texture_png()
    kit = open(KIT).read()
    rocks = re.findall(r'File = "props/([^"]+)", Kind = KitKind\.Rock, Height = ([\d.]+)f, Reach = ([\d.]+)f', kit)
    for index, (name, height, reach) in enumerate(rocks):
        rock(name, float(height), float(reach), 100 + index, png)
        print("rock", name, height, reach)


if __name__ == "__main__":
    main()
