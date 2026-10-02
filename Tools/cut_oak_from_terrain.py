"""Take the oak (its trunk and roots) out of the original ground model, leaving the ground, the reeds and the boulders.

    pip install numpy pillow
    python3 Tools/cut_oak_from_terrain.py Tools/kin_src/terrain_original.glb Assets/Models/Terrain/terrain.glb

The oak is every triangle within --radius metres (default 27) of the trunk (x, z = 0.54, -19.39: Terrain0.OakX, OakZ) with a corner more than
--above metres (0.35) over the ground height grid in Source/World/Terrains/Terrain0.cs (which has the oak painted out), and, inside the old oak's
keep-out circles (Tools/oak_old_circles.json), also any that is low but steep or a little proud of the ground (root tops lying in the grass). The
banks of the pond are steep too, so outside those circles steepness alone takes nothing. Where that leaves no ground (under the trunk) the ground is filled in from the grid, coloured like the nearest ground left. The
picture is copied as it is. The oak is then drawn from its own models (Assets/Models/Props/Oak.glb and Oak_lod.glb, see Tools/convert_tripo_prop.py).
"""
import argparse
import base64
import json
import re
import struct

import numpy as np

HERE = __file__.rsplit("/Tools/", 1)[0]
FILL_SINK = 0.6  # metres
POND_LEVEL = -2.327  # the ponds' water level (Terrain0.PondLevel): ground under it is left to the ground that is there


def read_glb(path):
    data = open(path, "rb").read()
    n = struct.unpack("<I", data[12:16])[0]
    return json.loads(data[20:20 + n]), data[20 + n + 8:]


def ground_grid():
    src = open(HERE + "/Source/World/Terrains/Terrain0.cs").read()
    strings = re.findall(r'"([A-Za-z0-9+/=]*)"', src[src.index("Encoded"):])
    heights = np.frombuffer(base64.b64decode("".join(strings)), "<i2").astype(float) / 100.0
    n = int(round(len(heights) ** 0.5))
    return heights.reshape(n, n), n


