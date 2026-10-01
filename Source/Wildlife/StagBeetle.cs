using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// Big prey: a slow, heavily armoured beetle that minds its own business
/// until something attacks it — then it turns on its attackers and bites.
/// With <see cref="MaxHealth"/> health and a bite that hurts, a lone
/// Bramblekin usually has to break off before it wins; a pack brings it
/// down, and it feeds them well (<see cref="MeatYield"/> pieces of meat —
/// see <see cref="World.KillBeetle"/>). It's the clearest reason for
/// Bramblekin to hunt together.
/// </summary>
public sealed class StagBeetle : ICombatant
{
    public const float BodyRadius = 0.45f;

    public const float EdgeMargin = 1f;

    public const int MaxHealth = 60;

    /// <summary>Meat scattered where it falls.</summary>
    public const int MeatYield = 8;

    private const float WanderSpeed = 0.5f;
    private const float ChargeSpeed = 1.2f;
    private const float WanderPauseDuration = 3f;

    /// <summary>It fights back against any attacker within this many meters, charging the nearest.</summary>
    private const float RetaliationRadius = 3f;

    private const float BiteRange = BodyRadius + Bramblekin.BodyRadius + 0.35f;
    public const int BiteDamage = 7;
    private const float BiteCooldownDuration = 1.5f;

    private static readonly Color ShellColor = new(70, 45, 30, 255);
    private static readonly Color ShellHighlight = new(110, 75, 45, 255);
    private static readonly Color MandibleColor = new(40, 25, 15, 255);

    private readonly Random _rng;
    private readonly GroundMover _mover;
    private Vector3 _wanderTarget;
    private float _pauseTimer;
    private float _biteCooldown;

    public StagBeetle(Vector3 position, Random rng)
    {
        _rng = rng;
        _mover = new GroundMover(position, BodyRadius, EdgeMargin, rng);
        _wanderTarget = position;
        _pauseTimer = (float)rng.NextDouble() * WanderPauseDuration;
    }

    /// <summary>Terrain-aware — Y is snapped to World.GetHeightAt every read.</summary>
    public Vector3 Position => _mover.GroundedPosition;

    public bool IsDead { get; private set; }

    public int Health { get; private set; } = MaxHealth;

    public float CollisionRadius => BodyRadius;

    /// <summary>Marks it dead. Called once, from World.KillBeetle.</summary>
    public void MarkDead() => IsDead = true;

    /// <summary>A Bramblekin's strike: at 0 Health it dies, crediting <paramref name="attacker"/> with the kill.</summary>
    public void TakeHit(int damage, Bramblekin attacker, World world)
    {
        Health = Math.Max(0, Health - damage);
        if (Health <= 0)
            world.KillBeetle(this, attacker);
    }

    public void Update(float deltaTime, World world)
    {
        if (IsDead)
            return;

        _mover.Idle();
        _biteCooldown = MathF.Max(0f, _biteCooldown - deltaTime);

        // Fights back against whoever is attacking it.
        if (NearestAttacker(world) is { } attacker)
        {
            float distance = GroundMover.HorizontalDistance(Position, attacker.Position);
            var toAttacker = new Vector2(attacker.Position.X - Position.X, attacker.Position.Z - Position.Z);
            if (toAttacker.LengthSquared() > 1e-6f)
                _mover.Heading = Vector2.Normalize(toAttacker);

            if (distance <= BiteRange)
            {
                if (_biteCooldown <= 0f)
                {
                    attacker.TakeDamage(BiteDamage, world, DeathCause.Predator, this);
                    _biteCooldown = BiteCooldownDuration;
                }
                return;
            }

            _mover.MoveTowards(attacker.Position, ChargeSpeed, deltaTime, world, static (w, p) => !w.IsBlocked(p, BodyRadius));
            return;
        }

        // Otherwise, a slow graze across the lawn.
        if (_pauseTimer > 0f)
        {
            _pauseTimer -= deltaTime;
            if (_pauseTimer <= 0f)
            {
                float angle = (float)(_rng.NextDouble() * MathF.Tau);
                float radius = 3f + (float)_rng.NextDouble() * 8f;
                Vector3 candidate = Position + new Vector3(MathF.Cos(angle) * radius, 0f, MathF.Sin(angle) * radius);
                _wanderTarget = world.Terrain.Contains(candidate, EdgeMargin + 1f) ? candidate : Vector3.Zero;
            }
            return;
        }

        if (_mover.MoveTowards(_wanderTarget, WanderSpeed, deltaTime, world, static (w, p) => !w.IsBlocked(p, BodyRadius)))
            _pauseTimer = WanderPauseDuration * (0.5f + (float)_rng.NextDouble());
    }

    /// <summary>The nearest living Bramblekin within <see cref="RetaliationRadius"/> that is attacking it.</summary>
    private Bramblekin? NearestAttacker(World world)
    {
        Bramblekin? best = null;
        float bestDistanceSquared = RetaliationRadius * RetaliationRadius;
        List<Bramblekin> nearby = world.QueryNearbyColony(Position, RetaliationRadius);
        for (int i = 0; i < nearby.Count; i++)
        {
            Bramblekin kin = nearby[i];
            if (kin.IsDead || !ReferenceEquals(kin.CombatTarget, this))
                continue;

            float distanceSquared = GroundMover.HorizontalDistanceSquared(Position, kin.Position);
            if (distanceSquared <= bestDistanceSquared)
            {
                best = kin;
                bestDistanceSquared = distanceSquared;
            }
        }
        return best;
    }

    /// <summary>Metres it covers in one loop of its walk (its legs keep time with the ground it covers).</summary>
    private const float StrideMetres = 0.9f;

    /// <summary>How big the model is drawn: 1 unit long as made, this many metres long here.</summary>
    private const float ModelLength = 1.6f;

    private float _walkCycle;
    private Vector3 _lastPosition;

    /// <summary>The beetle (the model, rigged to walk on six legs by Tools/convert_beetle.py), facing the way it heads.</summary>
    public void Draw()
    {
        float yawDegrees = -MathF.Atan2(_mover.Heading.Y, _mover.Heading.X) * 180f / MathF.PI;
        Vector3 here = Position;
        if (_mover.IsMoving)
            _walkCycle += GroundMover.HorizontalDistance(_lastPosition, here) / StrideMetres;
        _lastPosition = here;
        PropModels.DrawWalker(PropModels.Prop.Beetle, here, yawDegrees, ModelLength, Color.White, _walkCycle, _mover.IsMoving);
    }
}
