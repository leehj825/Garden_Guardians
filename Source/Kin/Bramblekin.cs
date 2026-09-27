using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// An individual survival agent. Each Bramblekin is born solitary with a
/// random <see cref="Personality"/> and, every frame, serves exactly one
/// need, in strict priority order (a leadership duel, once started, comes
/// before all of them):
///
///   1. Critical — Hunger (Bramblekin.Hunger.cs): once <see cref="IsHungry"/>,
///      it eats what it's carrying, forages visible Food, eats from its
///      home's store, scavenges, hunts, raids or robs, or searches further
///      afield. Nothing else matters until it's fed — a hungry Bramblekin
///      will brave a Hornet swarm for a berry.
///   2. Safety (Bramblekin.Safety.cs): a predator, a raider, a hostile
///      Bramblekin, or anything attacking a groupmate inside its
///      Intelligence-scaled <see cref="DetectionRadius"/> triggers one
///      Aggression roll per threat — fight, flee, or hide at home.
///   3. Duty (Bramblekin.Duty.cs): a loyal group member does the job its
///      Leader gave it.
///   4. Settle (Bramblekin.Settle.cs): build a home, rest in it when hurt,
///      and stock its store.
///   5. Social (Bramblekin.Social.cs): fed and safe, it wanders (around
///      home, if it has one), and — depending on Sociability — seeks out
///      strangers or keeps its distance; a homeless group follows its
///      Leader.
/// Loyalty, Reputation and leadership duels live in Bramblekin.Loyalty.cs.
/// </summary>
public sealed partial class Bramblekin : ICombatant
{
    private static int _nextId = 0;

    /// <summary>A stable, never-reused identity — what other Bramblekin remember it by in their <see cref="KnownKins"/>.</summary>
    public int ID { get; } = _nextId++;

    // --- Body --------------------------------------------------------------------

    /// <summary>Normal walking speed in m/s.</summary>
    public const float WalkSpeed = 1.5f;

    /// <summary>Flee speed as a multiple of <see cref="WalkSpeed"/> — outruns a Hornet, not a pouncing spider.</summary>
    public const float FleeSpeedMultiplier = 2.2f;

    /// <summary>How long a Bramblekin rests between moves, in seconds (randomized ±50%).</summary>
    public const float PauseDuration = 2f;

    /// <summary>Collision radius in meters: used against Pebbles and other obstacles.</summary>
    public const float BodyRadius = 0.25f;

    /// <summary>Total body height in meters.</summary>
    public const float BodyHeight = 0.9f;

    /// <summary>How far from the terrain edge targets are kept, in meters.</summary>
    public const float EdgeMargin = 0.5f;

    public const int MaxHealth = 30;

    /// <summary>
    /// Cached Body Model: a single cylinder <see cref="Model"/> reused by
    /// every Bramblekin's <see cref="Draw"/> call via
    /// <see cref="Raylib.DrawModelEx"/>, instead of each unit calling
    /// <see cref="Raylib.DrawCylinder"/>/<see cref="Raylib.DrawCapsule"/>
    /// every frame — those immediate-mode calls regenerate their vertex
    /// geometry on the CPU on every single call, which is the real cost at
    /// hundreds of units; a cached <see cref="Model"/>'s mesh is built once
    /// and only re-uploaded to the GPU as a transform, not rebuilt. Lazily
    /// built on first use (not eagerly in a static initializer) so it can
    /// never run before <see cref="Raylib.InitWindow"/> has created a GPU
    /// context — building/uploading a Mesh before that would crash.
    /// </summary>
    private static Model _bodyModel;

    private static bool _bodyModelReady;

    /// <summary>
    /// Builds <see cref="_bodyModel"/> the first time any Bramblekin draws.
    /// A plain cylinder — this raylib-cs build has no GenMeshCapsule — sized
    /// to <see cref="BodyRadius"/>/<see cref="BodyHeight"/>.
    /// </summary>
    private static void EnsureBodyModel()
    {
        if (_bodyModelReady)
            return;

        Mesh mesh = Raylib.GenMeshCylinder(BodyRadius, BodyHeight, 8);
        _bodyModel = Raylib.LoadModelFromMesh(mesh);
        _bodyModelReady = true;
    }

    // --- Metabolism ----------------------------------------------------------------

    public const float MaxHunger = 100f;

    /// <summary>Hunger gained per second — a full belly lasts well under two minutes.</summary>
    public const float HungerPerSecond = 1f;

