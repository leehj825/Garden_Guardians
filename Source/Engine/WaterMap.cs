using System.Numerics;

namespace GardenGuardians;

/// <summary>
/// Walking round the water: nothing that walks may set foot in the pond,
/// or climb the Giant Oak's trunk and roots, so a walker whose straight way
/// is cut by them finds a way round — A* over a 1m grid of the garden,
/// eight ways, pulled tight into a few waypoints (see <see cref="FindRoute"/>).
/// The pond's level drops in a drought (see World.Drought), so every grid is
/// worked out once for each of <see cref="Levels"/> water levels, from its
/// usual one down to the dregs, and <see cref="Level"/> picks the one in
/// force. Floods are shallow: they don't count.
/// </summary>
public static class WaterMap
{
    private const float HalfSize = 50f;

    /// <summary>Route cells are this size (m)…</summary>
    private const float CellSize = 1f;

    private const int Cells = (int)(2 * HalfSize / CellSize);

    /// <summary>…and a route keeps this far off the water's edge…</summary>
    private const float ShoreClearance = 0.6f;

    /// <summary>…and off the oak's trunk and roots.</summary>
    private const float OakClearance = 0.35f;

    /// <summary>The fine grid (m) that says exactly where the water is, for <see cref="IsWet"/>.</summary>
    private const float WetCellSize = 0.25f;

    private const int WetCells = (int)(2 * HalfSize / WetCellSize);

    /// <summary>A route search gives up after looking at this many cells (the garden has 10,000).</summary>
    private const int MaxSearch = 6000;

    /// <summary>How many water levels are worked out: 0 is the pond as usual, the last its lowest in a drought.</summary>
    public const int Levels = 5;

    /// <summary>At its lowest, the water stands this far (m) below its usual level — a sixth or so of the pond left, in the bottom of each hollow.</summary>
    private const float DroughtDrop = 0.92f;

    /// <summary>The surface (world Y) at each level.</summary>
    public static readonly float[] LevelHeights = Enumerable.Range(0, Levels).Select(i => World.PondLevel - DroughtDrop * i / (Levels - 1)).ToArray();

    private static readonly float[] Ground = BuildGround();
    private static readonly bool[] Oak = BuildOak();
    private static readonly bool[][] Wet = Enumerable.Range(0, Levels).Select(BuildWet).ToArray();
    private static readonly bool[][] WaterBlocked = Enumerable.Range(0, Levels).Select(BuildWaterBlocked).ToArray();
    private static readonly bool[][] Blocked = Enumerable.Range(0, Levels).Select(l => WaterBlocked[l].Select((water, i) => water || Oak[i]).ToArray()).ToArray();
    private static readonly float[][] ShoreDistance = Enumerable.Range(0, Levels).Select(BuildShoreDistance).ToArray();
    private static readonly Vector3[][] Shores = Enumerable.Range(0, Levels).Select(BuildShore).ToArray();
    private static readonly int[] WetCount = Wet.Select(w => w.Count(c => c)).ToArray();

    /// <summary>The water level in force (0 = usual; see <see cref="Levels"/>).</summary>
    public static int Level { get; private set; }

    /// <summary>Goes up whenever the level changes, so walkers know to look again at their way round.</summary>
    public static int Generation { get; private set; }

    /// <summary>How much of the pond is left: 1 as usual, about a sixth at its lowest.</summary>
    public static float Fullness => WetCount[Level] / (float)Math.Max(1, WetCount[0]);

    /// <summary>The water's surface (world Y) now.</summary>
    public static float SurfaceHeight => LevelHeights[Level];

    /// <summary>How many routes have been worked out (for the headless report).</summary>
    public static int RoutesFound { get; private set; }

    /// <summary>Sets the water level in force (see World.Drought).</summary>
    public static void SetLevel(int level)
    {
        level = Math.Clamp(level, 0, Levels - 1);
        if (level == Level)
            return;
        Level = level;
        Generation++;
    }

