"""Rig the low-poly Tripo stag beetle (Tools/kin_src/Beetle.glb) with six legs and a walk - no Blender.

    pip install numpy pillow meshoptimizer
    python3 Tools/convert_beetle.py Tools/kin_src/Beetle.glb Assets/Models/Props/Beetle.glb --preview Tools/previews/beetle_walk.png
    python3 Tools/convert_beetle.py Ants.glb Assets/Models/Props/Ant.glb --face=-x --tris 700 --texture 512 --body-half-width 0.1 --leg-max-height 0.33   # the Tripo ant, rigged the same way

Like the spider (Tools/convert_spider.py): the skeleton Tripo made does nothing, so it is thrown away and a new one built - a body bone,
and for each of the six legs a hip and a knee - with the vertices weighted by where they lie along their leg. The clip "Walk" is
an insect's tripod gait: front-left, middle-right and rear-left step together, then the other three. The mesh is simplified first,
turned to face +X (the game's way), scaled to 1 unit long, its feet on y = 0, its texture stored as PNG.
"""
import argparse
import io
import math
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, "procedural"))
import convert_spider as cs  # noqa: E402
import convert_tripo_prop as tp  # noqa: E402
from decimate_props import simplify, smooth_normals, weld  # noqa: E402

BODY_HALF_WIDTH = 0.19   # beyond this to the side a vertex is leg
LEG_MAX_HEIGHT = 9.0     # ...and below this high (the ant's feelers reach out to the side above its legs)
SWING = math.radians(20.0)
LIFT = math.radians(30.0)
KNEE_LIFT = math.radians(22.0)
FRAMES, DURATION = 30, 0.8


def build_rig(Q):
    """Six legs (index 0 rear .. 2 front, per side), their hips and knees, and the skin weights."""
    legs = []
    n = len(Q)
    leg_of = np.full(n, -1)
    t_along = np.zeros(n)
    best = np.full(n, 1e9)
    for side in (1, -1):
        out = (Q[:, 2] * side > BODY_HALF_WIDTH) & (Q[:, 1] < LEG_MAX_HEIGHT)
        ids = np.where(out)[0]
        xs = Q[ids, 0]
        centres = np.array([-0.4, -0.04, 0.3])
        for _ in range(12):
            assign = np.argmin(np.abs(xs[:, None] - centres[None, :]), axis=1)
            for k in range(3):
                if (assign == k).any():
                    centres[k] = xs[assign == k].mean()
        for k in range(3):
            members = ids[assign == k]
            lateral = np.abs(Q[members, 2])
            tip = members[np.argmax(np.hypot(Q[members, 0] - 0.0, Q[members, 2]))]
            hip_pts = members[lateral < BODY_HALF_WIDTH + 0.05]
            hip = Q[hip_pts].mean(axis=0) if len(hip_pts) else Q[members].mean(axis=0)
            hip = np.array([hip[0], Q[members][:, 1].max() * 0.8, hip[2]])
            tip_pos = Q[tip]
            leg = {"side": side, "index": k, "hip": hip, "tip": tip_pos, "members": members}
            d2 = tip_pos[[0, 2]] - hip[[0, 2]]
            leg["dir"] = d2 / max(np.linalg.norm(d2), 1e-6)
            legs.append(leg)

    for k, leg in enumerate(legs):
        hip, tip = leg["hip"], leg["tip"]
        seg = (tip - hip)[[0, 2]]
        length2 = float(seg @ seg)
        members = leg["members"]
        t = ((Q[members][:, [0, 2]] - hip[[0, 2]]) @ seg) / length2
        dist = np.linalg.norm(Q[members][:, [0, 2]] - (hip[[0, 2]] + np.clip(t, 0, 1)[:, None] * seg), axis=1)
        ok = dist < 0.12
        leg_of[members[ok]] = k
        t_along[members[ok]] = t[ok]
        # The knee: halfway along (the leg runs out, then down).
        knee_t = 0.55
        leg["knee_t"] = knee_t
        horizontal = hip[[0, 2]] + seg * knee_t
        near = members[ok][np.abs(t[ok] - knee_t) < 0.12]
        y = float(Q[near][:, 1].mean()) if len(near) else float(hip[1])
        leg["knee"] = np.array([horizontal[0], y, horizontal[1]])

    joints = np.zeros((n, 4), np.uint16)
    weights = np.zeros((n, 4), np.float32)
    weights[:, 0] = 1.0
    for v in range(n):
        k = leg_of[v]
        if k < 0:
            continue
        t = t_along[v]
        to_hip = cs.smoothstep(-0.05, 0.25, t)
        to_knee = cs.smoothstep(legs[k]["knee_t"] - 0.12, legs[k]["knee_t"] + 0.12, t)
        joints[v] = [0, 1 + 2 * k, 2 + 2 * k, 0]
        weights[v] = [1 - to_hip, to_hip * (1 - to_knee), to_hip * to_knee, 0.0]
    return legs, joints, weights


