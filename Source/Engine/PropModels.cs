using System.Numerics;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// The 3D models of the acorn house, the berry bush and the wolf spider
/// (<c>Assets/Models/Props</c>, shrunk by Tools/convert_prop.py). Static
/// meshes, loaded the first time one is drawn (that needs a GPU context) and
/// shared by everything that draws them; the game poses them itself.
/// On Android the packaged asset path drops the "Assets/" prefix — see
/// <c>BramblekinModel.AssetPath</c> for why.
/// </summary>
public static unsafe class PropModels
{
    private static readonly string AssetPath = OperatingSystem.IsAndroid()
        ? "Models/Props/"
        : Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "Props") + Path.DirectorySeparatorChar;

    /// <summary>Each model is 1 unit tall, at the origin; this is how wide it is (m), for scaling it to a size.</summary>
    public const float HouseWidth = 0.82f, BushWidth = 0.795f, SpiderWidth = 1.0f, TentWidth = 1.117f;

    /// <summary>The acorn house's window (with the leaves) faces +Z, and it has firewood, a sack, a sword and shield round its foot; its cap's top is this fraction of its height (the stem rises above).</summary>
    public const float HouseCapTop = 0.72f;

    /// <summary>The props; each has a full model and a cheap one (a fifth or so of the triangles, a 512px texture) for when it is small on screen.</summary>
    public enum Prop { House, Bush, Spider, Tent, BerryPlot, CressPlot, MushroomPlot, GrainPlot, Beetle }

    private static readonly string[] Files = { "AcornHouse", "BerryFarm", "Spider", "Tent", "BerryPlot", "CressPlot", "MushroomPlot", "GrainPlot", "Beetle" };

    /// <summary>Below this many pixels across, the cheap model.</summary>
    private const float FullPixels = 110f;

    private static readonly Model[] _full = new Model[9], _cheap = new Model[9];
    private static readonly bool[] _ready = new bool[9];

    private static void EnsureLoaded(Prop prop)
    {
        int i = (int)prop;
        if (_ready[i])
            return;
        _full[i] = Raylib.LoadModel(AssetPath + Files[i] + ".glb");
        _cheap[i] = prop is Prop.Spider or Prop.Beetle ? _full[i] : Raylib.LoadModel(AssetPath + Files[i] + "_lod.glb"); // The spider is light already (and rigged): one model.
        if (prop is Prop.House or Prop.Tent or Prop.BerryPlot or Prop.CressPlot or Prop.MushroomPlot or Prop.GrainPlot)
        {
            // The house's pictures are big and painterly: smoothed and mipmapped, they don't shimmer or show as pixels.
            Smooth(_full[i]);
            Smooth(_cheap[i]);
        }
        _ready[i] = true;
    }

    private static void Smooth(Model model)
    {
        for (int m = 0; m < model.MaterialCount; m++)
        {
            Texture2D texture = model.Materials[m].Maps[(int)MaterialMapIndex.Albedo].Texture;
            Raylib.GenTextureMipmaps(ref texture);
            Raylib.SetTextureFilter(texture, TextureFilter.Trilinear);
            model.Materials[m].Maps[(int)MaterialMapIndex.Albedo].Texture = texture;
        }
    }

    /// <summary>Draws <paramref name="prop"/> standing on <paramref name="position"/>, turned <paramref name="yawDegrees"/> about the vertical, in the model that suits its size on screen.</summary>
    public static void Draw(Prop prop, Vector3 position, float yawDegrees, float scale, Color tint)
    {
        EnsureLoaded(prop);
        float pixels = Detail.Pixels(position, scale * (prop switch { Prop.House => HouseWidth, Prop.Bush => BushWidth, Prop.Tent => TentWidth, Prop.Spider => SpiderWidth, _ => 1f }));
        Model model = pixels >= FullPixels ? _full[(int)prop] : _cheap[(int)prop];
        Raylib.DrawModelEx(model, position, Vector3.UnitY, yawDegrees, new Vector3(scale), tint);
    }

    /// <summary>
    /// Draws a farm plot lying <paramref name="width"/> wide on the ground at <paramref name="position"/>, tilted to the slope under it
    /// (measured over the plot's own width, so a big plot follows the hill rather than one bump). The plot models are 1 unit across.
    /// </summary>
    public static void DrawOnGround(Prop plot, Vector3 position, float width, Color tint)
    {
        float reach = Math.Max(0.75f, width / 2f);
        float dx = World.GetHeightAt(position.X + reach, position.Z) - World.GetHeightAt(position.X - reach, position.Z);
        float dz = World.GetHeightAt(position.X, position.Z + reach) - World.GetHeightAt(position.X, position.Z - reach);
        Vector3 normal = Vector3.Normalize(new Vector3(-dx, 2f * reach, -dz));
        Vector3 axis = Vector3.Cross(Vector3.UnitY, normal);
        float angle = MathF.Acos(Math.Clamp(normal.Y, -1f, 1f)) * 180f / MathF.PI;
        Rlgl.PushMatrix();
        Rlgl.Translatef(position.X, position.Y, position.Z);
        if (axis.LengthSquared() > 1e-8f)
            Rlgl.Rotatef(angle, axis.X, axis.Y, axis.Z);
        Rlgl.DisableBackfaceCulling(); // Leaves and berries are single sheets: seen from the back (from above, or round the side) they must not vanish.
        Draw(plot, Vector3.Zero, 0f, width, tint);
        Rlgl.EnableBackfaceCulling();
        Rlgl.PopMatrix();
    }

    private static readonly Dictionary<Prop, ModelAnimation> _walks = new();
    private static readonly HashSet<Prop> _walkTried = new();

    /// <summary>The wolf spider, walking (see <see cref="DrawWalker"/>).</summary>
    public static void DrawSpider(Vector3 position, float yawDegrees, float scale, Color tint, float cycle, bool walking) =>
        DrawWalker(Prop.Spider, position, yawDegrees, scale, tint, cycle, walking);

    /// <summary>
    /// A rigged creature walking: <paramref name="cycle"/> is how far through its walk it is (one loop per whole number), held on the first
    /// frame when it is not <paramref name="walking"/>. The clip is the model's own (Tools/convert_spider.py, convert_beetle.py). The
    /// shared model is posed and drawn in one go, so any number of creatures can use it. A creature whose walk will not load stands still.
    /// </summary>
    public static void DrawWalker(Prop prop, Vector3 position, float yawDegrees, float scale, Color tint, float cycle, bool walking)
    {
        EnsureLoaded(prop);
        if (_walkTried.Add(prop))
        {
            Span<ModelAnimation> clips = Raylib.LoadModelAnimations(AssetPath + Files[(int)prop] + ".glb");
            if (clips.Length > 0)
                _walks[prop] = clips[0];
        }
        ref Model model = ref _full[(int)prop];
        if (_walks.TryGetValue(prop, out ModelAnimation walk) && model.BoneMatrices is null && model.Skeleton.BoneCount > 0)
        {
            // Posing needs its own bone buffers (see BramblekinModel.CreatePoseInstance): the loader does not make them.
            int bones = model.Skeleton.BoneCount;
            model.BoneMatrices = (Matrix4x4*)NativeMemory.AllocZeroed((nuint)bones, (nuint)sizeof(Matrix4x4));
            model.CurrentPose = (Transform*)NativeMemory.AllocZeroed((nuint)bones, (nuint)sizeof(Transform));
        }
        if (!_walks.ContainsKey(prop) || model.Skeleton.BoneCount == 0)
        {
            Raylib.DrawModelEx(model, position, Vector3.UnitY, yawDegrees, new Vector3(scale), tint);
            return;
        }
        float through = walking ? cycle - MathF.Floor(cycle) : 0f;
        int frame = Math.Clamp((int)(through * walk.KeyFrameCount), 0, Math.Max(walk.KeyFrameCount - 1, 0));
        Raylib.UpdateModelAnimation(model, walk, frame);
        Raylib.DrawModelEx(model, position, Vector3.UnitY, yawDegrees, new Vector3(scale), tint);
    }
}
