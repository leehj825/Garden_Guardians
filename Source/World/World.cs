using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// Owns the simulation state — terrain, loose Food, wildlife and every
/// Bramblekin — and steps it in a fixed order. There are no factions and no
/// shared economy: the World only spawns things, resolves what happens when
/// two Bramblekin cross paths (see <see cref="ResolveEncounter"/>), and
/// keeps each emergent group's membership and leader up to date. Every
/// decision about what to actually do lives in each Bramblekin's own
/// <see cref="Bramblekin.Update"/>.
///
///   Food-claim timeouts -> spatial grids -> group bookkeeping -> Hornets ->
///   Grubs -> Bramblekin -> Wolf Spider -> encounters -> spawners (berries,
///   wildlife, arriving wanderers) -> food despawn -> timed effects.
/// </summary>
public sealed partial class World
{
    /// <summary>
    /// The Terrain Height Function: procedural rolling-hills elevation at
    /// any (x, z) ground coordinate, via a stacked sine/cosine formula.
    /// Deterministic and stateless — the same (x, z) always yields the same
    /// height, so it can be called freely from rendering, spawning and
    /// grounding code alike without ever needing to be cached.
    /// </summary>
    public static float GetHeightAt(float x, float z)
    {
        if (!float.IsFinite(x) || !float.IsFinite(z))
            return 0f;

        return MathF.Sin(x * 0.1f) * 2.0f + MathF.Cos(z * 0.1f) * 2.0f + MathF.Sin((x + z) * 0.05f) * 1.5f;
    }

    /// <summary>
    /// Surface-Normal Tilting: the terrain's outward surface normal at
    /// (x, z), found via finite differences — sampling
    /// <see cref="GetHeightAt"/> a small step to either side on both axes
    /// and using the resulting slope to build a normalized normal vector.
    /// Used to tilt bodies and props (see <see cref="Bramblekin.Draw"/> and
    /// <see cref="GardenProp.Draw"/>) so they sit flush on a hillside
    /// instead of just being lifted straight up out of it.
    /// </summary>
    public static Vector3 GetNormalAt(float x, float z)
    {
        const float offset = 0.1f;
        float L = GetHeightAt(x - offset, z);
        float R = GetHeightAt(x + offset, z);
        float B = GetHeightAt(x, z - offset);
        float F = GetHeightAt(x, z + offset);
        return Vector3.Normalize(new Vector3(L - R, 2.0f * offset, B - F));
    }

    /// <summary>Grounding Entities: snaps <paramref name="position"/>'s Y onto the terrain's height at its (x, z).</summary>
    public static Vector3 Grounded(Vector3 position) => new(position.X, GetHeightAt(position.X, position.Z), position.Z);

    /// <summary>
    /// Same terrain snap as <see cref="Grounded(Vector3)"/>, but with an
    /// extra vertical <paramref name="yOffset"/> added on top — for
    /// overlays (rings, lines) that must float just above a slope instead
    /// of clipping into it.
    /// </summary>
    public static Vector3 Grounded(Vector3 position, float yOffset) =>
        new(position.X, GetHeightAt(position.X, position.Z) + yOffset, position.Z);

    // --- Food ------------------------------------------------------------------

    /// <summary>Object Pooling: fixed number of Food slots, constructed once and reused — see <see cref="ActivateFood"/>.</summary>
    private const int FoodPoolCapacity = 400;

    /// <summary>Berries already on the ground when the world is created, so the first arrivals have something to find.</summary>
    private const int InitialBerries = 45;

    /// <summary>Passive Foraging: seconds between wild Berry spawns in a normal season — divided by the season's abundance (see <see cref="AbundanceOf"/>).</summary>
    public const float BerrySpawnInterval = 0.6f;

    /// <summary>Wild Berries stop spawning once this many are on the ground in a normal season — scaled by the season's abundance.</summary>
    public const int MaxBerries = 100;

    /// <summary>
    /// Berry Patches: how many fixed spots (preferably Dandelions) most
    /// Berries grow around. Clustering the food gives Bramblekin a reason to
    /// converge on the same places — which is where encounters, alliances
    /// and robberies happen.
    /// </summary>
    private const int BerryPatchCount = 8;

