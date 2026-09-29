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

    private static Model _house, _bush, _spider;
    private static bool _houseReady, _bushReady, _spiderReady;

    public static Model House => Load(ref _house, ref _houseReady, "AcornHouse.glb");

    public static Model Bush => Load(ref _bush, ref _bushReady, "BerryFarm.glb");

    public static Model Spider => Load(ref _spider, ref _spiderReady, "Spider.glb");

    private static Model Load(ref Model model, ref bool ready, string file)
    {
        if (!ready)
        {
            model = Raylib.LoadModel(AssetPath + file);
            ready = true;
        }
        return model;
    }

    /// <summary>Draws <paramref name="model"/> standing on <paramref name="position"/>, turned <paramref name="yawDegrees"/> about the vertical.</summary>
    public static void Draw(Model model, Vector3 position, float yawDegrees, float scale, Color tint) =>
        Raylib.DrawModelEx(model, position, Vector3.UnitY, yawDegrees, new Vector3(scale), tint);
}
