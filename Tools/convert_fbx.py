"""Convert a Mixamo-rigged FBX into a Bramblekin clip for the game.

    pip install bpy pygltflib
    python Tools/convert_fbx.py "Walking.fbx" Assets/Models/Bramblekin/Walking.glb
    python Tools/convert_fbx.py "Walking.fbx" out.glb --texture 256 --decimate 0.04

--texture N shrinks the texture to N x N (the source is 4096 x 4096, some
64 MB of GPU memory). --decimate R keeps that fraction of the triangles
(skin weights survive), for the low-detail model drawn when far away.

Uses Blender's FBX importer (assimp's mishandles Mixamo pre-rotations and
scrambles the limbs), bakes the armature's 0.01 scale and Z-up rotation
into the rig so the glb is Y-up and in metres, then pins the hip bone's
horizontal (X/Z) translation: the game moves the character itself, so
root motion baked into the clip would double it.
"""
import struct
import sys

import bpy
from pygltflib import GLTF2


def all_fcurves(action):
    if hasattr(action, "fcurves"):
        yield from action.fcurves
    for layer in getattr(action, "layers", []):  # Blender 4.4+ layered actions
        for strip in layer.strips:
            for bag in strip.channelbags:
                yield from bag.fcurves


def export_with_blender(src, dst, texture=None, decimate=None):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=src, automatic_bone_orientation=False)
    armature = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    scale = armature.scale[0]
    if decimate:
        mesh = next(o for o in bpy.data.objects if o.type == "MESH")
        bpy.ops.object.select_all(action="DESELECT")
        bpy.context.view_layer.objects.active = mesh
        mod = mesh.modifiers.new("lod", "DECIMATE")
        mod.ratio = decimate
        while mesh.modifiers[0] != mod:  # decimate before the armature deform
            bpy.ops.object.modifier_move_up(modifier=mod.name)
        bpy.ops.object.modifier_apply(modifier=mod.name)
    if texture:
        for image in bpy.data.images:
            image.scale(texture, texture)
            image.pack()
    bpy.ops.object.select_all(action="SELECT")
    bpy.context.view_layer.objects.active = armature
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    # Applying scale shrinks the rest skeleton but not the bones' location keys.
    for action in bpy.data.actions:
        for fc in all_fcurves(action):
            if fc.data_path.endswith(".location"):
                for kp in fc.keyframe_points:
                    kp.co[1] *= scale
                    kp.handle_left[1] *= scale
                    kp.handle_right[1] *= scale
    bpy.ops.export_scene.gltf(filepath=dst, export_format="GLB", export_animations=True,
                              export_force_sampling=True, export_yup=True)


def pin_root_motion(path):
    g = GLTF2().load(path)
    blob = bytearray(g.binary_blob())
    hips = next(i for i, n in enumerate(g.nodes) if n.name == "mixamorig:Hips")
    anim = g.animations[0]
    for ch in anim.channels:
        if ch.target.node != hips or ch.target.path != "translation":
            continue
        acc = g.accessors[anim.samplers[ch.sampler].output]
        bv = g.bufferViews[acc.bufferView]
        base = (bv.byteOffset or 0) + (acc.byteOffset or 0)
        stride = bv.byteStride or 12
        x0, _, z0 = struct.unpack_from("<3f", blob, base)
        for i in range(acc.count):
            _, y, _ = struct.unpack_from("<3f", blob, base + i * stride)
            struct.pack_into("<3f", blob, base + i * stride, x0, y, z0)
    g.set_binary_blob(bytes(blob))
    g.save(path)


if __name__ == "__main__":
    import argparse
    ap = argparse.ArgumentParser()
    ap.add_argument("src"); ap.add_argument("dst")
    ap.add_argument("--texture", type=int); ap.add_argument("--decimate", type=float)
    a = ap.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else sys.argv[1:])
    export_with_blender(a.src, a.dst, a.texture, a.decimate)
    pin_root_motion(a.dst)
