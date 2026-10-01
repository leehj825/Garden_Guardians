"""Prepare a Tripo ant / ant hill model for the game (pure Python + Pillow, no Blender).

    pip install pillow
    python Tools/convert_ant.py ant.glb Assets/Models/Props/Ant.glb --texture 1024 --face=-x
    python Tools/convert_ant.py anthill.glb Assets/Models/Props/AntHill.glb --texture 1536

The glb's embedded JPEG is re-encoded as a PNG of N x N (this raylib build cannot read an embedded
JPEG). --face turns the model about the vertical so the way it looks (as exported: -x for the ant,
the head and the feelers) becomes +z, the way the game's models face; the default leaves it alone.
Everything else (the mesh, its single material, its UVs) is kept as it is.
"""
import argparse
import io
import json
import math
import struct

from PIL import Image


def read_glb(path):
    data = open(path, "rb").read()
    jlen = struct.unpack("<I", data[12:16])[0]
    j = json.loads(data[20:20 + jlen])
    off = 20 + jlen
    blen = struct.unpack("<I", data[off:off + 4])[0]
    return j, data[off + 8:off + 8 + blen]


def view_bytes(j, b, index):
    v = j["bufferViews"][index]
    start = v.get("byteOffset", 0)
    return b[start:start + v["byteLength"]]


def turn_vec3(blob, count, degrees, offset=0, stride=12):
    """Rotates VEC3 floats in place about +y (x' = x cos + z sin, z' = -x sin + z cos)."""
    c, s = math.cos(math.radians(degrees)), math.sin(math.radians(degrees))
    out = bytearray(blob)
    for i in range(count):
        o = offset + i * stride
        x, y, z = struct.unpack_from("<3f", out, o)
        struct.pack_into("<3f", out, o, x * c + z * s, y, -x * s + z * c)
    return bytes(out)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("dst")
    ap.add_argument("--texture", type=int, default=1024)
    ap.add_argument("--face", choices=["none", "+x", "-x", "+z", "-z"], default="none",
                    help="the direction the model faces as exported; it is turned to face +z")
    args = ap.parse_args()

    j, b = read_glb(args.src)
    views = [bytearray(view_bytes(j, b, i)) for i in range(len(j["bufferViews"]))]

    turn = {"none": 0, "+z": 0, "-z": 180, "-x": 90, "+x": -90}[args.face]
    if turn:
        prim = j["meshes"][0]["primitives"][0]
        for name in ("POSITION", "NORMAL"):
            acc = j["accessors"][prim["attributes"][name]]
            view = j["accessors"][prim["attributes"][name]]["bufferView"]
            views[view] = bytearray(turn_vec3(bytes(views[view]), acc["count"], turn, acc.get("byteOffset", 0),
                                              j["bufferViews"][view].get("byteStride", 12)))
            if name == "POSITION":
                lo = [min(c) for c in zip(*[struct.unpack_from("<3f", views[view], acc.get("byteOffset", 0) + i * 12)
                                            for i in range(acc["count"])])]
                hi = [max(c) for c in zip(*[struct.unpack_from("<3f", views[view], acc.get("byteOffset", 0) + i * 12)
                                            for i in range(acc["count"])])]
                acc["min"], acc["max"] = lo, hi

    image = j["images"][0]
    img = Image.open(io.BytesIO(bytes(views[image["bufferView"]]))).convert("RGB").resize((args.texture, args.texture), Image.LANCZOS)
    png = io.BytesIO()
    img.save(png, "PNG", optimize=True)
    views[image["bufferView"]] = bytearray(png.getvalue())
    image["mimeType"] = "image/png"

    binary = bytearray()
    for i, v in enumerate(views):
        while len(binary) % 4:
            binary.append(0)
        j["bufferViews"][i]["byteOffset"] = len(binary)
        j["bufferViews"][i]["byteLength"] = len(v)
        binary += v
    while len(binary) % 4:
        binary.append(0)
    j["buffers"] = [{"byteLength": len(binary)}]

    text = json.dumps(j, separators=(",", ":")).encode()
    text += b" " * (-len(text) % 4)
    total = 12 + 8 + len(text) + 8 + len(binary)
    with open(args.dst, "wb") as out:
        out.write(struct.pack("<4sII", b"glTF", 2, total))
        out.write(struct.pack("<I4s", len(text), b"JSON") + text)
        out.write(struct.pack("<I4s", len(binary), b"BIN\x00") + binary)
    print(f"{args.dst}: {total // 1024} KB")


main()
