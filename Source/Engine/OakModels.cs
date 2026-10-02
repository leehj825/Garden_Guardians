using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// The Giant Oak of the original garden, drawn from its own models (<c>Assets/Models/Props/Oak.glb</c>, about 19,000 triangles with a 2048 px picture, and
/// <c>Oak_lod.glb</c>, about 900 triangles with a 1024 px one: the same oak, the cheap one stretched to the full one's width) in place of the part of the ground
/// model it used to be (see Tools/cut_oak_from_terrain.py). The full one is drawn when the camera is near, the cheap one when it is far.
/// Each stands on y = 0 with its origin at the middle of its base, already 19.7 m tall.
/// </summary>
public static unsafe class OakModels
{
    private static readonly string AssetPath = OperatingSystem.IsAndroid()
        ? "Models/Props/"
        : System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "Props") + System.IO.Path.DirectorySeparatorChar;

    /// <summary>The full model is drawn while the camera is within this far (m) of the trunk, the cheap one beyond.</summary>
    private const float FullWithin = 75f;

    /// <summary>The oak reaches this far (m) from its origin, for culling it when it is out of sight.</summary>
    private const float Reach = 25f, Height = 19.7f;

    private static Model _full, _cheap;
    private static bool _ready;

    private static void EnsureLoaded()
    {
        if (_ready)
            return;
        _full = Raylib.LoadModel(AssetPath + "Oak.glb");
        _cheap = Raylib.LoadModel(AssetPath + "Oak_lod.glb");
        foreach (Model model in new[] { _full, _cheap })
        {
            for (int m = 0; m < model.MaterialCount; m++)
            {
                Texture2D texture = model.Materials[m].Maps[(int)MaterialMapIndex.Albedo].Texture;
                Raylib.GenTextureMipmaps(ref texture);
                Raylib.SetTextureFilter(texture, TextureFilter.Trilinear);
                model.Materials[m].Maps[(int)MaterialMapIndex.Albedo].Texture = texture;
            }
        }
        _ready = true;
    }

    /// <summary>Draws the oak with its origin at (<paramref name="x"/>, <paramref name="baseY"/>, <paramref name="z"/>), the season's tint over it.</summary>
    public static void Draw(float x, float baseY, float z, Color tint)
    {
        Vector3 middle = new(x, baseY + Height * 0.4f, z);
        Vector3 eye = ProceduralView.Eye, focus = ProceduralView.Focus;
        float distance = Vector3.Distance(eye, middle);
        if (distance > Reach)
        {
            // Not drawn while it is wholly behind the camera.
            Vector3 forward = Vector3.Normalize(focus - eye);
            if (Vector3.Dot(middle - eye, forward) < -Reach)
                return;
        }
        EnsureLoaded();
        Model model = distance < FullWithin ? _full : _cheap;
        Rlgl.PushMatrix();
        Rlgl.Translatef(x, baseY, z);
        for (int m = 0; m < model.MeshCount; m++)
        {
            Raylib_cs.Material material = model.Materials[model.MeshMaterial[m]];
            material.Maps[(int)MaterialMapIndex.Albedo].Color = tint;
            Raylib.DrawMesh(model.Meshes[m], material, Matrix4x4.Identity);
        }
        Rlgl.PopMatrix();
    }
}
