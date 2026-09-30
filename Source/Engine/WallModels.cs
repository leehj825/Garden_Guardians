using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// The stone wall's three pieces (<c>Assets/Models/Props/Walls.glb</c>, split from the Tripo sheet by Tools/convert_walls.py): one glb, three
/// meshes sharing one texture, in metres and standing on y = 0. Loaded the first time one is drawn (that needs a GPU context); the game
/// poses them itself (see <see cref="WallPiece"/>).
/// </summary>
public static unsafe class WallModels
{
    private static readonly string AssetPath = OperatingSystem.IsAndroid()
        ? "Models/Props/Walls.glb"
        : Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "Props", "Walls.glb");

    private static Model _model;
    private static bool _ready;

    private static void EnsureLoaded()
    {
        if (_ready)
            return;
        _model = Raylib.LoadModel(AssetPath);
        for (int m = 0; m < _model.MaterialCount; m++)
        {
            // A big painted picture: smoothed and mipmapped, it doesn't shimmer or show as pixels.
            Texture2D texture = _model.Materials[m].Maps[(int)MaterialMapIndex.Albedo].Texture;
            Raylib.GenTextureMipmaps(ref texture);
            Raylib.SetTextureFilter(texture, TextureFilter.Trilinear);
            _model.Materials[m].Maps[(int)MaterialMapIndex.Albedo].Texture = texture;
        }
        _ready = true;
    }

    /// <summary>Draws one piece standing at <paramref name="position"/>, its length running along <paramref name="yaw"/> (radians from +x towards +z).</summary>
    public static void Draw(WallKind kind, Vector3 position, float yaw, Color tint, float pitch = 0f, float stretch = 1f)
    {
        EnsureLoaded();
        int mesh = (int)kind;
        if (mesh >= _model.MeshCount)
            return;
        Raylib_cs.Material material = _model.Materials[_model.MeshMaterial[mesh]];
        material.Maps[(int)MaterialMapIndex.Albedo].Color = tint;
        Rlgl.PushMatrix();
        Rlgl.Translatef(position.X, position.Y, position.Z);
        Rlgl.Rotatef(-yaw * 180f / MathF.PI, 0f, 1f, 0f); // Raylib turns the other way about y than the x-z plane's angle runs.
        if (pitch != 0f)
            Rlgl.Rotatef(pitch * 180f / MathF.PI, 0f, 0f, 1f); // Leaning up or down the slope it runs along.
        if (stretch != 1f)
            Rlgl.Scalef(stretch, 1f, 1f);
        Raylib.DrawMesh(_model.Meshes[mesh], material, Matrix4x4.Identity);
        Rlgl.PopMatrix();
    }
}
