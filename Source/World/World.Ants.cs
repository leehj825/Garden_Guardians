using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    /// <summary>The ant colony digs in once the garden is this many years old.</summary>
    private const int AnthillYear = 2;

    /// <summary>A new ant comes out of the hill this often (s), while it's short of its <see cref="Anthill.MaxAnts"/>.</summary>
    private const float AntSpawnInterval = 30f;

    private static readonly Color AntTextColor = new(150, 60, 40, 255);

    private readonly List<Ant> _pendingAntRemovals = new();
    private float _antSpawnTimer;

    /// <summary>The rival ant colony, once it has dug in (see <see cref="AnthillYear"/>).</summary>
    public Anthill? Anthill { get; private set; }

    public List<Ant> Ants { get; } = new();

    /// <summary>Food carried off from stores by ants.</summary>
    public int AntThefts { get; private set; }

    public int AntsKilled { get; private set; }

    /// <summary>
    /// The ants: the colony digs in near an edge, well away from any home,
    /// in the garden's second year; from spring to autumn it sends ants out
    /// after the stores; in winter they stay underground.
    /// </summary>
    private void UpdateAnts(float deltaTime)
    {
        if (Anthill is null)
        {
            if (Year >= AnthillYear && CurrentSeason != Season.Winter)
                FoundAnthill();
            return;
        }

        if (CurrentSeason == Season.Winter)
        {
            // Underground for the winter: whatever the ants carry goes home with them.
            foreach (Ant ant in Ants)
            {
                if (ant.IsLaden)
                    Anthill.Stock++;
            }
            Ants.Clear();
            return;
        }

        _antSpawnTimer -= deltaTime;
        if (_antSpawnTimer <= 0f)
        {
            _antSpawnTimer = AntSpawnInterval;
            if (Ants.Count < Anthill.MaxAnts)
                Ants.Add(new Ant(Anthill.Position, Rng));
        }

        for (int i = Ants.Count - 1; i >= 0; i--)
            Ants[i].Update(deltaTime, this, Anthill);
    }

    private void FoundAnthill()
    {
        // Near an edge, as far as it can get from every home.
        Vector3 best = RandomEdgeSpot(Anthill.Radius, Bramblekin.EdgeMargin + 6f);
        float bestDistance = -1f;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            Vector3 candidate = RandomEdgeSpot(Anthill.Radius, Bramblekin.EdgeMargin + 6f);
            float nearest = float.MaxValue;
            foreach (Shelter shelter in Shelters)
            {
                if (!shelter.IsCollapsed)
                    nearest = MathF.Min(nearest, GroundMover.HorizontalDistance(candidate, shelter.Position));
            }
            if (nearest > bestDistance)
            {
                best = candidate;
                bestDistance = nearest;
            }
        }
        Anthill = new Anthill(best);
        _antSpawnTimer = 0f;
        Game.AddEventLog("[ANTS] A colony of ants has dug in at the edge of the garden");
        Headline("Ants", "A colony of ants has dug in at the edge of the garden - they'll be after the stores", Anthill.Position, false);
    }

    /// <summary>The nearest store an ant at <paramref name="from"/> can rob: built, with food, not palisaded, within reach of its hill.</summary>
    public Shelter? StoreForAnts(Vector3 from, Anthill hill)
    {
        Shelter? best = null;
        float bestDistanceSquared = float.MaxValue;
        foreach (Shelter shelter in Shelters)
        {
            if (!shelter.IsBuilt || shelter.IsCollapsed || shelter.HasPalisade || shelter.HasFooting || shelter.IsBurrow || shelter.StoredFood <= 0)
                continue;
            if (GroundMover.HorizontalDistanceSquared(shelter.Position, hill.Position) > Anthill.ForageRadius * Anthill.ForageRadius)
                continue;
            float distanceSquared = GroundMover.HorizontalDistanceSquared(from, shelter.Position);
            if (distanceSquared < bestDistanceSquared)
            {
                best = shelter;
                bestDistanceSquared = distanceSquared;
            }
        }
        return best;
    }

    /// <summary>An ant at a store takes a piece of food. False if it's empty.</summary>
    public bool AntSteal(Ant ant, Shelter store)
    {
        if (!store.TryWithdraw())
            return false;
        AntThefts++;
        if (AntThefts % 5 == 1)
            QueueFloatingText(store.Position, "Ants!", AntTextColor);
        return true;
    }

    /// <summary>A swatted ant: whatever it carried falls where it died. Removal from <see cref="Ants"/> is deferred to the end of the frame.</summary>
    public void KillAnt(Ant ant)
    {
        if (ant.IsDead)
            return;
        ant.MarkDead();
        _pendingAntRemovals.Add(ant);
        AntsKilled++;
        if (ant.IsLaden && !ant.CarriesAphid)
            _pendingFoodSpawns.Add((ant.Position, FoodShardKind.Berry));
    }

    /// <summary>The nearest living ant within <paramref name="radius"/> of <paramref name="from"/>, if any.</summary>
    public Ant? NearestLiveAnt(Vector3 from, float radius)
    {
        Ant? best = null;
        float bestDistanceSquared = radius * radius;
        foreach (Ant ant in Ants)
        {
            if (ant.IsDead)
                continue;
            float distanceSquared = GroundMover.HorizontalDistanceSquared(from, ant.Position);
            if (distanceSquared <= bestDistanceSquared)
            {
                best = ant;
                bestDistanceSquared = distanceSquared;
            }
        }
        return best;
    }

    /// <summary>End of frame: swatted ants leave the list.</summary>
    private void CommitAntRemovals()
    {
        foreach (Ant ant in _pendingAntRemovals)
            Ants.Remove(ant);
        _pendingAntRemovals.Clear();
    }

    private void DrawAnts(Camera3D camera)
    {
        if (Anthill is { } hill && IsVisible(hill.Position, camera))
            hill.Draw();
        foreach (Ant ant in Ants)
        {
            if (!ant.IsDead && IsVisible(ant.Position, camera))
                ant.Draw();
        }
    }

    /// <summary>Loading a saved world: the ant colony as it was (its ants come back out on their own).</summary>
    private void RestoreAnthill(V3? position, int stock)
    {
        if (position is { } where)
            Anthill = new Anthill(where) { Stock = stock };
    }
}
