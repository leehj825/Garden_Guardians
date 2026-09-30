"""Rig the low-poly Tripo wolf spider (Tools/kin_src/Spider.glb, ~485 triangles) and give it a walk - no Blender.

    pip install numpy pillow
    python3 Tools/convert_spider.py Tools/kin_src/Spider.glb Assets/Models/Props/Spider.glb --preview Tools/previews/spider_walk.png

The model Tripo made carries a skeleton that does nothing (every vertex is weighted to one bone, the bones have no
positions), so this throws it away and builds its own: a root bone for the body, and for each of the eight legs a hip
bone (where the leg leaves the body) and a knee bone (the joint at the leg's highest point). Vertices are weighted by
where they lie along their leg. The clip "Walk" is a one-second loop in the usual spider's tetrapod gait: the legs go
in two groups of four (front-left, second-right, third-left, rear-right, then the other four), each leg sweeping back
along the ground and then lifting and swinging forward.

The model is turned to face -X (as the game expects; its head faces +Z in the Tripo file), scaled to 1 unit across,
its feet on y = 0, and its texture shrunk and stored as PNG (this raylib build can't read an embedded JPEG).
"""
import argparse
import io
import json
import math
import struct

import numpy as np
from PIL import Image, ImageDraw

from convert_tripo_prop import accessor, pad, read_glb

# Where the legs go (degrees round the body, found by looking at the model): rear pair to front pair, each side.
LEG_ANGLES = (41.0, 73.0, 112.0, 144.0)
BODY_CENTRE = np.array([-0.05, 0.0])   # where the legs meet the body, in the game's frame (x, z)
HIP_RADIUS = 0.18                      # the hip sits this far from that centre, along the leg
LEG_HALF_WIDTH = 0.10                  # a vertex within this of a leg's line, and beyond the hip, is part of the leg
SWING = math.radians(17.0)             # how far a leg sweeps each way
LIFT = math.radians(26.0)              # how far the hip raises a leg as it swings forward
KNEE_LIFT = math.radians(22.0)         # and the knee folds up this much more
FRAMES = 30
DURATION = 1.0


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def quat_axis(axis, angle):
    axis = np.asarray(axis, float)
    axis = axis / np.linalg.norm(axis)
    return np.array([*(axis * math.sin(angle / 2)), math.cos(angle / 2)])


def quat_mul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return np.array([aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
                     aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz])


def quat_matrix(q):
    x, y, z, w = q
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def build_rig(Q):
    """Legs, hips and knees, and the skin weights: returns (legs, joints (n,4), weights (n,4))."""
    d = Q[:, [0, 2]] - BODY_CENTRE
    legs = []
    for side in (1, -1):
        for j, deg in enumerate(LEG_ANGLES):
            direction = np.array([math.cos(math.radians(deg)), side * math.sin(math.radians(deg))])
            # The leg's tip: the far end of the vertices near this leg's line.
            along = d @ direction
            across = np.abs(d[:, 0] * direction[1] - d[:, 1] * direction[0])
            near = (across < LEG_HALF_WIDTH) & (along > 0.3)
            tip2 = d[near][np.argmax(along[near])]
            length_dir = tip2 / np.linalg.norm(tip2)
            legs.append({"side": side, "index": j, "dir": length_dir, "tip": tip2})

    n = len(Q)
    leg_of = np.full(n, -1)
    t_along = np.zeros(n)
    best = np.full(n, 1e9)
    for k, leg in enumerate(legs):
        hip2 = leg["dir"] * HIP_RADIUS
        seg = leg["tip"] - hip2
        length2 = seg @ seg
        t = ((d - hip2) @ seg) / length2
        point = hip2 + np.clip(t, 0, 1)[:, None] * seg
        dist = np.linalg.norm(d - point, axis=1)
        ok = (dist < LEG_HALF_WIDTH) & (t > -0.3) & (np.linalg.norm(d, axis=1) > HIP_RADIUS * 0.9) & (dist < best)
        leg_of[ok] = k
        t_along[ok] = t[ok]
        best[ok] = dist[ok]

    for k, leg in enumerate(legs):
        members = leg_of == k
        hip2 = leg["dir"] * HIP_RADIUS
        hip_y = float(np.interp(0.0, np.sort(t_along[members]), Q[members][np.argsort(t_along[members])][:, 1]))
        mid = members & (t_along > 0.15) & (t_along < 0.85)
        top = Q[mid][:, 1].max()
        high = mid & (Q[:, 1] > top * 0.92)
        knee_t = float(t_along[high].mean())
        leg["hip"] = np.array([hip2[0] + BODY_CENTRE[0], Q[members][:, 1].max() * 0.6, hip2[1] + BODY_CENTRE[1]])
        seg = leg["tip"] - hip2
        knee2 = hip2 + seg * knee_t
        leg["knee"] = np.array([knee2[0] + BODY_CENTRE[0], float(Q[high][:, 1].mean()), knee2[1] + BODY_CENTRE[1]])
        leg["knee_t"] = knee_t
        del hip_y

    joints = np.zeros((n, 4), np.uint16)
    weights = np.zeros((n, 4), np.float32)
    weights[:, 0] = 1.0
    for v in range(n):
        k = leg_of[v]
        if k < 0:
            continue
        t = t_along[v]
        to_hip = smoothstep(0.0, 0.3, t)
        to_knee = smoothstep(legs[k]["knee_t"] - 0.12, legs[k]["knee_t"] + 0.12, t)
        root_w, hip_w, knee_w = 1 - to_hip, to_hip * (1 - to_knee), to_hip * to_knee
        joints[v] = [0, 1 + 2 * k, 2 + 2 * k, 0]
        weights[v] = [root_w, hip_w, knee_w, 0.0]
    return legs, joints, weights