    /// <summary>At or above this, Hunger is Critical and overrides every other need.</summary>
    public const float HungryThreshold = 60f;

    /// <summary>At or above this, a highly Aggressive Bramblekin may rob whoever it runs into.</summary>
    public const float StarvingThreshold = 80f;

    /// <summary>Hunger removed by eating one piece of Food.</summary>
    private const float FoodNourishment = 40f;

    /// <summary>Health restored by eating one piece of Food — the only way to heal.</summary>
    private const int FoodHealing = 6;

    private const float EatDuration = 1.5f;

    /// <summary>At full Hunger, one point of damage every this many seconds until it eats or dies.</summary>
    private const float StarvationDamageInterval = 1f;

    /// <summary>New Bramblekin arrive with a random Hunger between 0 and this.</summary>
    private const float StartingHungerMax = 40f;

    // --- Senses --------------------------------------------------------------------

    /// <summary>Detection radius (m) at Intelligence 0.</summary>
    public const float BaseDetectionRadius = 5f;

    /// <summary>Extra detection radius (m) at Intelligence 1 — a genius sees 20m, a dullard 5m.</summary>
    public const float DetectionRadiusPerIntelligence = 15f;

    /// <summary>
    /// Seconds between perception scans (food, Grubs, threats). Scans are
    /// staggered per Bramblekin, so the whole colony never scans on the
    /// same frame.
    /// </summary>
    private const float PerceptionInterval = 0.25f;

    /// <summary>A threat or target is let go once it's this many detection radii away.</summary>
    private const float ThreatLeashMultiplier = 1.3f;

    /// <summary>Whoever last hit this Bramblekin stays its top threat for this many seconds.</summary>
    private const float RecentAttackWindow = 4f;

    // --- Combat --------------------------------------------------------------------

    /// <summary>Strike reach (m) beyond both bodies' edges.</summary>
    private const float StrikeReach = 0.35f;

    private const float StrikeCooldownDuration = 1f;

    /// <summary>Strike damage is this, plus up to <see cref="StrikeDamagePerAggression"/> more for a fully Aggressive Bramblekin.</summary>
    private const int BaseStrikeDamage = 5;

    private const float StrikeDamagePerAggression = 6f;

    /// <summary>Chasing speed (fights, robberies) as a multiple of <see cref="WalkSpeed"/>.</summary>
    private const float PursuitSpeedMultiplier = 1.3f;

    /// <summary>At or below this fraction of <see cref="MaxHealth"/>, a fighter's nerve breaks and it flees instead — high enough that a fighter at the threshold can still survive one more Wolf Spider bite.</summary>
    private const float FightBreakHealthFraction = 0.4f;

    /// <summary>Keeps running for at least this long after losing sight of whatever it fled from.</summary>
    private const float FleeMinDuration = 2.5f;

    /// <summary>Groupmates within this many meters embolden a fight-or-flight roll by <see cref="AllySupportBonus"/> each.</summary>
    private const float AllySupportRadius = 6f;

    private const float AllySupportBonus = 0.15f;

    /// <summary>Group Dynamics: added to the fight roll when the threat is attacking a groupmate.</summary>
    private const float GroupDefenseBonus = 0.3f;

    /// <summary>The Wolf Spider is scarier than a Hornet: subtracted from the fight roll.</summary>
    private const float SpiderFearPenalty = 0.25f;

    // --- Social --------------------------------------------------------------------

    /// <summary>How close (m) it must get to Food to pick it up.</summary>
    private const float PickupDistance = 0.5f;

    /// <summary>How far (m) a solitary Bramblekin or a Leader wanders per move; searching for food ranges twice as far.</summary>
    private const float WanderRadius = 12f;

    /// <summary>Leaders amble a little slower so their followers can keep up.</summary>
    private const float LeaderWanderSpeedMultiplier = 0.8f;

    /// <summary>A follower tries to stay within this many meters of its Leader.</summary>
    private const float FollowRadius = 3f;

    /// <summary>A follower that has fallen more than twice <see cref="FollowRadius"/> behind hurries at this multiple of <see cref="WalkSpeed"/>.</summary>
    private const float FollowCatchUpSpeedMultiplier = 1.4f;

    /// <summary>After each rest, odds of going to meet a stranger are Sociability times this.</summary>
    private const float SocialSeekFactor = 0.8f;

