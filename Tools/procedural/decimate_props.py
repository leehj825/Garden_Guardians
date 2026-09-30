"""Makes the kit's oaks and plants low-poly, keeping their texture mapping, and punches up their textures a little.

    python3 Tools/procedural/decimate_props.py     (needs Pillow, numpy and meshoptimizer: pip install meshoptimizer)

Reads the original Tripo-made glbs from Tools/procedural/props_src and writes Assets/Models/Procedural/props. Each
mesh is welded (the originals have three private vertices per triangle), simplified with the texture coordinates as a
cost so the texture stays where it was, and given smooth normals. The texture gets a touch more contrast, colour and
crispness so it still reads at the lower detail.
"""
import io
import json
import os
import struct
import sys

import numpy as np
from PIL import Image, ImageEnhance, ImageFilter
import meshoptimizer as mo

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "props_src")
OUT = os.path.join(HERE, "..", "..", "Assets", "Models", "Procedural", "props")

# Triangles to keep, by kind: the oaks are huge and seen from far; plants are small and seen close.
TARGET = {"oak": 2400, "plant": 420}


def read_glb(path):
    d = open(path, "rb").read()
    jl, _ = struct.unpack("<I4s", d[12:20])
    js = json.loads(d[20:20 + jl])
    binary = d[20 + jl + 8:]
    return js, binary


def view(js, binary, accessor, dtype, width):
    a = js["accessors"][accessor]
    v = js["bufferViews"][a["bufferView"]]
    off = v.get("byteOffset", 0) + a.get("byteOffset", 0)
    return np.frombuffer(binary[off:off + a["count"] * width * np.dtype(dtype).itemsize], dtype).reshape(-1, width) if width > 1 else np.frombuffer(binary[off:off + a["count"] * np.dtype(dtype).itemsize], dtype)


def pad(b):
    return b + b"\0" * (-len(b) % 4)


def weld(pos, uv, idx):
    key = np.round(np.hstack([pos, uv]) * 1e4).astype(np.int64)
    _, first, inverse = np.unique(key, axis=0, return_index=True, return_inverse=True)
    inverse = inverse.reshape(-1)
    return pos[first], uv[first], inverse[idx].astype(np.uint32)


def simplify(pos, uv, idx, target_tris):
    """meshopt_simplifyWithAttributes, called directly (the wrapper's own binding rejects its arguments)."""
    import ctypes
    lib = mo.simplifier.lib
    fn = lib.meshopt_simplifyWithAttributes
    fn.restype = ctypes.c_size_t
    fn.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t, ctypes.c_void_p, ctypes.c_size_t, ctypes.c_size_t,
                   ctypes.c_void_p, ctypes.c_size_t, ctypes.c_void_p, ctypes.c_size_t, ctypes.c_void_p,
                   ctypes.c_size_t, ctypes.c_float, ctypes.c_uint, ctypes.c_void_p]
    idx = np.ascontiguousarray(idx, np.uint32)
    pos = np.ascontiguousarray(pos, np.float32)
    uv = np.ascontiguousarray(uv, np.float32)
    weights = np.array([1.0, 1.0], np.float32)
    dest = np.zeros(len(idx), np.uint32)
    got = fn(dest.ctypes.data, idx.ctypes.data, len(idx), pos.ctypes.data, len(pos), 12, uv.ctypes.data, 8,
             weights.ctypes.data, 2, None, min(len(idx), target_tris * 3), 0.5, mo.SIMPLIFY_PRUNE, None)
    return dest[:got]


def smooth_normals(pos, idx):
    tri = idx.reshape(-1, 3)
    n = np.cross(pos[tri[:, 1]] - pos[tri[:, 0]], pos[tri[:, 2]] - pos[tri[:, 0]])
    out = np.zeros_like(pos)
    for k in range(3):
        np.add.at(out, tri[:, k], n)
    length = np.linalg.norm(out, axis=1, keepdims=True)
    return (out / np.maximum(length, 1e-9)).astype(np.float32)