    /// <summary>How far (m) from its patch's anchor a patch Berry may grow.</summary>
    private const float BerryPatchRadius = 4f;

    /// <summary>Odds a new Berry grows in a patch rather than at a random spot anywhere on the map.</summary>
    private const double BerryPatchChance = 0.65;

    /// <summary>
    /// Dibs failsafe: a Food claim is force-released after this many
    /// seconds, so a claimant that's stuck never locks everyone else out.
    /// </summary>
    public const float FoodClaimTimeoutSeconds = 15f;

    // --- Wildlife ----------------------------------------------------------------

    /// <summary>Seconds after the Wolf Spider is slain before a new one moves in.</summary>
    public const float SpiderRespawnDelay = 60f;

    /// <summary>A new Wolf Spider never spawns within this many meters of a living Bramblekin.</summary>
    private const float MinSpiderSpawnDistanceFromKin = 15f;

    /// <summary>Food scattered where a slain Wolf Spider falls — the reward for a group that brings one down.</summary>
    private const int SpiderCarcassFood = 6;

    private const float SplatDuration = 6f;

    public const int MaxHornetsOnMap = 12;

    public const int HornetSwarmMinSize = 3, HornetSwarmMaxSize = 5;

    /// <summary>Seconds between checks that top the Hornet population back up toward <see cref="MaxHornetsOnMap"/>, one whole swarm at a time.</summary>
    public const float HornetSpawnInterval = 10f;

    public const int MaxGrubsOnMap = 4;

    /// <summary>Seconds between checks that top the Grub population back up toward <see cref="MaxGrubsOnMap"/>.</summary>
    public const float GrubSpawnInterval = 20f;

    // --- Bramblekin population ------------------------------------------------------

    /// <summary>
    /// A safety limit, for performance only: no births or arrivals past this
    /// many living Bramblekin. In practice the Food supply keeps the
    /// population well below it.
    /// </summary>
    public const int MaxPopulation = 150;

    /// <summary>Wandering Arrivals only come while fewer than this many Bramblekin are alive — once the world is busy, growth has to come from births.</summary>
    public const int ArrivalPopulationLimit = 30;

    /// <summary>
    /// Wandering Arrivals: seconds between new solitary Bramblekin drifting
    /// in from the map's edge while the world is sparse (below
    /// <see cref="ArrivalPopulationLimit"/>) — so a harsh stretch thins the
    /// population out without ever ending the simulation for good. Beyond
    /// that, the population only grows by births.
    /// </summary>
    public const float ArrivalInterval = 15f;

    /// <summary>How close (m) a tap has to land to a Bramblekin to select it for the Kin Inspector.</summary>
    public const float KinSelectionRadius = 2f;

    // --- Encounters & groups ------------------------------------------------------

    /// <summary>Two Bramblekin closer than this (m) have "crossed paths" — see <see cref="ResolveEncounter"/>.</summary>
    public const float EncounterRadius = 1.2f;

    /// <summary>The same pair of Bramblekin can't resolve another encounter until this many seconds have passed.</summary>
    public const float EncounterCooldown = 12f;

    /// <summary>A group never grows past this many members, by joining or by merging.</summary>
    public const int MaxGroupSize = 6;

    /// <summary>How big <paramref name="group"/> may grow, by joining or by births.</summary>
    public int GroupSizeLimit(KinGroup group) => MaxGroupSize;

    /// <summary>Every home <paramref name="group"/> has.</summary>
    public IEnumerable<Shelter> GroupHomes(KinGroup group)
    {
        if (group.Home is { IsCollapsed: false } home)
            yield return home;
    }

    /// <summary>Two Bramblekin both at least this Sociable band together on meeting — see <see cref="ResolveEncounter"/>.</summary>
    public const float AllianceSociabilityThreshold = 0.6f;

    /// <summary>Only a Bramblekin at least this Aggressive will turn on another for its food — see <see cref="TryStartRobbery"/>.</summary>
    public const float HighAggressionThreshold = 0.55f;

    public const float FloatingTextDuration = 1.5f;

    /// <summary>Oversized Garden Props: how many static decorations to scatter across the map.</summary>
    private const int GardenPropCount = 40;

