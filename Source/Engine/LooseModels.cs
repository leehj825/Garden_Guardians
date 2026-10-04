using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// The small things lying about the garden (<c>Assets/Models/Props/Loose.glb</c>, cut from a Tripo sheet by Tools/split_sheets.py): the food (blackberry, meat,
/// acorn, seed, minnow, honeydew), the building material (twig, stone, thorny branch), the sling's pebble, a honeycomb and a woven sack. Sized in metres
/// (a twig 0.45 long, a branch 1.3, a stone 0.36); the twig, branch, minnow and honeycomb lie flat with their long side along +X, the minnow's head at +X.
/// </summary>
internal static class LooseModels
{
    /// <summary>The meshes, in the file's order. The honeycomb has no entity in the game yet; the sack is the village's food sack (see <see cref="VillageModels"/>), whose own file this one's is the source of.</summary>
    public enum Kind { Berry, Meat, Acorn, Seed, Fish, Honeydew, Twig, Stone, Branch, Pebble, Honeycomb, Sack }

    private static readonly MeshSet Set = new("Loose.glb");

    /// <summary>The height (m) of a stone, from the ground to its top: a stone's centre is half of it up.</summary>
    public const float StoneHeight = 0.257f;

    /// <summary>Draws <paramref name="kind"/> on <paramref name="position"/> (the ground under it), turned <paramref name="yawDegrees"/> about the vertical.</summary>
    public static void Draw(Kind kind, Vector3 position, float yawDegrees, float scale, Color tint) =>
        Set.Draw((int)kind, position, yawDegrees, new Vector3(scale), tint);

    /// <summary>The same, stretched along each axis by <paramref name="scale"/>.</summary>
    public static void Draw(Kind kind, Vector3 position, float yawDegrees, Vector3 scale, Color tint) =>
        Set.Draw((int)kind, position, yawDegrees, scale, tint);

    /// <summary>The yaw (degrees) that turns a model's +X to point along <paramref name="direction"/> on the ground (x, z).</summary>
    public static float YawAlong(Vector2 direction) => -MathF.Atan2(direction.Y, direction.X) * 180f / MathF.PI;

    /// <summary>Draws a long flat thing (<paramref name="kind"/>: a branch) from <paramref name="from"/> to <paramref name="to"/>: its middle halfway, its long side along the line (tipped up or down with it).</summary>
    public static void DrawBetween(Kind kind, Vector3 from, Vector3 to, Color tint)
    {
        Vector3 d = to - from;
        float flat = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
        float pitch = MathF.Atan2(d.Y, flat) * 180f / MathF.PI;
        Set.Draw((int)kind, (from + to) * 0.5f, YawAlong(new Vector2(d.X, d.Z)), Vector3.One, tint, pitch);
    }
}
