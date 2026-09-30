"""A 3D picture of a generated garden with the real textures: the ground from the tiles, the oak, boulders and plants from the prop kits.

    python Tools/procedural/render_preview.py --seed 6 --out garden6.png [--view wide|oak|pond]

Rendered with Blender's Cycles on the CPU (no GPU needed), so it takes a minute or so.
"""
import argparse
import math
import os
import sys

import bpy  # noqa: I001
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import generate_terrain as G  # noqa: E402


def build_ground(garden, texture_path):
    n, half = garden.n, garden.half
    xs = -half + np.arange(n) * G.STEP
    verts = [(float(xs[i]), -float(xs[j]), float(garden.ground[j, i])) for j in range(n) for i in range(n)]
    faces = [(j * n + i, j * n + i + 1, (j + 1) * n + i + 1, (j + 1) * n + i) for j in range(n - 1) for i in range(n - 1)]
    # Game z runs down Blender's y, so going +j goes -y: this order faces up when reversed.
    faces = [f[::-1] for f in faces]
    mesh = bpy.data.meshes.new("ground")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    uv = mesh.uv_layers.new(name="UVMap")
    for poly in mesh.polygons:
        for loop_index in poly.loop_indices:
            v = mesh.vertices[mesh.loops[loop_index].vertex_index].co
            uv.data[loop_index].uv = ((v.x + half) / (2 * half), 1.0 - (-v.y + half) / (2 * half))
    for poly in mesh.polygons:
        poly.use_smooth = True
    obj = bpy.data.objects.new("ground", mesh)
    bpy.context.collection.objects.link(obj)
    mat = bpy.data.materials.new("ground")
    mat.use_nodes = True
    tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(texture_path)
    tex.interpolation = "Cubic"
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 1.0
    mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    mesh.materials.append(mat)
    return obj


def add_water(garden):
    bpy.ops.mesh.primitive_plane_add(size=2 * garden.half, location=(0, 0, G.WATER_LEVEL))
    plane = bpy.context.active_object
    mat = bpy.data.materials.new("water")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (0.16, 0.36, 0.62, 1)
    bsdf.inputs["Roughness"].default_value = 0.08
    bsdf.inputs["Alpha"].default_value = 0.55
    plane.data.materials.append(mat)
    return plane


def import_kits():
    """Every kit's props, hidden templates by (source model, name)."""
    templates = {}
    for path in sorted(os.path.join(HERE, "kit", f) for f in os.listdir(os.path.join(HERE, "kit")) if f.endswith(".glb")):
        before = set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=path)
        source = os.path.basename(path).replace("kit_", "").replace(".glb", "") + ".glb"
        for obj in set(bpy.data.objects) - before:
            if obj.type == "MESH":
                base = obj.name.split(".")[0]
                templates[(source, base)] = obj
                obj.hide_render = True
                obj.hide_viewport = True
    return templates


def place_props(garden, templates):
    for prop in [garden.oak] + garden.props:
        template = templates.get((prop["source"], prop["name"])) if prop.get("source") else None
        if template is None:
            continue
        obj = template.copy()
        obj.data = template.data  # shares the mesh
        obj.hide_render = False
        obj.hide_viewport = False
        bpy.context.collection.objects.link(obj)
        obj.location = (prop["x"], -prop["z"], prop["base"])
        obj.rotation_euler = (0, 0, -prop["yaw"])
        obj.scale = (prop["scale"],) * 3


def set_up_render(view, half, out, samples):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    scene.cycles.use_denoising = True
    scene.render.resolution_x, scene.render.resolution_y = 1400, 900
    scene.render.filepath = out
    world = bpy.data.worlds.new("sky")
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (0.55, 0.72, 0.95, 1)
    bg.inputs["Strength"].default_value = 1.0
    scene.world = world
    sun = bpy.data.lights.new("sun", "SUN")
    sun.energy = 3.5
    sun.angle = math.radians(4)
    sun_obj = bpy.data.objects.new("sun", sun)
    sun_obj.rotation_euler = (math.radians(50), 0, math.radians(35))
    bpy.context.collection.objects.link(sun_obj)
    cam_data = bpy.data.cameras.new("cam")
    cam_data.lens = 32
    cam = bpy.data.objects.new("cam", cam_data)
    bpy.context.collection.objects.link(cam)
    scene.camera = cam
    return cam


def aim(cam, location, target):
    cam.location = location
    direction = Vector(target) - Vector(location)
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--out", required=True)
    ap.add_argument("--view", default="wide", choices=["wide", "oak", "pond"])
    ap.add_argument("--samples", type=int, default=32)
    ap.add_argument("--no-props", action="store_true", help="the ground and water only")
    args = ap.parse_args()

    bpy.ops.wm.read_factory_settings(use_empty=True)
    kit = G.load_kit()
    garden = G.Garden(args.seed).generate(kit)
    texture = os.path.abspath(args.out + ".ground.png")
    garden.picture(G.load_tiles(), px_per_m=12, markers=False).save(texture)
    build_ground(garden, texture)
    add_water(garden)
    if not args.no_props:
        place_props(garden, import_kits())
    cam = set_up_render(args.view, garden.half, os.path.abspath(args.out), args.samples)
    h = garden.half
    if args.view == "wide":
        aim(cam, (-h * 0.2, -h * 1.9, h * 1.15), (0, 0, 0))
    elif args.view == "oak":
        ox, oz = garden.oak["x"], garden.oak["z"]
        aim(cam, (ox - 40, -oz - 55, 30), (ox, -oz, 8))
    else:
        p = garden.ponds[0]
        aim(cam, (p["x"] - 25, -p["z"] - 38, 16), (p["x"], -p["z"], -1))
    bpy.ops.render.render(write_still=True)
    os.remove(texture)
    print("wrote", args.out)


if __name__ == "__main__":
    main()
