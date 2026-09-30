using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

public sealed partial class World
{
    // --- Twigs ---------------------------------------------------------------------

    /// <summary>Object Pooling: fixed number of Twig slots, constructed once and reused.</summary>
    private static int TwigPoolCapacity => Scaled(150);

    private static int InitialTwigs => Scaled(30);

    /// <summary>Fallen twigs stop appearing once this many are lying loose.</summary>
    public static int MaxLooseTwigs => Scaled(50);

    /// <summary>Seconds between fallen twigs.</summary>
    public static float TwigSpawnInterval => 1.5f / MapArea;

    /// <summary>How far (m) from a big Twig prop a fallen twig may land.</summary>
    private const float TwigPatchRadius = 3f;

    /// <summary>Odds a fallen twig lands near a big Twig prop rather than anywhere on the map.</summary>
    private const double TwigPatchChance = 0.5;

    // --- Shelters --------------------------------------------------------------------

    /// <summary>No two shelters are ever built closer than this (m).</summary>
    public const float MinShelterSpacing = 6f;

    /// <summary>A new shelter site is looked for within this many meters of where its builder wants it.</summary>
    private const float ShelterSiteSearchRadius = 8f;

    private readonly List<Vector3> _twigPatches = new();
    private readonly SpatialGrid<Twig> _twigGrid = new();
    private readonly List<Twig> _twigQueryBuffer = new();
    private float _twigSpawnTimer = TwigSpawnInterval;

    /// <summary>Object Pooling: the fixed pool of Twig slots; only the <see cref="Twig.IsActive"/> ones are real.</summary>
    public List<Twig> Twigs { get; } = new();

    /// <summary>Every shelter on the map, finished or under construction.</summary>
    public List<Shelter> Shelters { get; } = new();

    public int LooseTwigCount { get; private set; }

    private double _tentBuildSeconds;
    private double _houseUpgradeSeconds;

    /// <summary>Average seconds from marking out a site to a finished Tent. NaN before the first.</summary>
    public double AverageTentBuildSeconds => TentsBuilt > 0 ? _tentBuildSeconds / TentsBuilt : double.NaN;

    /// <summary>Average seconds from starting a House upgrade to finishing it. NaN before the first.</summary>
    public double AverageHouseUpgradeSeconds => HousesBuilt > 0 ? _houseUpgradeSeconds / HousesBuilt : double.NaN;

    /// <summary>Construction stages (sites, or upgrades) under way for longer than <paramref name="seconds"/> — a sign building has stalled.</summary>
    public int StagesOlderThan(float seconds) =>
        Shelters.Count(s => (!s.IsBuilt || s.IsUpgrading) && ElapsedSeconds - s.StageStartedAt > seconds);

    public int TentsBuilt { get; private set; }
    public int HousesBuilt { get; private set; }
    public int SheltersCollapsed { get; private set; }

    /// <summary>Food taken from someone else's store — see <see cref="RaidStore"/>.</summary>
    public int StoreRaids { get; private set; }

    private void InitializeTwigs()
    {
        foreach (GardenProp prop in GardenProps)
        {
            if (prop.Kind == GardenPropKind.Twig)
                _twigPatches.Add(prop.Position);
        }

        for (int i = 0; i < TwigPoolCapacity; i++)
            Twigs.Add(new Twig());
        for (int i = 0; i < InitialTwigs; i++)
            ActivateTwig(RandomTwigSpot());
    }

    private void ActivateTwig(Vector3 position)
    {
        foreach (Twig twig in Twigs)
        {
            if (!twig.IsActive)
            {
                twig.Activate(position, (float)(Rng.NextDouble() * MathF.Tau));
                return;
            }
        }
    }

