"""Measure a low-poly oak model for the procedural kit (Tools/procedural/kit/*.json oak entries), without Blender.

    python3 Tools/procedural/measure_oak.py Assets/Models/Procedural/props/terrain2_oak_0.glb

Prints the numbers ProceduralKit.cs holds for an oak: its height and reach, where its trunk stands (relative to the model's
origin at the base centre), the trunk's radius and height, the way its hive can face (the gap between two roots at the hive's height
where the trunk is barest) and how far out that surface is, and the circles walkers keep out of (a greedy cover, by the
distance transform, of the ground the trunk and roots stand over). Needs numpy and scipy.
"""
import json
import struct
import sys

import numpy as np
from scipy import ndimage as ndi

STEP = 0.5           # metres between grid cells
SOLID = 1.2          # a root tip lower than this (m) is walked over
HIVE_HEIGHT = 2.6    # the game hangs the hive this far up (World.Beehive)


def load(path):
    d = open(path, "rb").read()
    n = struct.unpack("<I", d[12:16])[0]
    doc = json.loads(d[20:20 + n])
    binary = d[20 + n + 8:]
    p = doc["meshes"][0]["primitives"][0]
    def get(i, dt, w):
        a = doc["accessors"][i]; v = doc["bufferViews"][a["bufferView"]]
        arr = np.frombuffer(binary, dt, a["count"] * w, v.get("byteOffset", 0) + a.get("byteOffset", 0))
        return arr.reshape(-1, w) if w > 1 else arr
    pos = get(p["attributes"]["POSITION"], "<f4", 3).astype(float)
    ic = doc["accessors"][p["indices"]]
    idx = get(p["indices"], "<u2" if ic["componentType"] == 5123 else "<u4", 1).astype(int).reshape(-1, 3)
    return pos, idx


def height_field(pos, idx, half):
    n = int(2 * half / STEP) + 1
    top = np.zeros((n, n))
    rng = np.random.default_rng(1)
    for t in idx:
        a, b, c = pos[t]
        area = np.linalg.norm(np.cross(b - a, c - a)) / 2
        k = int(min(2000, 4 + area / (STEP * STEP) * 8))
        u, v = rng.random(k), rng.random(k)
        flip = u + v > 1
        u[flip], v[flip] = 1 - u[flip], 1 - v[flip]
        pts = a + np.outer(u, b - a) + np.outer(v, c - a)
        ci = np.clip(((pts[:, 0] + half) / STEP).round().astype(int), 0, n - 1)
        cj = np.clip(((pts[:, 2] + half) / STEP).round().astype(int), 0, n - 1)
        np.maximum.at(top, (cj, ci), pts[:, 1])
    return top


def circles_of(mask, half, min_radius=0.5 * STEP + 0.2):
    dist = ndi.distance_transform_edt(mask) * STEP
    covered = np.zeros_like(mask)
    n = mask.shape[0]
    xs = -half + np.arange(n) * STEP
    gx, gz = np.meshgrid(xs, xs)
    out = []
    while True:
        rest = np.where(covered, 0, dist)
        best = np.unravel_index(np.argmax(rest), rest.shape)
        radius = max(rest[best], 0.0)
        if radius <= min_radius:
            break
        cx, cz = xs[best[1]], xs[best[0]]
        r = radius + STEP * 0.5
        out.append((float(cx), float(cz), float(r)))
        covered |= (gx - cx) ** 2 + (gz - cz) ** 2 <= (r * 0.85) ** 2
    return out


def measure(path):
    pos, idx = load(path)
    height = float(pos[:, 1].max())
    half = float(np.ceil(np.hypot(pos[:, 0], pos[:, 2]).max() / 5) * 5 + 5)
    mid = pos[(pos[:, 1] > 0.35 * height) & (pos[:, 1] < 0.9 * height)]
    dx, dz = float(mid[:, 0].mean()), float(mid[:, 2].mean())
    radius = float(np.percentile(np.hypot(mid[:, 0] - dx, mid[:, 2] - dz), 90))
    top = height_field(pos, idx, half)
    circles = circles_of(top > SOLID, half)
    reach = max(np.hypot(cx, cz) + r for cx, cz, r in circles)
    # The hive: the direction at its height where the surface is nearest the trunk's middle.
    band = pos[np.abs(pos[:, 1] - HIVE_HEIGHT) < 1.2]
    ang = np.arctan2(band[:, 2] - dz, band[:, 0] - dx) % (2 * np.pi)
    rad = np.hypot(band[:, 0] - dx, band[:, 2] - dz)
    bins = np.linspace(0, 2 * np.pi, 25)
    worst = [rad[(ang >= bins[k]) & (ang < bins[k + 1])].max() if ((ang >= bins[k]) & (ang < bins[k + 1])).any() else 1e9 for k in range(24)]
    k = int(np.argmin(worst))
    angle = float((bins[k] + bins[k + 1]) / 2)
    surface = float(round(worst[k] * 2) / 2)
    return dict(height=height, radius=reach, circles=[list(c) for c in circles], trunk_dx=dx, trunk_dz=dz, trunk_radius=radius,
                trunk_height=height, hive_angle=angle, hive_surface=surface)


if __name__ == "__main__":
    m = measure(sys.argv[1])
    print({k: (v if k != "circles" else "%d circles" % len(v)) for k, v in m.items()})
    if len(sys.argv) > 2:
        json.dump(m, open(sys.argv[2], "w"))