def gait(legs, time01):
    forward = np.array([1.0, 0.0, 0.0])
    rotations = [np.array([0.0, 0.0, 0.0, 1.0])]
    for leg in legs:
        d3 = np.array([leg["dir"][0], 0.0, leg["dir"][1]])
        group = (leg["index"] + (0 if leg["side"] == 1 else 1)) % 2   # tripod: front-left, middle-right, rear-left together
        c = (time01 + 0.5 * group) % 1.0
        if c < 0.5:
            swing, lift = 1 - 4 * c, 0.0
        else:
            u = (c - 0.5) * 2
            swing, lift = -1 + 2 * (u * u * (3 - 2 * u)), math.sin(math.pi * u)
        sign = 1.0 if np.dot(np.cross([0, 1, 0], d3), forward) > 0 else -1.0
        yaw = cs.quat_axis([0, 1, 0], sign * SWING * swing)
        axis = np.cross(d3, [0, 1, 0])
        rotations += [cs.quat_mul(yaw, cs.quat_axis(axis, LIFT * lift)), cs.quat_axis(axis, KNEE_LIFT * lift)]
    return rotations


def main():
    global BODY_HALF_WIDTH, LEG_MAX_HEIGHT
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("dst")
    ap.add_argument("--tris", type=int, default=1300)
    ap.add_argument("--texture", type=int, default=1024)
    ap.add_argument("--preview")
    ap.add_argument("--face", choices=["+z", "-x"], default="+z", help="the way the model looks as exported (the beetle +z, the Tripo ant -x); it is turned to face +X")
    ap.add_argument("--body-half-width", type=float, default=BODY_HALF_WIDTH, help="beyond this far to the side (the model 1 long) a vertex is leg")
    ap.add_argument("--leg-max-height", type=float, default=LEG_MAX_HEIGHT, help="a vertex higher than this (the model 1 long, on y = 0) is not leg")
    args = ap.parse_args()
    BODY_HALF_WIDTH = args.body_half_width
    LEG_MAX_HEIGHT = args.leg_max_height

    doc, binary = tp.read_glb(args.src)
    prim = doc["meshes"][0]["primitives"][0]
    at = prim["attributes"]
    P = tp.accessor(doc, binary, at["POSITION"]).astype(np.float32)
    uv = tp.accessor(doc, binary, at["TEXCOORD_0"]).astype(np.float32)
    idx = tp.accessor(doc, binary, prim["indices"]).astype(np.uint32)
    view = doc["bufferViews"][doc["images"][0]["bufferView"]]
    off = view.get("byteOffset", 0)
    picture = Image.open(io.BytesIO(binary[off:off + view["byteLength"]])).convert("RGB")
    buf = io.BytesIO()
    picture.resize((args.texture, args.texture), Image.LANCZOS).save(buf, "PNG", optimize=True)

    wpos, wuv, widx = weld(P, uv, idx)
    kept = simplify(wpos, wuv, widx, args.tris)
    used, remap = np.unique(kept, return_inverse=True)
    pos, uvs = wpos[used].astype(np.float64), wuv[used]
    index = remap.reshape(-1).astype(np.uint32)
    nrm = smooth_normals(pos.astype(np.float32), index)

    long_axis = 2 if args.face == "+z" else 0
    scale = 1.0 / (pos[:, long_axis].max() - pos[:, long_axis].min())          # 1 unit long
    if args.face == "+z":
        turn = lambda a: np.stack([a[:, 2], a[:, 1], -a[:, 0]], 1)    # the head (+Z) becomes +X
    else:
        turn = lambda a: np.stack([-a[:, 0], a[:, 1], -a[:, 2]], 1)   # the head (-X) becomes +X
    Q = turn(pos) * scale
    Q[:, 1] -= Q[:, 1].min()
    nrm = turn(nrm.astype(np.float64))
    nrm /= np.linalg.norm(nrm, axis=1, keepdims=True)

    legs, joints, weights = build_rig(Q)
    for leg in legs:
        print(("L" if leg["side"] == 1 else "R") + str(leg["index"]), "hip", np.round(leg["hip"], 3), "knee", np.round(leg["knee"], 3), "tip", np.round(leg["tip"], 3))
    print("vertices on legs:", int((joints[:, 1] > 0).sum()), "of", len(Q), "; triangles", len(index) // 3)

    # The shared writer and previewer are the spider's; give them the beetle's gait and timing.
    cs.gait, cs.FRAMES, cs.DURATION = gait, FRAMES, DURATION
    cs.write(args.dst, Q.astype(np.float32), nrm.astype(np.float32), uvs.astype(np.float32), index, joints, weights, legs, buf.getvalue())
    if args.preview:
        cs.preview(args.preview, Q, index, legs, joints, weights)


if __name__ == "__main__":
    main()