    private Vector3 RandomTwigSpot()
    {
        if (_twigPatches.Count > 0 && Rng.NextDouble() < TwigPatchChance)
        {
            Vector3 anchor = _twigPatches[Rng.Next(_twigPatches.Count)];
            float angle = (float)(Rng.NextDouble() * MathF.Tau);
            float radius = MathF.Sqrt((float)Rng.NextDouble()) * TwigPatchRadius;
            Vector3 spot = anchor + new Vector3(MathF.Cos(angle) * radius, 0f, MathF.Sin(angle) * radius);
            if (Terrain.Contains(spot, 1f) && !IsBlocked(spot, 0.2f))
                return spot;
        }
        return RandomFreePoint(0.3f, edgeMargin: 1f);
    }

    /// <summary>A twig falls every <see cref="TwigSpawnInterval"/> seconds while fewer than <see cref="MaxLooseTwigs"/> lie loose; loose twigs decompose after <see cref="Twig.DespawnLifespan"/>.</summary>
    private void UpdateTwigSpawn(float deltaTime)
    {
        foreach (Twig twig in Twigs)
        {
            if (!twig.IsActive || twig.IsCarried)
                continue;

            twig.DespawnTimer -= deltaTime;
            if (twig.DespawnTimer <= 0f)
            {
                twig.Deactivate(); // Decomposed: room for a fresh one somewhere else.
                continue;
            }

            if (twig.ClaimedBy is null)
                continue;

            twig.ClaimTimer += deltaTime;
            if (twig.ClaimTimer >= FoodClaimTimeoutSeconds || twig.ClaimedBy.IsDead)
            {
                twig.ClaimedBy = null;
                twig.ClaimTimer = 0f;
            }
        }

        _twigSpawnTimer -= deltaTime;
        if (_twigSpawnTimer > 0f)
            return;
        _twigSpawnTimer = TwigSpawnInterval;

        if (LooseTwigCount < MaxLooseTwigs)
            ActivateTwig(RandomTwigSpot());
    }

    private void RebuildTwigGrid()
    {
        _twigGrid.Clear();
        int loose = 0;
        foreach (Twig twig in Twigs)
        {
            if (twig.IsActive && !twig.IsCarried)
            {
                _twigGrid.Register(twig, twig.Position);
                loose++;
            }
        }
        LooseTwigCount = loose;
    }

    public static bool IsAvailable(Twig twig, Bramblekin claimant) =>
        twig.IsActive && !twig.IsCarried &&
        (twig.ClaimedBy is null || twig.ClaimedBy == claimant || twig.ClaimedBy.IsDead);

    /// <summary>The nearest loose twig within <paramref name="radius"/> of <paramref name="from"/> that <paramref name="claimant"/> may take.</summary>
    public Twig? NearestAvailableTwig(Vector3 from, float radius, Bramblekin claimant)
    {
        _twigGrid.QueryRadius(from, radius, _twigQueryBuffer);
        Twig? best = null;
        float bestDistanceSquared = radius * radius;
        foreach (Twig twig in _twigQueryBuffer)
        {
            if (!IsAvailable(twig, claimant))
                continue;

            float distanceSquared = GroundMover.HorizontalDistanceSquared(from, twig.Position);
            if (distanceSquared <= bestDistanceSquared)
            {
                best = twig;
                bestDistanceSquared = distanceSquared;
            }
        }
        return best;
    }

    public static void PickUpTwig(Twig twig)
    {
        twig.IsCarried = true;
        twig.ClaimedBy = null;
        twig.ClaimTimer = 0f;
    }

    public static void DropTwig(Twig twig, Vector3 position)
    {
        twig.Position = Grounded(position);
        twig.IsCarried = false;
    }

