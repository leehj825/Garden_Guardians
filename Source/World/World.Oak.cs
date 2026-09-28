using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>
    /// The Giant Oak: the foot of a real tree at the garden's back edge —
    /// its trunk, far wider than a Bramblekin village, rises out of sight,
    /// which is what makes the garden a small world. Its roots sprawl over
    /// the ground, its shade darkens the lawn around it, and in autumn it
    /// drops acorns: food for whoever gathers them.
    /// </summary>
    public static readonly Vector3 OakCenter = Grounded(new Vector3(10f, 0f, -43f));

    /// <summary>The trunk's radius at the ground (a solid obstacle).</summary>
    public const float OakRadius = 6f;

    private const float OakHeight = 110f;

    /// <summary>In autumn an acorn falls this often (s)…</summary>
    private const float AcornDropInterval = 4f;

    /// <summary>…until this many lie on the ground.</summary>
    private const int MaxAcornsOnGround = 12;

    /// <summary>Acorns land between the trunk and this far from it.</summary>
    private const float AcornFallReach = 16f;

    private static readonly Color BarkColor = new(88, 70, 52, 255);
    private static readonly Color BarkRidgeColor = new(64, 50, 38, 255);
    private static readonly Color MossColor = new(84, 120, 60, 255);
    private static readonly Color OakShadeColor = new(10, 30, 10, 45);

    /// <summary>Where each root leaves the trunk, and where it dives into the ground — fixed, so the tree looks the same every time.</summary>
    private static readonly (Vector3 From, Vector3 To, float Thickness)[] OakRoots = MakeOakRoots();

    private float _acornTimer;

    public int AcornsFallen { get; private set; }

    private static (Vector3, Vector3, float)[] MakeOakRoots()
    {
        var roots = new List<(Vector3, Vector3, float)>();
        float[] angles = { 0.3f, 1.1f, 1.9f, 2.6f, 3.4f, 4.3f, 5.1f, 5.8f };
        for (int i = 0; i < angles.Length; i++)
        {
            var outward = new Vector3(MathF.Cos(angles[i]), 0f, MathF.Sin(angles[i]));
            float reach = OakRadius + 4f + (i % 3) * 2.5f;
            Vector3 from = OakCenter + outward * (OakRadius * 0.8f) + new Vector3(0f, 2.2f, 0f);
            Vector3 to = Grounded(OakCenter + outward * reach, -0.3f);
            roots.Add((from, to, 1.5f - (i % 3) * 0.25f));
        }
        return roots.ToArray();
    }

    /// <summary>The oak's trunk is solid: added to the obstacles every walker steers round.</summary>
    private void AddOakObstacle() =>
        _obstacles.Add(new Obstacle(new Vector2(OakCenter.X, OakCenter.Z), OakRadius));

    /// <summary>
    /// A loaded garden from before the oak grew: whatever stood where the
    /// trunk is now — homes, props, bushes — is cleared away (a home's
    /// store spills round the trunk).
    /// </summary>
    private void ClearOakGround()
    {
        bool Inside(Vector3 p, float margin) => GroundMover.HorizontalDistance(p, OakCenter) < OakRadius + margin;
        GardenProps.RemoveAll(p => Inside(p.Position, 0.5f));
        Bushes.RemoveAll(b => Inside(b.Position, BerryBush.Radius));
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

    /// <summary>The trunk rising out of sight, ridged bark and a mossy foot, its roots, and the shade it casts.</summary>
    private void DrawOak()
    {
        // Shade first, under everything else on the ground.
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        Rlgl.DisableBackfaceCulling();
        DrawTerrainBand(OakCenter, OakRadius, OakRadius + 22f, OakShadeColor);
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableBackfaceCulling();
        Rlgl.EnableDepthMask();

        Vector3 foot = OakCenter - new Vector3(0f, 1f, 0f);
        Vector3 top = OakCenter + new Vector3(0f, OakHeight, 0f);
        Raylib.DrawCylinderEx(foot, top, OakRadius, OakRadius * 0.8f, 36, BarkColor);

        // Deep ridges in the bark, running up the trunk.
        for (int i = 0; i < 18; i++)
        {
            float angle = i * MathF.Tau / 18f + 0.1f * (i % 3);
            var outward = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));
            Raylib.DrawCylinderEx(foot + outward * OakRadius, top + outward * OakRadius * 0.8f, 0.45f, 0.35f, 5, BarkRidgeColor);
        }

        // Moss round its foot, and the roots sprawling out and down into the ground.
        Raylib.DrawCylinderEx(foot, foot + new Vector3(0f, 3.5f, 0f), OakRadius + 0.3f, OakRadius + 0.05f, 36, MossColor);
        foreach (var (from, to, thickness) in OakRoots)
        {
            Vector3 middle = (from + to) / 2f + new Vector3(0f, 0.8f, 0f);
            Raylib.DrawCylinderEx(from, middle, thickness, thickness * 0.75f, 10, BarkColor);
            Raylib.DrawCylinderEx(middle, to, thickness * 0.75f, thickness * 0.35f, 10, BarkColor);
        }

        // One great bough, leaving the trunk high overhead and reaching back out of the garden.
        Vector3 boughBase = OakCenter + new Vector3(0f, 58f, 0f);
        Raylib.DrawCylinderEx(boughBase, boughBase + new Vector3(-28f, 26f, -24f), 3.2f, 1.6f, 14, BarkColor);
    }
}
