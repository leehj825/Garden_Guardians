"""Build the arrow (Assets/Models/Props/Gear/Arrow.glb): a wooden shaft, a stone head and three feathers, lying along +X, centred, 1 unit long
(tail at -0.5, point at +0.5), like the other gear models (see Source/Engine/GearModels.cs). Plain colours, no texture.

    pip install bpy
    python3 Tools/make_arrow.py Assets/Models/Props/Gear/Arrow.glb
"""
import math
import sys

import bpy

out = sys.argv[1] if len(sys.argv) > 1 else "Arrow.glb"
bpy.ops.wm.read_factory_settings(use_empty=True)


def material(name, rgb):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.9
    return m


wood, stone, feather, dark = (material("Wood", (0.55, 0.36, 0.17)), material("Stone", (0.42, 0.42, 0.45)),
                              material("Feather", (0.93, 0.9, 0.8)), material("Cock", (0.55, 0.15, 0.1)))
parts = []


def add(obj, mat):
    obj.data.materials.append(mat)
    parts.append(obj)


# Shaft: x from -0.5 to 0.38, along X (cylinders stand along Z, so turn them).
bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.016, depth=0.88, location=(-0.06, 0, 0), rotation=(0, math.pi / 2, 0))
add(bpy.context.object, wood)
# Head: a stone point from x = 0.36 to 0.5.
bpy.ops.mesh.primitive_cone_add(vertices=8, radius1=0.05, radius2=0.0, depth=0.16, location=(0.43, 0, 0), rotation=(0, math.pi / 2, 0))
add(bpy.context.object, stone)
# Three feathers round the tail (thin boxes), one of them the dark cock feather.
for i in range(3):
    angle = i * 2 * math.pi / 3 + math.pi / 2
    bpy.ops.mesh.primitive_cube_add(size=1, location=(-0.4, 0.045 * math.cos(angle), 0.045 * math.sin(angle)))
    fin = bpy.context.object
    fin.scale = (0.2, 0.09, 0.004)  # wide in Y, thin in Z, then turned about X to point outwards at its angle
    fin.rotation_euler = (angle, 0, 0)
    add(fin, dark if i == 0 else feather)
# A nock at the very tail.
bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=4, radius=0.022, location=(-0.5, 0, 0))
add(bpy.context.object, wood)

bpy.ops.object.select_all(action="DESELECT")
for p in parts:
    p.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)  # (bake every part's turn into the vertices, so the file has no node rotation)
bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", export_apply=True)
print("wrote", out)