    /// <summary>A builder adds its carried twig to <paramref name="shelter"/>'s current construction stage.</summary>
    public void DeliverTwig(Bramblekin builder, Shelter shelter, Twig twig)
    {
        twig.Deactivate();
        if (!shelter.AddTwig())
            return;

        builder.AddReputation(0.5f);
        float buildTime = ElapsedSeconds - shelter.StageStartedAt;
        shelter.StageStartedAt = ElapsedSeconds;
        if (shelter.Tier == ShelterTier.House)
        {
            _houseUpgradeSeconds += buildTime;
        }
        else
        {
            _tentBuildSeconds += buildTime;
        }
        if (shelter.Tier == ShelterTier.House)
        {
            HousesBuilt++;
            QueueFloatingText(shelter.Position, "House built!", new Color(170, 120, 70, 255));
            KinGroup? group = shelter.GroupId is { } id && _groups.TryGetValue(id, out KinGroup? g) ? g : null;
            Game.AddEventLog(group is null
                ? $"[SETTLE] {builder.Name} finished a House"
                : $"[SETTLE] {group.CapitalTitle} finished a House");
        }
        else
        {
            TentsBuilt++;
            QueueFloatingText(shelter.Position, "Tent built", new Color(200, 180, 120, 255));
            Game.AddEventLog(shelter.GroupId is null
                ? $"[SETTLE] {builder.Name} built a Tent"
                : $"[SETTLE] {builder.Name} finished its group's Tent");
        }
    }

    // --- Sites & ownership ---------------------------------------------------------------

    /// <summary>
    /// Marks out a new construction site within <paramref name="searchRadius"/>
    /// (default <see cref="ShelterSiteSearchRadius"/>) of <paramref name="near"/>:
    /// on open ground, inside the map, and at least <see cref="MinShelterSpacing"/>
    /// from every other shelter. Returns null if nowhere nearby qualifies.
    /// </summary>
    public Shelter? TryCreateShelterSite(Vector3 near, Bramblekin? owner, Guid? groupId, float searchRadius = ShelterSiteSearchRadius)
    {
        // Of the open spots it tries, the one nearest water: nobody wants a long walk for a drink.
        Vector3? best = null;
        float bestToWater = float.MaxValue;
        for (int attempt = 0; attempt < 16; attempt++)
        {
            float angle = (float)(Rng.NextDouble() * MathF.Tau);
            float radius = attempt == 0 ? 0f : (float)Rng.NextDouble() * searchRadius;
            Vector3 candidate = near + new Vector3(MathF.Cos(angle) * radius, 0f, MathF.Sin(angle) * radius);
            if (!Terrain.Contains(candidate, 3f) || IsBlocked(candidate, Shelter.HouseRadius + 0.3f))
                continue;
            if (Shelters.Any(s => GroundMover.HorizontalDistanceSquared(s.Position, candidate) < MinShelterSpacing * MinShelterSpacing))
                continue;
            float toWater = WaterMap.UsualDistanceToWater(candidate.X, candidate.Z);
            if (toWater < bestToWater)
            {
                best = candidate;
                bestToWater = toWater;
            }
        }
        if (best is not { } site)
            return null;

        var shelter = new Shelter(site) { Owner = owner, GroupId = groupId, StageStartedAt = ElapsedSeconds };
        // A loner on a hillside may dig in instead — an introvert most likely.
        if (groupId is null && owner is not null && IsBurrowGround(site) && Rng.NextDouble() < BurrowChance * (1f - owner.Personality.Sociability))
        {
            shelter.MakeBurrow();
            BurrowsDug++;
        }
        Shelters.Add(shelter);
        return shelter;
    }

    /// <summary>Ground this steep (rise per meter) is a hillside a burrow can be dug into…</summary>
    private const float BurrowSlope = 0.15f;

    /// <summary>…and a loner settling there digs one with odds this × (1 − its Sociability).</summary>
    private const float BurrowChance = 0.8f;

    /// <summary>Burrows marked out.</summary>
    public int BurrowsDug { get; private set; }

    /// <summary>A hillside above the reach of any flood — somewhere to dig in.</summary>
    private static bool IsBurrowGround(Vector3 site)
    {
        const float step = 0.5f;
        float dx = (GetHeightAt(site.X + step, site.Z) - GetHeightAt(site.X - step, site.Z)) / (2f * step);
        float dz = (GetHeightAt(site.X, site.Z + step) - GetHeightAt(site.X, site.Z - step)) / (2f * step);
        return MathF.Sqrt(dx * dx + dz * dz) >= BurrowSlope && GetHeightAt(site.X, site.Z) > FloodHeights.Peak + 0.3f;
    }

