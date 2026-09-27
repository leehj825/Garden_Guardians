using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// The Hornet Swarm: a small, fast, genuinely (if mildly) hostile
/// predator — a weaker, faster, group version of the Wolf Spider's own
/// concept. Spawns in
/// clusters of <see cref="World.HornetSwarmMinSize"/>-<see cref="World.HornetSwarmMaxSize"/>
/// (see <see cref="World.UpdateHornetSpawn"/>) around a shared anchor point
/// and wanders erratically near it — each Hornet's own aggro check runs
/// independently every frame rather than through any shared swarm-wide
/// coordination, but because they spawn clustered together this still
/// reads as "the whole swarm reacts together" the instant any one
/// Bramblekin strays within <see cref="AggroRadius"/> of any one of them.
///
/// Damage Model: Bramblekin genuinely has hit points (<see cref="Bramblekin.Health"/>/
/// <see cref="Bramblekin.TakeDamage"/>) — the Wolf Spider's own Pounce
/// simply chooses to call <see cref="World.Kill"/> outright rather than
/// damage through it. A Hornet instead deals a small
/// <see cref="BiteDamage"/> per bite, on its own cooldown, so a Bramblekin
/// it catches takes several bites to actually die rather than being
/// one-shot the way the Spider's Pounce is — "low attack damage" reads
/// literally, through the same Health/TakeDamage plumbing every other
/// damage source in this file already uses, rather than a percentage
/// chance or some other approximation.
///
/// A Hornet counts as a threat to any Bramblekin that can see it (see
/// Bramblekin.Perceive) — a sharp-eyed one gives a swarm a wide berth, a
/// dull or hungry one may blunder right into it.
/// </summary>
public sealed class Hornet : ICombatant
{
    /// <summary>Collision/body radius in meters.</summary>
    public const float BodyRadius = 0.1f;

    /// <summary>How far from the terrain edge it wanders, in meters.</summary>
    public const float EdgeMargin = 0.3f;

    /// <summary>How far (m) from its cluster's own spawn anchor a Hornet may land at spawn time, or wander to while idle.</summary>
    public const float ClusterJitterRadius = 1.2f;

    private const float WanderSpeed = 0.4f;

    /// <summary>Faster than a walking Bramblekin's own <see cref="Bramblekin.WalkSpeed"/> (1.5), slower than a fleeing one's (4.5) — genuinely hard to simply outrun, but not an inescapable predator either.</summary>
    private const float ChaseSpeed = 2.6f;

    private const float WanderPauseDuration = 1.2f;

    /// <summary>How close (m) a Bramblekin has to wander to any one Hornet in a cluster to aggro the whole thing (see this class's own doc comment).</summary>
    public const float AggroRadius = 3f;

    /// <summary>Gives up the chase once its target has out-run this far (m) past <see cref="AggroRadius"/> — otherwise a single fast Bramblekin could drag a Hornet clean across the map.</summary>
    private const float ChaseLeashRadius = AggroRadius * 3f;

    private const float BiteRange = 0.35f;

    /// <summary>Low Attack Damage: a small fraction of a Bramblekin's own <see cref="Bramblekin.MaxHealth"/> (30) per bite — several bites to actually kill, not the Wolf Spider's one-touch Pounce.</summary>
    private const int BiteDamage = 3;

    private const float BiteCooldownDuration = 1f;

    /// <summary>Hit points out of this — low, so a single Bramblekin strike swats one out of the air.</summary>
    public const int MaxHealth = 4;

    private static readonly Color StripeColorYellow = new(230, 190, 20, 255);
    private static readonly Color StripeColorBlack = new(30, 25, 20, 255);
    private static readonly Color WingColor = new(230, 230, 235, 90);

    private readonly Random _rng;
    private readonly GroundMover _mover;

    /// <summary>This Hornet's cluster's shared spawn anchor — see <see cref="World.UpdateHornetSpawn"/>. Wandering (while not chasing) stays within <see cref="ClusterJitterRadius"/> of this point.</summary>
    private readonly Vector3 _anchor;

    private Vector3 _target;
    private float _pauseTimer;
    private float _biteCooldown;
    private Bramblekin? _chaseTarget;

    /// <summary>Terrain-aware, same treatment as Bramblekin — Y is snapped to World.GetHeightAt every read.</summary>
    public Vector3 Position => World.Grounded(_mover.Position);

    /// <summary>True once swatted by a Bramblekin. Removal from World.Hornets is deferred to the end of the frame.</summary>
    public bool IsDead { get; private set; }

    /// <summary>Hit points out of <see cref="MaxHealth"/>.</summary>
    public int Health { get; private set; } = MaxHealth;

    public float CollisionRadius => BodyRadius;

    /// <summary>True while it's chasing a Bramblekin — an idle Hornet at its nest is something to steer around, not to fight.</summary>
    public bool IsChasing => _chaseTarget is { IsDead: false };

