"""Calm the arms of a walking clip: every arm rotation is pulled part of the way back to the skeleton's rest pose, in place.

    python3 Tools/damp_arm_swing.py Assets/Models/Bramblekin/Walking.glb 0.45

The Mixamo walk swings the arms wide with the elbows bent; on these chunky kin the near hand rides up and the two arms look unequal.
The factor is how much of the swing is kept (1 = all of it, 0 = arms rigid at the sides). Only the three arm bones on each side change
(not the fingers), and the file keeps its size. Run it on a clip freshly made by convert_tripo_kin.py / convert_fbx.py, once.
"""
import struct
import sys

import numpy as np

sys.path.insert(0, __file__.rsplit("/", 1)[0])
from convert_female import read_glb  # noqa: E402

ARMS = {side + bone for side in ("Left", "Right") for bone in ("Arm", "ForeArm", "Hand")}


def slerp(a, b, t):
    d = float(np.dot(a, b))
    if d < 0:
        b, d = -b, -d
    if d > 0.9995:
        r = a + t * (b - a)
        return r / np.linalg.norm(r)
    th = np.arccos(d)
    return (np.sin((1 - t) * th) * a + np.sin(t * th) * b) / np.sin(th)


def main():
    path, keep = sys.argv[1], float(sys.argv[2])
    doc, _ = read_glb(path)
    raw = bytearray(open(path, "rb").read())
    jlen = struct.unpack("<I", raw[12:16])[0]
    base = 20 + jlen + 8
    anim = doc["animations"][0]
    count = 0
    for ch in anim["channels"]:
        node = doc["nodes"][ch["target"]["node"]]
        if ch["target"]["path"] != "rotation" or node.get("name", "").split(":")[-1] not in ARMS:
            continue
        acc = doc["accessors"][anim["samplers"][ch["sampler"]]["output"]]
        view = doc["bufferViews"][acc["bufferView"]]
        assert acc["componentType"] == 5126 and acc["type"] == "VEC4"
        at = base + view.get("byteOffset", 0) + acc.get("byteOffset", 0)
        q = np.frombuffer(bytes(raw[at:at + acc["count"] * 16]), "<f4").reshape(-1, 4).astype(np.float64)
        rest = np.array(node.get("rotation", [0, 0, 0, 1]), np.float64)
        out = np.array([slerp(rest, k, keep) for k in q], "<f4")
        raw[at:at + out.nbytes] = out.tobytes()
        count += 1
    open(path, "wb").write(raw)
    print("damped", count, "arm channels in", path)


if __name__ == "__main__":
    main()
