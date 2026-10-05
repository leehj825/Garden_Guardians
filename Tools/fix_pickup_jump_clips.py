"""Hands down in the pick-up clip, straight legs in the jump clip - no Blender, no numpy.

    python3 Tools/fix_pickup_jump_clips.py Assets/Models/Bramblekin

The Mixamo pick-up clip spreads both arms out to the sides and the jump clip throws the legs back and out. For the bones named below, every
rotation key of the clip is replaced by the first frame of Idle.glb (arms hanging, legs straight and together); everything else in the clip
(the spine bending, the hips moving) is untouched. The files keep their size (the rotations are rewritten in place). Run it once on freshly
converted clips.
"""
import json
import struct
import sys

PICKUP_BONES = [side + part for side in ("Left", "Right")
                for part in ("Shoulder", "Arm", "ForeArm", "Hand")]
FINGER_PARTS = ("Thumb", "Index", "Middle", "Ring", "Pinky")
PICKUP_BONES += [side + finger + str(n) for side in ("Left", "Right") for finger in FINGER_PARTS for n in (1, 2, 3, 4)]
JUMP_BONES = [side + part for side in ("Left", "Right") for part in ("UpLeg", "Leg", "Foot", "ToeBase", "Toe_End")]


def read_glb(path):
    raw = bytearray(open(path, "rb").read())
    jlen = struct.unpack("<I", raw[12:16])[0]
    doc = json.loads(bytes(raw[20:20 + jlen]))
    base = 20 + jlen + 8  # past the BIN chunk's header
    return doc, raw, base


def bone(doc, ch):
    return doc["nodes"][ch["target"]["node"]]["name"].split(":")[-1]


def rotation_tracks(doc, raw, base):
    """{bone name: (offset of its first key in raw, number of keys)} for the rotation channels of the first animation."""
    anim = doc["animations"][0]
    tracks = {}
    for ch in anim["channels"]:
        if ch["target"]["path"] != "rotation":
            continue
        acc = doc["accessors"][anim["samplers"][ch["sampler"]]["output"]]
        assert acc["componentType"] == 5126 and acc["type"] == "VEC4", "rotation keys are expected as float quaternions"
        view = doc["bufferViews"][acc["bufferView"]]
        tracks[bone(doc, ch)] = (base + view.get("byteOffset", 0) + acc.get("byteOffset", 0), acc["count"])
    return tracks


def patch(clip_path, idle_path, bones):
    doc, raw, base = read_glb(clip_path)
    idoc, iraw, ibase = read_glb(idle_path)
    mine = rotation_tracks(doc, raw, base)
    idle = rotation_tracks(idoc, iraw, ibase)
    done = 0
    for name in bones:
        if name not in mine or name not in idle:
            continue
        first = iraw[idle[name][0]:idle[name][0] + 16]  # Idle's first key (4 floats)
        offset, count = mine[name]
        for k in range(count):
            raw[offset + 16 * k:offset + 16 * k + 16] = first
        done += 1
    open(clip_path, "wb").write(raw)
    print(f"{clip_path}: {done} bones set to the idle pose")


def main():
    folder = sys.argv[1].rstrip("/")
    patch(f"{folder}/PickingUp.glb", f"{folder}/Idle.glb", PICKUP_BONES)
    patch(f"{folder}/Jump.glb", f"{folder}/Idle.glb", JUMP_BONES)


if __name__ == "__main__":
    main()
