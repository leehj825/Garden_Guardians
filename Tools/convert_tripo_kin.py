"""Rig a static Tripo Bramblekin (about 1,000 triangles) to the game's skeleton, with no Blender.

    pip install numpy pillow
    python3 Tools/convert_tripo_kin.py Tools/kin_src/Male.glb   Tools/kin_src/Walking_skeleton.glb Assets/Models/Bramblekin/Walking.glb --lods --brighten 0.8
    python3 Tools/convert_tripo_kin.py Tools/kin_src/Female.glb Tools/kin_src/Walking_skeleton.glb Assets/Models/Bramblekin/Walking_female.glb --female
    python3 Tools/convert_tripo_kin.py Tools/kin_src/GuardMale.glb Tools/kin_src/Walking_skeleton.glb Assets/Models/Bramblekin/Guard_male.glb --lods --guard --brighten 0.8
    python3 Tools/convert_tripo_kin.py Tools/kin_src/GuardFemale.glb Tools/kin_src/Walking_skeleton.glb Assets/Models/Bramblekin/Guard_female.glb --female --lods --guard --brighten 0.7

What convert_female.py does for a decimated mesh, done directly: the model (Y-up, facing +Z, about 0.95-0.98 tall) is scaled to the
skeleton's height of 1, turned into its bind space (Z-up, facing -Y), skinned to its 33 joints by distance to the bones, and
written as the skeleton's glb with the mesh, weights and texture replaced, so every animation clip fits it. The texture (a
4096 px picture) is shrunk to --texture px and stored as PNG (this raylib build reads no embedded JPEG).

  --female   her arms hang at her sides where the skeleton's stick out (FITTED_ARM), and her long hair is weighted to the head and
             spine only, so it does not swing with her arms or legs when she walks.
  --lods     also write <name>_lod1.glb and <name>_lod2.glb: the same mesh with a 1024 px and a 512 px texture.
"""
import argparse
import io
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(__file__))
from convert_female import (EPSILON, FITTED_ARM, NEIGHBOURS, POWER, accessor, bind_joints, build, point_segment_distance,  # noqa: E402
                            read_glb, segments, write_glb)

Image.MAX_IMAGE_PIXELS = None
ARM_RADIUS = 0.07      # beyond this far from every arm bone a vertex is not arm (hair, skirt)
CLOTH_LEG_PULL = 1.8   # the dress feels a leg this much farther off than it is, so the hem stays with her hips instead of streaming out behind a raised leg
CLOTH_GREEN = 1.1      # a vertex whose texture is this much greener than it is red (and more than that than blue) is cloth (her dress): no arm bone carries it
RIGID_X = 0.2         # a guard's shield (left) and sword (right) lie beyond this far to the side (bind space), between the knees and the helmet
HEAD_BASE = 0.6        # above this height a guard's helmet, brim and hair go with the head alone...
BLADE_BOX = ((-1.0, -0.315), (-0.25, -0.165))  # ...except the sword blade, which rises beside the helmet: x and y limits (bind space)
CHIN_Z, CHIN_X = 0.5, 0.15  # the chin and jaw reach down below HEAD_BASE (to the neck): within this far of the middle, from this height, they go with the head too
BLADE_FOOT = 0.4       # ...from this height up
LEG_TOP = 0.28         # above this height (bind space) nothing follows the legs (hair, dress)


def load_static(path):
    doc, binary = read_glb(path)
    prim = doc["meshes"][0]["primitives"][0]
    at = prim["attributes"]
    pos = accessor(doc, binary, at["POSITION"]).astype(np.float64)
    nrm = accessor(doc, binary, at["NORMAL"]).astype(np.float64)
    uv = accessor(doc, binary, at["TEXCOORD_0"]).astype(np.float32)
    idx = accessor(doc, binary, prim["indices"]).astype(np.uint32)
    view = doc["bufferViews"][doc["images"][0]["bufferView"]]
    offset = view.get("byteOffset", 0)
    picture = Image.open(io.BytesIO(binary[offset:offset + view["byteLength"]])).convert("RGB")
    return pos, nrm, uv, idx, picture