    private static readonly Color FriendlyTextColor = new(60, 170, 80, 255);
    private static readonly Color HostileTextColor = new(210, 50, 40, 255);

    private readonly List<Obstacle> _obstacles = new();
    private readonly List<(Vector3 Position, float TimeLeft)> _splats = new();
    private readonly List<(Vector3 Position, string Text, Color Color, float TimeLeft)> _floatingTexts = new();
    private readonly List<Vector3> _berryPatches = new();

    // Deferred spawns/removals, applied once per frame in CommitPendingChanges.
    private readonly List<Bramblekin> _pendingKinSpawns = new();
    private readonly List<Bramblekin> _pendingKinRemovals = new();
    private readonly List<(Vector3 Position, FoodShardKind Kind)> _pendingFoodSpawns = new();
    private readonly List<Hornet> _pendingHornetSpawns = new();
    private readonly List<Hornet> _pendingHornetRemovals = new();
    private readonly List<Grub> _pendingGrubSpawns = new();
    private readonly List<Grub> _pendingGrubRemovals = new();

    // The Spatial Grid, plus one scratch buffer per kind of query so an
    // outer query's results are never clobbered by an unrelated inner one.
    private readonly SpatialGrid<FoodShard> _foodGrid = new();
    private readonly SpatialGrid<Bramblekin> _colonyGrid = new();
    private readonly List<FoodShard> _foodQueryBuffer = new();
    private readonly List<Bramblekin> _colonyQueryBuffer = new();
    private readonly List<Bramblekin> _kinPerceptionBuffer = new();
    private readonly List<Bramblekin> _encounterBuffer = new();

    private readonly Dictionary<Guid, KinGroup> _groups = new();
    private readonly List<Guid> _groupRemovalBuffer = new();

    /// <summary>When each pair of Bramblekin (lower ID first) last resolved an encounter — see <see cref="EncounterCooldown"/>.</summary>
    private readonly Dictionary<(int, int), float> _lastEncounter = new();
    private readonly List<(int, int)> _encounterExpiryBuffer = new();

    private float _berrySpawnTimer = BerrySpawnInterval;
    private float _hornetSpawnTimer = HornetSpawnInterval;
    private float _grubSpawnTimer = GrubSpawnInterval;
    private float _arrivalTimer = ArrivalInterval;
    private float _encounterCleanupTimer = 30f;

    public Terrain Terrain { get; }
    public Random Rng { get; }

    /// <summary>Game time simulated so far, in seconds (the sum of every Update's deltaTime).</summary>
    public float ElapsedSeconds { get; private set; }

    /// <summary>Every Bramblekin on the map. A dead one lingers (IsDead) until the end of the frame — see <see cref="CommitPendingChanges"/>.</summary>
    public List<Bramblekin> Colony { get; } = new();

    /// <summary>Object Pooling: the fixed pool of Food slots; only the <see cref="FoodShard.IsActive"/> ones are real.</summary>
    public List<FoodShard> FoodShards { get; } = new();

    public WolfSpider? Spider { get; private set; }
    public List<Hornet> Hornets { get; } = new();
    public List<Grub> Grubs { get; } = new();
    public List<GardenProp> GardenProps { get; } = new();

    /// <summary>Every group with at least two living members, keyed by <see cref="Bramblekin.GroupId"/>.</summary>
    public IReadOnlyCollection<KinGroup> Groups => _groups.Values;

    /// <summary>The Bramblekin shown in the Kin Inspector panel, if any — see <see cref="TrySelectKinAt"/>.</summary>
    public Bramblekin? SelectedKin { get; private set; }

    public IReadOnlyList<Obstacle> Obstacles => _obstacles;
    public IReadOnlyList<(Vector3 Position, string Text, Color Color, float TimeLeft)> FloatingTexts => _floatingTexts;

    /// <summary>Loose (active, uncarried) Food on the map, as of the start of this frame.</summary>
    public int LooseFoodCount { get; private set; }

    public float SpiderRespawnTimer { get; private set; }

