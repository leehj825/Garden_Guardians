using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// An ant from the rival colony (see <see cref="Anthill"/> and World.Ants):
/// it goes for the nearest store with food in reach of its hill — any but
/// a palisaded one — takes a piece and carries it home, or picks up loose
/// food lying near the hill. Easy to swat, and Bramblekin defend their homes
/// against them; it bites back at whoever hits it.
/// </summary>
public sealed class Ant : ICombatant
{
    public const float BodyRadius = 0.12f;
    public const float EdgeMargin = 0.3f;
    public const int MaxHealth = 5;

    private const float Speed = 1.5f;

    /// <summary>Odds an ant setting out goes for an aphid pen before any store.</summary>
    private const double PenFirstChance = 0.4;

    /// <summary>It bites back at whoever hit it, this hard…</summary>
    public const int BiteDamage = 2;

    /// <summary>…this often, while it's within reach.</summary>
    private const float BiteInterval = 1.2f;

    private const float BiteRange = 0.45f;

    /// <summary>It stays angry at its attacker this long.</summary>
    private const float AngerSeconds = 5f;

    private static readonly Color BodyColor = new(90, 35, 25, 255);
    private static readonly Color LoadColor = new(210, 40, 45, 255);
    private static readonly Color AphidLoadColor = new(150, 210, 90, 255);

    private readonly GroundMover _mover;
    private readonly Random _rng;
    private Shelter? _targetStore;
    private AphidPen? _targetPen;
    private FoodShard? _targetFood;
    private Vector3 _wanderTarget;
    private Bramblekin? _angryAt;
    private float _angerTimer;
    private float _biteTimer;

    public Ant(Vector3 position, Random rng)
    {
        _rng = rng;
        _mover = new GroundMover(position, BodyRadius, EdgeMargin, rng);
        _wanderTarget = position;
    }

    public Vector3 Position => _mover.GroundedPosition;
    public bool IsDead { get; private set; }
    public int Health { get; private set; } = MaxHealth;
    public float CollisionRadius => BodyRadius;

    /// <summary>True while it's carrying a piece of food home.</summary>
    public bool IsLaden { get; private set; }

    /// <summary>True while what it carries home is an aphid from a clan's pen, not food.</summary>
    public bool CarriesAphid { get; private set; }

    public void MarkDead() => IsDead = true;

    public void TakeHit(int damage, Bramblekin attacker, World world)
    {
        Health = Math.Max(0, Health - damage);
        _angryAt = attacker;
        _angerTimer = AngerSeconds;
        if (Health <= 0)
            world.KillAnt(this);
    }

    public void Update(float deltaTime, World world, Anthill hill)
    {
        if (IsDead)
            return;
        _mover.Idle();

        // Bitten: it turns on whoever hit it, for a while.
        if (_angryAt is { IsDead: false } foe && _angerTimer > 0f)
        {
            _angerTimer -= deltaTime;
            _biteTimer -= deltaTime;
            if (GroundMover.HorizontalDistance(Position, foe.Position) <= BiteRange + Bramblekin.BodyRadius)
            {
                if (_biteTimer <= 0f)
                {
                    _biteTimer = BiteInterval;
                    foe.TakeDamage(BiteDamage, world, DeathCause.Predator, this);
                }
                return;
            }
            _mover.MoveTowards(foe.Position, Speed, deltaTime, world, static (w, p) => !w.IsBlocked(p, BodyRadius));
            return;
        }

        if (IsLaden)
        {
            if (_mover.MoveTowards(hill.Position, Speed, deltaTime, world, static (w, p) => !w.IsBlocked(p, BodyRadius)) ||
                GroundMover.HorizontalDistance(Position, hill.Position) <= Anthill.Radius)
            {
                if (!CarriesAphid)
                    hill.Stock++;
                IsLaden = false;
                CarriesAphid = false;
            }
            return;
        }

        // An aphid pen, to carry one off for the hill to milk: some ants go for one first, the rest only with no store to rob.
        if (_targetPen is null && _targetStore is null && world.Pens.Count > 0 && _rng.NextDouble() < PenFirstChance)
            _targetPen = world.PenForAnts(Position, hill);
        if (_targetPen is { Aphids: > 0 } firstPen && TryPen(firstPen, deltaTime, world))
            return;

        // A store to rob, else a pen, else loose food, else a wander near the hill.
        if (_targetStore is not { IsCollapsed: false, IsBuilt: true, HasPalisade: false, HasFooting: false, StoredFood: > 0 })
            _targetStore = world.StoreForAnts(Position, hill);
        if (_targetStore is { } store)
        {
            if (store.Contains(Position))
            {
                if (world.AntSteal(this, store))
                    IsLaden = true;
                _targetStore = null;
                return;
            }
            _mover.MoveTowards(store.Position, Speed, deltaTime, world, static (w, p) => !w.IsBlocked(p, BodyRadius));
            return;
        }

        if (_targetPen is not { Aphids: > 0 })
            _targetPen = world.Pens.Count > 0 ? world.PenForAnts(Position, hill) : null;
        if (_targetPen is { } pen && TryPen(pen, deltaTime, world))
            return;

        if (_targetFood is null || !world.IsAvailable(_targetFood, claimant: null))
        {
            _targetFood = world.NearestAvailableFood(Position, Anthill.GleanRadius, claimant: null);
            if (_targetFood is { } found && GroundMover.HorizontalDistance(found.Position, hill.Position) > Anthill.GleanRadius)
                _targetFood = null;
        }
        if (_targetFood is { } food)
        {
            if (GroundMover.HorizontalDistance(Position, food.Position) <= 0.35f)
            {
                if (world.GrubEat(food)) // Taken off the ground, like a Grub's meal — but carried home.
                    IsLaden = true;
                _targetFood = null;
                return;
            }
            _mover.MoveTowards(food.Position, Speed, deltaTime, world, static (w, p) => !w.IsBlocked(p, BodyRadius));
            return;
        }

        if (_mover.MoveTowards(_wanderTarget, Speed * 0.5f, deltaTime, world, static (w, p) => !w.IsBlocked(p, BodyRadius)))
        {
            float angle = (float)(_rng.NextDouble() * MathF.Tau);
            _wanderTarget = hill.Position + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * (2f + (float)_rng.NextDouble() * 6f);
        }
    }

