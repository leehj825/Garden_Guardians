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
    public const float ZoneRadius = 20f;

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

    /// <summary>How many guards it keeps: 5 × level.</summary>
    public int GuardCount => GuardsPerLevel * Level;

    /// <summary>How many thief ants it keeps out at once: 2, plus one per 15 food taken, up to 6.</summary>
    public int MaxThieves => Math.Min(6, 2 + Stock / 15);

    public bool IsInZone(Vector3 point, float margin = 0f) =>
        GroundMover.HorizontalDistanceSquared(point, Position) <= (ZoneRadius + margin) * (ZoneRadius + margin);

    public void Draw() => PropModels.Draw(PropModels.Prop.AntHill, Position, 0f, DrawWidth, Color.White);
}
