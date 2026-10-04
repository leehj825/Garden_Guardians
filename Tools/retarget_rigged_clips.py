"""Fit the idle, walk and run clips of a rigged Tripo Bramblekin to the game's 33-joint skeleton, with no Blender.

    pip install numpy
    python3 Tools/retarget_rigged_clips.py Male_kin.glb Assets/Models/Bramblekin --walk-seconds 1.0333 --run-seconds 0.7

Writes Idle.glb, Walking.glb and Running.glb into the folder: the skeleton and the clip only (like the other clips there; the meshes are written by
convert_rigged_kin.py). The model's skeleton has other proportions and bone axes than the game's, so the clip is carried over as turns, not as angles:
for every bone, how far the model's bone has turned in the world from its own bind pose (a rotation about world axes) is applied to the game
skeleton's bone from the game's bind pose. Both stand Y-up and face +Z once posed (the game's bind space is Z-up: its clips turn the hips a quarter
turn about X to stand it up, which is what C is). The hips' movement is scaled by how much longer the game skeleton's legs are than the model's, and the
way it travels is taken out (the game moves the body itself): a walk's steady advance is subtracted from the hips, their sway and bounce stay.

  --walk-seconds / --run-seconds   play the clip over this long (a cycle of the model's own walk is 2.3 s, which would make the legs crawl at the
                                   game's walking pace: the walk the game had before took 1.03 s, the run 0.67 s). The idle keeps its own time.
"""
import argparse
import json
import math
import os
import struct
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from convert_female import accessor, read_glb  # noqa: E402

SKELETON = os.path.join(os.path.dirname(os.path.abspath(__file__)), "kin_skeleton.json")
HIPS = "Hips"
# The game's bind space is Z-up facing -Y; its clips stand the body up with a quarter turn about X (-90 degrees): (x, y, z) -> (x, z, -y).
C = np.array([[1.0, 0.0, 0.0], [0.0, 0.0, 1.0], [0.0, -1.0, 0.0]])


def q_to_m(q):
    x, y, z, w = q / np.linalg.norm(q)
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def m_to_q(m):
    t = m[0, 0] + m[1, 1] + m[2, 2]
    if t > 0:
        s = math.sqrt(t + 1.0) * 2
        q = [(m[2, 1] - m[1, 2]) / s, (m[0, 2] - m[2, 0]) / s, (m[1, 0] - m[0, 1]) / s, 0.25 * s]
    elif m[0, 0] > m[1, 1] and m[0, 0] > m[2, 2]:
        s = math.sqrt(1.0 + m[0, 0] - m[1, 1] - m[2, 2]) * 2
        q = [0.25 * s, (m[0, 1] + m[1, 0]) / s, (m[0, 2] + m[2, 0]) / s, (m[2, 1] - m[1, 2]) / s]
    elif m[1, 1] > m[2, 2]:
        s = math.sqrt(1.0 + m[1, 1] - m[0, 0] - m[2, 2]) * 2
        q = [(m[0, 1] + m[1, 0]) / s, 0.25 * s, (m[1, 2] + m[2, 1]) / s, (m[0, 2] - m[2, 0]) / s]
    else:
        s = math.sqrt(1.0 + m[2, 2] - m[0, 0] - m[1, 1]) * 2
        q = [(m[0, 2] + m[2, 0]) / s, (m[1, 2] + m[2, 1]) / s, 0.25 * s, (m[1, 0] - m[0, 1]) / s]
    q = np.array(q)
    return q / np.linalg.norm(q)


def slerp(a, b, t):
    d = float(np.dot(a, b))
    if d < 0:
        b, d = -b, -d
    if d > 0.9995:
        r = a + t * (b - a)
        return r / np.linalg.norm(r)
    th = math.acos(min(d, 1.0))
    return (math.sin((1 - t) * th) * a + math.sin(t * th) * b) / math.sin(th)