    // --- Running tallies (HUD / headless reports) -------------------------------------
    public int Arrivals { get; private set; }
    public int DeathsByStarvation { get; private set; }
    public int DeathsByPredator { get; private set; }
    public int DeathsByKin { get; private set; }
    public int Casualties => DeathsByStarvation + DeathsByPredator + DeathsByKin;
    public int FoodEaten { get; private set; }
    public int FoodShared { get; private set; }
    public int Thefts { get; private set; }
    public int AlliancesFormed { get; private set; }
    public int GrubsKilled { get; private set; }
    public int HornetsKilled { get; private set; }
    public int SpidersKilled { get; private set; }

    public World(Terrain terrain, Random rng, int initialKinCount)
    {
        Terrain = terrain;
        Rng = rng;

        SpawnGardenProps();
        RebuildObstacles();
        PickBerryPatches();

        // Object Pooling: every Food slot is constructed once here
        // (inactive) rather than instantiated and destroyed per
        // spawn/pickup/despawn — see ActivateFood.
        for (int i = 0; i < FoodPoolCapacity; i++)
            FoodShards.Add(new FoodShard());
        for (int i = 0; i < InitialBerries; i++)
            ActivateFood(RandomBerrySpot(), FoodShardKind.Berry);
        InitializeTwigs();

        // Every starting Bramblekin is solitary, with its own freshly
        // rolled Personality (see the Bramblekin constructor) — groups only
        // ever form later, out of encounters.
        for (int i = 0; i < initialKinCount; i++)
            Colony.Add(new Bramblekin(RandomFreePoint(Bramblekin.BodyRadius, Bramblekin.EdgeMargin), rng));

        SpawnSpider();
        RebuildSpatialGrids(); // So LooseFoodCount is right before the first Update.
    }

    // --- Setup -----------------------------------------------------------------

    /// <summary>
    /// Oversized Garden Props: scatters <see cref="GardenPropCount"/>
    /// Pebbles/Twigs/Dandelions randomly across the 100x100 map. Every
    /// prop's Y is snapped onto the terrain the instant it's placed.
    /// </summary>
    private void SpawnGardenProps()
    {
        for (int i = 0; i < GardenPropCount; i++)
        {
            Vector3 candidate = Terrain.RandomPoint(Rng, margin: 1f);
            var kind = (GardenPropKind)Rng.Next(3);
            float rotation = (float)(Rng.NextDouble() * MathF.Tau);
            GardenProps.Add(new GardenProp(Grounded(candidate), kind, rotation, Rng));
        }
    }

    /// <summary>Large Pebbles are the only solid things on the map; built once, since props never move.</summary>
    private void RebuildObstacles()
    {
        _obstacles.Clear();
        foreach (GardenProp prop in GardenProps)
        {
            if (prop.FootprintRadius > 0f)
                _obstacles.Add(new Obstacle(new Vector2(prop.Position.X, prop.Position.Z), prop.FootprintRadius));
        }
    }

    /// <summary>Berry Patches: anchors on Dandelions first (a flowerbed reads naturally as a berry patch), then random open ground.</summary>
    private void PickBerryPatches()
    {
        foreach (GardenProp prop in GardenProps)
        {
            if (_berryPatches.Count >= BerryPatchCount)
                break;
            if (prop.Kind == GardenPropKind.Dandelion)
                _berryPatches.Add(prop.Position);
        }

        while (_berryPatches.Count < BerryPatchCount)
            _berryPatches.Add(RandomFreePoint(BerryPatchRadius * 0.5f, edgeMargin: BerryPatchRadius + 1f));
    }

    /// <summary>Spawns a Wolf Spider somewhere open, at least <see cref="MinSpiderSpawnDistanceFromKin"/> meters from every living Bramblekin.</summary>
    private void SpawnSpider()
    {
        Vector3 position = Vector3.Zero;
        for (int attempt = 0; attempt < 30; attempt++)
        {
            position = Terrain.RandomPoint(Rng, margin: 1.5f);
            if (!IsBlocked(position, WolfSpider.BodyRadius) &&
                Colony.All(b => b.IsDead || GroundMover.HorizontalDistance(position, b.Position) >= MinSpiderSpawnDistanceFromKin))
                break;
        }

        Spider = new WolfSpider(position, Rng);
    }

    // --- Object Pooling ----------------------------------------------------------

