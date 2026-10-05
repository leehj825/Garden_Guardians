"""Hands to the ground in the pick-up clip, bent knees in the jump clip - no Blender.

    pip install numpy
    python3 Tools/fix_pickup_jump_clips.py Assets/Models/Bramblekin

Run it once, on the clips as converted (it works on the rotations of the file in place; the files keep their size).

PickingUp.glb: the Mixamo clip spreads both arms out to the sides and bends the legs oddly. In every frame the two arms are set so they hang
  straight down in the world, as they do in the standing Idle pose, however far the body bends - so while the kin folds over, the hands drop to
  the ground in front of it - and the legs stand straight under the hips as in Idle (the shoulders, forearms and hands are Idle's own rotations).
Jump.glb: the arms are held as in Idle (hanging at the sides), the clip's own arm movements dropped. The clip throws the legs back and out. In every frame the thighs are brought forward ~HIP degrees from Idle's standing legs and the
  shins back ~KNEE degrees, in the kin's own forward direction, so the knees are bent a little and the feet tucked up under it; the feet are
  held flat as in Idle.
"""
import json
import math
import struct
import sys

import numpy as np

HIP, KNEE = 30.0, 55.0


# ---- glb ---------------------------------------------------------------------------------------------------------------

def read_glb(path):
    raw = bytearray(open(path, "rb").read())
    jlen = struct.unpack("<I", raw[12:16])[0]
    doc = json.loads(bytes(raw[20:20 + jlen]))
    return doc, raw, 20 + jlen + 8


def accessor(doc, raw, base, index):
    """A writable float view of an accessor."""
    a = doc["accessors"][index]
    view = doc["bufferViews"][a["bufferView"]]
    width = {"SCALAR": 1, "VEC3": 3, "VEC4": 4}[a["type"]]
    assert a["componentType"] == 5126
    start = base + view.get("byteOffset", 0) + a.get("byteOffset", 0)
    return np.frombuffer(raw, dtype="<f4", count=a["count"] * width, offset=start).reshape(a["count"], width)


class Rig:
    """The skeleton of a glb with its first animation: world rotations and positions at a frame."""

    def __init__(self, path):
        self.doc, self.raw, self.base = read_glb(path)
        self.nodes = self.doc["nodes"]
        self.parent = [-1] * len(self.nodes)
        for i, n in enumerate(self.nodes):
            for c in n.get("children", []):
                self.parent[c] = i
        self.index = {n["name"].split(":")[-1]: i for i, n in enumerate(self.nodes)}
        self.tracks = {}  # (node index, path) -> view
        anim = self.doc["animations"][0]
        for ch in anim["channels"]:
            out = accessor(self.doc, self.raw, self.base, anim["samplers"][ch["sampler"]]["output"])
            self.tracks[(ch["target"]["node"], ch["target"]["path"])] = out
        self.frames = max(len(v) for v in self.tracks.values())

    def local(self, node, frame, overrides):
        n = self.nodes[node]
        t = np.array(n.get("translation", [0, 0, 0]), float)
        r = np.array(n.get("rotation", [0, 0, 0, 1]), float)
        s = np.array(n.get("scale", [1, 1, 1]), float)
        for path, current in (("translation", t), ("rotation", r), ("scale", s)):
            v = self.tracks.get((node, path))
            if v is not None:
                current[:] = v[min(frame, len(v) - 1)]
        if node in overrides:
            r = overrides[node]
        return t, r, s

    def world(self, frame, overrides=None):
        """{node: (3x3 world rotation, world position)} at <frame>; <overrides> maps node -> a local rotation quaternion."""
        overrides = overrides or {}
        out = {}

        def get(i):
            if i in out:
                return out[i]
            t, r, s = self.local(i, frame, overrides)
            m = np.eye(4)
            m[:3, :3] = quat_to_mat(r) * s
            m[:3, 3] = t
            if self.parent[i] >= 0:
                m = get_matrix(self.parent[i]) @ m
            mats[i] = m
            out[i] = m
            return m

        mats = {}

        def get_matrix(i):
            get(i)
            return mats[i]

        for i in range(len(self.nodes)):
            get(i)
        result = {}
        for i, m in mats.items():
            rot = m[:3, :3].copy()
            scale = np.linalg.norm(rot, axis=0)
            result[i] = (rot / scale, m[:3, 3].copy())
        return result


def quat_to_mat(q):
    x, y, z, w = q / np.linalg.norm(q)
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def mat_to_quat(m):
    t = m[0, 0] + m[1, 1] + m[2, 2]
    if t > 0:
        s = math.sqrt(t + 1) * 2
        q = [(m[2, 1] - m[1, 2]) / s, (m[0, 2] - m[2, 0]) / s, (m[1, 0] - m[0, 1]) / s, 0.25 * s]
    elif m[0, 0] > m[1, 1] and m[0, 0] > m[2, 2]:
        s = math.sqrt(1 + m[0, 0] - m[1, 1] - m[2, 2]) * 2
        q = [0.25 * s, (m[0, 1] + m[1, 0]) / s, (m[0, 2] + m[2, 0]) / s, (m[2, 1] - m[1, 2]) / s]
    elif m[1, 1] > m[2, 2]:
        s = math.sqrt(1 + m[1, 1] - m[0, 0] - m[2, 2]) * 2
        q = [(m[0, 1] + m[1, 0]) / s, 0.25 * s, (m[1, 2] + m[2, 1]) / s, (m[0, 2] - m[2, 0]) / s]
    else:
        s = math.sqrt(1 + m[2, 2] - m[0, 0] - m[1, 1]) * 2
        q = [(m[0, 2] + m[2, 0]) / s, (m[1, 2] + m[2, 1]) / s, 0.25 * s, (m[1, 0] - m[0, 1]) / s]
    q = np.array(q)
    return q / np.linalg.norm(q)


