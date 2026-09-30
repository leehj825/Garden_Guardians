using System.Numerics;
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
    public const float HouseWidth = 0.82f, BushWidth = 0.81f, SpiderWidth = 1.0f, TentWidth = 1.117f;

    /// <summary>The acorn house's window (with the leaves) faces +Z, and it has firewood, a sack, a sword and shield round its foot; its cap's top is this fraction of its height (the stem rises above).</summary>
    public const float HouseCapTop = 0.80f;

    /// <summary>The props; each has a full model and a cheap one (a fifth or so of the triangles, a 512px texture) for when it is small on screen.</summary>
    public enum Prop { House, Bush, Spider, Tent }

    private static readonly string[] Files = { "AcornHouse", "BerryFarm", "Spider", "Tent" };

    /// <summary>Below this many pixels across, the cheap model.</summary>
    private const float FullPixels = 110f;

    private static readonly Model[] _full = new Model[4], _cheap = new Model[4];
    private static readonly bool[] _ready = new bool[4];

    private static void EnsureLoaded(Prop prop)
    {
        int i = (int)prop;
        if (_ready[i])
            return;
        _full[i] = Raylib.LoadModel(AssetPath + Files[i] + ".glb");
        _cheap[i] = prop == Prop.Spider ? _full[i] : Raylib.LoadModel(AssetPath + Files[i] + "_lod.glb"); // The spider is light already (and rigged): one model.
        if (prop is Prop.House or Prop.Tent)
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
        float pixels = Detail.Pixels(position, scale * (prop switch { Prop.House => HouseWidth, Prop.Bush => BushWidth, Prop.Tent => TentWidth, _ => SpiderWidth }));
        Model model = pixels >= FullPixels ? _full[(int)prop] : _cheap[(int)prop];
        Raylib.DrawModelEx(model, position, Vector3.UnitY, yawDegrees, new Vector3(scale), tint);
    }

    private static ModelAnimation _spiderWalk;
    private static bool _spiderWalkLoaded;

    /// <summary>
    /// The wolf spider, walking: <paramref name="cycle"/> is how far through its eight-legged walk it is (one loop per whole number), held on
    /// the first frame when it is not <paramref name="walking"/>. The clip is the model's own (Tools/convert_spider.py); there is one spider,
    /// so the shared model is posed and drawn in one go.
    /// </summary>
    public static void DrawSpider(Vector3 position, float yawDegrees, float scale, Color tint, float cycle, bool walking)
    {
        EnsureLoaded(Prop.Spider);
        if (!_spiderWalkLoaded)
        {
            Span<ModelAnimation> clips = Raylib.LoadModelAnimations(AssetPath + "Spider.glb");
            if (clips.Length == 0)
                throw new InvalidOperationException("No walk found in Spider.glb — is the Assets folder missing or not copied next to the app?");
            _spiderWalk = clips[0];
            _spiderWalkLoaded = true;
        }
        Model model = _full[(int)Prop.Spider];
        float through = walking ? cycle - MathF.Floor(cycle) : 0f;
        int frame = Math.Clamp((int)(through * _spiderWalk.KeyFrameCount), 0, Math.Max(_spiderWalk.KeyFrameCount - 1, 0));
        Raylib.UpdateModelAnimation(model, _spiderWalk, frame);
        Raylib.DrawModelEx(model, position, Vector3.UnitY, yawDegrees, new Vector3(scale), tint);
    }
}
