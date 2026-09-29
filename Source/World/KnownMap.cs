using System.Numerics;

namespace GardenGuardians;

/// <summary>
/// What a clan knows of the garden: a coarse grid of cells, each known once one of its people has walked
/// near it (see World.Exploration). Not saved — a loaded garden re-marks the ground round each clan's homes
/// and people, and scouts fill in the rest again.
/// </summary>
public sealed class KnownMap
{
    /// <summary>Cell edge (m), and cells per side, covering the garden's ±50m.</summary>
    public const float CellSize = 5f;
    public const int Cells = 20;
    private const float Half = 50f;

    private readonly bool[] _known = new bool[Cells * Cells];

    /// <summary>How many cells are known.</summary>
    public int KnownCount { get; private set; }

    /// <summary>The share of the garden known, 0..1.</summary>
    public float Fraction => KnownCount / (float)(Cells * Cells);

    private static int Index(int cx, int cz) => cz * Cells + cx;

    private static bool InRange(int cx, int cz) => cx >= 0 && cz >= 0 && cx < Cells && cz < Cells;

    private static int CellOf(float coordinate) => (int)MathF.Floor((coordinate + Half) / CellSize);

    /// <summary>The middle of cell (<paramref name="cx"/>, <paramref name="cz"/>) on the ground plane.</summary>
    public static Vector3 CenterOf(int cx, int cz) => new(-Half + (cx + 0.5f) * CellSize, 0f, -Half + (cz + 0.5f) * CellSize);

    public bool IsKnown(int cx, int cz) => !InRange(cx, cz) || _known[Index(cx, cz)];

    public bool IsKnown(Vector3 point) => IsKnown(CellOf(point.X), CellOf(point.Z));

    /// <summary>Marks every cell whose centre lies within <paramref name="radius"/> of <paramref name="point"/>; returns how many were new.</summary>
    public int Mark(Vector3 point, float radius)
    {
        int newly = 0;
        int minX = CellOf(point.X - radius), maxX = CellOf(point.X + radius);
        int minZ = CellOf(point.Z - radius), maxZ = CellOf(point.Z + radius);
        float radiusSquared = radius * radius;
        for (int cz = Math.Max(0, minZ); cz <= Math.Min(Cells - 1, maxZ); cz++)
        {
            for (int cx = Math.Max(0, minX); cx <= Math.Min(Cells - 1, maxX); cx++)
            {
                if (_known[Index(cx, cz)])
                    continue;
                Vector3 centre = CenterOf(cx, cz);
                float dx = centre.X - point.X, dz = centre.Z - point.Z;
                if (dx * dx + dz * dz > radiusSquared)
                    continue;
                _known[Index(cx, cz)] = true;
                KnownCount++;
                newly++;
            }
        }
        return newly;
    }

    /// <summary>Marks the one cell containing <paramref name="point"/> (a scout gave up on it).</summary>
    public void MarkCell(Vector3 point)
    {
        int cx = CellOf(point.X), cz = CellOf(point.Z);
        if (InRange(cx, cz) && !_known[Index(cx, cz)])
        {
            _known[Index(cx, cz)] = true;
            KnownCount++;
        }
    }

    /// <summary>The unknown cell's centre nearest <paramref name="from"/> within <paramref name="reach"/>, chosen with a little randomness, or null if none.</summary>
    public Vector3? NearestUnknown(Vector3 from, float reach, Random rng)
    {
        Vector3? best = null;
        float bestScore = float.MaxValue;
        for (int cz = 0; cz < Cells; cz++)
        {
            for (int cx = 0; cx < Cells; cx++)
            {
                if (_known[Index(cx, cz)])
                    continue;
                Vector3 centre = CenterOf(cx, cz);
                float dx = centre.X - from.X, dz = centre.Z - from.Z;
                float distance = MathF.Sqrt(dx * dx + dz * dz);
                if (distance > reach)
                    continue;
                float score = distance + (float)rng.NextDouble() * 12f;
                if (score < bestScore)
                {
                    best = centre;
                    bestScore = score;
                }
            }
        }
        return best;
    }
}
