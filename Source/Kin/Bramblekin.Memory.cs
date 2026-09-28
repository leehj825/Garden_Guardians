using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    /// <summary>A remembered danger keeps a Bramblekin away from within this many meters of it…</summary>
    public const float DangerRadius = 6f;

    /// <summary>…for this long after it last struck there.</summary>
    public const float DangerMemorySeconds = 300f;

    /// <summary>A food spot the group remembers stays worth a look this long.</summary>
    public const float FoodSpotMemorySeconds = 240f;

    /// <summary>Where it has been stung, bitten or pounced on — see <see cref="RememberDanger"/>.</summary>
    private readonly PlaceMemory _dangers = new(capacity: 4, mergeRadius: 4f);

    /// <summary>How many danger spots it remembers (for the Kin Inspector).</summary>
    public int DangersRemembered => _dangers.Count;

    /// <summary>Somewhere a Hornet stung it or the Spider bit it: it keeps away from there for a while — and so does its group (see <see cref="World.NoteDanger"/>).</summary>
    public void RememberDanger(Vector3 where, World world)
    {
        _dangers.Remember(where, world.ElapsedSeconds);
        world.NoteDanger(this, where);
    }

    /// <summary>True if it (or its group) remembers danger near <paramref name="point"/>.</summary>
    private bool IsDangerous(Vector3 point, World world) =>
        _dangers.IsNear(point, DangerRadius, world.ElapsedSeconds, DangerMemory) ||
        (world.GroupOf(this) is { } group && group.Dangers.IsNear(point, DangerRadius, world.ElapsedSeconds, DangerMemory));

    /// <summary>How long it gives a place of danger a wide berth: longer for the cautious, shorter for the brave.</summary>
    private float DangerMemory => DangerMemorySeconds * (1.5f - Personality.Courage);

    /// <summary>
    /// Food it could go for: remembered danger puts it off anything nearby
    /// — unless it's starving, when nothing is too risky.
    /// </summary>
    private bool WorthTheRisk(Vector3 foodPosition, World world) => IsStarving || !IsDangerous(foodPosition, world);

    /// <summary>Where to look for food when there's none in sight: where it last saw some, else the freshest spot its group knows (that isn't dangerous, and isn't right here).</summary>
    private Vector3? RememberedFoodSpot(World world)
    {
        if (_foodMemory is { } own)
            return own;
        if (world.GroupOf(this) is not { } group)
            return null;
        return group.FoodSpots.Freshest(world.ElapsedSeconds, FoodSpotMemorySeconds,
            spot => GroundMover.HorizontalDistanceSquared(spot, Position) > 9f && !IsDangerous(spot, world));
    }
}
