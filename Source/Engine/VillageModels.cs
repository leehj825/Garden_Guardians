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

    private static Model _model;
    private static bool _ready;

    private static void EnsureLoaded()
    {
        if (_ready)
            return;
        _model = Raylib.LoadModel(ModelPath);
        if (_model.MeshCount != VillageItems.Sizes.Length)
            throw new InvalidOperationException($"Village.glb has {_model.MeshCount} meshes, expected {VillageItems.Sizes.Length}");
        _ready = true;
    }

    /// <summary>Every item is drawn this much larger than the width it is asked for.</summary>
    public const float Scale = 1.35f;

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
        Draw(item, Vector3.Zero, 0f, width, tint);
        Rlgl.PopMatrix();
    }

    /// <summary>Draws <paramref name="item"/> standing on <paramref name="position"/>, <paramref name="width"/> wide, turned <paramref name="yawDegrees"/> about the vertical (0 faces +Z, 90 faces +X).</summary>
    public static void Draw(VillageItem item, Vector3 position, float yawDegrees, float width, Color tint)
    {
        EnsureLoaded();
        int i = (int)item;
        float scale = width * Scale / VillageItems.Sizes[i].Width;
        Raylib_cs.Material material = _model.Materials[_model.MeshMaterial[i]];
        material.Maps[(int)MaterialMapIndex.Albedo].Color = tint;
        Rlgl.PushMatrix();
        Rlgl.Translatef(position.X, position.Y, position.Z);
        Rlgl.Rotatef(yawDegrees, 0f, 1f, 0f);
        Rlgl.Scalef(scale, scale, scale);
        Raylib.DrawMesh(_model.Meshes[i], material, Matrix4x4.Identity);
        Rlgl.PopMatrix();
    }
}