    /// <summary>A Bramblekin less Sociable than this walks away from anyone inside its <see cref="PersonalSpaceRadius"/>.</summary>
    private const float LonerThreshold = 0.35f;

    private const float PersonalSpaceRadius = 4f;

    /// <summary>A fed, empty-handed Bramblekin pockets visible Food within this fraction of its detection radius as a reserve.</summary>
    private const float ReserveGrabRadiusFraction = 0.5f;

    /// <summary>Gives up on reaching a stranger after this many seconds.</summary>
    private const float SocializeTimeout = 15f;

    private static readonly Color CalmColor = new(196, 160, 110, 255);       // Bark brown.
    private static readonly Color AggressiveColor = new(150, 60, 45, 255);   // Thorny red-brown, blended in by Aggression.
    private static readonly Color PanicColor = new(225, 85, 60, 255);        // Alarm red.
    private static readonly Color SolitaryHeadColor = new(235, 235, 225, 255);
    private static readonly Color ThornColor = new(120, 55, 40, 255);
    private static readonly Color BloodyThornColor = new(200, 30, 30, 255);
    private static readonly Color BannerPoleColor = new(120, 90, 50, 255);

    private readonly Random _rng;
    private readonly GroundMover _mover;
    private readonly Dictionary<int, RelationshipState> _knownKins = new();

    private Vector3 _wanderTarget;
    private Vector3 _lastThreatPosition;

    /// <summary>Where it last saw Food — the first place it looks when hungry and nothing's in sight.</summary>
    private Vector3? _foodMemory;
    private float _pauseTimer;
    private float _eatTimer;
    private float _strikeCooldown;
    private float _starvationTimer;
    private float _perceptionTimer;
    private float _fleeTimer;
    private float _socializeTimer;
    private float _lastHitTime = float.NegativeInfinity;

    private FoodShard? _carried;
    private FoodShard? _claimedFood;
    private Twig? _carriedTwig;
    private Twig? _claimedTwig;

    /// <summary>Where it last saw a loose twig — where it looks first when it needs building material.</summary>
    private Vector3? _twigMemory;

    /// <summary>Seconds alive — a newcomer looks around for a while before it settles.</summary>
    private float _age;

    private float _restTimer;
    private Bramblekin? _robTarget;
    private Bramblekin? _companion;
    private ICombatant? _lastAttacker;

    // Perception results, refreshed every PerceptionInterval.
    private FoodShard? _perceivedFood;
    private Grub? _perceivedGrub;
    private Twig? _perceivedTwig;
    private ICombatant? _perceivedThreat;
    private bool _threatIsAllyDefense;

    // Safety: the threat the current fight-or-flight roll was made against.
    private ICombatant? _respondingTo;
    private bool _fightDecision;

    public Bramblekin(Vector3 position, Random rng)
    {
        _rng = rng;
        Personality = Personality.Roll(rng);
        Hunger = (float)rng.NextDouble() * StartingHungerMax;
        _mover = new GroundMover(position, BodyRadius, EdgeMargin, rng);
        _perceptionTimer = (float)rng.NextDouble() * PerceptionInterval;

        // Start mid-pause with a random timer so the colony doesn't move in lockstep.
        StartPause();
        _pauseTimer = (float)rng.NextDouble() * PauseDuration;
    }

    public Personality Personality { get; }

    /// <summary>The group this Bramblekin has joined, or null while solitary.</summary>
    public Guid? GroupId { get; private set; }

    /// <summary>
    /// Where it lives: its own Tent while solitary, or its group's home. May
    /// still be a construction site (<see cref="Shelter.IsBuilt"/> false).
    /// </summary>
    public Shelter? Home { get; private set; }

    /// <summary>True while it's standing inside its own finished home — safe from the Wolf Spider's pounce and from Hornets — unless the home is overcrowded.</summary>
    public bool IsSheltered => IsInsideHome && !Home!.IsOvercrowded;

    /// <summary>True while it's standing inside its own finished home.</summary>
    public bool IsInsideHome => Home is { IsBuilt: true } home && home.Contains(Position);

    /// <summary>Where it last saw Food, if anywhere.</summary>
    public Vector3? FoodMemory => _foodMemory;

    /// <summary>True while it's holding a twig for building.</summary>
    public bool HasTwig => _carriedTwig is not null;
    /// <summary>Every Bramblekin it has met (by <see cref="ID"/>) and how it regards them.</summary>
    public IReadOnlyDictionary<int, RelationshipState> KnownKins => _knownKins;