class Rig:
    """A glb's skeleton: node names, parents, rest transforms, and its animations as (times, values) per node and path."""

    def __init__(self, path):
        self.doc, self.bin = read_glb(path)
        nodes = self.doc["nodes"]
        self.names = [n.get("name", "").split(":")[-1] for n in nodes]
        self.parent = {c: i for i, n in enumerate(nodes) for c in n.get("children", [])}
        self.rest_rot = [np.array(n.get("rotation", [0, 0, 0, 1]), float) for n in nodes]
        self.rest_pos = [np.array(n.get("translation", [0, 0, 0]), float) for n in nodes]
        self.index = {n: i for i, n in enumerate(self.names)}

    def order(self, indices):
        """The given nodes with every parent before its children."""
        seen, out = set(), []

        def visit(i):
            if i in seen:
                return
            seen.add(i)
            if i in self.parent:
                visit(self.parent[i])
            out.append(i)
        for i in indices:
            visit(i)
        return out

    def rest_world(self):
        world = {}
        for i in self.order(range(len(self.names))):
            m = q_to_m(self.rest_rot[i])
            world[i] = world[self.parent[i]] @ m if i in self.parent else m
        return world

    def clip(self, name):
        anim = next(a for a in self.doc["animations"] if a.get("name") == name)
        out = {}
        for ch in anim["channels"]:
            s = anim["samplers"][ch["sampler"]]
            times = accessor(self.doc, self.bin, s["input"]).astype(float).reshape(-1)
            values = accessor(self.doc, self.bin, s["output"]).astype(float)
            out[(ch["target"]["node"], ch["target"]["path"])] = (times, values.reshape(len(times), -1), s.get("interpolation", "LINEAR"))
        return out


def sample(track, t, rotation):
    times, values, _ = track
    if len(times) == 1 or t <= times[0]:
        return values[0]
    if t >= times[-1]:
        return values[-1]
    i = int(np.searchsorted(times, t, side="right") - 1)
    f = (t - times[i]) / (times[i + 1] - times[i])
    return slerp(values[i], values[i + 1], f) if rotation else values[i] + (values[i + 1] - values[i]) * f


