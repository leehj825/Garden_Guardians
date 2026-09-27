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

    private readonly Dictionary<(int X, int Z), List<T>> _cells = new();

    private static (int X, int Z) ChunkOf(Vector3 position) =>
        ((int)MathF.Floor(position.X / ChunkSize), (int)MathF.Floor(position.Z / ChunkSize));

    /// <summary>Empties every chunk, ready for this frame's <see cref="Register"/> calls.</summary>
    public void Clear()
    {
        foreach (var list in _cells.Values)
            list.Clear();
    }

    /// <summary>Registers <paramref name="item"/> under the chunk containing <paramref name="position"/>.</summary>
    public void Register(T item, Vector3 position)
    {
        var key = ChunkOf(position);
        if (!_cells.TryGetValue(key, out List<T>? list))
        {
            list = new List<T>();
            _cells[key] = list;
        }
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
                if (_cells.TryGetValue((x, z), out List<T>? list))
                    results.AddRange(list);
            }
        }
    }
}