def gait(legs, time01):
    """Local rotations (quaternions) for every joint at <time01> of the loop: root, then a hip and a knee per leg."""
    forward = np.array([-1.0, 0.0, 0.0])
    rotations = [np.array([0.0, 0.0, 0.0, 1.0])]
    for leg in legs:
        d3 = np.array([leg["dir"][0], 0.0, leg["dir"][1]])
        # Tetrapod gait: alternate legs along the body and across it move together.
        group = (leg["index"] + (0 if leg["side"] == 1 else 1)) % 2
        c = (time01 + 0.5 * group) % 1.0
        if c < 0.5:                        # stance: the foot sweeps back along the ground
            swing = 1 - 4 * c              # +1 (forward) to -1 (back)
            lift = 0.0
        else:                              # swing: up and forward
            u = (c - 0.5) * 2
            swing = -1 + 2 * (u * u * (3 - 2 * u))
            lift = math.sin(math.pi * u)
        sign = 1.0 if np.dot(np.cross([0, 1, 0], d3), forward) > 0 else -1.0
        yaw = quat_axis([0, 1, 0], sign * SWING * swing)
        axis = np.cross(d3, [0, 1, 0])     # raises the leg
        hip = quat_mul(yaw, quat_axis(axis, LIFT * lift))
        knee = quat_axis(axis, KNEE_LIFT * lift)
        rotations += [hip, knee]
    return rotations


def pose_vertices(Q, legs, joints, weights, rotations):
    """The linear-blend-skinned vertices for a set of local joint rotations (for the preview)."""
    positions = [np.zeros(3)]
    parents = [None]
    for k, leg in enumerate(legs):
        positions += [leg["hip"], leg["knee"]]
        parents += [0, 1 + 2 * k]
    world = [np.eye(4) for _ in positions]
    for i, (p, parent) in enumerate(zip(positions, parents)):
        local = np.eye(4)
        local[:3, :3] = quat_matrix(rotations[i])
        local[:3, 3] = p - (positions[parent] if parent is not None else 0)
        world[i] = local if parent is None else world[parent] @ local
    out = np.zeros_like(Q)
    for i in range(len(Q)):
        acc = np.zeros(3)
        for slot in range(4):
            w = weights[i, slot]
            if w <= 0:
                continue
            j = joints[i, slot]
            bind = np.eye(4)
            bind[:3, 3] = positions[j]
            m = world[j] @ np.linalg.inv(bind)
            acc += w * (m[:3, :3] @ Q[i] + m[:3, 3])
        out[i] = acc
    return out


