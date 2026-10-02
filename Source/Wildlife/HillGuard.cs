using System.Numerics;
using Raylib_cs;

namespace GardenGuardians;

/// <summary>
/// One of the ant hill's guards (see World.AntHill): it stands about the hill and falls on any Bramblekin that comes into its no-go zone,
/// chasing it until it is dead or has got clear of the zone, and fighting to the death. When no one has been in the zone for a while,
/// the wounded go back inside and heal, and the dead are replaced, so the next fight meets the hill's full guard.
/// </summary>
public sealed class HillGuard : ICombatant
{
    /// <summary>The model's scale: a little bigger than a thief ant (about 1 m from head to tail).</summary>
    private const float ModelScale = 1.0f;

    public const float BodyRadius = 0.38f;
    public const int MaxHealth = 24;
    public const int BiteDamage = 4;

    /// <summary>A Bramblekin that is not in a Kingdom's assault scratches a guard for no more than this: only an army can bring one down.</summary>
    public const int BystanderDamage = 1;

    private const float Speed = 2.0f;
    private const float BiteInterval = 1.0f;

    /// <summary>It bites from this close to a Bramblekin's edge.</summary>
    private const float BiteReach = 0.4f;

    /// <summary>Wounds close at this many Health a second while it is inside.</summary>
    private const float HealPerSecond = 1f;

    /// <summary>When posted it stands about the hill between these distances (m) from its middle.</summary>
    private const float PostMin = Anthill.Radius + 2f, PostMax = Anthill.Radius + 6f;

    private static readonly Color Tint = new(255, 215, 205, 255);

    private readonly GroundMover _mover;
    private readonly Random _rng;
    private Bramblekin? _target;
    private Vector3 _postTarget;
    private float _biteTimer;
    private float _healCarry;
    private float _alertDelay = -1f;
    private float _walkCycle;
    private Vector3 _lastPosition;

    public HillGuard(Vector3 position, Random rng, bool hidden, bool sentry = false)
    {
        _rng = rng;
        IsSentry = sentry;
        _mover = new GroundMover(position, BodyRadius, edgeMargin: 0.5f, rng);
        _postTarget = position;
        IsHidden = hidden;
        _biteTimer = (float)rng.NextDouble() * BiteInterval;
    }

    public Vector3 Position => _mover.GroundedPosition;
    public bool IsDead { get; private set; }
    public int Health { get; private set; } = MaxHealth;
    public float CollisionRadius => BodyRadius;

    /// <summary>One of the few that roam about the hill when all is quiet (the rest wait inside until it is on alert).</summary>
    public bool IsSentry { get; set; }

    /// <summary>True while it is inside the hill (healing, or kept below in winter): not drawn, and nothing can strike it.</summary>
    public bool IsHidden { get; private set; }

    public void MarkDead() => IsDead = true;

    public void TakeHit(int damage, Bramblekin attacker, World world)
    {
        if (IsHidden)
            return;
        if (attacker.AssaultParty is null)
            damage = Math.Min(damage, BystanderDamage);
        Health = Math.Max(0, Health - damage);
        _target = attacker;
        if (Health <= 0)
            world.KillHillGuard(this, attacker);
    }

    public void Update(float deltaTime, World world, Anthill hill)
    {
        if (IsDead)
            return;
        _mover.Idle();
        _biteTimer -= deltaTime;

        // Anyone in the zone: out and at them.
        if (ChooseTarget(world, hill) is { } prey)
        {
            if (IsHidden)
                Emerge(hill);
            Fight(prey, deltaTime, world);
            return;
        }
        _target = null;

        // Kin near the zone but not in it: the hill is on alert and the guards inside come out (one by one), ready.
        bool alert = world.AntAlert;
        if (!alert)
            _alertDelay = -1f;
        else if (IsHidden && Health >= MaxHealth && world.CurrentSeason != Season.Winter)
        {
            if (_alertDelay < 0f)
                _alertDelay = (float)_rng.NextDouble() * 2.5f;
            _alertDelay -= deltaTime;
            if (_alertDelay <= 0f)
                Emerge(hill);
        }

        // Quiet: the wounded (and everyone, in winter) go below, and so do all but the sentries; the sentries stand about the hill.
        bool quiet = world.AntAlertQuietSeconds >= World.AntZoneQuietNeeded;
        bool belowWanted = quiet && (!IsSentry || Health < MaxHealth || world.CurrentSeason == Season.Winter);
        if (IsHidden)
        {
            if (!belowWanted && !alert && IsSentry && world.CurrentSeason != Season.Winter && Health >= MaxHealth)
                Emerge(hill);
            else if (belowWanted || alert)
            {
                _healCarry += HealPerSecond * deltaTime;
                int whole = (int)_healCarry;
                if (whole > 0)
                {
                    Health = Math.Min(MaxHealth, Health + whole);
                    _healCarry -= whole;
                }
            }
            return;
        }
        if (belowWanted)
        {
            if (_mover.MoveTowards(hill.Mouth, Speed, deltaTime, world, static (w, p) => !w.IsBlocked(p, BodyRadius)) ||
                GroundMover.HorizontalDistanceSquared(Position, hill.Mouth) < 1.5f * 1.5f)
                IsHidden = true;
            return;
        }
        Post(deltaTime, world, hill);
    }

    private void Emerge(Anthill hill)
    {
        IsHidden = false;
        _mover.Position = hill.Mouth;
    }

    /// <summary>The nearest living Bramblekin in the zone (or the one it is already after, until it has got well clear).</summary>
    private Bramblekin? ChooseTarget(World world, Anthill hill)
    {
        float leash = Anthill.ZoneRadius + Anthill.ChaseLeash;
        if (_target is { IsDead: false } held && GroundMover.HorizontalDistanceSquared(held.Position, hill.Position) <= leash * leash && !held.IsSheltered)
            return held;
        Bramblekin? best = null;
        float bestDistanceSquared = float.MaxValue;
        foreach (Bramblekin kin in world.AntZoneKin)
        {
            if (kin.IsDead || kin.IsSheltered)
                continue;
            float distanceSquared = GroundMover.HorizontalDistanceSquared(Position, kin.Position);
            if (distanceSquared < bestDistanceSquared)
            {
                best = kin;
                bestDistanceSquared = distanceSquared;
            }
        }
        _target = best;
        return best;
    }

    private void Fight(Bramblekin prey, float deltaTime, World world)
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
            return;
        }
        _mover.MoveTowards(prey.Position, Speed, deltaTime, world, static (w, p) => !w.IsBlocked(p, BodyRadius));
    }

    private void Post(float deltaTime, World world, Anthill hill)
    {
        if (_mover.MoveTowards(_postTarget, Speed * 0.4f, deltaTime, world, static (w, p) => !w.IsBlocked(p, BodyRadius)) ||
            GroundMover.HorizontalDistanceSquared(_postTarget, hill.Position) < 1f)
        {
            float angle = (float)(_rng.NextDouble() * MathF.Tau);
            _postTarget = hill.Position + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * (PostMin + (float)_rng.NextDouble() * (PostMax - PostMin));
        }
    }

    public void Draw()
    {
        if (IsHidden)
            return;
        Vector3 here = Position;
        if (_mover.IsMoving)
            _walkCycle += GroundMover.HorizontalDistance(_lastPosition, here) / (ModelScale * Ant.StrideLengths);
        _lastPosition = here;
        PropModels.DrawAnt(here, _mover.Heading, ModelScale, Tint, _walkCycle, _mover.IsMoving);
    }
}
