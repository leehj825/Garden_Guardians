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

    // --- Items with a model of their own (Assets/Models/Props/Village/<Item>.glb, from Tools/convert_plot.py): each is 1 unit across, centred, on y = 0 ---

    /// <summary>Each replaced item's height in units of its width; an item not listed still comes from the village sheet.</summary>
    private static readonly Dictionary<VillageItem, float> OwnHeights = new()
    {
        [VillageItem.AphidPen] = 0.366f, [VillageItem.Cistern] = 0.594f, [VillageItem.FoodSack] = 1.112f, [VillageItem.Palisade] = 0.348f,
        [VillageItem.Poultice] = 0.814f, [VillageItem.Shield] = 1.018f, [VillageItem.Shrine] = 0.937f, [VillageItem.StoneFooting] = 0.089f,
        [VillageItem.WaterCup] = 0.698f, [VillageItem.Well] = 0.874f, [VillageItem.ConstructionSite] = 0.559f, [VillageItem.Burrow] = 0.462f,
        [VillageItem.Hearth] = 0.353f, [VillageItem.Granary] = 1.081f,
    };

    private static readonly string OwnPath = OperatingSystem.IsAndroid()
        ? "Models/Props/Village/"
        : Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "Props", "Village") + Path.DirectorySeparatorChar;

    private static readonly Dictionary<VillageItem, (Model Full, Model Cheap)> _own = new();

    /// <summary>Below this many pixels across, an own-model item draws its cheaper mesh.</summary>
    private const float OwnCheapPixels = 60f;

    private static (Model Full, Model Cheap) OwnModels(VillageItem item)
    {
        if (_own.TryGetValue(item, out var cached))
            return cached;
        Model Load(string file)
        {
            Model model = Raylib.LoadModel(OwnPath + file);
            for (int m = 0; m < model.MaterialCount; m++)
            {
                Texture2D texture = model.Materials[m].Maps[(int)MaterialMapIndex.Albedo].Texture;
                Raylib.GenTextureMipmaps(ref texture);
                Raylib.SetTextureFilter(texture, TextureFilter.Trilinear);
                model.Materials[m].Maps[(int)MaterialMapIndex.Albedo].Texture = texture;
            }
            return model;
        }
        var pair = (Load(item + ".glb"), Load(item + "_lod.glb"));
        _own[item] = pair;
        return pair;
    }

    /// <summary>Every item is drawn this much larger than the width it is asked for.</summary>
    public const float Scale = 1.08f;

    /// <summary>The height (m) <paramref name="item"/> stands when drawn <paramref name="width"/> wide.</summary>
    public static float HeightAt(VillageItem item, float width) => OwnHeights.TryGetValue(item, out float own) ? own * width * Scale : VillageItems.Sizes[(int)item].Height * width * Scale / VillageItems.Sizes[(int)item].Width;

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
        if (OwnHeights.ContainsKey(item))
        {
            (Model full, Model cheap) = OwnModels(item);
            float across = width * Scale;
            Rlgl.PushMatrix();
            Rlgl.Translatef(position.X, position.Y, position.Z);
            Rlgl.Rotatef(yawDegrees, 0f, 1f, 0f);
            if (pitchDegrees != 0f)
                Rlgl.Rotatef(pitchDegrees, 1f, 0f, 0f);
            Model chosen = Detail.Pixels(lodAnchor ?? position, across) >= OwnCheapPixels ? full : cheap;
            Raylib.DrawModelEx(chosen, Vector3.Zero, Vector3.UnitY, 0f, new Vector3(across), tint);
            Rlgl.PopMatrix();
            return;
        }
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
