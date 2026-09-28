using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class Bramblekin
{
    /// <summary>A fisher sits this long on the shore per cast (less for the diligent — see <see cref="WorkPace"/>)…</summary>
    private const float FishingSeconds = 8f;

    /// <summary>…and fishes from a stretch of shore within this far (m) of home.</summary>
    private const float FishingReach = 20f;

    private static readonly Color RodColor = new(120, 95, 60, 255);
    private static readonly Color LineColor = new(230, 230, 230, 200);

    /// <summary>Where it has gone to fish.</summary>
    private Vector3? _fishingSpot;

    private float _fishingTimer;

    /// <summary>Odds a cast lands a minnow or tadpole: good in spring and autumn, poor in winter.</summary>
    private static float CatchChance(Season season) => season switch
    {
        Season.Spring => 0.55f,
        Season.Summer => 0.45f,
        Season.Autumn => 0.5f,
        _ => 0.15f,
    };

    /// <summary>
    /// A Gatherer that knows <see cref="Craft.Fishing"/>, with nothing
    /// ripe and no food in sight: walks to a stretch of shore near home and
    /// casts, again and again, until it lands something to carry to the
    /// stores. False if it can't fish (or there's no shore near home).
    /// </summary>
    private bool TryFishing(Shelter home, float deltaTime, World world)
    {
        if (!Knows(Craft.Fishing))
            return false;
        if (_fishingSpot is { } old && GroundMover.HorizontalDistance(old, home.Position) > FishingReach)
            _fishingSpot = null; // Moved house since: fish nearer the new one.
        _fishingSpot ??= world.RandomShoreSpot(home.Position, FishingReach);
        if (_fishingSpot is not { } spot)
            return false;

        SetState(BramblekinState.Fishing);
        if (GroundMover.HorizontalDistanceSquared(Position, spot) > 0.5f * 0.5f)
        {
            _fishingTimer = 0f;
            MoveTo(spot, WalkSpeed, deltaTime, world);
            return true;
        }

        _mover.Heading = TowardWater(spot);
        _fishingTimer += deltaTime * WorkPace;
        if (_fishingTimer < FishingSeconds)
            return true;
        _fishingTimer = 0f;
        if (_rng.NextDouble() >= CatchChance(world.CurrentSeason))
            return true;

        if (world.CatchFish(this) is { } fish)
        {
            _carried = fish;
            _fishingSpot = null; // Next time, maybe another spot.
        }
        return true;
    }

    /// <summary>The way from <paramref name="spot"/> to the nearest water.</summary>
    private static Vector2 TowardWater(Vector3 spot)
    {
        for (int i = 0; i < 16; i++)
        {
            float angle = i * MathF.Tau / 16f;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            if (World.IsWater(spot + new Vector3(direction.X, 0f, direction.Y) * 2f))
                return direction;
        }
        return Vector2.UnitX;
    }

    /// <summary>A rod held out over the water, its line dropping to the surface.</summary>
    private void DrawFishingRod(Vector2 facing)
    {
        var grip = Position + new Vector3(0f, BodyHeight * 0.6f, 0f);
        var tip = grip + new Vector3(facing.X * 0.9f, 0.45f, facing.Y * 0.9f);
        Raylib.DrawCylinderEx(grip, tip, 0.02f, 0.01f, 4, RodColor);
        var bob = new Vector3(tip.X + facing.X * 0.2f, World.PondLevel + 0.02f, tip.Z + facing.Y * 0.2f);
        Raylib.DrawLine3D(tip, bob, LineColor);
        Raylib.DrawSphere(bob, 0.04f, new Color(220, 60, 50, 255));
    }
}
