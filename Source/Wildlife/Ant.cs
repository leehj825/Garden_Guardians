using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// A thief ant from the rival colony (see <see cref="Anthill"/> and World.AntHill):
/// it goes for the nearest store with food in reach of its hill — any but
/// a palisaded one — takes a piece and carries it home, or picks up loose
/// food lying near the hill. Easy to swat, and Bramblekin defend their homes
/// against them; it bites back at whoever hits it.
/// </summary>
public sealed class Ant : ICombatant
{
    /// <summary>The model's scale: about 0.8 m from head to tail (a Bramblekin is 1 m tall).</summary>
    public const float ModelScale = 0.8f;

    public const float BodyRadius = 0.28f;
    public const float EdgeMargin = 0.5f;
    public const int MaxHealth = 5;

    private const float Speed = 1.5f;

    /// <summary>Odds an ant setting out goes for an aphid pen before any store.</summary>
    private const double PenFirstChance = 0.4;

    /// <summary>It bites back at whoever hit it, this hard…</summary>
    public const int BiteDamage = 2;

    /// <summary>…this often, while it's within reach.</summary>
    private const float BiteInterval = 1.2f;

    private const float BiteRange = 0.6f;

    /// <summary>It stays angry at its attacker this long.</summary>
    private const float AngerSeconds = 5f;

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
            if (_mover.MoveTowards(hill.Mouth, Speed, deltaTime, world, static (w, p) => !w.IsBlocked(p, BodyRadius)) ||
                GroundMover.HorizontalDistance(Position, hill.Mouth) <= 1.2f)
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
            _wanderTarget = hill.Position + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * (Anthill.Radius + 1.5f + (float)_rng.NextDouble() * 6f);
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

    /// <summary>The ant model, facing the way it walks, with a berry (or an aphid) on its back when laden.</summary>
    public void Draw()
    {
        Vector2 heading = _mover.Heading;
        PropModels.DrawAnt(Position, MathF.Atan2(heading.X, heading.Y) * 180f / MathF.PI, ModelScale, Color.White);
        if (IsLaden)
            Detail.Sphere(Position + new Vector3(0f, 0.5f, 0f), 0.14f, CarriesAphid ? AphidLoadColor : LoadColor);
    }
}
