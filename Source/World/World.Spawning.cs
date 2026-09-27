using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    // --- Spawners -----------------------------------------------------------------------

    /// <summary>Passive Foraging: a wild Berry every <see cref="BerrySpawnInterval"/> seconds, up to <see cref="MaxBerries"/> — both scaled by the season (see <see cref="FoodAbundance"/>).</summary>
    private void UpdateBerrySpawn(float deltaTime)
    {
        _berrySpawnTimer -= deltaTime;
        if (_berrySpawnTimer > 0f)
            return;
        _berrySpawnTimer = BerrySpawnInterval / FoodAbundance;

        int berries = 0;
        foreach (FoodShard food in FoodShards)
        {
            if (food.IsActive && food.Kind == FoodShardKind.Berry)
                berries++;
        }
        berries += _pendingFoodSpawns.Count(f => f.Kind == FoodShardKind.Berry);
        if (berries >= MaxBerries * FoodAbundance)
            return;

        _pendingFoodSpawns.Add((RandomBerrySpot(), FoodShardKind.Berry));
    }

    /// <summary>Somewhere in a Berry Patch (<see cref="BerryPatchChance"/> of the time), else anywhere open on the map.</summary>
    private Vector3 RandomBerrySpot()
    {
        if (_berryPatches.Count > 0 && Rng.NextDouble() < BerryPatchChance)
        {
            Vector3 anchor = _berryPatches[Rng.Next(_berryPatches.Count)];
            float angle = (float)(Rng.NextDouble() * MathF.Tau);
            float radius = MathF.Sqrt((float)Rng.NextDouble()) * BerryPatchRadius;
            Vector3 spot = anchor + new Vector3(MathF.Cos(angle) * radius, 0f, MathF.Sin(angle) * radius);
            if (Terrain.Contains(spot, 1f) && !IsBlocked(spot, FoodShard.Radius + 0.1f))
                return spot;
        }
        return RandomFreePoint(FoodShard.Radius + 0.3f, edgeMargin: 1f);
    }

    private void UpdateSpiderRespawn(float deltaTime)
    {
        if (Spider is not null || SpiderRespawnTimer <= 0f)
            return;

        SpiderRespawnTimer -= deltaTime;
        if (SpiderRespawnTimer <= 0f)
        {
            SpawnSpider();
            Game.AddEventLog("[PREDATOR] A new Wolf Spider has moved in");
        }
    }

    /// <summary>
    /// The Hornet Swarm's spawner: tops the population up in whole clusters
    /// of <see cref="HornetSwarmMinSize"/>-<see cref="HornetSwarmMaxSize"/>
    /// Hornets at once. Each cluster nests around a randomly chosen
    /// <see cref="GardenProp"/> — which can easily be a Berry Patch's
    /// Dandelion, making that patch a risk worth weighing.
    /// </summary>
    private void UpdateHornetSpawn(float deltaTime)
    {
        _hornetSpawnTimer -= deltaTime;
        if (_hornetSpawnTimer > 0f)
            return;
        _hornetSpawnTimer = HornetSpawnInterval;

        int living = Hornets.Count(h => !h.IsDead) + _pendingHornetSpawns.Count;
        if (living + HornetSwarmMinSize > MaxHornetsOnMap)
            return;

        Vector3 anchor = GardenProps.Count > 0
            ? GardenProps[Rng.Next(GardenProps.Count)].Position
            : RandomFreePoint(Hornet.BodyRadius + 0.1f, Hornet.EdgeMargin);

        int clusterSize = HornetSwarmMinSize + Rng.Next(HornetSwarmMaxSize - HornetSwarmMinSize + 1);
        for (int i = 0; i < clusterSize && living + i < MaxHornetsOnMap; i++)
        {
            float angle = (float)(Rng.NextDouble() * MathF.Tau);
            float jitter = (float)Rng.NextDouble() * Hornet.ClusterJitterRadius;
            Vector3 spot = anchor + new Vector3(MathF.Cos(angle) * jitter, 0f, MathF.Sin(angle) * jitter);
            if (!Terrain.Contains(spot, Hornet.EdgeMargin))
                spot = anchor;
            _pendingHornetSpawns.Add(new Hornet(spot, anchor, Rng));
        }
    }

    /// <summary>Grubs burrow in one at a time from the map's edges, up to <see cref="MaxGrubsOnMap"/>.</summary>
    private void UpdateGrubSpawn(float deltaTime)
    {
        _grubSpawnTimer -= deltaTime;
        if (_grubSpawnTimer > 0f)
            return;
        _grubSpawnTimer = GrubSpawnInterval;

        int living = Grubs.Count(g => !g.IsDead) + _pendingGrubSpawns.Count;
        if (living >= MaxGrubsOnMap)
            return;

        _pendingGrubSpawns.Add(new Grub(RandomEdgeSpot(Grub.BodyRadius + 0.1f, Grub.EdgeMargin), Rng));
    }

    /// <summary>
    /// Wandering Arrivals: a new solitary Bramblekin, with its own freshly
    /// randomized Personality, drifts in from a random edge every
    /// <see cref="ArrivalInterval"/> seconds while the population is below
    /// <see cref="ArrivalPopulationLimit"/>.
    /// </summary>
    private void UpdateArrivals(float deltaTime)
    {
        _arrivalTimer -= deltaTime;
        if (_arrivalTimer > 0f)
            return;
        _arrivalTimer = ArrivalInterval;

        int living = Colony.Count(b => !b.IsDead) + _pendingKinSpawns.Count;
        if (living >= ArrivalPopulationLimit)
            return;

        Bramblekin kin = Newcomer(RandomEdgeSpot(Bramblekin.BodyRadius, Bramblekin.EdgeMargin + 0.5f));
        _pendingKinSpawns.Add(kin);
        Arrivals++;
        Personality p = kin.Personality;
        Game.AddEventLog($"[ARRIVAL] {kin.Name} wandered in (aggr {p.Aggression:0.00}, soc {p.Sociability:0.00}, int {p.Intelligence:0.00})");
    }

    /// <summary>Family names already founded — every newcomer founds a new one.</summary>
    private readonly HashSet<string> _familyNames = new();

    /// <summary>A newcomer: a fresh Personality and a name, founding a family nobody has yet.</summary>
    private Bramblekin Newcomer(Vector3 at)
    {
        var kin = new Bramblekin(at, Rng);
        kin.Christen(Names.Given(Rng), NewFamilyName());
        kin.SetArrivalAge(Rng);
        return kin;
    }

    /// <summary>A family name nobody has founded yet (as long as there are any left).</summary>
    public string NewFamilyName()
    {
        string name = Names.Family(Rng);
        for (int attempt = 0; attempt < 50 && _familyNames.Contains(name); attempt++)
            name = Names.Family(Rng);
        _familyNames.Add(name);
        return name;
    }

    /// <summary>Names a new group after the family of the Leader it was founded under — see <see cref="Names.Clan"/>.</summary>
    private void NameGroup(KinGroup group)
    {
        if (group.Name is not null || group.Leader is not { } leader)
            return;
        group.Name = Names.Clan(leader.FamilyName, name => _groups.Values.Any(g => g != group && g.Name == name));
        Chronicle($"{group.CapitalTitle} was founded, led by {leader.Name}", group);
    }

    /// <summary>Dibs failsafe — see <see cref="FoodClaimTimeoutSeconds"/>.</summary>
    private void UpdateFoodClaimTimeouts(float deltaTime)
    {
        foreach (FoodShard food in FoodShards)
        {
            if (!food.IsActive || food.ClaimedBy is null)
                continue;

            food.ClaimTimer += deltaTime;
            if (food.ClaimTimer >= FoodClaimTimeoutSeconds)
            {
                food.ClaimedBy = null;
                food.ClaimTimer = 0f;
            }
        }
    }

    /// <summary>Loose Food that nobody picks up rots away after <see cref="FoodShard.DespawnLifespan"/> seconds; carried Food never does.</summary>
    private void UpdateFoodDespawn(float deltaTime)
    {
        foreach (FoodShard food in FoodShards)
        {
            if (!food.IsActive || food.IsCarried)
                continue;

            food.DespawnTimer -= deltaTime;
            if (food.DespawnTimer <= 0f)
                food.Deactivate();
        }
    }

    public void QueueFloatingText(Vector3 position, string text, Color color) =>
        _floatingTexts.Add((position, text, color, FloatingTextDuration));
}
