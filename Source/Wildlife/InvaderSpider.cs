using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// A small spider of an invasion (see World.Invasions): a knee-high cousin of the Wolf Spider, in a swarm that sets out for one village or
/// kingdom. It marches to the village's middle, goes for the nearest Bramblekin outside its home that it can see and bites it on a cooldown,
/// and circles the village while nobody is about. It does not pounce and cannot be shaken off by hiding indoors, but it is frail: a few
/// blows from a soldier kill it. When its invasion withdraws, it walks off the way it came.
/// </summary>
public sealed class InvaderSpider : ICombatant
{
    /// <summary>The model's scale: its legs span about 0.7 m (the Wolf Spider's span 2 m).</summary>
    private const float ModelScale = 0.7f / PropModels.SpiderWidth;

    public const float BodyRadius = 0.18f;
    public const int MaxHealth = 12;
    public const int BiteDamage = 4;

    private const float Speed = 1.7f;
    private const float BiteInterval = 1.2f;

    /// <summary>It goes for a Bramblekin it sees within this many meters.</summary>
    private const float SightRadius = 9f;

    /// <summary>It bites from this close to a Bramblekin's edge.</summary>
    private const float BiteReach = 0.35f;

    /// <summary>With no one to bite it roams within this far (m) of its goal.</summary>
    private const float RoamRadius = 6f;

    private static readonly Color Tint = new(255, 205, 195, 255);

    private readonly GroundMover _mover;
    private readonly Random _rng;
    private Vector3 _roamTarget;
    private float _biteTimer;
    private float _walkCycle;

    public InvaderSpider(Vector3 position, Vector3 goal, Guid invasionId, Guid villageId, Random rng)
    {
        _rng = rng;
        _mover = new GroundMover(position, BodyRadius, edgeMargin: 0.5f, rng);
        Goal = goal;
        InvasionId = invasionId;
        VillageId = villageId;
        _roamTarget = goal;
        _biteTimer = (float)rng.NextDouble() * BiteInterval;
    }

    public Vector3 Position => _mover.GroundedPosition;
    public bool IsDead { get; private set; }
    public int Health { get; private set; } = MaxHealth;
    public float CollisionRadius => BodyRadius;

    /// <summary>Where it is headed: the middle of the village it is sent against.</summary>
    public Vector3 Goal { get; }
    public Guid InvasionId { get; }
    public Guid VillageId { get; }

    /// <summary>Set when its invasion gives up: it heads for <see cref="ExitPoint"/> and is gone once it gets there.</summary>
    public bool IsWithdrawing { get; set; }
    public Vector3 ExitPoint { get; set; }

    public void MarkDead() => IsDead = true;

    public void TakeHit(int damage, Bramblekin attacker, World world)
    {
        Health = Math.Max(0, Health - damage);
        if (Health <= 0)
            world.KillInvader(this, attacker);
    }

    public void Update(float deltaTime, World world)
    {
        if (IsDead)
            return;
        _mover.Idle();
        _biteTimer -= deltaTime;

        if (IsWithdrawing)
        {
            if (_mover.MoveTowards(ExitPoint, Speed, deltaTime, world, static (w, p) => !w.IsBlocked(p, BodyRadius)) ||
                GroundMover.HorizontalDistanceSquared(Position, ExitPoint) < 4f)
                world.RemoveInvader(this);
        }
        else if (Nearest(world) is { } prey)
        {
            float reach = BodyRadius + Bramblekin.BodyRadius + BiteReach;
            if (GroundMover.HorizontalDistanceSquared(Position, prey.Position) <= reach * reach)
            {
                var toPrey = new Vector2(prey.Position.X - Position.X, prey.Position.Z - Position.Z);
                if (toPrey.LengthSquared() > 1e-6f)
                    _mover.Heading = Vector2.Normalize(toPrey);
                if (_biteTimer <= 0f)
                {
                    _biteTimer = BiteInterval;
                    prey.TakeDamage(BiteDamage, world, DeathCause.Predator, this);
                }
            }
            else
                _mover.MoveTowards(prey.Position, Speed, deltaTime, world, static (w, p) => !w.IsBlocked(p, BodyRadius));
        }
        else if (GroundMover.HorizontalDistanceSquared(Position, Goal) > RoamRadius * RoamRadius)
            _mover.MoveTowards(Goal, Speed, deltaTime, world, static (w, p) => !w.IsBlocked(p, BodyRadius));
        else if (_mover.MoveTowards(_roamTarget, Speed * 0.5f, deltaTime, world, static (w, p) => !w.IsBlocked(p, BodyRadius)))
        {
            float angle = (float)(_rng.NextDouble() * MathF.Tau);
            _roamTarget = Goal + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * (1f + (float)_rng.NextDouble() * (RoamRadius - 1f));
        }

        if (_mover.IsMoving)
            _walkCycle += deltaTime * 14f;
    }

    /// <summary>The nearest living Bramblekin it can see that is not safe indoors or behind stakes.</summary>
    private Bramblekin? Nearest(World world)
    {
        Bramblekin? best = null;
        float bestDistanceSquared = SightRadius * SightRadius;
        List<Bramblekin> nearby = world.QueryNearbyColony(Position, SightRadius);
        for (int i = nearby.Count - 1; i >= 0; i--)
        {
            Bramblekin kin = nearby[i];
            if (kin.IsDead || kin.IsSheltered || world.IsInsidePalisade(kin.Position))
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

    public void Draw()
    {
        float yawDegrees = -MathF.Atan2(_mover.Heading.Y, _mover.Heading.X) * 180f / MathF.PI;
        Rlgl.PushMatrix();
        Rlgl.Translatef(Position.X, Position.Y, Position.Z);
        Rlgl.Rotatef(yawDegrees, 0, 1, 0);
        PropModels.DrawSpider(Vector3.Zero, 180f, ModelScale, Tint, _walkCycle / MathF.Tau, _mover.IsMoving);
        Rlgl.PopMatrix();
    }
}
