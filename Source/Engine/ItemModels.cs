using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// The village's small fittings (<c>Assets/Models/Props/Village/Items.glb</c>, split from a generated sheet by Tools/split_market_items.py):
/// the rune stone, the workbench, four market stalls and a basket. One file of seven meshes sharing a picture, each standing on y = 0 centred on its
/// own origin and already sized in metres (their long side runs along z). Loaded the first time one is drawn (that needs a GPU context).
/// </summary>
public static unsafe class ItemModels
{
    /// <summary>The meshes, in the file's order.</summary>
    public enum Kind { RuneStone, Workbench, StallA, StallB, StallC, StallD, Basket }

    private static readonly string Path_ = OperatingSystem.IsAndroid()
        ? "Models/Props/Village/Items.glb"
        : System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "Props", "Village", "Items.glb");

    private static Model _model;
    private static bool _ready;

    /// <summary>A market stall for the home numbered <paramref name="id"/>: the four look different.</summary>
    public static Kind StallFor(int id) => (Kind)((int)Kind.StallA + (id & 3));

    private static void EnsureLoaded()
    {
        if (_ready)
            return;
        _model = Raylib.LoadModel(Path_);
        for (int m = 0; m < _model.MaterialCount; m++)
        {
            Texture2D texture = _model.Materials[m].Maps[(int)MaterialMapIndex.Albedo].Texture;
            Raylib.GenTextureMipmaps(ref texture);
            Raylib.SetTextureFilter(texture, TextureFilter.Trilinear);
            _model.Materials[m].Maps[(int)MaterialMapIndex.Albedo].Texture = texture;
        }
        _ready = true;
    }

    /// <summary>Draws <paramref name="kind"/> standing on <paramref name="position"/>, turned <paramref name="yawDegrees"/> about the vertical, at <paramref name="scale"/> times its own size.</summary>
    public static void Draw(Kind kind, Vector3 position, float yawDegrees, float scale, Color tint)
    {
        EnsureLoaded();
        int mesh = (int)kind;
        if (mesh >= _model.MeshCount)
            return;
        Raylib_cs.Material material = _model.Materials[_model.MeshMaterial[mesh]];
        material.Maps[(int)MaterialMapIndex.Albedo].Color = tint;
        Rlgl.PushMatrix();
        Rlgl.Translatef(position.X, position.Y, position.Z);
        Rlgl.Rotatef(yawDegrees, 0f, 1f, 0f);
        Rlgl.Scalef(scale, scale, scale);
        Raylib.DrawMesh(_model.Meshes[mesh], material, Matrix4x4.Identity);
        Rlgl.PopMatrix();
    }
}