def png_of(picture, size):
    buf = io.BytesIO()
    picture.resize((size, size), Image.LANCZOS).save(buf, "PNG", optimize=True)
    return buf.getvalue()


def cloth_mask(uv, picture):
    """True where the texture under a vertex is green: the dress, whose hem hangs by her hands but must not follow them."""
    small = np.asarray(picture.resize((512, 512), Image.LANCZOS).convert("RGB"), dtype=np.float64)
    u = np.clip((uv[:, 0] * 511).astype(int), 0, 511)
    v = np.clip((uv[:, 1] * 511).astype(int), 0, 511)
    r, g, b = small[v, u, 0], small[v, u, 1], small[v, u, 2]
    return (g > r * CLOTH_GREEN) & (g > b * 1.2) & (g > 20)


def largest_piece(points, tris, selected):
    """The biggest connected piece of the mesh among the <paramref name=selected> vertices (joined by the faces between them): a held shield or sword, not the hair beside it."""
    key = {}
    rep = np.array([key.setdefault(tuple(np.round(p, 4)), len(key)) for p in points])
    parent = list(range(len(key)))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    for t in tris:
        if selected[t].all():
            a, b, c = rep[t]
            parent[find(a)] = find(b)
            parent[find(b)] = find(c)
    sizes = {}
    for v in np.where(selected)[0]:
        sizes[find(rep[v])] = sizes.get(find(rep[v]), 0) + 1
    if not sizes:
        return selected
    biggest = max(sizes, key=sizes.get)
    return np.array([bool(selected[v]) and find(rep[v]) == biggest for v in range(len(points))])