def enhance(png):
    img = Image.open(io.BytesIO(png)).convert("RGB")
    img = ImageEnhance.Contrast(img).enhance(1.10)
    img = ImageEnhance.Color(img).enhance(1.15)
    img = img.filter(ImageFilter.UnsharpMask(radius=1.4, percent=70, threshold=2))
    buf = io.BytesIO()
    img.save(buf, "PNG", optimize=True)
    return buf.getvalue()


def process(name):
    js, binary = read_glb(os.path.join(SRC, name))
    prim = js["meshes"][0]["primitives"][0]
    at = prim["attributes"]
    pos = view(js, binary, at["POSITION"], np.float32, 3).astype(np.float64)
    uv = view(js, binary, at["TEXCOORD_0"], np.float32, 2).astype(np.float64)
    idx = view(js, binary, prim["indices"], np.uint16, 1).astype(np.uint32)
    kind = "oak" if "oak" in name else "plant"
    wpos, wuv, widx = weld(pos, uv, idx)
    before = len(widx) // 3
    kept = simplify(wpos, wuv, widx, TARGET[kind])
    # Compact to the vertices still used.
    used, remap = np.unique(kept, return_inverse=True)
    p, u = wpos[used].astype(np.float32), wuv[used].astype(np.float32)
    i = remap.astype(np.uint16) if len(used) < 65536 else remap.astype(np.uint32)
    nrm = smooth_normals(p, remap.astype(np.int64))
    image = js["images"][0]
    bv = js["bufferViews"][image["bufferView"]]
    png = enhance(binary[bv.get("byteOffset", 0):bv.get("byteOffset", 0) + bv["byteLength"]])

    parts = [pad(p.tobytes()), pad(nrm.tobytes()), pad(u.tobytes()), pad(i.tobytes()), pad(png)]
    sizes = [p.nbytes, nrm.nbytes, u.nbytes, i.nbytes, len(png)]
    offs = np.cumsum([0] + [len(x) for x in parts])
    js["bufferViews"] = [{"buffer": 0, "byteOffset": int(offs[k]), "byteLength": sizes[k]} for k in range(5)]
    comp = 5123 if i.dtype == np.uint16 else 5125
    js["accessors"] = [
        {"bufferView": 0, "componentType": 5126, "count": len(p), "type": "VEC3", "min": p.min(axis=0).tolist(), "max": p.max(axis=0).tolist()},
        {"bufferView": 1, "componentType": 5126, "count": len(nrm), "type": "VEC3"},
        {"bufferView": 2, "componentType": 5126, "count": len(u), "type": "VEC2"},
        {"bufferView": 3, "componentType": comp, "count": len(i), "type": "SCALAR"},
    ]
    js["meshes"][0]["primitives"][0] = {"attributes": {"POSITION": 0, "NORMAL": 1, "TEXCOORD_0": 2}, "indices": 3, "material": prim.get("material", 0)}
    js["images"][0]["bufferView"] = 4
    js["buffers"] = [{"byteLength": int(offs[-1])}]
    j = pad(json.dumps(js, separators=(",", ":")).encode()).replace(b"\0", b" ")
    body = b"".join(parts)
    with open(os.path.join(OUT, name), "wb") as f:
        f.write(struct.pack("<4sII", b"glTF", 2, 12 + 8 + len(j) + 8 + len(body)))
        f.write(struct.pack("<I4s", len(j), b"JSON") + j)
        f.write(struct.pack("<I4s", len(body), b"BIN\0") + body)
    print(f"{name}: {before} -> {len(i) // 3} triangles, {os.path.getsize(os.path.join(OUT, name)) // 1024} KB")


if __name__ == "__main__":
    for n in sorted(os.listdir(SRC)):
        if n.endswith(".glb"):
            process(n)