    /// <summary>The nearest built, abandoned shelter within <paramref name="radius"/> — free for the taking.</summary>
    public Shelter? NearestAbandonedShelter(Vector3 from, float radius)
    {
        Shelter? best = null;
        float bestDistanceSquared = radius * radius;
        foreach (Shelter shelter in Shelters)
        {
            if (!shelter.IsBuilt || !shelter.IsAbandoned || shelter.IsCollapsed)
                continue;

            float distanceSquared = GroundMover.HorizontalDistanceSquared(from, shelter.Position);
            if (distanceSquared <= bestDistanceSquared)
            {
                best = shelter;
                bestDistanceSquared = distanceSquared;
            }
        }
        return best;
    }

    /// <summary>A homeless Bramblekin moves into an abandoned shelter and makes it its own.</summary>
    public void ClaimShelter(Shelter shelter, Bramblekin kin)
    {
        shelter.Owner = kin;
        shelter.GroupId = null;
        shelter.AbandonedSeconds = 0f;
        QueueFloatingText(shelter.Position, "Moved in", new Color(200, 180, 120, 255));
    }

    /// <summary>Its owner is gone: the shelter is abandoned (and starts counting toward collapse).</summary>
    public static void Abandon(Shelter shelter)
    {
        shelter.Owner = null;
        shelter.GroupId = null;
    }

    /// <summary>
    /// Shelter upkeep: a shelter whose owner died or whose group dissolved is
    /// abandoned; one abandoned for <see cref="Shelter.AbandonedCollapseSeconds"/>
    /// (or an abandoned site nobody ever finished) collapses, spilling its
    /// store as loose Food.
    /// </summary>
    private void UpdateShelters(float deltaTime)
    {
        for (int i = Shelters.Count - 1; i >= 0; i--)
        {
            Shelter shelter = Shelters[i];
            if (shelter.Owner is { IsDead: true } || (shelter.Owner is { } owner && owner.Home != shelter))
                shelter.Owner = null;
            if (shelter.GroupId is { } groupId && !_groups.ContainsKey(groupId))
                shelter.GroupId = null;

            if (!shelter.IsAbandoned)
            {
                shelter.AbandonedSeconds = 0f;
                continue;
            }

            shelter.AbandonedSeconds += deltaTime;
            float collapseAfter = shelter.IsBuilt ? Shelter.AbandonedCollapseSeconds : Shelter.AbandonedCollapseSeconds / 3f;
            if (shelter.AbandonedSeconds < collapseAfter)
                continue;

            int spilled = shelter.Collapse();
            if (spilled > 0)
                ScatterFoodAround(shelter.Position, spilled, shelter.Radius + 0.3f, FoodShardKind.Berry);
            Shelters.RemoveAt(i);
            if (shelter.IsBuilt)
                SheltersCollapsed++;
        }
    }

    // --- The food store ---------------------------------------------------------------------

    /// <summary>Puts a Bramblekin's carried food into <paramref name="shelter"/>'s store, where it never rots — or, seed, into the clan's seed corn while it's short (see <see cref="TryKeepSeedCorn"/>). Returns false if the store is full.</summary>
    public bool DepositFood(Shelter shelter, FoodShard food)
    {
        if (TryKeepSeedCorn(shelter, food))
            return true;
        if (!shelter.TryDeposit())
            return false;
        _foodByKind[(int)food.Kind]++;
        StoreHoneyExtra(shelter, food);
        food.Deactivate();
        return true;
    }

    /// <summary>Takes one piece of Food out of <paramref name="shelter"/>'s store, already in hand (carried). Null if the store is empty.</summary>
    public FoodShard? WithdrawFood(Shelter shelter)
    {
        if (!shelter.TryWithdraw())
            return null;

        FoodShard? food = ActivateFood(shelter.Position, FoodShardKind.Berry);
        if (food is null)
            return null; // Pool exhausted: practically unreachable.
        PickUpFood(food);
        return food;
    }