def skin(points, names, segs, female, guard=False, tris=None, cloth=None):
    owners = list(segs)
    dist = np.stack([np.min([point_segment_distance(points, a, b) for a, b in segs[n]], axis=0) for n in owners], axis=1)
    if female:
        arm = np.array([any(k in n for k in ("Shoulder", "Arm", "Hand")) for n in owners])
        leg = np.array([any(k in n for k in ("UpLeg", "Leg", "Foot", "Toe")) for n in owners])
        not_arm = dist[:, arm].min(axis=1) > ARM_RADIUS
        if cloth is not None:
            not_arm |= cloth
            dist[np.ix_(cloth, np.where(leg)[0])] *= CLOTH_LEG_PULL
        dist[np.ix_(not_arm, np.where(arm)[0])] = np.inf
        high = points[:, 2] > LEG_TOP
        dist[np.ix_(high, np.where(leg)[0])] = np.inf
    order = np.argsort(dist, axis=1)[:, :NEIGHBOURS]
    d = np.take_along_axis(dist, order, axis=1)
    w = 1.0 / (d + EPSILON) ** POWER
    w[~np.isfinite(d)] = 0.0
    w /= w.sum(axis=1, keepdims=True)
    index = np.array([names.index(owners[k]) for k in range(len(owners))])[order].astype("u1")
    w = w.astype("<f4")
    if guard:
        # The held things and the helmet are rigid: one bone each, so a sword or shield never bends, and a brim never follows an arm.
        x, y, z = points[:, 0], points[:, 1], points[:, 2]
        rigid = np.full(len(points), -1)
        between = (z > 0.12) & (z < HEAD_BASE)
        for side, bone in ((1, "LeftHand"), (-1, "RightHand")):
            held = largest_piece(points, tris, (x * side > RIGID_X) & between)
            rigid[held] = names.index(bone)
        blade = (z >= BLADE_FOOT) & (x > BLADE_BOX[0][0]) & (x <= BLADE_BOX[0][1]) & (y >= BLADE_BOX[1][0]) & (y <= BLADE_BOX[1][1])
        rigid[blade] = names.index("RightHand")
        rigid[((z >= HEAD_BASE) | ((z >= CHIN_Z) & (np.abs(x) < CHIN_X))) & ~blade] = names.index("Head")
        fixed = rigid >= 0
        index[fixed] = 0
        index[fixed, 0] = rigid[fixed]
        w[fixed] = 0.0
        w[fixed, 0] = 1.0
    elif not female:
        # A plain kin's whole head is rigid too (one bone), so its face does not wobble as the neck and shoulders swing in the walk.
        head = points[:, 2] >= CHIN_Z  # (all the way across: the cheeks reach 0.19 to the side)
        index[head] = 0
        index[head, 0] = names.index("Head")
        w[head] = 0.0
        w[head, 0] = 1.0
    return index, w


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("model")
    ap.add_argument("skeleton")
    ap.add_argument("dst")
    ap.add_argument("--female", action="store_true")
    ap.add_argument("--texture", type=int, default=2048)
    ap.add_argument("--lods", action="store_true")
    ap.add_argument("--guard", action="store_true", help="shield and sword follow the arms only")
    ap.add_argument("--brighten", type=float, default=1.0, help="gamma applied to the texture (below 1 lightens; the guards' armour and leather are painted darker than the plain kin)")
    args = ap.parse_args()

    pos_y, nrm_y, uv, idx, picture = load_static(args.model)
    if len(pos_y) > 65535:
        sys.exit("%d vertices: raylib meshes take 16-bit indices" % len(pos_y))
    scale = 1.0 / pos_y[:, 1].max()  # to the skeleton's height of 1 (feet at 0)

    def to_bind(a, s=1.0):  # Y-up facing +Z  ->  Z-up facing -Y
        return np.stack([a[:, 0], -a[:, 2], a[:, 1]], axis=1) * s

    pos = to_bind(pos_y, scale)
    nrm = to_bind(nrm_y)

    doc, binary = read_glb(args.skeleton)
    names, position, parent = bind_joints(doc, binary)
    segs = segments(names, position, parent, FITTED_ARM if args.female else {})
    cloth = cloth_mask(uv, picture) if args.female and not args.guard else None  # (a guard's green is a tunic and a shield, which follow her arms)
    if cloth is not None:
        print(int(cloth.sum()), "of", len(cloth), "vertices are cloth")
    joints_idx, w = skin(pos, names, segs, args.female, args.guard, idx.reshape(-1, 3), cloth)
    joints4 = np.zeros((len(pos), 4), "u1")
    weights4 = np.zeros((len(pos), 4), "<f4")
    joints4[:, :NEIGHBOURS] = joints_idx
    weights4[:, :NEIGHBOURS] = w

    if args.brighten != 1.0:
        # Lighten the value only (hue and saturation kept, so the copper and leather stay rich rather than going grey).
        hsv = np.asarray(picture.convert("RGB").convert("HSV")).astype("f4")
        hsv[..., 2] = 255.0 * (hsv[..., 2] / 255.0) ** args.brighten
        picture = Image.fromarray(np.clip(hsv, 0, 255).astype("u1"), "HSV").convert("RGB")

    outputs = [(args.dst, args.texture)]
    if args.lods:
        stem = args.dst[:-4]
        outputs += [(stem + "_lod1.glb", 1024), (stem + "_lod2.glb", 512)]
    for path, size in outputs:
        d, b = read_glb(args.skeleton)
        d, out = build(d, b, pos.astype("<f4"), nrm.astype("<f4"), uv, idx.reshape(-1), joints4, weights4, png_of(picture, size))
        d["images"][0]["name"] = "Bramblekin_" + ("female" if args.female else "male") + "_basecolor"
        write_glb(path, d, out)
        print("wrote", path, len(pos), "vertices,", len(idx) // 3, "triangles,", size, "px texture")


if __name__ == "__main__":
    main()
