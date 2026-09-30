using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// A solid circle on the ground that walkers steer around — a large
/// Pebble's footprint. Coordinates are (x, z).
/// </summary>
public readonly record struct Obstacle(Vector2 Center, float Radius);

/// <summary>
/// The Spatial Grid: divides the map into fixed <see cref="ChunkSize"/>
/// (10m) chunks keyed by (chunk-x, chunk-z), so a nearest-target search
/// can look only at the handful of entities near the searcher instead of
/// scanning every entity on the whole map. Rebuilt from scratch once a
/// frame (see <see cref="World.RebuildSpatialGrids"/>) rather than having
/// each entity push incremental chunk-membership updates as it moves —
/// exactly equivalent, since every entity moves at most once per frame
/// anyway.
/// </summary>
public sealed class SpatialGrid<T>
{
    public const float ChunkSize = 10f;

    /// <summary>Chunks -<see cref="Half"/> to <see cref="Half"/>-1 on each axis (the whole garden and a chunk more) live in a flat array; anything further out in a dictionary.</summary>
    private readonly int Half = (int)MathF.Ceiling(TerrainData.Half / ChunkSize) + 1;

    private int Span => Half * 2;

    private readonly List<T>?[] _chunks;

    public SpatialGrid() => _chunks = new List<T>?[Half * 2 * Half * 2];
    private readonly Dictionary<(int X, int Z), List<T>> _outside = new();

    /// <summary>The chunks holding anything since the last <see cref="Clear"/>: only these need emptying, not every chunk of a big map.</summary>
    private readonly List<List<T>> _used = new();

    private static (int X, int Z) ChunkOf(Vector3 position) =>
        ((int)MathF.Floor(position.X / ChunkSize), (int)MathF.Floor(position.Z / ChunkSize));

    /// <summary>The list for chunk (<paramref name="x"/>, <paramref name="z"/>) — created if <paramref name="create"/>, else null if it's never been used.</summary>
    private List<T>? Chunk(int x, int z, bool create)
    {
        List<T>? list;
        if (x >= -Half && x < Half && z >= -Half && z < Half)
        {
            int index = (x + Half) * Span + z + Half;
            list = _chunks[index];
            if (list is null && create)
                _chunks[index] = list = new List<T>();
            return list;
        }
        if (!_outside.TryGetValue((x, z), out list) && create)
            _outside[(x, z)] = list = new List<T>();
        return list;
    }

    /// <summary>Empties every chunk, ready for this frame's <see cref="Register"/> calls.</summary>
    public void Clear()
    {
        foreach (List<T> list in _used)
            list.Clear();
        _used.Clear();
    }

    /// <summary>Registers <paramref name="item"/> under the chunk containing <paramref name="position"/>.</summary>
    public void Register(T item, Vector3 position)
    {
        var (x, z) = ChunkOf(position);
        List<T> list = Chunk(x, z, create: true)!;
        if (list.Count == 0)
            _used.Add(list);
        list.Add(item);
    }

    /// <summary>
    /// Fills <paramref name="results"/> (cleared first) with every item
    /// registered in the chunk containing <paramref name="position"/> and
    /// its 8 neighbors — a 30x30m window around the searcher, not the
    /// whole map. Guaranteed to cover at least <see cref="ChunkSize"/> in
    /// every direction.
    /// </summary>
    public void QueryNearby(Vector3 position, List<T> results) => QueryRadius(position, ChunkSize, results);

    /// <summary>
    /// Fills <paramref name="results"/> (cleared first) with every item in
    /// any chunk overlapping the square of half-size <paramref name="radius"/>
    /// around <paramref name="position"/> — a superset of everything within
    /// <paramref name="radius"/>, which the caller still distance-checks.
    /// Used for Intelligence-scaled perception, which can reach past a
    /// single neighboring chunk.
    /// </summary>
    public void QueryRadius(Vector3 position, float radius, List<T> results)
    {
        results.Clear();
        int minX = (int)MathF.Floor((position.X - radius) / ChunkSize);
        int maxX = (int)MathF.Floor((position.X + radius) / ChunkSize);
        int minZ = (int)MathF.Floor((position.Z - radius) / ChunkSize);
        int maxZ = (int)MathF.Floor((position.Z + radius) / ChunkSize);
        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                // As a span: no interface casts through shared generic code, thousands of times a step.
                if (Chunk(x, z, create: false) is { } list)
                    results.AddRange(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list));
            }
        }
    }
}
