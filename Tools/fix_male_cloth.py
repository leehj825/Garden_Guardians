"""Cut the male's hands loose from his vest and shorts in his skinned model (no source art needed: it works on the glb itself).

    pip install numpy pygltflib
    python3 Tools/fix_male_cloth.py Assets/Models/Bramblekin/Walking*.glb          # (not the female: Walking_female*.glb)
    python3 Tools/fix_male_cloth.py Assets/Models/Bramblekin/Walking*.glb --high 0.5 --low 0.35   # a second, gentler pass for the edges the first leaves

The hands hang at the hips in the bind pose, and the mesh (made in one piece) has faces joining the fists and forearms to the shorts and the vest's hem: a
triangle with a corner the arm drives (upper arm, forearm and hand weights, at least --high of the vertex) and another it hardly does (at most --low). When a hand
swings in a run or a blow those faces stretch and drag the cloth out with it. This deletes them (only below --max-z, so the shoulders keep their skin); the
fist and the cloth are left side by side, which is where the eye puts them anyway. The two passes above are what the shipped male went through; measure the result with GARDEN_STRETCH (Source/Kin/BramblekinStretch.cs).
"""
import argparse

import numpy as np
from pygltflib import GLTF2


def fix(path, high, low, max_z):
    g = GLTF2().load(path)
    blob = bytearray(g.binary_blob())
    prim = g.meshes[0].primitives[0]

    def view(index, width, dtype):
        a = g.accessors[index]
        bv = g.bufferViews[a.bufferView]
        off = (bv.byteOffset or 0) + (a.byteOffset or 0)
        return a, off, np.frombuffer(blob, dtype, a.count * width, off).reshape(-1, width)

    _, _, P = view(prim.attributes.POSITION, 3, "<f4")
    _, _, J = view(prim.attributes.JOINTS_0, 4, {5121: "u1", 5123: "<u2"}[g.accessors[prim.attributes.JOINTS_0].componentType])
    _, _, W = view(prim.attributes.WEIGHTS_0, 4, "<f4")
    ia, ioff, I = view(prim.indices, 1, "<u4" if g.accessors[prim.indices].componentType == 5125 else "<u2")
    tris = I.reshape(-1, 3)
    names = [g.nodes[j].name for j in g.skins[0].joints]
    pull = np.array([any(k in n for k in ("Arm", "Hand")) and "Shoulder" not in n for n in names])
    share = (W * pull[J]).sum(1)[tris]
    low_down = P[tris][:, :, 2].mean(1) < max_z  # (bind space is Z-up)
    cut = (share.max(1) >= high) & (share.min(1) <= low) & low_down
    kept = tris[~cut]
    dtype = I.dtype
    blob[ioff:ioff + I.nbytes] = kept.astype(dtype).tobytes() + bytes(I.nbytes - kept.size * dtype.itemsize)
    ia.count = int(kept.size)
    g.set_binary_blob(bytes(blob))
    g.save(path)
    print(path, ":", int(cut.sum()), "of", len(tris), "triangles joining an arm to the body deleted")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("files", nargs="+")
    ap.add_argument("--high", type=float, default=0.6)
    ap.add_argument("--low", type=float, default=0.2)
    ap.add_argument("--max-z", type=float, default=0.45)
    args = ap.parse_args()
    for f in args.files:
        fix(f, args.high, args.low, args.max_z)
