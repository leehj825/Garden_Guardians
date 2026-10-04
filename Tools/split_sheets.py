"""Cut the generated Tripo sheets (several objects laid out in one mesh sharing one picture) into the game's model files, with no Blender.

    pip install numpy pillow meshoptimizer
    python3 Tools/split_sheets.py loose     low-polygameassets.glb Assets/Models/Props/Loose.glb
    python3 Tools/split_sheets.py fittings  outdoorgardenprops.glb Assets/Models/Props/Village/Fittings.glb
    python3 Tools/split_sheets.py gear      Weapons_armor.glb      Assets/Models/Props/Gear
    python3 Tools/split_sheets.py sack      low-polygameassets.glb Assets/Models/Props/Village/FoodSack.glb
    python3 Tools/split_sheets.py shield    Weapons_armor.glb      Assets/Models/Props/Village/Shield.glb
    python3 Tools/split_sheets.py aphid     aphid.glb              Assets/Models/Props/Aphid.glb

A sheet is one mesh whose objects are not joined to each other, so the objects are found as connected pieces (vertices at the same place count as one) and
named by where they lie in the sheet (see the tables below: row by height, then along x). Each piece is turned to the way the game draws it, sized in
metres, set on y = 0 centred on its own origin, and written with the part of the sheet's picture it uses, cut out, shrunk and stored as PNG (this raylib build
reads no embedded JPEG; the picture is shared when several pieces go into one file).

loose      Loose.glb: twelve meshes in the order of LooseModels.Kind (Source/Engine/LooseModels.cs), in metres. The flat things (twig, branch, fish, honeycomb)
           lie on the ground with their long side along +X (the fish's head at +X).
fittings   Fittings.glb: seven meshes in the order of FittingModels.Kind (Source/Engine/FittingModels.cs), in metres, facing +Z.
gear       Sword.glb, Spear.glb, Bow.glb, Arrow.glb and Shield.glb in the folder, each lying along +X, 1 unit long, centred (see Source/Engine/GearModels.cs):
           the point or blade at +X, the bow's wood bulging towards +Y, the shield's face towards +Z (it stands on y = 0, 1 unit across).
sack       the village's food sack: 1 unit across, standing on y = 0 (and its _lod twin).
shield     the village's shield: 1 unit across, standing on y = 0, face to +Z (and its _lod twin).
aphid      one aphid, 1 unit long, facing +Z (an aphid pen's herd).
"""
import io
import json
import struct
import sys

import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None


# --- reading -----------------------------------------------------------------------------------------------------------------------------

def read_sheet(path):
    data = open(path, "rb").read()
    n = struct.unpack("<I", data[12:16])[0]
    doc = json.loads(data[20:20 + n])
    binary = data[20 + n + 8:]

    def acc(i):
        a = doc["accessors"][i]
        v = doc["bufferViews"][a["bufferView"]]
        width = {"SCALAR": 1, "VEC2": 2, "VEC3": 3}[a["type"]]
        dtype = {5126: "<f4", 5125: "<u4", 5123: "<u2"}[a["componentType"]]
        arr = np.frombuffer(binary, dtype, a["count"] * width, v.get("byteOffset", 0) + a.get("byteOffset", 0))
        return arr.reshape(-1, width) if width > 1 else arr

    prim = doc["meshes"][0]["primitives"][0]
    at = prim["attributes"]
    material = doc["materials"][prim.get("material", 0)]
    texture = doc["textures"][material["pbrMetallicRoughness"]["baseColorTexture"]["index"]]
    image = doc["images"][texture["source"]]
    view = doc["bufferViews"][image["bufferView"]]
    start = view.get("byteOffset", 0)
    picture = Image.open(io.BytesIO(binary[start:start + view["byteLength"]])).convert("RGB")
    return (acc(at["POSITION"]).astype(np.float64), acc(at["NORMAL"]).astype(np.float64), acc(at["TEXCOORD_0"]).astype(np.float32),
            acc(prim["indices"]).astype(np.int64).reshape(-1, 3), picture)