def retarget(model, clip_name, skeleton, game_names, seconds):
    """-> (times, {bone name: local rotation quaternions}, hips translations) for the game's skeleton."""
    tracks = model.clip(clip_name)
    grid = max((v[0] for k, v in tracks.items() if k[1] == "rotation"), key=len)
    duration = float(grid[-1] - grid[0])
    times = grid - grid[0]

    # The model's bind pose in the world, and the game's (Y-up, as its clips pose it).
    bind = model.rest_world()
    game_world = {}
    sk_parent = {c: i for i, n in enumerate(skeleton["nodes"]) for c in n.get("children", [])}
    sk_names = [n["name"].split(":")[-1] for n in skeleton["nodes"]]

    def game_rest(i):
        if i in game_world:
            return game_world[i]
        m = q_to_m(np.array(skeleton["nodes"][i].get("rotation", [0, 0, 0, 1]), float))
        game_world[i] = game_rest(sk_parent[i]) @ m if i in sk_parent else m
        return game_world[i]

    game_index = {n: i for i, n in enumerate(sk_names)}
    game_parent = {n: (sk_names[sk_parent[game_index[n]]] if game_index[n] in sk_parent and sk_names[sk_parent[game_index[n]]] in game_names else None) for n in game_names}
    game_bind_y = {n: C @ game_rest(game_index[n]) for n in game_names}

    bones = [model.index[n] for n in game_names]
    chain = model.order(bones)
    local_game = {n: [] for n in game_names}
    hips_t = []
    # The leg lengths (hips to ankle) of the two skeletons, to scale the hips' movement.
    hips_game = skeleton_bind_position(skeleton, "Hips")
    ankle_game = skeleton_bind_position(skeleton, "LeftFoot")
    hips_model = model_bind_position(model, "Hips")
    ankle_model = model_bind_position(model, "LeftFoot")
    k = (hips_game[2] - ankle_game[2]) / (hips_model[1] - ankle_model[1])
    base = C @ hips_game  # the hips' rest place once the game's clip has stood the body up (Y-up)
    hips_node = model.index[HIPS]
    htrack = tracks[(hips_node, "translation")]
    h0 = np.array(model.rest_pos[hips_node])
    first, last = sample(htrack, grid[0], False), sample(htrack, grid[-1], False)

    for f, t in enumerate(grid):
        local = {}
        for i in chain:
            r = tracks.get((i, "rotation"))
            local[i] = q_to_m(sample(r, t, True)) if r else q_to_m(model.rest_rot[i])
        world = {}
        for i in chain:
            world[i] = world[model.parent[i]] @ local[i] if i in model.parent else local[i]
        game_anim = {}
        for n in game_names:
            i = model.index[n]
            delta = world[i] @ bind[i].T  # how far the model's bone has turned in the world from its bind pose
            game_anim[n] = delta @ game_bind_y[n]
        for n in game_names:
            i = model.index[n]
            p = game_parent[n]
            m = game_anim[n] if p is None else game_anim[p].T @ game_anim[n]
            local_game[n].append(m_to_q(m))
        # Hips: the model's movement from its bind place, its steady advance (what a walk or run travels over the clip) taken out.
        pos = sample(htrack, t, False)
        share = (t - grid[0]) / duration if duration > 0 else 0.0
        pos = pos - (last - first) * share
        delta = pos - h0
        hips_t.append(base + k * delta)

    for n in game_names:  # (keep the quaternions on one side of the sphere, so that the clip interpolates the short way)
        qs = local_game[n]
        for f in range(1, len(qs)):
            if np.dot(qs[f], qs[f - 1]) < 0:
                qs[f] = -qs[f]
    scale = seconds / duration if seconds else 1.0
    return times * scale, {n: np.array(v) for n, v in local_game.items()}, np.array(hips_t)


def skeleton_bind_position(skeleton, name):
    """A joint's bind position (the game's bind space, Z-up) from its inverse bind matrix."""
    names = [skeleton["nodes"][j]["name"].split(":")[-1] for j in skeleton["joints"]]
    m = np.array(skeleton["inverseBindMatrices"][names.index(name)]).reshape(4, 4).T
    return np.linalg.inv(m)[:3, 3]


def model_bind_position(model, name):
    """A joint's bind position in the model's own world (Y-up), from its rest transforms."""
    world, pos = {}, {}
    for i in model.order(range(len(model.names))):
        m = q_to_m(model.rest_rot[i])
        if i in model.parent:
            p = model.parent[i]
            pos[i] = pos[p] + world[p] @ model.rest_pos[i]
            world[i] = world[p] @ m
        else:
            pos[i], world[i] = model.rest_pos[i].copy(), m
    return pos[model.index[name]]


