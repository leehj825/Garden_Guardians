using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// An individual survival agent. Each Bramblekin is born solitary with a
/// random <see cref="Personality"/> and, every frame, serves exactly one
/// need, in strict priority order (a leadership duel, once started, comes
/// before all of them):
///
///   1. Critical — Thirst (Bramblekin.Thirst.cs) and Hunger
///      (Bramblekin.Hunger.cs), whichever is worse: once <see cref="IsThirsty"/>
///      it walks to the pond (or its home's cistern) and drinks; once
///      <see cref="IsHungry"/> it eats what it's carrying, forages visible
///      Food, eats from its home's store, scavenges, hunts, raids or robs,
///      or searches further afield. Nothing else matters until it's fed
///      and watered — a hungry Bramblekin will brave a Hornet swarm for a berry.
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
    public int ID { get; private set; } = _nextId++;

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
    public const float BodyHeight = 1.0f;

    /// <summary>How far from the terrain edge targets are kept, in meters.</summary>
    public const float EdgeMargin = 0.5f;

    public const int MaxHealth = 30;

    /// <summary>
    /// This Bramblekin's own pose of the shared rig — see
    /// <see cref="BramblekinModel.CreatePoseInstance"/>: the mesh and
    /// texture are shared with every other Bramblekin, but the bone matrices
    /// are its own, so it can be mid-stride at a different frame than the
    /// one next to it. Built lazily on first draw (see <see cref="Draw"/>)
    /// so it never runs before <see cref="Raylib.InitWindow(int, int, string)"/> has created a
    /// GPU context, and released in <see cref="MarkDead"/> since it owns
    /// unmanaged memory the garbage collector won't reclaim on its own.
    /// </summary>
    private Model _animModel;

    private bool _animModelReady;

    /// <summary>Seconds into whichever clip <see cref="BramblekinModel.ClipFor"/> currently picks — see <see cref="Draw"/>.</summary>
    private float _animTime;

    private void EnsureAnimModel()
    {
        if (_animModelReady)
            return;

        _animModel = BramblekinModel.CreatePoseInstance(Sex);
        _animModelReady = true;
    }

    // --- Metabolism ----------------------------------------------------------------

    public const float MaxHunger = 100f;

    /// <summary>Hunger gained per second — a full belly lasts well under two minutes.</summary>
    public const float HungerPerSecond = 1f;

    /// <summary>Huddled inside its home in winter (see <see cref="IsSheltered"/>), Hunger rises at this fraction of the usual rate.</summary>
    public const float WinterShelterMetabolism = 0.5f;

    /// <summary>A burrow's earthen walls keep in more warmth: wintering in one, Hunger rises at this fraction.</summary>
    public const float BurrowWinterMetabolism = 0.4f;

    /// <summary>At or above this, Hunger is Critical and overrides every other need.</summary>
    public const float HungryThreshold = 60f;

    /// <summary>At or above this, a highly Aggressive Bramblekin may rob whoever it runs into.</summary>
    public const float StarvingThreshold = 80f;

    /// <summary>Hunger removed by eating one piece of Food.</summary>
    private const float FoodNourishment = 40f;

    /// <summary>Pieces of food one kin eats a second, at the usual pace (a village's rations are worked out from it).</summary>
    public const float MealsPerSecond = HungerPerSecond / FoodNourishment;

    /// <summary>True while its village feeds it for its job (see World.Rations): it eats from the village's stores rather than foraging.</summary>
    public bool IsPaid { get; set; }

    /// <summary>A job its village's headman gave it (a soldier, healer, builder or scout, fed from the village's stores); it takes over from the clan's own say in what it does. None when it has none. Not saved: the headman re-gives them.</summary>
    public KinJob VillageJob { get; set; }

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

    /// <summary>At war, only a resident at least this Aggressive goes after an enemy passer-by…</summary>
    private const float WarIntruderAggression = 0.5f;

    /// <summary>…and only one this close (m) to home.</summary>
    private const float WarIntruderRadius = 6f;

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

    /// <summary>At or below this fraction of <see cref="HealthCap"/>, a fighter's nerve breaks and it flees instead — high enough that a fighter at the threshold can still survive one more Wolf Spider bite.</summary>
    private const float FightBreakHealthFraction = 0.4f;

    /// <summary>At work — gathering, building, stocking, farming, carrying for its group — the diligent go briskly and the idle slowly.</summary>
    private float WorkPace => State is BramblekinState.Collecting or BramblekinState.Building or BramblekinState.Stockpiling or
        BramblekinState.Farming or BramblekinState.Traveling or BramblekinState.Fishing or BramblekinState.Healing
        ? (0.85f + 0.3f * Personality.Diligence) * SkillPace * (Knows(Craft.Tools) ? ToolPaceBonus : 1f)
        : 1f;

    /// <summary>A clan with tools (see <see cref="Craft.Tools"/>) works this much faster.</summary>
    private const float ToolPaceBonus = 1.25f;

    /// <summary>Its nerve breaks at this fraction of its Health: lower for the brave, higher for the cautious (<see cref="FightBreakHealthFraction"/> for the middling).</summary>
    private float NerveBreaksAt => FightBreakHealthFraction * (1.4f - 0.8f * Personality.Courage);

    /// <summary>
    /// True once its nerve breaks against <paramref name="foe"/>: at or
    /// below <see cref="NerveBreaksAt"/> of its Health — or, however brave,
    /// once one more of the foe's blows could kill it. Courage holds a
    /// fighter in longer, never into a blow it can't survive.
    /// </summary>
    private bool NerveBroken(ICombatant? foe) =>
        Health <= HealthCap * NerveBreaksAt || (foe is not null && Health <= HardestBlow(foe));

    /// <summary>The most one blow from <paramref name="foe"/> takes off.</summary>
    private static int HardestBlow(ICombatant foe) => foe switch
    {
        WolfSpider => WolfSpider.BiteDamage,
        StagBeetle => StagBeetle.BiteDamage,
        Hornet => Hornet.BiteDamage,
        Ant => Ant.BiteDamage,
        InvaderSpider => InvaderSpider.BiteDamage,
        HillGuard => HillGuard.BiteDamage,
        Bramblekin kin => kin.StrikeDamage,
        _ => 0,
    };

    /// <summary>A fighter whose nerve breaks backs away still braced — the Wolf Spider can't pounce on it — for this long, times (0.5 + Courage).</summary>
    private const float GuardedRetreatSeconds = 2f;

    /// <summary>Seconds left of a guarded retreat (see <see cref="GuardedRetreatSeconds"/>).</summary>
    private float _guardedRetreat;

    /// <summary>Braced for the Wolf Spider — fighting it, or backing away from a fight on guard — so its pounce can't catch it.</summary>
    public bool IsBraced => State == BramblekinState.Fighting || _guardedRetreat > 0f;

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

    private static readonly Color AggressiveColor = new(150, 60, 45, 255);   // Thorny red-brown, blended in by Aggression.
    private static readonly Color PanicColor = new(225, 85, 60, 255);        // Alarm red.
    private static readonly Color BannerPoleColor = new(120, 90, 50, 255);

    private readonly Random _rng;
    private readonly GroundMover _mover;
    private readonly Dictionary<int, RelationshipState> _knownKins = new();

    private Vector3 _wanderTarget;
    private Vector3 _lastThreatPosition;

    /// <summary>Whether the threat it last ran from is one that home keeps out (wildlife) — see <see cref="FleeFrom"/>.</summary>
    private bool _lastThreatStopsAtHome;

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
    private Twig? _claimedTwig;

    /// <summary>Where it last saw a loose twig — where it looks first when it needs building material.</summary>
    private Vector3? _twigMemory;

    /// <summary>Its age in seconds — see <see cref="Age"/> (a newcomer arrives already grown).</summary>
    private float _age;

    private float _restTimer;
    private Bramblekin? _robTarget;
    private Bramblekin? _companion;
    private ICombatant? _lastAttacker;

    // Perception results, refreshed every PerceptionInterval.
    private FoodShard? _perceivedFood;
    /// <summary>Small game in sight: a Grub.</summary>
    private ICombatant? _perceivedPrey;
    private Twig? _perceivedTwig;
    private ICombatant? _perceivedThreat;
    private bool _threatIsAllyDefense;

    // Safety: the threat the current fight-or-flight roll was made against.
    private ICombatant? _respondingTo;
    private bool _fightDecision;

    /// <summary>A newcomer (or one of the first Bramblekin): a freshly rolled Personality, unless given one.</summary>
    public Bramblekin(Vector3 position, Random rng, Personality? personality = null)
    {
        _rng = rng;
        Personality = personality ?? Personality.Roll(rng);
        Sex = rng.Next(2) == 0 ? Sex.Female : Sex.Male;
        RollLifespan(rng);
        Hunger = (float)rng.NextDouble() * StartingHungerMax;
        Thirst = (float)rng.NextDouble() * StartingThirstMax;
        _mover = new GroundMover(position, BodyRadius, EdgeMargin, rng);
        _perceptionTimer = (float)rng.NextDouble() * PerceptionInterval;

        // Start mid-pause with a random timer so the colony doesn't move in lockstep.
        StartPause();
        _pauseTimer = (float)rng.NextDouble() * PauseDuration;
    }

    public Personality Personality { get; }

    /// <summary>Female or male, at even odds — see <see cref="GardenGuardians.Sex"/>. Only births care.</summary>
    public Sex Sex { get; private set; }

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
    public bool HasTwig => Pack.Count(ItemKind.Twig) > 0;

    /// <summary>Its group as World.GroupOf last found it — a lookup cache, not state (never saved).</summary>
    internal KinGroup? CachedGroup { get; set; }
    /// <summary>Every Bramblekin it has met (by <see cref="ID"/>) and how it regards them.</summary>
    public IReadOnlyDictionary<int, RelationshipState> KnownKins => _knownKins;

    /// <summary>Terrain-aware: Y is snapped to World.GetHeightAt every read.</summary>
    public Vector3 Position => _mover.GroundedPosition;

    public BramblekinState State { get; private set; }

    public int Health { get; private set; } = MaxHealth;

    /// <summary>0 (full) to <see cref="MaxHunger"/> (starving to death).</summary>
    public float Hunger { get; private set; }

    public bool IsDead { get; private set; }

    /// <summary>Whatever it's currently fighting, robbing or hunting — other Bramblekin read this to tell who's attacking whom.</summary>
    public ICombatant? CombatTarget { get; private set; }

    public float CollisionRadius => BodyRadius;

    /// <summary>True while it's holding a piece of Food (a reserve, or a meal about to be eaten) — food a thief could snatch. (An errand sack is slung tight: it's only lost if the runner is cut down.)</summary>
    public bool HasFood => _carried is not null || Pack.FoodCount > 0;

    public bool IsHungry => Hunger >= HungryThreshold;

    public bool IsStarving => Hunger >= StarvingThreshold;

    public bool IsRobbing => _robTarget is not null;

    /// <summary>True once it has left (or been thrown out of) a group — see <see cref="Status"/>.</summary>
    public bool HasLeftGroup { get; private set; }

    /// <summary>Where it stands socially right now, for survival statistics.</summary>
    public SurvivalStatus Status =>
        IsYoung ? SurvivalStatus.Young
        : GroupId is not null ? SurvivalStatus.Member
        : HasLeftGroup ? SurvivalStatus.Independent
        : Home is { IsBuilt: true } ? SurvivalStatus.Homesteader
        : SurvivalStatus.Wanderer;

    /// <summary>Intelligence-scaled radius (m) for spotting food, threats and other Bramblekin.</summary>
    public float DetectionRadius => BaseDetectionRadius + DetectionRadiusPerIntelligence * Personality.Intelligence;

    /// <summary>True if it can currently see a living Wolf Spider or Hornet — see <see cref="World.ResolveEncounter"/>'s Alliance rule.</summary>
    public bool IsThreatenedByPredator => _perceivedThreat is { IsDead: false } threat && threat is WolfSpider or Hornet or InvaderSpider or HillGuard;

    /// <summary>True if it can currently see loose Food it could take — a starving Bramblekin that can doesn't need to rob anyone.</summary>
    public bool SeesFood => _perceivedFood is { IsActive: true, IsCarried: false };

    /// <summary>The Wolf Spider hunts by vibration: a Bramblekin busy with food (or a fight over it) gives itself away.</summary>
    public bool IsVibrating => !IsDead && (State is BramblekinState.Foraging or BramblekinState.Eating or BramblekinState.Hunting
        or BramblekinState.Attacking or BramblekinState.Raiding or BramblekinState.Farming || IsDrinkingAtPond);

    /// <summary>A strong kin hits harder (±<see cref="StrikeDamagePerStrength"/>/2 around the average) and a trained soldier harder again.</summary>
    private const float StrikeDamagePerStrength = 4f;

    /// <summary>The damage of a blow against another Bramblekin (its job's share of the kin's own: see <see cref="ProfileOf"/>).</summary>
    private int StrikeDamage => (int)MathF.Round(BaseStrike * Profile.VsKin);

    /// <summary>
    /// Armed with a shield: a clan that knows <see cref="Craft.Shields"/> issues them to those whose job is fighting — Swordsmen (soldiers),
    /// Raiders and Hunters — not to everyone.
    /// </summary>
    /// <summary>Soldiers and raiders (grown) look the part: the guard model, its own sword-and-shield walk.</summary>
    public bool WearsSwordsmanKit => !IsYoung && Job is KinJob.Swordsman or KinJob.Raider;

    /// <summary>A Swordsman always carries a wooden shield on its left wrist; a Raider has one only once its clan knows <see cref="Craft.Shields"/> (beetle shell, which turns more of a blow).</summary>
    public bool HasShield => !IsYoung && (Job == KinJob.Swordsman || (Job == KinJob.Raider && Knows(Craft.Shields)));

    /// <summary>A Swordsman holds its shield up and stands in the front: it takes this much less than another shield-bearer.</summary>
    private const float SwordsmanBlockFactor = 0.85f;

    /// <summary>A strong kin shrugs off a little of every blow (±<see cref="ToughnessPerStrength"/>/2 around the average); its job adds its own measure (see <see cref="ProfileOf"/>).</summary>
    private const float ToughnessPerStrength = 0.2f;

    /// <summary>A blow against a creature: half as hard again with <see cref="Craft.Spears"/>, and up to half as hard again for a master hunter.</summary>
    private int HuntingDamage => (int)MathF.Round(BaseStrike * Profile.VsCreature * (Knows(Craft.Spears) || Job == KinJob.Spearman ? 1.5f : 1f) * (1f + 0.5f * SkillAt(Skill.Hunting)));

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
        if (IsPlayerControlled)
            return; // At the player's wheel it stands alone; it rejoins its clan when given back.
        GroupId = groupId;
        Loyalty = LoyaltyBaseline;
        _joinedAt = _timeHere;
    }

    /// <summary>When (in <see cref="_timeHere"/>) it last joined a group.</summary>
    private float _joinedAt;

    /// <summary>A newcomer to a group gives it this long (s) before it rebels, or is thrown out.</summary>
    private const float NewMemberGrace = 90f;

    /// <summary>True for a while after it joins a group — see <see cref="NewMemberGrace"/>.</summary>
    public bool IsNewMember => _timeHere - _joinedAt < NewMemberGrace;

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

    /// <summary>Budding: its House in the village sets up as a group of its own — no quarrel, so it keeps its home and holds nothing against the old group.</summary>
    public void BudOff(Guid newGroupId)
    {
        LeaveGroup();
        JoinGroup(newGroupId);
        _groupBuildSite = null;
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
    public FoodShard? SurrenderFood(World world)
    {
        FoodShard? food = _carried;
        _carried = null;
        if (food is null && Pack.FoodSlot() is { } slot && Pack.RemoveAt(slot) is { } item)
        {
            food = world.HoldFood(Position, ItemInfo.FoodOf(item));
            if (food is null)
                Pack.Add(item);
        }
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

    /// <summary>With a shield (see <see cref="Craft.Shields"/>), a blow or bite does this fraction of its damage.</summary>
    private const float ShieldFactor = 0.67f;

    /// <summary>A Swordsman's plain wooden shield (before its clan has the beetle-shell craft) takes this fraction of a blow.</summary>
    private const float WoodShieldFactor = 0.8f;

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

        // A beetle-shell shield takes the edge off every blow and bite.
        if (cause is DeathCause.Kin or DeathCause.Predator)
        {
            float factor = 1f - ToughnessPerStrength * (Strength - 0.5f);
            factor *= Profile.Taken;
            if (HasShield)
                factor *= (Knows(Craft.Shields) ? ShieldFactor : WoodShieldFactor) * (Job == KinJob.Swordsman ? SwordsmanBlockFactor : 1f);
            amount = Math.Max(1, (int)MathF.Round(amount * factor));
        }
        Health = Math.Max(0, Health - amount);

        // A leadership duel is a contest, not a feud: no lingering threat, no enmity.
        if (source is not null && ReferenceEquals(source, _duelOpponent))
        {
            if (Health <= 0)
                world.Kill(this, cause, source);
            return;
        }

        if (source is WolfSpider or Hornet or InvaderSpider)
            RememberDanger(source.Position, world);

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
        PutDownMaterial();
        ReleaseFoodClaim();
        ReleaseTwigClaim();
        _robTarget = null;
        _companion = null;
        CombatTarget = null;
        IsDead = true;

        if (_animModelReady)
        {
            BramblekinModel.DestroyPoseInstance(ref _animModel);
            _animModelReady = false;
        }
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
        _animTime += deltaTime;
        if (UpdateAging(deltaTime, world))
            return;
        UpdateFamily(deltaTime);
        RustSkills(deltaTime);
        if (Home is { IsCollapsed: true })
            Home = null;

        // Metabolism: Hunger always rises (slower huddled at home in winter); at the very top it starts costing Health.
        float metabolism = world.CurrentSeason == Season.Winter && IsSheltered
            ? Home!.IsHearthLit ? World.HearthWinterMetabolism : Home.IsBurrow ? BurrowWinterMetabolism : WinterShelterMetabolism
            : 1f;
        if (!IsSheltered)
            metabolism *= world.ColdFactor; // A harsh winter bites anyone caught outdoors.
        if (IsSick)
            metabolism *= SickHungerFactor;
        if (IsAsleep)
            metabolism *= SleepMetabolism;
        Hunger = MathF.Min(MaxHunger, Hunger + HungerPerSecond * metabolism * VigorHungerFactor * deltaTime);
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
        if (UpdateThirstMetabolism(deltaTime, world))
            return;
        UpdateBottles(deltaTime, world);
        if (UpdateSickness(deltaTime, world))
            return;
        if (_onRaft && PoleAcross(deltaTime, world))
            return; // Out on the water: nothing else can be done until it lands.
        if (_actionLock > 0f)
        {
            _actionLock -= deltaTime; // A blow or a pick-up is being played out: it is finished before the kin moves or does anything else.
            if (IsPlayerControlled)
                UpdatePlayerBlow(deltaTime, world); // (the arrow still leaves the bow partway through the clip)
            return;
        }
        if (IsPlayerControlled)
        {
            UpdatePlayerControl(deltaTime, world); // The player is at the wheel: no mind of its own.
            return;
        }

        UpdateShaken(world);
        _perceptionTimer -= deltaTime;
        if (_perceptionTimer <= 0f)
        {
            _perceptionTimer += PerceptionInterval;
            Perceive(world);
        }

        // 0) A leadership duel, once started, is settled before anything else.
        if (UpdateDuel(deltaTime, world))
            return;

        // 1) Critical: Thirst or Hunger — whichever is worse. A meal already under way is always finished.
        if (State != BramblekinState.Eating && ThirstComesFirst)
        {
            _fleeTimer = 0f; // Whatever it was running from, water comes first now.
            UpdateThirst(deltaTime, world);
            return;
        }
        if (IsHungry || State == BramblekinState.Eating)
        {
            _fleeTimer = 0f; // Whatever it was running from, food comes first now.
            UpdateHunger(deltaTime, world);
            return;
        }
        _robTarget = null; // Fed again: no reason left to rob anyone.

        // 1b) In the thick of a Kingdom's assault on the ant hill (the fight, the prize), a soldier has no mind for anything else.
        if (AssaultParty is { Phase: AssaultPhase.Fighting or AssaultPhase.Looting } && UpdateAssault(deltaTime, world))
            return;

        // 2) Safety.
        if (UpdateSafety(deltaTime, world))
            return;

        // 2a) On the road to or from the hill, a soldier answers a threat on the way (above) and otherwise keeps with the band.
        if (UpdateAssault(deltaTime, world))
            return;

        // 2b) Ants at its home's store get swatted.
        if (UpdateAntDefense(deltaTime, world))
            return;

        // 2c) A cupful of pond water goes home to the cistern.
        if (UpdateWaterCarry(deltaTime, world))
            return;

        // 2c'') A harvest feast within reach.
        if (UpdateFeast(deltaTime, world))
            return;

        // 2d) Night: bed — for all but the watch, raiders and anyone on an errand.
        if (UpdateNight(deltaTime, world))
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
    /// small game (a Grub) within
    /// <see cref="DetectionRadius"/>, and the most pressing threat — whoever
    /// just hit it, else the nearest of: the Wolf Spider, any Hornet, any Bramblekin attacking
    /// it, or (Group Dynamics) whatever is attacking or fighting one of its
    /// groupmates.
    /// </summary>
    private void Perceive(World world)
    {
        float radius = DetectionRadius;
        _perceivedFood = world.NearestAvailableFood(Position, radius, this);
        // Memory: food near a remembered danger isn't worth it (unless starving).
        if (_perceivedFood is not null && !WorthTheRisk(_perceivedFood.Position, world))
            _perceivedFood = null;
        if (_perceivedFood is not null)
        {
            _foodMemory = _perceivedFood.Position;
            world.GroupOf(this)?.FoodSpots.Remember(_perceivedFood.Position, world.ElapsedSeconds);
        }
        _perceivedPrey = world.NearestPrey(Position, radius);
        _perceivedAnt = world.Ants.Count > 0 ? world.NearestLiveAnt(Position, radius) : null;
        // Nobody chases a Grub or a thief ant into the ant hill's zone (but a Kingdom's army, which is there to fight).
        if (AssaultParty is null)
        {
            if (_perceivedPrey is not null && world.IsInAntZone(_perceivedPrey.Position, 2f))
                _perceivedPrey = null;
            if (_perceivedAnt is not null && world.IsInAntZone(_perceivedAnt.Position, 2f))
                _perceivedAnt = null;
        }
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
            if (IsAsleep && world.GroupOf(this) is { } woken)
                world.RaiseAlarm(woken, Position); // Attacked in its sleep: its cry wakes the clan.
            return;
        }

        // A watchtower's lookout spots trouble for everyone near home, calling out farther than they could see.
        if (Home is { HasWatchtower: true, IsCollapsed: false } tower &&
            GroundMover.HorizontalDistanceSquared(Position, tower.Position) <= World.TowerCoverRange * World.TowerCoverRange)
            radius += World.TowerSightBonus;

        // Asleep, only something right on top of it wakes it — and a raider creeping in, not even that.
        bool sleeping = IsAsleep && !world.IsAlarmed(world.GroupOf(this));
        if (sleeping)
            radius = MathF.Min(radius, SleepSenseRadius);

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
        foreach (InvaderSpider invader in world.Invaders)
        {
            if (!invader.IsDead)
                Consider(invader, allyDefense: false);
        }
        // The ant hill's guards, if it has come near the hill's zone.
        if (world.Anthill is { } nearHill && GroundMover.HorizontalDistanceSquared(Position, nearHill.Position) <= (Anthill.ZoneRadius + radius) * (Anthill.ZoneRadius + radius))
        {
            foreach (HillGuard guard in world.HillGuards)
            {
                if (!guard.IsDead && !guard.IsHidden)
                    Consider(guard, allyDefense: false);
            }
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

            // Fight to protect: a raider heading for its home (or any of its group's, or its allies').
            if (!sleeping && other.RaidTarget is { } raided &&
                (raided == Home || (GroupId is not null && (raided.GroupId == GroupId || world.AreAllied(raided.GroupId, GroupId)))))
            {
                Consider(other, allyDefense: true);
                continue;
            }

            if (GroupId is null || other.GroupId is null)
                continue;

            // War: a bold resident drives off a member of an enemy group that comes right up to home.
            if (Home is { } home && (Personality.Aggression + Personality.Courage) / 2f >= WarIntruderAggression && world.AreAtWar(GroupId, other.GroupId) &&
                GroundMover.HorizontalDistanceSquared(other.Position, home.Position) <= WarIntruderRadius * WarIntruderRadius)
            {
                Consider(other, allyDefense: true);
                continue;
            }

            if (other.GroupId != GroupId && !world.AreAllied(GroupId, other.GroupId))
                continue;

            // Group Dynamics: a groupmate under attack, or already fighting,
            // pulls its foe into this Bramblekin's sights too; an ally only
            // when it's actually being hit.
            bool groupmate = other.GroupId == GroupId;
            ICombatant? allyFoe = other.RecentAttacker(world) ??
                                  (groupmate && other.State == BramblekinState.Fighting ? other.CombatTarget : null);
            if (allyFoe is { IsDead: false } && !ReferenceEquals(allyFoe, this) &&
                !(allyFoe is Bramblekin foeKin && (foeKin.GroupId == GroupId || world.AreAllied(foeKin.GroupId, GroupId))))
                Consider(allyFoe, allyDefense: true);
        }

        _perceivedThreat = best;
        _threatIsAllyDefense = bestIsAllyDefense;

        // The night watch cries out at anything it sees coming.
        if (best is not null && world.IsNight && world.GroupOf(this) is { } clan && clan.NightWatch == this)
            world.RaiseAlarm(clan, Position);
    }
}
