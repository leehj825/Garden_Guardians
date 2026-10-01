"""Make the guards' walk and stand clips from the sword walk (Tools/kin_src/SwordWalk.glb) - no Blender.

    pip install numpy pillow
    python3 Tools/make_guard_clips.py Tools/kin_src/SwordWalk.glb Assets/Models/Bramblekin

Writes GuardWalk.glb and GuardIdle.glb. The sword is part of each guard's mesh, held in the right fist with its blade standing up from the
hand (the mesh's rest pose, arm down at the side). The sword walk it came with lifts that hand to the shoulder, so the blade pointed
backward and, standing still (the walk's first frame), went through the face. So the right arm's clips are replaced:

  GuardWalk  the right arm hangs at the side as in the rest pose; the hand is turned so the blade points forward and a little up (WALK_TILT).
  GuardIdle  a still pose for standing, eating, resting: the walk frame with the feet closest together, the same arm, the blade a little
             more upright (IDLE_TILT). Every channel is held at that frame.

The tilt is a turn of the hand about its own Z axis (positive = the blade swings forward). Check the result with Tools/preview_pose.py.
"""
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import convert_female as cf  # noqa: E402
import preview_pose as pp  # noqa: E402

WALK_TILT = math.radians(40.0)   # the blade about 30 degrees above level, pointing forward
IDLE_TILT = math.radians(20.0)   # about 48 degrees: more upright
ARM_BONES = ("RightShoulder", "RightArm", "RightForeArm", "RightHand", "RightHandIndex1", "RightHandIndex2", "RightHandIndex3", "RightHandIndex4")
MESH = os.path.join(os.path.dirname(__file__), "..", "Assets", "Models", "Bramblekin", "Guard_male.glb")


def qmul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return np.array([aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
                     aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz])


def qz(angle):
    return np.array([0.0, 0.0, math.sin(angle / 2), math.cos(angle / 2)])


def output_view(doc, binary, accessor_index):
    """A writable float view of an accessor's data."""
    a = doc["accessors"][accessor_index]
    view = doc["bufferViews"][a["bufferView"]]
    width = {"SCALAR": 1, "VEC3": 3, "VEC4": 4}[a["type"]]
    start = view.get("byteOffset", 0) + a.get("byteOffset", 0)
    return np.frombuffer(binary, dtype="<f4", count=a["count"] * width, offset=start).reshape(a["count"], width)


def arm_clip(path, tilt):
    """The sword walk with the right arm hanging at the side, the blade tilted forward by <tilt>."""
    doc, binary = cf.read_glb(path)
    binary = bytearray(binary)
    nodes = doc["nodes"]
    mesh_doc, _ = cf.read_glb(MESH)  # (the clip's own nodes are its first pose; the rest pose is the mesh skeleton's)
    rest = {n["name"].split(":")[-1]: np.array(n.get("rotation", [0, 0, 0, 1]), float) for n in mesh_doc["nodes"]}
    anim = doc["animations"][0]
    for ch in anim["channels"]:
        name = nodes[ch["target"]["node"]]["name"].split(":")[-1]
        if name in ARM_BONES and ch["target"]["path"] == "rotation":
            q = rest[name] if name != "RightHand" else qmul(rest[name], qz(tilt))
            out = output_view(doc, binary, anim["samplers"][ch["sampler"]]["output"])
            out[:] = q
    return doc, binary


def held_frame(doc, binary, frame):
    """Every channel held at keyframe <frame>."""
    anim = doc["animations"][0]
    for ch in anim["channels"]:
        out = output_view(doc, binary, anim["samplers"][ch["sampler"]]["output"])
        out[:] = out[frame if len(out) > frame else 0].copy()  # (a track of two keys is a held value already)


def quietest_frame(path):
    """The keyframe of the walk (on the guard mesh) with the feet closest together, front to back."""
    doc, binary = cf.read_glb(MESH)
    at = doc["meshes"][0]["primitives"][0]["attributes"]
    pos = cf.accessor(doc, binary, at["POSITION"]).astype(float)
    left = (pos[:, 0] > 0.02) & (pos[:, 2] < 0.08)
    right = (pos[:, 0] < -0.02) & (pos[:, 2] < 0.08)
    cdoc, cbin = cf.read_glb(path)
    anim = cdoc["animations"][0]
    times = cf.accessor(cdoc, cbin, anim["samplers"][0]["input"]).astype(float)
    best, best_spread = 0, 1e9
    for i, t in enumerate(times):
        q, _, _, _, _ = pp.skinned(MESH, float(t), path)
        forward = q[np.argmin(pos[:, 1])] - q[np.argmax(pos[:, 1])]
        forward[1] = 0
        forward /= np.linalg.norm(forward)
        spread = abs(np.dot(q[left].mean(axis=0) - q[right].mean(axis=0), forward))
        if spread < best_spread:
            best, best_spread = i, spread
    return best


def main():
    src, out_dir = sys.argv[1], sys.argv[2]
    walk_doc, walk_bin = arm_clip(src, WALK_TILT)
    walk_path = os.path.join(out_dir, "GuardWalk.glb")
    cf.write_glb(walk_path, walk_doc, bytes(walk_bin))
    print("wrote", walk_path)

    idle_doc, idle_bin = arm_clip(src, IDLE_TILT)
    tmp = os.path.join(out_dir, "_idle_source.glb")
    cf.write_glb(tmp, idle_doc, bytes(idle_bin))
    frame = quietest_frame(tmp)
    os.remove(tmp)
    held_frame(idle_doc, idle_bin, frame)
    idle_path = os.path.join(out_dir, "GuardIdle.glb")
    cf.write_glb(idle_path, idle_doc, bytes(idle_bin))
    print("wrote", idle_path, "(held at keyframe %d)" % frame)


if __name__ == "__main__":
    main()
