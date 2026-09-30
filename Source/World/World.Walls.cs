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

        int slots = (int)MathF.Floor(MathF.Tau * radius / WallPiece.StraightLength);
        if (slots < 8)
            return;
        float step = MathF.Tau / slots;
        float start = (float)(Rng.NextDouble() * MathF.Tau);
        Vector3 Ring(float angle) => Grounded(new Vector3(centre.X + MathF.Cos(angle) * radius, 0f, centre.Z + MathF.Sin(angle) * radius));

        // Each slot of the ring is wall, or a gap: water, a rock, the oak, another home or a crop in the way, or a path worn through it.
        var gap = new bool[slots];
        var blocked = new bool[slots];
        var wear = new float[slots];
        for (int i = 0; i < slots; i++)
        {
            float a0 = start + i * step, a1 = a0 + step, mid = a0 + step / 2f;
            Vector3 p = Ring(mid);
            blocked[i] = !Terrain.Contains(p, 3f) || IsBlocked(p, 1.3f) || IsBlocked(Ring(a0), 0.8f) || IsBlocked(Ring(a1), 0.8f) ||
                Shelters.Any(s => !s.IsCollapsed && GroundMover.HorizontalDistance(s.Position, p) < s.Radius + 1.3f) ||
                Crops.Any(c => GroundMover.HorizontalDistance(c.Position, p) < Crop.Radius + 1f);
            for (int k = 0; k <= 4; k++)
            {
                Vector3 q = Ring(a0 + step * k / 4f);
                wear[i] = MathF.Max(wear[i], TrailWearNear(q.X, q.Z));
            }
            gap[i] = blocked[i] || wear[i] >= PathWear;
        }

        int Next(int i) => (i + 1) % slots;
        int Prev(int i) => (i + slots - 1) % slots;
        if (gap.All(g => g) || gap.Count(g => g) > slots * 6 / 10)
            return; // Too much water and clutter for a wall to make sense.

        // At least two gates, so nobody is shut in: open the most-walked stretches of wall, well apart.
        int Gates() => Enumerable.Range(0, slots).Count(i => gap[i] && !gap[Prev(i)]);
        while (Gates() < 2)
        {
            int pick = -1;
            for (int i = 0; i < slots; i++)
            {
                if (gap[i] || (pick >= 0 && wear[i] <= wear[pick]))
                    continue;
                bool far = true;
                for (int j = 0; j < slots && far; j++)
                {
                    int between = Math.Min(Math.Abs(i - j), slots - Math.Abs(i - j));
                    if (gap[j] && between < slots / 4)
                        far = false;
                }
                if (far)
                    pick = i;
            }
            if (pick < 0)
                return;
            gap[pick] = true;
        }
        // A gate is at least two pieces wide.
        for (int i = 0; i < slots; i++)
        {
            if (gap[i] && !gap[Prev(i)] && !gap[Next(i)])
                gap[Next(i)] = true;
        }

        var pieces = new List<(float Order, WallPiece Piece)>();
        for (int i = 0; i < slots; i++)
        {
            float a0 = start + i * step, mid = a0 + step / 2f;
            if (!gap[i])
                pieces.Add((i + 0.5f, new WallPiece(Ring(mid), mid + MathF.PI / 2f, WallKind.Straight, group.Id, false)));
            if (gap[i] && !gap[Prev(i)])
                pieces.Add((i, new WallPiece(Ring(a0), a0 + MathF.PI / 2f, WallKind.EndA, group.Id, false))); // The wall's end, its tip into the gate.
            if (gap[i] && !gap[Next(i)])
            {
                float a1 = a0 + step;
                pieces.Add((i + 1f, new WallPiece(Ring(a1), a1 + MathF.PI / 2f + MathF.PI, WallKind.EndB, group.Id, false))); // The other end, facing back.
            }
        }
        foreach (var entry in pieces.OrderBy(p => p.Order))
            WallPieces.Add(entry.Piece);
        Game.AddEventLog($"[BUILD] {group.CapitalTitle} marked out a stone wall round its homes ({slots - gap.Count(g => g)} lengths, {Gates()} gates where the paths run)");
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
                WallModels.Draw(piece.Kind, piece.Position, piece.Yaw, Color.White);
            else
                Raylib.DrawCylinderEx(piece.Position, piece.Position + new Vector3(0f, 0.55f, 0f), 0.06f, 0.03f, 4, WallStakeColor);
        }
    }
}