    /// <summary>Heads for <paramref name="pen"/> and, once in it, carries off an aphid. False if the pen's empty.</summary>
    private bool TryPen(AphidPen pen, float deltaTime, World world)
    {
        if (pen.Aphids <= 0)
        {
            _targetPen = null;
            return false;
        }
        if (pen.Contains(Position))
        {
            if (world.AntTakesAphid(this, pen))
                IsLaden = CarriesAphid = true;
            _targetPen = null;
            return true;
        }
        _mover.MoveTowards(pen.Position, Speed, deltaTime, world, static (w, p) => !w.IsBlocked(p, BodyRadius));
        return true;
    }

    /// <summary>Three dark red-brown beads — head, thorax, abdomen — with a berry on its back when laden.</summary>
    public void Draw()
    {
        Vector2 heading = _mover.Heading;
        var forward = new Vector3(heading.X, 0f, heading.Y) * 0.11f;
        Vector3 center = Position + new Vector3(0f, 0.07f, 0f);
        Detail.Sphere(center + forward, 0.05f, BodyColor);
        Detail.Sphere(center, 0.04f, BodyColor);
        Detail.Sphere(center - forward * 1.1f, 0.065f, BodyColor);
        if (IsLaden)
            Detail.Sphere(center + new Vector3(0f, 0.09f, 0f), 0.06f, CarriesAphid ? AphidLoadColor : LoadColor);
    }
}

/// <summary>
/// The rival ant colony's mound, dug in near the garden's edge from its
/// second year: it sends ants out after the Bramblekin's stores from
/// spring to autumn — more of them the more food it has taken in.
/// </summary>
public sealed class Anthill
{
    public const float Radius = 0.8f;

    /// <summary>Its ants rob stores within this many meters of it…</summary>
    public const float ForageRadius = 55f;

    /// <summary>…and pick up loose food only this close to home.</summary>
    public const float GleanRadius = 18f;

    private static readonly Color MoundColor = new(150, 110, 70, 255);
    private static readonly Color HoleColor = new(40, 28, 20, 255);

    public Anthill(Vector3 position) => Position = World.Grounded(position);

    public Vector3 Position { get; }

    /// <summary>Food its ants have brought home.</summary>
    public int Stock { get; set; }

    /// <summary>How many ants it keeps out at once: 2, plus one per 15 food taken, up to 6.</summary>
    public int MaxAnts => Math.Min(6, 2 + Stock / 15);

    public void Draw()
    {
        float size = Radius * (1f + Math.Min(Stock, 60) / 120f);
        Raylib.DrawCylinder(Position, 0f, size, size * 0.8f, 10, MoundColor);
        Detail.Sphere(Position + new Vector3(0f, size * 0.78f, 0f), 0.09f, HoleColor);
    }
}