    /// <summary>Terrain-aware: Y is snapped to World.GetHeightAt every read.</summary>
    public Vector3 Position => World.Grounded(_mover.Position);

    public BramblekinState State { get; private set; }

    public int Health { get; private set; } = MaxHealth;

    /// <summary>0 (full) to <see cref="MaxHunger"/> (starving to death).</summary>
    public float Hunger { get; private set; }

    public bool IsDead { get; private set; }

    /// <summary>Whatever it's currently fighting, robbing or hunting — other Bramblekin read this to tell who's attacking whom.</summary>
    public ICombatant? CombatTarget { get; private set; }

    public float CollisionRadius => BodyRadius;

    /// <summary>True while it's holding a piece of Food (a reserve, or a meal about to be eaten).</summary>
    public bool HasFood => _carried is not null;

    public bool IsHungry => Hunger >= HungryThreshold;

    public bool IsStarving => Hunger >= StarvingThreshold;

    public bool IsRobbing => _robTarget is not null;

    /// <summary>True once it has left (or been thrown out of) a group — see <see cref="Status"/>.</summary>
    public bool HasLeftGroup { get; private set; }

    /// <summary>Where it stands socially right now, for survival statistics.</summary>
    public SurvivalStatus Status =>
        GroupId is not null ? SurvivalStatus.Member
        : HasLeftGroup ? SurvivalStatus.Independent
        : Home is { IsBuilt: true } ? SurvivalStatus.Homesteader
        : SurvivalStatus.Wanderer;

    /// <summary>Intelligence-scaled radius (m) for spotting food, threats and other Bramblekin.</summary>
    public float DetectionRadius => BaseDetectionRadius + DetectionRadiusPerIntelligence * Personality.Intelligence;

    /// <summary>True if it can currently see a living Wolf Spider or Hornet — see <see cref="World.ResolveEncounter"/>'s Alliance rule.</summary>
    public bool IsThreatenedByPredator => _perceivedThreat is { IsDead: false } threat && threat is WolfSpider or Hornet;

    /// <summary>True if it can currently see loose Food it could take — a starving Bramblekin that can doesn't need to rob anyone.</summary>
    public bool SeesFood => _perceivedFood is { IsActive: true, IsCarried: false };

    /// <summary>The Wolf Spider hunts by vibration: a Bramblekin busy with food (or a fight over it) gives itself away.</summary>
    public bool IsVibrating => !IsDead && State is BramblekinState.Foraging or BramblekinState.Eating or BramblekinState.Hunting
        or BramblekinState.Attacking or BramblekinState.Raiding;

    private int StrikeDamage => BaseStrikeDamage + (int)MathF.Round(StrikeDamagePerAggression * Personality.Aggression);

    // --- Relationships & groups ----------------------------------------------------------

    /// <summary>How this Bramblekin regards <paramref name="other"/>, or null if they've never met.</summary>
    public RelationshipState? RelationshipTo(Bramblekin other) =>
        _knownKins.TryGetValue(other.ID, out RelationshipState state) ? state : null;

    /// <summary>Records how it regards <paramref name="other"/>. Enemy is permanent — nothing overwrites it.</summary>
    public void SetRelationship(Bramblekin other, RelationshipState state)
    {
        if (_knownKins.TryGetValue(other.ID, out RelationshipState current) && current == RelationshipState.Enemy)
            return;
        _knownKins[other.ID] = state;
    }

    /// <summary>Drops a dead Bramblekin from <see cref="KnownKins"/> — see <see cref="World.CommitPendingChanges"/>.</summary>
    public void ForgetKin(int id) => _knownKins.Remove(id);

    /// <summary>Joins a group, starting out as loyal as it is Sociable.</summary>
    public void JoinGroup(Guid groupId)
    {
        GroupId = groupId;
        Loyalty = LoyaltyBaseline;
    }

    /// <summary>Splinter: leaves its group, along with other unhappy members, for a new one of their own — homeless, but keen.</summary>
    public void SplitOff(Guid newGroupId)
    {
        if (GroupId is { } former)
            _formerGroups.Add(former);
        LeaveGroup();
        Home = null;
        HasLeftGroup = true;
        JoinGroup(newGroupId);
        Loyalty = 0.7f;
    }

    public void LeaveGroup()
    {
        GroupId = null;
        Job = KinJob.None;
    }

    /// <summary>Set whenever its group's sharing rule turned it away from the store.</summary>
    private bool _deniedFood;