    /// <summary>Stretches of shore, a meter or so from the water at the level in force: where watercress grows, drinkers drink and fishers fish.</summary>
    public static Vector3[] Shore => Shores[Level];

    /// <summary>The shore at the pond's usual level: where cress beds go, and what counts as living by the pond.</summary>
    public static Vector3[] UsualShore => Shores[0];

    private static float[] BuildGround()
    {
        var ground = new float[WetCells * WetCells];
        for (int x = 0; x < WetCells; x++)
        {
            for (int z = 0; z < WetCells; z++)
                ground[x * WetCells + z] = World.GetHeightAt(-HalfSize + (x + 0.5f) * WetCellSize, -HalfSize + (z + 0.5f) * WetCellSize);
        }
        return ground;
    }

    private static bool[] BuildWet(int level)
    {
        float surface = LevelHeights[level];
        return Ground.Select(height => height < surface).ToArray();
    }

    /// <summary>True if (x, z) is under water at <paramref name="level"/>.</summary>
    private static bool IsWetAt(float x, float z, int level) => World.GetHeightAt(x, z) < LevelHeights[level];

    /// <summary>True if water at <paramref name="level"/> reaches within <paramref name="clearance"/> of (x, z) — at its centre or four points round it.</summary>
    private static bool IsWetNear(float x, float z, float clearance, int level) =>
        IsWetAt(x, z, level) || IsWetAt(x + clearance, z, level) || IsWetAt(x - clearance, z, level) ||
        IsWetAt(x, z + clearance, level) || IsWetAt(x, z - clearance, level);

    private static bool[] BuildWaterBlocked(int level)
    {
        var blocked = new bool[Cells * Cells];
        for (int x = 0; x < Cells; x++)
        {
            for (int z = 0; z < Cells; z++)
                blocked[x * Cells + z] = IsWetNear(CellCenter(x), CellCenter(z), ShoreClearance, level);
        }
        return blocked;
    }

    private static bool[] BuildOak()
    {
        var oak = new bool[Cells * Cells];
        for (int x = 0; x < Cells; x++)
        {
            for (int z = 0; z < Cells; z++)
                oak[x * Cells + z] = World.IsOnOak(new Vector3(CellCenter(x), 0f, CellCenter(z)), OakClearance);
        }
        return oak;
    }