def write(path, Q, nrm, uv, idx, joints, weights, legs, png):
    bones = 1 + 2 * len(legs)
    positions = [np.zeros(3)]
    parents = [-1]
    names = ["body"]
    for k, leg in enumerate(legs):
        side = "L" if leg["side"] == 1 else "R"
        positions += [leg["hip"], leg["knee"]]
        parents += [0, 1 + 2 * k]
        names += [f"hip_{side}{leg['index']}", f"knee_{side}{leg['index']}"]

    ibm = np.zeros((bones, 4, 4), np.float32)
    for i, p in enumerate(positions):
        m = np.eye(4)
        m[:3, 3] = -np.asarray(p)
        ibm[i] = m.T                       # glTF stores matrices column-major

    times = np.arange(FRAMES + 1, dtype=np.float32) / FRAMES * DURATION
    tracks = np.zeros((bones, FRAMES + 1, 4), np.float32)
    for f in range(FRAMES + 1):
        for i, q in enumerate(gait(legs, (f % FRAMES) / FRAMES)):
            tracks[i, f] = q

    blobs = []

    def blob(arr):
        data = pad(np.ascontiguousarray(arr).tobytes())
        blobs.append(data)
        return len(blobs) - 1

    views = {}
    views["pos"] = blob(Q.astype(np.float32))
    views["nrm"] = blob(nrm)
    views["uv"] = blob(uv)
    views["idx"] = blob(idx.astype(np.uint16))
    views["joints"] = blob(joints.astype(np.uint8))
    views["weights"] = blob(weights)
    views["ibm"] = blob(ibm)
    views["time"] = blob(times)
    track_views = [blob(tracks[i]) for i in range(bones)]
    views["png"] = blob(np.frombuffer(png, np.uint8))
    lengths = {"pos": Q.astype(np.float32).nbytes, "nrm": nrm.nbytes, "uv": uv.nbytes, "idx": idx.astype(np.uint16).nbytes,
               "joints": len(joints) * 4, "weights": weights.nbytes, "ibm": ibm.nbytes, "time": times.nbytes, "png": len(png)}
    offsets = np.cumsum([0] + [len(b) for b in blobs])
    buffer_views = [{"buffer": 0, "byteOffset": int(offsets[i]), "byteLength": 0} for i in range(len(blobs))]
    for name, i in views.items():
        buffer_views[i]["byteLength"] = lengths[name]
    for i, v in enumerate(track_views):
        buffer_views[v]["byteLength"] = tracks[i].nbytes

    accessors = [
        {"bufferView": views["pos"], "componentType": 5126, "count": len(Q), "type": "VEC3", "min": Q.min(axis=0).tolist(), "max": Q.max(axis=0).tolist()},
        {"bufferView": views["nrm"], "componentType": 5126, "count": len(nrm), "type": "VEC3"},
        {"bufferView": views["uv"], "componentType": 5126, "count": len(uv), "type": "VEC2"},
        {"bufferView": views["idx"], "componentType": 5123, "count": len(idx), "type": "SCALAR"},
        {"bufferView": views["joints"], "componentType": 5121, "count": len(joints), "type": "VEC4"},
        {"bufferView": views["weights"], "componentType": 5126, "count": len(weights), "type": "VEC4"},
        {"bufferView": views["ibm"], "componentType": 5126, "count": bones, "type": "MAT4"},
        {"bufferView": views["time"], "componentType": 5126, "count": FRAMES + 1, "type": "SCALAR", "min": [0.0], "max": [DURATION]},
    ]
    samplers, channels = [], []
    for i in range(bones):
        accessors.append({"bufferView": track_views[i], "componentType": 5126, "count": FRAMES + 1, "type": "VEC4"})
        samplers.append({"input": 7, "output": len(accessors) - 1, "interpolation": "LINEAR"})
        channels.append({"sampler": i, "target": {"node": 1 + i, "path": "rotation"}})

    nodes = [{"name": "spider", "mesh": 0, "skin": 0}]
    for i, p in enumerate(positions):
        node = {"name": names[i]}
        rel = np.asarray(p) - (np.asarray(positions[parents[i]]) if parents[i] >= 0 else 0)
        node["translation"] = [float(x) for x in rel]
        children = [1 + c for c in range(bones) if parents[c] == i]
        if children:
            node["children"] = children
        nodes.append(node)

    doc = {
        "asset": {"version": "2.0", "generator": "convert_spider.py"},
        "scene": 0, "scenes": [{"nodes": [0, 1]}], "nodes": nodes,
        "meshes": [{"primitives": [{"attributes": {"POSITION": 0, "NORMAL": 1, "TEXCOORD_0": 2, "JOINTS_0": 4, "WEIGHTS_0": 5}, "indices": 3, "material": 0}]}],
        "skins": [{"joints": [1 + i for i in range(bones)], "inverseBindMatrices": 6, "skeleton": 1}],
        "animations": [{"name": "Walk", "samplers": samplers, "channels": channels}],
        "materials": [{"name": "spider", "doubleSided": True, "pbrMetallicRoughness": {"baseColorTexture": {"index": 0}, "metallicFactor": 0.0, "roughnessFactor": 0.6}}],
        "textures": [{"source": 0, "sampler": 0}], "samplers": [{"magFilter": 9729, "minFilter": 9987, "wrapS": 10497, "wrapT": 10497}],
        "images": [{"bufferView": views["png"], "mimeType": "image/png", "name": "spider_basecolor"}],
        "accessors": accessors, "bufferViews": buffer_views, "buffers": [{"byteLength": int(offsets[-1])}],
    }
    j = pad(json.dumps(doc, separators=(",", ":")).encode()).replace(b"\0", b" ")
    body = b"".join(blobs)
    with open(path, "wb") as f:
        f.write(struct.pack("<4sII", b"glTF", 2, 12 + 8 + len(j) + 8 + len(body)))
        f.write(struct.pack("<I4s", len(j), b"JSON") + j)
        f.write(struct.pack("<I4s", len(body), b"BIN\0") + body)
    print("wrote", path, len(Q), "vertices,", len(idx) // 3, "triangles,", bones, "bones,", FRAMES, "frames")


def preview(path, Q, idx, legs, joints, weights):
    """A contact sheet: the rest pose and six moments of the walk, from above and from the side."""
    size = 300
    sheet = Image.new("RGB", (size * 7, size * 2), "white")
    times = [None, 0.0, 1 / 6, 2 / 6, 3 / 6, 4 / 6, 5 / 6]
    for col, t in enumerate(times):
        posed = Q if t is None else pose_vertices(Q, legs, joints, weights, gait(legs, t))
        for row in range(2):
            d = ImageDraw.Draw(sheet)
            ox, oy = col * size, row * size
            for tri in idx.reshape(-1, 3):
                pts = []
                for i in tri:
                    x, y, z = posed[i]
                    pts.append((ox + size / 2 + x * size * 0.9, oy + size / 2 + z * size * 0.9) if row == 0 else (ox + size / 2 + z * size * 0.9, oy + size * 0.8 - y * size * 0.9))
                d.polygon(pts, outline=(70, 70, 70))
    sheet.save(path)
    print("preview", path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("dst")
    ap.add_argument("--texture", type=int, default=1024)
    ap.add_argument("--preview")
    args = ap.parse_args()

    doc, binary = read_glb(args.src)
    prim = doc["meshes"][0]["primitives"][0]
    at = prim["attributes"]
    P = accessor(doc, binary, at["POSITION"]).astype(np.float64)
    N = accessor(doc, binary, at["NORMAL"]).astype(np.float64)
    uv = accessor(doc, binary, at["TEXCOORD_0"]).astype(np.float32)
    idx = accessor(doc, binary, prim["indices"]).astype(np.uint32)
    view = doc["bufferViews"][doc["images"][0]["bufferView"]]
    offset = view.get("byteOffset", 0)
    picture = Image.open(io.BytesIO(binary[offset:offset + view["byteLength"]])).convert("RGB")
    buf = io.BytesIO()
    picture.resize((args.texture, args.texture), Image.LANCZOS).save(buf, "PNG", optimize=True)

    scale = 1.0 / (P[:, 0].max() - P[:, 0].min())
    turn = lambda a: np.stack([-a[:, 2], a[:, 1], a[:, 0]], 1)  # +Z (the head) becomes -X
    Q = (turn(P) * scale)
    Q[:, 1] -= Q[:, 1].min()
    nrm = turn(N)
    nrm /= np.linalg.norm(nrm, axis=1, keepdims=True)

    legs, joints, weights = build_rig(Q)
    for leg in legs:
        print(("L" if leg["side"] == 1 else "R") + str(leg["index"]), "hip", np.round(leg["hip"], 3), "knee", np.round(leg["knee"], 3), "t", round(leg["knee_t"], 2))
    print("vertices on legs:", int((joints[:, 1] > 0).sum()), "of", len(Q))
    write(args.dst, Q.astype(np.float32), nrm.astype(np.float32), uv, idx, joints, weights, legs, buf.getvalue())
    if args.preview:
        preview(args.preview, Q, idx, legs, joints, weights)


if __name__ == "__main__":
    main()