    public void NoteDeniedFood() => _deniedFood = true;

    /// <summary>Hostility: commits to attacking <paramref name="victim"/> until its food is stolen, it gets away, or this Bramblekin eats.</summary>
    public void BeginRobbery(Bramblekin victim) => _robTarget = victim;

    /// <summary>Hands over whatever food it's holding (to a thief or a hungry friend), interrupting a meal in progress.</summary>
    public FoodShard? SurrenderFood()
    {
        FoodShard? food = _carried;
        _carried = null;
        if (State == BramblekinState.Eating)
            StartPause();
        return food;
    }

    /// <summary>Takes <paramref name="food"/> in hand (stolen or shared). Callers only hand food to an empty-handed Bramblekin.</summary>
    public void ReceiveFood(FoodShard food)
    {
        _carried = food;
        _perceivedFood = null;

        // A robbery ends the moment it pays off.
        _robTarget = null;
        if (CombatTarget is Bramblekin)
            CombatTarget = null;
    }

    // --- Damage & death --------------------------------------------------------------------

    /// <summary>A strike from another Bramblekin — see <see cref="ICombatant"/>.</summary>
    public void TakeHit(int damage, Bramblekin attacker, World world) =>
        TakeDamage(damage, world, DeathCause.Kin, attacker);

    /// <summary>
    /// Reduces Health and, at 0, dies via <see cref="World.Kill"/>. Any hit
    /// with a <paramref name="source"/> makes that source its top threat for
    /// <see cref="RecentAttackWindow"/> seconds (and its groupmates' — see
    /// <see cref="Perceive"/>); a hit from another Bramblekin also makes the
    /// two Enemies for good.
    /// </summary>
    public void TakeDamage(int amount, World world, DeathCause cause, ICombatant? source)
    {
        if (IsDead)
            return;

        Health = Math.Max(0, Health - amount);

        // A leadership duel is a contest, not a feud: no lingering threat, no enmity.
        if (source is not null && ReferenceEquals(source, _duelOpponent))
        {
            if (Health <= 0)
                world.Kill(this, cause, source);
            return;
        }

        if (source is not null)
        {
            _lastAttacker = source;
            _lastHitTime = world.ElapsedSeconds;
            _perceivedThreat = source;
            _threatIsAllyDefense = false;
        }
        if (source is Bramblekin attacker)
            world.DeclareEnemies(this, attacker);

        if (Health <= 0)
            world.Kill(this, cause, source);
    }

    /// <summary>
    /// Marks this Bramblekin dead: drops any food it was holding right where
    /// it fell (still edible) and releases its Food claim. Called once, from
    /// <see cref="World.Kill"/>; the removal from <see cref="World.Colony"/>
    /// is deferred to the end of the frame.
    /// </summary>
    public void MarkDead()
    {
        if (IsDead)
            return;

        if (_carried is not null)
        {
            World.DropFood(_carried, Position);
            _carried = null;
        }
        if (_carriedTwig is not null)
        {
            World.DropTwig(_carriedTwig, Position);
            _carriedTwig = null;
        }
        ReleaseFoodClaim();
        ReleaseTwigClaim();
        _robTarget = null;
        _companion = null;
        CombatTarget = null;
        IsDead = true;
    }

    /// <summary>Whoever hit this Bramblekin within the last <see cref="RecentAttackWindow"/> seconds, if it's still alive.</summary>
    public ICombatant? RecentAttacker(World world) =>
        _lastAttacker is { IsDead: false } attacker && world.ElapsedSeconds - _lastHitTime <= RecentAttackWindow ? attacker : null;

    // --- The survival loop --------------------------------------------------------------------