def axis_rotation(axis, degrees):
    axis = axis / np.linalg.norm(axis)
    a = math.radians(degrees)
    k = np.array([[0, -axis[2], axis[1]], [axis[2], 0, -axis[0]], [-axis[1], axis[0], 0]])
    return np.eye(3) + math.sin(a) * k + (1 - math.cos(a)) * (k @ k)


def set_rotation(rig, name, frame, quat):
    view = rig.tracks[(rig.index[name], "rotation")]
    # (keep the quaternion on the same side as the previous key, so nothing flips between frames)
    if frame > 0 and np.dot(view[frame - 1], quat) < 0:
        quat = -quat
    view[frame] = quat


# ---- the two clips ----------------------------------------------------------------------------------------------------------

def copy_idle(rig, idle, names):
    """The first frame of Idle's rotations for <names> in every frame of the clip."""
    for name in names:
        view = rig.tracks.get((rig.index[name], "rotation"))
        first = idle.tracks[(idle.index[name], "rotation")][0]
        if view is not None:
            view[:] = first


def hold_world(rig, idle, standing, names):
    """In every frame, each of <names> (parents first) keeps the world orientation it has in Idle, whatever the body above it does."""
    for frame in range(rig.frames):
        for name in names:
            parent = rig.world(frame)[rig.parent[rig.index[name]]][0]
            set_rotation(rig, name, frame, mat_to_quat(parent.T @ standing[idle.index[name]][0]))


def fix_arms(rig, idle, standing):
    """Arms hanging straight down at the sides, as in Idle, in every frame."""
    sides = ("Left", "Right")
    copy_idle(rig, idle, [s + p for s in sides for p in ("Shoulder", "ForeArm", "Hand")])
    hold_world(rig, idle, standing, [s + "Arm" for s in sides])


def fix_pickup(folder):
    rig, idle = Rig(f"{folder}/PickingUp.glb"), Rig(f"{folder}/Idle.glb")
    sides = ("Left", "Right")
    standing = idle.world(0)
    fix_arms(rig, idle, standing)
    # The legs stand straight under the hips as in Idle (the clip bends them oddly as the body folds over).
    copy_idle(rig, idle, [s + p for s in sides for p in ("ToeBase", "Toe_End")])
    hold_world(rig, idle, standing, [s + p for s in sides for p in ("UpLeg", "Leg", "Foot")])
    open(f"{folder}/PickingUp.glb", "wb").write(rig.raw)


def fix_jump(folder):
    rig, idle = Rig(f"{folder}/Jump.glb"), Rig(f"{folder}/Idle.glb")
    sides = ("Left", "Right")
    standing = idle.world(0)
    pos = lambda n: standing[idle.index[n]][1]  # noqa: E731
    side_axis = pos("LeftUpLeg") - pos("RightUpLeg")
    forward = pos("LeftToeBase") - pos("LeftFoot")
    forward[1] = 0  # (up here is the skeleton's own up: take the horizontal part of the way the toes point)
    up = pos("Head") - pos("Hips") if "Head" in idle.index else np.array([0.0, 1.0, 0.0])
    up = up / np.linalg.norm(up)
    forward = forward - np.dot(forward, up) * up
    forward /= np.linalg.norm(forward)
    thigh_dir = pos("LeftLeg") - pos("LeftUpLeg")
    thigh_dir /= np.linalg.norm(thigh_dir)
    # Which way round the side axis swings a hanging thigh forward?
    sign = 1.0 if np.dot(axis_rotation(side_axis, 10) @ thigh_dir, forward) > np.dot(thigh_dir, forward) else -1.0
    hip_turn = axis_rotation(side_axis, sign * HIP)
    knee_turn = axis_rotation(side_axis, -sign * KNEE)

    for frame in range(rig.frames):
        w = rig.world(frame)
        for side in sides:
            up_leg, leg, foot = side + "UpLeg", side + "Leg", side + "Foot"
            parent = w[rig.parent[rig.index[up_leg]]][0]
            thigh = hip_turn @ standing[idle.index[up_leg]][0]
            shin = knee_turn @ standing[idle.index[leg]][0]
            flat = standing[idle.index[foot]][0]
            set_rotation(rig, up_leg, frame, mat_to_quat(parent.T @ thigh))
            set_rotation(rig, leg, frame, mat_to_quat(thigh.T @ shin))
            set_rotation(rig, foot, frame, mat_to_quat(shin.T @ flat))
    copy_idle(rig, idle, [s + "ToeBase" for s in sides] + [s + "Toe_End" for s in sides])
    fix_arms(rig, idle, standing)  # (the arms are left as they are standing, not thrown about)
    open(f"{folder}/Jump.glb", "wb").write(rig.raw)


def main():
    folder = sys.argv[1].rstrip("/")
    fix_pickup(folder)
    fix_jump(folder)
    print("done")


if __name__ == "__main__":
    main()
