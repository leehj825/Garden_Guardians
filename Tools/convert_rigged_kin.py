"""Put a rigged Tripo Bramblekin (a glb with its own Mixamo-named skeleton and skin weights) onto the game's 33-joint skeleton, with no Blender.

    pip install numpy pillow meshoptimizer
    python3 Tools/convert_rigged_kin.py Male_kin.glb   Assets/Models/Bramblekin/Male.glb
    python3 Tools/convert_rigged_kin.py Female_kin.glb Assets/Models/Bramblekin/Female.glb --female

Writes Male.glb, Male_lod1.glb and Male_lod2.glb (the mesh cut to --lod-tris triangles, with 1024 px and 512 px pictures). Every clip in
Assets/Models/Bramblekin is built on the game's skeleton (Tools/kin_skeleton.json), so the mesh is skinned to that: the model (Y-up, facing +Z) is scaled to a
height of 1 and turned into the skeleton's bind space (Z-up, facing -Y). retarget_rigged_clips.py fits the supplied idle, walk and run clips to the same skeleton.

The skin weights are the model's own, with its fingers folded into the hand (the game's skeleton has the index finger only), then corrected so that
loose parts never follow the arms (they would swing out with a fist in a walk or a blow):
  * cloth (the green of the vest, shorts and dress, read off the texture) takes no weight from the shoulders, arms or hands, and only half of what it
    had from the legs, so a hem stays with the hips instead of streaming out behind a raised leg;
  * hair (what is not cloth above --hair-base: the crown, and the bob that hangs to the shoulders) takes none from the arms either, fading in over --hair-fade;
  * the head is rigid (one bone: --head-base and above) so the face does not wobble as the neck and shoulders swing.
"""
import argparse
import colorsys
import io
import json
import os
import struct
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from convert_female import accessor, read_glb  # noqa: E402
from convert_tripo_kin import png_of, simplified  # noqa: E402

Image.MAX_IMAGE_PIXELS = None
SKELETON = os.path.join(os.path.dirname(os.path.abspath(__file__)), "kin_skeleton.json")
ARM_KEYS = ("Shoulder", "Arm", "Hand")  # (ForeArm and the index finger bones contain these)
LEG_KEYS = ("UpLeg", "Leg", "Foot", "Toe")
CLOTH_LEG_KEEP = 0.5  # how much of its leg weight cloth keeps
CLOTH_HUE = (50.0, 170.0)  # degrees: the wood and the hair are orange (15 to 45), the vest, shorts and dress yellow-green (60 to 90)
CLOTH_SATURATION = 0.25


def load_model(path):
    doc, binary = read_glb(path)
    prim = doc["meshes"][0]["primitives"][0]
    at = prim["attributes"]
    pos = accessor(doc, binary, at["POSITION"]).astype(np.float64)
    nrm = accessor(doc, binary, at["NORMAL"]).astype(np.float64)
    uv = accessor(doc, binary, at["TEXCOORD_0"]).astype(np.float32)
    joints = accessor(doc, binary, at["JOINTS_0"]).astype(np.int64)
    weights = accessor(doc, binary, at["WEIGHTS_0"]).astype(np.float64)
    idx = accessor(doc, binary, prim["indices"]).astype(np.uint32)
    names = [doc["nodes"][j]["name"].split(":")[-1] for j in doc["skins"][0]["joints"]]
    image = next(i for i in doc["images"] if "basecolor" in i.get("name", ""))
    view = doc["bufferViews"][image["bufferView"]]
    offset = view.get("byteOffset", 0)
    picture = Image.open(io.BytesIO(binary[offset:offset + view["byteLength"]])).convert("RGB")
    return pos, nrm, uv, joints, weights, idx, names, picture


def game_joint_names(skeleton):
    return [skeleton["nodes"][j]["name"].split(":")[-1] for j in skeleton["joints"]]


def fold_weights(joints, weights, source_names, game_names):
    """The model's weights on the game's 33 joints: a bone the game's skeleton has keeps its weight, a finger it has not (all but the index) goes to the hand."""
    target = []
    for n in source_names:
        if n in game_names:
            target.append(game_names.index(n))
        else:
            side = "Left" if n.startswith("Left") else "Right"
            target.append(game_names.index(side + "Hand"))
    target = np.array(target)
    out = np.zeros((len(joints), len(game_names)))
    for k in range(joints.shape[1]):
        np.add.at(out, (np.arange(len(joints)), target[joints[:, k]]), weights[:, k])
    return out / np.maximum(out.sum(axis=1, keepdims=True), 1e-9)


