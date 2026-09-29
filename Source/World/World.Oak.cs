using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>
    /// The Giant Oak: a hollow dead oak on the pond's back bank, part of the
    /// terrain model (see <see cref="Terrain"/>) — its trunk, far wider than
    /// a Bramblekin village, is what makes the garden a small world. Its
    /// roots sprawl over the ground (walkers steer round them: see
    /// <see cref="OakFootprint"/>), its shade darkens the lawn around it,
    /// and in autumn it drops acorns: food for whoever gathers them. Where
    /// it stands and how wide it is are measured off the model (TerrainData).
    /// </summary>
    public static readonly Vector3 OakCenter = Grounded(new Vector3(TerrainData.OakX, 0f, TerrainData.OakZ));

    /// <summary>The trunk's radius (a solid obstacle).</summary>
    public const float OakRadius = TerrainData.OakTrunkRadius;

    /// <summary>In autumn an acorn falls this often (s)…</summary>
    private const float AcornDropInterval = 4f;

    /// <summary>…until this many lie on the ground.</summary>
    private const int MaxAcornsOnGround = 12;

    /// <summary>Acorns land between the trunk and this far from it.</summary>
    private const float AcornFallReach = 16f;

    private static readonly Color OakShadeColor = new(10, 30, 10, 45);

    /// <summary>
    /// The oak's footprint on the ground, as circles walkers can't enter:
    /// the trunk and every root, covered by circles measured off the model.
    /// </summary>
    public static readonly (Vector2 Center, float Radius)[] OakFootprint = MakeOakFootprint();

    private float _acornTimer;

    public int AcornsFallen { get; private set; }

    private static (Vector2, float)[] MakeOakFootprint()
    {
        float[] c = TerrainData.OakCircles;
        var circles = new (Vector2, float)[c.Length / 3];
        for (int i = 0; i < circles.Length; i++)
            circles[i] = (new Vector2(c[i * 3], c[i * 3 + 1]), c[i * 3 + 2]);
        return circles;
    }

    /// <summary>The oak's trunk and roots are solid: added to the obstacles every walker steers round.</summary>
    private void AddOakObstacle()
    {
        foreach (var (center, radius) in OakFootprint)
            _obstacles.Add(new Obstacle(center, radius));
    }

    /// <summary>True if <paramref name="point"/> is within <paramref name="margin"/> of the trunk or a root.</summary>
    public static bool IsOnOak(Vector3 point, float margin)
    {
        var p = new Vector2(point.X, point.Z);
        foreach (var (center, radius) in OakFootprint)
        {
            if (Vector2.DistanceSquared(p, center) < (radius + margin) * (radius + margin))
                return true;
        }
        return false;
    }

    /// <summary>
    /// A loaded garden from before the oak grew (or its roots did): whatever
    /// stood where the trunk and roots are now — homes, props, crops — is
    /// cleared away (a home's store spills round the trunk).
    /// </summary>
    private void ClearOakGround()
    {
        static bool Inside(Vector3 p, float margin) => IsOnOak(p, margin);
        GardenProps.RemoveAll(p => Inside(p.Position, 0.5f));
        Crops.RemoveAll(b => Inside(b.Position, Crop.Radius));
        for (int i = Shelters.Count - 1; i >= 0; i--)
        {
            Shelter shelter = Shelters[i];
            if (!Inside(shelter.Position, shelter.Radius))
                continue;
            int spilled = shelter.Collapse();
            if (spilled > 0)
                ScatterFoodAround(OakCenter, spilled, OakRadius + 1.5f, FoodShardKind.Berry);
            Shelters.RemoveAt(i);
        }
    }

    /// <summary>Autumn: acorns drop around the trunk, now and then, onto open ground.</summary>
    private void UpdateOak(float deltaTime)
    {
        if (CurrentSeason != Season.Autumn)
            return;
        _acornTimer -= deltaTime;
        if (_acornTimer > 0f)
            return;
        _acornTimer = AcornDropInterval;

        int onGround = 0;
        foreach (FoodShard food in FoodShards)
        {
            if (food is { IsActive: true, IsCarried: false, Kind: FoodShardKind.Acorn })
                onGround++;
        }
        onGround += _pendingFoodSpawns.Count(f => f.Kind == FoodShardKind.Acorn);
        if (onGround >= MaxAcornsOnGround)
            return;

        float angle = (float)(Rng.NextDouble() * MathF.Tau);
        float distance = OakRadius + 1f + (float)Rng.NextDouble() * (AcornFallReach - OakRadius);
        Vector3 spot = OakCenter + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * distance;
        if (!Terrain.Contains(spot, 1f) || IsBlocked(spot, FoodShard.Radius + 0.1f))
            return;
        _pendingFoodSpawns.Add((spot, FoodShardKind.Acorn));
        AcornsFallen++;
    }

    /// <summary>The shade the oak casts on the lawn (the trunk and roots themselves are part of the terrain model).</summary>
    private void DrawOak()
    {
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        Rlgl.DisableBackfaceCulling();
        DrawTerrainBand(OakCenter, OakRadius, OakRadius + 22f, OakShadeColor);
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableBackfaceCulling();
        Rlgl.EnableDepthMask();
    }
}