def pieces(pos, idx, min_triangles=1):
    """The sheet's separate pieces: lists of triangle numbers (vertices at one place are one vertex)."""
    _, inv = np.unique(np.round(pos, 5), axis=0, return_inverse=True)
    inv = inv.reshape(-1)
    parent = list(range(inv.max() + 1))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    for a, b, c in inv[idx]:
        parent[find(a)] = find(b)
        parent[find(b)] = find(c)
    label = np.array([find(v) for v in inv[idx[:, 0]]])
    out = [np.where(label == c)[0] for c in np.unique(label)]
    return [t for t in out if len(t) >= min_triangles]


class Piece:
    def __init__(self, pos, nrm, uv, idx, tris):
        used, inverse = np.unique(idx[tris], return_inverse=True)
        self.pos, self.nrm, self.uv = pos[used].copy(), nrm[used].copy(), uv[used].copy()
        self.idx = inverse.reshape(-1, 3)

    @property
    def lo(self):
        return self.pos.min(axis=0)

    @property
    def hi(self):
        return self.pos.max(axis=0)

    @property
    def centre(self):
        return (self.lo + self.hi) / 2

    def transform(self, matrix):
        """Turn by a 3x3 rotation (rows are the new x, y and z axes in the old coordinates)."""
        self.pos = self.pos @ matrix.T
        self.nrm = self.nrm @ matrix.T

    def fit(self, longest, floor=True):
        """Scale so that the longest side is <longest> metres, then centre in x and z and stand on y = 0 (or centre in y too)."""
        size = (self.hi - self.lo).max()
        self.pos *= longest / size
        lo, hi = self.lo, self.hi
        self.pos -= [(lo[0] + hi[0]) / 2, lo[1] if floor else (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2]

    def lie_flat(self):
        """Turn so that the piece lies on the ground: its longest side along +X, its next along Z, and its thinnest up (a right-handed turn)."""
        p = self.pos - self.pos.mean(axis=0)
        w, v = np.linalg.eigh(p.T @ p)
        x_axis, z_axis = v[:, 2], v[:, 1]
        y_axis = np.cross(z_axis, x_axis)
        self.transform(np.stack([x_axis, y_axis, z_axis]))

    def face_dark_to(self, picture, sign=1.0, axis=0, threshold=150):
        """Turn half a circle about the vertical if the dark spot of the picture (an eye) is not on the <sign> side of <axis> (0 = x, 2 = z)."""
        w, h = picture.size
        px = np.asarray(picture)
        u = np.clip((self.uv[:, 0] * (w - 1)).astype(int), 0, w - 1)
        v = np.clip((self.uv[:, 1] * (h - 1)).astype(int), 0, h - 1)
        dark = px[v, u].astype(float).sum(axis=1) < threshold
        if dark.any() and (self.pos[dark, axis].mean() - self.centre[axis]) * sign < 0:
            self.transform(np.array([[-1, 0, 0], [0, 1, 0], [0, 0, -1]], float))
        return dark.any()

    def pic_bounds(self, margin=0.01):
        lo, hi = self.uv.min(axis=0), self.uv.max(axis=0)
        pad = (hi - lo) * margin + 1.0 / 4096
        return np.clip(lo - pad, 0, 1), np.clip(hi + pad, 0, 1)


# Rotation (rows are the new axes). TURN: the sheet's upright things (point up) lie along +X, the point at +X: (x, y, z) -> (y, -x, z).
TURN = np.array([[0, 1, 0], [-1, 0, 0], [0, 0, 1]], float)


# --- writing -----------------------------------------------------------------------------------------------------------------------------

def pad4(b):
    return b + b"\0" * (-len(b) % 4)


def png_bytes(picture, size):
    buf = io.BytesIO()
    picture.resize((size, size), Image.LANCZOS).save(buf, "PNG", optimize=True)
    return buf.getvalue()


def write_glb(path, named, png, label, double_sided=True):
    """named: [(mesh name, Piece)] sharing one picture (their uv already fitted to it)."""
    chunks, views, accessors, meshes = [], [], [], []
    offset = 0

    def add(arr):
        nonlocal offset
        raw = pad4(arr.tobytes())
        views.append({"buffer": 0, "byteOffset": offset, "byteLength": arr.nbytes})
        chunks.append(raw)
        offset += len(raw)
        return len(views) - 1

    for name, piece in named:
        p32, n32, u32 = piece.pos.astype("<f4"), piece.nrm.astype("<f4"), piece.uv.astype("<f4")
        i32 = piece.idx.astype("<u2").reshape(-1)
        first = len(accessors)
        accessors += [
            {"bufferView": add(p32), "componentType": 5126, "count": len(p32), "type": "VEC3", "min": p32.min(0).tolist(), "max": p32.max(0).tolist()},
            {"bufferView": add(n32), "componentType": 5126, "count": len(n32), "type": "VEC3"},
            {"bufferView": add(u32), "componentType": 5126, "count": len(u32), "type": "VEC2"},
            {"bufferView": add(i32), "componentType": 5123, "count": len(i32), "type": "SCALAR"},
        ]
        meshes.append({"name": name, "primitives": [{"attributes": {"POSITION": first, "NORMAL": first + 1, "TEXCOORD_0": first + 2}, "indices": first + 3, "material": 0}]})
        size = p32.max(0) - p32.min(0)
        print("  %-10s %5d triangles, %.3f x %.3f x %.3f (x, y, z)" % (name, len(piece.idx), size[0], size[1], size[2]))
    views.append({"buffer": 0, "byteOffset": offset, "byteLength": len(png)})
    chunks.append(pad4(png))
    offset += len(pad4(png))
    doc = {
        "asset": {"version": "2.0", "generator": "Tools/split_sheets.py"},
        "scene": 0, "scenes": [{"nodes": list(range(len(meshes)))}],
        "nodes": [{"mesh": i, "name": m["name"]} for i, m in enumerate(meshes)],
        "meshes": meshes,
        "materials": [{"name": label, "doubleSided": double_sided, "pbrMetallicRoughness": {"baseColorTexture": {"index": 0}, "metallicFactor": 0.0, "roughnessFactor": 0.6}}],
        "textures": [{"source": 0, "sampler": 0}], "samplers": [{"magFilter": 9729, "minFilter": 9987, "wrapS": 10497, "wrapT": 10497}],
        "images": [{"bufferView": len(views) - 1, "mimeType": "image/png", "name": label + "_basecolor"}],
        "accessors": accessors, "bufferViews": views, "buffers": [{"byteLength": offset}],
    }
    text = pad4(json.dumps(doc, separators=(",", ":")).encode()).replace(b"\0", b" ")
    body = b"".join(chunks)
    with open(path, "wb") as f:
        f.write(struct.pack("<4sII", b"glTF", 2, 12 + 8 + len(text) + 8 + len(body)))
        f.write(struct.pack("<I4s", len(text), b"JSON") + text)
        f.write(struct.pack("<I4s", len(body), b"BIN\0") + body)
    print("wrote", path, len(body) // 1024, "KB")


def cut_picture(piece_list, picture, size):
    """The part of the sheet's picture the pieces use, as a size x size picture, with their uv refitted to it."""
    lo = np.min([p.pic_bounds()[0] for p in piece_list], axis=0)
    hi = np.max([p.pic_bounds()[1] for p in piece_list], axis=0)
    w, h = picture.size
    crop = picture.crop((int(lo[0] * w), int(lo[1] * h), int(np.ceil(hi[0] * w)), int(np.ceil(hi[1] * h))))
    # (glTF uv origin is the top left of the picture: v runs down the picture, so the crop's box is taken straight from uv.)
    box = np.array([int(lo[0] * w) / w, int(lo[1] * h) / h, int(np.ceil(hi[0] * w)) / w, int(np.ceil(hi[1] * h)) / h])
    for p in piece_list:
        p.uv = np.stack([(p.uv[:, 0] - box[0]) / (box[2] - box[0]), (p.uv[:, 1] - box[1]) / (box[3] - box[1])], axis=1).astype(np.float32)
    return crop, png_bytes(crop, size)


def simplified(piece, triangles):
    import ctypes

    import meshoptimizer
    flat = piece.idx.reshape(-1).astype(np.uint32)
    out = np.zeros(len(flat), np.uint32)
    count = meshoptimizer.simplify(out, flat, piece.pos.astype("f4"), target_index_count=triangles * 3, target_error=ctypes.c_float(1.0),
                                   options=meshoptimizer.SIMPLIFY_LOCK_BORDER)
    out = out[:count].astype(np.int64)
    used, inverse = np.unique(out, return_inverse=True)
    lod = Piece.__new__(Piece)
    lod.pos, lod.nrm, lod.uv, lod.idx = piece.pos[used], piece.nrm[used], piece.uv[used], inverse.reshape(-1, 3)
    return lod


# --- the sheets --------------------------------------------------------------------------------------------------------------------------

def by_rows(parts, rows):
    """Name the pieces of a grid-like sheet: rows (name lists, top row first) filled left to right in ascending x."""
    parts = sorted(parts, key=lambda p: -p.centre[1])
    names, k = {}, 0
    for row in rows:
        chosen = sorted(parts[k:k + len(row)], key=lambda p: p.centre[0])
        for name, piece in zip(row, chosen):
            names[name] = piece
        k += len(row)
    return names


# name: (how it stands: "up" / "lay", longest side in metres)
LOOSE = [("Berry", "up", 0.32), ("Meat", "up", 0.36), ("Acorn", "up", 0.38), ("Seed", "up", 0.30), ("Fish", "lay", 0.44), ("Honeydew", "up", 0.32),
         ("Twig", "lay", 0.45), ("Stone", "up", 0.36), ("Branch", "lay", 1.30), ("Pebble", "up", 0.13), ("Honeycomb", "lay", 0.42), ("Sack", "up", 0.40)]
LOOSE_ROWS = [["Berry", "Acorn", "Seed", "Meat", "Honeycomb"], ["Honeydew", "Fish", "HoneydewSpare", "Twig", "Stone"], ["Branch", "Sack", "Pebble"]]


def loose(src, dst):
    pos, nrm, uv, idx, picture = read_sheet(src)
    parts = [Piece(pos, nrm, uv, idx, t) for t in pieces(pos, idx, 50)]
    if len(parts) != 13:
        sys.exit("expected 13 pieces, found %d" % len(parts))
    named = by_rows(parts, LOOSE_ROWS)
    out = []
    for name, how, longest in LOOSE:
        piece = named[name]
        if how == "lay":
            piece.lie_flat()
            if name == "Fish" and not piece.face_dark_to(picture, threshold=260):
                print("  (no dark eye found on the fish)")
        piece.fit(longest)
        out.append((name, piece))
    _, png = cut_picture([p for _, p in out], picture, 1024)
    write_glb(dst, out, png, "loose")


FITTINGS = [("Lantern", 0.95), ("Snare", 0.60), ("HerbBed", 1.10), ("Sundial", 0.70), ("Watchtower", 3.20), ("Beehive", 0.60), ("Aloe", 0.60)]


def fittings(src, dst):
    pos, nrm, uv, idx, picture = read_sheet(src)
    tris = pieces(pos, idx, 1)
    parts = [Piece(pos, nrm, uv, idx, t) for t in tris]
    big = [p for p in parts if len(p.idx) >= 50]
    tiny = [p for p in parts if len(p.idx) < 50]
    if len(big) != 7:
        sys.exit("expected 7 pieces, found %d" % len(big))
    by = {len(p.idx): p for p in big}  # (the pieces are told apart by their triangle counts: 1982 bed, 914 snare, 868 tower, 328 lantern, 279 aloe, 242 hive, 70 sundial)
    wanted = {"HerbBed": 1982, "Snare": 914, "Watchtower": 868, "Lantern": 328, "Aloe": 279, "Beehive": 242, "Sundial": 70}
    named = {}
    for name, count in wanted.items():
        if count not in by:
            sys.exit("no piece of %d triangles for the %s" % (count, name))
        named[name] = by[count]
    tower = named["Watchtower"]
    for p in tiny:  # the tower's roof slab is a separate sliver on top of it
        if (p.lo >= tower.lo - 0.02).all() and (p.hi <= tower.hi + 0.02).all():
            allpos = np.concatenate([tower.pos, p.pos])
            tower.idx = np.concatenate([tower.idx, p.idx + len(tower.pos)])
            tower.nrm = np.concatenate([tower.nrm, p.nrm])
            tower.uv = np.concatenate([tower.uv, p.uv])
            tower.pos = allpos
            print("  (joined a %d-triangle sliver to the tower)" % len(p.idx))
    out = []
    for name, longest in FITTINGS:
        piece = named[name]
        piece.fit(longest)
        out.append((name, piece))
    _, png = cut_picture([p for _, p in out], picture, 1024)
    write_glb(dst, out, png, "fittings")


def upright_to_x(piece):
    """An upright thing (point up) lying along +X with the point at +X, 1 unit long and centred."""
    piece.transform(TURN)
    piece.pos -= piece.centre
    piece.pos /= piece.hi[0] - piece.lo[0]


def single(piece, picture, dst, size, lod_triangles=0, lod_size=256):
    """One piece in a file of its own with its own cut of the picture (and a cheaper twin <name>_lod.glb if asked)."""
    name = dst.rsplit("/", 1)[-1][:-4]
    full = Piece.__new__(Piece)
    full.pos, full.nrm, full.uv, full.idx = piece.pos, piece.nrm, piece.uv.copy(), piece.idx
    crop, png = cut_picture([full], picture, size)
    write_glb(dst, [(name, full)], png, name.lower())
    if lod_triangles:
        lod = simplified(full, lod_triangles)
        write_glb(dst[:-4] + "_lod.glb", [(name, lod)], png_bytes(crop, lod_size), name.lower())


def parts_of(src):
    pos, nrm, uv, idx, picture = read_sheet(src)
    return [Piece(pos, nrm, uv, idx, t) for t in pieces(pos, idx, 50)], picture


def gear(src, folder):
    parts, picture = parts_of(src)
    if len(parts) != 7:
        sys.exit("expected 7 pieces, found %d" % len(parts))
    count = {len(p.idx): p for p in parts}  # (armour 6062, helmet 1478, shield 598, bow 589, sword 426, spear 248, arrow 97)
    for name, tris, size in (("Sword", 426, 512), ("Spear", 248, 256), ("Bow", 589, 512), ("Arrow", 97, 256)):
        piece = count[tris]
        upright_to_x(piece)
        if name == "Bow":
            # The wood must bulge towards +Y and the string run straight on the other side: the wood is the side the vertices lie off the line between the two tips.
            tips = piece.pos[[np.argmin(piece.pos[:, 0]), np.argmax(piece.pos[:, 0])]]
            line_y = np.interp(piece.pos[:, 0], tips[:, 0], tips[:, 1])
            off = piece.pos[:, 1] - line_y
            if off[np.argmax(np.abs(off))] < 0:
                piece.transform(np.array([[1, 0, 0], [0, -1, 0], [0, 0, -1]], float))  # half a turn about the long axis
        single(piece, picture, "%s/%s.glb" % (folder, name), size)
    shield = count[598]
    shield.fit(1.0)  # 1 unit across, standing on y = 0, the face to +Z (the sheet's front)
    single(shield, picture, "%s/Shield.glb" % folder, 512)


def shield(src, dst):
    parts, picture = parts_of(src)
    piece = {len(p.idx): p for p in parts}[598]
    piece.fit(1.0)
    single(piece, picture, dst, 512, 300, 256)


def sack(src, dst):
    parts, picture = parts_of(src)
    piece = next(p for p in parts if len(p.idx) == 1012)
    piece.fit(1.0)  # (1 unit across its wider side)
    size = piece.hi - piece.lo
    print("  height %.3f of its width" % (size[1] / max(size[0], size[2])))
    single(piece, picture, dst, 512, 450, 256)


def aphid(src, dst):
    parts, picture = parts_of(src)
    piece = parts[0]
    # The head is the end with the dark eye: find it from the picture, then turn the aphid to face +Z.
    w, h = picture.size
    px = np.asarray(picture)
    u = np.clip((piece.uv[:, 0] * (w - 1)).astype(int), 0, w - 1)
    v = np.clip((piece.uv[:, 1] * (h - 1)).astype(int), 0, h - 1)
    dark = px[v, u].astype(float).sum(axis=1) < 150
    head = piece.pos[dark].mean(axis=0) - piece.centre if dark.any() else np.array([0.0, 0.0, 1.0])
    angle = np.arctan2(head[0], head[2])  # turn the head to +Z about y
    c, s = np.cos(angle), np.sin(angle)
    piece.transform(np.array([[c, 0, -s], [0, 1, 0], [s, 0, c]]))
    size = piece.hi - piece.lo
    piece.fit(1.0)
    print("  eye side", np.round(head, 2), "-> head now at z %+.2f" % (piece.pos[dark].mean(axis=0)[2] if dark.any() else 0))
    single(piece, picture, dst, 512)


def main():
    mode, src, dst = sys.argv[1:4]
    {"loose": loose, "fittings": fittings, "gear": gear, "shield": shield, "sack": sack, "aphid": aphid}[mode](src, dst)


if __name__ == "__main__":
    main()
