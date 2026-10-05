using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// Level of detail: how finely to draw something, by how big it looks on
/// screen right now. raylib builds every sphere's vertices on the CPU each
/// frame — its default is 16×16 segments, some 1,500 vertices — so a garden
/// full of berries, bushes and acorn houses costs far more to draw than it
/// shows at the whole-map view. Close up, everything is drawn as finely as
/// ever.
/// </summary>
public static class Detail
{
    private static Vector3 _eye;

    /// <summary>Screen pixels covered by one meter, one meter from the camera.</summary>
    private static float _pixelsPerMeter = 1000f;

    /// <summary>
    /// True while the camera is zoomed right out (a metre at the middle of the view is under <see cref="FarMeterPixels"/> pixels): the whole-garden
    /// view shows only what matters at that scale — the terrain, the water, the big stones and plants, the ant hill, each clan's main home and its
    /// range — and nothing small or moving (see World.Draw).
    /// </summary>
    public static bool FarView { get; private set; }

    private const float FarMeterPixels = 16f;

    /// <summary>Call once a frame, before drawing the world.</summary>
    public static void BeginFrame(Camera3D camera)
    {
        _eye = camera.Position;
        _pixelsPerMeter = Raylib.GetScreenHeight() / (2f * MathF.Tan(camera.FovY * MathF.PI / 360f));
        FarView = Pixels(camera.Target, 1f) < FarMeterPixels;
    }

    /// <summary>Roughly how many pixels across something of <paramref name="radius"/> at <paramref name="at"/> looks.</summary>
    public static float Pixels(Vector3 at, float radius) =>
        radius * _pixelsPerMeter / MathF.Max(0.5f, Vector3.Distance(_eye, at));

    /// <summary>A sphere, with as many segments as its size on screen calls for (at most raylib's usual 16×16).</summary>
    public static void Sphere(Vector3 center, float radius, Color color) => Sphere(center, radius, color, center);

    /// <summary>A sphere drawn in some thing's own frame (inside a PushMatrix): sized by <paramref name="anchor"/>, that thing's place in the world.</summary>
    public static void Sphere(Vector3 center, float radius, Color color, Vector3 anchor)
    {
        float pixels = Pixels(anchor, radius);
        (int rings, int slices) = pixels < 2f ? (3, 4)
            : pixels < 5f ? (4, 6)
            : pixels < 12f ? (6, 10)
            : pixels < 30f ? (10, 14)
            : (16, 16);
        Raylib.DrawSphereEx(center, radius, rings, slices, color);
    }
}
