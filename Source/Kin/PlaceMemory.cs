using System.Numerics;

namespace GardenGuardians;

/// <summary>
/// A handful of remembered places — where danger struck, where food grows —
/// each with when it was last confirmed. A new place close to one already
/// remembered just refreshes it; past <see cref="Capacity"/> the stalest
/// is forgotten. Used by each Bramblekin, and shared within its group (see
/// Bramblekin.Memory).
/// </summary>
public sealed class PlaceMemory
{
    private readonly List<(Vector3 Where, float When)> _places = new();

    public PlaceMemory(int capacity, float mergeRadius)
    {
        Capacity = capacity;
        MergeRadius = mergeRadius;
    }

    public int Capacity { get; }

    public float MergeRadius { get; }

    public int Count => _places.Count;

    public void Remember(Vector3 where, float now)
    {
        for (int i = 0; i < _places.Count; i++)
        {
            if (GroundMover.HorizontalDistanceSquared(_places[i].Where, where) <= MergeRadius * MergeRadius)
            {
                _places[i] = (where, now);
                return;
            }
        }
        if (_places.Count >= Capacity)
            _places.RemoveAt(IndexOfStalest());
        _places.Add((where, now));
    }

    /// <summary>True if a place remembered within the last <paramref name="maxAge"/> seconds lies within <paramref name="radius"/> of <paramref name="point"/>.</summary>
    public bool IsNear(Vector3 point, float radius, float now, float maxAge)
    {
        foreach (var (where, when) in _places)
        {
            if (now - when <= maxAge && GroundMover.HorizontalDistanceSquared(where, point) <= radius * radius)
                return true;
        }
        return false;
    }

    /// <summary>The most recently confirmed place younger than <paramref name="maxAge"/> that <paramref name="accept"/> allows, if any.</summary>
    public Vector3? Freshest(float now, float maxAge, Func<Vector3, bool> accept)
    {
        Vector3? best = null;
        float bestWhen = float.MinValue;
        foreach (var (where, when) in _places)
        {
            if (now - when <= maxAge && when > bestWhen && accept(where))
            {
                best = where;
                bestWhen = when;
            }
        }
        return best;
    }

    /// <summary>Forgets any place within <see cref="MergeRadius"/> of <paramref name="point"/> (it turned out empty).</summary>
    public void Forget(Vector3 point) =>
        _places.RemoveAll(p => GroundMover.HorizontalDistanceSquared(p.Where, point) <= MergeRadius * MergeRadius);

    /// <summary>Loading a saved world: puts back what was remembered.</summary>
    public void Load(IEnumerable<(Vector3 Where, float When)> places)
    {
        _places.Clear();
        _places.AddRange(places.Take(Capacity));
    }

    /// <summary>Everything remembered, for saving.</summary>
    public IReadOnlyList<(Vector3 Where, float When)> Places => _places;

    private int IndexOfStalest()
    {
        int stalest = 0;
        for (int i = 1; i < _places.Count; i++)
        {
            if (_places[i].When < _places[stalest].When)
                stalest = i;
        }
        return stalest;
    }
}
