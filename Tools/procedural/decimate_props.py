"""Makes the kit's oaks and plants low-poly, keeping their texture mapping, and punches up their textures a little.

    python3 Tools/procedural/decimate_props.py     (needs Pillow, numpy and meshoptimizer: pip install meshoptimizer)

Reads the original Tripo-made glbs from Tools/procedural/props_src and writes Assets/Models/Procedural/props. Each
mesh is welded (the originals have three private vertices per triangle), simplified with the texture coordinates as a
cost so the texture stays where it was, and given smooth normals. The oaks keep their own texture, given a touch more
contrast, colour and crispness so it still reads at the lower detail. The plants are re-textured: their blades wear the
original terrain's grass (a clean patch cut from Assets/Models/Terrain/terrain.glb) and the dark boulder blobs inside
them the game's plain stone (from terrain4_rock_0.glb), each laid on by box projection at one scale, so a plant is two
meshes with two textures.
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


TERRAIN = os.path.join(HERE, "..", "..", "Assets", "Models", "Terrain", "terrain.glb")
ROCK = os.path.join(OUT, "terrain4_rock_0.glb")
TILE_METRES = 3.0  # one repeat of a box-projected texture covers this much of a plant


def embedded_png(path, image=0):
    js, binary = read_glb(path)
    bv = js["bufferViews"][js["images"][image]["bufferView"]]
    off = bv.get("byteOffset", 0)
    return binary[off:off + bv["byteLength"]]


def png_bytes(img):
    buf = io.BytesIO()
    img.save(buf, "PNG", optimize=True)
    return buf.getvalue()


def grass_png():
    """A clean patch of the original terrain's lawn, made seamless, as a 256 px tile."""
    atlas = Image.open(io.BytesIO(embedded_png(TERRAIN))).convert("RGB")
    patch = atlas.crop((1000, 725, 1090, 830)).resize((256, 256), Image.LANCZOS)
    a = np.asarray(patch).astype(float)
    ramp = 1 - np.abs(np.linspace(-1, 1, 256))
    m = np.clip(np.minimum.outer(ramp, ramp) * 2, 0, 1)[..., None]
    a = a * m + np.roll(np.roll(a, 128, 0), 128, 1) * (1 - m)
    img = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8)).filter(ImageFilter.UnsharpMask(radius=1.6, percent=90, threshold=2))
    return png_bytes(ImageEnhance.Color(img).enhance(1.1))


def box_uv(pos, nrm_faces):
    """Texture coordinates for unwelded triangles (n x 3 x 3 positions): each laid flat along its dominant axis."""
    uv = np.zeros((len(pos), 3, 2), np.float32)
    for k in range(len(pos)):
        axis = int(np.argmax(np.abs(nrm_faces[k])))
        for v in range(3):
            x, y, z = pos[k, v]
            uv[k, v] = {0: (y, z), 1: (x, z), 2: (x, y)}[axis]
    return uv / TILE_METRES


def primitive_arrays(tri_pos, tri_nrm, tri_uv):
    n = len(tri_pos)
    return (tri_pos.reshape(-1, 3).astype(np.float32), tri_nrm.reshape(-1, 3).astype(np.float32),
            tri_uv.reshape(-1, 2).astype(np.float32), np.arange(n * 3, dtype=np.uint16 if n * 3 < 65536 else np.uint32))


