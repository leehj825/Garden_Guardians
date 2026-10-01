using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// A well a clan far from water digs beside its main home (see
/// <see cref="Craft.Wells"/> and World.Wells): its Builders carry stones to
/// line the shaft — more of them the higher the ground, since the water lies
/// deeper under a hill — and once it's dug it's water at the door, all year
/// round, drought or no. Stone-ringed, with a wooden frame and an acorn-cup
/// bucket on a rope. A well outlives its clan: anyone may drink from it then.
/// </summary>
public sealed class Well
{
    /// <summary>Its ring of stones (m) — solid, like a rock.</summary>
    public const float Radius = 0.6f;

    /// <summary>How far the well's picture reaches (m): nothing else is set down closer than this.</summary>
    public const float DrawRadius = 0.9f;

    private static readonly Color StoneColor = new(140, 140, 146, 255);
    private static readonly Color DarkStoneColor = new(110, 110, 116, 255);
    private static readonly Color WaterColor = new(40, 70, 110, 255);
    private static readonly Color WoodColor = new(110, 80, 50, 255);
    private static readonly Color RopeColor = new(200, 180, 140, 255);
    private static readonly Color BucketColor = new(140, 100, 60, 255);
    private static readonly Color DirtColor = new(110, 85, 60, 255);
    private static readonly Color HoleColor = new(45, 32, 22, 255);

    public Well(Vector3 position, Guid? groupId, int stonesNeeded)
    {
        Position = World.Grounded(position);
        GroupId = groupId;
        StonesNeeded = stonesNeeded;
    }

    public Vector3 Position { get; }

    /// <summary>The clan that dug it — null once that clan is gone.</summary>
    public Guid? GroupId { get; set; }

    /// <summary>Stones it takes to line the shaft down to the water.</summary>
    public int StonesNeeded { get; }

    /// <summary>Stones laid so far.</summary>
    public int StonesLaid { get; set; }

    /// <summary>Under this many pixels across, a well is drawn as the coarse stand-in (see <see cref="Draw"/>).</summary>
    private const float FarPixels = 14f;

    /// <summary>The stand-in is grown to at least this many pixels across.</summary>
    private const float FarMinPixels = 10f;

    public bool IsDug => StonesLaid >= StonesNeeded;

    /// <summary>The ring of stones, rising course by course as they're laid (only as many as have been carried in); dug, dark water inside, a frame over it and a bucket on a rope. While it's being dug, a dark shaft and a heap of earth beside it.</summary>
    public void Draw(Color? clanColor)
    {
        // Zoomed out, a metre-wide well is a couple of pixels and disappears:
        // stand in a chunkier, coarser one that stays visible (a dark shaft
        // mound while it's dug, blue water in a stone ring once it's done).
        float onScreen = Detail.Pixels(Position, Radius * 2f);
        if (onScreen < FarPixels)
        {
            float grow = Math.Clamp(FarMinPixels / Math.Max(onScreen, 0.01f), 1f, 10f);
            float r = Radius * grow;
            Raylib.DrawCylinder(Position, r, r, 0.35f * grow, 6, StoneColor);
            Raylib.DrawCylinder(Position + new Vector3(0f, 0.36f * grow, 0f), r * 0.65f, r * 0.65f, 0.01f * grow, 6, IsDug ? WaterColor : HoleColor);
            return;
        }

        if (IsDug)
        {
            // The finished well: stone ring, wooden frame and bucket.
            float width = Radius * 2.4f;
            VillageModels.Draw(VillageItem.Well, Position, 0f, width, Color.White);
            if (clanColor is { } pennant)
                Raylib.DrawCube(Position + new Vector3(Radius * 0.9f + 0.1f, VillageModels.HeightAt(VillageItem.Well, width) + 0.05f, 0f), 0.16f, 0.1f, 0.02f, pennant);
            return;
        }

        const int perCourse = 10, courses = 3;
        int shown = IsDug ? perCourse * courses : perCourse * courses * StonesLaid / Math.Max(1, StonesNeeded);
        for (int course = 0; course < courses; course++)
        {
            for (int i = 0; i < perCourse && course * perCourse + i < shown; i++)
            {
                float angle = (i + 0.5f * (course % 2)) * MathF.Tau / perCourse;
                Vector3 stone = Position + new Vector3(MathF.Cos(angle) * Radius * 0.85f, 0.08f + course * 0.13f, MathF.Sin(angle) * Radius * 0.85f);
                Raylib.DrawSphereEx(stone, 0.12f, 4, 6, (i + course) % 3 == 0 ? DarkStoneColor : StoneColor);
            }
        }

        if (!IsDug)
        {
            // The shaft going down, and the earth dug out so far heaped beside it.
            Raylib.DrawCylinder(Position + new Vector3(0f, 0.02f, 0f), Radius * 0.72f, Radius * 0.72f, 0.02f, 12, HoleColor);
            Rlgl.PushMatrix();
            Rlgl.Translatef(Position.X + Radius + 0.45f, Position.Y, Position.Z);
            Rlgl.Scalef(1f, 0.45f, 1f);
            Detail.Sphere(Vector3.Zero, 0.35f, DirtColor, Position);
            Rlgl.PopMatrix();
            return;
        }
    }
}
