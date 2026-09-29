"""Shrink a Tripo prop model (acorn house, berry farm, spider) for the game.

    pip install bpy
    python Tools/convert_prop.py acorn_house.glb Assets/Models/Props/AcornHouse.glb --tris 20000
    python Tools/convert_prop.py Spider.glb Assets/Models/Props/Spider.glb --tris 0

--tris N decimates to N triangles (0 keeps the mesh as it is). --texture N
shrinks every texture to N x N (default 1024) and stores it as PNG: this
raylib build can't read a glb's embedded JPEG. Any armature is applied and
removed, leaving the mesh in its rest pose: the game moves these props itself.
The bottom of the model (faces pointing down at ground level) is dropped.
"""
import argparse

import bpy  # noqa: I001 (bpy first: it registers bmesh)
import bmesh


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("dst")
    ap.add_argument("--tris", type=int, default=20000)
    ap.add_argument("--texture", type=int, default=1024)
    args = ap.parse_args()

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=args.src)
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    mesh = max(meshes, key=lambda o: len(o.data.polygons))  # the model itself; the spider's glb also carries a bone-shape icosphere
    for o in meshes:
        if o is not mesh:
            bpy.data.objects.remove(o)
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = mesh
    mesh.select_set(True)
    for mod in list(mesh.modifiers):  # bake the rest pose in and drop the rig
        if mod.type == "ARMATURE":
            bpy.ops.object.modifier_apply(modifier=mod.name)
    mesh.parent = None
    for o in list(bpy.data.objects):
        if o.type == "ARMATURE":
            bpy.data.objects.remove(o)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for group in list(mesh.vertex_groups):
        mesh.vertex_groups.remove(group)

    bm = bmesh.new()
    bm.from_mesh(mesh.data)
    height = max(v.co.z for v in bm.verts)
    floor = [f for f in bm.faces if f.normal.z < -0.5 and f.calc_center_median().z < 0.03 * height]
    bmesh.ops.delete(bm, geom=floor, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.to_mesh(mesh.data)
    bm.free()

    have = len(mesh.data.polygons)
    if args.tris and have > args.tris:
        mod = mesh.modifiers.new("lod", "DECIMATE")
        mod.ratio = args.tris / have
        bpy.ops.object.modifier_apply(modifier=mod.name)
    for p in mesh.data.polygons:
        p.use_smooth = True

    for image in bpy.data.images:
        if image.size[0] > args.texture:
            image.scale(args.texture, args.texture)
        image.pack()

    bpy.ops.export_scene.gltf(filepath=args.dst, export_format="GLB", export_yup=True, export_image_format="AUTO", use_selection=False)
    print("wrote", args.dst, len(mesh.data.polygons), "triangles")


if __name__ == "__main__":
    main()