def write_glb(name, js, prims, images):
    """prims: [(pos, nrm, uv, idx)], images: [png bytes]; one material/texture per primitive."""
    parts, views, accessors, meshprims = [], [], [], []
    def add(data, size):
        offset = sum(len(x) for x in parts)
        parts.append(pad(data))
        views.append({"buffer": 0, "byteOffset": offset, "byteLength": size})
        return len(views) - 1
    for k, (p, nrm, u, i) in enumerate(prims):
        ids = []
        for data, comp, typ, extra in ((p, 5126, "VEC3", {"min": p.min(axis=0).tolist(), "max": p.max(axis=0).tolist()}),
                                       (nrm, 5126, "VEC3", {}), (u, 5126, "VEC2", {}),
                                       (i, 5123 if i.dtype == np.uint16 else 5125, "SCALAR", {})):
            accessors.append({"bufferView": add(data.tobytes(), data.nbytes), "componentType": comp, "count": len(data), "type": typ, **extra})
            ids.append(len(accessors) - 1)
        meshprims.append({"attributes": {"POSITION": ids[0], "NORMAL": ids[1], "TEXCOORD_0": ids[2]}, "indices": ids[3], "material": k})
    image_defs = [{"bufferView": add(png, len(png)), "mimeType": "image/png", "name": f"{name}_{k}"} for k, png in enumerate(images)]
    base_material = js["materials"][0]
    js["materials"] = [{**base_material, "name": f"{name}_{k}", "pbrMetallicRoughness": {**base_material["pbrMetallicRoughness"], "baseColorTexture": {"index": k}}} for k in range(len(prims))]
    js["textures"] = [{"sampler": 0, "source": k} for k in range(len(prims))]
    js["images"] = image_defs
    js["accessors"], js["bufferViews"] = accessors, views
    js["meshes"][0]["primitives"] = meshprims
    js["buffers"] = [{"byteLength": sum(len(x) for x in parts)}]
    j = pad(json.dumps(js, separators=(",", ":")).encode()).replace(b"\0", b" ")
    body = b"".join(parts)
    with open(os.path.join(OUT, name), "wb") as f:
        f.write(struct.pack("<4sII", b"glTF", 2, 12 + 8 + len(j) + 8 + len(body)))
        f.write(struct.pack("<I4s", len(j), b"JSON") + j)
        f.write(struct.pack("<I4s", len(body), b"BIN\0") + body)


def process(name, grass, stone):
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
    used, remap = np.unique(kept, return_inverse=True)
    p, u = wpos[used].astype(np.float32), wuv[used].astype(np.float32)
    nrm = smooth_normals(p, remap.astype(np.int64))
    original_png = embedded_png(os.path.join(SRC, name))

    if kind == "oak":
        i = remap.astype(np.uint16) if len(used) < 65536 else remap.astype(np.uint32)
        img = Image.open(io.BytesIO(original_png)).convert("RGB")
        img = ImageEnhance.Contrast(img).enhance(1.10)
        img = ImageEnhance.Color(img).enhance(1.15)
        img = img.filter(ImageFilter.UnsharpMask(radius=1.4, percent=70, threshold=2))
        write_glb(name, js, [(p, nrm, u, i)], [png_bytes(img)])
        print(f"{name}: {before} -> {len(i) // 3} triangles (own texture)")
        return

    # Plants: blades (green in the original texture) get the grass, everything else the stone.
    tris = remap.reshape(-1, 3)
    original = np.asarray(Image.open(io.BytesIO(original_png)).convert("RGB")).astype(int)
    h, w, _ = original.shape
    centre = u[tris].mean(axis=1)
    px = original[np.clip((centre[:, 1] % 1 * h).astype(int), 0, h - 1), np.clip((centre[:, 0] % 1 * w).astype(int), 0, w - 1)]
    is_grass = (px[:, 1] - px[:, 2]) > 8
    groups = []
    for mask, png in ((is_grass, grass), (~is_grass, stone)):
        if not mask.any():
            continue
        tp, tn = p[tris[mask]], nrm[tris[mask]]
        face_n = np.cross(tp[:, 1] - tp[:, 0], tp[:, 2] - tp[:, 0])
        groups.append((primitive_arrays(tp, tn, box_uv(tp, face_n)), png))
    write_glb(name, js, [g[0] for g in groups], [g[1] for g in groups])
    print(f"{name}: {before} -> {len(tris)} triangles ({int(is_grass.sum())} grass, {int((~is_grass).sum())} stone)")


if __name__ == "__main__":
    grass = grass_png()
    stone = embedded_png(ROCK)
    for n in sorted(os.listdir(SRC)):
        if n.endswith(".glb"):
            process(n, grass, stone)