def cloth_mask(uv, picture):
    """True where the texture under a vertex is the green of the clothes."""
    small = np.asarray(picture.resize((1024, 1024), Image.LANCZOS), dtype=np.float64) / 255.0
    u = np.clip((uv[:, 0] * 1023).astype(int), 0, 1023)
    v = np.clip((uv[:, 1] * 1023).astype(int), 0, 1023)
    hsv = np.array([colorsys.rgb_to_hsv(*c) for c in small[v, u]])
    hue = hsv[:, 0] * 360.0
    return (hue > CLOTH_HUE[0]) & (hue < CLOTH_HUE[1]) & (hsv[:, 1] > CLOTH_SATURATION)


def corrected_weights(dense, points, cloth, game_names, head_base, hair_base, hair_fade):
    """Apply the rules in the module's docstring to dense per-joint weights (vertices x 33); points are in bind space (z up)."""
    arm = np.array([any(k in n for k in ARM_KEYS) for n in game_names])
    leg = np.array([any(k in n for k in LEG_KEYS) for n in game_names])
    z = points[:, 2]
    w = dense.copy()
    # Cloth: no arms, half the legs.
    w[np.ix_(cloth, np.where(arm)[0])] = 0.0
    w[np.ix_(cloth, np.where(leg)[0])] *= CLOTH_LEG_KEEP
    # Hair: no arms, fading in from the base of the hair (cloth in the hair, the leaves, is cloth already).
    hair = ~cloth & (z > hair_base)
    fade = np.clip((z - hair_base) / max(hair_fade, 1e-6), 0.0, 1.0)
    keep = 1.0 - fade * hair
    w[:, arm] *= keep[:, None]
    # Whatever lost all its weight goes to the nearest torso bone it had, else the spine.
    empty = w.sum(axis=1) < 1e-6
    spine = game_names.index("Spine1")
    w[empty, spine] = 1.0
    w /= w.sum(axis=1, keepdims=True)
    # Rigid head: one bone, from head_base up (the leaves at the crown follow the head too).
    head = game_names.index("Head")
    rigid = z >= head_base
    w[rigid] = 0.0
    w[rigid, head] = 1.0
    return w


def four_largest(w):
    order = np.argsort(-w, axis=1)[:, :4]
    top = np.take_along_axis(w, order, axis=1)
    top /= np.maximum(top.sum(axis=1, keepdims=True), 1e-9)
    return order.astype("u1"), top.astype("<f4")


def write_glb(path, doc, binary):
    text = json.dumps(doc, separators=(",", ":")).encode()
    text += b" " * ((-len(text)) % 4)
    binary += b"\0" * ((-len(binary)) % 4)
    with open(path, "wb") as f:
        f.write(struct.pack("<4sII", b"glTF", 2, 12 + 8 + len(text) + 8 + len(binary)))
        f.write(struct.pack("<I4s", len(text), b"JSON") + text)
        f.write(struct.pack("<I4s", len(binary), b"BIN\0") + binary)