    public Hornet(Vector3 position, Vector3 anchor, Random rng)
    {
        _rng = rng;
        _anchor = anchor;
        _mover = new GroundMover(position, BodyRadius, EdgeMargin, rng);
        _target = position;
        _pauseTimer = (float)rng.NextDouble() * WanderPauseDuration;
    }

    /// <summary>Marks it caught. Called once, from World.KillHornet.</summary>
    public void MarkDead() => IsDead = true;

    /// <summary>A Bramblekin's strike: at 0 Health it's swatted out of the air (see <see cref="World.KillHornet"/>).</summary>
    public void TakeHit(int damage, Bramblekin attacker, World world)
    {
        Health = Math.Max(0, Health - damage);
        if (Health <= 0)
            world.KillHornet(this);
    }

    public void Update(float deltaTime, World world)
    {
        if (IsDead)
            return;

        _mover.Idle();

        // Safety net: the Bramblekin we're chasing may have died, or
        // simply out-run the leash, since last frame.
        if (_chaseTarget is { } stale && (stale.IsDead || stale.IsSheltered ||
            GroundMover.HorizontalDistanceSquared(Position, stale.Position) > ChaseLeashRadius * ChaseLeashRadius))
        {
            _chaseTarget = null;
        }

        // The Hornet Swarm's aggro: an independent per-Hornet check every
        // frame — see this class's own doc comment for why this alone is
        // enough to make a whole cluster read as reacting together.
        if (_chaseTarget is null)
        {
            Bramblekin? threat = NearestBramblekinWithin(world, AggroRadius);
            if (threat is not null)
                _chaseTarget = threat;
        }

        if (_chaseTarget is { } target)
        {
            if (GroundMover.HorizontalDistance(Position, target.Position) <= BiteRange)
            {
                _biteCooldown -= deltaTime;
                if (_biteCooldown <= 0f)
                {
                    target.TakeDamage(BiteDamage, world, DeathCause.Predator, this);
                    _biteCooldown = BiteCooldownDuration;
                }
                return;
            }

            _mover.MoveTowards(target.Position, ChaseSpeed, deltaTime, world, static (w, p) => w.Terrain.Contains(p, EdgeMargin));
            return;
        }

        // Idle: a tight random wander that never strays far from this
        // cluster's own anchor point.
        if (_pauseTimer > 0f)
        {
            _pauseTimer -= deltaTime;
            if (_pauseTimer <= 0f)
            {
                float angle = (float)(_rng.NextDouble() * MathF.Tau);
                float radius = (float)_rng.NextDouble() * ClusterJitterRadius;
                _target = _anchor + new Vector3(MathF.Cos(angle) * radius, 0, MathF.Sin(angle) * radius);
            }
            return;
        }

        if (_mover.MoveTowards(_target, WanderSpeed, deltaTime, world, static (w, p) => w.Terrain.Contains(p, EdgeMargin)))
            _pauseTimer = WanderPauseDuration;
    }

    private Bramblekin? NearestBramblekinWithin(World world, float radius)
    {
        Bramblekin? nearest = null;
        float bestDistanceSquared = radius * radius;
        // The Spatial Grid: only the Colony chunks around this Hornet.
        List<Bramblekin> nearby = world.QueryNearbyColony(Position, radius);
        for (int i = nearby.Count - 1; i >= 0; i--)
        {
            Bramblekin bramblekin = nearby[i];
            if (bramblekin.IsDead || bramblekin.IsSheltered)
                continue;

            float distanceSquared = GroundMover.HorizontalDistanceSquared(Position, bramblekin.Position);
            if (distanceSquared <= bestDistanceSquared)
            {
                nearest = bramblekin;
                bestDistanceSquared = distanceSquared;
            }
        }
        return nearest;
    }

    /// <summary>A tiny yellow/black striped body with a pair of thin wing lines — cheap enough to draw many at once.</summary>
    public void Draw()
    {
        var bottom = Position + new Vector3(0, BodyRadius * 0.6f, 0);
        var top = Position + new Vector3(0, BodyRadius * 1.6f, 0);
        Raylib.DrawCapsule(bottom, top, BodyRadius, 5, 3, StripeColorYellow);
        Raylib.DrawCapsuleWires(bottom, top, BodyRadius, 5, 3, StripeColorBlack);

        // A single dark stripe band around the middle of the body reads as
        // its namesake stripe without a second, more expensive shape.
        Vector3 mid = Position + new Vector3(0, BodyRadius * 1.1f, 0);
        Raylib.DrawCircle3D(mid, BodyRadius * 1.02f, new Vector3(1, 0, 0), 90f, StripeColorBlack);

        // A pair of thin, near-transparent wing lines flicking out to the sides.
        Vector3 wingBase = mid;
        Raylib.DrawLine3D(wingBase, wingBase + new Vector3(BodyRadius * 2f, BodyRadius * 0.5f, 0), WingColor);
        Raylib.DrawLine3D(wingBase, wingBase + new Vector3(-BodyRadius * 2f, BodyRadius * 0.5f, 0), WingColor);
    }
}
