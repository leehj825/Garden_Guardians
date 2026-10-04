using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// The things a clan sets up round its home and its feasts (<c>Assets/Models/Props/Village/Fittings.glb</c>, cut from a Tripo sheet by Tools/split_sheets.py):
/// a feast lantern, a snare, the herb garden's bed, the sundial and the watchtower, in metres, facing +Z.
/// </summary>
internal static class FittingModels
{
    /// <summary>The meshes, in the file's order. The beehive and the aloe have no entity in the game yet.</summary>
    public enum Kind { Lantern, Snare, HerbBed, Sundial, Watchtower, Beehive, Aloe }

    private static readonly MeshSet Set = new("Village/Fittings.glb");

    /// <summary>Draws <paramref name="kind"/> standing on <paramref name="position"/>, turned <paramref name="yawDegrees"/> about the vertical, at <paramref name="scale"/> times its own size.</summary>
    public static void Draw(Kind kind, Vector3 position, float yawDegrees, float scale, Color tint) =>
        Set.Draw((int)kind, position, yawDegrees, new Vector3(scale), tint);

    /// <summary>The same, stretched along each axis by <paramref name="scale"/>.</summary>
    public static void Draw(Kind kind, Vector3 position, float yawDegrees, Vector3 scale, Color tint) =>
        Set.Draw((int)kind, position, yawDegrees, scale, tint);
}
