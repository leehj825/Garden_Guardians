using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// The village asset sheet's items (<c>Assets/Models/Props/Village.glb</c>, cut
/// out and shrunk by Tools/convert_village.py): tents, burrows, the well and the
/// rest, one mesh each in <see cref="VillageItem"/> order, sharing one texture.
/// Each stands on y = 0 centred on the origin, facing +Z; <see cref="Draw"/>
/// scales it to a width in metres. Loaded the first time one is drawn (that
/// needs a GPU context).
/// </summary>
public static unsafe class VillageModels
{
    private static readonly string ModelPath = OperatingSystem.IsAndroid()
        ? "Models/Props/Village.glb"
        : Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "Props", "Village.glb");

    /// <summary>Each item has this many meshes: 0 full detail, 1 a quarter of the triangles, 2 a fourteenth (Tools/convert_village.py).</summary>
    private const int Lods = 3;

    /// <summary>From this many pixels across, the full mesh; from <see cref="MidPixels"/> the middle one; below that the cheapest.</summary>
    private const float FullPixels = 140f, MidPixels = 45f;

    private static Model _model;
    private static bool _ready;

    private static void EnsureLoaded()
    {
        if (_ready)
            return;
        _model = Raylib.LoadModel(ModelPath);
        if (_model.MeshCount != VillageItems.Sizes.Length * Lods)
            throw new InvalidOperationException($"Village.glb has {_model.MeshCount} meshes, expected {VillageItems.Sizes.Length * Lods}");
        _ready = true;
    }

    /// <summary>Every item is drawn this much larger than the width it is asked for.</summary>
    public const float Scale = 1.08f;

    /// <summary>The height (m) <paramref name="item"/> stands when drawn <paramref name="width"/> wide.</summary>
    public static float HeightAt(VillageItem item, float width) => VillageItems.Sizes[(int)item].Height * width * Scale / VillageItems.Sizes[(int)item].Width;

    /// <summary>
    /// Draws <paramref name="item"/> lying <paramref name="width"/> wide on the ground at <paramref name="position"/>,
    /// tilted to the slope under it (measured over the item's own width, so a big plot follows the hill rather than one bump).
    /// </summary>
    public static void DrawOnGround(VillageItem item, Vector3 position, float width, Color tint)
    {
        float reach = Math.Max(0.75f, width * Scale / 2f);
        float dx = World.GetHeightAt(position.X + reach, position.Z) - World.GetHeightAt(position.X - reach, position.Z);
        float dz = World.GetHeightAt(position.X, position.Z + reach) - World.GetHeightAt(position.X, position.Z - reach);
        Vector3 normal = Vector3.Normalize(new Vector3(-dx, 2f * reach, -dz));
        Vector3 axis = Vector3.Cross(Vector3.UnitY, normal);
        float angle = MathF.Acos(Math.Clamp(normal.Y, -1f, 1f)) * 180f / MathF.PI;
        Rlgl.PushMatrix();
        Rlgl.Translatef(position.X, position.Y, position.Z);
        if (axis.LengthSquared() > 1e-8f)
            Rlgl.Rotatef(angle, axis.X, axis.Y, axis.Z);
        Draw(item, Vector3.Zero, 0f, width, tint, 0f, position);
        Rlgl.PopMatrix();
    }

    /// <summary>Draws <paramref name="item"/> standing on <paramref name="position"/>, <paramref name="width"/> wide, turned <paramref name="yawDegrees"/> about the vertical (0 faces +Z, 90 faces +X) and leaning <paramref name="pitchDegrees"/> toward the way it faces.</summary>
    public static void Draw(VillageItem item, Vector3 position, float yawDegrees, float width, Color tint, float pitchDegrees = 0f, Vector3? lodAnchor = null)
    {
        EnsureLoaded();
        int i = (int)item;
        float scale = width * Scale / VillageItems.Sizes[i].Width;
        Raylib_cs.Material material = _model.Materials[_model.MeshMaterial[i * Lods]];
        material.Maps[(int)MaterialMapIndex.Albedo].Color = tint;
        Rlgl.PushMatrix();
        Rlgl.Translatef(position.X, position.Y, position.Z);
        Rlgl.Rotatef(yawDegrees, 0f, 1f, 0f);
        if (pitchDegrees != 0f)
            Rlgl.Rotatef(pitchDegrees, 1f, 0f, 0f); // leaning toward the way it faces
        Rlgl.Scalef(scale, scale, scale);
        float pixels = Detail.Pixels(lodAnchor ?? position, width * Scale);
        int lod = pixels >= FullPixels ? 0 : pixels >= MidPixels ? 1 : 2;
        Raylib.DrawMesh(_model.Meshes[i * Lods + lod], material, Matrix4x4.Identity);
        Rlgl.PopMatrix();
    }
}