def write_clip(path, template, game_names, times, rotations, hips, skeleton):
    """The skeleton and one animation, the way the other clips in Assets/Models/Bramblekin are: a rotation, a translation and a scale per bone."""
    nodes = []
    for node in template["nodes"]:
        nodes.append({k: v for k, v in node.items() if k not in ("mesh", "skin")})
    names = [n.get("name", "").split(":")[-1] for n in nodes]
    blobs, views, accessors = [], [], []

    def add(array, kind, count, bounds=None):
        data = np.ascontiguousarray(array, "<f4").tobytes()
        pad = (-sum(len(b) for b in blobs)) % 4
        if pad:
            blobs.append(b"\0" * pad)
        views.append({"buffer": 0, "byteOffset": sum(len(b) for b in blobs), "byteLength": len(data)})
        blobs.append(data)
        a = {"bufferView": len(views) - 1, "byteOffset": 0, "componentType": 5126, "count": count, "type": kind}
        if bounds:
            a["min"], a["max"] = bounds
        accessors.append(a)
        return len(accessors) - 1

    rest_offsets = {nd["name"].split(":")[-1]: nd.get("translation", [0, 0, 0]) for nd in skeleton["nodes"]}  # (the offsets every clip of the game carries: the skeleton's own)
    ibm = add(np.array(skeleton["inverseBindMatrices"], "<f4"), "MAT4", len(skeleton["inverseBindMatrices"]))
    end = float(times[-1])
    time_acc = add(times, "SCALAR", len(times), ([float(times[0])], [end]))
    pair = add(np.array([0.0, end]), "SCALAR", 2, ([0.0], [end]))
    samplers, channels = [], []

    def channel(node, path_name, in_acc, values, kind):
        samplers.append({"input": in_acc, "output": add(values, kind, len(values)), "interpolation": "LINEAR"})
        channels.append({"sampler": len(samplers) - 1, "target": {"node": node, "path": path_name}})

    for n in game_names:
        i = names.index(n)
        channel(i, "rotation", time_acc, rotations[n], "VEC4")
        rest = np.array(rest_offsets[n], float)
        if n == HIPS:
            channel(i, "translation", time_acc, hips, "VEC3")
        else:
            channel(i, "translation", pair, np.tile(rest, (2, 1)), "VEC3")
        channel(i, "scale", pair, np.ones((2, 3)), "VEC3")
    doc = {
        "asset": {"version": "2.0", "generator": "Tools/retarget_rigged_clips.py"},
        "scene": 0, "scenes": template["scenes"], "nodes": nodes,
        "skins": [{"inverseBindMatrices": ibm, "joints": template["skins"][0]["joints"], "name": "Armature"}],
        "animations": [{"name": os.path.splitext(os.path.basename(path))[0], "samplers": samplers, "channels": channels}],
        "accessors": accessors, "bufferViews": views, "buffers": [{"byteLength": sum(len(b) for b in blobs)}],
    }
    text = json.dumps(doc, separators=(",", ":")).encode()
    text += b" " * ((-len(text)) % 4)
    binary = b"".join(blobs)
    binary += b"\0" * ((-len(binary)) % 4)
    with open(path, "wb") as f:
        f.write(struct.pack("<4sII", b"glTF", 2, 12 + 8 + len(text) + 8 + len(binary)))
        f.write(struct.pack("<I4s", len(text), b"JSON") + text)
        f.write(struct.pack("<I4s", len(binary), b"BIN\0") + binary)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("model", help="the rigged glb with idle, walk and run animations")
    ap.add_argument("folder", help="where Idle.glb, Walking.glb and Running.glb are written")
    ap.add_argument("--template", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Models", "Bramblekin", "GatheringObjects.glb"),
                    help="a clip of the game's, whose skeleton nodes (Y-up, as the clips pose them) the new clips are written on")
    ap.add_argument("--walk-seconds", type=float, default=1.0333)
    ap.add_argument("--run-seconds", type=float, default=0.7)
    ap.add_argument("--idle-seconds", type=float, default=0.0, help="0 keeps the clip's own length")
    args = ap.parse_args()

    skeleton = json.load(open(SKELETON))
    game_names = [skeleton["nodes"][j]["name"].split(":")[-1] for j in skeleton["joints"]]
    model = Rig(args.model)
    template, _ = read_glb(args.template)
    for clip, file_name, seconds in (("idle", "Idle.glb", args.idle_seconds), ("walk", "Walking.glb", args.walk_seconds), ("run", "Running.glb", args.run_seconds)):
        times, rotations, hips = retarget(model, clip, skeleton, game_names, seconds)
        path = os.path.join(args.folder, file_name)
        write_clip(path, template, game_names, times, rotations, hips, skeleton)
        print("wrote", path, len(times), "frames over %.2f s" % times[-1], "hips travel", (hips.max(axis=0) - hips.min(axis=0)).round(3))


if __name__ == "__main__":
    main()
