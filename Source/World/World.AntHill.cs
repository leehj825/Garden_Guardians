using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    // --- The ant hill: a fixed hill in a corner, thief ants, and guards (see Garden_Guardians_Design.md, "The Ant Hill") ----------------

    /// <summary>A new thief ant comes out of the hill this often (s), while it is short of its <see cref="Anthill.MaxThieves"/>.</summary>
    private const float AntSpawnInterval = 30f;

    /// <summary>The guards heal and are replaced only once no Bramblekin has been in the zone for this long (s).</summary>
    public const float AntZoneQuietNeeded = 10f;

    /// <summary>One dead guard is replaced this often (s), while the zone is quiet.</summary>
    private const float GuardRefillInterval = 3f;

    private static readonly Color AntTextColor = new(150, 60, 40, 255);

    private readonly List<Ant> _pendingAntRemovals = new();
    private float _antSpawnTimer;
    private float _guardRefillTimer;

    /// <summary>The ant colony's hill: fixed in a corner of the garden from the first day.</summary>
    public Anthill? Anthill { get; private set; }

    /// <summary>The thief ants out of the hill (single ants after the Bramblekin's stores; not its guards).</summary>
    public List<Ant> Ants { get; } = new();

    /// <summary>The hill's guards, hidden ones (healing inside) included.</summary>
    public List<HillGuard> HillGuards { get; } = new();

    /// <summary>The Bramblekin in the hill's no-go zone right now (rebuilt each frame).</summary>
    public List<Bramblekin> AntZoneKin { get; } = new();

    /// <summary>How long (s) the zone has been empty of Bramblekin.</summary>
    public float AntZoneQuietSeconds { get; private set; } = AntZoneQuietNeeded;

    /// <summary>True while any Bramblekin is in the zone or within <see cref="Anthill.AlertMargin"/> of it: the hill is on alert.</summary>
    public bool AntAlert { get; private set; }

    /// <summary>How long (s) no Bramblekin has been near enough to alert the hill.</summary>
    public float AntAlertQuietSeconds { get; private set; } = AntZoneQuietNeeded;

    /// <summary>Food carried off from stores by ants.</summary>
    public int AntThefts { get; private set; }

    public int AntsKilled { get; private set; }
    public int GuardsSlain { get; private set; }

    /// <summary>True if <paramref name="point"/> lies in (or within <paramref name="margin"/> of) the hill's no-go zone.</summary>
    public bool IsInAntZone(Vector3 point, float margin = 0f) => Anthill is { } hill && hill.IsInZone(point, margin);

    /// <summary>Blocked by something solid, or in the hill's no-go zone: for choosing where to build, plant, settle, spawn or wander.</summary>
    public bool IsBlockedOrAntZone(Vector3 point, float clearance) => IsBlocked(point, clearance) || IsInAntZone(point);

    /// <summary>
    /// The hill goes up in one of the four corners, the same place every time for a terrain: the corner with room for it that is farthest
    /// from the oak and the water and has the gentlest ground.
    /// </summary>
    private void PlaceAnthill()
    {
        float half = TerrainData.Half;
        float inset = Anthill.Radius + 9f;
        Vector3? best = null;
        float bestScore = float.MinValue;
        foreach ((int sx, int sz) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
        {
            for (int step = 0; step < 8; step++)
            {
                float d = half - inset - step * 3f;
                Vector3 spot = Grounded(new Vector3(sx * d, 0f, sz * d));
                if (IsBlocked(spot, Anthill.Radius + 1.5f))
                    continue;
                float slope = MathF.Abs(GetHeightAt(spot.X + 4f, spot.Z) - GetHeightAt(spot.X - 4f, spot.Z)) +
                              MathF.Abs(GetHeightAt(spot.X, spot.Z + 4f) - GetHeightAt(spot.X, spot.Z - 4f));
                float score = GroundMover.HorizontalDistance(spot, OakCenter) + MathF.Min(25f, WaterMap.UsualDistanceToWater(spot.X, spot.Z)) - 6f * slope;
                if (score > bestScore)
                {
                    best = spot;
                    bestScore = score;
                }
                break;
            }
        }
        best ??= Grounded(new Vector3(half - inset, 0f, half - inset));
        SetAnthill(new Anthill(best.Value, Vector3.Zero));
        FillGuards();
    }

    /// <summary>The hill's full guard: a couple of sentries out, the rest inside.</summary>
    private void FillGuards()
    {
        HillGuards.Clear();
        for (int i = 0; i < Anthill!.GuardCount; i++)
            HillGuards.Add(new HillGuard(Anthill.Mouth, Rng, hidden: i >= Anthill.Sentries, sentry: i < Anthill.Sentries));
    }

    private void SetAnthill(Anthill hill)
    {
        Anthill = hill;
        RebuildObstacles();
    }

    /// <summary>
    /// The ants: from spring to autumn thief ants come out one at a time after the stores (in winter they stay underground), the guards
    /// keep to the zone, and the Kingdom's assault, if there is one, is carried through.
    /// </summary>
    private void UpdateAnts(float deltaTime)
    {
        if (Anthill is not { } hill)
            return;

        // Who is in the no-go zone.
        AntZoneKin.Clear();
        foreach (Bramblekin kin in QueryNearbyColony(hill.Position, Anthill.ZoneRadius))
        {
            if (!kin.IsDead && hill.IsInZone(kin.Position))
                AntZoneKin.Add(kin);
        }
        AntZoneQuietSeconds = AntZoneKin.Count > 0 ? 0f : AntZoneQuietSeconds + deltaTime;
        AntAlert = AntZoneKin.Count > 0;
        if (!AntAlert)
        {
            foreach (Bramblekin kin in QueryNearbyColony(hill.Position, Anthill.ZoneRadius + Anthill.AlertMargin))
            {
                if (!kin.IsDead && hill.IsInZone(kin.Position, Anthill.AlertMargin))
                {
                    AntAlert = true;
                    break;
                }
            }
        }
        AntAlertQuietSeconds = AntAlert ? 0f : AntAlertQuietSeconds + deltaTime;

        // Thieves.
        if (CurrentSeason == Season.Winter)
        {
            // Underground for the winter: whatever the thieves carry goes home with them.
            foreach (Ant ant in Ants)
            {
                if (ant.IsLaden)
                    hill.Stock++;
            }
            Ants.Clear();
        }
        else
        {
            _antSpawnTimer -= deltaTime;
            if (_antSpawnTimer <= 0f)
            {
                _antSpawnTimer = AntSpawnInterval;
                if (Ants.Count < hill.MaxThieves)
                    Ants.Add(new Ant(hill.Mouth, Rng));
            }
            for (int i = Ants.Count - 1; i >= 0; i--)
                Ants[i].Update(deltaTime, this, hill);
        }

        // Guards: the dead are replaced while the zone is quiet, up to the hill's level.
        if (AntZoneQuietSeconds >= AntZoneQuietNeeded && HillGuards.Count < hill.GuardCount)
        {
            _guardRefillTimer -= deltaTime;
            if (_guardRefillTimer <= 0f)
            {
                _guardRefillTimer = GuardRefillInterval;
                HillGuards.Add(new HillGuard(hill.Mouth, Rng, hidden: true, sentry: HillGuards.Count(g => g.IsSentry && !g.IsDead) < Anthill.Sentries));
            }
        }
        for (int i = HillGuards.Count - 1; i >= 0; i--)
            HillGuards[i].Update(deltaTime, this, hill);

        UpdateAssault(deltaTime);
    }


    /// <summary>The nearest guard out on the surface within <paramref name="radius"/> of <paramref name="from"/>, if any.</summary>
    public HillGuard? NearestHillGuard(Vector3 from, float radius)
    {
        HillGuard? best = null;
        float bestDistanceSquared = radius * radius;
        foreach (HillGuard guard in HillGuards)
        {
            if (guard.IsDead || guard.IsHidden)
                continue;
            float distanceSquared = GroundMover.HorizontalDistanceSquared(from, guard.Position);
            if (distanceSquared <= bestDistanceSquared)
            {
                best = guard;
                bestDistanceSquared = distanceSquared;
            }
        }
        return best;
    }

    /// <summary>A guard struck down: it counts for whoever fought it in the Kingdom's assault. Removal from <see cref="HillGuards"/> is deferred to the end of the frame.</summary>
    public void KillHillGuard(HillGuard guard, Bramblekin killer)
    {
        if (guard.IsDead)
            return;
        guard.MarkDead();
        GuardsSlain++;
        if (killer.AssaultParty is { } assault)
            assault.Kills[killer.ID] = assault.Kills.GetValueOrDefault(killer.ID) + 1;
    }

    /// <summary>The nearest store a thief ant at <paramref name="from"/> can rob: built, with food, not palisaded, within reach of its hill.</summary>
    public Shelter? StoreForAnts(Vector3 from, Anthill hill, Shelter? avoid = null)
    {
        Shelter? best = null;
        float bestDistanceSquared = float.MaxValue;
        foreach (Shelter shelter in Shelters)
        {
            if (shelter == avoid || !shelter.IsBuilt || shelter.IsCollapsed || shelter.HasPalisade || shelter.HasFooting || shelter.IsBurrow || shelter.StoredFood <= 0)
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

    /// <summary>A swatted thief ant: whatever it carried falls where it died. Removal from <see cref="Ants"/> is deferred to the end of the frame.</summary>
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

    /// <summary>A thief ant that is stuck lets go of what it carries where it stands (a piece of food falls; an aphid is lost).</summary>
    public void AntDropsLoad(Ant ant)
    {
        if (ant.IsLaden && !ant.CarriesAphid)
            _pendingFoodSpawns.Add((ant.Position, FoodShardKind.Berry));
    }

    /// <summary>The nearest living thief ant within <paramref name="radius"/> of <paramref name="from"/>, if any.</summary>
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

    /// <summary>End of frame: swatted ants and fallen guards leave their lists.</summary>
    private void CommitAntRemovals()
    {
        foreach (Ant ant in _pendingAntRemovals)
            Ants.Remove(ant);
        _pendingAntRemovals.Clear();
        HillGuards.RemoveAll(g => g.IsDead);
    }

    private void DrawAnts(Camera3D camera)
    {
        Anthill?.Draw();
        if (Detail.FarView)
            return; // (the hill alone: no eggs, ants or guards)
        if (Anthill is { } eggHill)
        {
            // The eggs lie in the cave just inside the entrance, where they can be seen from outside (a looter takes one off the heap).
            int eggs = Math.Min(Anthill.Level + 2, 7);
            if (CurrentAssault is { Phase: AssaultPhase.Looting } clutch)
                eggs = Math.Max(0, eggs - clutch.Carriers.Count);
            var side = new Vector3(-eggHill.Facing.Z, 0f, eggHill.Facing.X);
            for (int i = 0; i < eggs; i++)
            {
                Vector3 at = eggHill.Top + side * ((i % 3 - 1) * 0.42f) - eggHill.Facing * ((i / 3) * 0.3f) + new Vector3(0f, 0.03f, 0f);
                PropModels.Draw(PropModels.Prop.Larvae, at, i * 67f, 0.6f, Color.White);
            }
        }
        foreach (Ant ant in Ants)
        {
            if (!ant.IsDead && IsVisible(ant.Position, camera))
                ant.Draw();
        }
        foreach (HillGuard guard in HillGuards)
        {
            if (!guard.IsDead && !guard.IsHidden && IsVisible(guard.Position, camera))
                guard.Draw();
        }
    }

    /// <summary>Loading a saved world: the hill as it was (its ants come back out on their own, the guards at full strength).</summary>
    private void RestoreAnthill(V3? position, int stock, int level)
    {
        if (position is not { } where)
            return;
        SetAnthill(new Anthill(where, Vector3.Zero) { Stock = stock, Level = Math.Max(1, level) });
        FillGuards();
    }
}
