"""Shrink a Tripo farm plot (berry, cress or mushroom bed, ~3,000 triangles) for the game, with no Blender.

    pip install numpy pillow meshoptimizer
    python3 Tools/convert_plot.py Tools/kin_src/Plot_berry.glb Assets/Models/Props/BerryPlot.glb --name berry_plot

Writes the plot 1 unit across (its widest side), centred, its base on y = 0, simplified to --full triangles with a 1024 px
texture, and <name>_lod.glb with --lod triangles and a 256 px texture for when it is small on screen. Prints its height
(in units of its width), which the game's PropModels wants.
"""
import argparse
import io
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "procedural"))
import convert_tripo_prop as tp  # noqa: E402
from decimate_props import simplify, smooth_normals, weld  # noqa: E402


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("dst")
    ap.add_argument("--name", default="plot")
    ap.add_argument("--full", type=int, default=1200)
    ap.add_argument("--lod", type=int, default=350)
    args = ap.parse_args()
    tp.NAME = args.name

    doc, binary = tp.read_glb(args.src)
    prim = doc["meshes"][0]["primitives"][0]
    at = prim["attributes"]
    pos = tp.accessor(doc, binary, at["POSITION"]).astype(np.float32)
    uv = tp.accessor(doc, binary, at["TEXCOORD_0"]).astype(np.float32)
    idx = tp.accessor(doc, binary, prim["indices"]).astype(np.uint32)
    view = doc["bufferViews"][doc["images"][0]["bufferView"]]
    off = view.get("byteOffset", 0)
    picture = Image.open(io.BytesIO(binary[off:off + view["byteLength"]])).convert("RGB")

    lo, hi = pos.min(axis=0), pos.max(axis=0)
    width = max(hi[0] - lo[0], hi[2] - lo[2])
    pos = (pos - [(lo[0] + hi[0]) / 2, lo[1], (lo[2] + hi[2]) / 2]) / width
    wpos, wuv, widx = weld(pos, uv, idx)

    def png(size):
        buf = io.BytesIO()
        picture.resize((size, size), Image.LANCZOS).save(buf, "PNG", optimize=True)
        return buf.getvalue()

    for path, tris, size in ((args.dst, args.full, 1024), (args.dst.replace(".glb", "_lod.glb"), args.lod, 256)):
        kept = simplify(wpos, wuv, widx, tris)
        used, remap = np.unique(kept, return_inverse=True)
        p, u = wpos[used], wuv[used]
        n = smooth_normals(p, remap.astype(np.uint32))
        tp.write(path, p.astype(np.float32), n, u.astype(np.float32), remap.reshape(-1).astype(np.uint32), png(size))
    print("height %.3f of width" % ((hi[1] - lo[1]) / width))


if __name__ == "__main__":
    main()
