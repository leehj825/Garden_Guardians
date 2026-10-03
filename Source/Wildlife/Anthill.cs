using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// The ant colony's hill: a big mound fixed in a corner of the garden from the first day (see World.AntHill). It sends single thief ants out
/// after the Bramblekin's stores from spring to autumn, and is guarded by <see cref="GuardsPerLevel"/> × its <see cref="Level"/> ants inside a
/// no-go zone that only a Kingdom's assault enters. It never dies: each assault that wipes out its guards and brings its prize home makes it a
/// level tougher.
/// </summary>
public sealed class Anthill
{
    /// <summary>The solid mound's radius (m): walkers steer round it.</summary>
    public const float Radius = 4.4f;

    /// <summary>The model is drawn this wide (m): 1 unit across in the glb.</summary>
    public const float DrawWidth = 10f;

    /// <summary>The no-go zone: nothing is built, planted or settled within this far (m) of the hill's middle, and its guards fight whoever comes in.</summary>
    public const float ZoneRadius = 12f;

    /// <summary>Bramblekin this much (m) beyond the zone's edge put the hill on alert: all its guards come out (but attack only in the zone).</summary>
    public const float AlertMargin = 6f;

    /// <summary>Only this many guards roam about the hill when all is quiet; the rest stay inside until it is on alert.</summary>
    public const int Sentries = 2;

    /// <summary>Guards give up the chase of someone who has got this far (m) outside the zone.</summary>
    public const float ChaseLeash = 6f;

    /// <summary>Thief ants rob stores within this many meters of it…</summary>
    public const float ForageRadius = 70f;

    /// <summary>…and pick up loose food only this close to it.</summary>
    public const float GleanRadius = 30f;

    /// <summary>Guards at level 1; each level adds this many again.</summary>
    public const int GuardsPerLevel = 5;

    public Anthill(Vector3 position, Vector3 mapCentre)
    {
        Position = World.Grounded(position);
        var inward = new Vector3(mapCentre.X - position.X, 0f, mapCentre.Z - position.Z);
        Facing = inward.LengthSquared() > 1e-6f ? Vector3.Normalize(inward) : Vector3.UnitX;
    }

    public Vector3 Position { get; }

    /// <summary>The way its mouth faces: into the garden, away from the corner.</summary>
    public Vector3 Facing { get; }

    /// <summary>The foot of the mound on its garden side, where ants come and go.</summary>
    public Vector3 Mouth => World.Grounded(Position + Facing * (Radius + 0.8f));

    /// <summary>Food its thief ants have brought home.</summary>
    public int Stock { get; set; }

    /// <summary>The hill's level (1 to start): it never goes down, and goes up when an assault on it brings its prize home.</summary>
    public int Level { get; set; } = 1;

    /// <summary>Assaults on this level that failed since the last win: each makes the Kingdom send half as many soldiers again next time.</summary>
    public int Failures { get; set; }

    /// <summary>How many guards it keeps: 5 × level.</summary>
    public int GuardCount => GuardsPerLevel * Level;

    /// <summary>How many thief ants it keeps out at once: 2, plus one per 15 food taken, up to 6.</summary>
    public int MaxThieves => Math.Min(6, 2 + Stock / 15);

    /// <summary>The way in (distance from the middle, height; m): from the foot of the mound on its garden side, through the cave entrance, to where the eggs lie just inside.</summary>
    private static readonly (float R, float Y)[] Profile =
    {
        (Radius + 0.8f, 0f), (4.6f, 0.1f), (4.1f, 0.3f), (3.7f, 0.55f), (3.3f, 0.7f),
    };

    /// <summary>
    /// Where a climber is, <paramref name="t"/> of the way (0 at the <see cref="Mouth"/>, 1 at the top, by the crater's rim) up the mound's garden
    /// side, as an offset from the mouth: in towards the middle, and up.
    /// </summary>
    public Vector3 ClimbOffset(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        float r = Profile[0].R + (Profile[^1].R - Profile[0].R) * t;
        float y = Profile[^1].Y;
        for (int i = 1; i < Profile.Length; i++)
        {
            if (r >= Profile[i].R)
            {
                float u = (r - Profile[i].R) / (Profile[i - 1].R - Profile[i].R);
                y = Profile[i].Y + (Profile[i - 1].Y - Profile[i].Y) * u;
                break;
            }
        }
        return -Facing * (Profile[0].R - r) + new Vector3(0f, y, 0f);
    }

    /// <summary>Where the eggs lie: just inside the cave entrance, which faces <see cref="Facing"/>.</summary>
    public Vector3 Top => Position + Facing * Profile[^1].R + new Vector3(0f, Profile[^1].Y, 0f);

    /// <summary>The model's cave entrance lies this way from its middle (x and z, from the model), 3.9 m out.</summary>
    private const float EntranceX = -3.8f, EntranceZ = 0.5f;

    /// <summary>How far the model is turned (degrees about the vertical) so that its cave entrance faces <see cref="Facing"/>.</summary>
    public float DrawYaw => (MathF.Atan2(EntranceZ, EntranceX) - MathF.Atan2(Facing.Z, Facing.X)) * 180f / MathF.PI;

    public bool IsInZone(Vector3 point, float margin = 0f) =>
        GroundMover.HorizontalDistanceSquared(point, Position) <= (ZoneRadius + margin) * (ZoneRadius + margin);

    public void Draw() => PropModels.Draw(PropModels.Prop.AntHill, Position, DrawYaw, DrawWidth, Color.White);
}
