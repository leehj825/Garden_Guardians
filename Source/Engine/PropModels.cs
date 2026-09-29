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
public static class PropModels
{
    private static readonly string AssetPath = OperatingSystem.IsAndroid()
        ? "Models/Props/"
        : Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "Props") + Path.DirectorySeparatorChar;

    /// <summary>Each model is 1 unit tall, at the origin; this is how wide it is (m), for scaling it to a size.</summary>
    public const float HouseWidth = 0.77f, BushWidth = 0.81f, SpiderWidth = 1.0f;

    /// <summary>The acorn house's door faces −X and its window +Z; its cap's top is this fraction of its height (the stem rises above).</summary>
    public const float HouseCapTop = 0.86f;

    /// <summary>The three props; each has a full model and a cheap one (a fifth or so of the triangles, a 512px texture) for when it is small on screen.</summary>
    public enum Prop { House, Bush, Spider }

    private static readonly string[] Files = { "AcornHouse", "BerryFarm", "Spider" };

    /// <summary>Below this many pixels across, the cheap model.</summary>
    private const float FullPixels = 110f;

    private static readonly Model[] _full = new Model[3], _cheap = new Model[3];
    private static readonly bool[] _ready = new bool[3];

    private static void EnsureLoaded(Prop prop)
    {
        int i = (int)prop;
        if (_ready[i])
            return;
        _full[i] = Raylib.LoadModel(AssetPath + Files[i] + ".glb");
        _cheap[i] = Raylib.LoadModel(AssetPath + Files[i] + "_lod.glb");
        _ready[i] = true;
    }

    /// <summary>Draws <paramref name="prop"/> standing on <paramref name="position"/>, turned <paramref name="yawDegrees"/> about the vertical, in the model that suits its size on screen.</summary>
    public static void Draw(Prop prop, Vector3 position, float yawDegrees, float scale, Color tint)
    {
        EnsureLoaded(prop);
        float pixels = Detail.Pixels(position, scale * (prop switch { Prop.House => HouseWidth, Prop.Bush => BushWidth, _ => SpiderWidth }));
        Model model = pixels >= FullPixels ? _full[(int)prop] : _cheap[(int)prop];
        Raylib.DrawModelEx(model, position, Vector3.UnitY, yawDegrees, new Vector3(scale), tint);
    }
}
