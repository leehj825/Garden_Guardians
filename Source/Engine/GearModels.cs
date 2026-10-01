using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// What Bramblekin carry (<c>Assets/Models/Props/Gear</c>, from Tools/convert_plot.py): a sword for soldiers and raiders, a spear and a bow for
/// hunters, and the hunters' quiver on the back. Each model lies along +X, centred, 1 unit long; <see cref="Draw"/> sets it in a bone's frame
/// (so it moves with the walk), scaled to a length in metres. Loaded the first time one is drawn (that needs a GPU context).
/// </summary>
public static unsafe class GearModels
{
    public enum Gear { Sword, Spear, Bow, Quiver, Shield }

    private static readonly string Folder = OperatingSystem.IsAndroid()
        ? "Models/Props/Gear/"
        : Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "Props", "Gear") + Path.DirectorySeparatorChar;

    private static readonly Model[] _models = new Model[5];
    private static readonly bool[] _ready = new bool[5];

    private static Model Get(Gear gear)
    {
        int i = (int)gear;
        if (!_ready[i])
        {
            _models[i] = Raylib.LoadModel(Folder + gear + ".glb");
            for (int m = 0; m < _models[i].MaterialCount; m++)
            {
                Texture2D texture = _models[i].Materials[m].Maps[(int)MaterialMapIndex.Albedo].Texture;
                Raylib.GenTextureMipmaps(ref texture);
                Raylib.SetTextureFilter(texture, TextureFilter.Trilinear);
                _models[i].Materials[m].Maps[(int)MaterialMapIndex.Albedo].Texture = texture;
            }
            _ready[i] = true;
        }
        return _models[i];
    }

    /// <summary>
    /// Draws <paramref name="gear"/> where <paramref name="world"/> puts it: a row-vector world matrix (the model lies along +X, 1 unit long, so the
    /// matrix's scale is its length in metres). Taken apart and handed to DrawModelEx, whose own conventions are known to be right.
    /// </summary>
    public static void Draw(Gear gear, Matrix4x4 world)
    {
        if (!Matrix4x4.Decompose(world, out Vector3 scale, out Quaternion rotation, out Vector3 translation))
            return;
        rotation = Quaternion.Normalize(rotation);
        float angle = 2f * MathF.Acos(Math.Clamp(rotation.W, -1f, 1f)) * (180f / MathF.PI);
        float sinHalf = MathF.Sqrt(Math.Max(0f, 1f - rotation.W * rotation.W));
        Vector3 axis = sinHalf > 1e-6f ? new Vector3(rotation.X, rotation.Y, rotation.Z) / sinHalf : Vector3.UnitY;
        Raylib.DrawModelEx(Get(gear), translation, axis, angle, scale, Color.White);
    }
}