    public void Update(float deltaTime, World world)
    {
        if (IsDead)
            return;

        _mover.Idle();
        _strikeCooldown = MathF.Max(0f, _strikeCooldown - deltaTime);
        _age += deltaTime;
        if (Home is { IsCollapsed: true })
            Home = null;

        // Metabolism: Hunger always rises; at the very top it starts costing Health.
        Hunger = MathF.Min(MaxHunger, Hunger + HungerPerSecond * deltaTime);
        if (Hunger >= MaxHunger)
        {
            _starvationTimer += deltaTime;
            if (_starvationTimer >= StarvationDamageInterval)
            {
                _starvationTimer -= StarvationDamageInterval;
                TakeDamage(1, world, DeathCause.Starvation, source: null);
                if (IsDead)
                    return;
            }
        }
        else
        {
            _starvationTimer = 0f;
        }

        _perceptionTimer -= deltaTime;
        if (_perceptionTimer <= 0f)
        {
            _perceptionTimer += PerceptionInterval;
            Perceive(world);
        }

        // 0) A leadership duel, once started, is settled before anything else.
        if (UpdateDuel(deltaTime, world))
            return;

        // 1) Critical: Hunger. A meal already under way is always finished.
        if (IsHungry || State == BramblekinState.Eating)
        {
            _fleeTimer = 0f; // Whatever it was running from, food comes first now.
            UpdateHunger(deltaTime, world);
            return;
        }
        _robTarget = null; // Fed again: no reason left to rob anyone.

        // 2) Safety.
        if (UpdateSafety(deltaTime, world))
            return;

        // 3) Duty: the job its group's Leader gave it.
        if (UpdateDuty(deltaTime, world))
            return;

        // 4) Settle: build a home, stock its store, rest up in it.
        if (UpdateSettle(deltaTime, world))
            return;

        // 5) Social.
        UpdateSocial(deltaTime, world);
    }

    /// <summary>
    /// Perception, scaled by Intelligence: the nearest available Food and
    /// Grub within <see cref="DetectionRadius"/>, and the most pressing
    /// threat — whoever just hit it, else the nearest of: the Wolf Spider,
    /// any Hornet, any Bramblekin attacking it, or (Group Dynamics) whatever
    /// is attacking or fighting one of its groupmates.
    /// </summary>
    private void Perceive(World world)
    {
        float radius = DetectionRadius;
        _perceivedFood = world.NearestAvailableFood(Position, radius, this);
        if (_perceivedFood is not null)
            _foodMemory = _perceivedFood.Position;
        _perceivedGrub = world.NearestLiveGrub(Position, radius);
        _perceivedBeetle = world.NearestLiveBeetle(Position, radius);
        _perceivedTwig = NeedsTwig ? world.NearestAvailableTwig(Position, radius, this) : null;
        if (_perceivedTwig is not null)
            _twigMemory = _perceivedTwig.Position;

        float leash = radius * ThreatLeashMultiplier;
        if (RecentAttacker(world) is { } attacker &&
            GroundMover.HorizontalDistanceSquared(Position, attacker.Position) <= leash * leash)
        {
            _perceivedThreat = attacker;
            _threatIsAllyDefense = false;
            return;
        }

        ICombatant? best = null;
        bool bestIsAllyDefense = false;
        float bestDistanceSquared = radius * radius;

        void Consider(ICombatant candidate, bool allyDefense)
        {
            float distanceSquared = GroundMover.HorizontalDistanceSquared(Position, candidate.Position);
            if (distanceSquared <= bestDistanceSquared)
            {
                best = candidate;
                bestIsAllyDefense = allyDefense;
                bestDistanceSquared = distanceSquared;
            }
        }

        if (world.Spider is { IsDead: false } spider)
            Consider(spider, allyDefense: false);
        foreach (Hornet hornet in world.Hornets)
        {
            if (!hornet.IsDead)
                Consider(hornet, allyDefense: false);
        }

        List<Bramblekin> nearby = world.QueryColonyWithin(Position, radius);
        for (int i = 0; i < nearby.Count; i++)
        {
            Bramblekin other = nearby[i];
            if (other == this || other.IsDead)
                continue;

            if (ReferenceEquals(other.CombatTarget, this))
            {
                Consider(other, allyDefense: false);
                continue;
            }

            // Fight to protect: a raider heading for its home.
            if (Home is not null && other.RaidTarget == Home)
            {
                Consider(other, allyDefense: true);
                continue;
            }

            if (GroupId is null || other.GroupId != GroupId)
                continue;

            // Group Dynamics: a groupmate under attack, or already
            // fighting, pulls its foe into this Bramblekin's sights too.
            ICombatant? allyFoe = other.RecentAttacker(world) ??
                                  (other.State == BramblekinState.Fighting ? other.CombatTarget : null);
            if (allyFoe is { IsDead: false } && !ReferenceEquals(allyFoe, this) &&
                !(allyFoe is Bramblekin foeKin && foeKin.GroupId == GroupId))
                Consider(allyFoe, allyDefense: true);
        }

        _perceivedThreat = best;
        _threatIsAllyDefense = bestIsAllyDefense;
    }
}
