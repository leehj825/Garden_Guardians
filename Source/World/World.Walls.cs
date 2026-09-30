using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    // --- Stone walls ----------------------------------------------------------------------
    // A clan that reaches the Kingdom Age walls in its village: a ring of stone round all its homes, raised a piece at a time, with a gate
    // (an opening between two wall ends) wherever feet have worn a path through the line. Built, every piece is solid — an obstacle to
    // everything that walks, and blocked ground in the walkers' route grid (see WaterMap.SetWalls), so they find their way round the
    // wall, through the gates.

    /// <summary>The wall stands this far (m) outside its clan's outermost home…</summary>
    private const float WallMargin = 4.5f;

    /// <summary>…in a ring of at least this radius and at most that.</summary>
    private const float WallMinRadius = 8f, WallMaxRadius = 26f;

    /// <summary>A clan raises a piece this often (s)…</summary>
    private const float WallPieceSeconds = 8f;

    /// <summary>…if at least this many grown kin are there to do it.</summary>
    private const int WallBuilders = 3;

    /// <summary>Solid to walkers: circles this big (m), this far apart, along each piece.</summary>
    private const float WallObstacleRadius = 0.42f, WallObstacleSpacing = 0.8f;

    /// <summary>Route cells within this far (m) of a built wall count as blocked (a cell is a metre: this keeps the wall from being cut through at a corner).</summary>
    private const float WallRouteClearance = 0.75f;

    /// <summary>A gate is at least this many pieces wide (two pieces less the ends that cap it leave about 4 m).</summary>
    private const int WallMinGate = 2;

    /// <summary>Every piece of wall, planned or built.</summary>
    public List<WallPiece> WallPieces { get; } = new();

    /// <summary>Clans that have laid out their wall (so one that could not is not looked at again and again).</summary>
    private readonly HashSet<Guid> _wallsPlanned = new();

    private float _wallTimer;

    /// <summary>Pieces of wall raised so far.</summary>
    public int WallsRaised { get; private set; }

    private static readonly Color WallStakeColor = new(120, 90, 60, 255);

    /// <summary>
    /// At each Leader decision: a clan in the Kingdom Age, with a home built, that has not laid out a wall marks out the ring round its homes
    /// — stakes first, then the clan raises the pieces one by one (see <see cref="UpdateWallBuilding"/>).
    /// </summary>
    private void UpdateWalls(KinGroup group)
    {
        if (EraOf(group) < Era.KingdomAge || group.Home is not { IsBuilt: true, IsCollapsed: false } home || _wallsPlanned.Contains(group.Id))
            return;
        PlanWalls(group, home);
    }

    /// <summary>The most (wear in seconds of footfalls) that the path in the cell at (x, z) or any next to it shows: a road counts as a lot.</summary>
    private float TrailWearNear(float x, float z)
    {
        float most = 0f;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                int cell = TrailIndex(x + dx, z + dz);
                if (cell >= 0)
                    most = MathF.Max(most, _paved[cell] ? 100f : _wear[cell]);
            }
        }
        return most;
    }

    private void PlanWalls(KinGroup group, Shelter home)
    {
        _wallsPlanned.Add(group.Id);
        var homes = new List<Shelter> { home };
        homes.AddRange(group.Annexes.Where(a => a is { IsBuilt: true, IsCollapsed: false }));
        Vector3 centre = Vector3.Zero;
        foreach (Shelter h in homes)
            centre += h.Position;
        centre /= homes.Count;
        float radius = homes.Max(h => GroundMover.HorizontalDistance(h.Position, centre) + h.Radius) + WallMargin;
        radius = Math.Clamp(radius, WallMinRadius, WallMaxRadius);
        PlanWallRing(group, centre, radius);
    }

    /// <summary>True if a wall can't stand at <paramref name="p"/>: water, a rock, the oak, another home or a crop is in the way, or it is off the map.</summary>
    private bool WallSiteBlocked(Vector3 p) =>
        !Terrain.Contains(p, 3f) || IsBlocked(p, 1.3f) ||
        Shelters.Any(s => !s.IsCollapsed && GroundMover.HorizontalDistance(s.Position, p) < s.Radius + 1.3f) ||
        Crops.Any(c => GroundMover.HorizontalDistance(c.Position, p) < Crop.Radius + 1f);

    /// <summary>
    /// Lays out a wall round <paramref name="centre"/>: not a neat circle but an outline that wobbles with the clan's own whim and is
    /// pulled in where water, rocks, the oak or another home are in the way, broken (a natural barrier, or a gate) where feet have
    /// worn a path through it. Each run of wall between the breaks is laid as lengths stretched a little to meet their ends exactly
    /// (see <see cref="WallPiece.Scale"/>), with an end cap either side of each gap, and follows the lie of the land (see <see cref="DrawWalls"/>).
    /// </summary>
    private void PlanWallRing(KinGroup group, Vector3 centre, float radius)
    {
        const int Samples = 96;
        float inner = MathF.Max(3f, radius - WallMargin + 1.5f); // never closer to the homes' middle than this
        float phase1 = (float)(Rng.NextDouble() * MathF.Tau), phase2 = (float)(Rng.NextDouble() * MathF.Tau), phase3 = (float)(Rng.NextDouble() * MathF.Tau);
        Vector3 At(float angle, float r) => Grounded(new Vector3(centre.X + MathF.Cos(angle) * r, 0f, centre.Z + MathF.Sin(angle) * r));

        var points = new Vector3[Samples];
        var solid = new bool[Samples];
        var wear = new float[Samples];
        for (int i = 0; i < Samples; i++)
        {
            float angle = i * MathF.Tau / Samples;
            float wobble = 1f + 0.14f * MathF.Sin(2f * angle + phase1) + 0.08f * MathF.Sin(3f * angle + phase2) + 0.05f * MathF.Sin(5f * angle + phase3);
            for (float r = radius * wobble; r >= inner; r -= 0.8f)
            {
                Vector3 p = At(angle, r);
                if (WallSiteBlocked(p))
                    continue;
                points[i] = p;
                solid[i] = true;
                wear[i] = TrailWearNear(p.X, p.Z);
                break;
            }
        }

        // Breaks: nowhere to stand, a blocked stretch between two stands, or a worn path — each widened to a gate's width.
        float spacing = MathF.Tau * radius / Samples;
        int widen = Math.Max(1, (int)MathF.Ceiling(2.7f / spacing));
        int Wrap(int i) => ((i % Samples) + Samples) % Samples;
        var gap = new bool[Samples];
        var breaks = new bool[Samples];
        for (int i = 0; i < Samples; i++)
        {
            int next = Wrap(i + 1);
            breaks[i] = !solid[i] || wear[i] >= PathWear || (solid[next] && WallSiteBlocked((points[i] + points[next]) / 2f));
        }
        void Open(int at)
        {
            for (int k = -widen; k <= widen; k++)
                gap[Wrap(at + k)] = true;
        }
        for (int i = 0; i < Samples; i++)
        {
            if (breaks[i])
                Open(i);
        }
        if (gap.All(g => g) || gap.Count(g => g) > Samples * 6 / 10)
            return; // Too much water and clutter for a wall to make sense.

        // At least two gates, so nobody is shut in: open the most-walked stretches of wall, well apart.
        int Gates() => Enumerable.Range(0, Samples).Count(i => gap[i] && !gap[Wrap(i - 1)]);
        while (Gates() < 2)
        {
            int pick = -1;
            for (int i = 0; i < Samples; i++)
            {
                if (gap[i] || (pick >= 0 && wear[i] <= wear[pick]))
                    continue;
                bool far = true;
                for (int j = 0; j < Samples && far; j++)
                {
                    int between = Math.Min(Math.Abs(i - j), Samples - Math.Abs(i - j));
                    if (gap[j] && between < Samples / 4)
                        far = false;
                }
                if (far)
                    pick = i;
            }
            if (pick < 0)
                return;
            Open(pick);
        }

        // Each run of wall between two gaps: laid from its first point to its last.
        int laid = 0, runs = 0;
        int startAt = Enumerable.Range(0, Samples).First(i => gap[i]);
        for (int n = 0; n < Samples; n++)
        {
            int first = Wrap(startAt + n);
            if (gap[first] || !gap[Wrap(first - 1)])
                continue;
            var run = new List<Vector3>();
            for (int k = first; !gap[Wrap(k)]; k++)
                run.Add(points[Wrap(k)]);
            if (run.Count < 2)
                continue;
            var length = new float[run.Count];
            for (int k = 1; k < run.Count; k++)
                length[k] = length[k - 1] + GroundMover.HorizontalDistance(run[k - 1], run[k]);
            float total = length[^1];
            if (total < WallPiece.StraightLength * 0.7f)
                continue;

            Vector3 Along(float distance)
            {
                int k = 1;
                while (k < run.Count - 1 && length[k] < distance)
                    k++;
                float t = (distance - length[k - 1]) / MathF.Max(length[k] - length[k - 1], 1e-4f);
                return Vector3.Lerp(run[k - 1], run[k], Math.Clamp(t, 0f, 1f));
            }
            static float Heading(Vector3 from, Vector3 to) => MathF.Atan2(to.Z - from.Z, to.X - from.X);

            runs++;
            WallPieces.Add(new WallPiece(run[0], Heading(run[0], run[1]) + MathF.PI, WallKind.EndB, group.Id, false)); // The start's cap, facing back into the gap.
            int count = Math.Max(1, (int)MathF.Round(total / WallPiece.StraightLength));
            for (int j = 0; j < count; j++)
            {
                Vector3 a = Along(total * j / count), b = Along(total * (j + 1) / count);
                float span = GroundMover.HorizontalDistance(a, b);
                WallPieces.Add(new WallPiece(Grounded((a + b) / 2f), Heading(a, b), WallKind.Straight, group.Id, false, span / WallPiece.StraightLength));
                laid++;
            }
            Vector3 end = run[^1];
            WallPieces.Add(new WallPiece(end, Heading(run[^2], end), WallKind.EndA, group.Id, false)); // The end's cap, facing on into the gap.
        }
        Game.AddEventLog($"[BUILD] {group.CapitalTitle} marked out a stone wall round its homes ({laid} lengths in {runs} runs, {Gates()} gates where the paths run)");
    }

    /// <summary>Each few seconds, every clan with a wall planned and enough grown kin raises its next piece.</summary>
    private void UpdateWallBuilding(float deltaTime)
    {
        if (WallPieces.Count == 0)
            return;
        _wallTimer += deltaTime;
        if (_wallTimer < WallPieceSeconds)
            return;
        _wallTimer = 0f;

        bool changed = false;
        foreach (Guid id in WallPieces.Where(p => !p.IsBuilt && p.GroupId is not null).Select(p => p.GroupId!.Value).Distinct().ToList())
        {
            if (!_groups.TryGetValue(id, out KinGroup? group) || group.Members.Count(m => !m.IsDead && !m.IsYoung) < WallBuilders)
                continue;
            WallPiece? next = WallPieces.FirstOrDefault(p => p.GroupId == id && !p.IsBuilt);
            if (next is null)
                continue;
            bool first = !WallPieces.Any(p => p.GroupId == id && p.IsBuilt);
            next.IsBuilt = true;
            WallsRaised++;
            changed = true;
            if (first)
                Game.AddEventLog($"[BUILD] {group.CapitalTitle} began raising its stone wall");
            if (!WallPieces.Any(p => p.GroupId == id && !p.IsBuilt))
                Headline("A wall", $"{group.CapitalTitle} finished the stone wall round its homes, with gates where the paths run", next.Position, false, group);
        }
        if (changed)
            RebuildObstacles();
    }

    /// <summary>The walls' pieces as solid circles for <see cref="RebuildObstacles"/>, and as blocked ground for the walkers' routes.</summary>
    private void AddWallObstacles()
    {
        var segments = new List<(Vector2 A, Vector2 B)>();
        foreach (WallPiece piece in WallPieces)
        {
            if (!piece.IsBuilt)
                continue;
            foreach (Vector2 centre in piece.ObstacleCentres(WallObstacleSpacing))
                _obstacles.Add(new Obstacle(centre, WallObstacleRadius));
            segments.Add(piece.Segment);
        }
        WaterMap.SetWalls(segments, WallRouteClearance);
    }

    private void DrawWalls(Camera3D camera)
    {
        foreach (WallPiece piece in WallPieces)
        {
            if (!IsVisible(piece.Position, camera))
                continue;
            if (piece.IsBuilt)
            {
                // Along the lie of the land: leaning with the slope under it, and set a touch into the ground.
                (Vector2 from, Vector2 to) = piece.Segment;
                float rise = GetHeightAt(to.X, to.Y) - GetHeightAt(from.X, from.Y);
                float pitch = MathF.Atan2(rise, piece.Length);
                WallModels.Draw(piece.Kind, piece.Position + new Vector3(0f, -0.08f, 0f), piece.Yaw, Color.White, pitch, piece.Kind == WallKind.Straight ? piece.Scale : 1f);
            }
            else
                Raylib.DrawCylinderEx(piece.Position, piece.Position + new Vector3(0f, 0.55f, 0f), 0.06f, 0.03f, 4, WallStakeColor);
        }
    }
}
