"""Pull the arrow out of the Tripo weapons sheet (one mesh holding the shield, sword, spear, bow, quiver, helmet and an arrow) for the game.

    pip install numpy pillow pygltflib
    python3 Tools/extract_arrow.py Weapons.glb Assets/Models/Props/Gear/Arrow.glb

The sheet is a single mesh, so the arrow is found by where it lies (a box round it in the glTF's own units, see BOX) and its triangles copied out with
their normals and UVs. It is laid along +X, centred, 1 unit long with the point at +X (like the other gear: see Source/Engine/GearModels.cs), and the
4096 px texture is shrunk to --texture px (the arrow uses only a few small islands of it).
"""
import argparse
import io

import numpy as np
from PIL import Image
from pygltflib import (ARRAY_BUFFER, ELEMENT_ARRAY_BUFFER, GLTF2, Accessor, Asset, Attributes, Buffer, BufferView, Image as GImage, Material,
                       Mesh, Node, PbrMetallicRoughness, Primitive, Sampler, Scene, Texture, TextureInfo)

# The arrow stands upright in the sheet, point up and fletching down: x 0.16-0.26, z -0.3 to -0.1 (glTF units, y up).
BOX = (0.16, 0.26, -0.3, -0.1)


def read(g, blob, index, width, dtype):
    a = g.accessors[index]
    bv = g.bufferViews[a.bufferView]
    return np.frombuffer(blob, dtype, a.count * width, (bv.byteOffset or 0) + (a.byteOffset or 0)).reshape(-1, width)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("dst")
    ap.add_argument("--texture", type=int, default=512)
    args = ap.parse_args()

    g = GLTF2().load(args.src)
    blob = g.binary_blob()
    prim = g.meshes[0].primitives[0]
    pos = read(g, blob, prim.attributes.POSITION, 3, "<f4")
    nrm = read(g, blob, prim.attributes.NORMAL, 3, "<f4")
    uv = read(g, blob, prim.attributes.TEXCOORD_0, 2, "<f4")
    idx = read(g, blob, prim.indices, 1, "<u4" if g.accessors[prim.indices].componentType == 5125 else "<u2").astype(np.int64).reshape(-1, 3)

    x0, x1, z0, z1 = BOX
    inside = (pos[:, 0] >= x0) & (pos[:, 0] <= x1) & (pos[:, 2] >= z0) & (pos[:, 2] <= z1)
    tris = idx[inside[idx].all(axis=1)]
    used, remap = np.unique(tris.ravel(), return_inverse=True)
    p, n, t = pos[used].copy(), nrm[used].copy(), uv[used].copy()
    tris = remap.reshape(-1, 3)
    print(len(tris), "triangles,", len(used), "vertices")

    # Upright (point at +Y) to lying along +X, point at +X: (x, y, z) -> (y, -x, z), a proper turn; then centred and 1 unit long.
    def turn(v):
        return np.stack([v[:, 1], -v[:, 0], v[:, 2]], 1)

    p, n = turn(p), turn(n)
    centre = (p.min(0) + p.max(0)) / 2
    length = p[:, 0].max() - p[:, 0].min()
    p = (p - centre) / length
    print("length", length, "bounds", p.min(0), p.max(0))

    out = GLTF2()
    out.asset = Asset(version="2.0", generator="Tools/extract_arrow.py")
    parts = [p.astype("<f4").tobytes(), n.astype("<f4").tobytes(), t.astype("<f4").tobytes(), tris.astype("<u4").ravel().tobytes()]
    # The texture, shrunk.
    img = g.images[g.materials[prim.material].pbrMetallicRoughness.baseColorTexture.index and g.textures[g.materials[prim.material].pbrMetallicRoughness.baseColorTexture.index].source or 0]
    bv = g.bufferViews[img.bufferView]
    tex = Image.open(io.BytesIO(blob[(bv.byteOffset or 0):(bv.byteOffset or 0) + bv.byteLength])).convert("RGB").resize((args.texture, args.texture), Image.LANCZOS)
    buf = io.BytesIO()
    tex.save(buf, "PNG")
    parts.append(buf.getvalue())

    data = bytearray()
    views = []
    for i, part in enumerate(parts):
        while len(data) % 4:
            data.append(0)
        views.append(BufferView(buffer=0, byteOffset=len(data), byteLength=len(part), target=ELEMENT_ARRAY_BUFFER if i == 3 else (ARRAY_BUFFER if i < 3 else None)))
        data += part
    out.buffers = [Buffer(byteLength=len(data))]
    out.bufferViews = views
    out.accessors = [
        Accessor(bufferView=0, componentType=5126, count=len(p), type="VEC3", min=p.min(0).tolist(), max=p.max(0).tolist()),
        Accessor(bufferView=1, componentType=5126, count=len(n), type="VEC3"),
        Accessor(bufferView=2, componentType=5126, count=len(t), type="VEC2"),
        Accessor(bufferView=3, componentType=5125, count=int(tris.size), type="SCALAR"),
    ]
    out.images = [GImage(bufferView=4, mimeType="image/png")]
    out.samplers = [Sampler(magFilter=9729, minFilter=9987, wrapS=10497, wrapT=10497)]
    out.textures = [Texture(source=0, sampler=0)]
    out.materials = [Material(name="arrow", pbrMetallicRoughness=PbrMetallicRoughness(baseColorTexture=TextureInfo(index=0), metallicFactor=0.0, roughnessFactor=1.0))]
    out.meshes = [Mesh(name="Arrow", primitives=[Primitive(attributes=Attributes(POSITION=0, NORMAL=1, TEXCOORD_0=2), indices=3, material=0)])]
    out.nodes = [Node(mesh=0, name="Arrow")]
    out.scenes = [Scene(nodes=[0])]
    out.scene = 0
    out.set_binary_blob(bytes(data))
    out.save(args.dst)
    print("wrote", args.dst)


if __name__ == "__main__":
    main()