    /// <summary>
    /// A raid: <paramref name="raider"/> takes one piece of Food from a
    /// store that isn't its own (or its group's). Taking from an abandoned
    /// shelter is just scavenging; taking from anyone else's makes the
    /// raider an Enemy of everyone who lives there.
    /// </summary>
    public FoodShard? RaidStore(Bramblekin raider, Shelter shelter)
    {
        FoodShard? food = WithdrawFood(shelter);
        if (food is null || shelter.IsAbandoned)
            return food;

        StoreRaids++;
        foreach (Bramblekin victim in ResidentsOf(shelter))
            DeclareEnemies(raider, victim);
        AddGrievance(raider.GroupId, shelter.GroupId, RaidGrievance);
        QueueFloatingText(shelter.Position, "Raided!", new Color(210, 50, 40, 255));
        // A war raid is announced once, when the party sets out (see DecideGroupGoal).
        if (!AreAtWar(raider.GroupId, shelter.GroupId))
        {
            string whose = shelter.GroupId is { } owners && _groups.TryGetValue(owners, out KinGroup? victims) ? $"{victims.Title}'s" : $"{shelter.Owner?.Name}'s";
            Game.AddEventLog($"[RAID] {raider.Name} raided {whose} {shelter.Tier} store");
        }
        return food;
    }

    /// <summary>
    /// The nearest built shelter within <paramref name="radius"/> with Food
    /// in its store that doesn't belong to <paramref name="kin"/> or its
    /// group — only abandoned ones when <paramref name="abandonedOnly"/>.
    /// </summary>
    public Shelter? NearestForeignStore(Bramblekin kin, float radius, bool abandonedOnly)
    {
        Shelter? best = null;
        float bestDistanceSquared = radius * radius;
        foreach (Shelter shelter in Shelters)
        {
            if (!shelter.IsBuilt || shelter.StoredFood <= 0 || shelter == kin.Home)
                continue;
            if (kin.GroupId is not null && shelter.GroupId == kin.GroupId)
                continue;
            if (abandonedOnly && !shelter.IsAbandoned)
                continue;
            if (shelter.IsBurrow && !shelter.IsAbandoned)
                continue; // A lived-in burrow's store is tucked away where a raider won't find it.
            float distanceSquared = GroundMover.HorizontalDistanceSquared(kin.Position, shelter.Position);
            if (distanceSquared > bestDistanceSquared)
                continue;
            // Nobody raids a home its own parent, child or sibling lives in.
            if (!shelter.IsAbandoned && HasCloseKinLivingIn(shelter, kin))
                continue;

            best = shelter;
            bestDistanceSquared = distanceSquared;
        }
        return best;
    }

    /// <summary>Bookkeeping hook for a meal taken from a store.</summary>
    public void NoteAteFromStore(Bramblekin kin, Shelter home)
    {
        StoreMeals++;
        kin.NoteAteFromStore();
    }

    /// <summary>Meals eaten from a store rather than off the ground.</summary>
    public int StoreMeals { get; private set; }

    /// <summary>Everyone who calls <paramref name="shelter"/> home.</summary>
    /// <summary>True if a parent, child or sibling of <paramref name="kin"/> lives in <paramref name="shelter"/>.</summary>
    private bool HasCloseKinLivingIn(Shelter shelter, Bramblekin kin)
    {
        foreach (Bramblekin resident in Colony)
        {
            if (!resident.IsDead && resident.Home == shelter && resident.IsCloseKinOf(kin))
                return true;
        }
        return false;
    }

    public IEnumerable<Bramblekin> ResidentsOf(Shelter shelter)
    {
        foreach (Bramblekin kin in Colony)
        {
            if (!kin.IsDead && kin.Home == shelter)
                yield return kin;
        }
    }
}
