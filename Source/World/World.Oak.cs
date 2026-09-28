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
    private static readonly Color RootShadeColor = new(82, 65, 49, 255);
    private static readonly Color OakShadeColor = new(10, 30, 10, 45);

    /// <summary>One of the oak's great roots: which way it runs, how far from the trunk's centre it dives into the ground, and how thick it is at the trunk.</summary>
    public readonly record struct OakRoot(float Angle, float Reach, float Thickness);

    /// <summary>The roots — fixed, so the tree looks (and blocks) the same every time.</summary>
    public static readonly OakRoot[] OakRoots = MakeOakRoots();

    /// <summary>
    /// The oak's footprint on the ground, as circles walkers can't enter:
    /// the trunk, and each root from where it leaves the trunk to where it
    /// dives into the ground (see <see cref="RootPoint"/>).
    /// </summary>
    public static readonly (Vector2 Center, float Radius)[] OakFootprint = MakeOakFootprint();

    private float _acornTimer;

    public int AcornsFallen { get; private set; }

    private static OakRoot[] MakeOakRoots()
    {
        float[] angles = { 0.25f, 0.85f, 1.5f, 2.1f, 2.75f, 3.35f, 4.0f, 4.6f, 5.2f, 5.8f };
        var roots = new OakRoot[angles.Length];
        for (int i = 0; i < angles.Length; i++)
            roots[i] = new OakRoot(angles[i], OakRadius + 7f + (i % 3) * 3f, 2.1f - (i % 3) * 0.3f);
        return roots;
    }

    /// <summary>
    /// A point along <paramref name="root"/> — <paramref name="t"/> 0 where it
    /// leaves the trunk, high up its flank, arching down to 1 where it dives
    /// into the ground — and how thick the root is there.
    /// </summary>
    public static (Vector3 Center, float Thickness) RootPoint(OakRoot root, float t)
    {
        var outward = new Vector3(MathF.Cos(root.Angle), 0f, MathF.Sin(root.Angle));
        float start = OakRadius * 0.85f;
        float thickness = root.Thickness * (1f - 0.7f * t);
        Vector3 ground = Grounded(OakCenter + outward * (start + t * (root.Reach - start)));
        float lift = thickness * 0.55f + 3.5f * (1f - t) * (1f - t);
        return (ground + new Vector3(0f, lift, 0f), thickness);
    }

    private static (Vector2, float)[] MakeOakFootprint()
    {
        var circles = new List<(Vector2, float)> { (new Vector2(OakCenter.X, OakCenter.Z), OakRadius) };
        foreach (OakRoot root in OakRoots)
        {
            float length = root.Reach - OakRadius * 0.85f;
            int steps = (int)MathF.Ceiling(length / 0.6f);
            for (int i = 0; i <= steps; i++)
            {
                var (center, thickness) = RootPoint(root, i / (float)steps);
                circles.Add((new Vector2(center.X, center.Z), thickness));
            }
        }
        return circles.ToArray();
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

        // Moss round its foot, and the great roots arching out and down into the ground.
        Raylib.DrawCylinderEx(foot, foot + new Vector3(0f, 3.5f, 0f), OakRadius + 0.3f, OakRadius + 0.05f, 36, MossColor);
        foreach (OakRoot root in OakRoots)
        {
            const int segments = 7;
            var (previous, previousThickness) = RootPoint(root, 0f);
            for (int i = 1; i <= segments; i++)
            {
                var (next, thickness) = RootPoint(root, i / (float)segments);
                if (i == segments)
                    next -= new Vector3(0f, thickness * 0.8f, 0f); // Diving in.
                Raylib.DrawCylinderEx(previous, next, previousThickness, thickness, 10, i % 2 == 0 ? RootShadeColor : BarkColor);
                Raylib.DrawSphere(next, thickness * 0.98f, BarkColor);
                previous = next;
                previousThickness = thickness;
            }
        }

        // One great bough, leaving the trunk high overhead and reaching back out of the garden.
        Vector3 boughBase = OakCenter + new Vector3(0f, 58f, 0f);
        Raylib.DrawCylinderEx(boughBase, boughBase + new Vector3(-28f, 26f, -24f), 3.2f, 1.6f, 14, BarkColor);
    }
}
