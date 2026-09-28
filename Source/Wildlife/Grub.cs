using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// A food competitor and easy prey: burrows in from the map's edge (see
/// <see cref="World.UpdateGrubSpawn"/>), sniffs out the nearest loose Food
/// within <see cref="SmellRadius"/> and eats it — claimed or not, Grubs
/// don't respect anyone's dibs — growing fatter with every bite. Skitters
/// away from any Bramblekin that gets close, but it's slower than one
/// walking, so a hungry Bramblekin that can't see any Food will run it
/// down (see Bramblekin.UpdateHunger); killed, it drops a little Food
/// plus some of whatever it ate (see <see cref="World.KillGrub"/>).
/// </summary>
public sealed class Grub : ICombatant
{
    /// <summary>Collision/body radius in meters.</summary>
    public const float BodyRadius = 0.18f;

    /// <summary>How far from the terrain edge it may wander/spawn, in meters.</summary>
    public const float EdgeMargin = 0.3f;

    /// <summary>How far (m) away it can smell loose Food.</summary>
    public const float SmellRadius = 12f;

    /// <summary>At most this much Food drops when it dies, however much it ate.</summary>
    public const int MaxCarcassFood = 4;

    public const int MaxHealth = 12;

    private const float CrawlSpeed = 0.9f;
    private const float SkitterSpeed = 1.1f;

    /// <summary>It skitters away from any Bramblekin closer than this (m).</summary>
    private const float SkittishRadius = 2.5f;

    /// <summary>How close (m) it must get to Food to eat it.</summary>
    private const float EatDistance = 0.4f;

    private const float WanderPauseDuration = 2f;

    private static readonly Color BodyColor = new(120, 95, 60, 255);
    private static readonly Color SnoutColor = new(90, 65, 40, 255);

    private readonly Random _rng;
    private readonly GroundMover _mover;
    private FoodShard? _targetFood;
    private Vector3 _wanderTarget;
    private float _pauseTimer;

    public Grub(Vector3 position, Random rng)
    {
        _rng = rng;
        _mover = new GroundMover(position, BodyRadius, EdgeMargin, rng);
        _wanderTarget = position;
    }

    /// <summary>Terrain-aware, same treatment as Hornet/Bramblekin — Y is snapped to World.GetHeightAt every read.</summary>
    public Vector3 Position => _mover.GroundedPosition;

    /// <summary>True once killed by a Bramblekin. Removal from World.Grubs is deferred to the end of the frame.</summary>
    public bool IsDead { get; private set; }

    public int Health { get; private set; } = MaxHealth;

    /// <summary>Pieces of Food eaten so far — it grows with each one, and drops some of it back on death.</summary>
    public int FoodEaten { get; private set; }

    public float CollisionRadius => BodyRadius * Girth;

    /// <summary>Visual and collision scale: fattens with every piece of Food eaten.</summary>
    private float Girth => 1f + 0.12f * Math.Min(FoodEaten, 5);

    /// <summary>Marks it dead. Called once, from World.KillGrub.</summary>
    public void MarkDead() => IsDead = true;

    /// <summary>A Bramblekin's strike: at 0 Health it dies (see <see cref="World.KillGrub"/>).</summary>
    public void TakeHit(int damage, Bramblekin attacker, World world)
    {
        Health = Math.Max(0, Health - damage);
        if (Health <= 0)
            world.KillGrub(this, attacker);
    }

    public void Update(float deltaTime, World world)
    {
        if (IsDead)
            return;

        _mover.Idle();

        // Skittish: a Bramblekin too close sends it scurrying straight away.
        if (world.NearestLivingKinWithin(Position, SkittishRadius) is { } kin)
        {
            var away = new Vector2(Position.X - kin.Position.X, Position.Z - kin.Position.Z);
            away = away.LengthSquared() > 1e-4f ? Vector2.Normalize(away) : Vector2.UnitX;
            Vector3 fleeTarget = Position + new Vector3(away.X, 0f, away.Y) * 2f;
            if (!world.Terrain.Contains(fleeTarget, EdgeMargin))
                fleeTarget = Position + new Vector3(-away.Y, 0f, away.X) * 2f;
            _mover.MoveTowards(fleeTarget, SkitterSpeed, deltaTime, world, static (w, p) => w.Terrain.Contains(p, EdgeMargin));
            return;
        }

        if (_targetFood is null || !world.IsAvailable(_targetFood, claimant: null))
            _targetFood = world.NearestAvailableFood(Position, SmellRadius, claimant: null);

        if (_targetFood is { } food)
        {
            if (GroundMover.HorizontalDistance(Position, food.Position) <= EatDistance)
            {
                if (world.GrubEat(food))
                    FoodEaten++;
                _targetFood = null;
                return;
            }

            _mover.MoveTowards(food.Position, CrawlSpeed, deltaTime, world, static (w, p) => !w.IsBlocked(p, BodyRadius));
            return;
        }

        // The bait in a set snare smells like food too — and that's the end of it.
        if (world.Snares.Count > 0 && world.NearestSetSnare(Position, SmellRadius) is { } snare)
        {
            if (GroundMover.HorizontalDistance(Position, snare.Position) <= Snare.CatchRadius)
            {
                world.SpringSnare(snare, this);
                return;
            }
            _mover.MoveTowards(snare.Position, CrawlSpeed, deltaTime, world, static (w, p) => !w.IsBlocked(p, BodyRadius));
            return;
        }

        // Nothing to smell: a slow random wander.
        if (_pauseTimer > 0f)
        {
            _pauseTimer -= deltaTime;
            if (_pauseTimer <= 0f)
                _wanderTarget = world.RandomFreePoint(BodyRadius, EdgeMargin + 1f);
            return;
        }

        if (_mover.MoveTowards(_wanderTarget, CrawlSpeed * 0.6f, deltaTime, world, static (w, p) => !w.IsBlocked(p, BodyRadius)))
            _pauseTimer = WanderPauseDuration * (0.5f + (float)_rng.NextDouble());
    }

    /// <summary>A small brown/tan mole-like silhouette: a low capsule body with a darker snout, fattening as it eats.</summary>
    public void Draw()
    {
        float radius = BodyRadius * Girth;
        var bottom = Position + new Vector3(0, radius * 0.5f, 0);
        var top = Position + new Vector3(0, radius * 1.3f, 0);
        Raylib.DrawCapsule(bottom, top, radius, 6, 3, BodyColor);
        Raylib.DrawCapsuleWires(bottom, top, radius, 6, 3, new Color(40, 30, 20, 255));

        Vector2 heading = _mover.Heading.LengthSquared() > 1e-6f ? _mover.Heading : Vector2.UnitX;
        Vector3 snout = Position + new Vector3(heading.X, radius * 0.6f, heading.Y) * radius;
        Raylib.DrawSphere(snout, radius * 0.4f, SnoutColor);
    }
}