def mesh_glb(skeleton, pos, nrm, uv, indices, joints, weights, png, texture_name):
    """A glb of the game's skeleton (its nodes, joint order and inverse bind matrices) with one skinned mesh and its picture."""
    blobs, views, accessors = [], [], []

    def add(data, target=None):
        pad = (-sum(len(b) for b in blobs)) % 4
        if pad:
            blobs.append(b"\0" * pad)
        view = {"buffer": 0, "byteOffset": sum(len(b) for b in blobs), "byteLength": len(data)}
        if target:
            view["target"] = target
        blobs.append(bytes(data))
        views.append(view)
        return len(views) - 1

    def accessor_of(data, component, kind, count, target=None, bounds=None):
        a = {"bufferView": add(data, target), "byteOffset": 0, "componentType": component, "count": count, "type": kind}
        if bounds:
            a["min"], a["max"] = bounds
        accessors.append(a)
        return len(accessors) - 1

    n = len(pos)
    p = pos.astype("<f4")
    attributes = {
        "POSITION": accessor_of(p.tobytes(), 5126, "VEC3", n, 34962, (p.min(axis=0).tolist(), p.max(axis=0).tolist())),
        "NORMAL": accessor_of(nrm.astype("<f4").tobytes(), 5126, "VEC3", n, 34962),
        "TEXCOORD_0": accessor_of(uv.astype("<f4").tobytes(), 5126, "VEC2", n, 34962),
        "JOINTS_0": accessor_of(joints.astype("u1").tobytes(), 5121, "VEC4", n, 34962),
        "WEIGHTS_0": accessor_of(weights.astype("<f4").tobytes(), 5126, "VEC4", n, 34962),
    }
    index = accessor_of(indices.astype("<u2").tobytes(), 5123, "SCALAR", len(indices), 34963)
    ibm = np.array(skeleton["inverseBindMatrices"], dtype="<f4")
    ibm_acc = accessor_of(ibm.tobytes(), 5126, "MAT4", len(ibm))
    image_view = add(png)
    nodes = []
    for node in skeleton["nodes"]:
        out = {k: v for k, v in node.items() if k != "mesh"}
        if node.get("mesh"):
            out["mesh"], out["skin"] = 0, 0
        nodes.append(out)
    root = next(i for i, node in enumerate(skeleton["nodes"]) if node["name"] == "Armature")
    doc = {
        "asset": {"version": "2.0", "generator": "Tools/convert_rigged_kin.py"},
        "scene": 0,
        "scenes": [{"name": "Scene", "nodes": [root]}],
        "nodes": nodes,
        "skins": [{"inverseBindMatrices": ibm_acc, "joints": skeleton["joints"], "name": "Armature"}],
        "meshes": [{"name": "kin", "primitives": [{"attributes": attributes, "indices": index, "mode": 4, "material": 0}]}],
        "materials": [{"pbrMetallicRoughness": {"baseColorFactor": [1, 1, 1, 1], "metallicFactor": 1.0, "roughnessFactor": 1.0,
                                                "baseColorTexture": {"index": 0, "texCoord": 0}},
                       "emissiveFactor": [0, 0, 0], "alphaMode": "BLEND", "doubleSided": True, "name": "kin"}],
        "textures": [{"sampler": 0, "source": 0}],
        "images": [{"mimeType": "image/png", "bufferView": image_view, "name": texture_name}],
        "samplers": [{"magFilter": 9729, "minFilter": 9987, "wrapS": 10497, "wrapT": 10497}],
        "accessors": accessors,
        "bufferViews": views,
        "buffers": [{"byteLength": sum(len(b) for b in blobs)}],
    }
    return doc, b"".join(blobs)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("model")
    ap.add_argument("dst")
    ap.add_argument("--female", action="store_true", help="only names the picture")
    ap.add_argument("--texture", type=int, default=2048)
    ap.add_argument("--lod-tris", type=int, nargs=2, default=(3500, 900), help="triangles in the first and second lower level of detail")
    ap.add_argument("--head-base", type=float, default=0.56, help="height (the model is 1 tall) from which the head is one rigid bone")
    ap.add_argument("--hair-base", type=float, default=0.49, help="height from which what is not cloth is hair, which takes no weight from the arms")
    ap.add_argument("--hair-fade", type=float, default=0.03, help="over how far above --hair-base the arms' weight fades out")
    ap.add_argument("--brighten", type=float, default=1.0, help="gamma applied to the picture's value (below 1 lightens)")
    ap.add_argument("--no-lods", action="store_true")
    args = ap.parse_args()

    skeleton = json.load(open(SKELETON))
    game_names = game_joint_names(skeleton)
    pos_y, nrm_y, uv, joints, weights, idx, names, picture = load_model(args.model)
    if len(pos_y) > 65535:
        sys.exit("%d vertices: raylib meshes take 16-bit indices" % len(pos_y))
    scale = 1.0 / pos_y[:, 1].max()  # the skeleton's height of 1, feet at 0

    def to_bind(a, s=1.0):  # Y-up facing +Z  ->  Z-up facing -Y
        return np.stack([a[:, 0], -a[:, 2], a[:, 1]], axis=1) * s

    pos, nrm = to_bind(pos_y, scale), to_bind(nrm_y)
    cloth = cloth_mask(uv, picture)
    print(int(cloth.sum()), "of", len(cloth), "vertices are cloth")
    dense = fold_weights(joints, weights, names, game_names)
    dense = corrected_weights(dense, pos, cloth, game_names, args.head_base, args.hair_base, args.hair_fade)
    j4, w4 = four_largest(dense)

    if args.brighten != 1.0:
        hsv = np.asarray(picture.convert("HSV")).astype("f4")
        hsv[..., 2] = 255.0 * (hsv[..., 2] / 255.0) ** args.brighten
        picture = Image.fromarray(np.clip(hsv, 0, 255).astype("u1"), "HSV").convert("RGB")

    outputs = [(args.dst, args.texture, None)]
    if not args.no_lods:
        stem = args.dst[:-4]
        outputs += [(stem + "_lod1.glb", 1024, args.lod_tris[0]), (stem + "_lod2.glb", 512, args.lod_tris[1])]
    flat = idx.reshape(-1)
    tag = "female" if args.female else "male"
    for path, size, tris in outputs:
        p_, n_, u_, i_, jj, ww = pos, nrm, uv, flat, j4, w4
        if tris is not None and tris * 3 < len(flat):
            p_, n_, u_, i_, jj, ww = simplified(pos, nrm, uv, flat, j4, w4, tris)
        doc, binary = mesh_glb(skeleton, p_, n_, u_, i_, jj, ww, png_of(picture, size), "Bramblekin_" + tag + "_basecolor")
        write_glb(path, doc, binary)
        print("wrote", path, len(p_), "vertices,", len(i_) // 3, "triangles,", size, "px texture")


if __name__ == "__main__":
    main()
