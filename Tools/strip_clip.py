"""Strip a Mixamo animation clip down to its skeleton and animation, with no Blender.

    python3 Tools/strip_clip.py Assets/Models/Bramblekin/FishingCast.glb [more.glb ...]

The game reads only the animation out of FishingCast, GatheringObjects and SwordAndShieldSlash (the Bramblekin's mesh comes
from Walking.glb), but each file also carries a 50,000-triangle mesh and its texture, about 1.9 MB. This drops the mesh,
material, texture and image, and every buffer only they used, in place.
"""
import json
import struct
import sys


def read(path):
    data = open(path, "rb").read()
    n = struct.unpack("<I", data[12:16])[0]
    return json.loads(data[20:20 + n]), data[20 + n + 8:]


def main():
    for path in sys.argv[1:]:
        doc, binary = read(path)
        before = len(binary)
        for node in doc["nodes"]:
            node.pop("mesh", None)
            node.pop("skin", None)
        for key in ("meshes", "materials", "textures", "images", "samplers"):
            doc.pop(key, None)
        used = set()
        for skin in doc.get("skins", []):
            used.add(skin["inverseBindMatrices"])
        for anim in doc["animations"]:
            for s in anim["samplers"]:
                used.add(s["input"])
                used.add(s["output"])
        # Keep only the accessors the skeleton and the clip use, and the buffer views under them, repacked.
        keep_acc = sorted(used)
        remap_acc = {old: new for new, old in enumerate(keep_acc)}
        keep_views = sorted({doc["accessors"][a]["bufferView"] for a in keep_acc})
        remap_view = {old: new for new, old in enumerate(keep_views)}
        out, views = b"", []
        for old in keep_views:
            v = doc["bufferViews"][old]
            chunk = binary[v.get("byteOffset", 0):v.get("byteOffset", 0) + v["byteLength"]]
            nv = {"buffer": 0, "byteOffset": len(out), "byteLength": len(chunk)}
            if "byteStride" in v:
                nv["byteStride"] = v["byteStride"]
            views.append(nv)
            out += chunk + b"\0" * (-len(chunk) % 4)
        accessors = []
        for old in keep_acc:
            a = dict(doc["accessors"][old])
            a["bufferView"] = remap_view[a["bufferView"]]
            accessors.append(a)
        doc["accessors"], doc["bufferViews"], doc["buffers"] = accessors, views, [{"byteLength": len(out)}]
        for skin in doc.get("skins", []):
            skin["inverseBindMatrices"] = remap_acc[skin["inverseBindMatrices"]]
        for anim in doc["animations"]:
            for s in anim["samplers"]:
                s["input"], s["output"] = remap_acc[s["input"]], remap_acc[s["output"]]
        j = json.dumps(doc, separators=(",", ":")).encode()
        j += b" " * (-len(j) % 4)
        with open(path, "wb") as f:
            f.write(struct.pack("<4sII", b"glTF", 2, 12 + 8 + len(j) + 8 + len(out)))
            f.write(struct.pack("<I4s", len(j), b"JSON") + j)
            f.write(struct.pack("<I4s", len(out), b"BIN\0") + out)
        print(path, before // 1024, "KB ->", len(out) // 1024, "KB")


if __name__ == "__main__":
    main()