    /// <summary>Activates the first inactive slot in <see cref="FoodShards"/> at <paramref name="position"/> and returns it, or returns null if the pool is exhausted.</summary>
    private FoodShard? ActivateFood(Vector3 position, FoodShardKind kind)
    {
        foreach (FoodShard food in FoodShards)
        {
            if (!food.IsActive)
            {
                food.Activate(position, kind);
                return food;
            }
        }
        return null;
    }

    // --- The frame -------------------------------------------------------------------

    public void Update(float deltaTime)
    {
        ElapsedSeconds += deltaTime;
        UpdateSeason();
        AccumulateExposure(deltaTime);
        UpdateFoodClaimTimeouts(deltaTime);
        RebuildSpatialGrids();
        RebuildGroups();
        UpdateGroupHomes(deltaTime);
        UpdateGroupDecisions(deltaTime);
        CountShelterOccupants();

        // Wildlife moves before the colony reacts to it this frame. Reverse
        // for-loops: a Bramblekin's strike (below) can kill a Hornet or Grub,
        // which marks it dead but defers the actual list removal.
        for (int i = Hornets.Count - 1; i >= 0; i--)
            Hornets[i].Update(deltaTime, this);

        for (int i = Grubs.Count - 1; i >= 0; i--)
            Grubs[i].Update(deltaTime, this);

        for (int i = Beetles.Count - 1; i >= 0; i--)
            Beetles[i].Update(deltaTime, this);

        // Reverse for-loop: a Bramblekin's own Update() can kill another
        // (combat, robbery) — World.Kill only queues the removal, but
        // walking backwards keeps this loop correct even if that changes.
        for (int i = Colony.Count - 1; i >= 0; i--)
            Colony[i].Update(deltaTime, this);

        if (Spider is { IsDead: false } spider)
            spider.Update(deltaTime, this);

        ResolveEncounters();

        UpdateShelters(deltaTime);
        UpdateBerrySpawn(deltaTime);
        UpdateTwigSpawn(deltaTime);
        UpdateSpiderRespawn(deltaTime);
        UpdateHornetSpawn(deltaTime);
        UpdateGrubSpawn(deltaTime);
        UpdateBeetleSpawn(deltaTime);
        UpdateArrivals(deltaTime);
        UpdateFoodDespawn(deltaTime);
        UpdateEncounterCleanup(deltaTime);

        for (int i = _splats.Count - 1; i >= 0; i--)
        {
            var splat = _splats[i];
            splat.TimeLeft -= deltaTime;
            if (splat.TimeLeft <= 0f)
                _splats.RemoveAt(i);
            else
                _splats[i] = splat;
        }

        for (int i = _floatingTexts.Count - 1; i >= 0; i--)
        {
            var text = _floatingTexts[i];
            text.TimeLeft -= deltaTime;
            if (text.TimeLeft <= 0f)
                _floatingTexts.RemoveAt(i);
            else
                _floatingTexts[i] = text;
        }
    }

    /// <summary>
    /// Applies every entity spawned or removed this frame. Called once, at
    /// the very end of the frame after Update() and Draw() have both run, so
    /// nothing is ever adding to or removing from an entity list while
    /// something else might still be iterating it.
    /// </summary>
    public void CommitPendingChanges()
    {
        if (_pendingKinRemovals.Count > 0)
        {
            foreach (Bramblekin dead in _pendingKinRemovals)
            {
                Colony.Remove(dead);
                if (SelectedKin == dead)
                    SelectedKin = null;
            }

            // IDs are never reused, so a dead Bramblekin's entry in everyone
            // else's KnownKins is just clutter from here on.
            foreach (Bramblekin kin in Colony)
            {
                foreach (Bramblekin dead in _pendingKinRemovals)
                    kin.ForgetKin(dead.ID);
            }
            _pendingKinRemovals.Clear();
        }

        if (_pendingKinSpawns.Count > 0)
        {
            Colony.AddRange(_pendingKinSpawns);
            _pendingKinSpawns.Clear();
        }

        if (_pendingFoodSpawns.Count > 0)
        {
            foreach (var (position, kind) in _pendingFoodSpawns)
                ActivateFood(position, kind);
            _pendingFoodSpawns.Clear();
        }

        if (_pendingHornetRemovals.Count > 0)
        {
            foreach (Hornet hornet in _pendingHornetRemovals)
                Hornets.Remove(hornet);
            _pendingHornetRemovals.Clear();
        }

        if (_pendingHornetSpawns.Count > 0)
        {
            Hornets.AddRange(_pendingHornetSpawns);
            _pendingHornetSpawns.Clear();
        }

        if (_pendingGrubRemovals.Count > 0)
        {
            foreach (Grub grub in _pendingGrubRemovals)
                Grubs.Remove(grub);
            _pendingGrubRemovals.Clear();
        }

        if (_pendingGrubSpawns.Count > 0)
        {
            Grubs.AddRange(_pendingGrubSpawns);
            _pendingGrubSpawns.Clear();
        }

        CommitBeetleChanges();
    }

