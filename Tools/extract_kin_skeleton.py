"""Write the game's Bramblekin skeleton (its 35 nodes, joint order and inverse bind matrices) from a glb that carries it, as Tools/kin_skeleton.json.

    python3 Tools/extract_kin_skeleton.py <glb with the skeleton> Tools/kin_skeleton.json

The skeleton was taken from the original Walking.glb (the 33-joint Mixamo rig every clip in Assets/Models/Bramblekin is built on). convert_rigged_kin.py
writes the skinned meshes onto it and retarget_rigged_clips.py fits new clips to it, so neither needs that glb any more.
"""
import json
import sys

import numpy as np

sys.path.insert(0, __file__.rsplit("/", 1)[0] if "/" in __file__ else ".")
from convert_female import accessor, read_glb  # noqa: E402


def main():
    doc, binary = read_glb(sys.argv[1])
    skin = doc["skins"][0]
    ibm = accessor(doc, binary, skin["inverseBindMatrices"]).reshape(-1, 16)
    nodes = []
    for n in doc["nodes"]:
        keep = {k: n[k] for k in ("name", "translation", "rotation", "scale", "children") if k in n}
        if "mesh" in n:
            keep["mesh"] = True  # the node the skinned mesh hangs on
        nodes.append(keep)
    out = {"nodes": nodes, "joints": skin["joints"], "inverseBindMatrices": [[round(float(v), 7) for v in row] for row in ibm]}
    with open(sys.argv[2], "w") as f:
        json.dump(out, f, indent=None, separators=(",", ":"))
    print("wrote", sys.argv[2], len(nodes), "nodes", len(skin["joints"]), "joints")


if __name__ == "__main__":
    main()
