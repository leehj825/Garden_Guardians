"""Make the kit's boulders as plain stone.

    python Tools/procedural/make_rocks.py

The boulders cut out of the terrain models come with ferns and grass growing on them and are hollow shells, so each
rock prop's glb is replaced by a lumpy, flat-bottomed boulder of the same size, textured with the stone from
terrain4_rock_0.glb (which has no plants on it). Origin at the base centre, like every other prop.
"""
import math
import os
import re
import sys

import bpy
import bmesh
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
PROPS = os.path.join(HERE, "..", "..", "Assets", "Models", "Procedural", "props")
KIT = os.path.join(HERE, "..", "..", "Source", "World", "Procedural", "ProceduralKit.cs")
STONE = os.path.join(HERE, "stone_source_terrain4_rock_0.glb")


def stone_texture():
    """The stone texture from the (plant-free) rock, saved as a small png."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=STONE)
    image = next(i for i in bpy.data.images if i.size[0] > 0)
    path = os.path.join(HERE, "stone.png")
    image.scale(256, 256)
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    return path


def rock(name, height, reach, seed, texture):
    rng = np.random.default_rng(seed)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=2, radius=1.0)
    rx = 0.55 * reach
    rz = rx * rng.uniform(0.7, 0.9)
    hz = min(height * 0.8, rx * rng.uniform(0.75, 1.0))
    phase = rng.uniform(0, math.tau, 6)
    for v in bm.verts:
        d = v.co.normalized()
        bump = 1.0 + 0.16 * math.sin(3 * d.x + phase[0]) * math.cos(2 * d.y + phase[1]) + 0.10 * math.sin(5 * d.z + 4 * d.x + phase[2]) + rng.uniform(-0.05, 0.05)
        v.co = Vector((d.x * rx * bump, d.y * rz * bump, d.z * hz * bump))
    for v in bm.verts:
        v.co.z = max(v.co.z, -0.12 * hz)  # flat underside, sunk a little into the ground
    for v in bm.verts:
        v.co.z += 0.12 * hz
    bm.normal_update()
    for f in bm.faces:
        f.smooth = False
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.cube_project(cube_size=2.0 * hz + 1.0)
    bpy.ops.object.mode_set(mode="OBJECT")
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    node = material.node_tree.nodes.new("ShaderNodeTexImage")
    node.image = bpy.data.images.load(texture)
    node.image.pack()
    material.node_tree.links.new(node.outputs["Color"], material.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
    mesh.materials.append(material)
    bpy.ops.export_scene.gltf(filepath=os.path.join(PROPS, name), export_format="GLB", use_selection=True, export_yup=True, export_apply=True)


def main():
    texture = stone_texture()
    kit = open(KIT).read()
    rocks = re.findall(r'File = "props/([^"]+)", Kind = KitKind\.Rock, Height = ([\d.]+)f, Reach = ([\d.]+)f', kit)
    for index, (name, height, reach) in enumerate(rocks):
        rock(name, float(height), float(reach), 100 + index, texture)
        print("rock", name, height, reach)


if __name__ == "__main__":
    main()
