"""Shrink a static Tripo prop (the acorn house, about 1,000 triangles) for the game, with no Blender.

    pip install numpy pillow
    python3 Tools/convert_tripo_prop.py Tools/kin_src/House.glb Assets/Models/Props/AcornHouse.glb --texture 2048 --lod Assets/Models/Props/AcornHouse_lod.glb --lod-texture 512

The mesh is kept as it is (it is already light), scaled so the model is 1 unit tall with its base at y = 0, and written
with its texture (4096 px, JPEG) shrunk to --texture px and stored as PNG: this raylib build can't read an embedded JPEG.
--lod also writes the same mesh with a --lod-texture px texture, for when the prop is small on screen.
"""
import argparse
import io
import json
import struct

import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None
NAME = "acorn_house"


def read_glb(path):
    data = open(path, "rb").read()
    length = struct.unpack("<I", data[12:16])[0]
    return json.loads(data[20:20 + length]), data[20 + length + 8:]


def accessor(doc, binary, index):
    a = doc["accessors"][index]
    view = doc["bufferViews"][a["bufferView"]]
    width = {"SCALAR": 1, "VEC2": 2, "VEC3": 3}[a["type"]]
    dtype = {5126: "<f4", 5125: "<u4", 5123: "<u2"}[a["componentType"]]
    arr = np.frombuffer(binary, dtype, a["count"] * width, view.get("byteOffset", 0) + a.get("byteOffset", 0))
    return arr.reshape(-1, width) if width > 1 else arr


def pad(b):
    return b + b"\0" * (-len(b) % 4)


def write(path, pos, nrm, uv, idx, png):
    idx = idx.astype(np.uint16 if len(pos) < 65536 else np.uint32)
    parts = [pad(pos.tobytes()), pad(nrm.tobytes()), pad(uv.tobytes()), pad(idx.tobytes()), pad(png)]
    sizes = [pos.nbytes, nrm.nbytes, uv.nbytes, idx.nbytes, len(png)]
    offsets = np.cumsum([0] + [len(p) for p in parts])
    doc = {
        "asset": {"version": "2.0", "generator": "convert_tripo_prop.py"},
        "scene": 0, "scenes": [{"nodes": [0]}], "nodes": [{"mesh": 0, "name": NAME}],
        "meshes": [{"primitives": [{"attributes": {"POSITION": 0, "NORMAL": 1, "TEXCOORD_0": 2}, "indices": 3, "material": 0}]}],
        "materials": [{"name": NAME, "doubleSided": True, "pbrMetallicRoughness": {"baseColorTexture": {"index": 0}, "metallicFactor": 0.0, "roughnessFactor": 0.5}}],
        "textures": [{"source": 0, "sampler": 0}], "samplers": [{"magFilter": 9729, "minFilter": 9987, "wrapS": 10497, "wrapT": 10497}],
        "images": [{"bufferView": 4, "mimeType": "image/png", "name": NAME + "_basecolor"}],
        "accessors": [
            {"bufferView": 0, "componentType": 5126, "count": len(pos), "type": "VEC3", "min": pos.min(axis=0).tolist(), "max": pos.max(axis=0).tolist()},
            {"bufferView": 1, "componentType": 5126, "count": len(nrm), "type": "VEC3"},
            {"bufferView": 2, "componentType": 5126, "count": len(uv), "type": "VEC2"},
            {"bufferView": 3, "componentType": 5123 if idx.dtype == np.uint16 else 5125, "count": len(idx), "type": "SCALAR"},
        ],
        "bufferViews": [{"buffer": 0, "byteOffset": int(offsets[k]), "byteLength": sizes[k]} for k in range(5)],
        "buffers": [{"byteLength": int(offsets[-1])}],
    }
    j = pad(json.dumps(doc, separators=(",", ":")).encode()).replace(b"\0", b" ")
    body = b"".join(parts)
    with open(path, "wb") as f:
        f.write(struct.pack("<4sII", b"glTF", 2, 12 + 8 + len(j) + 8 + len(body)))
        f.write(struct.pack("<I4s", len(j), b"JSON") + j)
        f.write(struct.pack("<I4s", len(body), b"BIN\0") + body)
    print("wrote", path, len(pos), "vertices,", len(idx) // 3, "triangles")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("dst")
    ap.add_argument("--texture", type=int, default=2048)
    ap.add_argument("--lod")
    ap.add_argument("--lod-texture", type=int, default=512)
    ap.add_argument("--height", type=float, default=1.0, help="stand this tall (m); the default 1 suits the acorn house, which the game scales itself")
    ap.add_argument("--name", default="acorn_house")
    ap.add_argument("--xz", type=float, default=1.0, help="stretch x and z by this much after sizing (to make a lower-detail twin as wide as the full one)")
    args = ap.parse_args()
    global NAME
    NAME = args.name

    doc, binary = read_glb(args.src)
    prim = doc["meshes"][0]["primitives"][0]
    at = prim["attributes"]
    pos = accessor(doc, binary, at["POSITION"]).astype(np.float64)
    nrm = accessor(doc, binary, at["NORMAL"]).astype(np.float32)
    uv = accessor(doc, binary, at["TEXCOORD_0"]).astype(np.float32)
    idx = accessor(doc, binary, prim["indices"]).astype(np.uint32)
    view = doc["bufferViews"][doc["images"][0]["bufferView"]]
    offset = view.get("byteOffset", 0)
    picture = Image.open(io.BytesIO(binary[offset:offset + view["byteLength"]])).convert("RGB")

    pos = (pos - [0.0, pos[:, 1].min(), 0.0]) / (pos[:, 1].max() - pos[:, 1].min()) * args.height  # --height tall, base at y = 0
    pos[:, 0] *= args.xz
    pos[:, 2] *= args.xz
    pos = pos.astype(np.float32)

    def png(size):
        buf = io.BytesIO()
        picture.resize((size, size), Image.LANCZOS).save(buf, "PNG", optimize=True)
        return buf.getvalue()

    write(args.dst, pos, nrm, uv, idx, png(args.texture))
    if args.lod:
        write(args.lod, pos, nrm, uv, idx, png(args.lod_texture))


if __name__ == "__main__":
    main()