    /// <summary>How far (m) each cell is from the water's edge at <paramref name="level"/>, walking round the pond and the oak — worked out outward from the shore.</summary>
    private static float[] BuildShoreDistance(int level)
    {
        bool[] water = WaterBlocked[level];
        var distance = new float[Cells * Cells];
        var frontier = new PriorityQueue<int, float>();
        for (int i = 0; i < distance.Length; i++)
        {
            distance[i] = water[i] ? 0f : float.MaxValue;
            if (water[i])
                frontier.Enqueue(i, 0f);
        }
        while (frontier.TryDequeue(out int current, out float reached))
        {
            if (reached > distance[current])
                continue;
            int cx = current / Cells, cz = current % Cells;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    int x = cx + dx, z = cz + dz;
                    if ((dx == 0 && dz == 0) || x < 0 || z < 0 || x >= Cells || z >= Cells)
                        continue;
                    int next = x * Cells + z;
                    if (Oak[next])
                        continue;
                    float step = reached + (dx != 0 && dz != 0 ? 1.4142f : 1f) * CellSize;
                    if (step < distance[next])
                    {
                        distance[next] = step;
                        frontier.Enqueue(next, step);
                    }
                }
            }
        }
        return distance;
    }

    /// <summary>The shore at <paramref name="level"/>: points 0.8–1.8m from the water, off the oak.</summary>
    private static Vector3[] BuildShore(int level)
    {
        var spots = new List<Vector3>();
        for (float x = -48f; x <= 48f; x += 1f)
        {
            for (float z = -48f; z <= 48f; z += 1f)
            {
                if (IsWetNear(x, z, 0.8f, level) || World.IsOnOak(new Vector3(x, 0f, z), 0.5f))
                    continue;
                bool near = false;
                for (int i = 0; i < 12 && !near; i++)
                {
                    float angle = i * MathF.Tau / 12f;
                    near = IsWetAt(x + MathF.Cos(angle) * 1.8f, z + MathF.Sin(angle) * 1.8f, level) ||
                           IsWetAt(x + MathF.Cos(angle) * 1.2f, z + MathF.Sin(angle) * 1.2f, level);
                }
                if (near)
                    spots.Add(World.Grounded(new Vector3(x, 0f, z)));
            }
        }
        return spots.ToArray();
    }

    /// <summary>How far (m) (x, z) is from the water's edge now, walking round the pond and the oak — how far a Bramblekin there walks for a drink.</summary>
    public static float DistanceToWater(float x, float z) => ShoreDistance[Level][CellOf(x) * Cells + CellOf(z)];

    /// <summary>How far (m) (x, z) is from the water's edge at the pond's usual level — what settlers weigh up.</summary>
    public static float UsualDistanceToWater(float x, float z) => ShoreDistance[0][CellOf(x) * Cells + CellOf(z)];

    private static float CellCenter(int index) => -HalfSize + (index + 0.5f) * CellSize;

    private static int CellOf(float coordinate) => Math.Clamp((int)MathF.Floor((coordinate + HalfSize) / CellSize), 0, Cells - 1);

    private static bool IsOpen(int x, int z) => x >= 0 && z >= 0 && x < Cells && z < Cells && !Blocked[Level][x * Cells + z];

    /// <summary>True if (x, z) is under water now — a quick lookup, for every step a walker takes.</summary>
    public static bool IsWet(float x, float z)
    {
        int cx = Math.Clamp((int)MathF.Floor((x + HalfSize) / WetCellSize), 0, WetCells - 1);
        int cz = Math.Clamp((int)MathF.Floor((z + HalfSize) / WetCellSize), 0, WetCells - 1);
        return Wet[Level][cx * WetCells + cz];
    }

    /// <summary>
    /// True if the straight way from <paramref name="from"/> to
    /// <paramref name="to"/> stays clear of the water, with room to spare.
    /// Stepping off the shore at the start, or onto it at the end, is fine —
    /// so a walker standing by the water, or heading for a spot beside it,
    /// isn't sent round the whole pond — but not crossing any water between.
    /// </summary>
    public static bool IsClearWay(Vector2 from, Vector2 to)
    {
        float length = Vector2.Distance(from, to);
        int samples = (int)MathF.Ceiling(length / 0.5f);
        if (samples <= 1)
            return !IsWet(to.X, to.Y);
        Vector2 step = (to - from) / samples;

        // Skip the shore at either end (near the water, but not in it)…
        int first = 0, last = samples;
        while (first < samples && IsShore(from + step * first))
            first++;
        while (last > first && IsShore(from + step * last))
            last--;

        // …and everything between must be open ground.
        for (int i = first; i <= last; i++)
        {
            Vector2 point = from + step * i;
            if (!IsOpen(CellOf(point.X), CellOf(point.Y)))
                return false;
        }
        return true;

        static bool IsShore(Vector2 point) =>
            WaterBlocked[Level][CellOf(point.X) * Cells + CellOf(point.Y)] && !Oak[CellOf(point.X) * Cells + CellOf(point.Y)] && !IsWet(point.X, point.Y);
    }

    // Search scratch space, reused by every search (the simulation runs on one thread).
    private static readonly float[] Cost = new float[Cells * Cells];
    private static readonly int[] CameFrom = new int[Cells * Cells];
    private static readonly int[] Stamp = new int[Cells * Cells];
    private static readonly PriorityQueue<int, float> Frontier = new();
    private static int _search;

    /// <summary>
    /// A way round the water (and the oak) from <paramref name="from"/> to
    /// <paramref name="to"/>: waypoints ending at <paramref name="to"/> —
    /// if that's in blocked ground, by a last straight step from the nearest
    /// open square. Null if there's no way round — the walker then goes
    /// straight, and stops at the shore.
    /// </summary>
    public static List<Vector2>? FindRoute(Vector2 from, Vector2 to)
    {
        if (NearestOpen(CellOf(from.X), CellOf(from.Y)) is not { } start ||
            NearestOpen(CellOf(to.X), CellOf(to.Y)) is not { } goal)
            return null;
        RoutesFound++;

        _search++;
        Frontier.Clear();
        int startIndex = start.X * Cells + start.Z, goalIndex = goal.X * Cells + goal.Z;
        Stamp[startIndex] = _search;
        Cost[startIndex] = 0f;
        CameFrom[startIndex] = -1;
        Frontier.Enqueue(startIndex, 0f);

        int looked = 0;
        bool found = false;
        while (Frontier.TryDequeue(out int current, out float priority))
        {
            if (current == goalIndex)
            {
                found = true;
                break;
            }
            if (++looked > MaxSearch)
                break;
            int cx = current / Cells, cz = current % Cells;
            if (priority - Heuristic(cx, cz, goal.X, goal.Z) > Cost[current] + 1e-3f)
                continue; // A stale entry: a cheaper way here was found since.

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if ((dx == 0 && dz == 0) || !IsOpen(cx + dx, cz + dz))
                        continue;
                    if (dx != 0 && dz != 0 && (!IsOpen(cx + dx, cz) || !IsOpen(cx, cz + dz)))
                        continue; // No cutting a corner of the water.
                    int next = (cx + dx) * Cells + cz + dz;
                    float cost = Cost[current] + (dx != 0 && dz != 0 ? 1.4142f : 1f);
                    if (Stamp[next] == _search && Cost[next] <= cost)
                        continue;
                    Stamp[next] = _search;
                    Cost[next] = cost;
                    CameFrom[next] = current;
                    Frontier.Enqueue(next, cost + Heuristic(cx + dx, cz + dz, goal.X, goal.Z));
                }
            }
        }
        if (!found)
            return null;

        // The cells walked, goal back to start…
        var cells = new List<Vector2>();
        for (int index = goalIndex; index >= 0; index = CameFrom[index])
            cells.Add(new Vector2(CellCenter(index / Cells), CellCenter(index % Cells)));
        cells.Reverse();
        // The way always ends at the target itself: when that lies in ground the grid counts as blocked
        // (right by a root, a rock or the shore), the last step is a short straight one from the nearest
        // open square — never stopping a pace short and waiting there for good.
        if (IsOpen(CellOf(to.X), CellOf(to.Y)))
            cells[^1] = to;
        else
            cells.Add(to);

        // …pulled tight: from each waypoint, straight on as far along them as is in clear sight.
        var route = new List<Vector2>();
        Vector2 anchor = from;
        int i = -1;
        while (i < cells.Count - 1)
        {
            int furthest = i + 1;
            while (furthest + 1 < cells.Count && IsClearWay(anchor, cells[furthest + 1]))
                furthest++;
            route.Add(cells[furthest]);
            anchor = cells[furthest];
            i = furthest;
        }
        return route;
    }

    /// <summary>Octile distance: the length of the best eight-way walk on an open grid.</summary>
    private static float Heuristic(int x, int z, int goalX, int goalZ)
    {
        int dx = Math.Abs(x - goalX), dz = Math.Abs(z - goalZ);
        return Math.Max(dx, dz) + 0.4142f * Math.Min(dx, dz);
    }

    /// <summary>The open cell nearest (x, z) — itself, if it's open — searching out a few meters.</summary>
    private static (int X, int Z)? NearestOpen(int x, int z)
    {
        if (IsOpen(x, z))
            return (x, z);
        for (int ring = 1; ring <= 16; ring++)
        {
            (int, int)? best = null;
            float bestDistance = float.MaxValue;
            for (int dx = -ring; dx <= ring; dx++)
            {
                for (int dz = -ring; dz <= ring; dz++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != ring || !IsOpen(x + dx, z + dz))
                        continue;
                    float distance = dx * dx + dz * dz;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = (x + dx, z + dz);
                    }
                }
            }
            if (best is not null)
                return best;
        }
        return null;
    }
}