def pad(b):
    return b + b"\0" * (-len(b) % 4)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("dst")
    ap.add_argument("--oak", type=float, nargs=2, default=(0.54, -19.39))
    ap.add_argument("--radius", type=float, default=27.0)
    ap.add_argument("--above", type=float, default=0.35)
    args = ap.parse_args()

    doc, binary = read_glb(args.src)
    prim = doc["meshes"][0]["primitives"][0]
    at = prim["attributes"]

    def acc(i, dt, w):
        a = doc["accessors"][i]; v = doc["bufferViews"][a["bufferView"]]
        arr = np.frombuffer(binary, dt, a["count"] * w, v.get("byteOffset", 0) + a.get("byteOffset", 0))
        return arr.reshape(-1, w) if w > 1 else arr
    pos = acc(at["POSITION"], "<f4", 3); nrm = acc(at["NORMAL"], "<f4", 3); uv = acc(at["TEXCOORD_0"], "<f4", 2)
    ic = doc["accessors"][prim["indices"]]
    idx = acc(prim["indices"], "<u2" if ic["componentType"] == 5123 else "<u4", 1).astype(np.int64).reshape(-1, 3)
    v = doc["bufferViews"][doc["images"][0]["bufferView"]]
    picture = binary[v.get("byteOffset", 0):v.get("byteOffset", 0) + v["byteLength"]]

    grid, n = ground_grid()
    def ground_at(x, z):
        fx = np.clip((x + 50) / 0.5, 0, n - 1.001); fz = np.clip((z + 50) / 0.5, 0, n - 1.001)
        ix, iz = fx.astype(int), fz.astype(int); tx, tz = fx - ix, fz - iz
        return (grid[iz, ix] * (1 - tx) * (1 - tz) + grid[iz, ix + 1] * tx * (1 - tz) + grid[iz + 1, ix] * (1 - tx) * tz + grid[iz + 1, ix + 1] * tx * tz)

    c = pos[idx].mean(axis=1)
    over = (pos[:, 1] - ground_at(pos[:, 0], pos[:, 2]))[idx].max(axis=1)
    cross = np.cross(pos[idx[:, 1]] - pos[idx[:, 0]], pos[idx[:, 2]] - pos[idx[:, 0]])
    up = cross[:, 1] / np.maximum(np.linalg.norm(cross, axis=1), 1e-12)
    r = np.hypot(c[:, 0] - args.oak[0], c[:, 2] - args.oak[1])
    circles = np.array(json.load(open(HERE + "/Tools/oak_old_circles.json")))
    inside = np.zeros(len(c), bool)
    for cx, cz, cr in circles:
        inside |= np.hypot(c[:, 0] - cx, c[:, 2] - cz) < cr + 0.5
    oak = (r < args.radius) & ((over > args.above) | (inside & ((np.abs(up) < 0.6) | (over > 0.12))))
    keep = idx[~oak]

    # Ground cells (0.5 m) near the oak that no triangle left covers: remember the picture's coordinates where there is ground.
    from scipy import ndimage as ndi
    cover = np.full((n, n), False)
    cuv = np.zeros((n, n, 2), "f4")
    near = keep[np.hypot(pos[keep].mean(axis=1)[:, 0] - args.oak[0], pos[keep].mean(axis=1)[:, 2] - args.oak[1]) < args.radius + 6]
    gx = -50 + np.arange(n) * 0.5
    for tri in near:
        a, b2, c2 = pos[tri][:, [0, 2]]
        x0, x1 = int(max(0, np.floor((min(a[0], b2[0], c2[0]) + 50) / 0.5))), int(min(n - 1, np.ceil((max(a[0], b2[0], c2[0]) + 50) / 0.5)))
        z0, z1 = int(max(0, np.floor((min(a[1], b2[1], c2[1]) + 50) / 0.5))), int(min(n - 1, np.ceil((max(a[1], b2[1], c2[1]) + 50) / 0.5)))
        if x1 < x0 or z1 < z0:
            continue
        px, pz = np.meshgrid(gx[x0:x1 + 1], gx[z0:z1 + 1])
        den = (b2[1] - c2[1]) * (a[0] - c2[0]) + (c2[0] - b2[0]) * (a[1] - c2[1])
        if abs(den) < 1e-12:
            continue
        w0 = ((b2[1] - c2[1]) * (px - c2[0]) + (c2[0] - b2[0]) * (pz - c2[1])) / den
        w1 = ((c2[1] - a[1]) * (px - c2[0]) + (a[0] - c2[0]) * (pz - c2[1])) / den
        w2 = 1 - w0 - w1
        inside = (w0 >= 0) & (w1 >= 0) & (w2 >= 0)
        if not inside.any():
            continue
        uvt = uv[tri]
        cuv[z0:z1 + 1, x0:x1 + 1][inside] = (w0[..., None] * uvt[0] + w1[..., None] * uvt[1] + w2[..., None] * uvt[2])[inside]
        cover[z0:z1 + 1, x0:x1 + 1] |= inside
    xs, zs = np.meshgrid(gx, gx)
    hole = (~cover) & (np.hypot(xs - args.oak[0], zs - args.oak[1]) < args.radius + 1.0)
    near_idx = ndi.distance_transform_edt(~cover, return_distances=False, return_indices=True)
    # Cells whose centres no triangle covers are only the middle of the gaps (triangle edges cut across cells): the patch overlaps the ground round
    # them, sunk a little (FILL_SINK) so that where ground stands it hides the patch.
    spread = ndi.binary_dilation(hole, iterations=1) & (np.hypot(xs - args.oak[0], zs - args.oak[1]) < args.radius + 2.0)
    hole_cells = np.argwhere(spread[:-1, :-1] | spread[1:, :-1] | spread[:-1, 1:] | spread[1:, 1:])
    hole_cells = np.array([c for c in hole_cells if grid[c[0]:c[0] + 2, c[1]:c[1] + 2].min() >= POND_LEVEL + 0.05]).reshape(-1, 2)
    fp, fn, fu, fi = [], [], [], []
    for j, i in hole_cells:
        base = len(fp)
        for dj, di in ((0, 0), (0, 1), (1, 1), (1, 0)):
            x, z = gx[i + di], gx[j + dj]
            gh = grid[j + dj, i + di] - FILL_SINK
            gl = grid[j + dj, max(i + di - 1, 0)]; gr = grid[j + dj, min(i + di + 1, n - 1)]; gb = grid[max(j + dj - 1, 0), i + di]; gf = grid[min(j + dj + 1, n - 1), i + di]
            nv = np.array([gl - gr, 1.0, gb - gf]); nv /= np.linalg.norm(nv)
            nj, ni = near_idx[0][j + dj, i + di], near_idx[1][j + dj, i + di]
            fp.append((x, gh, z)); fn.append(nv); fu.append(cuv[nj, ni])
        fi += [base, base + 2, base + 1, base, base + 3, base + 2]
    print("filled %d ground cells under the oak" % len(hole_cells))
    used, inverse = np.unique(keep, return_inverse=True)
    p = np.concatenate([pos[used], np.array(fp, "f4").reshape(-1, 3)]); nr = np.concatenate([nrm[used], np.array(fn, "f4").reshape(-1, 3)]); u = np.concatenate([uv[used], np.array(fu, "f4").reshape(-1, 2)])
    ix2 = np.concatenate([inverse.reshape(-1), np.array(fi, "i8") + len(used)]).astype("<u2")
    print("removed %d of %d triangles; %d vertices now" % (oak.sum(), len(idx), len(p)))
    if len(p) > 65535:
        raise SystemExit("too many vertices for raylib's 16-bit indices")

    parts = [pad(p.astype("<f4").tobytes()), pad(nr.astype("<f4").tobytes()), pad(u.astype("<f4").tobytes()), pad(ix2.tobytes()), pad(picture)]
    sizes = [p.nbytes, nr.nbytes, u.nbytes, ix2.nbytes, len(picture)]
    offsets = np.cumsum([0] + [len(x) for x in parts])
    doc["accessors"] = [
        {"bufferView": 0, "componentType": 5126, "count": len(p), "type": "VEC3", "min": p.min(axis=0).tolist(), "max": p.max(axis=0).tolist()},
        {"bufferView": 1, "componentType": 5126, "count": len(p), "type": "VEC3"},
        {"bufferView": 2, "componentType": 5126, "count": len(p), "type": "VEC2"},
        {"bufferView": 3, "componentType": 5123, "count": len(ix2), "type": "SCALAR"},
    ]
    doc["meshes"][0]["primitives"][0] = {"attributes": {"POSITION": 0, "NORMAL": 1, "TEXCOORD_0": 2}, "indices": 3, "material": prim.get("material", 0), "mode": 4}
    doc["bufferViews"] = [{"buffer": 0, "byteOffset": int(offsets[k]), "byteLength": sizes[k]} for k in range(5)]
    doc["images"][0]["bufferView"] = 4
    doc["buffers"] = [{"byteLength": int(offsets[-1])}]
    j = pad(json.dumps(doc, separators=(",", ":")).encode()).replace(b"\0", b" ")
    body = b"".join(parts)
    with open(args.dst, "wb") as f:
        f.write(struct.pack("<4sII", b"glTF", 2, 12 + 8 + len(j) + 8 + len(body)))
        f.write(struct.pack("<I4s", len(j), b"JSON") + j)
        f.write(struct.pack("<I4s", len(body), b"BIN\0") + body)
    print("wrote", args.dst)


if __name__ == "__main__":
    main()
