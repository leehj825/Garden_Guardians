"""Make a lower-detail copy of a rigged Bramblekin glb, for drawing kin far away.

    pip install bpy numpy scipy pillow
    python Tools/convert_kin_lod.py Assets/Models/Bramblekin/Walking.glb Assets/Models/Bramblekin/Walking_lod1.glb --tris 9000 --texture 1024
    python Tools/convert_kin_lod.py Assets/Models/Bramblekin/Walking.glb Assets/Models/Bramblekin/Walking_lod2.glb --tris 2500 --texture 512

The mesh is decimated in Blender (as plain geometry, in the glb's own bind
space, so it cannot drift off the skeleton) and every new vertex takes the
skin weights and normal of the nearest vertex of the full-detail mesh. The
result is the same glb as the source — same nodes, skin and clips — with a
cheaper mesh and a smaller texture, so the same pose drives either.
(Walking_lod.glb, the first attempt, was decimated through a Blender
armature and came out out of step with the skeleton.)
"""
import argparse
import io
import os
import sys

import bpy  # noqa: I001 (bpy first: it registers the other Blender modules)
import numpy as np
from PIL import Image
from scipy.spatial import cKDTree

sys.path.insert(0, os.path.dirname(__file__))
from convert_female import accessor, build, read_glb, write_glb  # noqa: E402


def decimate(positions, indices, uv, tris):
    """Decimated (positions, uv per corner, triangles as corner-vertex indices)."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    faces = indices.reshape(-1, 3)
    mesh = bpy.data.meshes.new("kin")
    mesh.from_pydata(positions.tolist(), [], faces.tolist())
    layer = mesh.uv_layers.new(name="UVMap")
    corner_uv = uv[faces.reshape(-1)]  # the glb keeps one uv per vertex, so one per corner follows
    layer.data.foreach_set("uv", corner_uv.reshape(-1))
    ob = bpy.data.objects.new("kin", mesh)
    bpy.context.collection.objects.link(ob)
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    mod = ob.modifiers.new("lod", "DECIMATE")
    mod.ratio = min(1.0, tris / len(faces))
    bpy.ops.object.modifier_apply(modifier=mod.name)
    mesh = ob.data
    mesh.calc_loop_triangles()
    co = np.empty(len(mesh.vertices) * 3, "f4")
    mesh.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    loops = np.empty(len(mesh.loops), "i4")
    mesh.loops.foreach_get("vertex_index", loops)
    luv = np.empty(len(mesh.loops) * 2, "f4")
    mesh.uv_layers.active.data.foreach_get("uv", luv)
    luv = luv.reshape(-1, 2)
    tri_loops = np.empty(len(mesh.loop_triangles) * 3, "i4")
    mesh.loop_triangles.foreach_get("loops", tri_loops)
    # One output vertex per (vertex, uv): a seam splits it.
    key = np.concatenate([loops[:, None].astype("f8"), np.round(luv.astype("f8"), 5)], axis=1)
    _, first, inverse = np.unique(key, axis=0, return_index=True, return_inverse=True)
    inverse = inverse.ravel()
    out_pos = co[loops[first]]
    out_uv = luv[first]
    out_tri = inverse[tri_loops].reshape(-1)
    return out_pos, out_uv, out_tri


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("dst")
    ap.add_argument("--tris", type=int, default=9000)
    ap.add_argument("--texture", type=int, default=1024)
    args = ap.parse_args()

    doc, binary = read_glb(args.src)
    at = doc["meshes"][0]["primitives"][0]["attributes"]
    pos = accessor(doc, binary, at["POSITION"]).astype(np.float64)
    nrm = accessor(doc, binary, at["NORMAL"]).astype(np.float32)
    uv = accessor(doc, binary, at["TEXCOORD_0"]).astype(np.float32)
    joints = accessor(doc, binary, at["JOINTS_0"])
    weights = accessor(doc, binary, at["WEIGHTS_0"])
    idx = accessor(doc, binary, doc["meshes"][0]["primitives"][0]["indices"]).astype(np.int64)

    new_pos, new_uv, new_tri = decimate(pos, idx, uv, args.tris)
    _, nearest = cKDTree(pos).query(new_pos)
    new_nrm = nrm[nearest]
    new_joints, new_weights = joints[nearest], weights[nearest]
    if len(new_pos) > 65535:
        sys.exit("%d vertices: too many for 16-bit indices" % len(new_pos))

    view = doc["bufferViews"][doc["images"][0]["bufferView"]]
    png = Image.open(io.BytesIO(binary[view["byteOffset"]:view["byteOffset"] + view["byteLength"]])).convert("RGBA")
    if png.size[0] > args.texture:
        png = png.resize((args.texture, args.texture), Image.LANCZOS)
    buf = io.BytesIO()
    png.save(buf, "PNG")

    doc, out = build(doc, binary, new_pos.astype("<f4"), new_nrm, new_uv, new_tri, new_joints, new_weights, buf.getvalue())
    write_glb(args.dst, doc, out)
    print("wrote", args.dst, len(new_pos), "vertices,", len(new_tri) // 3, "triangles, texture", png.size[0])


if __name__ == "__main__":
    main()