    /// <summary>The Spatial Grid: every loose Food and every living Bramblekin, re-registered into its current 10m chunk. Rebuilt fresh once a frame rather than tracked incrementally as each entity moves.</summary>
    private void RebuildSpatialGrids()
    {
        _foodGrid.Clear();
        int looseFood = 0;
        foreach (FoodShard food in FoodShards)
        {
            if (food.IsActive && !food.IsCarried)
            {
                _foodGrid.Register(food, food.Position);
                looseFood++;
            }
        }
        LooseFoodCount = looseFood;

        _colonyGrid.Clear();
        foreach (Bramblekin bramblekin in Colony)
        {
            if (!bramblekin.IsDead)
                _colonyGrid.Register(bramblekin, bramblekin.Position);
        }

        RebuildTwigGrid();
    }

    // --- Queries used by the AI ------------------------------------------------------

    /// <summary>True if a round body of <paramref name="clearance"/> radius at <paramref name="point"/> would overlap an obstacle.</summary>
    public bool IsBlocked(Vector3 point, float clearance)
    {
        var p = new Vector2(point.X, point.Z);
        foreach (var obstacle in _obstacles)
        {
            float reach = obstacle.Radius + clearance;
            if (Vector2.DistanceSquared(p, obstacle.Center) < reach * reach)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Dibs: Food can be taken if it's loose, and it isn't claimed by a
    /// different living Bramblekin actively walking to it. A null
    /// <paramref name="claimant"/> (a Grub) ignores claims altogether —
    /// Grubs don't respect anyone's dibs.
    /// </summary>
    public bool IsAvailable(FoodShard food, Bramblekin? claimant) =>
        food.IsActive && !food.IsCarried &&
        (claimant is null || food.ClaimedBy is null || food.ClaimedBy == claimant || food.ClaimedBy.IsDead);

    /// <summary>The nearest available Food within <paramref name="radius"/> of <paramref name="from"/>, if any.</summary>
    public FoodShard? NearestAvailableFood(Vector3 from, float radius, Bramblekin? claimant)
    {
        _foodGrid.QueryRadius(from, radius, _foodQueryBuffer);
        FoodShard? best = null;
        float bestDistanceSquared = radius * radius;
        for (int i = 0; i < _foodQueryBuffer.Count; i++)
        {
            FoodShard food = _foodQueryBuffer[i];
            if (!IsAvailable(food, claimant))
                continue;

            float distanceSquared = GroundMover.HorizontalDistanceSquared(from, food.Position);
            if (distanceSquared <= bestDistanceSquared)
            {
                best = food;
                bestDistanceSquared = distanceSquared;
            }
        }
        return best;
    }

    /// <summary>The nearest living Grub within <paramref name="radius"/> of <paramref name="from"/>, if any.</summary>
    public Grub? NearestLiveGrub(Vector3 from, float radius)
    {
        Grub? best = null;
        float bestDistanceSquared = radius * radius;
        foreach (Grub grub in Grubs)
        {
            if (grub.IsDead)
                continue;

            float distanceSquared = GroundMover.HorizontalDistanceSquared(from, grub.Position);
            if (distanceSquared <= bestDistanceSquared)
            {
                best = grub;
                bestDistanceSquared = distanceSquared;
            }
        }
        return best;
    }

    /// <summary>The nearest living Bramblekin within <paramref name="radius"/> (at most <see cref="SpatialGrid{T}.ChunkSize"/>) of <paramref name="from"/>, if any.</summary>
    public Bramblekin? NearestLivingKinWithin(Vector3 from, float radius)
    {
        Bramblekin? best = null;
        float bestDistanceSquared = radius * radius;
        List<Bramblekin> nearby = QueryNearbyColony(from);
        for (int i = 0; i < nearby.Count; i++)
        {
            Bramblekin kin = nearby[i];
            if (kin.IsDead)
                continue;

            float distanceSquared = GroundMover.HorizontalDistanceSquared(from, kin.Position);
            if (distanceSquared <= bestDistanceSquared)
            {
                best = kin;
                bestDistanceSquared = distanceSquared;
            }
        }
        return best;
    }

    /// <summary>
    /// The Spatial Grid: every living Bramblekin registered within 10m
    /// chunks of <paramref name="position"/> (its own chunk plus the 8
    /// neighbors) — used by the Wolf Spider's prey search, a Hornet's aggro
    /// check and a Grub's skittishness. The returned list is a reused
    /// scratch buffer: safe to iterate immediately, but don't hold onto it
    /// past the call that reads it.
    /// </summary>
    public List<Bramblekin> QueryNearbyColony(Vector3 position)
    {
        _colonyGrid.QueryNearby(position, _colonyQueryBuffer);
        return _colonyQueryBuffer;
    }

    /// <summary>
    /// Every Bramblekin in the chunks overlapping <paramref name="radius"/>
    /// of <paramref name="position"/> (a superset — distance-check the
    /// results). Used for a Bramblekin's own Intelligence-scaled perception;
    /// same reused-scratch-buffer caveat as <see cref="QueryNearbyColony"/>,
    /// but a separate buffer, so the two never clobber each other.
    /// </summary>
    public List<Bramblekin> QueryColonyWithin(Vector3 position, float radius)
    {
        _colonyGrid.QueryRadius(position, radius, _kinPerceptionBuffer);
        return _kinPerceptionBuffer;
    }

    /// <summary>A uniformly random unblocked ground point, keeping <paramref name="edgeMargin"/> meters from the edges.</summary>
    public Vector3 RandomFreePoint(float clearance, float edgeMargin)
    {
        Vector3 candidate = Vector3.Zero;
        for (int attempt = 0; attempt < 30; attempt++)
        {
            candidate = Terrain.RandomPoint(Rng, edgeMargin);
            if (!IsBlocked(candidate, clearance))
                return candidate;
        }
        return candidate; // Practically unreachable: obstacles cover a tiny fraction of the map.
    }

    /// <summary>A random unblocked spot along one of the map's four edges — where Grubs burrow in and wandering Bramblekin arrive.</summary>
    private Vector3 RandomEdgeSpot(float clearance, float edgeMargin)
    {
        float half = Terrain.Size / 2f - edgeMargin;
        Vector3 candidate = new(-half, 0f, 0f);
        for (int attempt = 0; attempt < 20; attempt++)
        {
            float along = (float)(Rng.NextDouble() * 2.0 - 1.0) * half;
            candidate = Rng.Next(4) switch
            {
                0 => new Vector3(-half, 0f, along),
                1 => new Vector3(half, 0f, along),
                2 => new Vector3(along, 0f, -half),
                _ => new Vector3(along, 0f, half),
            };
            if (!IsBlocked(candidate, clearance))
                return candidate;
        }
        return candidate;
    }

    /// <summary>Kin Inspector: selects the living Bramblekin nearest <paramref name="groundPoint"/> within <see cref="KinSelectionRadius"/>, or clears the selection on a tap at empty ground.</summary>
    public void TrySelectKinAt(Vector3 groundPoint)
    {
        Bramblekin? best = null;
        float bestDistanceSquared = KinSelectionRadius * KinSelectionRadius;
        foreach (Bramblekin kin in Colony)
        {
            if (kin.IsDead)
                continue;

            float distanceSquared = GroundMover.HorizontalDistanceSquared(groundPoint, kin.Position);
            if (distanceSquared <= bestDistanceSquared)
            {
                best = kin;
                bestDistanceSquared = distanceSquared;
            }
        }
        SelectedKin = best;
    }
}
