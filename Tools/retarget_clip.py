"""Fit a Mixamo clip converted by convert_fbx.py to the game's skeleton (the one in Assets/Models/Bramblekin/Walking.glb).

    pip install pygltflib numpy
    python3 Tools/retarget_clip.py Standing_Aim_Recoil.glb Assets/Models/Bramblekin/StandingAimRecoil.glb --yaw 90

Clips from other Mixamo downloads come in their own units and proportions: every bone's offset is a hundredth of ours, and the hips' height too. The
rotations carry over (the bones are the same and face the same way), so this keeps them and puts our skeleton's own offsets back: each bone but the
hips gets the constant offset the template clip (Walking.glb) has for it, and the hips' movement is scaled by the ratio of the two skeletons' hip
heights. --yaw turns the whole body about the vertical (degrees), for a clip whose character stands side-on to the way it acts.
"""
import argparse
import math
import struct

import numpy as np
from pygltflib import GLTF2

HIPS = "mixamorig:Hips"
OUR_HIPS_HEIGHT = 0.2769  # the hips' height in Walking_skeleton.glb (the rest pose the clips are built on)


def channels(g):
    names = {i: n.name for i, n in enumerate(g.nodes)}
    anim = g.animations[0]
    for ch in anim.channels:
        acc = g.accessors[anim.samplers[ch.sampler].output]
        yield names[ch.target.node], ch.target.path, acc


def read(g, blob, acc, width):
    bv = g.bufferViews[acc.bufferView]
    base = (bv.byteOffset or 0) + (acc.byteOffset or 0)
    return np.frombuffer(blob, "<f4", acc.count * width, base).reshape(-1, width).copy()


def write(g, blob, acc, data):
    bv = g.bufferViews[acc.bufferView]
    base = (bv.byteOffset or 0) + (acc.byteOffset or 0)
    flat = data.astype("<f4").tobytes()
    blob[base:base + len(flat)] = flat


def qmul(a, b):  # (x, y, z, w) quaternions, rows
    ax, ay, az, aw = a[..., 0], a[..., 1], a[..., 2], a[..., 3]
    bx, by, bz, bw = b[..., 0], b[..., 1], b[..., 2], b[..., 3]
    return np.stack([aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
                     aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz], axis=-1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("dst")
    ap.add_argument("--template", default="Assets/Models/Bramblekin/Walking.glb")
    ap.add_argument("--yaw", type=float, default=0.0, help="degrees to turn the body about the vertical (positive turns it to its left)")
    args = ap.parse_args()

    template = GLTF2().load(args.template)
    tblob = bytes(template.binary_blob())
    offsets = {}
    for name, path, acc in channels(template):
        if path == "translation":
            offsets[name] = read(template, tblob, acc, 3)[0]

    g = GLTF2().load(args.src)
    blob = bytearray(g.binary_blob())
    rest_hips = next(n.translation[1] for n in g.nodes if n.name == HIPS)
    ratio = OUR_HIPS_HEIGHT / rest_hips
    half = math.radians(args.yaw) / 2
    turn = np.array([0.0, math.sin(half), 0.0, math.cos(half)])
    for name, path, acc in channels(g):
        if path == "translation":
            keys = read(g, blob, acc, 3)
            if name == HIPS:
                keys = keys * ratio
                keys[:, 0] = keys[0, 0]  # no root motion: the game moves the body itself
                keys[:, 2] = keys[0, 2]
            else:
                keys[:] = offsets[name]
            write(g, blob, acc, keys)
        elif path == "rotation" and name == HIPS and args.yaw:
            write(g, blob, acc, qmul(np.broadcast_to(turn, (acc.count, 4)), read(g, blob, acc, 4)))
    g.set_binary_blob(bytes(blob))
    g.save(args.dst)
    print("wrote", args.dst, "hip height ratio %.1f" % ratio)


if __name__ == "__main__":
    main()
